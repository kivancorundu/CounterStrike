using System;
using System.Numerics;

namespace Vexa.Core
{
    [Flags]
    public enum ShotFlags : byte { None = 0, Melee = 1, Heavy = 2, Taser = 4, Silenced = 8 }

    /// <summary>
    /// A shot fired by a player. Pellet directions are derived from <see cref="Seed"/> so the
    /// shooter's client can draw predicted impacts that match what the server computes.
    /// </summary>
    public struct ShotInfo
    {
        public int ShooterId;
        public WeaponId Weapon;
        public Vector3 Origin;
        public float Yaw, Pitch;       // aim including recoil
        public float Inaccuracy;       // mrad
        public float Spread;           // mrad
        public uint Seed;
        public int Pellets;
        public ShotFlags Flags;
        public int CmdTick;
        public float InterpTick;

        public Vector3 PelletDirection(int i)
        {
            var rng = new DetRandom(DetRandom.Hash((int)Seed, i, 0x5EED));
            float a1 = rng.NextFloat() * MathF.PI * 2f, r1 = rng.NextFloat();
            float a2 = rng.NextFloat() * MathF.PI * 2f, r2 = rng.NextFloat();
            float ox = (MathF.Cos(a1) * r1 * Inaccuracy + MathF.Cos(a2) * r2 * Spread) / 1000f * VMath.Rad2Deg;
            float oy = (MathF.Sin(a1) * r1 * Inaccuracy + MathF.Sin(a2) * r2 * Spread) / 1000f * VMath.Rad2Deg;
            return VMath.Forward(Yaw + ox, Pitch + oy);
        }
    }

    public interface IShotSink
    {
        void OnShot(in ShotInfo shot);
    }

    public struct GrenadeThrow
    {
        public int OwnerId;
        public GrenadeType Type;
        public Vector3 Origin;
        public Vector3 Velocity;
        public int CmdTick;
    }

    /// <summary>Gameplay events produced by the shared simulation (besides shots).</summary>
    public interface ISimEvents
    {
        void OnThrow(in GrenadeThrow t);
        void OnPlanted(int playerId, Vector3 position, string site);
    }

    /// <summary>Weapon state machine: switching, deploy, reload, fire rate, burst, zoom, silencer, recoil and inaccuracy.</summary>
    public static class WeaponLogic
    {
        public const float RecoilScale = 2f;       // bullets go to punch * 2
        public const float ViewRecoilTracking = 0.45f; // camera shows punch * 0.9

        public static void AimPunch(in PlayerState s, out float pitch, out float yaw)
        {
            var def = s.ActiveDef;
            if (!def.IsGun) { pitch = yaw = 0; return; }
            RecoilPatterns.Sample(def, s.RecoilIndex, out pitch, out yaw);
        }

        public static float CurrentInaccuracy(in PlayerState s, WeaponDef def, float time)
        {
            var ia = def.Inacc;
            bool scoped = s.Zoom > 0 && ia.HasScoped;
            float v = scoped ? ia.Scoped : (s.Ducked && s.OnGround ? ia.Crouch : ia.Stand);
            float maxSpd = def.MoveSpeed;
            float hs = VMath.HorizontalLength(s.Velocity);
            float f = VMath.Clamp01((hs - maxSpd * 0.34f) / (maxSpd * 0.66f));
            v += ia.Move * f;
            if (!s.OnGround)
                v += ia.HasJumpApex && MathF.Abs(s.Velocity.Y) < 25f * VMath.HU ? ia.JumpApex : ia.Jump;
            float since = time - s.LastLandTime;
            if (since >= 0 && since < 0.35f) v += ia.Land * (1f - since / 0.35f);
            return v + s.AccumInaccuracy;
        }

