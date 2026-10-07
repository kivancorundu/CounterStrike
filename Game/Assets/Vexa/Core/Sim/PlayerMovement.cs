using System;
using System.Numerics;

namespace Vexa.Core
{
    public struct MoveEvents
    {
        public bool Jumped;
        public bool Landed;
        public float FallSpeed;   // m/s when landing
        public bool SteppedUp;
    }

    /// <summary>
    /// Source-engine style player movement on top of <see cref="CollisionWorld"/>:
    /// friction/acceleration, air strafing, step-up, stair snapping, ducking, crouch-jump.
    /// Pure function of (state, input, world) so client prediction reproduces the server exactly.
    /// </summary>
    public static class PlayerMovement
    {
        static readonly Vector3 Up = new Vector3(0, 1, 0);

        public static Vector3 HalfExtents(bool ducked) =>
            new Vector3(SimConstants.HullHalfWidth, (ducked ? SimConstants.DuckHeight : SimConstants.StandHeight) * 0.5f, SimConstants.HullHalfWidth);
        static Vector3 Center(Vector3 feet, bool ducked) => feet + Up * ((ducked ? SimConstants.DuckHeight : SimConstants.StandHeight) * 0.5f);

        /// <param name="maxSpeed">Run speed from the active weapon (m/s), before walk/duck multipliers.</param>
        /// <param name="frozen">Freeze time / planting: no wish movement, no jumping.</param>
        public static MoveEvents Simulate(ref PlayerState s, in PlayerInput cmd, float dt, float time, CollisionWorld w, float maxSpeed, bool frozen)
        {
            var ev = new MoveEvents();
            s.Yaw = cmd.Yaw;
            s.Pitch = cmd.Pitch;

            // never start a move stuck inside geometry
            var half = HalfExtents(s.Ducked);
            var c = w.Depenetrate(Center(s.Position, s.Ducked), half);
            s.Position = c - Up * half.Y;

            HandleDuck(ref s, cmd, dt, w);

            // ---- wish direction ----
            float fmove = 0, smove = 0;
            if (!frozen)
            {
                if (cmd.Has(Buttons.Forward)) fmove += 1;
                if (cmd.Has(Buttons.Back)) fmove -= 1;
                if (cmd.Has(Buttons.Right)) smove += 1;
                if (cmd.Has(Buttons.Left)) smove -= 1;
            }
            Vector3 wish = VMath.FlatForward(s.Yaw) * fmove + VMath.FlatRight(s.Yaw) * smove;
            float wl = wish.Length();
            if (wl > 1e-5f) wish /= wl;
            float runMax = maxSpeed * s.VelocityModifier * (1f - s.Stamina * SimConstants.StaminaSpeedPenalty);
            float wishSpeed = runMax;
            if (cmd.Has(Buttons.Walk)) wishSpeed *= SimConstants.WalkMultiplier;
            if (s.Ducked && s.OnGround) wishSpeed *= SimConstants.DuckMultiplier;
            if (wl < 1e-5f) wishSpeed = 0;

            // ---- jump (fresh press only, like CS without autobhop) ----
            bool jumpDown = cmd.Has(Buttons.Jump);
            if (jumpDown && !s.JumpHeld && s.OnGround && !frozen)
            {
                s.Velocity.Y = SimConstants.JumpImpulse;
                s.OnGround = false;
                s.Stamina = VMath.Clamp01(s.Stamina + SimConstants.StaminaJumpCost);
                ev.Jumped = true;
            }
            s.JumpHeld = jumpDown;

            if (s.OnGround)
            {
                s.Velocity.Y = 0;
                ApplyFriction(ref s, dt);
                Accelerate(ref s.Velocity, wish, wishSpeed, SimConstants.Accelerate, dt);
                // no bunny-hop speed retained while grounded
                float hs = VMath.HorizontalLength(s.Velocity);
                if (hs > runMax && hs > 1e-4f)
                {
                    float k = runMax / hs;
                    s.Velocity.X *= k; s.Velocity.Z *= k;
                }
                if (VMath.HorizontalLength(s.Velocity) > 1e-4f)
                {
                    StepSlideMove(ref s, dt, w, ref ev);
                    StayOnGround(ref s, w);
                }
            }
            else
            {
                AirAccelerate(ref s.Velocity, wish, wishSpeed, dt);
                s.Velocity.Y -= SimConstants.Gravity * dt * 0.5f;
                ClampVelocity(ref s.Velocity);
                SlideMove(ref s.Position, ref s.Velocity, dt, s.Ducked, w, out _);
                s.Velocity.Y -= SimConstants.Gravity * dt * 0.5f;
            }
            ClampVelocity(ref s.Velocity);

            bool wasGround = s.OnGround;
            float vyBefore = s.Velocity.Y;
            CategorizePosition(ref s, w);
            if (!wasGround && s.OnGround)
            {
                ev.Landed = true;
                ev.FallSpeed = -vyBefore;
                s.LastLandTime = time;
                if (ev.FallSpeed > 300f * VMath.HU)
                {
                    s.Stamina = VMath.Clamp01(s.Stamina + SimConstants.StaminaLandCost);
                    s.VelocityModifier = MathF.Min(s.VelocityModifier, 0.82f);
                }
            }

            s.Stamina = MathF.Max(0f, s.Stamina - SimConstants.StaminaRecovery * dt);
            s.VelocityModifier = MathF.Min(1f, s.VelocityModifier + SimConstants.VelocityModifierRecovery * dt);
            return ev;
        }

