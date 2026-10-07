using System;
using System.Collections.Generic;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    public sealed partial class ServerGame
    {
        public sealed class Projectile
        {
            public int Id;
            public GrenadeType Type;
            public Player Owner;
            public Team OwnerTeam;
            public Vector3 Position, Velocity;
            public int BornTick, RestTick = -1, DecoyStartTick = -1, NextDecoyTick;
            public bool Done;
        }

        public sealed class Area
        {
            public int Id;
            public GrenadeType Type;
            public Vector3 Center;
            public float Radius;
            public int StartTick, EndTick, NextDamageTick;
            public Player Owner;
            public Team OwnerTeam;
        }

        public const float SmokeRadius = 144f * VMath.HU;
        public const float SmokeDuration = 18f;
        public const float FireRadius = 2.4f;
        public const float FireDuration = 7f;
        public const float HeRadius = 350f * VMath.HU;

        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Area> Smokes = new List<Area>();
        public readonly List<Area> Fires = new List<Area>();
        private int _nextProjectileId = 1;

        internal void SpawnProjectile(Player p, in GrenadeThrow t)
        {
            var origin = t.Origin;
            float speed = t.Velocity.Length();
            if (speed > 0.1f)
            {
                var dir = t.Velocity / speed;
                // start a little in front of the eye unless that's inside a wall
                if (!World.Raycast(origin, dir, 0.45f, out _)) origin += dir * 0.4f;
            }
            Projectiles.Add(new Projectile
            {
                Id = _nextProjectileId++ & 0xFFFF, Type = t.Type, Owner = p, OwnerTeam = p.State.Team,
                Position = origin, Velocity = t.Velocity, BornTick = Tick,
            });
        }

        private void StepGrenades()
        {
            foreach (var g in Projectiles)
            {
                if (g.Done) continue;
                float age = (Tick - g.BornTick) * Dt;
                if (g.RestTick < 0)
                {
                    var r = GrenadePhysics.Step(ref g.Position, ref g.Velocity, Dt, World);
                    if ((g.Type == GrenadeType.Molotov || g.Type == GrenadeType.Incendiary) && r.HitFloor) { Detonate(g); continue; }
                    if (r.AtRest) g.RestTick = Tick;
                }
                switch (g.Type)
                {
                    case GrenadeType.HE:
                    case GrenadeType.Flash:
                        if (age >= 1.5f) Detonate(g);
                        break;
                    case GrenadeType.Smoke:
                        if ((g.RestTick >= 0 && Tick - g.RestTick >= TickRate / 4) || age > 10f) Detonate(g);
                        break;
                    case GrenadeType.Molotov:
                    case GrenadeType.Incendiary:
                        if (age >= 2.0f) Detonate(g); // air burst, no fire
                        break;
                    case GrenadeType.Decoy:
                        StepDecoy(g);
                        break;
                }
            }
            Projectiles.RemoveAll(p => p.Done);
            Smokes.RemoveAll(s => Tick >= s.EndTick);

            // fires: damage over time, put out by smoke
            foreach (var f in Fires)
            {
                if (Tick >= f.EndTick) continue;
                foreach (var s in Smokes)
                    if (Vector3.Distance(s.Center, f.Center) < s.Radius + f.Radius * 0.5f) { f.EndTick = Tick; break; }
                if (Tick < f.NextDamageTick || Tick >= f.EndTick) continue;
                f.NextDamageTick = Tick + TickRate / 4;
                var def = Weapons.Get(WeaponId.Grenade);
                foreach (var p in _players.ToArray())
                {
                    if (!p.State.Alive) continue;
                    var d = p.State.Position - f.Center;
                    if (d.X * d.X + d.Z * d.Z > f.Radius * f.Radius || MathF.Abs(d.Y) > 1.2f) continue;
                    _killGrenade = f.Type;
                    ApplyDamage(f.Owner != null && _players.Contains(f.Owner) ? f.Owner : null, p, def, HitGroup.LeftLeg, 10, 0, false, p.State.Position);
                    _killGrenade = GrenadeType.None;
                }
            }
            Fires.RemoveAll(f => Tick >= f.EndTick);
        }

        private GrenadeType _killGrenade;

        private void Detonate(Projectile g)
        {
            g.Done = true;
            var pos = g.Position;
            Broadcast(new GameEvent { Type = GameEventType.Detonation, A = (int)g.Type, B = g.Owner?.Id ?? 0, Position = pos });
            ServerBot.HearNoise(this, g.Owner, pos, 30f);
            switch (g.Type)
            {
                case GrenadeType.HE: ExplodeHE(g, pos); break;
                case GrenadeType.Flash: Flashbang(g, pos); break;
                case GrenadeType.Smoke:
                    {
                        var smoke = new Area { Id = g.Id, Type = GrenadeType.Smoke, Center = pos + new Vector3(0, 1.0f, 0), Radius = SmokeRadius, StartTick = Tick, EndTick = Tick + (int)(SmokeDuration * TickRate), Owner = g.Owner, OwnerTeam = g.OwnerTeam };
                        Smokes.Add(smoke);
                        break;
                    }
                case GrenadeType.Molotov:
                case GrenadeType.Incendiary:
                    {
                        // only bursts into flames on the ground, and not inside a smoke
                        bool onGround = g.RestTick >= 0 || World.Raycast(pos + new Vector3(0, 0.2f, 0), new Vector3(0, -1, 0), 0.5f, out _);
                        if (!onGround || PointInSmoke(pos + new Vector3(0, 0.3f, 0))) break;
                        Fires.Add(new Area { Id = g.Id, Type = g.Type, Center = pos, Radius = FireRadius, StartTick = Tick, EndTick = Tick + (int)(FireDuration * TickRate), Owner = g.Owner, OwnerTeam = g.OwnerTeam });
                        break;
                    }
            }
        }

        private void ExplodeHE(Projectile g, Vector3 pos)
        {
            var c = pos + new Vector3(0, 0.1f, 0);
            var def = Weapons.Get(WeaponId.Grenade);
            foreach (var p in _players.ToArray())
            {
                if (!p.State.Alive) continue;
                var chest = p.State.Position + new Vector3(0, p.State.HullHeight * 0.5f, 0);
                float d = Vector3.Distance(chest, c);
                if (d > HeRadius) continue;
                if (!World.LineOfSight(c, chest) && !World.LineOfSight(c, p.State.EyePosition)) continue;
                float raw = 98f * (1f - d / HeRadius);
                var r = DamageModel.ApplyArmor(raw, 0.5f, HitGroup.Chest, p.State.Armor, p.State.Helmet);
                _killGrenade = GrenadeType.HE;
                ApplyDamage(g.Owner != null && _players.Contains(g.Owner) ? g.Owner : null, p, def, HitGroup.Chest, r.Health, r.Armor, false, chest);
                _killGrenade = GrenadeType.None;
            }
        }

        private void Flashbang(Projectile g, Vector3 pos)
        {
            foreach (var p in _players)
            {
                if (!p.State.Alive) continue;
                var eye = p.State.EyePosition;
                var to = pos - eye;
                float d = to.Length();
                if (d > 2400f * VMath.HU || d < 1e-3f) continue;
                if (!World.LineOfSight(eye, pos) || SegmentInSmoke(eye, to / d, d)) continue;
                float dot = Vector3.Dot(VMath.Forward(p.State.Yaw, p.State.Pitch), to / d);
                float facing = dot > 0.6f ? 1f : dot > -0.2f ? 0.35f + 0.65f * (dot + 0.2f) / 0.8f : 0.12f;
                float near = 400f * VMath.HU;
                float distF = d < near ? 1f : VMath.Clamp(1f - (d - near) / (2000f * VMath.HU), 0.15f, 1f);
                float dur = 4.9f * facing * distF;
                if (dur < 0.25f) continue;
                float now = p.PlayerTime(Dt);
                if (now + dur <= p.State.FlashEndTime) continue;
                p.State.FlashEndTime = now + dur;
                p.State.FlashFullEndTime = now + (facing >= 1f ? MathF.Min(dur * 0.45f, 2.2f) : dur * 0.15f);
                p.Bot?.OnFlashed(dur);
            }
        }

        private void StepDecoy(Projectile g)
        {
            if (g.RestTick < 0) return;
            if (g.DecoyStartTick < 0) { g.DecoyStartTick = Tick; g.NextDecoyTick = Tick + TickRate / 2; }
            if (Tick >= g.NextDecoyTick)
            {
                var weapon = g.Owner != null && !g.Owner.State.Primary.IsEmpty ? g.Owner.State.Primary.Id : WeaponId.Glock;
                var shot = new ShotInfo { ShooterId = g.Owner?.Id ?? 0, Weapon = weapon, Origin = g.Position + new Vector3(0, 0.1f, 0), Yaw = _rng.Next(360), Pitch = 0, Pellets = 0 };
                _w.Reset(); _w.Byte((byte)Msg.ShotFx); Protocol.WriteShot(_w, shot);
                SendAll(Delivery.Unreliable);
                ServerBot.HearNoise(this, g.Owner, g.Position, 35f);
                g.NextDecoyTick = Tick + (int)(TickRate * (0.4f + (float)_rng.NextDouble() * 1.1f));
            }
            if (Tick - g.DecoyStartTick > 15 * TickRate)
            {
                g.Done = true;
                Broadcast(new GameEvent { Type = GameEventType.Detonation, A = (int)GrenadeType.Decoy, B = g.Owner?.Id ?? 0, Position = g.Position });
            }
        }

        public bool PointInSmoke(Vector3 p)
        {
            foreach (var s in Smokes)
            {
                float grow = VMath.Clamp01((Tick - s.StartTick) / (float)TickRate);
                float r = s.Radius * grow;
                var d = p - s.Center; d.Y *= 1.3f;
                if (d.LengthSquared() < r * r) return true;
            }
            return false;
        }

        /// <summary>True if the segment passes through more than ~1 m of smoke (vision blocked).</summary>
        public bool SegmentInSmoke(Vector3 o, Vector3 dir, float len)
        {
            foreach (var s in Smokes)
            {
                float grow = VMath.Clamp01((Tick - s.StartTick) / (float)TickRate);
                float r = s.Radius * grow;
                if (r < 0.3f) continue;
                var oc = s.Center - o;
                float t = Vector3.Dot(oc, dir);
                float d2 = oc.LengthSquared() - t * t;
                if (d2 >= r * r) continue;
                float half = MathF.Sqrt(r * r - d2);
                float t0 = MathF.Max(0f, t - half), t1 = MathF.Min(len, t + half);
                if (t1 - t0 > 1.0f) return true;
            }
            return false;
        }

        /// <summary>Line of sight that respects both walls and smoke.</summary>
        public bool CanSee(Vector3 a, Vector3 b)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-3f) return true;
            return World.LineOfSight(a, b) && !SegmentInSmoke(a, d / len, len);
        }

        private void ClearGrenades()
        {
            Projectiles.Clear(); Smokes.Clear(); Fires.Clear();
        }

        private void WriteGrenadeSnapshot(NetWriter w)
        {
            int n = Math.Min(Projectiles.Count, 30);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) { var p = Projectiles[i]; w.UShort((ushort)p.Id); w.Byte((byte)p.Type); w.Vec3(p.Position); }
            w.Byte((byte)Math.Min(Smokes.Count, 20));
            for (int i = 0; i < Math.Min(Smokes.Count, 20); i++) { var s = Smokes[i]; Protocol.WriteArea(w, new AreaInfo { Id = s.Id, Type = s.Type, Center = s.Center, Radius = s.Radius, StartTick = s.StartTick, EndTick = s.EndTick }); }
            w.Byte((byte)Math.Min(Fires.Count, 20));
            for (int i = 0; i < Math.Min(Fires.Count, 20); i++) { var f = Fires[i]; Protocol.WriteArea(w, new AreaInfo { Id = f.Id, Type = f.Type, Center = f.Center, Radius = f.Radius, StartTick = f.StartTick, EndTick = f.EndTick }); }
        }
    }
}
