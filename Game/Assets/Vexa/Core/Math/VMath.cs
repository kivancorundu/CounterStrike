using System;
using System.Numerics;

namespace Vexa.Core
{
    /// <summary>
    /// Shared math helpers. The simulation works in meters with Y up and +Z forward (same as Unity),
    /// yaw 0 = +Z, yaw 90 = +X, pitch positive = looking up. Gameplay tuning values come from
    /// Source-engine "hammer units" (HU) and are converted with <see cref="HU"/>.
    /// </summary>
    public static class VMath
    {
        public const float HU = 0.0254f;                 // 1 hammer unit in meters
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Approach(float v, float target, float delta) =>
            v < target ? MathF.Min(v + delta, target) : MathF.Max(v - delta, target);

        public static Vector3 Forward(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Deg2Rad, p = pitchDeg * Deg2Rad;
            float cp = MathF.Cos(p);
            return new Vector3(MathF.Sin(y) * cp, MathF.Sin(p), MathF.Cos(y) * cp);
        }

        public static Vector3 FlatForward(float yawDeg)
        {
            float y = yawDeg * Deg2Rad;
            return new Vector3(MathF.Sin(y), 0, MathF.Cos(y));
        }

        public static Vector3 FlatRight(float yawDeg)
        {
            float y = yawDeg * Deg2Rad;
            return new Vector3(MathF.Cos(y), 0, -MathF.Sin(y));
        }

        /// <summary>Yaw/pitch (degrees) that look along a direction.</summary>
        public static void AnglesFromDir(Vector3 d, out float yawDeg, out float pitchDeg)
        {
            yawDeg = MathF.Atan2(d.X, d.Z) * Rad2Deg;
            pitchDeg = MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z)) * Rad2Deg;
        }

        public static float NormalizeAngle(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }

        public static float AngleDelta(float from, float to) => NormalizeAngle(to - from);

        // ---- quantization (identical on client & server so predicted input == simulated input) ----
        public static ushort QuantizeYaw(float yawDeg)
        {
            float n = NormalizeAngle(yawDeg);
            if (n < 0) n += 360f;
            return (ushort)((int)MathF.Round(n / 360f * 65536f) & 0xFFFF);
        }
        public static float DequantizeYaw(ushort q) => NormalizeAngle(q * (360f / 65536f));

        public static short QuantizePitch(float pitchDeg) => (short)MathF.Round(Clamp(pitchDeg, -89f, 89f) * 300f);
        public static float DequantizePitch(short q) => q / 300f;

        public static float HorizontalLength(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
    }

    /// <summary>Small deterministic PRNG (xorshift) so spread/recoil jitter match on client and server.</summary>
    public struct DetRandom
    {
        private uint _s;
        public DetRandom(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; Next(); Next(); }
        public static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return h;
            }
        }
        public uint Next()
        {
            uint x = _s;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            _s = x;
            return x;
        }
        public float NextFloat() => (Next() >> 8) * (1f / 16777216f);
        public float Range(float a, float b) => a + (b - a) * NextFloat();
    }
}
