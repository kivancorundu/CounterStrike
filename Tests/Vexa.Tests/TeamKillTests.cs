using System.Linq;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Xunit;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class TeamKillTests
    {
        sealed class Room
        {
            public readonly LoopbackNetwork Net = new LoopbackNetwork { Latency = 0.01 };
            public readonly ServerGame G;
            public readonly MapData Map = LoadMap("kasaba");
            const double Dt = 1.0 / 64;

            public Room()
            {
                var cfg = MatchConfig.Competitive();
                cfg.FillBots = false;
                cfg.WarmupTime = 0.1f; cfg.FreezeTime = 0.5f;
                G = new ServerGame(Net.Server, Map, 64, cfg);
            }

            public ClientGame Join(string name)
            {
                var end = Net.AddClient();
                var c = new ClientGame(end, name, _ => Map);
                end.Connect();
                Run(0.5, c);
                return c;
            }

            public void Run(double seconds, params ClientGame[] clients)
            {
                for (double t = 0; t < seconds; t += Dt)
                {
                    Net.Advance(Dt);
                    G.Step();
                    foreach (var c in clients) c.Update(Dt, () => default);
                }
            }

            public void Kill(ServerGame.Player attacker, ServerGame.Player victim)
            {
                G.ApplyDamage(attacker, victim, Weapons.Get(WeaponId.Ak47), HitGroup.Head, 500, 0, false, victim.State.Position);
            }
        }

        [Fact]
        public void TeamKillsAreFinedAndTheThirdOneKicks()
        {
            var room = new Room();
            var client = room.Join("hain");
            var me = room.G.Players.Single(p => !p.IsBot);
            var mates = Enumerable.Range(0, 3).Select(i => room.G.AddBot("mate" + i, me.State.Team, brain: false)).ToList();
            room.G.AddBot("enemy", me.State.Team == Team.T ? Team.CT : Team.T, brain: false);
            for (int i = 0; i < 64 * 30 && room.G.Phase != GamePhase.Live; i++) room.Run(1.0 / 64, client);
            Assert.Equal(GamePhase.Live, room.G.Phase);

            me.Money = 10000;
            room.Kill(me, mates[0]);
            Assert.Equal(10000 - room.G.Config.TeamKillPenalty, me.Money);
            Assert.Equal(1, me.TeamKills);
            room.Kill(me, mates[1]);
            Assert.Equal(System.Math.Max(0, 10000 - 2 * room.G.Config.TeamKillPenalty), me.Money);
            Assert.False(me.Kicked);

            room.Kill(me, mates[2]);
            Assert.True(me.Kicked);
            room.Run(1.0, client);
            Assert.DoesNotContain(me, room.G.Players);
            Assert.True(client.Disconnected);
            Assert.Contains("atıldın", client.KickReason);

            // the same name can't come back during this match
            var again = room.Join("hain");
            room.Run(1.0, again);
            Assert.True(again.Disconnected);
            Assert.NotNull(again.KickReason);
            Assert.DoesNotContain(room.G.Players, p => p.Name == "hain");
        }

        [Fact]
        public void WarmupTeamKillsDontCountAndBotsAreNotKicked()
        {
            var room = new Room();
            var a = room.G.AddBot("a", Team.T, brain: false);
            var b = room.G.AddBot("b", Team.T, brain: false);
            room.G.Step();
            Assert.Equal(GamePhase.Warmup, room.G.Phase);
            room.Kill(a, b);
            Assert.Equal(0, a.TeamKills);

            var mates = Enumerable.Range(0, 4).Select(i => room.G.AddBot("m" + i, Team.T, brain: false)).ToList();
            room.G.AddBot("enemy", Team.CT, brain: false);
            for (int i = 0; i < 64 * 30 && room.G.Phase != GamePhase.Live; i++) room.G.Step();
            Assert.Equal(GamePhase.Live, room.G.Phase);
            foreach (var m in mates) room.Kill(a, m);
            Assert.Equal(4, a.TeamKills);
            Assert.False(a.Kicked);
            Assert.Contains(a, room.G.Players);
        }
    }
}
