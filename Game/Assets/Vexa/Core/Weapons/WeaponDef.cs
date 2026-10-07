using System;
using System.Collections.Generic;

namespace Vexa.Core
{
    public enum WeaponId : byte
    {
        None = 0,
        // pistols
        Glock, Usp, P250, Elite, Tec9, FiveSeven, Cz75, Deagle, R8,
        // smgs
        Mac10, Mp9, Mp7, Mp5sd, Ump45, P90, Bizon,
        // heavy
        Nova, Xm1014, SawedOff, Mag7, M249, Negev,
        // rifles
        Galil, Famas, Ak47, M4a4, M4a1s, Sg553, Aug, Ssg08, Awp, G3sg1, Scar20,
        // melee / misc
        Knife, Taser,
        Count
    }

    public enum WeaponSlotKind : byte { None = 0, Primary = 1, Secondary = 2, Melee = 3, Grenade = 4, Bomb = 5 }
    public enum WeaponCategory : byte { Pistol, Smg, Shotgun, MachineGun, Rifle, Sniper, Melee, Taser }

    public struct Inaccuracy
    {
        public float Stand, Crouch, Move, Jump, Land, Fire, Recovery, Spread, Scoped, JumpApex; // milliradians, seconds
        public bool HasScoped, HasJumpApex;
    }

    public struct RecoilParams
    {
        public float Climb;      // degrees of aim punch per shot while climbing
        public int ClimbShots;
        public float Sway;       // horizontal sway amplitude (deg) after the climb
        public float Period;     // shots per half sway
        public float Kick;       // cosmetic view kick
        public float Jitter;
        public int StartDir;     // -1 left / +1 right / 0 = seeded
    }

    public sealed class WeaponDef
    {
        public WeaponId Id;
        public string Name;
        public WeaponSlotKind Slot;
        public WeaponCategory Category;
        public Team Team;               // None = both teams
        public int Price;
        public float Damage;
        public float ArmorPen;          // fraction of damage that goes through armor (CS "armor ratio" * 0.5)
        public float RangeModifier = 0.98f;
        public float RangeHU = 8192f;
        public float CycleTime;         // seconds between shots
        public bool Automatic = true;
        public int ClipSize;
        public int ReserveAmmo;
        public float ReloadTime;
        public float MoveSpeedHU = 220f;
        public float ScopedSpeedHU;
        public int KillReward = 300;
        public float Penetration = 150f;
        public int Pellets = 1;
        public float HeadshotMultiplier = 4f;
        public float DeployTime = 1f;
        public float[] ZoomFov;         // horizontal 4:3 fov per zoom level
        public bool Silencer, FixedSilencer;
        public int BurstShots;
        public float BurstInterval, BurstCycle;
        public bool ShellReload;
        public float PrimeTime;         // R8
        public Inaccuracy Inacc;
        public RecoilParams Recoil;

        public bool IsGun => Category != WeaponCategory.Melee && Category != WeaponCategory.Taser;
        public float MoveSpeed => MoveSpeedHU * VMath.HU;
        public float ScopedSpeed => (ScopedSpeedHU > 0 ? ScopedSpeedHU : MoveSpeedHU) * VMath.HU;
        public float Range => RangeHU * VMath.HU;
    }

    /// <summary>All weapon data (CS2 prices, damage, armor penetration, fire rate, magazines, speeds, rewards).</summary>
    public static class Weapons
    {
        private static readonly WeaponDef[] _defs = new WeaponDef[(int)WeaponId.Count];
        public static WeaponDef Get(WeaponId id) => _defs[(int)id];
        public static IEnumerable<WeaponDef> All { get { foreach (var d in _defs) if (d != null) yield return d; } }

        static Inaccuracy I(float stand, float crouch, float move, float jump, float land, float fire, float recovery, float spread)
            => new Inaccuracy { Stand = stand, Crouch = crouch, Move = move, Jump = jump, Land = land, Fire = fire, Recovery = recovery, Spread = spread };
        static RecoilParams R(float climb, int shots, float sway, float period, float kick, float jit, int startDir = 0)
            => new RecoilParams { Climb = climb, ClimbShots = shots, Sway = sway, Period = period, Kick = kick, Jitter = jit, StartDir = startDir };
        static float Rpm(float rpm) => 60f / rpm;

        static void Add(WeaponDef d) => _defs[(int)d.Id] = d;