        static void HandleDuck(ref PlayerState s, in PlayerInput cmd, float dt, CollisionWorld w)
        {
            bool want = cmd.Has(Buttons.Duck);
            float diff = SimConstants.StandHeight - SimConstants.DuckHeight;
            if (want)
            {
                if (!s.Ducked)
                {
                    if (!s.OnGround)
                    {
                        // crouch-jump: legs are pulled up, the head stays where it is
                        var raised = s.Position + Up * diff;
                        if (!w.OverlapBox(Center(raised, true), HalfExtents(true))) s.Position = raised;
                        s.Ducked = true;
                        s.DuckAmount = 1f;
                    }
                    else
                    {
                        s.DuckAmount = MathF.Min(1f, s.DuckAmount + dt / SimConstants.DuckTime);
                        if (s.DuckAmount >= 1f) s.Ducked = true;
                    }
                }
                else s.DuckAmount = MathF.Min(1f, s.DuckAmount + dt / SimConstants.DuckTime);
                return;
            }
            if (s.Ducked)
            {
                Vector3? target = null;
                if (!s.OnGround)
                {
                    var lowered = s.Position - Up * diff;
                    if (!w.OverlapBox(Center(lowered, false), HalfExtents(false))) target = lowered;
                    else if (!w.OverlapBox(Center(s.Position, false), HalfExtents(false))) target = s.Position;
                    if (target.HasValue)
                    {
                        // keep the head where it was when the feet drop back down
                        s.DuckAmount = 0f;
                    }
                }
                else if (!w.OverlapBox(Center(s.Position, false), HalfExtents(false))) target = s.Position;
                if (target.HasValue) { s.Position = target.Value; s.Ducked = false; }
            }
            if (!s.Ducked) s.DuckAmount = MathF.Max(0f, s.DuckAmount - dt / SimConstants.UnduckTime);
        }

        static void ApplyFriction(ref PlayerState s, float dt)
        {
            float speed = VMath.HorizontalLength(s.Velocity);
            if (speed < 1e-4f) { s.Velocity.X = 0; s.Velocity.Z = 0; return; }
            float control = MathF.Max(speed, SimConstants.StopSpeed);
            float drop = control * SimConstants.Friction * dt;
            float ns = MathF.Max(speed - drop, 0f) / speed;
            s.Velocity.X *= ns; s.Velocity.Z *= ns;
        }

        static void Accelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            if (wishSpeed <= 0) return;
            float cur = vel.X * wishDir.X + vel.Z * wishDir.Z;
            float add = wishSpeed - cur;
            if (add <= 0) return;
            float a = MathF.Min(accel * dt * wishSpeed, add);
            vel.X += a * wishDir.X; vel.Z += a * wishDir.Z;
        }

