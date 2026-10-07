using System;
using System.Linq;
using System.Numerics;
using Vexa.Core;
using Vexa.Core.AI;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Xunit;
using Xunit.Abstractions;
using static Vexa.Tests.TestUtil;

namespace Vexa.Tests
{
    public class MatchTests
    {
        private readonly ITestOutputHelper _out;
        public MatchTests(ITestOutputHelper o) { _out = o; }

        sealed class Dummies
        {
            public ServerGame G;
            public ServerGame.Player T, CT;
            public int TickRate = 64;

            public Dummies(MatchConfig cfg = null)
            {
                cfg = cfg ?? MatchConfig.Competitive();
                cfg.FillBots = false;
                cfg.WarmupTime = 0.1f; cfg.FreezeTime = 1f; cfg.RoundEndDelay = 1f;
                var net = new LoopbackNetwork();
                G = new ServerGame(net.Server, LoadMap("kasaba"), TickRate, cfg);
                // brainless players we control directly
                T = G.AddBot("t", Team.T, brain: false);
                CT = G.AddBot("ct", Team.CT, brain: false);
            }
            public void Run(float seconds) { int n = (int)(seconds * TickRate); for (int i = 0; i < n; i++) G.Step(); }
            public void UntilLive() { for (int i = 0; i < 64 * 30 && G.Phase != GamePhase.Live; i++) G.Step(); }
            public void Kill(ServerGame.Player attacker, ServerGame.Player victim, WeaponId w = WeaponId.Ak47)
            {
                G.ApplyDamage(attacker, victim, Weapons.Get(w), HitGroup.Head, 500, 0, false, victim.State.Position);
                G.Step(); // win conditions are evaluated on the next tick
            }
        }

        [Fact]
        public void EliminationPaysWinBonusKillRewardAndLossBonus()
        {
            var d = new Dummies();
            d.UntilLive();
            Assert.Equal(GamePhase.Live, d.G.Phase);
            Assert.True(d.T.State.HasC4, "a terrorist gets the bomb");
            d.Kill(d.CT, d.T);
            Assert.Equal(GamePhase.RoundEnd, d.G.Phase);
            Assert.Equal(1, d.G.ScoreOf(Team.CT));
            Assert.Equal(800 + 300 + 3250, d.CT.Money);   // start + AK kill + elimination win
            Assert.Equal(800 + 1900, d.T.Money);          // loss bonus starts at $1900 after the pistol round
            Assert.Equal(BombState.Dropped, d.G.Bomb.State);
        }

        [Fact]
        public void LossBonusGrowsAndWinsDecrementIt()
        {
            var d = new Dummies();
            int[] expected = { 1900, 2400, 2900, 3400, 3400 };
            for (int r = 0; r < 5; r++)
            {
                d.UntilLive();
                int before = d.T.Money;
                d.Kill(d.CT, d.T);
                Assert.Equal(expected[r], d.T.Money - before);
                d.Run(1.2f);
            }
            // T wins one round: counter goes down by one (CS2 MR12 rule), next loss pays $2900
            d.UntilLive();
            d.Kill(d.T, d.CT);
            d.Run(1.2f);
            d.UntilLive();
            d.T.Money = 0; // stay below the $16000 cap
            d.Kill(d.CT, d.T);
            Assert.Equal(2900, d.T.Money);
        }

        [Fact]
        public void TimeoutGivesNothingToSurvivingTerrorists()
        {
            var cfg = MatchConfig.Competitive(); cfg.RoundTime = 2f;
            var d = new Dummies(cfg);
            d.UntilLive();
            int before = d.T.Money;
            d.Run(2.2f);
            Assert.Equal(GamePhase.RoundEnd, d.G.Phase);
            Assert.Equal(1, d.G.ScoreOf(Team.CT));
            Assert.Equal(before, d.T.Money);
        }

        [Fact]
        public void PlantThenDefuseWithKit()
        {
            var d = new Dummies();
            d.UntilLive();
            var site = d.G.Map.Sites.First(s => s.Name == "A");
            var pos = new Vector3((site.MinX + site.MaxX) / 2, 0, (site.MinZ + site.MaxZ) / 2);
            int tMoney = d.T.Money;
            d.T.State.HasC4 = false;
            d.G.PlantBomb(d.T, pos, "A");
            Assert.Equal(BombState.Planted, d.G.Bomb.State);
            Assert.Equal(tMoney + 300, d.T.Money);
            // CT with a kit holds E next to the bomb
            d.CT.State.Position = d.G.Bomb.Position + new Vector3(0.5f, 0, 0);
            d.CT.State.HasKit = true;
            d.CT.LastButtons = Buttons.Use;
            d.Run(4.5f);
            Assert.Equal(BombState.Planted, d.G.Bomb.State);
            Assert.True(d.CT.State.Defusing);
            d.Run(0.7f);
            Assert.Equal(BombState.Defused, d.G.Bomb.State);
            Assert.Equal(GamePhase.RoundEnd, d.G.Phase);
            Assert.Equal(RoundEndReason.BombDefused, d.G.History.Last().Reason);
            // losing terrorists still get the $800 plant bonus
            Assert.Equal(tMoney + 300 + 1900 + 800, d.T.Money);
        }

