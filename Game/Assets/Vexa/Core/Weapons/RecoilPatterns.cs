using System;
using System.Collections.Generic;

namespace Vexa.Core
{
    /// <summary>
    /// Fixed spray patterns (aim punch in degrees per shot index). Deterministic so every player
    /// can learn them, exactly like CS: the first shots climb vertically, then the spray sways left/right.
    /// </summary>
    public static class RecoilPatterns
    {
        private static readonly Dictionary<WeaponId, (float pitch, float yaw)[]> _cache = new Dictionary<WeaponId, (float, float)[]>();

        public static (float pitch, float yaw)[] Get(WeaponDef def)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(def.Id, out var p)) return p;
                p = Generate(def);
                _cache[def.Id] = p;
                return p;
            }
        }

        public static void Sample(WeaponDef def, float index, out float pitch, out float yaw)
        {
            var pts = Get(def);
            int i0 = (int)MathF.Floor(index);
            float f = index - i0;
            var a = pts[Math.Min(Math.Max(i0, 0), pts.Length - 1)];
            var b = pts[Math.Min(Math.Max(i0 + 1, 0), pts.Length - 1)];
            pitch = a.pitch + (b.pitch - a.pitch) * f;
            yaw = a.yaw + (b.yaw - a.yaw) * f;
        }

        private static (float, float)[] Generate(WeaponDef def)
        {
            var r = def.Recoil;
            int n = Math.Max(def.ClipSize, 1) + 2;
            var rng = new DetRandom(DetRandom.Hash((int)def.Id, 7, 13));
            float J() => rng.NextFloat() - 0.5f;
            int dir0 = r.StartDir != 0 ? r.StartDir : (J() > 0 ? 1 : -1);
            var pts = new (float, float)[n];
            float p = 0, y = 0;
            for (int i = 1; i < n; i++)
            {
                if (i <= r.ClimbShots)
                {
                    p += r.Climb * (i < 3 ? 0.75f : 1.0f + (i / (float)Math.Max(1, r.ClimbShots)) * 0.1f);
                    y += r.Jitter * J() * 2f + (i > r.ClimbShots * 0.6f ? 0.04f * -dir0 : 0f);
                }
                else
                {
                    p += r.Climb * 0.08f * (J() + 1.1f);
                    float k = (i - r.ClimbShots) / MathF.Max(1f, r.Period);
                    float target = dir0 * r.Sway * MathF.Sin(k * MathF.PI) * (1f + 0.15f * MathF.Floor(k));
                    y += (target - y) * 0.55f + r.Jitter * J();
                }
                pts[i] = (p, y);
            }
            return pts;
        }
    }
}
