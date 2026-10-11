using System;
using System.Numerics;

namespace Vexa.Core
{
    /// <summary>
    /// Keeps player towers standing. A carrier ignores the players on its head when it moves (so it can stand up or
    /// jump with them on top); afterwards this pass lifts every rider it pushed into back on top of it, and a rider
    /// lifted by a carrier going up fast is launched (boost). Runs bottom-up, so a tower of five resolves in one pass.
    /// Server only: clients get the result through their snapshots.
    /// </summary>
    public static class StackSolver
    {
        /// <summary>Resolves <paramref name="n"/> players (ids[i] owns states[i]). Returns how many were lifted.</summary>
        public static int Resolve(PlayerState[] states, int[] ids, int n, CollisionWorld world)
        {
            Span<int> order = n <= 64 ? stackalloc int[n] : new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            // insertion sort by feet height (few players)
            for (int i = 1; i < n; i++)
            {
                int k = order[i], j = i - 1;
                while (j >= 0 && states[order[j]].Position.Y > states[k].Position.Y) { order[j + 1] = order[j]; j--; }
                order[j + 1] = k;
            }
            const float w2 = 2f * SimConstants.HullHalfWidth - 0.002f;
            int lifted = 0;
            for (int a = 0; a < n; a++)
            {
                int lo = order[a];
                ref var low = ref states[lo];
                if (!low.Alive) continue;
                float top = low.Position.Y + low.HullHeight;
                for (int b = 0; b < n; b++)
                {
                    if (b == lo) continue;
                    ref var up = ref states[b];
                    if (!up.Alive) continue;
                    if (MathF.Abs(up.Position.X - low.Position.X) >= w2 || MathF.Abs(up.Position.Z - low.Position.Z) >= w2) continue;
                    // feet inside the upper part of the lower hull: it was pushed into from below
                    if (up.Position.Y >= top - 0.001f || up.Position.Y <= low.Position.Y + low.HullHeight * 0.4f) continue;
                    var feet = new Vector3(up.Position.X, top + CollisionWorld.SurfaceEpsilon, up.Position.Z);
                    var half = PlayerMovement.HalfExtents(up.Ducked);
                    if (world.OverlapBox(feet + new Vector3(0, half.Y, 0), half)) continue; // no room above: leave it
                    up.Position = feet;
                    if (low.Velocity.Y > SimConstants.BoostMinSpeed)
                    {
                        up.Velocity.Y = MathF.Max(up.Velocity.Y, low.Velocity.Y * SimConstants.BoostScale);
                        up.OnGround = false;
                        up.GroundKind = BodyKind.World; up.GroundOwner = 0;
                    }
                    else
                    {
                        if (up.Velocity.Y < 0) up.Velocity.Y = 0;
                        up.OnGround = true;
                        up.GroundKind = BodyKind.Player; up.GroundOwner = ids[lo];
                    }
                    lifted++;
                }
            }
            return lifted;
        }
    }
}
