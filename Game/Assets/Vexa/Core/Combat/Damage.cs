using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vexa.Core
{
    public struct DamageResult
    {
        public int Health;
        public int Armor;
    }

    /// <summary>CS damage model: range falloff, hit group multipliers, armor / helmet.</summary>
    public static class DamageModel
    {
        public static DamageResult Compute(WeaponDef def, HitGroup group, float distanceMeters, float powerMul, int victimArmor, bool victimHelmet)
        {
            float distHU = distanceMeters / VMath.HU;
            float dmg = def.Damage * powerMul * MathF.Pow(def.RangeModifier, distHU / 500f) * Hitboxes.Multiplier(group, def);
            return ApplyArmor(dmg, def.ArmorPen, group, victimArmor, victimHelmet);
        }

        public static DamageResult ApplyArmor(float dmg, float armorPen, HitGroup group, int armor, bool helmet)
        {
            int armorDmg = 0;
            if (armor > 0 && Hitboxes.ArmorProtects(group, helmet))
            {
                float newDmg = dmg * armorPen;
                float aDmg = (dmg - newDmg) * 0.5f;
                if (aDmg > armor) { aDmg = armor; newDmg = dmg - aDmg / 0.5f; }
                armorDmg = (int)MathF.Round(aDmg);
                dmg = newDmg;
            }
            return new DamageResult { Health = Math.Max(1, (int)MathF.Floor(dmg)), Armor = armorDmg };
        }
    }

    /// <summary>
    /// Server-side pose history for lag compensation: when a shot arrives, other players are
    /// rewound to where the shooter actually saw them (the client's interpolation time).
    /// </summary>
    public sealed class PoseHistory
    {
        private const int Size = 128;
        private readonly Dictionary<int, (int tick, HitPose pose)[]> _rings = new Dictionary<int, (int, HitPose)[]>();

        public void Record(int tick, int playerId, in HitPose pose)
        {
            if (!_rings.TryGetValue(playerId, out var ring)) { ring = new (int, HitPose)[Size]; for (int i = 0; i < Size; i++) ring[i].tick = -1; _rings[playerId] = ring; }
            ring[tick % Size] = (tick, pose);
        }

        public void Remove(int playerId) => _rings.Remove(playerId);

        public bool TryGet(int playerId, float tick, out HitPose pose)
        {
            pose = default;
            if (!_rings.TryGetValue(playerId, out var ring)) return false;
            int t0 = (int)MathF.Floor(tick);
            float f = tick - t0;
            var a = ring[((t0 % Size) + Size) % Size];
            var b = ring[(((t0 + 1) % Size) + Size) % Size];
            bool okA = a.tick == t0, okB = b.tick == t0 + 1;
            if (okA && okB) { pose = HitPose.Lerp(a.pose, b.pose, f); return true; }
            if (okA) { pose = a.pose; return true; }
            if (okB) { pose = b.pose; return true; }
            return false;
        }
    }
}
