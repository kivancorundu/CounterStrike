using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vexa.Core
{
    public enum PropKind : byte { None = 0, Crate = 1, SmallCrate = 2, Barrel = 3, RedBarrel = 4 }

    public sealed class PropDef
    {
        public PropKind Kind;
        public string Name;
        public Vector3 Half;          // collision half extents (boxes, also for the round barrels)
        public float Mass;            // kg
        public float Restitution;     // bounce
        public float Friction;        // ground deceleration, m/s^2
        public SurfaceMaterial Material;
        public short Health;          // 0 = can't be destroyed
    }

    public static class Props
    {
        static readonly PropDef[] Defs =
        {
            null,
            new PropDef { Kind = PropKind.Crate, Name = "crate", Half = new Vector3(0.45f, 0.45f, 0.45f), Mass = 25f, Restitution = 0.15f, Friction = 7f, Material = SurfaceMaterial.Wood },
            new PropDef { Kind = PropKind.SmallCrate, Name = "smallcrate", Half = new Vector3(0.3f, 0.3f, 0.3f), Mass = 10f, Restitution = 0.25f, Friction = 6f, Material = SurfaceMaterial.Wood },
            new PropDef { Kind = PropKind.Barrel, Name = "barrel", Half = new Vector3(0.29f, 0.44f, 0.29f), Mass = 30f, Restitution = 0.3f, Friction = 5f, Material = SurfaceMaterial.Metal },
            new PropDef { Kind = PropKind.RedBarrel, Name = "redbarrel", Half = new Vector3(0.29f, 0.44f, 0.29f), Mass = 30f, Restitution = 0.3f, Friction = 5f, Material = SurfaceMaterial.Metal, Health = 30 },
        };

        public static PropDef Get(PropKind k) => Defs[(int)k] ?? Defs[1];

        public static bool TryParse(string s, out PropKind k)
        {
            for (int i = 1; i < Defs.Length; i++)
                if (string.Equals(Defs[i].Name, s, StringComparison.OrdinalIgnoreCase)) { k = (PropKind)i; return true; }
            k = PropKind.None;
            return false;
        }

        public static string NameOf(PropKind k) => Get(k).Name;
    }

    public sealed class Prop
    {
        public int Id;
        public PropDef Def;
        public PropKind Kind => Def.Kind;
        public Vector3 Position;      // center
        public Vector3 Velocity;
        public float Yaw;             // degrees, looks only (collision stays an axis-aligned box)
        public float Spin;            // degrees per second
        public bool OnGround, Asleep, Destroyed;
        public int RestTicks;
        public short Health;
        public int FuseTick = -1;     // red barrel: set alight, explodes at this tick
        public int Attacker;          // last player who shot / pushed it (explosion kill credit)
        public int LastActiveTick;    // last tick it moved (snapshots send recently active props every tick)
        public int RespawnTick = -1;
        public Vector3 Home;
        public float HomeYaw;

        public Vector3 Half => Def.Half;
        public Vector3 Bottom => Position - new Vector3(0, Def.Half.Y, 0);

        public StaticBox Box => new StaticBox
        {
            Min = Position - Def.Half, Max = Position + Def.Half, Material = Def.Material,
            Kind = BodyKind.Prop, Owner = Id, Velocity = Velocity,
        };
    }

    /// <summary>
    /// The movable things in a map: crates and barrels you can push, stand and climb on, stack, shoot around and blow
    /// up. A small rigid-box engine tuned for fun, not realism: gravity, sliding friction, bounces, momentum passed on
    /// between props, players as immovable walls (they push props through <see cref="Push"/>), sleep when at rest.
    /// Deterministic and server-authoritative; clients interpolate the result.
    /// </summary>
    public sealed class PropWorld
    {
        public const float SleepSpeed = 0.08f;
        public const int SleepTicks = 24;
        public const float HitPlayerSpeed = 7f;   // a prop faster than this hurts the player it hits

        public readonly List<Prop> Props = new List<Prop>();
        private readonly List<StaticBox> _scratch = new List<StaticBox>();
        public int Tick;

        /// <summary>Players hit by a fast prop during the last step: (prop, player id, relative speed).</summary>
        public readonly List<(Prop prop, int player, float speed)> PlayerHits = new List<(Prop, int, float)>();

        public Prop Get(int id)
        {
            foreach (var p in Props) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Adds a prop standing at <paramref name="bottom"/> (center of its base).</summary>
        public Prop Add(PropKind kind, Vector3 bottom, float yaw)
        {
            var d = global::Vexa.Core.Props.Get(kind);
            var p = new Prop
            {
                Id = Props.Count + 1, Def = d, Position = bottom + new Vector3(0, d.Half.Y, 0), Yaw = yaw,
                Health = d.Health, Asleep = true, OnGround = true,
            };
            p.Home = p.Position; p.HomeYaw = yaw;
            Props.Add(p);
            return p;
        }

        public void ResetAll()
        {
            foreach (var p in Props) Respawn(p);
        }

        public void Respawn(Prop p)
        {
            p.Position = p.Home; p.Yaw = p.HomeYaw; p.Velocity = Vector3.Zero; p.Spin = 0;
            p.Destroyed = false; p.Health = p.Def.Health; p.FuseTick = -1; p.Attacker = 0; p.RespawnTick = -1;
            p.Asleep = true; p.OnGround = true; p.RestTicks = 0; p.LastActiveTick = Tick;
        }

        public void AddObstacles(List<StaticBox> into, Prop except = null)
        {
            foreach (var p in Props)
                if (!p.Destroyed && p != except) into.Add(p.Box);
        }

        public void Wake(Prop p)
        {
            if (p.Destroyed) return;
            p.Asleep = false; p.RestTicks = 0; p.LastActiveTick = Tick;
        }

        /// <summary>Adds velocity (m/s) and spin (deg/s).</summary>
        public void Impulse(Prop p, Vector3 dv, float spin = 0f)
        {
            if (p.Destroyed) return;
            p.Velocity += dv;
            p.Spin += spin;
            if (dv.Y > 0.5f) p.OnGround = false;
            Wake(p);
        }

        /// <summary>A player walking into a prop: it slides ahead of them (lighter ones a little faster).</summary>
        public void Push(Prop p, Vector3 dir, float speed, int playerId)
        {
            if (p.Destroyed || speed <= 0.05f) return;
            dir.Y = 0;
            float l = dir.Length();
            if (l < 1e-4f) return;
            dir /= l;
            float target = speed * MathF.Min(1.1f, 40f / (p.Def.Mass + 15f));
            float cur = Vector3.Dot(p.Velocity, dir);
            if (cur >= target) return;
            p.Velocity += dir * (target - cur);
            p.Attacker = playerId;
            Wake(p);
        }

        /// <summary>One simulation step. <paramref name="playerHulls"/> block props (and get hit by fast ones).</summary>
        public void Step(float dt, CollisionWorld world, List<StaticBox> playerHulls)
        {
            Tick++;
            PlayerHits.Clear();
            for (int i = 0; i < Props.Count; i++)
            {
                var p = Props[i];
                if (p.Destroyed) continue;
                if (p.Asleep)
                {
                    // a sleeping prop wakes up when what held it up is gone (staggered checks)
                    if ((Tick + p.Id) % 16 != 0) continue;
                    Obstacles(world, playerHulls, p);
                    var g = world.TraceBox(p.Position, p.Position - new Vector3(0, 0.03f, 0), p.Half);
                    world.Dynamic.Clear();
                    if (g.Hit && g.Normal.Y > 0.7f) continue;
                    Wake(p);
                }
                StepOne(p, dt, world, playerHulls);
            }
        }

        private void Obstacles(CollisionWorld world, List<StaticBox> playerHulls, Prop self)
        {
            world.Dynamic.Clear();
            if (playerHulls != null) world.Dynamic.AddRange(playerHulls);
            AddObstacles(world.Dynamic, self);
        }

        private void StepOne(Prop p, float dt, CollisionWorld world, List<StaticBox> playerHulls)
        {
            var d = p.Def;
            p.Position = world.Depenetrate(p.Position, d.Half);
            Obstacles(world, playerHulls, p);
            p.Velocity.Y -= SimConstants.Gravity * dt;
            if (p.OnGround)
            {
                float hs = MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Z * p.Velocity.Z);
                if (hs > 1e-5f)
                {
                    float k = MathF.Max(0f, hs - d.Friction * dt) / hs;
                    p.Velocity.X *= k; p.Velocity.Z *= k;
                }
                p.Spin *= MathF.Max(0f, 1f - 6f * dt);
            }
            else p.Spin *= MathF.Max(0f, 1f - 0.8f * dt);
            float m = p.Velocity.Length();
            if (m > 40f) p.Velocity *= 40f / m;

            float remaining = 1f;
            for (int bump = 0; bump < 4 && remaining > 1e-4f; bump++)
            {
                var move = p.Velocity * dt * remaining;
                if (move.LengthSquared() < 1e-12f) break;
                var tr = world.TraceBox(p.Position, p.Position + move, d.Half);
                p.Position = tr.EndCenter;
                if (!tr.Hit) break;
                remaining *= 1f - tr.Fraction;
                var n = tr.Normal;
                if (world.TryGetDynamic(tr.BoxIndex, out var other) && other.Kind == BodyKind.Prop)
                {
                    var q = Get(other.Owner);
                    if (q != null) Collide(p, q, n);
                    else Bounce(ref p.Velocity, n, d.Restitution);
                }
                else
                {
                    if (world.TryGetDynamic(tr.BoxIndex, out var pl) && pl.Kind == BodyKind.Player)
                    {
                        float rel = -Vector3.Dot(p.Velocity - pl.Velocity, n);
                        if (rel > HitPlayerSpeed) PlayerHits.Add((p, pl.Owner, rel));
                    }
                    Bounce(ref p.Velocity, n, d.Restitution);
                    if (n.Y < 0.7f) p.Spin += (n.X * p.Velocity.Z - n.Z * p.Velocity.X) * 25f; // glancing hits spin it
                }
            }

            var down = world.TraceBox(p.Position, p.Position - new Vector3(0, 0.03f, 0), d.Half);
            p.OnGround = down.Hit && down.Normal.Y > 0.7f;
            if (p.OnGround && p.Velocity.Y < 0f) p.Velocity.Y = 0f;
            // riding on a moving prop (a crate on a crate being pushed)
            if (p.OnGround && world.TryGetDynamic(down.BoxIndex, out var under) && under.Kind == BodyKind.Prop)
            {
                var carry = new Vector3(under.Velocity.X, 0, under.Velocity.Z) * dt;
                if (carry.LengthSquared() > 1e-10f) p.Position = world.TraceBox(p.Position, p.Position + carry, d.Half).EndCenter;
            }
            world.Dynamic.Clear();

            // whatever sleeps on top of a moving prop wakes up to ride along (or fall off)
            if (p.Velocity.LengthSquared() > SleepSpeed * SleepSpeed)
            {
                float top = p.Position.Y + d.Half.Y;
                foreach (var q in Props)
                {
                    if (q == p || q.Destroyed || !q.Asleep) continue;
                    if (MathF.Abs(q.Position.Y - q.Def.Half.Y - top) > 0.06f) continue;
                    if (MathF.Abs(q.Position.X - p.Position.X) < q.Def.Half.X + d.Half.X && MathF.Abs(q.Position.Z - p.Position.Z) < q.Def.Half.Z + d.Half.Z) Wake(q);
                }
            }

            p.Yaw = (p.Yaw + p.Spin * dt) % 360f;
            if (p.OnGround && p.Velocity.LengthSquared() < SleepSpeed * SleepSpeed && MathF.Abs(p.Spin) < 10f)
            {
                if (++p.RestTicks >= SleepTicks) { p.Asleep = true; p.Velocity = Vector3.Zero; p.Spin = 0; }
            }
            else { p.RestTicks = 0; p.LastActiveTick = Tick; }
        }

        private static void Bounce(ref Vector3 v, Vector3 n, float e)
        {
            float vn = Vector3.Dot(v, n);
            if (vn >= 0f) return;
            // slow contacts don't bounce (no jitter on the floor)
            v -= n * vn * (-vn > 1.5f ? 1f + e : 1f);
        }

        /// <summary>Momentum exchange along the contact normal between two props.</summary>
        private void Collide(Prop a, Prop b, Vector3 n)
        {
            float rel = Vector3.Dot(a.Velocity - b.Velocity, n);
            if (rel >= 0f) return;
            if (n.Y > 0.7f)
            {
                // landing on top of another prop: it holds the weight like the ground does
                var v = a.Velocity - b.Velocity;
                Bounce(ref v, n, MathF.Min(a.Def.Restitution, b.Def.Restitution));
                a.Velocity = v + b.Velocity;
                return;
            }
            if (n.Y < -0.7f)
            {
                // thrown up into the one above: lift it
                b.Velocity.Y = MathF.Max(b.Velocity.Y, a.Velocity.Y);
                b.OnGround = false;
                Wake(b);
                return;
            }
            float e = MathF.Min(a.Def.Restitution, b.Def.Restitution);
            float j = -(1f + e) * rel / (1f / a.Def.Mass + 1f / b.Def.Mass);
            a.Velocity += n * (j / a.Def.Mass);
            b.Velocity -= n * (j / b.Def.Mass);
            if (a.Attacker != 0) b.Attacker = a.Attacker;
            Wake(b);
        }
    }
}
