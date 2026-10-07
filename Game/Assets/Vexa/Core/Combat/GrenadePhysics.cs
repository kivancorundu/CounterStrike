using System.Numerics;

namespace Vexa.Core
{
    /// <summary>
    /// Thrown grenade motion (CS: 0.4x gravity, 0.45 elasticity). Deterministic and shared, so the client
    /// can draw the same trajectory preview the server will simulate.
    /// </summary>
    public static class GrenadePhysics
    {
        public const float GravityScale = 0.4f;
        public const float Elasticity = 0.45f;
        public const float Radius = 0.05f;

        public struct StepResult { public bool Hit, HitFloor, AtRest; public Vector3 Normal; public float ImpactSpeed; }

        public static StepResult Step(ref Vector3 pos, ref Vector3 vel, float dt, CollisionWorld world)
        {
            var res = new StepResult();
            vel.Y -= SimConstants.Gravity * GravityScale * dt;
            float remaining = dt;
            for (int i = 0; i < 3 && remaining > 0; i++)
            {
                float speed = vel.Length();
                if (speed < 1e-4f) break;
                float dist = speed * remaining;
                var dir = vel / speed;
                if (!world.Raycast(pos, dir, dist + Radius, out var hit) || hit.Distance > dist + Radius)
                {
                    pos += dir * dist;
                    break;
                }
                float travel = System.MathF.Max(0f, hit.Distance - Radius);
                pos += dir * travel;
                remaining -= travel / speed;
                res.Hit = true; res.Normal = hit.Normal;
                float vn = Vector3.Dot(vel, hit.Normal);
                res.ImpactSpeed = -vn;
                vel -= 2f * vn * hit.Normal;
                vel *= Elasticity;
                if (hit.Normal.Y > 0.7f)
                {
                    res.HitFloor = true;
                    if (vel.Length() < 0.5f) { vel = Vector3.Zero; res.AtRest = true; pos = hit.Point + hit.Normal * Radius; break; }
                }
            }
            return res;
        }
    }
}
