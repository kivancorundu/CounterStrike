using System;
using System.Numerics;

namespace Vexa.Core
{
    public struct WeaponSlot
    {
        public WeaponId Id;
        public short Clip;
        public short Reserve;
        public bool Silenced;
        public bool BurstMode;

        public static WeaponSlot Create(WeaponId id)
        {
            var d = Weapons.Get(id);
            return new WeaponSlot { Id = id, Clip = (short)d.ClipSize, Reserve = (short)d.ReserveAmmo, Silenced = d.Silencer };
        }
        public bool IsEmpty => Id == WeaponId.None;
    }

    /// <summary>
    /// Complete simulated state of one player. It is a plain value type so the client can keep a
    /// history of predicted states and the server can send it back for reconciliation.
    /// Times are simulation seconds on the player's own command timeline (cmd tick * dt).
    /// </summary>
    public struct PlayerState
    {
        // --- movement ---
        public Vector3 Position;       // feet
        public Vector3 Velocity;
        public float Yaw, Pitch;
        public bool OnGround, Ducked, JumpHeld;
        public float DuckAmount;
        public float Stamina;
        public float VelocityModifier;
        public float LastLandTime;

        // --- vitals ---
        public bool Alive;
        public Team Team;
        public short Health;
        public short Armor;
        public bool Helmet;

        // --- weapons ---
        public WeaponSlot Primary, Secondary, Melee;
        public WeaponSlotKind Active, LastActive;
        public float NextAttackTime, DeployEndTime, ReloadStartTime, ReloadEndTime;
        public bool Reloading;
        public float RecoilIndex, AccumInaccuracy, LastShotTime;
        public byte Zoom, ZoomResume;
        public float ZoomResumeTime;
        public byte BurstLeft;
        public float NextBurstTime, SilencerEndTime, PrimeStartTime, InspectEndTime;
        public bool AttackHeld, Attack2Held, ReloadHeld, InspectHeld, DropHeld, UseHeld;
        public ushort ShotCounter;     // seeds spread so client & server agree

        // --- utility / objective ---
        public byte NadeHE, NadeFlash, NadeSmoke, NadeFire, NadeDecoy;
        public GrenadeType ActiveGrenade;
        public float PinTime;          // >0 while the pin is pulled
        public float ThrowStrength;
        public float SwitchBackTime;   // after a throw, return to the previous weapon
        public bool HasC4, HasKit;
        public bool Planting, Defusing;
        public float PlantStartTime;
        public float FlashEndTime, FlashFullEndTime;   // set by the server (player timeline)

        public int GrenadeTotal => NadeHE + NadeFlash + NadeSmoke + NadeFire + NadeDecoy;

        public int GrenadeCount(GrenadeType g)
        {
            switch (g)
            {
                case GrenadeType.HE: return NadeHE;
                case GrenadeType.Flash: return NadeFlash;
                case GrenadeType.Smoke: return NadeSmoke;
                case GrenadeType.Molotov:
                case GrenadeType.Incendiary: return NadeFire;
                case GrenadeType.Decoy: return NadeDecoy;
                default: return 0;
            }
        }

        public void AddGrenade(GrenadeType g, int n)
        {
            switch (g)
            {
                case GrenadeType.HE: NadeHE = (byte)Math.Max(0, NadeHE + n); break;
                case GrenadeType.Flash: NadeFlash = (byte)Math.Max(0, NadeFlash + n); break;
                case GrenadeType.Smoke: NadeSmoke = (byte)Math.Max(0, NadeSmoke + n); break;
                case GrenadeType.Molotov:
                case GrenadeType.Incendiary: NadeFire = (byte)Math.Max(0, NadeFire + n); break;
                case GrenadeType.Decoy: NadeDecoy = (byte)Math.Max(0, NadeDecoy + n); break;
            }
        }

        /// <summary>The fire grenade a player holds depends on the side (molotov T / incendiary CT).</summary>
        public GrenadeType FireGrenadeType => Team == Team.CT ? GrenadeType.Incendiary : GrenadeType.Molotov;

        public bool CanTakeGrenade(GrenadeType g) => GrenadeTotal < Items.MaxGrenades && GrenadeCount(g) < Items.GrenadeMax(g);

