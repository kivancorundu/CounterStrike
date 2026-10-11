using System;
using System.Numerics;

namespace Vexa.Core
{
    /// <summary>
    /// Explosions throw players and props around (an HE grenade at your feet is a rocket jump). Fun over realism:
    /// strong, with a lift so even a sideways blast gets you airborne.
    /// </summary>
    public static class Knockback
    {
        public const float HeStrength = 11f;        // m/s at the center of an HE
        public const float BarrelStrength = 14f;    // red barrel
        public const float Lift = 0.45f;            // upward bias added to the blast direction
        public const float MinLaunchSpeed = 3.8f;   // above MaxUpwardGroundSpeed, so a knocked player leaves the ground

        /// <summary>Velocity change for something at <paramref name="target"/> (m/s), zero outside the radius.</summary>
        public static Vector3 Explosion(Vector3 center, Vector3 target, float radius, float strength)
        {
            var d = target - center;
            float dist = d.Length();
            if (dist >= radius) return Vector3.Zero;
            var dir = dist > 1e-3f ? d / dist : new Vector3(0, 1, 0);
            dir.Y += Lift;
            dir = Vector3.Normalize(dir);
            float falloff = 1f - dist / radius;
            return dir * (strength * MathF.Sqrt(falloff));
        }

        /// <summary>Throws a player: adds the velocity and takes them off the ground.</summary>
        public static void Apply(ref PlayerState s, Vector3 dv)
        {
            if (dv.LengthSquared() < 0.25f || !s.Alive) return;
            s.Velocity += dv;
            if (s.Velocity.Y < MinLaunchSpeed) s.Velocity.Y = MathF.Max(s.Velocity.Y, MathF.Min(MinLaunchSpeed, dv.Length()));
            if (s.Velocity.Y > SimConstants.MaxUpwardGroundSpeed)
            {
                s.OnGround = false;
                s.GroundKind = BodyKind.World; s.GroundOwner = 0;
            }
        }
    }
}
