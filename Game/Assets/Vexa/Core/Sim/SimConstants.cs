namespace Vexa.Core
{
    /// <summary>Movement tuning (Source / CS values converted from hammer units to meters).</summary>
    public static class SimConstants
    {
        const float HU = VMath.HU;
        public const float Gravity = 800f * HU;
        public const float JumpImpulse = 301.993f * HU;     // ~57 HU jump height
        public const float StepSize = 18f * HU;
        public const float HullHalfWidth = 16f * HU;
        public const float StandHeight = 72f * HU;
        public const float DuckHeight = 54f * HU;
        public const float StandEye = 64f * HU;
        public const float DuckEye = 46f * HU;
        public const float Friction = 5.2f;
        public const float StopSpeed = 80f * HU;
        public const float Accelerate = 5.5f;
        public const float AirAccelerate = 12f;
        public const float AirMaxWishSpeed = 30f * HU;
        public const float WalkMultiplier = 0.52f;
        public const float DuckMultiplier = 0.34f;
        public const float MaxVelocity = 3500f * HU;
        public const float DuckTime = 0.15f;
        public const float UnduckTime = 0.15f;
        public const float GroundNormalMin = 0.7f;
        public const float GroundCheckDistance = 2f * HU;
        public const float MaxUpwardGroundSpeed = 140f * HU; // moving up faster than this = airborne
        public const float StaminaJumpCost = 0.15f;
        public const float StaminaLandCost = 0.08f;
        public const float StaminaRecovery = 0.6f;          // per second
        public const float StaminaSpeedPenalty = 0.4f;      // max ground speed loss from stamina
        public const float VelocityModifierRecovery = 0.4f; // per second (tagging / landing slowdown)
        public const float SafeFallSpeed = 580f * HU;
        public const float FatalFallSpeed = 1024f * HU;
    }
}
