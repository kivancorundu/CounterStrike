using System;
using System.Numerics;
using Vexa.Core;
using Xunit;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class WeaponTests
    {
        static PlayerState Armed(WeaponId id)
        {
            var s = Player(Vector3.Zero);
            s.Primary = WeaponSlot.Create(id);
            s.Active = WeaponSlotKind.Primary;
            s.DeployEndTime = 0; s.NextAttackTime = 0;
            return s;
        }

        [Theory]
        [InlineData(64)]
        [InlineData(128)]
        public void Ak47FiresTenShotsPerSecond(int tickRate)
        {
            var w = Flat();
            var s = Armed(WeaponId.Ak47);
            var rec = new ShotRecorder();
            int tick = tickRate; // start at t = 1s so deploy timers are in the past
            Run(ref s, w, tickRate, 1.0f, Buttons.Attack, ref tick, sink: rec);
            Assert.InRange(rec.Count, 10, 11);
        }

        [Fact]
        public void MagazineEmptiesAndReloads()
        {
            var w = Flat();
            var s = Armed(WeaponId.Ak47);
            var rec = new ShotRecorder();
            int tick = 64;
            Run(ref s, w, 64, 4f, Buttons.Attack, ref tick, sink: rec);
            Assert.Equal(30, rec.Count);
            Assert.Equal(0, s.Primary.Clip);
            Run(ref s, w, 64, 0.1f, Buttons.Reload, ref tick);
            Assert.True(s.Reloading);
            Run(ref s, w, 64, 2.6f, Buttons.None, ref tick);
            Assert.False(s.Reloading);
            Assert.Equal(30, s.Primary.Clip);
            Assert.Equal(60, s.Primary.Reserve);
        }

        [Fact]
        public void SemiAutoNeedsSeparateClicks()
        {
            var w = Flat();
            var s = Armed(WeaponId.Deagle);
            var rec = new ShotRecorder();
            int tick = 64;
            Run(ref s, w, 64, 1f, Buttons.Attack, ref tick, sink: rec); // held down
            Assert.Equal(1, rec.Count);
        }

        [Fact]
        public void SprayPatternIsDeterministicAndClimbs()
        {
            var w = Flat();
            float[] Spray()
            {
                var s = Armed(WeaponId.Ak47);
                var rec = new ShotRecorder();
                int tick = 64;
                var pitches = new float[10];
                int k = 0;
                Run(ref s, w, 64, 1f, Buttons.Attack, ref tick, sink: new Lambda(sh => { if (k < 10) pitches[k++] = sh.Pitch; }));
                return pitches;
            }
            var a = Spray(); var b = Spray();
            Assert.Equal(a, b);
            Assert.Equal(0f, a[0], 3);          // first bullet goes exactly where you aim
            Assert.True(a[9] > a[3] && a[3] > a[0], "spray climbs");
        }

        [Fact]
        public void SpreadIsSeededSoClientAndServerAgree()
        {
            var shot = new ShotInfo { Yaw = 10, Pitch = 2, Inaccuracy = 40, Spread = 5, Seed = 12345, Pellets = 1 };
            var copy = shot;
            Assert.Equal(shot.PelletDirection(0), copy.PelletDirection(0));
            copy.Seed = 54321;
            Assert.NotEqual(shot.PelletDirection(0), copy.PelletDirection(0));
        }

        [Fact]
        public void MovingMakesShotsInaccurate()
        {
            var def = Weapons.Get(WeaponId.Ak47);
            var still = Armed(WeaponId.Ak47);
            var moving = still; moving.Velocity = new Vector3(def.MoveSpeed, 0, 0);
            Assert.True(WeaponLogic.CurrentInaccuracy(moving, def, 5) > WeaponLogic.CurrentInaccuracy(still, def, 5) * 10);
            // below 34% of max speed you are as accurate as standing still (counter-strafe)
            var slow = still; slow.Velocity = new Vector3(def.MoveSpeed * 0.3f, 0, 0);
            Assert.Equal(WeaponLogic.CurrentInaccuracy(still, def, 5), WeaponLogic.CurrentInaccuracy(slow, def, 5), 3);
        }

        [Theory]
        // weapon, group, armor, helmet, expected health damage (point blank)
        [InlineData(WeaponId.Ak47, HitGroup.Head, 0, false, 144)]
        [InlineData(WeaponId.Ak47, HitGroup.Head, 100, true, 111)]
        [InlineData(WeaponId.Ak47, HitGroup.Chest, 100, true, 27)]
        [InlineData(WeaponId.M4a4, HitGroup.Chest, 100, true, 23)]
        [InlineData(WeaponId.Awp, HitGroup.Chest, 100, true, 112)]
        [InlineData(WeaponId.Awp, HitGroup.LeftLeg, 100, true, 86)]
        [InlineData(WeaponId.Glock, HitGroup.Head, 100, true, 56)]
        [InlineData(WeaponId.Ak47, HitGroup.Stomach, 0, false, 45)]
        public void DamageTableMatchesCs(WeaponId id, HitGroup g, int armor, bool helmet, int expected)
        {
            var r = DamageModel.Compute(Weapons.Get(id), g, 0f, 1f, armor, helmet);
            Assert.Equal(expected, r.Health);
        }

        [Fact]
        public void DamageFallsOffWithRange()
        {
            var def = Weapons.Get(WeaponId.Ak47);
            var near = DamageModel.Compute(def, HitGroup.Chest, 1f, 1f, 0, false).Health;
            var far = DamageModel.Compute(def, HitGroup.Chest, 2000 * HU, 1f, 0, false).Health;
            Assert.True(far < near);
            Assert.Equal((int)MathF.Floor(36 * MathF.Pow(0.98f, 4)), far);
        }

        [Fact]
        public void SwitchingWeaponsHasDeployTime()
        {
            var w = Flat();
            var s = Armed(WeaponId.Awp);
            var rec = new ShotRecorder();
            var ctx = new SimContext { World = w, Dt = 1f / 64, PlayerId = 1, Shots = rec };
            int tick = 64;
            PlayerSimulation.Step(ref s, Cmd(++tick, Buttons.None, sel: WeaponSelect.Secondary), ctx);
            Assert.Equal(WeaponSlotKind.Secondary, s.Active);
            PlayerSimulation.Step(ref s, Cmd(++tick, Buttons.Attack), ctx);
            Assert.Equal(0, rec.Count); // still drawing the pistol
            Run(ref s, w, 64, 1.0f, Buttons.None, ref tick);
            PlayerSimulation.Step(ref s, Cmd(++tick, Buttons.Attack), ctx);
            Assert.Equal(1, rec.Count);
        }

        [Fact]
        public void HitboxesFollowCrouch()
        {
            var pose = new HitPose { Position = Vector3.Zero, Yaw = 0, Alive = true };
            var o = new Vector3(0, 66 * HU, -5);
            Assert.True(Hitboxes.Raycast(pose, o, new Vector3(0, 0, 1), 10, out _, out var g));
            Assert.Equal(HitGroup.Head, g);
            pose.DuckAmount = 1;
            Assert.True(!Hitboxes.Raycast(pose, o, new Vector3(0, 0, 1), 10, out _, out g) || g != HitGroup.Head);
        }

        sealed class Lambda : IShotSink
        {
            readonly Action<ShotInfo> _a;
            public Lambda(Action<ShotInfo> a) { _a = a; }
            public void OnShot(in ShotInfo shot) => _a(shot);
        }
    }
}
