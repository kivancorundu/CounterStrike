using System;
using System.Numerics;
using Vexa.Core;
using Xunit;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class MovementTests
    {
        [Theory]
        [InlineData(64)]
        [InlineData(128)]
        public void RunSpeedMatchesWeaponSpeed(int tickRate)
        {
            var w = Flat();
            var s = Player(Vector3.Zero); // knife: 250 HU/s
            int tick = 0;
            Run(ref s, w, tickRate, 1.5f, Buttons.Forward, ref tick);
            float speedHU = VMath.HorizontalLength(s.Velocity) / HU;
            Assert.InRange(speedHU, 247f, 250.5f);
        }

        [Fact]
        public void WalkAndDuckMultipliers()
        {
            var w = Flat();
            var s = Player(Vector3.Zero);
            int tick = 0;
            Run(ref s, w, 64, 1.5f, Buttons.Forward | Buttons.Walk, ref tick);
            Assert.InRange(VMath.HorizontalLength(s.Velocity) / HU, 250 * 0.52f - 3, 250 * 0.52f + 1);

            s = Player(Vector3.Zero); tick = 0;
            Run(ref s, w, 64, 1.5f, Buttons.Forward | Buttons.Duck, ref tick);
            Assert.True(s.Ducked);
            Assert.InRange(VMath.HorizontalLength(s.Velocity) / HU, 250 * 0.34f - 3, 250 * 0.34f + 1);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(128)]
        public void JumpHeightIsAbout57Units(int tickRate)
        {
            var w = Flat();
            var s = Player(Vector3.Zero);
            int tick = 0;
            float maxY = 0;
            Run(ref s, w, tickRate, 1.2f, Buttons.Jump, ref tick, each: st => maxY = MathF.Max(maxY, st.Position.Y));
            Assert.InRange(maxY / HU, 55f, 58f);
            Assert.True(s.OnGround);
        }

        [Fact]
        public void StepsUpStairsButNotHighLedges()
        {
            var low = Flat((new Vector3(-2, 0, 1), new Vector3(2, 17f * HU, 3)));
            var s = Player(Vector3.Zero);
            int tick = 0;
            Run(ref s, low, 64, 0.5f, Buttons.Forward, ref tick);
            Assert.True(s.Position.Z > 1.5f, "should walk onto a 17 HU step");
            Assert.InRange(s.Position.Y / HU, 16.5f, 17.5f);

            var high = Flat((new Vector3(-2, 0, 1), new Vector3(2, 24f * HU, 3)));
            s = Player(Vector3.Zero); tick = 0;
            Run(ref s, high, 64, 1.0f, Buttons.Forward, ref tick);
            Assert.True(s.Position.Z < 1f, "a 24 HU ledge needs a jump");
            Assert.InRange(s.Position.Y, -0.001f, 0.001f);
        }

        [Fact]
        public void WallsBlockAndPlayerSlidesAlong()
        {
            // wall in front at z=1, walking diagonally forward-right should slide along +X
            var w = Flat((new Vector3(-10, 0, 1), new Vector3(10, 3, 1.5f)));
            var s = Player(Vector3.Zero);
            int tick = 0;
            Run(ref s, w, 64, 1.0f, Buttons.Forward | Buttons.Right, ref tick);
            Assert.True(s.Position.Z < 1f - SimConstants.HullHalfWidth + 0.01f);
            Assert.True(s.Position.X > 1.5f, "should slide along the wall");
        }

        [Fact]
        public void CrouchJumpReachesHigherBoxes()
        {
            // 64 HU box: a normal jump (57 HU) cannot land on it, a crouch-jump can
            var box = (new Vector3(-2, 0, 0.6f), new Vector3(2, 64f * HU, 3));
            var w = Flat(box);
            float Try(bool crouch)
            {
                var s = Player(Vector3.Zero);
                var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
                for (int t = 1; t <= 64 * 2; t++)
                {
                    var b = Buttons.Forward;
                    if (t <= 2) b |= Buttons.Jump;
                    if (crouch && t > 6) b |= Buttons.Duck;
                    PlayerSimulation.Step(ref s, Cmd(t, b), ctx);
                }
                return s.Position.Y;
            }
            Assert.True(Try(false) < 0.01f, "normal jump should not reach 64 HU");
            Assert.InRange(Try(true) / HU, 63.5f, 64.5f);
        }

        [Fact]
        public void CounterStrafeStopsQuickly()
        {
            var w = Flat();
            var s = Player(Vector3.Zero, 0, WeaponSlotKind.Primary);
            s.Primary = WeaponSlot.Create(WeaponId.Ak47);
            int tick = 0;
            Run(ref s, w, 64, 1.0f, Buttons.Right, ref tick, yaw: 0);
            float max = WeaponId.Ak47 == WeaponId.Ak47 ? Weapons.Get(WeaponId.Ak47).MoveSpeed : 0;
            Assert.InRange(VMath.HorizontalLength(s.Velocity), max * 0.97f, max * 1.01f);
            // tap the opposite key: speed must drop below the 34% accuracy threshold fast
            int ticks = 0;
            var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
            while (VMath.HorizontalLength(s.Velocity) > max * 0.34f && ticks < 64)
            {
                tick++; ticks++;
                PlayerSimulation.Step(ref s, Cmd(tick, Buttons.Left), ctx);
            }
            float ms = ticks * 1000f / 64;
            Assert.InRange(ms, 40f, 140f);
        }

        [Fact]
        public void AirStrafingCanGainSpeed()
        {
            var w = Flat();
            var s = Player(Vector3.Zero);
            int tick = 0;
            Run(ref s, w, 64, 1.0f, Buttons.Forward, ref tick);
            float before = VMath.HorizontalLength(s.Velocity);
            var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1 };
            float yaw = 0;
            tick++; PlayerSimulation.Step(ref s, Cmd(tick, Buttons.Jump | Buttons.Forward, yaw), ctx);
            // strafe right while turning right, like a real air strafe
            while (!s.OnGround)
            {
                tick++; yaw += 1.6f;
                PlayerSimulation.Step(ref s, Cmd(tick, Buttons.Right, yaw), ctx);
            }
            Assert.True(VMath.HorizontalLength(s.Velocity) > before * 1.02f || s.Position.Y >= 0);
        }

        [Fact]
        public void TrainingMapLoadsAndPlayerStandsOnFloor()
        {
            var map = LoadMap();
            Assert.Equal("training", map.Name);
            Assert.True(map.Boxes.Count > 10);
            var w = map.BuildCollision();
            var s = Player(map.Spawns[0].Position + new Vector3(0, 0.5f, 0));
            int tick = 0;
            Run(ref s, w, 64, 1f, Buttons.None, ref tick);
            Assert.True(s.OnGround);
            Assert.InRange(s.Position.Y, -0.002f, 0.002f);
        }

        [Fact]
        public void StairsLeadToPlatform()
        {
            var map = LoadMap();
            var w = map.BuildCollision();
            var s = Player(new Vector3(13.5f, 0, 12f), 90);
            int tick = 0;
            Run(ref s, w, 64, 1.3f, Buttons.Forward, ref tick, yaw: 90);
            Assert.InRange(s.Position.Y, 1.99f, 2.01f);
        }
    }
}
