using System;

namespace Vexa.Core
{
    public enum GrenadeType : byte { None = 0, HE, Flash, Smoke, Molotov, Incendiary, Decoy }

    /// <summary>Anything that can be bought. 1..63 are <see cref="WeaponId"/> values.</summary>
    public enum ItemId : byte
    {
        None = 0,
        Vest = 100, VestHelmet = 101, DefuseKit = 102,
        HE = 110, Flash = 111, Smoke = 112, Molotov = 113, Incendiary = 114, Decoy = 115,
    }

    public static class Items
    {
        public const int MaxGrenades = 4;
        public const int VestPrice = 650, VestHelmetPrice = 1000, HelmetOnlyPrice = 350, KitPrice = 400;

        public static ItemId FromWeapon(WeaponId w) => (ItemId)(byte)w;
        public static bool IsWeapon(ItemId i) => (byte)i > 0 && (byte)i < (byte)WeaponId.Grenade && i != (ItemId)(byte)WeaponId.Knife;
        public static WeaponId ToWeapon(ItemId i) => (WeaponId)(byte)i;
        public static bool IsGrenade(ItemId i) => i >= ItemId.HE && i <= ItemId.Decoy;
        public static GrenadeType ToGrenade(ItemId i) => IsGrenade(i) ? (GrenadeType)(i - ItemId.HE + 1) : GrenadeType.None;
        public static ItemId FromGrenade(GrenadeType g) => g == GrenadeType.None ? ItemId.None : (ItemId)((int)ItemId.HE + (int)g - 1);

        public static int GrenadePrice(GrenadeType g)
        {
            switch (g)
            {
                case GrenadeType.HE: return 300;
                case GrenadeType.Flash: return 200;
                case GrenadeType.Smoke: return 300;
                case GrenadeType.Molotov: return 400;
                case GrenadeType.Incendiary: return 500;
                case GrenadeType.Decoy: return 50;
                default: return 0;
            }
        }
        public static int GrenadeMax(GrenadeType g) => g == GrenadeType.Flash ? 2 : 1;
        public static Team GrenadeTeam(GrenadeType g) => g == GrenadeType.Molotov ? Team.T : g == GrenadeType.Incendiary ? Team.CT : Team.None;

        public static string GrenadeName(GrenadeType g)
        {
            switch (g)
            {
                case GrenadeType.HE: return "HE Bombası";
                case GrenadeType.Flash: return "Flaş Bombası";
                case GrenadeType.Smoke: return "Sis Bombası";
                case GrenadeType.Molotov: return "Molotof";
                case GrenadeType.Incendiary: return "Yangın Bombası";
                case GrenadeType.Decoy: return "Dekoy";
                default: return "";
            }
        }

        public static int Price(ItemId i)
        {
            if (IsWeapon(i)) return Weapons.Get(ToWeapon(i)).Price;
            if (IsGrenade(i)) return GrenadePrice(ToGrenade(i));
            switch (i)
            {
                case ItemId.Vest: return VestPrice;
                case ItemId.VestHelmet: return VestHelmetPrice;
                case ItemId.DefuseKit: return KitPrice;
                default: return 0;
            }
        }

        public static Team TeamOf(ItemId i)
        {
            if (IsWeapon(i)) return Weapons.Get(ToWeapon(i)).Team;
            if (IsGrenade(i)) return GrenadeTeam(ToGrenade(i));
            return i == ItemId.DefuseKit ? Team.CT : Team.None;
        }

        public static string Name(ItemId i)
        {
            if (IsWeapon(i)) return Weapons.Get(ToWeapon(i)).Name;
            if (IsGrenade(i)) return GrenadeName(ToGrenade(i));
            switch (i)
            {
                case ItemId.Vest: return "Kevlar Yelek";
                case ItemId.VestHelmet: return "Kevlar + Kask";
                case ItemId.DefuseKit: return "İmha Kiti";
                default: return "";
            }
        }

        /// <summary>The CS2-style buy menu columns. Entries with two ids are (T, CT) variants.</summary>
        public static readonly (string title, (ItemId t, ItemId ct)[] items)[] BuyMenu =
        {
            ("EKİPMAN", new[] { (ItemId.Vest, ItemId.Vest), (ItemId.VestHelmet, ItemId.VestHelmet), (W(WeaponId.Taser), W(WeaponId.Taser)), (ItemId.None, ItemId.DefuseKit) }),
            ("TABANCALAR", new[] { (W(WeaponId.Glock), W(WeaponId.Usp)), (W(WeaponId.Elite), W(WeaponId.Elite)), (W(WeaponId.P250), W(WeaponId.P250)), (W(WeaponId.Tec9), W(WeaponId.FiveSeven)), (W(WeaponId.Cz75), W(WeaponId.Cz75)), (W(WeaponId.Deagle), W(WeaponId.Deagle)), (W(WeaponId.R8), W(WeaponId.R8)) }),
            ("HAFİF MAKİNELİ", new[] { (W(WeaponId.Mac10), W(WeaponId.Mp9)), (W(WeaponId.Mp7), W(WeaponId.Mp7)), (W(WeaponId.Mp5sd), W(WeaponId.Mp5sd)), (W(WeaponId.Ump45), W(WeaponId.Ump45)), (W(WeaponId.P90), W(WeaponId.P90)), (W(WeaponId.Bizon), W(WeaponId.Bizon)) }),
            ("AĞIR", new[] { (W(WeaponId.Nova), W(WeaponId.Nova)), (W(WeaponId.Xm1014), W(WeaponId.Xm1014)), (W(WeaponId.SawedOff), W(WeaponId.Mag7)), (W(WeaponId.M249), W(WeaponId.M249)), (W(WeaponId.Negev), W(WeaponId.Negev)) }),
            ("TÜFEKLER", new[] { (W(WeaponId.Galil), W(WeaponId.Famas)), (W(WeaponId.Ak47), W(WeaponId.M4a4)), (ItemId.None, W(WeaponId.M4a1s)), (W(WeaponId.Ssg08), W(WeaponId.Ssg08)), (W(WeaponId.Sg553), W(WeaponId.Aug)), (W(WeaponId.Awp), W(WeaponId.Awp)), (W(WeaponId.G3sg1), W(WeaponId.Scar20)) }),
            ("BOMBALAR", new[] { (ItemId.HE, ItemId.HE), (ItemId.Flash, ItemId.Flash), (ItemId.Smoke, ItemId.Smoke), (ItemId.Molotov, ItemId.Incendiary), (ItemId.Decoy, ItemId.Decoy) }),
        };

        static ItemId W(WeaponId w) => FromWeapon(w);
    }
}
