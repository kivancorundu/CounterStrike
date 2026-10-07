using System;
using System.Numerics;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Xunit;
using Xunit.Abstractions;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class NetcodeTests
    {
        private readonly ITestOutputHelper _out;
        public NetcodeTests(ITestOutputHelper o) { _out = o; }

        sealed class Harness
        {
            public LoopbackNetwork Net;
            public ServerGame Server;
            public ClientGame Client;
            public LoopbackNetwork.Endpoint ClientEnd;
            public MapData Map;
            public double FrameDt = 1.0 / 144; // client render rate

            public Harness(double latency, double jitter, double loss, int tickRate = 64, int seed = 7)
            {
                Map = LoadMap();
                Net = new LoopbackNetwork(seed) { Latency = latency, Jitter = jitter, Loss = loss };
                Server = new ServerGame(Net.Server, Map, tickRate);
                ClientEnd = Net.AddClient();
                Client = new ClientGame(ClientEnd, "tester", _ => Map);
                ClientEnd.Connect();
            }

            public void Run(double seconds, Func<double, PlayerInput> input)
            {
                double t = 0;
                while (t < seconds)
                {
                    Net.Advance(FrameDt);
                    Server.Update(FrameDt);
                    Client.Update(FrameDt, () => input(t));
                    t += FrameDt;
                }
            }

            public ServerGame.Player ServerPlayer => Server.GetPlayer(Client.LocalId);
        }

        static PlayerInput Scripted(double t)
        {
            // a busy input script: run, strafe, jump, crouch, turn, shoot
            var b = Buttons.Forward;
            int phase = (int)(t * 2) % 8;
            if (phase == 1 || phase == 5) b |= Buttons.Right;
            if (phase == 3) b = Buttons.Left | Buttons.Back;
            if (phase == 2 && (t % 0.5) < 0.05) b |= Buttons.Jump;
            if (phase == 6) b |= Buttons.Duck;
            if (phase == 4) b |= Buttons.Walk | Buttons.Attack;
            if (phase == 7 && (t % 0.5) < 0.02) b |= Buttons.Reload;
            var c = new PlayerInput { Buttons = b };
            c.SetAngles((float)(t * 40 % 360), (float)Math.Sin(t) * 10);
            return c;
        }

        [Theory]
        [InlineData(0.020, 0.000, 0.00)]
        [InlineData(0.060, 0.015, 0.00)]
        [InlineData(0.090, 0.020, 0.05)]
        public void PredictionMatchesServerUnderLatencyJitterAndLoss(double latency, double jitter, double loss)
        {
            var h = new Harness(latency, jitter, loss);
            h.Run(12, Scripted);
            // stop and let everything settle
            h.Run(1.0, _ => { var c = new PlayerInput(); c.SetAngles(0, 0); return c; });
            Assert.True(h.Client.Welcomed);
            var sp = h.ServerPlayer;
            Assert.NotNull(sp);
            float err = Vector3.Distance(sp.State.Position, h.Client.Predicted.Position);
            _out.WriteLine($"lat={latency} jit={jitter} loss={loss}: mispredictions={h.Client.Mispredictions} snapshots={h.Client.SnapshotsReceived} finalError={err * 1000:0.00}mm serverCmd={sp.LastCmdTick} clientCmd={h.Client.CmdTick}");
            Assert.True(err < 0.01f, $"final position error {err}");
            Assert.Equal(sp.State.Primary.Clip, h.Client.Predicted.Primary.Clip);
            Assert.Equal(sp.State.Secondary.Clip, h.Client.Predicted.Secondary.Clip);
            if (loss == 0) Assert.True(h.Client.Mispredictions <= 2, $"unexpected mispredictions: {h.Client.Mispredictions}");
            else Assert.True(h.Client.Mispredictions < 40, $"too many mispredictions: {h.Client.Mispredictions}");
        }

        [Fact]
        public void ServerCorrectsAClientThatDisagrees()
        {
            var h = new Harness(0.05, 0, 0);
            h.Run(1, _ => new PlayerInput());
            // server teleports the player (e.g. respawn / admin) – client must converge
            var sp = h.ServerPlayer;
            sp.State.Position = new Vector3(-10, 0, 10);
            h.Run(1, _ => new PlayerInput());
            Assert.True(Vector3.Distance(h.Client.Predicted.Position, new Vector3(-10, 0, 10)) < 0.01f);
            Assert.True(h.Client.Mispredictions >= 1);
        }

        [Fact]
        public void LagCompensationRegistersHitsOnMovingTargets()
        {
            int RunScenario(int maxRewind)
            {
                var h = new Harness(0.05, 0.005, 0); // 100 ms round trip
                h.Server.MaxRewindTicks = maxRewind;
                // target bot strafing left/right in the open
                var bot = h.Server.AddBot("target");
                bot.Bot = null;
                h.Run(0.5, _ => new PlayerInput());
                var me = h.ServerPlayer;
                me.State.Position = new Vector3(0, 0, -15);
                me.State.Secondary = WeaponSlot.Create(WeaponId.Deagle);
                me.State.Active = WeaponSlotKind.Secondary;
                me.State.Armor = 0; me.State.Helmet = false;
                bot.State.Position = new Vector3(0, 0, -5);
                bot.State.Armor = 0; bot.State.Helmet = false;
                int hits = 0;
                h.Server.OnHit += e => { if (e.Attacker == me.Id) hits++; };
                // drive the bot manually: strafing at full knife speed
                int dir = 1;
                double t = 0, lastShot = -1;
                while (t < 6)
                {
                    h.Net.Advance(h.FrameDt);
                    // bot input is generated per server tick by replacing its brain
                    h.Server.Update(h.FrameDt);
                    // aim exactly at where the client SEES the target (interpolated remote pose)
                    // the input is sampled once per simulation tick, at that moment
                    h.Client.Update(h.FrameDt, () =>
                    {
                        var input = new PlayerInput();
                        if (h.Client.TryGetRemotePose(bot.Id, out var pose))
                        {
                            var eye = h.Client.Predicted.EyePosition;
                            var target = pose.Position + new Vector3(0, 50 * HU, 0); // chest
                            VMath.AnglesFromDir(target - eye, out float y, out float p);
                            input.SetAngles(y, p);
                            if (t - lastShot > 0.5 && t > 1) { input.Buttons |= Buttons.Attack; lastShot = t; }
                        }
                        return input;
                    });
                    t += h.FrameDt;
                    if (bot.State.Position.X > 3) dir = -1;
                    if (bot.State.Position.X < -3) dir = 1;
                    bot.State.Position.X += dir * 6.35f * (float)h.FrameDt;
                    bot.State.Health = 100; bot.State.Alive = true;
                }
                return hits;
            }
            int withLagComp = RunScenario((int)(0.25f * 64));
            int without = RunScenario(0);
            _out.WriteLine($"hits with lag compensation: {withLagComp}, without: {without}");
            Assert.True(withLagComp >= 6, $"expected (almost) every shot of the 7-round magazine to hit, got {withLagComp}");
            Assert.True(without < withLagComp / 2, "without rewinding, shots at a moving target should mostly miss");
        }

        [Fact]
        public void SpeedHackIsLimitedByCommandBudget()
        {
            var h = new Harness(0.02, 0, 0);
            h.Run(1, _ => new PlayerInput());
            var sp = h.ServerPlayer;
            int startCmds = sp.CmdsProcessed;
            int startTick = h.Server.Tick;
            // a cheating client generating commands 3x faster than real time
            double t = 0;
            while (t < 3)
            {
                h.Net.Advance(h.FrameDt);
                h.Server.Update(h.FrameDt);
                h.Client.Update(h.FrameDt * 3, () => new PlayerInput { Buttons = Buttons.Forward });
                t += h.FrameDt;
            }
            int simulated = sp.CmdsProcessed - startCmds;
            int serverTicks = h.Server.Tick - startTick;
            _out.WriteLine($"server ticks {serverTicks}, commands sent ~{serverTicks * 3}, simulated {simulated}, rejected {sp.RejectedCmds}");
            Assert.True(simulated <= serverTicks + ServerGame.MaxCmdBudget, "server must never simulate more than one command per tick on average");
        }

        [Fact]
        public void RapidFireBySkippingCommandNumbersIsBlocked()
        {
            var map = LoadMap();
            var net = new LoopbackNetwork(3) { Latency = 0.02 };
            var server = new ServerGame(net.Server, map, 64);
            var cheat = net.AddClient();
            var w = new NetWriter();
            int myId = -1;
            cheat.Received += (peer, data, len) => { if (data[0] == (byte)Msg.Welcome) myId = data[1]; };
            cheat.Connected += _ => { w.Reset(); w.Byte((byte)Msg.Hello); w.UShort(Protocol.Version); w.String("cheater"); cheat.Send(0, w.Data, w.Length, Delivery.ReliableOrdered); };
            cheat.Connect();
            for (int i = 0; i < 30; i++) { net.Advance(1 / 64.0); cheat.Poll(); server.Step(); }
            Assert.True(myId > 0);
            w.Reset(); w.Byte((byte)Msg.Buy); w.Byte((byte)WeaponId.Ak47); cheat.Send(0, w.Data, w.Length, Delivery.ReliableOrdered);
            for (int i = 0; i < 80; i++) { net.Advance(1 / 64.0); cheat.Poll(); server.Step(); }
            var sp = server.GetPlayer(myId);
            int shotsBefore = sp.ShotsFired;
            // send attack commands whose tick numbers jump by 10 every real tick
            int fakeTick = server.Tick;
            for (int i = 0; i < 64 * 2; i++)
            {
                fakeTick += 10;
                var c = new PlayerInput { Tick = fakeTick, Buttons = Buttons.Attack, InterpTick = server.Tick };
                c.SetAngles(0, 0);
                w.Reset(); w.Byte((byte)Msg.Input); w.Byte(1); Protocol.WriteInput(w, c);
                cheat.Send(0, w.Data, w.Length, Delivery.Unreliable);
                net.Advance(1 / 64.0); cheat.Poll(); server.Step();
            }
            int shots = sp.ShotsFired - shotsBefore;
            _out.WriteLine($"rapid-fire attempt over 2 s: {shots} shots (legit max ~21), rejected cmds {sp.RejectedCmds}");
            Assert.True(shots <= 23, $"rapid fire got through: {shots} shots in 2 s with a 600 rpm rifle");
        }

        [Fact]
        public void StateSerializationRoundTrips()
        {
            var s = PlayerState.Spawn(Team.CT, new Vector3(1, 2, 3), 45);
            s.Velocity = new Vector3(4, 5, 6); s.Primary = WeaponSlot.Create(WeaponId.M4a1s); s.Active = WeaponSlotKind.Primary;
            s.RecoilIndex = 3.5f; s.Zoom = 1; s.Health = 77; s.Armor = 50; s.Helmet = true; s.ShotCounter = 999;
            var w = new NetWriter();
            Protocol.WriteState(w, s);
            var r = new NetReader(w.Data, w.Length);
            var back = Protocol.ReadState(r);
            Assert.False(r.Error);
            Assert.True(PlayerState.NearlyEqual(s, back));
            Assert.Equal(s.ShotCounter, back.ShotCounter);
            Assert.Equal(s.RecoilIndex, back.RecoilIndex);
            Assert.Equal(s.Primary.Silenced, back.Primary.Silenced);
            _out.WriteLine($"full player state = {w.Length} bytes");
        }
    }
}
