namespace Vexa.Core
{
    public sealed class SimContext
    {
        public CollisionWorld World;
        public float Dt;
        public int PlayerId;
        public bool Frozen;
        public IShotSink Shots;
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
                return default;
            }
            var def = s.ActiveDef;
            float maxSpeed = s.Zoom > 0 ? def.ScopedSpeed : def.MoveSpeed;
            if (s.Reloading) maxSpeed = def.MoveSpeed;
            var ev = PlayerMovement.Simulate(ref s, cmd, ctx.Dt, time, ctx.World, maxSpeed, ctx.Frozen);
            if (ev.Landed)
            {
                int fall = PlayerMovement.FallDamage(ev.FallSpeed);
                if (fall > 0)
                {
                    s.Health -= (short)fall;
                    if (s.Health <= 0) { s.Health = 0; s.Alive = false; }
                }
            }
            WeaponLogic.Tick(ref s, cmd, time, ctx.Dt, ctx.PlayerId, ctx.Frozen, ctx.Shots);
            return ev;
        }
    }
}
