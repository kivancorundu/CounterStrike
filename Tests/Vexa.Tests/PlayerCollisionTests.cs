using System.Numerics;
using Vexa.Core;
using Xunit;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class PlayerCollisionTests
    {
        [Fact]
        public void PlayersCannotWalkThroughEachOther()
        {
            var w = Flat();
            var a = Player(new Vector3(0, 0, 0));
            var b = Player(new Vector3(0, 0, 3));
            var ctxA = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
            var ctxB = new SimContext { World = w, Dt = 1f / 64, PlayerId = 2 };
            for (int t = 1; t <= 64 * 3; t++)
            {
                ctxA.Obstacles.Clear(); ctxA.Obstacles.Add(SimContext.HullOf(b));
                PlayerSimulation.Step(ref a, Cmd(t, Buttons.Forward, 0), ctxA);      // walks +Z
                ctxB.Obstacles.Clear(); ctxB.Obstacles.Add(SimContext.HullOf(a));
                PlayerSimulation.Step(ref b, Cmd(t, Buttons.Forward, 180), ctxB);    // walks -Z
            }
            float gap = b.Position.Z - a.Position.Z;
            Assert.True(gap >= 2 * SimConstants.HullHalfWidth - 0.01f, $"players overlap: gap {gap:0.000}");
            Assert.True(gap < 2 * SimConstants.HullHalfWidth + 0.05f, $"players stopped too early: gap {gap:0.000}");
            Assert.Empty(w.Dynamic); // obstacles never leak into later traces
        }

        [Fact]
        public void PlayerCanSlideAlongAnotherPlayer()
        {
            var w = Flat();
            var a = Player(new Vector3(0, 0, 0));
            var blocker = Player(new Vector3(0.2f, 0, 1.5f));
            var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
            ctx.Obstacles.Add(SimContext.HullOf(blocker));
            // walking at an angle into another player slides along them (straight on, you just stop, like CS)
            for (int t = 1; t <= 64 * 2; t++) PlayerSimulation.Step(ref a, Cmd(t, Buttons.Forward, 25), ctx);
            Assert.True(a.Position.Z > 2.5f, $"should slide past the blocker, z = {a.Position.Z:0.00}");
        }

        [Fact]
        public void PlayerCanStandOnAnotherPlayersHead()
        {
            var w = Flat();
            var below = Player(new Vector3(0, 0, 0));
            below.Ducked = true; below.DuckAmount = 1;
            var top = Player(new Vector3(0, SimConstants.DuckHeight + 0.5f, 0));
            top.OnGround = false;
            var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
            ctx.Obstacles.Add(SimContext.HullOf(below));
            for (int t = 1; t <= 64; t++) PlayerSimulation.Step(ref top, Cmd(t, Buttons.None), ctx);
            Assert.True(top.OnGround);
            Assert.InRange(top.Position.Y, SimConstants.DuckHeight - 0.01f, SimConstants.DuckHeight + 0.05f);
        }

        [Fact]
        public void BulletsIgnoreMovementObstacles()
        {
            var w = Flat();
            w.Dynamic.Add(SimContext.HullOf(Player(new Vector3(0, 0, 2))));
            Assert.False(w.Raycast(new Vector3(0, 1, 0), new Vector3(0, 0, 1), 5f, out _));
            w.Dynamic.Clear();
        }
    }
}