        public static void Tick(ref PlayerState s, in PlayerInput cmd, float time, float dt, SimContext ctx)
        {
            int playerId = ctx.PlayerId;
            bool frozen = ctx.Frozen;
            IShotSink sink = ctx.Shots;
            bool atk = cmd.Has(Buttons.Attack), atk2 = cmd.Has(Buttons.Attack2);
            bool atkP = atk && !s.AttackHeld, atk2P = atk2 && !s.Attack2Held;
            bool relP = cmd.Has(Buttons.Reload) && !s.ReloadHeld;
            bool inspP = cmd.Has(Buttons.Inspect) && !s.InspectHeld;
            s.AttackHeld = atk; s.Attack2Held = atk2; s.ReloadHeld = cmd.Has(Buttons.Reload); s.InspectHeld = cmd.Has(Buttons.Inspect);
            if (!s.Alive) return;

            if (s.SwitchBackTime > 0 && time >= s.SwitchBackTime)
            {
                s.SwitchBackTime = 0;
                if (s.Active == WeaponSlotKind.Grenade && s.GrenadeCount(s.ActiveGrenade) == 0)
                {
                    var back = s.LastActive != WeaponSlotKind.Grenade && s.HasSlot(s.LastActive) ? s.LastActive : s.BestSlot;
                    SelectSlot(ref s, back, time);
                }
            }
            if (cmd.Select != WeaponSelect.None) Select(ref s, cmd.Select, time);
            if (!s.HasSlot(s.Active)) SelectSlot(ref s, s.BestSlot, time);
            var def = s.ActiveDef;

            // recoil / inaccuracy recovery
            if (def.Inacc.Recovery > 0) s.AccumInaccuracy *= MathF.Exp(-dt * 2.6f / def.Inacc.Recovery);
            if (s.RecoilIndex > 0 && time - s.LastShotTime > def.CycleTime * 1.05f)
                s.RecoilIndex = MathF.Max(0f, s.RecoilIndex - dt * MathF.Max(9f, s.RecoilIndex * 4.2f));
            if (s.ZoomResumeTime > 0 && time >= s.ZoomResumeTime)
            {
                if (!s.Reloading && def.ZoomFov != null) s.Zoom = s.ZoomResume;
                s.ZoomResumeTime = 0;
            }
            if (inspP && !s.Reloading && time >= s.DeployEndTime) s.InspectEndTime = time + 3.2f;
            if (atk || atk2 || s.Reloading) s.InspectEndTime = 0;

            if (frozen || time < s.DeployEndTime) return;

            switch (def.Category)
            {
                case WeaponCategory.Melee:
                    if (time < s.NextAttackTime) return;
                    if (atk || atk2)
                    {
                        bool heavy = !atk && atk2;
                        s.NextAttackTime = time + (heavy ? 1.0f : 0.4f);
                        s.LastShotTime = time;
                        var shot = BaseShot(ref s, def, playerId, cmd);
                        shot.Flags = ShotFlags.Melee | (heavy ? ShotFlags.Heavy : 0);
                        sink?.OnShot(shot);
                    }
                    return;
                case WeaponCategory.Grenade:
                    GrenadeTick(ref s, atk, atk2, time, ctx);
                    return;
                case WeaponCategory.Bomb:
                    BombTick(ref s, atk, time, ctx);
                    return;
                case WeaponCategory.Taser:
                    {
                        var slot = s.GetSlot(s.Active);
                        if (!atkP || time < s.NextAttackTime || slot.Clip <= 0) return;
                        slot.Clip--; s.SetSlot(s.Active, slot);
                        s.NextAttackTime = time + def.CycleTime;
                        s.LastShotTime = time;
                        var shot = BaseShot(ref s, def, playerId, cmd);
                        shot.Flags = ShotFlags.Taser;
                        sink?.OnShot(shot);
                        return;
                    }
                default:
                    GunTick(ref s, def, cmd, time, playerId, atk, atkP, atk2P, relP, sink);
                    return;
            }
        }

        static ShotInfo BaseShot(ref PlayerState s, WeaponDef def, int playerId, in PlayerInput cmd)
        {
            return new ShotInfo
            {
                ShooterId = playerId, Weapon = def.Id, Origin = s.EyePosition, Yaw = s.Yaw, Pitch = s.Pitch,
                Seed = DetRandom.Hash(playerId, s.ShotCounter++, 0x51), Pellets = 1, CmdTick = cmd.Tick, InterpTick = cmd.InterpTick,
            };
        }

