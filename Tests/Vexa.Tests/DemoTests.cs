using System.IO;
using System.Linq;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Xunit;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class DemoTests
    {
        static MatchConfig SmallMatch()
        {
            var cfg = MatchConfig.Competitive();
            cfg.TeamSize = 2; cfg.WarmupTime = 0.5f; cfg.FreezeTime = 1f; cfg.RoundEndDelay = 1f; cfg.RoundTime = 25f;
            return cfg;
        }

        [Fact]
        public void RecordedMatchPlaysBackLikeSpectating()
        {
            var map = LoadMap("kasaba");
            var net = new LoopbackNetwork(9) { Latency = 0.02 };
            var server = new ServerGame(net.Server, map, 64, SmallMatch());

            // a live player joins: the recorder must be invisible to them
            var end = net.AddClient();
            var live = new ClientGame(end, "canli", _ => map);
            end.Connect();

            var mem = new MemoryStream();
            server.StartRecording(mem);
            Assert.True(server.IsRecording);
            for (int i = 0; i < 64 * 40; i++)
            {
                net.Advance(1.0 / 64);
                server.Step();
                live.Update(1.0 / 64, () => default);
            }
            Assert.DoesNotContain(live.Remotes, r => r.Name == "VEXA TV");
            Assert.DoesNotContain(live.Scores.Values, s => live.NameOf(s.Id) == "VEXA TV");
            server.StopRecording();
            Assert.False(server.IsRecording);
            int roundsPlayed = server.History.Count;

            var demo = DemoFile.Read(new MemoryStream(mem.ToArray()));
            Assert.Equal("kasaba", demo.Map);
            Assert.Equal(64, demo.TickRate);
            Assert.True(demo.Duration > 35, $"duration {demo.Duration:0.0}");
            Assert.NotEmpty(demo.Rounds);

            var playback = new DemoPlayback(demo);
            var viewer = new ClientGame(playback, "izleyici", _ => map);
            var spec = new Spectator();
            int maxCandidates = 0;
            float t = 0;
            while (!playback.Finished)
            {
                playback.Advance(1.0 / 64);
                viewer.Update(1.0 / 64, () => default);
                t += 1f / 64;
                if (viewer.Welcomed && spec.Update(viewer, t)) maxCandidates = System.Math.Max(maxCandidates, spec.Candidates.Count);
            }
            Assert.True(viewer.Welcomed);
            Assert.Equal(Team.None, viewer.LocalTeam);          // the recorder is a spectator...
            Assert.True(maxCandidates >= 4, $"spectator should be able to watch everyone, saw {maxCandidates}"); // ...who can watch all players
            Assert.Equal(server.Players.Count, viewer.Remotes.Count()); // everyone but the hidden recorder
            Assert.Equal(roundsPlayed, viewer.History.Count);
            Assert.Equal(server.ScoreOf(Team.T), viewer.ScoreT);
        }

        [Fact]
        public void TruncatedDemoStillReadsUpToTheCut()
        {
            var map = LoadMap("kasaba");
            var net = new LoopbackNetwork(2);
            var server = new ServerGame(net.Server, map, 64, SmallMatch());
            var mem = new MemoryStream();
            server.StartRecording(mem);
            for (int i = 0; i < 64 * 5; i++) server.Step();
            server.StopRecording();
            var bytes = mem.ToArray();
            var cut = DemoFile.Read(new MemoryStream(bytes, 0, bytes.Length - 37));
            var full = DemoFile.Read(new MemoryStream(bytes));
            Assert.True(cut.Packets.Count > 0 && cut.Packets.Count < full.Packets.Count);
        }

        [Fact]
        public void TeammatesOnlySpectateTheirOwnTeam()
        {
            var map = LoadMap("kasaba");
            var net = new LoopbackNetwork(4) { Latency = 0.01 };
            var cfg = SmallMatch();
            var server = new ServerGame(net.Server, map, 64, cfg);
            var end = net.AddClient();
            var c = new ClientGame(end, "oyuncu", _ => map);
            end.Connect();
            for (int i = 0; i < 64 * 3 && server.Phase != GamePhase.Live; i++) { net.Advance(1.0 / 64); server.Step(); c.Update(1.0 / 64, () => default); }
            var me = server.GetPlayer(c.LocalId);
            server.ApplyDamage(null, me, Weapons.Get(WeaponId.Ak47), HitGroup.Head, 500, 0, false, me.State.Position);
            for (int i = 0; i < 32; i++) { net.Advance(1.0 / 64); server.Step(); c.Update(1.0 / 64, () => default); }
            Assert.False(c.Predicted.Alive);
            var spec = new Spectator();
            Assert.True(spec.Update(c, 0));
            Assert.All(spec.Candidates, id => Assert.Equal(c.LocalTeam, c.TeamOf(id)));
            int first = spec.Target;
            spec.Next(1);
            Assert.Equal(spec.Candidates.Count > 1, spec.Target != first);
        }
    }
}
