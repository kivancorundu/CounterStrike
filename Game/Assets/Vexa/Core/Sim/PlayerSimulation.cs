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
        /// <summary>Hulls of the other players and the physics props; they block movement (not bullets).</summary>
        public readonly System.Collections.Generic.List<StaticBox> Obstacles = new System.Collections.Generic.List<StaticBox>();
        /// <summary>Moving obstacles the player ran into during the last <see cref="PlayerSimulation.Step"/> (to push props).</summary>
        public readonly System.Collections.Generic.List<Touch> Touches = new System.Collections.Generic.List<Touch>();

        public static StaticBox HullOf(in PlayerState s, int owner = 0) => new StaticBox
        {
            Min = new System.Numerics.Vector3(s.Position.X - SimConstants.HullHalfWidth, s.Position.Y, s.Position.Z - SimConstants.HullHalfWidth),
            Max = new System.Numerics.Vector3(s.Position.X + SimConstants.HullHalfWidth, s.Position.Y + s.HullHeight, s.Position.Z + SimConstants.HullHalfWidth),
            Material = SurfaceMaterial.Flesh,
            Kind = BodyKind.Player,
            Owner = owner,
            Velocity = s.Velocity + s.BaseVelocity,
        };

        /// <summary>True when <paramref name="rider"/> stands on <paramref name="carrierId"/>'s head (it must not block
        /// the carrier from standing up or jumping: the carrier lifts it instead).</summary>
        public static bool Rides(in PlayerState rider, int carrierId) =>
            rider.OnGround && rider.GroundKind == BodyKind.Player && rider.GroundOwner == carrierId;
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
            ctx.World.Touches.Clear();
            ctx.Touches.Clear();
            try { ev = PlayerMovement.Simulate(ref s, cmd, ctx.Dt, time, ctx.World, maxSpeed, ctx.Frozen || s.Planting || s.Defusing); }
            finally
            {
                ctx.Touches.AddRange(ctx.World.Touches);
                ctx.World.Touches.Clear();
                ctx.World.Dynamic.Clear();
            }
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