        static void GunTick(ref PlayerState s, WeaponDef def, in PlayerInput cmd, float time, int playerId, bool atk, bool atkP, bool atk2P, bool relP, IShotSink sink)
        {
            var slot = s.GetSlot(s.Active);
            if (slot.IsEmpty) return;

            if (s.Reloading)
            {
                if (def.ShellReload)
                {
                    if (atkP && slot.Clip > 0) s.Reloading = false;
                    else if (time >= s.ReloadEndTime)
                    {
                        slot.Clip++; slot.Reserve--;
                        if (slot.Clip < def.ClipSize && slot.Reserve > 0) s.ReloadEndTime = time + def.ReloadTime;
                        else s.Reloading = false;
                        s.SetSlot(s.Active, slot);
                    }
                    if (s.Reloading) return;
                }
                else
                {
                    if (time < s.ReloadEndTime) return;
                    int take = Math.Min(def.ClipSize - slot.Clip, slot.Reserve);
                    slot.Clip += (short)take; slot.Reserve -= (short)take;
                    s.Reloading = false;
                    s.SetSlot(s.Active, slot);
                }
            }
            if (time < s.SilencerEndTime) return;

            if (atk2P)
            {
                if (def.ZoomFov != null)
                {
                    s.Zoom = (byte)((s.Zoom + 1) % (def.ZoomFov.Length + 1));
                    s.ZoomResumeTime = 0;
                }
                else if (def.Silencer && !def.FixedSilencer)
                {
                    s.SilencerEndTime = time + 1.6f;
                    slot.Silenced = !slot.Silenced;
                    s.SetSlot(s.Active, slot);
                    return;
                }
                else if (def.BurstShots > 0)
                {
                    slot.BurstMode = !slot.BurstMode;
                    s.SetSlot(s.Active, slot);
                }
                else if (def.PrimeTime > 0 && time >= s.NextAttackTime && slot.Clip > 0)
                {
                    Fire(ref s, def, ref slot, time, playerId, cmd, sink, 2.5f); // revolver fan
                    s.NextAttackTime = time + 0.4f;
                    s.SetSlot(s.Active, slot);
                    return;
                }
            }

            if (relP) { StartReload(ref s, def, slot, time); if (s.Reloading) return; }

            if (s.BurstLeft > 0 && time >= s.NextBurstTime)
            {
                if (slot.Clip > 0)
                {
                    Fire(ref s, def, ref slot, time, playerId, cmd, sink, 1f);
                    s.BurstLeft--; s.NextBurstTime += def.BurstInterval;
                    s.SetSlot(s.Active, slot);
                }
                else s.BurstLeft = 0;
                return;
            }

            if (!atk) { s.PrimeStartTime = 0; return; }
            if (slot.Clip <= 0)
            {
                if (atkP)
                {
                    if (slot.Reserve > 0) StartReload(ref s, def, slot, time);
                    s.NextAttackTime = MathF.Max(s.NextAttackTime, time + 0.2f);
                }
                return;
            }
            if (time < s.NextAttackTime) return;
            if (def.PrimeTime > 0)
            {
                if (s.PrimeStartTime <= 0) s.PrimeStartTime = time;
                if (time - s.PrimeStartTime < def.PrimeTime) return;
                s.PrimeStartTime = 0;
                Fire(ref s, def, ref slot, time, playerId, cmd, sink, 1f);
                s.SetSlot(s.Active, slot);
                return;
            }
            bool burst = slot.BurstMode && def.BurstShots > 0;
            if (!(def.Automatic && !burst) && !atkP) return;
            Fire(ref s, def, ref slot, time, playerId, cmd, sink, 1f);
            if (burst)
            {
                s.BurstLeft = (byte)(def.BurstShots - 1);
                s.NextBurstTime = time + def.BurstInterval;
                s.NextAttackTime = time + def.BurstCycle;
            }
            s.SetSlot(s.Active, slot);
        }

        static void StartReload(ref PlayerState s, WeaponDef def, WeaponSlot slot, float time)
        {
            if (s.Reloading || slot.Clip >= def.ClipSize || slot.Reserve <= 0 || def.ReloadTime <= 0) return;
            s.Reloading = true;
            s.ReloadStartTime = time;
            s.ReloadEndTime = time + (def.ShellReload ? def.ReloadTime + 0.35f : def.ReloadTime);
            s.Zoom = 0; s.ZoomResumeTime = 0; s.BurstLeft = 0;
        }