        public bool HasSlot(WeaponSlotKind k)
        {
            switch (k)
            {
                case WeaponSlotKind.Primary: return !Primary.IsEmpty;
                case WeaponSlotKind.Secondary: return !Secondary.IsEmpty;
                case WeaponSlotKind.Melee: return !Melee.IsEmpty;
                case WeaponSlotKind.Grenade: return GrenadeTotal > 0;
                case WeaponSlotKind.Bomb: return HasC4;
                default: return false;
            }
        }

        public WeaponSlotKind BestSlot => !Primary.IsEmpty ? WeaponSlotKind.Primary : !Secondary.IsEmpty ? WeaponSlotKind.Secondary : WeaponSlotKind.Melee;

        public bool IsBlind(float time) => time < FlashEndTime;

        public float EyeHeight => VMath.Lerp(SimConstants.StandEye, SimConstants.DuckEye, DuckAmount);
        public Vector3 EyePosition => new Vector3(Position.X, Position.Y + EyeHeight, Position.Z);
        public float HullHeight => Ducked ? SimConstants.DuckHeight : SimConstants.StandHeight;

        public WeaponSlot ActiveSlot => GetSlot(Active);

        public WeaponSlot GetSlot(WeaponSlotKind k)
        {
            switch (k)
            {
                case WeaponSlotKind.Primary: return Primary;
                case WeaponSlotKind.Secondary: return Secondary;
                case WeaponSlotKind.Melee: return Melee;
                default: return default;
            }
        }

        public void SetSlot(WeaponSlotKind k, WeaponSlot s)
        {
            switch (k)
            {
                case WeaponSlotKind.Primary: Primary = s; break;
                case WeaponSlotKind.Secondary: Secondary = s; break;
                case WeaponSlotKind.Melee: Melee = s; break;
            }
        }

        public WeaponDef ActiveDef
        {
            get
            {
                if (Active == WeaponSlotKind.Grenade) return Weapons.Get(WeaponId.Grenade);
                if (Active == WeaponSlotKind.Bomb) return Weapons.Get(WeaponId.C4);
                var s = GetSlot(Active);
                return Weapons.Get(s.IsEmpty ? WeaponId.Knife : s.Id);
            }
        }

        /// <summary>The weapon shown in other players' hands.</summary>
        public WeaponId VisibleWeapon
        {
            get
            {
                if (Active == WeaponSlotKind.Grenade) return WeaponId.Grenade;
                if (Active == WeaponSlotKind.Bomb) return WeaponId.C4;
                var s = GetSlot(Active);
                return s.IsEmpty ? WeaponId.Knife : s.Id;
            }
        }

        public static PlayerState Spawn(Team team, Vector3 pos, float yaw)
        {
            var s = new PlayerState
            {
                Position = pos, Yaw = yaw, Alive = true, Team = team, Health = 100, OnGround = true, VelocityModifier = 1f,
                Melee = WeaponSlot.Create(WeaponId.Knife),
                Secondary = WeaponSlot.Create(team == Team.CT ? WeaponId.Usp : WeaponId.Glock),
                Active = WeaponSlotKind.Secondary, LastActive = WeaponSlotKind.Melee,
                LastLandTime = -10, LastShotTime = -10,
            };
            return s;
        }

        /// <summary>Compare two states for prediction errors (position tolerance in meters).</summary>
        public static bool NearlyEqual(in PlayerState a, in PlayerState b, float posTol = 0.002f)
        {
            if (Vector3.DistanceSquared(a.Position, b.Position) > posTol * posTol) return false;
            if (Vector3.DistanceSquared(a.Velocity, b.Velocity) > 0.01f) return false;
            if (a.OnGround != b.OnGround || a.Ducked != b.Ducked || a.Alive != b.Alive) return false;
            if (a.Health != b.Health || a.Armor != b.Armor) return false;
            if (a.Active != b.Active || a.Reloading != b.Reloading) return false;
            if (a.Primary.Id != b.Primary.Id || a.Primary.Clip != b.Primary.Clip || a.Secondary.Id != b.Secondary.Id || a.Secondary.Clip != b.Secondary.Clip) return false;
            if (MathF.Abs(a.NextAttackTime - b.NextAttackTime) > 0.001f) return false;
            if (a.GrenadeTotal != b.GrenadeTotal || a.HasC4 != b.HasC4 || a.Planting != b.Planting || a.Defusing != b.Defusing || a.Team != b.Team) return false;
            if ((a.PinTime > 0) != (b.PinTime > 0)) return false;
            if (MathF.Abs(a.FlashEndTime - b.FlashEndTime) > 0.01f) return false;
            return true;
        }
    }
}