        [Fact]
        public void BombExplodesAfter40SecondsAndKillsNearbyPlayers()
        {
            var d = new Dummies();
            d.UntilLive();
            var site = d.G.Map.Sites.First(s => s.Name == "B");
            var pos = new Vector3((site.MinX + site.MaxX) / 2, 0, (site.MinZ + site.MaxZ) / 2);
            d.T.State.HasC4 = false;
            d.G.PlantBomb(d.T, pos, "B");
            d.CT.State.Position = pos + new Vector3(2, 0, 0);
            d.Run(39.5f);
            Assert.Equal(BombState.Planted, d.G.Bomb.State);
            d.Run(1f);
            Assert.Equal(BombState.Exploded, d.G.Bomb.State);
            Assert.False(d.CT.State.Alive);
            Assert.Equal(Team.T, d.G.History.Last().Winner);
            Assert.Equal(RoundEndReason.BombExploded, d.G.History.Last().Reason);
        }

        [Fact]
        public void BuyRules()
        {
            var d = new Dummies();
            d.UntilLive();
            var t = d.T;
            Assert.True(d.G.CanBuy(t, out var why), why + " pos=" + t.State.Position + " phase=" + d.G.Phase);
            Assert.False(d.G.TryBuy(t, Items.FromWeapon(WeaponId.M4a4)), "CT-only rifle");
            Assert.False(d.G.TryBuy(t, Items.FromWeapon(WeaponId.Ak47)), "not enough money");
            Assert.True(d.G.TryBuy(t, ItemId.Vest));
            Assert.Equal(150, t.Money);
            Assert.False(d.G.TryBuy(t, ItemId.Flash), "$150 left, a flash costs $200");
            t.Money = 10000;
            Assert.True(d.G.TryBuy(t, ItemId.Flash));
            Assert.True(d.G.TryBuy(t, ItemId.Flash));
            Assert.False(d.G.TryBuy(t, ItemId.Flash), "max 2 flashes");
            Assert.True(d.G.TryBuy(t, ItemId.HE));
            Assert.True(d.G.TryBuy(t, ItemId.Smoke));
            Assert.False(d.G.TryBuy(t, ItemId.Molotov), "max 4 grenades");
            Assert.True(d.G.TryBuy(t, ItemId.VestHelmet));
            Assert.Equal(10000 - 200 - 200 - 300 - 300 - 350, t.Money); // helmet upgrade costs $350
            Assert.False(d.G.TryBuy(t, ItemId.DefuseKit), "kits are CT only");
            // leave the buy zone
            t.State.Position = d.G.Map.Sites[0].Contains(t.State.Position) ? t.State.Position : new Vector3((d.G.Map.Sites[0].MinX + d.G.Map.Sites[0].MaxX) / 2, 0, (d.G.Map.Sites[0].MinZ + d.G.Map.Sites[0].MaxZ) / 2);
            Assert.False(d.G.TryBuy(t, Items.FromWeapon(WeaponId.Ak47)), "outside buy zone");
            // buy time is over after 20 s
            t.State.Position = d.G.Map.Spawns.First(s => s.Team == Team.T).Position;
            d.Run(21f);
            Assert.False(d.G.TryBuy(t, Items.FromWeapon(WeaponId.Ak47)), "buy time over");
        }

        [Fact]
        public void BuyingAPrimaryDropsTheOldOne()
        {
            var d = new Dummies();
            d.UntilLive();
            d.T.Money = 16000;
            Assert.True(d.G.TryBuy(d.T, Items.FromWeapon(WeaponId.Galil)));
            Assert.True(d.G.TryBuy(d.T, Items.FromWeapon(WeaponId.Ak47)));
            Assert.Equal(WeaponId.Ak47, d.T.State.Primary.Id);
            Assert.Contains(d.G.WorldItems, i => i.Weapon.Id == WeaponId.Galil);
        }

        [Fact]
        public void HeGrenadeDamagesAndFlashBlindsAndSmokeBlocksVision()
        {
            var d = new Dummies();
            d.UntilLive();
            var victimPos = d.CT.State.Position;
            // HE at the CT's feet
            d.G.SpawnProjectile(d.T, new GrenadeThrow { Type = GrenadeType.HE, Origin = victimPos + new Vector3(0, 0.3f, 0), Velocity = new Vector3(0, -0.1f, 0) });
            int hp = d.CT.State.Health;
            d.Run(1.7f);
            Assert.True(d.CT.State.Health < hp - 40, $"HE should hurt a lot point blank (hp {d.CT.State.Health})");

            // flash in front of the CT's eyes
            d.CT.State.Yaw = 0; d.CT.State.Pitch = 0;
            var front = d.CT.State.EyePosition + new Vector3(0, 0, 2f);
            if (!d.G.World.LineOfSight(d.CT.State.EyePosition, front)) front = d.CT.State.EyePosition + new Vector3(0, 0, -2f);
            d.G.SpawnProjectile(d.T, new GrenadeThrow { Type = GrenadeType.Flash, Origin = front, Velocity = Vector3.Zero });
            d.Run(1.6f);
            Assert.True(d.CT.State.FlashEndTime > d.CT.PlayerTime(d.G.Dt) + 1f, "CT should be blinded");

            // smoke between two points blocks line of sight once it has spread
            var a = d.T.State.EyePosition;
            var b = a + new Vector3(8, 0, 0);
            if (!d.G.World.LineOfSight(a, b)) { b = a + new Vector3(-8, 0, 0); }
            if (d.G.World.LineOfSight(a, b))
            {
                var mid = (a + b) / 2;
                d.G.SpawnProjectile(d.T, new GrenadeThrow { Type = GrenadeType.Smoke, Origin = mid, Velocity = Vector3.Zero });
                d.Run(3f);
                Assert.Single(d.G.Smokes);
                _out.WriteLine($"smoke at {d.G.Smokes[0].Center} start {d.G.Smokes[0].StartTick} now {d.G.Tick} a {a} b {b}");
                Assert.False(d.G.CanSee(a, b));
            }
        }