        static void Fire(ref PlayerState s, WeaponDef def, ref WeaponSlot slot, float time, int playerId, in PlayerInput cmd, IShotSink sink, float inaccMul)
        {
            slot.Clip--;
            // keep the exact fire rate even though shots happen on tick boundaries
            s.NextAttackTime = (time - s.NextAttackTime) < def.CycleTime ? s.NextAttackTime + def.CycleTime : time + def.CycleTime;
            if (s.NextAttackTime <= time) s.NextAttackTime = time + def.CycleTime;
            s.LastShotTime = time;
            float inacc = CurrentInaccuracy(s, def, time) * inaccMul;
            s.AccumInaccuracy += def.Inacc.Fire;
            RecoilPatterns.Sample(def, s.RecoilIndex, out float pp, out float py);
            s.RecoilIndex += 1f;
            var shot = BaseShot(ref s, def, playerId, cmd);
            shot.Yaw = s.Yaw + py * RecoilScale;
            shot.Pitch = s.Pitch + pp * RecoilScale;
            shot.Inaccuracy = inacc;
            shot.Spread = def.Inacc.Spread;
            shot.Pellets = def.Pellets;
            if (slot.Silenced) shot.Flags |= ShotFlags.Silenced;
            sink?.OnShot(shot);
            if (def.Category == WeaponCategory.Sniper && !def.Automatic && s.Zoom > 0)
            {
                // bolt-action: scope drops after the shot and comes back when the bolt is cycled
                s.ZoomResume = s.Zoom; s.Zoom = 0; s.ZoomResumeTime = s.NextAttackTime - 0.05f;
            }
        }

        public static void Select(ref PlayerState s, WeaponSelect sel, float time)
        {
            WeaponSlotKind target;
            switch (sel)
            {
                case WeaponSelect.Primary: target = WeaponSlotKind.Primary; break;
                case WeaponSelect.Secondary: target = WeaponSlotKind.Secondary; break;
                case WeaponSelect.Melee: target = WeaponSlotKind.Melee; break;
                case WeaponSelect.Grenade:
                    if (s.GrenadeTotal == 0) return;
                    if (s.Active == WeaponSlotKind.Grenade)
                    {
                        // pressing again cycles through the grenades you carry
                        var next = NextGrenade(s, s.ActiveGrenade);
                        if (next != s.ActiveGrenade) { s.ActiveGrenade = next; s.PinTime = 0; s.DeployEndTime = time + 0.4f; }
                        return;
                    }
                    if (s.GrenadeCount(s.ActiveGrenade) == 0) s.ActiveGrenade = NextGrenade(s, GrenadeType.None);
                    target = WeaponSlotKind.Grenade; break;
                case WeaponSelect.Bomb: target = WeaponSlotKind.Bomb; break;
                case WeaponSelect.LastUsed: target = s.LastActive; break;
                case WeaponSelect.Next: target = Cycle(s, 1); break;
                case WeaponSelect.Previous: target = Cycle(s, -1); break;
                default: return;
            }
            SelectSlot(ref s, target, time);
        }

        public static void SelectSlot(ref PlayerState s, WeaponSlotKind target, float time)
        {
            if (target == WeaponSlotKind.None || target == s.Active || !s.HasSlot(target)) return;
            if (target == WeaponSlotKind.Grenade && s.GrenadeCount(s.ActiveGrenade) == 0) s.ActiveGrenade = NextGrenade(s, GrenadeType.None);
            if (s.Active != WeaponSlotKind.None) s.LastActive = s.Active;
            s.Active = target;
            var def = s.ActiveDef;
            s.DeployEndTime = time + def.DeployTime;
            s.NextAttackTime = MathF.Max(s.NextAttackTime, s.DeployEndTime);
            s.Reloading = false; s.Zoom = 0; s.ZoomResumeTime = 0; s.BurstLeft = 0; s.PrimeStartTime = 0; s.InspectEndTime = 0;
            s.PinTime = 0; s.Planting = false; s.PlantStartTime = 0;
        }

        static readonly GrenadeType[] NadeOrder = { GrenadeType.HE, GrenadeType.Flash, GrenadeType.Smoke, GrenadeType.Molotov, GrenadeType.Decoy };

        public static GrenadeType NextGrenade(in PlayerState s, GrenadeType current)
        {
            int start = 0;
            var cur = current == GrenadeType.Incendiary ? GrenadeType.Molotov : current;
            for (int i = 0; i < NadeOrder.Length; i++) if (NadeOrder[i] == cur) start = i + 1;
            for (int k = 0; k < NadeOrder.Length; k++)
            {
                var g = NadeOrder[(start + k) % NadeOrder.Length];
                if (s.GrenadeCount(g) > 0) return g == GrenadeType.Molotov ? s.FireGrenadeType : g;
            }
            return GrenadeType.None;
        }