        static Weapons()
        {
            // ---------------- pistols ----------------
            Add(new WeaponDef { Id = WeaponId.Glock, Name = "Glock-18", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Team = Team.T, Price = 200, Damage = 30, ArmorPen = 0.47f, RangeModifier = 0.85f, CycleTime = Rpm(400), Automatic = false, ClipSize = 20, ReserveAmmo = 120, ReloadTime = 2.27f, MoveSpeedHU = 240, Penetration = 80, DeployTime = 0.9f, BurstShots = 3, BurstInterval = 0.05f, BurstCycle = 0.5f,
                Inacc = I(5.6f, 4.2f, 16, 110, 20, 34, 0.33f, 2), Recoil = R(0.9f, 20, 0.4f, 4, 1, 0.25f) });
            Add(new WeaponDef { Id = WeaponId.Usp, Name = "USP-S", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Team = Team.CT, Price = 200, Damage = 35, ArmorPen = 0.505f, RangeModifier = 0.99f, CycleTime = Rpm(352), Automatic = false, ClipSize = 12, ReserveAmmo = 24, ReloadTime = 2.17f, MoveSpeedHU = 240, Penetration = 80, DeployTime = 0.9f, Silencer = true,
                Inacc = I(3.7f, 2.9f, 13, 100, 20, 32, 0.3f, 1.5f), Recoil = R(1.0f, 20, 0.3f, 4, 1, 0.2f) });
            Add(new WeaponDef { Id = WeaponId.P250, Name = "P250", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Price = 300, Damage = 38, ArmorPen = 0.64f, RangeModifier = 0.85f, CycleTime = Rpm(400), Automatic = false, ClipSize = 13, ReserveAmmo = 26, ReloadTime = 2.2f, MoveSpeedHU = 240, Penetration = 90, DeployTime = 0.9f,
                Inacc = I(6.0f, 4.5f, 16, 110, 20, 52, 0.33f, 2), Recoil = R(1.3f, 20, 0.4f, 4, 1, 0.25f) });
            Add(new WeaponDef { Id = WeaponId.Elite, Name = "Dual Berettas", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Price = 300, Damage = 38, ArmorPen = 0.575f, RangeModifier = 0.79f, CycleTime = Rpm(500), Automatic = false, ClipSize = 30, ReserveAmmo = 120, ReloadTime = 3.6f, MoveSpeedHU = 240, Penetration = 80, DeployTime = 1f,
                Inacc = I(6.5f, 5, 18, 120, 20, 30, 0.35f, 2.5f), Recoil = R(0.9f, 30, 0.5f, 4, 1, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.Tec9, Name = "Tec-9", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Team = Team.T, Price = 500, Damage = 33, ArmorPen = 0.903f, RangeModifier = 0.79f, CycleTime = Rpm(500), Automatic = false, ClipSize = 18, ReserveAmmo = 90, ReloadTime = 2.5f, MoveSpeedHU = 240, Penetration = 100, DeployTime = 0.9f,
                Inacc = I(7.0f, 5.2f, 14, 110, 20, 40, 0.35f, 2.5f), Recoil = R(1.1f, 20, 0.5f, 4, 1, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.FiveSeven, Name = "Five-SeveN", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Team = Team.CT, Price = 500, Damage = 32, ArmorPen = 0.911f, RangeModifier = 0.81f, CycleTime = Rpm(400), Automatic = false, ClipSize = 20, ReserveAmmo = 100, ReloadTime = 2.2f, MoveSpeedHU = 240, Penetration = 100, DeployTime = 0.9f,
                Inacc = I(5.2f, 4.0f, 14, 110, 20, 38, 0.33f, 2), Recoil = R(1.1f, 20, 0.4f, 4, 1, 0.25f) });
            Add(new WeaponDef { Id = WeaponId.Cz75, Name = "CZ75-Auto", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Price = 500, Damage = 31, ArmorPen = 0.776f, RangeModifier = 0.85f, CycleTime = Rpm(600), Automatic = true, ClipSize = 12, ReserveAmmo = 12, ReloadTime = 2.7f, MoveSpeedHU = 240, KillReward = 100, Penetration = 90, DeployTime = 1f,
                Inacc = I(6.5f, 5, 18, 110, 20, 28, 0.4f, 2.5f), Recoil = R(0.6f, 12, 0.7f, 4, 1, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.Deagle, Name = "Desert Eagle", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Price = 700, Damage = 53, ArmorPen = 0.932f, RangeModifier = 0.81f, CycleTime = Rpm(267), Automatic = false, ClipSize = 7, ReserveAmmo = 35, ReloadTime = 2.2f, MoveSpeedHU = 230, Penetration = 200, DeployTime = 1f,
                Inacc = I(3.2f, 2.6f, 70, 160, 40, 105, 0.55f, 1.5f), Recoil = R(2.6f, 7, 0.6f, 3, 1.6f, 0.5f) });
            Add(new WeaponDef { Id = WeaponId.R8, Name = "R8 Revolver", Slot = WeaponSlotKind.Secondary, Category = WeaponCategory.Pistol, Price = 600, Damage = 86, ArmorPen = 0.932f, RangeModifier = 0.98f, CycleTime = Rpm(120), Automatic = false, ClipSize = 8, ReserveAmmo = 8, ReloadTime = 2.3f, MoveSpeedHU = 220, Penetration = 200, DeployTime = 1f, PrimeTime = 0.4f,
                Inacc = I(3.0f, 2.4f, 60, 150, 40, 80, 0.6f, 1.0f), Recoil = R(2.4f, 8, 0.5f, 3, 1.6f, 0.4f) });

            // ---------------- SMGs ----------------
            Add(new WeaponDef { Id = WeaponId.Mac10, Name = "MAC-10", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Team = Team.T, Price = 1050, Damage = 29, ArmorPen = 0.575f, RangeModifier = 0.80f, CycleTime = Rpm(800), ClipSize = 30, ReserveAmmo = 100, ReloadTime = 2.6f, MoveSpeedHU = 240, KillReward = 600, Penetration = 100,
                Inacc = I(10, 8, 24, 110, 30, 6, 0.35f, 1.5f), Recoil = R(0.2f, 9, 1.1f, 6, 0.6f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.Mp9, Name = "MP9", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Team = Team.CT, Price = 1250, Damage = 26, ArmorPen = 0.60f, RangeModifier = 0.87f, CycleTime = Rpm(857), ClipSize = 30, ReserveAmmo = 120, ReloadTime = 2.1f, MoveSpeedHU = 240, KillReward = 600, Penetration = 100,
                Inacc = I(9, 7, 22, 110, 30, 6, 0.33f, 1.5f), Recoil = R(0.19f, 9, 1.0f, 6, 0.6f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.Mp7, Name = "MP7", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Price = 1500, Damage = 29, ArmorPen = 0.625f, RangeModifier = 0.85f, CycleTime = Rpm(750), ClipSize = 30, ReserveAmmo = 120, ReloadTime = 3.1f, MoveSpeedHU = 220, KillReward = 600, Penetration = 100,
                Inacc = I(7, 5.5f, 28, 110, 30, 5.5f, 0.33f, 1.2f), Recoil = R(0.17f, 10, 0.8f, 6, 0.6f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.Mp5sd, Name = "MP5-SD", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Price = 1500, Damage = 27, ArmorPen = 0.625f, RangeModifier = 0.85f, CycleTime = Rpm(750), ClipSize = 30, ReserveAmmo = 120, ReloadTime = 2.9f, MoveSpeedHU = 235, KillReward = 600, Penetration = 100, Silencer = true, FixedSilencer = true,
                Inacc = I(7, 5.5f, 26, 110, 30, 5.5f, 0.33f, 1.2f), Recoil = R(0.17f, 10, 0.8f, 6, 0.6f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.Ump45, Name = "UMP-45", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Price = 1200, Damage = 35, ArmorPen = 0.65f, RangeModifier = 0.75f, CycleTime = Rpm(666), ClipSize = 25, ReserveAmmo = 100, ReloadTime = 3.5f, MoveSpeedHU = 230, KillReward = 600, Penetration = 100,
                Inacc = I(8, 6.5f, 30, 110, 30, 7, 0.35f, 1.5f), Recoil = R(0.24f, 9, 0.9f, 6, 0.7f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.P90, Name = "P90", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Price = 2350, Damage = 26, ArmorPen = 0.69f, RangeModifier = 0.86f, CycleTime = Rpm(857), ClipSize = 50, ReserveAmmo = 100, ReloadTime = 3.4f, MoveSpeedHU = 230, KillReward = 300, Penetration = 100,
                Inacc = I(8, 6.5f, 25, 110, 30, 4.5f, 0.35f, 1.4f), Recoil = R(0.13f, 12, 0.9f, 8, 0.5f, 0.1f) });
            Add(new WeaponDef { Id = WeaponId.Bizon, Name = "PP-Bizon", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Smg, Price = 1400, Damage = 27, ArmorPen = 0.575f, RangeModifier = 0.80f, CycleTime = Rpm(750), ClipSize = 64, ReserveAmmo = 120, ReloadTime = 2.4f, MoveSpeedHU = 240, KillReward = 600, Penetration = 100,
                Inacc = I(9, 7.5f, 26, 110, 30, 5, 0.35f, 1.8f), Recoil = R(0.14f, 12, 0.9f, 8, 0.5f, 0.1f) });

            // ---------------- heavy ----------------
            Add(new WeaponDef { Id = WeaponId.Nova, Name = "Nova", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Shotgun, Price = 1050, Damage = 26, Pellets = 9, ArmorPen = 0.5f, RangeModifier = 0.70f, RangeHU = 3000, CycleTime = Rpm(68), Automatic = false, ClipSize = 8, ReserveAmmo = 32, ReloadTime = 0.5f, ShellReload = true, MoveSpeedHU = 220, KillReward = 900, Penetration = 50,
                Inacc = I(12, 10, 30, 80, 20, 30, 0.4f, 40), Recoil = R(2.6f, 8, 0.3f, 3, 2, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.Xm1014, Name = "XM1014", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Shotgun, Price = 2000, Damage = 20, Pellets = 6, ArmorPen = 0.8f, RangeModifier = 0.70f, RangeHU = 3000, CycleTime = Rpm(171), Automatic = true, ClipSize = 7, ReserveAmmo = 32, ReloadTime = 0.45f, ShellReload = true, MoveSpeedHU = 215, KillReward = 900, Penetration = 50,
                Inacc = I(14, 11, 30, 80, 20, 20, 0.4f, 38), Recoil = R(1.6f, 8, 0.4f, 3, 1.6f, 0.4f) });
            Add(new WeaponDef { Id = WeaponId.SawedOff, Name = "Sawed-Off", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Shotgun, Team = Team.T, Price = 1100, Damage = 32, Pellets = 8, ArmorPen = 0.75f, RangeModifier = 0.45f, RangeHU = 1400, CycleTime = Rpm(71), Automatic = false, ClipSize = 7, ReserveAmmo = 32, ReloadTime = 0.5f, ShellReload = true, MoveSpeedHU = 210, KillReward = 900, Penetration = 50,
                Inacc = I(18, 15, 30, 80, 20, 30, 0.4f, 60), Recoil = R(2.8f, 8, 0.3f, 3, 2, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.Mag7, Name = "MAG-7", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Shotgun, Team = Team.CT, Price = 1300, Damage = 30, Pellets = 8, ArmorPen = 0.75f, RangeModifier = 0.45f, RangeHU = 1400, CycleTime = Rpm(71), Automatic = false, ClipSize = 5, ReserveAmmo = 32, ReloadTime = 2.4f, MoveSpeedHU = 225, KillReward = 900, Penetration = 50,
                Inacc = I(12, 10, 30, 80, 20, 30, 0.4f, 40), Recoil = R(2.8f, 8, 0.3f, 3, 2, 0.3f) });
            Add(new WeaponDef { Id = WeaponId.M249, Name = "M249", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.MachineGun, Price = 5200, Damage = 32, ArmorPen = 0.8f, RangeModifier = 0.97f, CycleTime = Rpm(750), ClipSize = 100, ReserveAmmo = 200, ReloadTime = 5.7f, MoveSpeedHU = 195, Penetration = 200, DeployTime = 1.2f,
                Inacc = I(9, 7, 160, 260, 60, 4, 0.5f, 2), Recoil = R(0.16f, 12, 1.4f, 9, 0.8f, 0.15f) });
            Add(new WeaponDef { Id = WeaponId.Negev, Name = "Negev", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.MachineGun, Price = 1700, Damage = 35, ArmorPen = 0.71f, RangeModifier = 0.97f, CycleTime = Rpm(800), ClipSize = 150, ReserveAmmo = 300, ReloadTime = 5.7f, MoveSpeedHU = 150, Penetration = 200, DeployTime = 1.2f,
                Inacc = I(10, 8, 170, 260, 60, 3, 0.6f, 2), Recoil = R(0.14f, 10, 1.2f, 9, 0.8f, 0.15f) });

            // ---------------- rifles ----------------
            Add(new WeaponDef { Id = WeaponId.Galil, Name = "Galil AR", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.T, Price = 1800, Damage = 30, ArmorPen = 0.775f, CycleTime = Rpm(666), ClipSize = 35, ReserveAmmo = 90, ReloadTime = 3.0f, MoveSpeedHU = 215, Penetration = 200,
                Inacc = I(6.0f, 4.6f, 130, 250, 60, 7.5f, 0.4f, 0.6f), Recoil = R(0.24f, 9, 0.95f, 7, 1, 0.08f) });
            Add(new WeaponDef { Id = WeaponId.Famas, Name = "FAMAS", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.CT, Price = 2050, Damage = 30, ArmorPen = 0.70f, RangeModifier = 0.96f, CycleTime = Rpm(666), ClipSize = 25, ReserveAmmo = 90, ReloadTime = 3.3f, MoveSpeedHU = 220, Penetration = 200, BurstShots = 3, BurstInterval = 0.075f, BurstCycle = 0.55f,
                Inacc = I(5.5f, 4.2f, 120, 250, 60, 7.5f, 0.4f, 0.6f), Recoil = R(0.22f, 9, 0.8f, 7, 1, 0.08f) });
            Add(new WeaponDef { Id = WeaponId.Ak47, Name = "AK-47", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.T, Price = 2700, Damage = 36, ArmorPen = 0.775f, CycleTime = Rpm(600), ClipSize = 30, ReserveAmmo = 90, ReloadTime = 2.43f, MoveSpeedHU = 215, Penetration = 200,
                Inacc = I(4.8f, 3.6f, 140, 260, 60, 7.8f, 0.38f, 0.6f), Recoil = R(0.32f, 9, 1.25f, 7, 1.2f, 0.07f, -1) });
            Add(new WeaponDef { Id = WeaponId.M4a4, Name = "M4A4", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.CT, Price = 3100, Damage = 33, ArmorPen = 0.70f, RangeModifier = 0.97f, CycleTime = Rpm(666), ClipSize = 30, ReserveAmmo = 90, ReloadTime = 3.07f, MoveSpeedHU = 225, Penetration = 200,
                Inacc = I(4.0f, 3.0f, 115, 250, 60, 7.0f, 0.36f, 0.6f), Recoil = R(0.26f, 10, 0.85f, 7, 1, 0.07f, 1) });
            Add(new WeaponDef { Id = WeaponId.M4a1s, Name = "M4A1-S", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.CT, Price = 2900, Damage = 38, ArmorPen = 0.70f, RangeModifier = 0.99f, CycleTime = Rpm(600), ClipSize = 20, ReserveAmmo = 80, ReloadTime = 3.07f, MoveSpeedHU = 225, Penetration = 200, Silencer = true,
                Inacc = I(3.4f, 2.6f, 105, 250, 60, 7.6f, 0.36f, 0.5f), Recoil = R(0.22f, 9, 0.6f, 6, 0.9f, 0.06f, 1) });
            Add(new WeaponDef { Id = WeaponId.Sg553, Name = "SG 553", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.T, Price = 3000, Damage = 30, ArmorPen = 1.0f, CycleTime = Rpm(545), ClipSize = 30, ReserveAmmo = 90, ReloadTime = 2.8f, MoveSpeedHU = 210, ScopedSpeedHU = 150, Penetration = 200, ZoomFov = new[] { 45f },
                Inacc = new Inaccuracy { Stand = 4.5f, Crouch = 3.4f, Move = 130, Jump = 250, Land = 60, Fire = 7.5f, Recovery = 0.4f, Spread = 0.5f, Scoped = 1.6f, HasScoped = true }, Recoil = R(0.24f, 9, 0.8f, 7, 1, 0.07f) });
            Add(new WeaponDef { Id = WeaponId.Aug, Name = "AUG", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Rifle, Team = Team.CT, Price = 3300, Damage = 28, ArmorPen = 0.90f, CycleTime = Rpm(600), ClipSize = 30, ReserveAmmo = 90, ReloadTime = 3.8f, MoveSpeedHU = 220, ScopedSpeedHU = 150, Penetration = 200, ZoomFov = new[] { 45f },
                Inacc = new Inaccuracy { Stand = 4.0f, Crouch = 3.0f, Move = 120, Jump = 250, Land = 60, Fire = 7.2f, Recovery = 0.4f, Spread = 0.5f, Scoped = 1.5f, HasScoped = true }, Recoil = R(0.22f, 9, 0.75f, 7, 1, 0.07f) });
            Add(new WeaponDef { Id = WeaponId.Ssg08, Name = "SSG 08", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Sniper, Price = 1700, Damage = 88, ArmorPen = 0.85f, CycleTime = Rpm(48), Automatic = false, ClipSize = 10, ReserveAmmo = 90, ReloadTime = 3.7f, MoveSpeedHU = 230, ScopedSpeedHU = 230, Penetration = 250, DeployTime = 1.1f, ZoomFov = new[] { 40f, 15f },
                Inacc = new Inaccuracy { Stand = 22, Crouch = 20, Move = 60, Jump = 150, Land = 50, Fire = 40, Recovery = 0.4f, Spread = 0.3f, Scoped = 1.5f, HasScoped = true, JumpApex = 3, HasJumpApex = true }, Recoil = R(2.2f, 3, 0.2f, 3, 1.5f, 0.2f) });
            Add(new WeaponDef { Id = WeaponId.Awp, Name = "AWP", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Sniper, Price = 4750, Damage = 115, ArmorPen = 0.975f, RangeModifier = 0.99f, CycleTime = Rpm(41), Automatic = false, ClipSize = 5, ReserveAmmo = 30, ReloadTime = 3.6f, MoveSpeedHU = 200, ScopedSpeedHU = 100, KillReward = 100, Penetration = 300, DeployTime = 1.25f, ZoomFov = new[] { 40f, 10f },
                Inacc = new Inaccuracy { Stand = 80, Crouch = 75, Move = 170, Jump = 300, Land = 80, Fire = 60, Recovery = 0.5f, Spread = 0.2f, Scoped = 1.2f, HasScoped = true }, Recoil = R(3.4f, 3, 0.2f, 3, 2, 0.2f) });
            Add(new WeaponDef { Id = WeaponId.G3sg1, Name = "G3SG1", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Sniper, Team = Team.T, Price = 5000, Damage = 80, ArmorPen = 0.825f, CycleTime = Rpm(240), Automatic = true, ClipSize = 20, ReserveAmmo = 90, ReloadTime = 4.7f, MoveSpeedHU = 215, ScopedSpeedHU = 120, Penetration = 250, DeployTime = 1.2f, ZoomFov = new[] { 40f, 15f },
                Inacc = new Inaccuracy { Stand = 30, Crouch = 25, Move = 150, Jump = 280, Land = 70, Fire = 20, Recovery = 0.35f, Spread = 0.3f, Scoped = 1.6f, HasScoped = true }, Recoil = R(0.9f, 6, 0.4f, 4, 1.2f, 0.2f) });
            Add(new WeaponDef { Id = WeaponId.Scar20, Name = "SCAR-20", Slot = WeaponSlotKind.Primary, Category = WeaponCategory.Sniper, Team = Team.CT, Price = 5000, Damage = 80, ArmorPen = 0.825f, CycleTime = Rpm(240), Automatic = true, ClipSize = 20, ReserveAmmo = 90, ReloadTime = 3.1f, MoveSpeedHU = 215, ScopedSpeedHU = 120, Penetration = 250, DeployTime = 1.2f, ZoomFov = new[] { 40f, 15f },
                Inacc = new Inaccuracy { Stand = 30, Crouch = 25, Move = 150, Jump = 280, Land = 70, Fire = 20, Recovery = 0.35f, Spread = 0.3f, Scoped = 1.6f, HasScoped = true }, Recoil = R(0.9f, 6, 0.4f, 4, 1.2f, 0.2f) });

            // ---------------- melee / taser ----------------
            Add(new WeaponDef { Id = WeaponId.Knife, Name = "Bıçak", Slot = WeaponSlotKind.Melee, Category = WeaponCategory.Melee, Price = 0, Damage = 40, ArmorPen = 0.85f, CycleTime = 0.4f, Automatic = true, MoveSpeedHU = 250, KillReward = 1500, DeployTime = 0.6f, RangeHU = 64 });
            Add(new WeaponDef { Id = WeaponId.Taser, Name = "Zeus x27", Slot = WeaponSlotKind.Melee, Category = WeaponCategory.Taser, Price = 200, Damage = 500, ArmorPen = 1f, RangeHU = 183, CycleTime = 2f, Automatic = false, ClipSize = 1, MoveSpeedHU = 220, KillReward = 0, Penetration = 0, DeployTime = 1f,
                Inacc = I(2, 2, 6, 30, 0, 0, 0.3f, 1), Recoil = R(1, 1, 0, 1, 1, 0) });
        }
    }
}
