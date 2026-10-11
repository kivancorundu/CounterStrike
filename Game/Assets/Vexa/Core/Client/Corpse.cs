using System;
using System.Numerics;

namespace Vexa.Core.Client
{
    /// <summary>
    /// A body thrown by the blow that killed it (client-side look only: the server keeps the dead player where it
    /// died). It flies, tumbles, bounces off walls and slides to a stop; the death animation then lays it down.
    /// </summary>
    public struct Corpse
    {
        public Vector3 Position, Velocity;   // feet
        public float Angle, Spin;            // tumble (degrees, degrees per second) about the flight direction
        public float FlightYaw;              // heading of the throw
        public float Age;
        public bool OnGround, Resting;
    }

    public static class CorpsePhysics
    {
        static readonly Vector3 Half = new Vector3(0.28f, 0.3f, 0.28f);
        const float Friction = 9f;           // m/s^2 sliding on the ground
        const float Bounce = 0.3f;

        public static Corpse Start(Vector3 feet, Vector3 force)
        {
            float speed = force.Length();
            var c = new Corpse { Position = feet, Velocity = force, Spin = MathF.Min(900f, speed * 70f) };
            var flat = new Vector2(force.X, force.Z);
            c.FlightYaw = flat.LengthSquared() > 1e-4f ? MathF.Atan2(force.X, force.Z) * 180f / MathF.PI : 0f;
            c.Resting = speed < 0.3f;
            return c;
        }

        public static void Step(ref Corpse c, float dt, CollisionWorld w)
        {
            c.Age += dt;
            if (c.Resting) return;
            c.Velocity.Y -= SimConstants.Gravity * dt;
            var up = new Vector3(0, Half.Y, 0);
            float remaining = 1f;
            c.OnGround = false;
            for (int bump = 0; bump < 3 && remaining > 1e-4f; bump++)
            {
                var start = c.Position + up;
                var move = c.Velocity * dt * remaining;
                if (move.LengthSquared() < 1e-12f) break;
                var tr = w.TraceBox(start, start + move, Half);
                c.Position = tr.EndCenter - up;
                if (!tr.Hit) break;
                remaining *= 1f - tr.Fraction;
                float vn = Vector3.Dot(c.Velocity, tr.Normal);
                if (vn < 0f) c.Velocity -= tr.Normal * vn * (-vn > 2f ? 1f + Bounce : 1f);
                if (tr.Normal.Y > 0.7f) c.OnGround = true;
                else c.Spin *= 0.6f;   // a wall stops the spin
            }
            if (!c.OnGround)
            {
                var g = w.TraceBox(c.Position + up, c.Position + up - new Vector3(0, 0.03f, 0), Half);
                c.OnGround = g.Hit && g.Normal.Y > 0.7f;
            }
            if (c.OnGround)
            {
                if (c.Velocity.Y < 0) c.Velocity.Y = 0;
                float hs = MathF.Sqrt(c.Velocity.X * c.Velocity.X + c.Velocity.Z * c.Velocity.Z);
                if (hs > 1e-5f)
                {
                    float k = MathF.Max(0f, hs - Friction * dt) / hs;
                    c.Velocity.X *= k; c.Velocity.Z *= k;
                }
                // settle the tumble on the nearest full turn, the death animation lays the body down
                float target = MathF.Round(c.Angle / 360f) * 360f;
                c.Angle += (target - c.Angle) * MathF.Min(1f, dt * 10f);
                c.Spin = 0f;
                if (hs < 0.05f && MathF.Abs(target - c.Angle) < 1f) { c.Angle = 0; c.Velocity = Vector3.Zero; c.Resting = true; }
            }
            else c.Angle += c.Spin * dt;
            if (c.Age > 8f) c.Resting = true;
        }
    }
}