        static WeaponSlotKind Cycle(in PlayerState s, int dir)
        {
            var order = new[] { WeaponSlotKind.Primary, WeaponSlotKind.Secondary, WeaponSlotKind.Melee, WeaponSlotKind.Grenade, WeaponSlotKind.Bomb };
            int k = Array.IndexOf(order, s.Active);
            for (int i = 1; i <= order.Length; i++)
            {
                var c = order[((k + dir * i) % order.Length + order.Length) % order.Length];
                if (s.HasSlot(c)) return c;
            }
            return s.Active;
        }

        // ---------------- grenades ----------------
        static void GrenadeTick(ref PlayerState s, bool atk, bool atk2, float time, SimContext ctx)
        {
            if (s.GrenadeCount(s.ActiveGrenade) == 0) { s.PinTime = 0; return; }
            if (s.PinTime <= 0)
            {
                if ((atk || atk2) && time >= s.NextAttackTime) { s.PinTime = time; s.ThrowStrength = atk && atk2 ? 0.5f : atk ? 1f : 0.3f; }
                return;
            }
            if (atk || atk2) { s.ThrowStrength = atk && atk2 ? 0.5f : atk ? 1f : 0.3f; return; }
            if (time - s.PinTime < 0.15f) return;
            var type = s.ActiveGrenade;
            s.AddGrenade(type, -1);
            var t = new GrenadeThrow { OwnerId = ctx.PlayerId, Type = type, Origin = s.EyePosition, Velocity = ThrowVelocity(s, s.ThrowStrength) };
            s.PinTime = 0;
            s.NextAttackTime = time + 0.5f;
            s.SwitchBackTime = time + 0.4f;
            s.LastShotTime = time;
            ctx.Events?.OnThrow(t);
            if (s.GrenadeCount(type) == 0 && s.GrenadeTotal > 0) s.ActiveGrenade = NextGrenade(s, type);
        }

        /// <summary>CS throw: aims a bit above the crosshair, 675 HU/s scaled by strength, plus 1.25x player velocity.</summary>
        public static Vector3 ThrowVelocity(in PlayerState s, float strength)
        {
            float src = -s.Pitch;
            src = src < 0 ? -10f + src * (80f / 90f) : -10f + src * (100f / 90f);
            var dir = VMath.Forward(s.Yaw, -src);
            float speed = 675f * VMath.HU * (VMath.Clamp01(strength) * 0.7f + 0.3f);
            return dir * speed + s.Velocity * 1.25f;
        }

        // ---------------- C4 ----------------
        public const float PlantTime = 3.2f;

        static void BombTick(ref PlayerState s, bool atk, float time, SimContext ctx)
        {
            string site = ctx.Map?.SiteAt(s.Position);
            if (atk && s.HasC4 && s.OnGround && site != null && ctx.PlantAllowed)
            {
                if (!s.Planting) { s.Planting = true; s.PlantStartTime = time; }
                else if (time - s.PlantStartTime >= PlantTime)
                {
                    s.Planting = false; s.PlantStartTime = 0;
                    s.HasC4 = false;
                    ctx.Events?.OnPlanted(ctx.PlayerId, s.Position, site);
                    SelectSlot(ref s, s.BestSlot, time);
                }
            }
            else { s.Planting = false; s.PlantStartTime = 0; }
        }

        /// <summary>Give a weapon (buy / pick up). Returns the replaced weapon (to drop), if any.</summary>
        public static WeaponSlot Give(ref PlayerState s, WeaponId id, float time, bool equip = true)
        {
            var def = Weapons.Get(id);
            var old = s.GetSlot(def.Slot);
            s.SetSlot(def.Slot, WeaponSlot.Create(id));
            if (equip)
            {
                if (s.Active == def.Slot) { s.Active = WeaponSlotKind.None; }
                var sel = def.Slot == WeaponSlotKind.Primary ? WeaponSelect.Primary : def.Slot == WeaponSlotKind.Secondary ? WeaponSelect.Secondary : WeaponSelect.Melee;
                Select(ref s, sel, time);
            }
            return old;
        }
    }
}
