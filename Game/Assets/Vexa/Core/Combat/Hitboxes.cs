using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vexa.Core
{
    public enum HitGroup : byte { None = 0, Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

    /// <summary>Everything needed to place a player's hitboxes at one moment in time.</summary>
    public struct HitPose
    {
        public Vector3 Position;
        public float Yaw;
        public float DuckAmount;
        public bool Alive;

        public static HitPose From(in PlayerState s) => new HitPose { Position = s.Position, Yaw = s.Yaw, DuckAmount = s.DuckAmount, Alive = s.Alive };

        public static HitPose Lerp(in HitPose a, in HitPose b, float t) => new HitPose
        {
            Position = Vector3.Lerp(a.Position, b.Position, t),
            Yaw = a.Yaw + VMath.AngleDelta(a.Yaw, b.Yaw) * t,
            DuckAmount = VMath.Lerp(a.DuckAmount, b.DuckAmount, t),
            Alive = t < 0.5f ? a.Alive : b.Alive,
        };
    }

    /// <summary>Capsule/sphere hitboxes that follow the player's pose (crouch scales them down).</summary>
    public static class Hitboxes
    {
        const float HU = VMath.HU;

        public struct Capsule { public Vector3 A, B; public float R; public HitGroup Group; }

        public static void Build(in HitPose p, List<Capsule> outList)
        {
            outList.Clear();
            float s = VMath.Lerp(1f, SimConstants.DuckHeight / SimConstants.StandHeight, p.DuckAmount);
            var f = VMath.FlatForward(p.Yaw);
            var r = VMath.FlatRight(p.Yaw);
            var o = p.Position;
            float eye = VMath.Lerp(SimConstants.StandEye, SimConstants.DuckEye, p.DuckAmount);
            Vector3 Y(float hu) => new Vector3(0, hu * HU * s, 0);
            var head = o + new Vector3(0, eye + 2.5f * HU, 0) + f * (1.5f * HU);
            outList.Add(new Capsule { A = head, B = head, R = 6f * HU, Group = HitGroup.Head });
            outList.Add(new Capsule { A = o + Y(45), B = o + Y(57), R = 9.5f * HU, Group = HitGroup.Chest });
            outList.Add(new Capsule { A = o + Y(31), B = o + Y(41), R = 8.8f * HU, Group = HitGroup.Stomach });
            outList.Add(new Capsule { A = o + Y(28) - r * (4.4f * HU), B = o + Y(3) - r * (4.4f * HU), R = 5.4f * HU, Group = HitGroup.LeftLeg });
            outList.Add(new Capsule { A = o + Y(28) + r * (4.4f * HU), B = o + Y(3) + r * (4.4f * HU), R = 5.4f * HU, Group = HitGroup.RightLeg });
            outList.Add(new Capsule { A = o + Y(54) - r * (9f * HU), B = o + Y(47) + f * (14f * HU) - r * (3f * HU), R = 3.6f * HU, Group = HitGroup.LeftArm });
            outList.Add(new Capsule { A = o + Y(54) + r * (9f * HU), B = o + Y(47) + f * (14f * HU) + r * (2f * HU), R = 3.6f * HU, Group = HitGroup.RightArm });
        }

        [ThreadStatic] static List<Capsule> _tmp;

        /// <summary>Ray (normalized dir) against the pose's hitboxes. Head wins ties.</summary>
        public static bool Raycast(in HitPose p, Vector3 o, Vector3 d, float maxDist, out float t, out HitGroup group)
        {
            t = maxDist; group = HitGroup.None;
            if (!p.Alive) return false;
            // broad phase: vertical bounding cylinder
            var c = p.Position + new Vector3(0, 0.95f, 0);
            var oc = c - o;
            float tc = Vector3.Dot(oc, d);
            if (tc < -1.2f || tc > maxDist + 1.2f) return false;
            var perp = oc - d * tc;
            if (perp.X * perp.X + perp.Z * perp.Z + perp.Y * perp.Y * 0.35f > 1.6f * 1.6f) return false;

            var list = _tmp ?? (_tmp = new List<Capsule>(8));
            Build(p, list);
            bool hit = false;
            foreach (var cap in list)
            {
                float ti = IntersectCapsule(o, d, cap.A, cap.B, cap.R);
                if (ti < 0 || ti > maxDist) continue;
                if (!hit || ti < t - (cap.Group == HitGroup.Head ? 0.05f : 0f))
                {
                    t = ti; group = cap.Group; hit = true;
                }
            }
            return hit;
        }

        /// <summary>Ray-capsule intersection (distance along ray, or -1).</summary>
        public static float IntersectCapsule(Vector3 ro, Vector3 rd, Vector3 pa, Vector3 pb, float r)
        {
            Vector3 ba = pb - pa, oa = ro - pa;
            float baba = Vector3.Dot(ba, ba);
            if (baba < 1e-10f)
            {
                // sphere
                float b0 = Vector3.Dot(oa, rd), c0 = Vector3.Dot(oa, oa) - r * r, h0 = b0 * b0 - c0;
                if (h0 < 0) return -1;
                float t0 = -b0 - MathF.Sqrt(h0);
                return t0 >= 0 ? t0 : (c0 < 0 ? 0 : -1);
            }
            float bard = Vector3.Dot(ba, rd), baoa = Vector3.Dot(ba, oa), rdoa = Vector3.Dot(rd, oa), oaoa = Vector3.Dot(oa, oa);
            float a = baba - bard * bard;
            float b = baba * rdoa - baoa * bard;
            float c = baba * oaoa - baoa * baoa - r * r * baba;
            float h = b * b - a * c;
            if (h >= 0 && MathF.Abs(a) > 1e-10f)
            {
                float t = (-b - MathF.Sqrt(h)) / a;
                float y = baoa + t * bard;
                if (y > 0 && y < baba && t >= 0) return t;
                Vector3 oc = y <= 0 ? oa : ro - pb;
                float bb = Vector3.Dot(rd, oc), cc = Vector3.Dot(oc, oc) - r * r, hh = bb * bb - cc;
                if (hh > 0) { float tt = -bb - MathF.Sqrt(hh); if (tt >= 0) return tt; }
                return -1;
            }
            // ray parallel to the axis: test both caps
            float best = -1;
            foreach (var cen in new[] { pa, pb })
            {
                var o2 = ro - cen;
                float bb = Vector3.Dot(o2, rd), cc = Vector3.Dot(o2, o2) - r * r, hh = bb * bb - cc;
                if (hh < 0) continue;
                float tt = -bb - MathF.Sqrt(hh);
                if (tt >= 0 && (best < 0 || tt < best)) best = tt;
            }
            return best;
        }

        public static float Multiplier(HitGroup g, WeaponDef def)
        {
            switch (g)
            {
                case HitGroup.Head: return def.HeadshotMultiplier;
                case HitGroup.Stomach: return 1.25f;
                case HitGroup.LeftLeg:
                case HitGroup.RightLeg: return 0.75f;
                default: return 1f;
            }
        }

        public static bool ArmorProtects(HitGroup g, bool helmet)
        {
            switch (g)
            {
                case HitGroup.Head: return helmet;
                case HitGroup.LeftLeg:
                case HitGroup.RightLeg: return false;
                default: return true;
            }
        }
    }
}
