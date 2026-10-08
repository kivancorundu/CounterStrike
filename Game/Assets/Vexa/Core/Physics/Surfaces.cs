namespace Vexa.Core
{
    public enum SurfaceMaterial : byte
    {
        Concrete = 0,
        Plaster = 1,
        Brick = 2,
        Wood = 3,
        Metal = 4,
        Sand = 5,
        Tile = 6,
        Glass = 7,
        Flesh = 8,   // other players (movement obstacles only)
    }

    public static class Surfaces
    {
        /// <summary>Penetration power consumed per hammer unit of thickness.</summary>
        public static float PenetrationCostPerHU(SurfaceMaterial m)
        {
            switch (m)
            {
                case SurfaceMaterial.Wood: return 1.0f;
                case SurfaceMaterial.Glass: return 0.2f;
                case SurfaceMaterial.Metal: return 2.0f;
                case SurfaceMaterial.Concrete: return 2.6f;
                case SurfaceMaterial.Plaster: return 3.4f;
                case SurfaceMaterial.Brick: return 4.0f;
                default: return 99f; // floors / ground
            }
        }

        public static bool TryParse(string s, out SurfaceMaterial m)
        {
            switch (s.ToLowerInvariant())
            {
                case "concrete": m = SurfaceMaterial.Concrete; return true;
                case "plaster": m = SurfaceMaterial.Plaster; return true;
                case "brick": m = SurfaceMaterial.Brick; return true;
                case "wood": m = SurfaceMaterial.Wood; return true;
                case "metal": m = SurfaceMaterial.Metal; return true;
                case "sand": m = SurfaceMaterial.Sand; return true;
                case "tile": m = SurfaceMaterial.Tile; return true;
                case "glass": m = SurfaceMaterial.Glass; return true;
                default: m = SurfaceMaterial.Concrete; return false;
            }
        }
    }
}