        static void AirAccelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float dt)
        {
            if (wishSpeed <= 0) return;
            float capped = MathF.Min(wishSpeed, SimConstants.AirMaxWishSpeed);
            float cur = vel.X * wishDir.X + vel.Z * wishDir.Z;
            float add = capped - cur;
            if (add <= 0) return;
            float a = MathF.Min(SimConstants.AirAccelerate * wishSpeed * dt, add);
            vel.X += a * wishDir.X; vel.Z += a * wishDir.Z;
        }

        static void ClampVelocity(ref Vector3 v)
        {
            float m = SimConstants.MaxVelocity;
            v.X = VMath.Clamp(v.X, -m, m); v.Y = VMath.Clamp(v.Y, -m, m); v.Z = VMath.Clamp(v.Z, -m, m);
        }

        static Vector3 Clip(Vector3 v, Vector3 n)
        {
            float back = Vector3.Dot(v, n);
            v -= n * back;
            // nudge away from the plane to avoid re-colliding due to float error
            float adjust = Vector3.Dot(v, n);
            if (adjust < 0f) v -= n * adjust;
            return v;
        }

        /// <summary>Quake/Source multi-plane slide move. Returns true if anything was hit.</summary>
        public static bool SlideMove(ref Vector3 feet, ref Vector3 vel, float dt, bool ducked, CollisionWorld w, out Vector3 lastNormal)
        {
            var half = HalfExtents(ducked);
            Vector3 primal = vel;
            Span<Vector3> planes = stackalloc Vector3[5];
            int np = 0;
            float timeLeft = dt;
            bool blocked = false;
            lastNormal = Vector3.Zero;
            for (int bump = 0; bump < 4; bump++)
            {
                if (vel.LengthSquared() < 1e-12f) break;
                var start = feet + Up * half.Y;
                var end = start + vel * timeLeft;
                var tr = w.TraceBox(start, end, half);
                if (tr.Fraction > 0f) feet = tr.EndCenter - Up * half.Y;
                if (!tr.Hit) break;
                blocked = true;
                lastNormal = tr.Normal;
                timeLeft -= timeLeft * tr.Fraction;
                if (np >= planes.Length) { vel = Vector3.Zero; break; }
                planes[np++] = tr.Normal;

                // find a velocity that satisfies every plane hit so far
                int i;
                Vector3 newVel = vel;
                for (i = 0; i < np; i++)
                {
                    newVel = Clip(vel, planes[i]);
                    int j;
                    for (j = 0; j < np; j++)
                        if (j != i && Vector3.Dot(newVel, planes[j]) < -1e-6f) break;
                    if (j == np) break;
                }
                if (i < np) vel = newVel;
                else
                {
                    if (np != 2) { vel = Vector3.Zero; break; }
                    var dir = Vector3.Cross(planes[0], planes[1]);
                    float dl = dir.Length();
                    if (dl < 1e-6f) { vel = Vector3.Zero; break; }
                    dir /= dl;
                    vel = dir * Vector3.Dot(dir, vel);
                }
                if (Vector3.Dot(vel, primal) <= 0f) { vel = Vector3.Zero; break; }
            }
            return blocked;
        }

        static void StepSlideMove(ref PlayerState s, float dt, CollisionWorld w, ref MoveEvents ev)
        {
            var startPos = s.Position;
            var startVel = s.Velocity;
            var half = HalfExtents(s.Ducked);

            // A: plain slide
            var posA = startPos; var velA = startVel;
            bool blocked = SlideMove(ref posA, ref velA, dt, s.Ducked, w, out _);
            if (!blocked) { s.Position = posA; s.Velocity = velA; return; }

            // B: step up, slide, step back down
            var posB = startPos; var velB = startVel;
            var upTr = w.TraceBox(Center(posB, s.Ducked), Center(posB, s.Ducked) + Up * SimConstants.StepSize, half);
            posB = upTr.EndCenter - Up * half.Y;
            float raised = posB.Y - startPos.Y;
            SlideMove(ref posB, ref velB, dt, s.Ducked, w, out _);
            var downTr = w.TraceBox(Center(posB, s.Ducked), Center(posB, s.Ducked) - Up * (raised + SimConstants.GroundCheckDistance), half);
            bool landedB = downTr.Hit && downTr.Normal.Y >= SimConstants.GroundNormalMin;
            posB = downTr.EndCenter - Up * half.Y;

            float dA = Sq(posA.X - startPos.X) + Sq(posA.Z - startPos.Z);
            float dB = Sq(posB.X - startPos.X) + Sq(posB.Z - startPos.Z);
            if (landedB && dB > dA + 1e-8f)
            {
                s.Position = posB;
                s.Velocity = new Vector3(velB.X, velA.Y, velB.Z);
                if (posB.Y - startPos.Y > 0.01f) ev.SteppedUp = true;
            }
            else { s.Position = posA; s.Velocity = velA; }
        }

        static float Sq(float x) => x * x;

        static void StayOnGround(ref PlayerState s, CollisionWorld w)
        {
            var half = HalfExtents(s.Ducked);
            var c = Center(s.Position, s.Ducked);
            var tr = w.TraceBox(c, c - Up * SimConstants.StepSize, half);
            if (tr.Hit && !tr.StartSolid && tr.Normal.Y >= SimConstants.GroundNormalMin && tr.Fraction > 0f)
                s.Position = tr.EndCenter - Up * half.Y;
        }

        static void CategorizePosition(ref PlayerState s, CollisionWorld w)
        {
            if (s.Velocity.Y > SimConstants.MaxUpwardGroundSpeed) { s.OnGround = false; return; }
            var half = HalfExtents(s.Ducked);
            var c = Center(s.Position, s.Ducked);
            var tr = w.TraceBox(c, c - Up * SimConstants.GroundCheckDistance, half);
            if (tr.Hit && tr.Normal.Y >= SimConstants.GroundNormalMin)
            {
                s.OnGround = true;
                s.Position = tr.EndCenter - Up * half.Y;
                if (s.Velocity.Y < 0) s.Velocity.Y = 0;
            }
            else s.OnGround = false;
        }

        /// <summary>CS fall damage (no damage under 580 HU/s).</summary>
        public static int FallDamage(float fallSpeed)
        {
            if (fallSpeed <= SimConstants.SafeFallSpeed) return 0;
            float k = 100f / (SimConstants.FatalFallSpeed - SimConstants.SafeFallSpeed);
            return (int)MathF.Round((fallSpeed - SimConstants.SafeFallSpeed) * k);
        }
    }
}
