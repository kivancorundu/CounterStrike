namespace Vexa.Core
{
    public sealed class SimContext
    {
        public CollisionWorld World;
        public float Dt;
        public int PlayerId;
        public bool Frozen;
        public IShotSink Shots;
        public ISimEvents Events;
        public MapData Map;
        public bool PlantAllowed = true;
        /// <summary>Hulls of the other players; they block movement (not bullets).</summary>
        public readonly System.Collections.Generic.List<StaticBox> Obstacles = new System.Collections.Generic.List<StaticBox>();

        public static StaticBox HullOf(in PlayerState s) => new StaticBox
        {
            Min = new System.Numerics.Vector3(s.Position.X - SimConstants.HullHalfWidth, s.Position.Y, s.Position.Z - SimConstants.HullHalfWidth),
            Max = new System.Numerics.Vector3(s.Position.X + SimConstants.HullHalfWidth, s.Position.Y + s.HullHeight, s.Position.Z + SimConstants.HullHalfWidth),
            Material = SurfaceMaterial.Flesh,
        };
    }

    /// <summary>
    /// One command of player simulation (movement + weapons). This exact function runs on the client
    /// for prediction and on the server for authority — keeping them identical is what makes
    /// client-side prediction line up.
    /// </summary>
    public static class PlayerSimulation
    {
        public static MoveEvents Step(ref PlayerState s, in PlayerInput cmd, SimContext ctx)
        {
            float time = cmd.Tick * ctx.Dt;
            if (!s.Alive)
            {
                s.AttackHeld = cmd.Has(Buttons.Attack);
                s.Attack2Held = cmd.Has(Buttons.Attack2);
                s.Yaw = cmd.Yaw; s.Pitch = cmd.Pitch;
                return default;
            }
            var def = s.ActiveDef;
            float maxSpeed = s.Zoom > 0 ? def.ScopedSpeed : def.MoveSpeed;
            if (s.Reloading) maxSpeed = def.MoveSpeed;
            MoveEvents ev;
            ctx.World.Dynamic.Clear();
            ctx.World.Dynamic.AddRange(ctx.Obstacles);
            try { ev = PlayerMovement.Simulate(ref s, cmd, ctx.Dt, time, ctx.World, maxSpeed, ctx.Frozen || s.Planting || s.Defusing); }
            finally { ctx.World.Dynamic.Clear(); }
            if (ev.Landed)
            {
                int fall = PlayerMovement.FallDamage(ev.FallSpeed);
                if (fall > 0)
                {
                    s.Health -= (short)fall;
                    if (s.Health <= 0) { s.Health = 0; s.Alive = false; }
                }
            }
            WeaponLogic.Tick(ref s, cmd, time, ctx.Dt, ctx);
            return ev;
        }
    }
}
