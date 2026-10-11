using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vexa.Core;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Xunit;
using Xunit.Abstractions;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class FunPhysicsTests
    {
        private readonly ITestOutputHelper _out;
        public FunPhysicsTests(ITestOutputHelper o) { _out = o; }

        const float Dt = 1f / 64;

        /// <summary>Steps players the way the server does: others block (except riders on your head), then the stack pass.</summary>
        sealed class Crowd
        {
            public readonly CollisionWorld W;
            public readonly PlayerState[] S;
            public readonly int[] Ids;
            readonly SimContext _ctx;
            int _tick;
            public Crowd(CollisionWorld w, params PlayerState[] players)
            {
                W = w; S = players; Ids = Enumerable.Range(1, players.Length).ToArray();
                _ctx = new SimContext { World = w, Dt = Dt };
            }
            public void Step(Func<int, PlayerInput> cmdFor)
            {
                _tick++;
                for (int i = 0; i < S.Length; i++)
                {
                    _ctx.PlayerId = Ids[i];
                    _ctx.Obstacles.Clear();
                    for (int j = 0; j < S.Length; j++)
                        if (j != i && S[j].Alive && !SimContext.Rides(S[j], Ids[i])) _ctx.Obstacles.Add(SimContext.HullOf(S[j], Ids[j]));
                    var c = cmdFor(i); c.Tick = _tick;
                    PlayerSimulation.Step(ref S[i], c, _ctx);
                }
                StackSolver.Resolve(S, Ids, S.Length, W);
            }
        }

        static Crowd Tower(int n)
        {
            var ps = new PlayerState[n];
            for (int i = 0; i < n; i++)
            {
                ps[i] = Player(new Vector3(0, i * SimConstants.StandHeight + i * 0.002f, 0));
                ps[i].OnGround = i == 0;
            }
            return new Crowd(Flat(), ps);
        }

        [Fact]
        public void FivePlayersCanStandOnEachOther()
        {
            var c = Tower(5);
            for (int t = 0; t < 64 * 2; t++) c.Step(_ => Cmd(0, Buttons.None));
            for (int i = 1; i < 5; i++)
            {
                Assert.True(c.S[i].OnGround, $"player {i} should stand");
                Assert.Equal(BodyKind.Player, c.S[i].GroundKind);
                Assert.Equal(c.Ids[i - 1], c.S[i].GroundOwner);
                Assert.InRange(c.S[i].Position.Y, i * SimConstants.StandHeight - 0.01f, i * SimConstants.StandHeight + 0.05f);
            }
        }

        [Fact]
        public void TowerIsCarriedByThePlayerAtTheBottom()
        {
            var c = Tower(5);
            for (int t = 0; t < 32; t++) c.Step(_ => Cmd(0, Buttons.None));
            for (int t = 0; t < 64 * 2; t++) c.Step(i => Cmd(0, i == 0 ? Buttons.Forward : Buttons.None)); // bottom walks +Z
            _out.WriteLine(string.Join(" ", c.S.Select(s => $"({s.Position.Z:0.00},{s.Position.Y:0.00})")));
            Assert.True(c.S[0].Position.Z > 3f, "the bottom player walks");
            for (int i = 1; i < 5; i++)
            {
                Assert.True(MathF.Abs(c.S[i].Position.Z - c.S[0].Position.Z) < SimConstants.HullHalfWidth, $"player {i} rode along");
                Assert.True(c.S[i].Position.Y > (i - 0.1f) * SimConstants.StandHeight, $"player {i} still up the tower");
            }
        }

        [Fact]
        public void JumpingCarrierLaunchesTheRiderHigherThanAJump()
        {
            var c = Tower(2);
            for (int t = 0; t < 32; t++) c.Step(_ => Cmd(0, Buttons.None));
            float start = c.S[1].Position.Y, peak = start;
            bool boosted = false;
            for (int t = 0; t < 64; t++)
            {
                c.Step(i => Cmd(0, i == 0 && t == 0 ? Buttons.Jump : Buttons.None));
                peak = MathF.Max(peak, c.S[1].Position.Y);
                boosted |= !c.S[1].OnGround;
            }
            float jumpHeight = SimConstants.JumpImpulse * SimConstants.JumpImpulse / (2 * SimConstants.Gravity);
            _out.WriteLine($"rider rose {peak - start:0.00} m (a jump: {jumpHeight:0.00} m)");
            Assert.True(boosted);
            Assert.True(peak - start > jumpHeight * 1.4f, "boost should throw the rider higher than a plain jump");
        }

        [Fact]
        public void StandingUpLiftsThePlayerOnYourHead()
        {
            var below = Player(new Vector3(0, 0, 0));
            below.Ducked = true; below.DuckAmount = 1;
            var top = Player(new Vector3(0, SimConstants.DuckHeight + 0.002f, 0));
            var c = new Crowd(Flat(), below, top);
            for (int t = 0; t < 32; t++) c.Step(i => Cmd(0, i == 0 ? Buttons.Duck : Buttons.None));
            Assert.InRange(c.S[1].Position.Y, SimConstants.DuckHeight - 0.01f, SimConstants.DuckHeight + 0.05f);
            for (int t = 0; t < 32; t++) c.Step(_ => Cmd(0, Buttons.None)); // stand up
            Assert.False(c.S[0].Ducked, "the carrier can stand up with someone on its head");
            Assert.InRange(c.S[1].Position.Y, SimConstants.StandHeight - 0.01f, SimConstants.StandHeight + 0.05f);
            Assert.True(c.S[1].OnGround);
        }

        [Fact]
        public void ExplosionThrowsPlayersIntoTheAir()
        {
            var s = Player(new Vector3(0, 0, 0));
            var ctx = new SimContext { World = Flat(), Dt = Dt };
            Knockback.Apply(ref s, Knockback.Explosion(new Vector3(-1f, 0.1f, 0), s.Position + new Vector3(0, 0.9f, 0), 8.9f, Knockback.HeStrength));
            Assert.False(s.OnGround);
            float peak = 0;
            for (int t = 1; t < 64 * 2; t++) { PlayerSimulation.Step(ref s, Cmd(t, Buttons.None), ctx); peak = MathF.Max(peak, s.Position.Y); }
            _out.WriteLine($"flew {s.Position.X:0.00} m sideways, {peak:0.00} m up");
            Assert.True(peak > 1f && s.Position.X > 2f, "a close HE throws the player up and away");
            Assert.True(s.OnGround, "and they land again");
        }

        [Fact]
        public void CratesFallSettleAndStack()
        {
            var w = Flat();
            var pw = new PropWorld();
            var a = pw.Add(PropKind.Crate, new Vector3(0, 1.5f, 0), 0);
            var b = pw.Add(PropKind.Crate, new Vector3(0.2f, 3f, 0), 0);
            pw.Wake(a); pw.Wake(b);
            for (int t = 0; t < 64 * 3; t++) pw.Step(Dt, w, null);
            Assert.InRange(a.Bottom.Y, -0.01f, 0.03f);
            Assert.InRange(b.Bottom.Y, 0.89f, 0.95f);     // on top of the first one
            Assert.True(a.Asleep && b.Asleep, "settled props go to sleep");
            Assert.Empty(w.Dynamic);
        }

        [Fact]
        public void PlayerPushesACrateAndCanClimbIt()
        {
            var w = Flat();
            var pw = new PropWorld();
            var crate = pw.Add(PropKind.Crate, new Vector3(0, 0, 2f), 0);
            var s = Player(new Vector3(0, 0, 0));
            var ctx = new SimContext { World = w, Dt = Dt };
            for (int t = 1; t <= 64 * 2; t++)
            {
                ctx.Obstacles.Clear(); pw.AddObstacles(ctx.Obstacles);
                PlayerSimulation.Step(ref s, Cmd(t, Buttons.Forward), ctx);
                foreach (var touch in ctx.Touches)
                    if (touch.Kind == BodyKind.Prop) pw.Push(pw.Get(touch.Owner), -touch.Normal, Vector3.Dot(touch.Velocity, -touch.Normal), 1);
                pw.Step(Dt, w, new List<StaticBox> { SimContext.HullOf(s, 1) });
            }
            _out.WriteLine($"crate z {crate.Position.Z:0.00}, player z {s.Position.Z:0.00}");
            Assert.True(crate.Position.Z > 4f, "walking into the crate pushes it");
            Assert.True(crate.Position.Z - s.Position.Z > 0.7f, "the crate stays in front of the player");

            // jump on top of it
            crate.Velocity = Vector3.Zero;
            for (int t = 0; t < 40; t++) pw.Step(Dt, w, null);
            var top = Player(new Vector3(crate.Position.X, 1.5f, crate.Position.Z));
            top.OnGround = false;
            for (int t = 1; t <= 64; t++) { ctx.Obstacles.Clear(); pw.AddObstacles(ctx.Obstacles); PlayerSimulation.Step(ref top, Cmd(t, Buttons.None), ctx); }
            Assert.True(top.OnGround && top.GroundKind == BodyKind.Prop);
            Assert.InRange(top.Position.Y, 0.89f, 0.95f);
        }

        [Fact]
        public void PlayerOnACrateRidesItWhenItSlides()
        {
            var w = Flat();
            var pw = new PropWorld();
            var crate = pw.Add(PropKind.Crate, Vector3.Zero, 0);
            var s = Player(new Vector3(0, 0.9f + 0.002f, 0));
            var ctx = new SimContext { World = w, Dt = Dt };
            for (int t = 1; t <= 8; t++) { ctx.Obstacles.Clear(); pw.AddObstacles(ctx.Obstacles); PlayerSimulation.Step(ref s, Cmd(t, Buttons.None), ctx); }
            pw.Impulse(crate, new Vector3(4f, 0, 0));
            for (int t = 9; t <= 40; t++)
            {
                pw.Step(Dt, w, null);
                ctx.Obstacles.Clear(); pw.AddObstacles(ctx.Obstacles);
                PlayerSimulation.Step(ref s, Cmd(t, Buttons.None), ctx);
            }
            Assert.True(crate.Position.X > 0.8f);
            Assert.True(MathF.Abs(s.Position.X - crate.Position.X) < 0.25f, $"player rides the crate ({s.Position.X:0.00} vs {crate.Position.X:0.00})");
        }

        [Fact]
        public void GrenadesBounceOffProps()
        {
            var w = Flat();
            var pw = new PropWorld();
            pw.Add(PropKind.Crate, new Vector3(0, 0, 3f), 0);
            var pos = new Vector3(0, 0.5f, 0); var vel = new Vector3(0, 0, 8f);
            pw.AddObstacles(w.Dynamic);
            for (int t = 0; t < 32; t++) GrenadePhysics.Step(ref pos, ref vel, Dt, w);
            w.Dynamic.Clear();
            Assert.True(pos.Z < 2.6f, $"the grenade bounced back off the crate (z {pos.Z:0.00})");
        }

        [Fact]
        public void MapPropsRoundTrip()
        {
            var m = MapData.Parse("name t\nbox -5 -1 -5 5 0 5 concrete\nprop crate 1 0 2 45\nprop redbarrel -1 0 0 0\n");
            Assert.Equal(2, m.Props.Count);
            var again = MapData.Parse(m.Serialize());
            Assert.Equal(PropKind.RedBarrel, again.Props[1].Kind);
            Assert.Equal(45f, again.Props[0].Yaw);
        }

        [Fact]
        public void ShootingARedBarrelBlowsItUpAndThrowsPeopleAround()
        {
            var map = MapData.Parse("name t\nbox -30 -1 -30 30 0 30 concrete\nspawn DM 0 0 0 0\nspawn DM 5 0 0 0\nprop redbarrel 0 0 5 0\nprop crate 2 0 5 0\n");
            var cfg = MatchConfig.Deathmatch(); cfg.FillBots = false;
            var g = new ServerGame(new LoopbackNetwork().Server, map, 64, cfg);
            var shooter = g.AddBot("a", Team.None, brain: false);
            var near = g.AddBot("b", Team.None, brain: false);
            for (int t = 0; t < 64 * 3; t++) g.Step();
            shooter.State.Position = new Vector3(0, 0, -5); near.State.Position = new Vector3(-1.5f, 0, 5);
            near.State.Health = 100;
            var barrel = g.PropWorld.Props.First(p => p.Kind == PropKind.RedBarrel);
            var crate = g.PropWorld.Props.First(p => p.Kind == PropKind.Crate);
            for (int i = 0; i < 4 && barrel.FuseTick < 0; i++) g.ShootProp(barrel, shooter, new Vector3(0, 0, 1), 36f, barrel.Position);
            Assert.True(barrel.FuseTick >= 0, "enough bullets set the barrel off");
            for (int t = 0; t < 32; t++) g.Step();
            Assert.True(barrel.Destroyed);
            Assert.True(near.State.Health < 100 || !near.State.Alive, "the blast hurts players nearby");
            Assert.True(crate.Velocity.LengthSquared() > 0.5f || crate.Position.X > 2.3f, "and throws the crate");
        }
        [Theory]
        [InlineData("kasaba")]
        [InlineData("training")]
        public void MapPropsStartAtRestInFreeSpace(string name)
        {
            var map = LoadMap(name);
            Assert.NotEmpty(map.Props);
            var w = map.BuildCollision();
            var pw = new PropWorld();
            foreach (var mp in map.Props) pw.Add(mp.Kind, mp.Position, mp.Yaw);
            foreach (var p in pw.Props) { Assert.False(w.OverlapBox(p.Position, p.Half - new Vector3(0.01f)), $"prop {p.Id} inside a wall"); pw.Wake(p); }
            for (int t = 0; t < 64 * 3; t++) pw.Step(Dt, w, null);
            foreach (var p in pw.Props)
            {
                Assert.True(p.Asleep, $"prop {p.Id} still moving");
                Assert.True(Vector3.Distance(p.Position, p.Home) < 0.05f, $"prop {p.Id} ({p.Kind}) moved {Vector3.Distance(p.Position, p.Home):0.00} m");
            }
        }
        [Fact]
        public void BodiesFlyTumbleAndComeToRest()
        {
            var w = Flat((new Vector3(-10, 0, 6), new Vector3(10, 3, 7)));   // a wall 6 m ahead
            var c = Vexa.Core.Client.CorpsePhysics.Start(Vector3.Zero, new Vector3(0, 3f, 9f));
            float maxAngle = 0;
            for (int t = 0; t < 64 * 4; t++) { Vexa.Core.Client.CorpsePhysics.Step(ref c, Dt, w); maxAngle = MathF.Max(maxAngle, MathF.Abs(c.Angle)); }
            Assert.True(c.Resting, "the body settles");
            Assert.InRange(c.Position.Z, 1.5f, 6f - 0.27f);   // thrown, stopped by the wall
            Assert.InRange(c.Position.Y, -0.01f, 0.05f);
            Assert.True(maxAngle > 90f, "it tumbled in the air");
        }
    }
}
