using System.Collections.Generic;
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
    public class TournamentTests
    {
        sealed class Harness
        {
            public readonly LoopbackNetwork Net = new LoopbackNetwork(5) { Latency = 0.01 };
            public readonly ServerGame S;
            public readonly Dictionary<string, ClientGame> C = new Dictionary<string, ClientGame>();
            public readonly Dictionary<string, List<ChatMessage>> Chat = new Dictionary<string, List<ChatMessage>>();
            const double Dt = 1.0 / 64;

            public Harness(MatchConfig cfg, params string[] names)
            {
                var map = LoadMap("kasaba");
                S = new ServerGame(Net.Server, map, 64, cfg);
                foreach (var n in names)
                {
                    var end = Net.AddClient();
                    var c = new ClientGame(end, n, _ => map);
                    var log = new List<ChatMessage>();
                    c.ChatReceived += m => log.Add(m);
                    C[n] = c; Chat[n] = log;
                    end.Connect();
                }
                Run(0.5);
            }

            public void Run(double seconds)
            {
                for (double t = 0; t < seconds; t += Dt)
                {
                    Net.Advance(Dt);
                    S.Update(Dt);
                    foreach (var c in C.Values) c.Update(Dt, () => default);
                }
            }

            public ServerGame.Player P(string name) => S.Players.First(p => p.Name == name);
            public void Say(string who, string text, bool team = false) { C[who].SendChat(text, team); Run(0.2); }
            public void Kill(string attacker, string victim)
            {
                var a = P(attacker); var v = P(victim);
                S.ApplyDamage(a, v, Weapons.Get(WeaponId.Knife), HitGroup.Head, 500, 0, false, v.State.Position);
                Run(0.1);
            }
            public void RunUntil(System.Func<bool> cond, double max = 60)
            {
                for (double t = 0; t < max && !cond(); t += 0.05) Run(0.05);
            }
        }

        static MatchConfig Cfg(bool knife = false, bool ready = true)
        {
            var c = MatchConfig.FromJson(@"{
                // comments are allowed in match files
                ""mode"": ""competitive"", ""teamA"": ""Kartallar"", ""teamB"": ""Kurtlar"",
                ""rosterA"": [""ali"", ""can""], ""rosterB"": [""deniz""],
                ""fillBots"": false, ""maxRounds"": 4
            }");
            c.KnifeRound = knife;
            c.RequireReady = ready;
            c.FreezeTime = 1f; c.RoundEndDelay = 1f; c.SidePickTime = 2f; c.TacticalTimeoutTime = 5f;
            return c;
        }

        [Fact]
        public void MatchFileParses()
        {
            var json = File.ReadAllText(Path.Combine(FindRepoRoot(), "Server", "Vexa.Server", "Examples", "tournament-match.json"));
            var c = MatchConfig.FromJson(json);
            Assert.True(c.Tournament);
            Assert.Equal("Kartallar", c.TeamA);
            Assert.Equal(5, c.RosterB.Count);
            Assert.True(c.KnifeRound);
            Assert.Equal("kasaba", c.Map);
            Assert.Equal(13, c.WinRounds);
            Assert.Equal(12, c.HalfRounds);
        }

        static string FindRepoRoot()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Vexa.sln"))) d = d.Parent;
            return d?.FullName ?? ".";
        }

        [Fact]
        public void RostersPickSidesAndOutsidersSpectate()
        {
            var h = new Harness(Cfg(), "ali", "deniz", "can", "misafir");
            Assert.Equal(Team.T, h.P("ali").State.Team);       // team A starts T
            Assert.Equal(Team.T, h.P("can").State.Team);
            Assert.Equal(Team.CT, h.P("deniz").State.Team);
            Assert.Equal(Team.None, h.P("misafir").State.Team);
            Assert.Equal("Kartallar", h.C["deniz"].TeamNameT);
            Assert.Equal("Kurtlar", h.C["deniz"].TeamNameCT);
            // teams are locked
            h.C["ali"].SelectTeam(Team.CT);
            h.Run(0.3);
            Assert.Equal(Team.T, h.P("ali").State.Team);
        }

        [Fact]
        public void WarmupWaitsUntilEveryoneIsReady()
        {
            var h = new Harness(Cfg(), "ali", "deniz");
            h.Run(5);
            Assert.Equal(GamePhase.Warmup, h.S.Phase);
            Assert.True(h.C["ali"].Flags.HasFlag(MatchFlags.WaitingReady));
            h.Say("ali", ".ready");
            Assert.Equal(1, h.C["deniz"].ReadyCount);
            Assert.Equal(2, h.C["deniz"].ReadyNeeded);
            h.Run(3);
            Assert.Equal(GamePhase.Warmup, h.S.Phase);
            h.Say("deniz", ".r");
            h.RunUntil(() => h.S.Phase != GamePhase.Warmup, 10);
            Assert.Equal(GamePhase.Freeze, h.S.Phase);
            Assert.Equal(1, h.S.Round);
            Assert.Equal(800, h.P("ali").Money);   // warmup money is reset when the match begins
        }

        [Fact]
        public void KnifeRoundWinnerPicksSide()
        {
            var h = new Harness(Cfg(knife: true), "ali", "deniz");
            h.Say("ali", ".ready"); h.Say("deniz", ".ready");
            h.RunUntil(() => h.S.Phase == GamePhase.Live, 15);
            Assert.True(h.S.KnifeRoundActive);
            var ali = h.P("ali").State;
            Assert.True(ali.Primary.IsEmpty && ali.Secondary.IsEmpty && !ali.HasC4);
            Assert.Equal(WeaponSlotKind.Melee, ali.Active);
            Assert.Equal(BombState.None, h.S.Bomb.State);

            h.Kill("deniz", "ali");                       // CT (team B) wins the knife round
            h.Run(0.2);
            Assert.True(h.S.SidePickActive);
            Assert.Equal(0, h.S.ScoreOf(Team.CT));          // the knife round is not scored
            h.Say("ali", ".switch");                       // the loser can't choose
            Assert.True(h.S.SidePickActive);
            h.Say("deniz", ".switch");
            h.RunUntil(() => h.S.Phase == GamePhase.Freeze, 5);
            Assert.False(h.S.SidePickActive);
            Assert.Equal(Team.T, h.P("deniz").State.Team);  // team B now attacks
            Assert.Equal(Team.CT, h.P("ali").State.Team);
            Assert.Equal("Kurtlar", h.S.TeamName(Team.T));
            Assert.Equal(1, h.S.Round);
            Assert.Empty(h.S.History);
            Assert.False(h.P("deniz").State.Secondary.IsEmpty); // real weapons again
        }

        [Fact]
        public void TeamChatStaysInTheTeamAndDeadCantTalkToTheLiving()
        {
            var h = new Harness(Cfg(ready: false), "ali", "can", "deniz");
            h.RunUntil(() => h.S.Phase == GamePhase.Live, 15);
            h.Say("ali", "rush B", team: true);
            Assert.Contains(h.Chat["can"], m => m.Text == "rush B" && m.TeamOnly);
            Assert.DoesNotContain(h.Chat["deniz"], m => m.Text == "rush B");

            h.Kill("deniz", "ali");
            h.Run(0.5); // chat flood control: one line per 0.4 s
            h.Say("ali", "o köşede biri var");
            Assert.DoesNotContain(h.Chat["can"], m => m.Text.Contains("köşede"));    // alive teammate
            Assert.DoesNotContain(h.Chat["deniz"], m => m.Text.Contains("köşede"));  // alive enemy
            Assert.Contains(h.Chat["ali"], m => m.Text.Contains("köşede") && m.Dead); // echo to the sender
        }

        [Fact]
        public void TacticalTimeoutExtendsFreezeTimeAndUsesOneTimeout()
        {
            var h = new Harness(Cfg(ready: false), "ali", "deniz");
            h.RunUntil(() => h.S.Phase == GamePhase.Live, 15);
            h.Say("ali", ".tac");
            Assert.Equal(ServerGame.PauseKind.Tactical, h.S.PendingPause);
            h.Kill("ali", "deniz");
            h.RunUntil(() => h.S.Phase == GamePhase.Freeze, 5);
            Assert.Equal(ServerGame.PauseKind.Tactical, h.S.ActivePause);
            Assert.Equal(2, h.S.TimeoutsLeft(Team.T));
            h.Run(3);
            Assert.Equal(GamePhase.Freeze, h.S.Phase);     // 1 s freeze + 5 s timeout
            h.RunUntil(() => h.S.Phase == GamePhase.Live, 6);
            Assert.Equal(GamePhase.Live, h.S.Phase);
            Assert.Equal(ServerGame.PauseKind.None, h.S.ActivePause);
        }

        [Fact]
        public void TechnicalPauseHoldsUntilBothTeamsUnpause()
        {
            var h = new Harness(Cfg(ready: false), "ali", "deniz");
            h.RunUntil(() => h.S.Phase == GamePhase.Freeze, 15);
            h.Say("deniz", ".tech");
            Assert.Equal(ServerGame.PauseKind.Technical, h.S.ActivePause);
            h.Run(10);
            Assert.Equal(GamePhase.Freeze, h.S.Phase);
            Assert.True(h.C["ali"].Flags.HasFlag(MatchFlags.TechnicalPause));
            h.Say("deniz", ".unpause");
            h.Run(2);
            Assert.Equal(GamePhase.Freeze, h.S.Phase);
            h.Say("ali", ".unpause");
            h.RunUntil(() => h.S.Phase == GamePhase.Live, 5);
            Assert.Equal(GamePhase.Live, h.S.Phase);
        }

        [Fact]
        public void ResultsFileHasTeamsScoresAndRounds()
        {
            var cfg = Cfg(ready: false);
            cfg.ResultsPath = Path.Combine(Path.GetTempPath(), "vexa-test-" + System.Guid.NewGuid().ToString("N"), "result.json");
            cfg.MaxRounds = 2; cfg.HalfRounds = 1; cfg.WinRounds = 2; cfg.Overtime = false;
            var h = new Harness(cfg, "ali", "deniz");
            string published = null;
            h.S.MatchFinished += j => published = j;
            for (int r = 0; r < 2; r++)
            {
                h.RunUntil(() => h.S.Phase == GamePhase.Live, 15);
                // team A (ali) wins both rounds: T side first, CT side after the half
                h.Kill("ali", "deniz");
                h.RunUntil(() => h.S.Phase != GamePhase.RoundEnd && h.S.Phase != GamePhase.Live, 5);
            }
            h.RunUntil(() => h.S.Phase == GamePhase.MatchOver, 10);
            Assert.Equal(GamePhase.MatchOver, h.S.Phase);
            Assert.NotNull(published);
            Assert.True(File.Exists(cfg.ResultsPath));
            var doc = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(cfg.ResultsPath));
            Assert.Equal("A", doc.Str("winner"));
            var a = doc.Obj("teamA");
            Assert.Equal("Kartallar", a.Str("name"));
            Assert.Equal(2, a.Int("score", -1));
            Assert.Equal(0, doc.Obj("teamB").Int("score", -1));
            var rounds = (List<object>)doc["roundHistory"];
            Assert.Equal(2, rounds.Count);
            Assert.All(rounds, r => Assert.Equal("A", ((Dictionary<string, object>)r).Str("winner")));
            var players = (List<object>)a["players"];
            Assert.Equal("ali", ((Dictionary<string, object>)players[0]).Str("name"));
            Assert.Equal(2, ((Dictionary<string, object>)players[0]).Int("kills", -1));
        }

        [Fact]
        public void ConsoleCommandsWork()
        {
            var h = new Harness(Cfg(), "ali", "deniz");
            Assert.Contains("ali", h.S.ConsoleCommand("status"));
            h.S.ConsoleCommand("say merhaba");
            h.Run(0.2);
            Assert.Contains(h.Chat["deniz"], m => m.SenderId == 0 && m.Text == "merhaba");
            h.S.ConsoleCommand("forceready");
            h.RunUntil(() => h.S.Phase != GamePhase.Warmup, 10);
            Assert.Equal(GamePhase.Freeze, h.S.Phase);
        }

        [Fact]
        public void MiniJsonRoundTrips()
        {
            var doc = new Dictionary<string, object> { ["a"] = 1, ["b"] = "x \"y\"\nz", ["c"] = new List<object> { true, null, 2.5 } };
            var back = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Write(doc));
            Assert.Equal(1, back.Int("a", 0));
            Assert.Equal("x \"y\"\nz", back.Str("b"));
            var c = (List<object>)back["c"];
            Assert.Equal(true, c[0]); Assert.Null(c[1]); Assert.Equal(2.5, (double)c[2]);
        }
    }
}
