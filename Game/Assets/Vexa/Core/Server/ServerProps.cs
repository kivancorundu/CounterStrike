using System;
using System.Collections.Generic;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Server side of the fun physics: props (crates, barrels) that players push, climb and stack, bullets that shove
    /// them, red barrels that explode, and explosions that throw players and props around.
    /// </summary>
    public sealed partial class ServerGame
    {
        public const float BarrelRadius = 5.5f;
        public const float BarrelDamage = 95f;
        public const float BarrelFuse = 0.35f;        // seconds from the killing shot to the bang
        public const float PropRespawnTime = 25f;     // deathmatch / practice: destroyed props come back

        public readonly PropWorld PropWorld = new PropWorld();
        private readonly List<StaticBox> _hulls = new List<StaticBox>();
        private int _propCursor;

        private void InitProps()
        {
            foreach (var mp in Map.Props) PropWorld.Add(mp.Kind, mp.Position, mp.Yaw);
        }

        private bool FunPhysics => Config.FunPhysics;

        private void AddPropObstacles(List<StaticBox> into) => PropWorld.AddObstacles(into);

        /// <summary>Walking into a prop pushes it.</summary>
        private void PushProps(Player p, List<Touch> touches)
        {
            foreach (var t in touches)
            {
                if (t.Kind != BodyKind.Prop || t.Normal.Y > 0.5f || t.Normal.Y < -0.5f) continue;
                var prop = PropWorld.Get(t.Owner);
                if (prop == null) continue;
                var dir = -t.Normal;
                float speed = Vector3.Dot(new Vector3(t.Velocity.X, 0, t.Velocity.Z), dir);
                PropWorld.Push(prop, dir, speed, p.Id);
            }
        }

        private void StepProps()
        {
            if (PropWorld.Props.Count == 0) return;
            _hulls.Clear();
            foreach (var p in _players) if (p.State.Alive) _hulls.Add(SimContext.HullOf(p.State, p.Id));
            PropWorld.Step(Dt, World, _hulls);

            // a prop flying into a player hurts and shoves them
            foreach (var (prop, id, speed) in PropWorld.PlayerHits)
            {
                var victim = GetPlayer(id);
                if (victim == null || !victim.State.Alive) continue;
                var dir = prop.Velocity; dir.Y = 0;
                if (dir.LengthSquared() > 1e-4f) Knockback.Apply(ref victim.State, Vector3.Normalize(dir) * MathF.Min(6f, speed * 0.5f));
                int dmg = (int)((speed - PropWorld.HitPlayerSpeed) * 5f);
                if (dmg > 0) ApplyDamage(GetPlayer(prop.Attacker), victim, Weapons.Get(WeaponId.Grenade), HitGroup.Chest, dmg, 0, false, victim.State.Position);
            }

            foreach (var prop in PropWorld.Props)
            {
                if (prop.FuseTick >= 0 && Tick >= prop.FuseTick) ExplodeBarrel(prop);
                if (prop.Destroyed && prop.RespawnTick >= 0 && Tick >= prop.RespawnTick) PropWorld.Respawn(prop);
            }
        }

        /// <summary>Round start (and match start): everything back where the map put it.</summary>
        private void ResetProps() => PropWorld.ResetAll();

        /// <summary>A bullet hit a prop: shove it, and hurt it if it can break (red barrels).</summary>
        internal void ShootProp(Prop prop, Player shooter, Vector3 dir, float damage, Vector3 point)
        {
            if (prop == null || prop.Destroyed) return;
            if (shooter != null) prop.Attacker = shooter.Id;
            float k = 0.012f * (25f / prop.Def.Mass);
            var lever = point - prop.Position;
            float spin = (lever.X * dir.Z - lever.Z * dir.X) * damage * 3f;
            PropWorld.Impulse(prop, dir * damage * k + new Vector3(0, damage * k * 0.3f, 0), spin);
            if (prop.Def.Health > 0 && prop.FuseTick < 0)
            {
                prop.Health -= (short)Math.Max(1, (int)damage);
                if (prop.Health <= 0) prop.FuseTick = Tick + (int)(BarrelFuse * TickRate);
            }
        }

        private void ExplodeBarrel(Prop prop)
        {
            prop.FuseTick = -1;
            prop.Destroyed = true;
            prop.LastActiveTick = PropWorld.Tick;
            if (Config.Mode == GameMode.Deathmatch || Config.Mode == GameMode.Practice || Phase == GamePhase.Warmup)
                prop.RespawnTick = Tick + (int)(PropRespawnTime * TickRate);
            var c = prop.Position;
            var owner = GetPlayer(prop.Attacker);
            Broadcast(new GameEvent { Type = GameEventType.Detonation, A = (int)GrenadeType.HE, B = owner?.Id ?? 0, Position = c });
            ServerBot.HearNoise(this, owner, c, 40f);
            Blast(c, BarrelRadius, BarrelDamage, Knockback.BarrelStrength, owner, GrenadeType.HE);
        }

        /// <summary>Damage (with line of sight), knockback for players and props, chain reactions.</summary>
        internal void Blast(Vector3 c, float radius, float maxDamage, float strength, Player owner, GrenadeType kind)
        {
            var def = Weapons.Get(WeaponId.Grenade);
            foreach (var p in _players.ToArray())
            {
                if (!p.State.Alive) continue;
                var chest = p.State.Position + new Vector3(0, p.State.HullHeight * 0.5f, 0);
                float d = Vector3.Distance(chest, c);
                if (d > radius) continue;
                if (!World.LineOfSight(c, chest) && !World.LineOfSight(c, p.State.EyePosition)) continue;
                if (maxDamage > 0)
                {
                    float raw = maxDamage * (1f - d / radius);
                    var r = DamageModel.ApplyArmor(raw, 0.5f, HitGroup.Chest, p.State.Armor, p.State.Helmet);
                    _killGrenade = kind;
                    ApplyDamage(owner != null && _players.Contains(owner) ? owner : null, p, def, HitGroup.Chest, r.Health, r.Armor, false, chest);
                    _killGrenade = GrenadeType.None;
                }
                if (FunPhysics) Knockback.Apply(ref p.State, Knockback.Explosion(c, chest, radius, strength));
            }
            if (!FunPhysics) return;
            foreach (var prop in PropWorld.Props)
            {
                if (prop.Destroyed) continue;
                var dv = Knockback.Explosion(c, prop.Position, radius, strength * 1.3f) * (25f / prop.Def.Mass);
                if (dv.LengthSquared() < 0.01f) continue;
                if (!World.LineOfSight(c, prop.Position)) continue;
                if (owner != null) prop.Attacker = owner.Id;
                PropWorld.Impulse(prop, dv, ((prop.Id * 137) % 2 == 0 ? 1 : -1) * dv.Length() * 60f);
                // chain reaction: barrels caught in a blast go off a moment later
                if (prop.Def.Health > 0 && prop.FuseTick < 0)
                {
                    prop.Health -= (short)(60f * (1f - Vector3.Distance(c, prop.Position) / radius));
                    if (prop.Health <= 0) prop.FuseTick = Tick + (int)(BarrelFuse * TickRate);
                }
            }
        }

        /// <summary>Props for one snapshot: every prop that moved recently, plus a few others in turn, so clients
        /// that just joined (or missed packets) learn about all of them without sending all of them every tick.</summary>
        private void WritePropsSnapshot(NetWriter w)
        {
            var list = PropWorld.Props;
            int n = list.Count;
            if (n == 0) { w.Byte(0); return; }
            Span<int> pick = stackalloc int[Math.Min(n, 48)];
            int count = 0;
            int recent = PropWorld.Tick - 64;
            for (int i = 0; i < n && count < pick.Length; i++)
                if (list[i].LastActiveTick >= recent || !list[i].Asleep) pick[count++] = i;
            for (int k = 0; k < 3 && count < pick.Length; k++)
            {
                _propCursor = (_propCursor + 1) % n;
                bool dup = false;
                for (int j = 0; j < count; j++) if (pick[j] == _propCursor) { dup = true; break; }
                if (!dup) pick[count++] = _propCursor;
            }
            w.Byte((byte)count);
            for (int j = 0; j < count; j++)
            {
                var p = list[pick[j]];
                w.UShort((ushort)p.Id);
                w.Byte((byte)((byte)p.Kind | (p.Destroyed ? 0x80 : 0) | (p.FuseTick >= 0 ? 0x40 : 0)));
                w.Vec3(p.Position);
                w.UShort(VMath.QuantizeYaw(p.Yaw));
            }
        }
    }
}