        [Fact]
        public void MolotovBurnsAndSmokePutsItOut()
        {
            var d = new Dummies(MatchConfig.Practice());
            d.Run(0.2f);
            var p = d.CT.State.Position;
            d.G.SpawnProjectile(d.T, new GrenadeThrow { Type = GrenadeType.Molotov, Origin = p + new Vector3(0, 1f, 0), Velocity = new Vector3(0, -3f, 0) });
            d.Run(0.5f);
            Assert.Single(d.G.Fires);
            int hp = d.CT.State.Health;
            d.Run(1f);
            Assert.True(d.CT.State.Health < hp, "standing in fire hurts");
            d.G.SpawnProjectile(d.T, new GrenadeThrow { Type = GrenadeType.Smoke, Origin = p + new Vector3(0, 0.5f, 0), Velocity = Vector3.Zero });
            d.Run(1.5f);
            Assert.Empty(d.G.Fires);
        }

        [Fact]
        public void NavGridConnectsSpawnsToBothSites()
        {
            var map = LoadMap("kasaba");
            var nav = NavGrid.Build(map.BuildCollision());
            _out.WriteLine($"nav nodes: {nav.Nodes.Count}");
            var tSpawn = map.Spawns.First(s => s.Team == Team.T).Position;
            var ctSpawn = map.Spawns.First(s => s.Team == Team.CT).Position;
            foreach (var site in map.Sites)
            {
                var c = new Vector3((site.MinX + site.MaxX) / 2, 0, (site.MinZ + site.MaxZ) / 2);
                var p1 = nav.FindPath(tSpawn, c);
                var p2 = nav.FindPath(ctSpawn, c);
                Assert.NotNull(p1); Assert.NotNull(p2);
                _out.WriteLine($"site {site.Name}: T path {p1.Count} pts, CT path {p2.Count} pts");
            }
        }

        [Fact]
        public void FullBotMatchOnKasabaPlaysToTheEnd()
        {
            var cfg = MatchConfig.Competitive();
            cfg.WarmupTime = 1f;
            var net = new LoopbackNetwork();
            var g = new ServerGame(net.Server, LoadMap("kasaba"), 64, cfg, seed: 42);
            int kills = 0, plants = 0, defuses = 0, explosions = 0, nades = 0, hs = 0;
            g.OnKill += k => { kills++; if (k.Headshot) hs++; if (k.Grenade != GrenadeType.None) nades++; };
            g.RoundEnded += (w, r) => { if (r == RoundEndReason.BombDefused) defuses++; if (r == RoundEndReason.BombExploded) explosions++; };
            int lastBomb = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 64 * 60 * 60 && g.Phase != GamePhase.MatchOver; i++)
            {
                g.Step();
                if (g.Bomb.State == BombState.Planted && lastBomb != g.Bomb.PlantTick) { plants++; lastBomb = g.Bomb.PlantTick; }
            }
            _out.WriteLine($"rounds {g.RoundsPlayed}, score T {g.ScoreOf(Team.T)} : CT {g.ScoreOf(Team.CT)}, kills {kills} (hs {hs}, grenade {nades}), plants {plants}, defuses {defuses}, explosions {explosions}, real time {sw.ElapsedMilliseconds} ms");
            foreach (var p in g.Players.OrderBy(p => p.State.Team).ThenByDescending(p => p.Kills))
                _out.WriteLine($"  {p.State.Team,-3} {p.Name,-10} K {p.Kills,3} D {p.Deaths,3} A {p.Assists,3} MVP {p.Mvps,2} ADR {p.Damage / Math.Max(1, g.RoundsPlayed),3} ${p.Money}");
            Assert.Equal(GamePhase.MatchOver, g.Phase);
            Assert.True(Math.Max(g.ScoreOf(Team.T), g.ScoreOf(Team.CT)) >= 13);
            Assert.True(g.RoundsPlayed >= 13);
            Assert.True(kills > g.RoundsPlayed * 3, "bots should fight");
            Assert.True(plants >= 1, "terrorists should plant at least once");
            Assert.All(g.Players, p => Assert.InRange(p.Money, 0, 16000));
        }
    }
}
