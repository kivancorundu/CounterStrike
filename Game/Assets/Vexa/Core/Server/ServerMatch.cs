using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>Rules of a match. Values follow CS2 competitive / casual.</summary>
    public sealed class MatchConfig
    {
        public GameMode Mode = GameMode.Competitive;
        public int MaxRounds = 24, HalfRounds = 12, WinRounds = 13;
        public int StartMoney = 800, MaxMoney = 16000;
        public float RoundTime = 115f, FreezeTime = 15f, BuyTime = 20f, RoundEndDelay = 7f, WarmupTime = 3f;
        public float BombTime = 40f, DefuseTime = 10f, KitDefuseTime = 5f;
        public bool Overtime = true;
        public int OvertimeMoney = 12500;
        public bool FriendlyFire = true;
        public bool FreeArmor, FreeKit;
        public int TeamSize = 5;
        public bool FillBots = true;
        public float DeathmatchDuration = 600f, RespawnDelay = 2f;
        public float BotDifficulty = 0.5f;   // 0 easy .. 1 expert

        // ---- tournament (esports) settings ----
        public bool Tournament;                 // ready-up, chat commands, results file
        public string TeamA = "TAKIM A", TeamB = "TAKIM B";
        public List<string> RosterA = new List<string>(), RosterB = new List<string>();  // player names; empty = anyone
        public bool TeamAStartsCT;              // otherwise team A starts as T (after the knife round, the winner picks)
        public bool RequireReady = true;        // warmup lasts until every player types .ready
        public bool KnifeRound;
        public int TacticalTimeouts = 3;
        public float TacticalTimeoutTime = 30f;
        public float SidePickTime = 30f;
        public string ResultsPath;              // results JSON is written here when the match ends
        public string MatchId;                  // external id (e.g. a rally.gg match), echoed in the results
        public string Map;                      // optional map name from the match file
        public string DemoPath;                 // record the match to this .vxdemo file

        public bool HasRounds => Mode == GameMode.Competitive || Mode == GameMode.Casual;

        /// <summary>
        /// Builds a config from a JSON match file, e.g.
        /// { "mode": "competitive", "teamA": "Kartallar", "teamB": "Kurtlar", "rosterA": ["ali", ...], "knifeRound": true, "maxRounds": 24 }.
        /// </summary>
        public static MatchConfig FromJson(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> d)) throw new FormatException("match config must be a JSON object");
            MatchConfig c;
            switch ((d.Str("mode", "competitive")).ToLowerInvariant())
            {
                case "casual": c = Casual(); break;
                case "deathmatch": c = Deathmatch(); break;
                case "practice": c = Practice(); break;
                case "competitive": c = Competitive(); break;
                default: throw new FormatException("unknown mode: " + d.Str("mode"));
            }
            c.Tournament = d.Bool("tournament", true);
            c.MatchId = d.Str("matchId", null);
            c.Map = d.Str("map", null);
            c.TeamA = d.Str("teamA", c.TeamA);
            c.TeamB = d.Str("teamB", c.TeamB);
            c.RosterA = d.Strings("rosterA");
            c.RosterB = d.Strings("rosterB");
            c.TeamAStartsCT = d.Str("teamAStarts", "t").Equals("ct", StringComparison.OrdinalIgnoreCase);
            c.RequireReady = d.Bool("requireReady", c.RequireReady);
            c.KnifeRound = d.Bool("knifeRound", c.KnifeRound);
            c.TacticalTimeouts = d.Int("tacticalTimeouts", c.TacticalTimeouts);
            c.TacticalTimeoutTime = d.Num("tacticalTimeoutSeconds", c.TacticalTimeoutTime);
            c.MaxRounds = d.Int("maxRounds", c.MaxRounds);
            c.HalfRounds = d.Int("halfRounds", c.MaxRounds / 2);
            c.WinRounds = d.Int("winRounds", c.MaxRounds / 2 + 1);
            c.Overtime = d.Bool("overtime", c.Overtime);
            c.OvertimeMoney = d.Int("overtimeMoney", c.OvertimeMoney);
            c.StartMoney = d.Int("startMoney", c.StartMoney);
            c.MaxMoney = d.Int("maxMoney", c.MaxMoney);
            c.RoundTime = d.Num("roundTime", c.RoundTime);
            c.FreezeTime = d.Num("freezeTime", c.FreezeTime);
            c.BuyTime = d.Num("buyTime", c.BuyTime);
            c.BombTime = d.Num("bombTime", c.BombTime);
            c.FriendlyFire = d.Bool("friendlyFire", c.FriendlyFire);
            c.TeamSize = d.Int("teamSize", c.TeamSize);
            c.FillBots = d.Bool("fillBots", false);
            c.BotDifficulty = d.Num("botDifficulty", c.BotDifficulty);
            c.ResultsPath = d.Str("resultsPath", null);
            c.DemoPath = d.Str("demoPath", null);
            return c;
        }
        public bool HasEconomy => HasRounds;

        public static MatchConfig Competitive() => new MatchConfig();
        public static MatchConfig Casual() => new MatchConfig
        {
            Mode = GameMode.Casual, MaxRounds = 15, HalfRounds = 7, WinRounds = 8, StartMoney = 1000, MaxMoney = 10000,
            RoundTime = 135f, FreezeTime = 6f, BuyTime = 45f, FriendlyFire = false, FreeArmor = true, FreeKit = true, Overtime = false,
        };
        public static MatchConfig Deathmatch() => new MatchConfig { Mode = GameMode.Deathmatch, StartMoney = 16000, FriendlyFire = false, FillBots = false, TeamSize = 0 };
        public static MatchConfig Practice() => new MatchConfig { Mode = GameMode.Practice, StartMoney = 16000, FriendlyFire = false, FillBots = false, TeamSize = 0 };
    }

    public sealed class WorldItem
    {
        public int Id;
        public WeaponSlot Weapon;
        public Vector3 Position, Velocity;
        public bool Rest;
        public int OwnerId, NoPickupUntil;
    }

    public sealed partial class ServerGame
    {
        public GamePhase Phase { get; private set; } = GamePhase.Warmup;
        public int PhaseEndTick { get; private set; }
        public int BuyEndTick { get; private set; }
        public int Round { get; private set; }
        public int RoundsPlayed { get; private set; }
        public BombInfo Bomb;
        public readonly List<RoundResult> History = new List<RoundResult>();
        public readonly List<WorldItem> WorldItems = new List<WorldItem>();
        private readonly int[] _score = new int[3];
        private readonly int[] _loss = { 1, 1, 1 };
        private bool _resetInventories = true;
        private int _nextItemId = 1;
        private int _nextScoreboardTick;
        public int ScoreOf(Team t) => _score[(int)t];
        public event Action<Team, RoundEndReason> RoundEnded;

        private static readonly string[] BotNames = { "Kartal", "Poyraz", "Bozkurt", "Atlas", "Toprak", "Yıldırım", "Kaya", "Demir", "Fırtına", "Doruk", "Alaz", "Tuna", "Efe", "Baran", "Kuzey" };
        private int _botNameIdx;

        private void InitMatch()
        {
            if (Config.Mode == GameMode.Deathmatch)
            {
                Phase = GamePhase.Live;
                PhaseEndTick = Tick + (int)(Config.DeathmatchDuration * TickRate);
            }
            else if (Config.Mode == GameMode.Practice)
            {
                Phase = GamePhase.Live;
                PhaseEndTick = int.MaxValue;
            }
            else
            {
                Phase = GamePhase.Warmup;
                PhaseEndTick = Tick + (int)(Config.WarmupTime * TickRate);
                if (Config.FillBots) for (int i = 0; i < Config.TeamSize * 2; i++) AddBot(NextBotName(), i % 2 == 0 ? Team.T : Team.CT);
            }
            InitTournament();
        }

        string NextBotName() => BotNames[_botNameIdx++ % BotNames.Length];

        // ---------------- teams ----------------
        int TeamCount(Team t) => _players.Count(p => p.State.Team == t);
        int HumanCount(Team t) => _players.Count(p => p.State.Team == t && !p.IsBot);

        private Team AutoTeam()
        {
            if (Config.Mode == GameMode.Deathmatch) return Team.None;
            int ht = HumanCount(Team.T), hc = HumanCount(Team.CT);
            if (ht != hc) return ht < hc ? Team.T : Team.CT;
            int t = TeamCount(Team.T), c = TeamCount(Team.CT);
            if (t != c) return t < c ? Team.T : Team.CT;
            return _rng.Next(2) == 0 ? Team.T : Team.CT;
        }

        /// <summary>Humans replace bots so teams stay at TeamSize.</summary>
        private void MakeRoomOnTeam(Team team)
        {
            if (Config.TeamSize <= 0 || team == Team.None) return;
            while (TeamCount(team) >= Config.TeamSize)
            {
                var bot = _players.LastOrDefault(p => p.IsBot && p.State.Team == team);
                if (bot == null) break;
                RemovePlayer(bot);
            }
        }

        private void OnPlayerJoined(Player p)
        {
            if (!Config.HasRounds) { Respawn(p); return; }
            if (p.State.Team == Team.None) return; // spectator
            if (Phase == GamePhase.Warmup || Phase == GamePhase.Freeze)
            {
                SpawnForRound(p, false, PickSpawn(p.State.Team, null));
                if (Phase == GamePhase.Warmup) p.Money = Config.MaxMoney; // warmup: buy anything, reset when the match begins
                if (Phase == GamePhase.Freeze && p.Bot != null) BotBuy(p);
            }
        }

        private void OnPlayerLeft(Player p)
        {
            if (Config.FillBots && Config.HasRounds && p.State.Team != Team.None && TeamCount(p.State.Team) < Config.TeamSize)
                AddBot(NextBotName(), p.State.Team);
        }

        private void RequestTeam(Player p, Team team)
        {
            if (!Config.HasRounds || team == p.State.Team || TeamsLocked) return;
            if (team != Team.T && team != Team.CT) team = AutoTeam();
            if (team == p.State.Team) return;
            if (p.State.Alive && Phase == GamePhase.Live) { OnDeath(p, null, null, false, false); }
            MakeRoomOnTeam(team);
            var old = p.State.Team;
            p.State.Team = team;
            p.State.Alive = false;
            if (Config.FillBots && TeamCount(old) < Config.TeamSize) AddBot(NextBotName(), old);
            if (Phase == GamePhase.Freeze || Phase == GamePhase.Warmup) SpawnForRound(p, false, PickSpawn(team, null));
            BroadcastPlayerInfo(p);
        }

        // ---------------- spawning ----------------
        private SpawnPoint PickSpawn(Team team, HashSet<int> used)
        {
            // team spawns for team modes, deathmatch spawns (or any) otherwise
            var list = Map.Spawns.Select((sp, i) => (sp, i)).Where(x => x.sp.Team == team).ToList();
            if (list.Count == 0) list = Map.Spawns.Select((sp, i) => (sp, i)).Where(x => x.sp.Team == Team.None).ToList();
            if (list.Count == 0) list = Map.Spawns.Select((sp, i) => (sp, i)).ToList();
            if (list.Count == 0) return new SpawnPoint();
            if (team == Team.None || used == null)
            {
                // farthest from living players (deathmatch style)
                SpawnPoint best = list[0].sp; float bestD = -1;
                for (int k = 0; k < Math.Min(12, list.Count * 2); k++)
                {
                    var c = list[_rng.Next(list.Count)].sp;
                    float d = 999;
                    foreach (var o in _players) if (o.State.Alive) d = Math.Min(d, Vector3.Distance(o.State.Position, c.Position));
                    if (d > bestD) { bestD = d; best = c; }
                }
                return best;
            }
            var free = list.Where(x => !used.Contains(x.i)).ToList();
            var pick = free.Count > 0 ? free[_rng.Next(free.Count)] : list[_rng.Next(list.Count)];
            used.Add(pick.i);
            return pick.sp;
        }

        private void SpawnForRound(Player p, bool keepInventory, SpawnPoint sp)
        {
            var old = p.State;
            var team = p.State.Team == Team.None ? Team.T : p.State.Team;
            var s = PlayerState.Spawn(team, sp.Position, sp.Yaw);
            s.Team = p.State.Team;
            if (keepInventory)
            {
                s.Primary = old.Primary; s.Secondary = old.Secondary;
                s.Armor = old.Armor; s.Helmet = old.Helmet; s.HasKit = old.HasKit;
                s.NadeHE = old.NadeHE; s.NadeFlash = old.NadeFlash; s.NadeSmoke = old.NadeSmoke; s.NadeFire = old.NadeFire; s.NadeDecoy = old.NadeDecoy;
            }
            else { s.Armor = 0; s.Helmet = false; }
            if (Config.FreeArmor) { s.Armor = 100; s.Helmet = true; }
            if (Config.FreeKit && s.Team == Team.CT) s.HasKit = true;
            s.Active = s.BestSlot; s.LastActive = WeaponSlotKind.Melee;
            s.ShotCounter = old.ShotCounter;
            s.Yaw = sp.Yaw;
            p.State = s;
            p.RespawnAt = -1;
            p.SpawnTime = Tick * (double)Dt;
            p.DamageTaken.Clear(); p.RoundKills = 0; p.RoundDamage = 0;
            p.Bot?.OnSpawn();
        }

        private void Respawn(Player p)
        {
            var sp = PickSpawn(p.State.Team, null);
            SpawnForRound(p, false, sp);
            p.State.Armor = 100; p.State.Helmet = true;
            p.Money = Config.MaxMoney;
            if (Config.Mode == GameMode.Deathmatch)
                foreach (var id in p.DmLoadout) ApplyItem(p, id, true);
            if (Config.Mode == GameMode.Practice && p.State.Team == Team.T && (Bomb.State == BombState.None || Bomb.State == BombState.Defused || Bomb.State == BombState.Exploded))
            {
                p.State.HasC4 = true;
                Bomb = new BombInfo { State = BombState.Carried, CarrierId = p.Id };
            }
        }

        // ---------------- round flow ----------------
        private void StartRound()
        {
            Round = RoundsPlayed + 1;
            Phase = GamePhase.Freeze;
            PhaseEndTick = Tick + (int)(Config.FreezeTime * TickRate);
            BuyEndTick = PhaseEndTick + (int)(Config.BuyTime * TickRate);
            ClearGrenades();
            WorldItems.Clear();
            Bomb = new BombInfo();
            var used = new HashSet<int>();
            foreach (var p in _players)
            {
                if (p.State.Team == Team.None) continue;
                bool keep = p.State.Alive && !_resetInventories;
                SpawnForRound(p, keep, PickSpawn(p.State.Team, used));
            }
            _resetInventories = false;
            if (_knifeRound) PrepareKnifeRound();
            var ts = _players.Where(p => p.State.Team == Team.T && p.State.Alive).ToList();
            if (ts.Count > 0 && !_knifeRound)
            {
                var carrier = ts[_rng.Next(ts.Count)];
                carrier.State.HasC4 = true;
                Bomb = new BombInfo { State = BombState.Carried, CarrierId = carrier.Id };
            }
            ServerBot.PlanRound(this);
            if (!_knifeRound) foreach (var p in _players) if (p.Bot != null) BotBuy(p);
            Broadcast(new GameEvent { Type = GameEventType.RoundStart, A = Round });
            ApplyPendingPause();
            SendMatchStateAll();
        }

        private void StepMatch()
        {
            switch (Phase)
            {
                case GamePhase.Warmup:
                    StepWarmup();
                    break;
                case GamePhase.Freeze:
                    StepPause();
                    if (Tick >= PhaseEndTick)
                    {
                        if (_activePause == PauseKind.Tactical) _activePause = PauseKind.None;
                        Phase = GamePhase.Live;
                        PhaseEndTick = Tick + (int)(Config.RoundTime * TickRate);
                        BuyEndTick = Tick + (int)(Config.BuyTime * TickRate);
                        SendMatchStateAll();
                    }
                    break;
                case GamePhase.Live:
                    StepLive();
                    break;
                case GamePhase.RoundEnd:
                    if (Tick >= PhaseEndTick) NextRound();
                    break;
            }
            StepItems();
            if (Tick >= _nextScoreboardTick) { _nextScoreboardTick = Tick + TickRate / 2; SendScoreboards(); }
        }

        private void StepLive()
        {
            double now = Tick * (double)Dt;
            if (!Config.HasRounds)
            {
                foreach (var p in _players)
                    if (!p.State.Alive && p.RespawnAt >= 0 && now >= p.RespawnAt) Respawn(p);
                if (Config.Mode == GameMode.Deathmatch && Tick >= PhaseEndTick) EndMatch(Team.None);
                StepBomb();
                return;
            }
            StepBomb();
            if (Phase != GamePhase.Live) return;
            if (_knifeRound && Tick >= PhaseEndTick) { EndRound(KnifeTimeWinner(), RoundEndReason.TimeExpired); return; }
            int totalT = TeamCount(Team.T), totalCT = TeamCount(Team.CT);
            int aliveT = _players.Count(p => p.State.Team == Team.T && p.State.Alive);
            int aliveCT = _players.Count(p => p.State.Team == Team.CT && p.State.Alive);
            if (Bomb.State == BombState.Planted)
            {
                if (totalCT > 0 && aliveCT == 0) EndRound(Team.T, RoundEndReason.Elimination);
                return;
            }
            if (totalT > 0 && aliveT == 0) EndRound(Team.CT, RoundEndReason.Elimination);
            else if (totalCT > 0 && aliveCT == 0) EndRound(Team.T, RoundEndReason.Elimination);
            else if (Tick >= PhaseEndTick) EndRound(Team.CT, RoundEndReason.TimeExpired);
        }

        private void EndRound(Team winner, RoundEndReason reason)
        {
            if (Phase != GamePhase.Live) return;
            Phase = GamePhase.RoundEnd;
            PhaseEndTick = Tick + (int)(Config.RoundEndDelay * TickRate);
            if (EndKnifeRound(winner, reason)) return;
            var loser = winner == Team.CT ? Team.T : Team.CT;
            _score[(int)winner]++;
            RoundsPlayed++;
            History.Add(new RoundResult { Winner = winner, Reason = reason });
            RecordRound(winner, reason);
            bool planted = Bomb.State == BombState.Planted || Bomb.State == BombState.Exploded || Bomb.State == BombState.Defused;

            // ---- economy (CS2) ----
            int winMoney = reason == RoundEndReason.BombExploded || reason == RoundEndReason.BombDefused ? 3500 : 3250;
            int lossMoney = 1400 + 500 * Math.Min(_loss[(int)loser], 4);
            foreach (var p in _players)
            {
                if (p.State.Team == winner) AddMoney(p, winMoney);
                else if (p.State.Team == loser)
                {
                    // terrorists who survive a time-out get no loss bonus
                    if (!(reason == RoundEndReason.TimeExpired && p.State.Team == Team.T && p.State.Alive)) AddMoney(p, lossMoney);
                    if (p.State.Team == Team.T && planted) AddMoney(p, 800);
                }
            }
            _loss[(int)loser] = Math.Min(_loss[(int)loser] + 1, 4);
            _loss[(int)winner] = Math.Max(_loss[(int)winner] - 1, 0);

            // ---- MVP ----
            Player mvp = null; byte mvpReason = 0;
            if (reason == RoundEndReason.BombExploded) { mvp = GetPlayer(Bomb.CarrierId); mvpReason = 1; }
            else if (reason == RoundEndReason.BombDefused) { mvp = GetPlayer(Bomb.DefuserId); mvpReason = 2; }
            if (mvp == null || mvp.State.Team != winner)
            {
                mvp = _players.Where(p => p.State.Team == winner).OrderByDescending(p => p.RoundKills).ThenByDescending(p => p.RoundDamage).FirstOrDefault();
                mvpReason = 0;
            }
            if (mvp != null) mvp.Mvps++;

            _w.Reset(); _w.Byte((byte)Msg.RoundEnd); _w.Byte((byte)winner); _w.Byte((byte)reason); _w.Byte((byte)(mvp?.Id ?? 0)); _w.Byte(mvpReason); _w.Byte((byte)(mvp?.RoundKills ?? 0));
            SendAll(Delivery.ReliableOrdered);
            Info($"round {Round} -> {winner} ({reason})  {_score[1]}:{_score[2]} (T:CT)");
            RoundEnded?.Invoke(winner, reason);
            SendMatchStateAll();
        }

        private Team MatchWinner()
        {
            int t = _score[(int)Team.T], c = _score[(int)Team.CT];
            if (Config.Mode == GameMode.Competitive)
            {
                int r = RoundsPlayed;
                int target;
                if (r <= Config.MaxRounds) target = Config.WinRounds;
                else
                {
                    if (!Config.Overtime) return t == c ? Team.None : (t > c ? Team.T : Team.CT);
                    int k = (r - Config.MaxRounds + 5) / 6;     // overtime number (MR3 halves)
                    target = Config.WinRounds - 1 + 3 * (k - 1) + 4;
                }
                if (t >= target) return Team.T;
                if (c >= target) return Team.CT;
                if (r >= Config.MaxRounds && !Config.Overtime && t != c) return t > c ? Team.T : Team.CT;
                return Team.None;
            }
            if (t >= Config.WinRounds) return Team.T;
            if (c >= Config.WinRounds) return Team.CT;
            if (RoundsPlayed >= Config.MaxRounds) return t > c ? Team.T : (c > t ? Team.CT : Team.None);
            return Team.None;
        }

        public bool IsDraw { get; private set; }

        private void NextRound()
        {
            if (ResolveSidePick()) return;
            var w = MatchWinner();
            bool drawAtEnd = Config.Mode == GameMode.Casual && RoundsPlayed >= Config.MaxRounds && w == Team.None;
            if (w != Team.None || drawAtEnd) { IsDraw = drawAtEnd; EndMatch(w); return; }
            int r = RoundsPlayed;
            if (r == Config.HalfRounds) { SwapTeams(Config.StartMoney); Broadcast(new GameEvent { Type = GameEventType.Halftime, Text = "DEVRE ARASI" }); }
            else if (Config.Mode == GameMode.Competitive && Config.Overtime && r >= Config.MaxRounds && (r - Config.MaxRounds) % 3 == 0)
            {
                if ((r - Config.MaxRounds) % 6 == 0)
                {
                    foreach (var p in _players) p.Money = Config.OvertimeMoney;
                    _resetInventories = true; _loss[1] = _loss[2] = 1;
                    Broadcast(new GameEvent { Type = GameEventType.Message, Text = "UZATMA — herkese $" + Config.OvertimeMoney });
                }
                else { SwapTeams(Config.OvertimeMoney); Broadcast(new GameEvent { Type = GameEventType.Halftime, Text = "UZATMA DEVRE ARASI" }); }
            }
            StartRound();
        }

        private void SwapTeams(int money)
        {
            foreach (var p in _players)
            {
                if (p.State.Team == Team.None) continue;
                p.State.Team = p.State.Team == Team.T ? Team.CT : Team.T;
                p.Money = money;
                BroadcastPlayerInfo(p);
            }
            _teamAIsT = !_teamAIsT;
            int t = _score[1]; _score[1] = _score[2]; _score[2] = t;
            for (int i = 0; i < History.Count; i++) { var h = History[i]; h.Winner = h.Winner == Team.T ? Team.CT : Team.T; History[i] = h; }
            _loss[1] = _loss[2] = 1;
            _resetInventories = true;
        }

        private void EndMatch(Team winner)
        {
            Phase = GamePhase.MatchOver;
            PhaseEndTick = int.MaxValue;
            int a = (int)winner;
            if (Config.Mode == GameMode.Deathmatch) a = _players.OrderByDescending(p => p.Kills).FirstOrDefault()?.Id ?? 0;
            Broadcast(new GameEvent { Type = GameEventType.MatchOver, A = a, B = IsDraw ? 1 : 0 });
            SendMatchStateAll();
            SendScoreboards();
            Info($"match over: {winner} {_score[1]}:{_score[2]}");
            PublishResults(winner);
            StopRecording();
        }

        // ---------------- kills & economy ----------------
        private void AddMoney(Player p, int amount)
        {
            p.Money = Math.Max(0, Math.Min(Config.MaxMoney, p.Money + amount));
        }

        private void OnKilled(Player victim, Player attacker, WeaponDef def, bool headshot)
        {
            victim.Deaths++;
            DropOnDeath(victim);
            if (attacker != null && attacker != victim)
            {
                bool enemy = victim.State.Team == Team.None || victim.State.Team != attacker.State.Team;
                if (enemy)
                {
                    attacker.Kills++; attacker.RoundKills++; attacker.Score += 2;
                    if (headshot) attacker.Headshots++;
                    if (Config.HasEconomy && def != null) AddMoney(attacker, def.KillReward);
                }
                else
                {
                    attacker.Kills--; attacker.Score -= 2;
                    if (Config.HasEconomy) AddMoney(attacker, -300);
                }
            }
            else victim.Score -= 1;
            foreach (var kv in victim.DamageTaken)
            {
                if (attacker != null && kv.Key == attacker.Id) continue;
                var a = GetPlayer(kv.Key);
                if (a == null || (victim.State.Team != Team.None && a.State.Team == victim.State.Team)) continue;
                if (kv.Value >= 41) { a.Assists++; a.Score += 1; }
            }
            if (!Config.HasRounds || Phase == GamePhase.Warmup) victim.RespawnAt = Tick * (double)Dt + Config.RespawnDelay;
            ServerBot.OnAnyDeath(this, victim, attacker);
        }

        // ---------------- buying ----------------
        public bool CanBuy(Player p, out string reason)
        {
            reason = null;
            if (!p.State.Alive) { reason = "Ölüyken satın alamazsın."; return false; }
            if (_knifeRound) { reason = "Bıçak raundunda satın alma yok."; return false; }
            if (Phase == GamePhase.Warmup && Config.HasRounds) return true; // warmup: buy anywhere
            if (Config.Mode == GameMode.Practice) return true;
            if (Config.Mode == GameMode.Deathmatch)
            {
                if (Tick * (double)Dt - p.SpawnTime > 15) { reason = "Satın alma süresi doldu."; return false; }
                return true;
            }
            if (!(Phase == GamePhase.Freeze || (Phase == GamePhase.Live && Tick <= BuyEndTick))) { reason = "Satın alma süresi doldu."; return false; }
            if (!InBuyZone(p)) { reason = "Satın alma bölgesinde değilsin."; return false; }
            return true;
        }

        public bool InBuyZone(Player p)
        {
            bool any = false;
            foreach (var (team, zone) in Map.BuyZones)
            {
                if (team != p.State.Team) continue;
                any = true;
                if (zone.Contains(p.State.Position)) return true;
            }
            if (any) return false;
            foreach (var sp in Map.Spawns)
                if (sp.Team == p.State.Team && Vector3.Distance(sp.Position, p.State.Position) < 15f) return true;
            return false;
        }

        public int PriceFor(Player p, ItemId item)
        {
            if (Config.Mode == GameMode.Deathmatch) return 0;
            if (item == ItemId.VestHelmet && p.State.Armor >= 100 && !p.State.Helmet) return Items.HelmetOnlyPrice;
            return Items.Price(item);
        }

        public bool TryBuy(Player p, ItemId item)
        {
            string fail = Validate(p, item);
            if (fail == null)
            {
                int price = PriceFor(p, item);
                if (price > p.Money) fail = "Yeterli paran yok.";
                else
                {
                    ApplyItem(p, item, false);
                    if (Config.Mode != GameMode.Practice) p.Money -= price;
                    if (Config.Mode == GameMode.Deathmatch && Items.IsWeapon(item))
                    {
                        var slot = Weapons.Get(Items.ToWeapon(item)).Slot;
                        p.DmLoadout.RemoveAll(x => Items.IsWeapon(x) && Weapons.Get(Items.ToWeapon(x)).Slot == slot);
                        p.DmLoadout.Add(item);
                    }
                    if (!p.IsBot) SendEvent(p, new GameEvent { Type = GameEventType.Purchase, A = (int)item, B = p.Money });
                    return true;
                }
            }
            if (!p.IsBot) SendEvent(p, new GameEvent { Type = GameEventType.PurchaseDenied, A = (int)item, Text = fail });
            return false;
        }

        private string Validate(Player p, ItemId item)
        {
            if (!CanBuy(p, out var reason)) return reason;
            var team = Items.TeamOf(item);
            if (team != Team.None && Config.Mode != GameMode.Deathmatch && team != p.State.Team) return "Bu eşya takımına ait değil.";
            ref var s = ref p.State;
            if (Items.IsWeapon(item))
            {
                var def = Weapons.Get(Items.ToWeapon(item));
                if (def.Slot != WeaponSlotKind.Primary && def.Slot != WeaponSlotKind.Secondary) return "Bu eşya satılmıyor.";
                if (s.GetSlot(def.Slot).Id == def.Id) return "Zaten sende var.";
                return null;
            }
            if (Items.IsGrenade(item))
            {
                var g = Items.ToGrenade(item);
                if (!s.CanTakeGrenade(g)) return s.GrenadeTotal >= Items.MaxGrenades ? "En fazla 4 bomba taşıyabilirsin." : "Bu bombadan daha fazla taşıyamazsın.";
                return null;
            }
            switch (item)
            {
                case ItemId.Vest: return s.Armor >= 100 ? "Zırhın zaten tam." : null;
                case ItemId.VestHelmet: return s.Armor >= 100 && s.Helmet ? "Zırhın ve kaskın zaten var." : null;
                case ItemId.DefuseKit: return s.Team != Team.CT ? "Sadece CT alabilir." : s.HasKit ? "Kitin zaten var." : null;
            }
            return "Bilinmeyen eşya.";
        }

        private void ApplyItem(Player p, ItemId item, bool free)
        {
            ref var s = ref p.State;
            float time = p.PlayerTime(Dt);
            if (Items.IsWeapon(item))
            {
                var def = Weapons.Get(Items.ToWeapon(item));
                var old = s.GetSlot(def.Slot);
                if (!old.IsEmpty && old.Id != def.Id) SpawnItem(old, s.EyePosition - new Vector3(0, 0.6f, 0), VMath.FlatForward(s.Yaw) * 2f + new Vector3(0, 1.5f, 0), p.Id);
                WeaponLogic.Give(ref s, def.Id, time);
            }
            else if (Items.IsGrenade(item))
            {
                var g = Items.ToGrenade(item);
                if (s.CanTakeGrenade(g)) s.AddGrenade(g, 1);
            }
            else if (item == ItemId.Vest) s.Armor = 100;
            else if (item == ItemId.VestHelmet) { s.Armor = 100; s.Helmet = true; }
            else if (item == ItemId.DefuseKit) s.HasKit = true;
        }

        // ---------------- bomb ----------------
        public bool BombPlanted => Bomb.State == BombState.Planted;

        internal void PlantBomb(Player p, Vector3 pos, string site)
        {
            if (Phase != GamePhase.Live || Bomb.State == BombState.Planted) return;
            Bomb = new BombInfo
            {
                State = BombState.Planted, CarrierId = p.Id, Position = pos + VMath.FlatForward(p.State.Yaw) * 0.3f, Site = site,
                PlantTick = Tick, ExplodeTick = Tick + (int)(Config.BombTime * TickRate),
            };
            if (Config.HasEconomy) AddMoney(p, 300);
            p.Score += 2;
            Broadcast(new GameEvent { Type = GameEventType.BombPlanted, A = p.Id, Position = Bomb.Position, Text = site });
            Info($"{p.Name} planted the bomb at {site}");
            ServerBot.OnBombPlanted(this);
        }

        private void StepBomb()
        {
            switch (Bomb.State)
            {
                case BombState.Carried:
                    {
                        var c = GetPlayer(Bomb.CarrierId);
                        if (c == null || !c.State.Alive || !c.State.HasC4) { if (c != null && c.State.HasC4) DropBomb(c); else Bomb.State = c == null ? BombState.None : Bomb.State; }
                        else Bomb.Position = c.State.Position;
                        break;
                    }
                case BombState.Dropped:
                    foreach (var p in _players)
                    {
                        if (!p.State.Alive || p.State.Team != Team.T) continue;
                        var d = p.State.Position - Bomb.Position;
                        if (d.X * d.X + d.Z * d.Z < 1.0f && MathF.Abs(d.Y) < 1.3f)
                        {
                            p.State.HasC4 = true;
                            Bomb.State = BombState.Carried; Bomb.CarrierId = p.Id;
                            Broadcast(new GameEvent { Type = GameEventType.BombPickedUp, A = p.Id });
                            break;
                        }
                    }
                    break;
                case BombState.Planted:
                    StepDefuse();
                    if (Bomb.State == BombState.Planted && Tick >= Bomb.ExplodeTick) ExplodeBomb();
                    break;
            }
        }

        private void StepDefuse()
        {
            const float range = 1.6f;
            var d = GetPlayer(Bomb.DefuserId);
            if (d != null && Bomb.DefuseEndTick > 0)
            {
                bool ok = d.State.Alive && (d.LastButtons & Buttons.Use) != 0 && Vector3.Distance(d.State.Position, Bomb.Position) < range && Phase == GamePhase.Live;
                if (!ok) { d.State.Defusing = false; Bomb.DefuserId = 0; Bomb.DefuseEndTick = 0; Bomb.DefuseStartTick = 0; }
                else if (Tick >= Bomb.DefuseEndTick)
                {
                    d.State.Defusing = false;
                    Bomb.State = BombState.Defused;
                    if (Config.HasEconomy) AddMoney(d, 300);
                    d.Score += 2;
                    Broadcast(new GameEvent { Type = GameEventType.BombDefused, A = d.Id, Position = Bomb.Position });
                    Info($"{d.Name} defused the bomb");
                    if (Config.HasRounds) EndRound(Team.CT, RoundEndReason.BombDefused);
                    else Bomb = new BombInfo();
                    return;
                }
                return;
            }
            foreach (var p in _players)
            {
                if (!p.State.Alive || p.State.Team != Team.CT || (p.LastButtons & Buttons.Use) == 0 || !p.State.OnGround) continue;
                if (Vector3.Distance(p.State.Position, Bomb.Position) > range) continue;
                Bomb.DefuserId = p.Id;
                Bomb.DefuseStartTick = Tick;
                Bomb.DefuseEndTick = Tick + (int)((p.State.HasKit ? Config.KitDefuseTime : Config.DefuseTime) * TickRate);
                p.State.Defusing = true;
                p.State.Velocity = Vector3.Zero;
                Broadcast(new GameEvent { Type = GameEventType.DefuseStarted, A = p.Id, B = p.State.HasKit ? 1 : 0 });
                break;
            }
        }

        private void ExplodeBomb()
        {
            var planter = GetPlayer(Bomb.CarrierId);
            Bomb.State = BombState.Exploded;
            if (GetPlayer(Bomb.DefuserId) is Player df) df.State.Defusing = false;
            Broadcast(new GameEvent { Type = GameEventType.BombExploded, Position = Bomb.Position });
            float sigma = 1750f * VMath.HU / 3f;
            var def = Weapons.Get(WeaponId.C4);
            foreach (var p in _players.ToArray())
            {
                if (!p.State.Alive) continue;
                float dist = Vector3.Distance(p.State.Position, Bomb.Position);
                float dmg = 500f * MathF.Exp(-(dist * dist) / (2f * sigma * sigma));
                if (dmg < 1f) continue;
                var r = DamageModel.ApplyArmor(dmg, 0.5f, HitGroup.Chest, p.State.Armor, p.State.Helmet);
                ApplyDamage(null, p, def, HitGroup.Chest, r.Health, r.Armor, false, p.State.Position);
            }
            if (Config.HasRounds) EndRound(Team.T, RoundEndReason.BombExploded);
            else Bomb = new BombInfo();
            _ = planter;
        }

        private void DropBomb(Player p)
        {
            if (!p.State.HasC4) return;
            p.State.HasC4 = false;
            if (p.State.Active == WeaponSlotKind.Bomb) WeaponLogic.SelectSlot(ref p.State, p.State.BestSlot, p.PlayerTime(Dt));
            var pos = p.State.Position + VMath.FlatForward(p.State.Yaw) * (p.State.Alive ? 0.9f : 0.2f);
            if (World.Raycast(pos + new Vector3(0, 1f, 0), new Vector3(0, -1, 0), 10f, out var hit)) pos = hit.Point;
            Bomb = new BombInfo { State = BombState.Dropped, Position = pos };
            Broadcast(new GameEvent { Type = GameEventType.BombDropped, A = p.Id, Position = pos });
        }

        // ---------------- dropped weapons ----------------
        private void SpawnItem(WeaponSlot w, Vector3 pos, Vector3 vel, int owner)
        {
            if (w.IsEmpty || w.Id == WeaponId.Knife) return;
            WorldItems.Add(new WorldItem { Id = _nextItemId++ & 0xFFFF, Weapon = w, Position = pos, Velocity = vel, OwnerId = owner, NoPickupUntil = Tick + TickRate });
            if (WorldItems.Count > 40) WorldItems.RemoveAt(0);
        }

        private void DropOnDeath(Player p)
        {
            var s = p.State;
            var w = !s.Primary.IsEmpty ? s.Primary : s.Secondary;
            if (!w.IsEmpty) SpawnItem(w, s.Position + new Vector3(0, 0.8f, 0), new Vector3(0, 1f, 0), p.Id);
            if (s.HasC4) DropBomb(p);
            p.State.Primary = default; p.State.Secondary = default;
            p.State.NadeHE = p.State.NadeFlash = p.State.NadeSmoke = p.State.NadeFire = p.State.NadeDecoy = 0;
            p.State.HasKit = false; p.State.Armor = 0; p.State.Helmet = false;
        }

        private void DropActive(Player p)
        {
            ref var s = ref p.State;
            if (s.Active == WeaponSlotKind.Bomb) { DropBomb(p); return; }
            if (s.Active != WeaponSlotKind.Primary && s.Active != WeaponSlotKind.Secondary) return;
            var w = s.GetSlot(s.Active);
            s.SetSlot(s.Active, default);
            SpawnItem(w, s.EyePosition - new Vector3(0, 0.3f, 0), VMath.Forward(s.Yaw, s.Pitch) * 3.5f + new Vector3(0, 1f, 0) + s.Velocity, p.Id);
            WeaponLogic.SelectSlot(ref s, s.BestSlot, p.PlayerTime(Dt));
        }

        private void UsePickup(Player p)
        {
            ref var s = ref p.State;
            var eye = s.EyePosition; var fwd = VMath.Forward(s.Yaw, s.Pitch);
            WorldItem best = null; float bestD = 2.2f;
            foreach (var it in WorldItems)
            {
                var to = it.Position - eye; float d = to.Length();
                if (d > bestD || d < 1e-3f) continue;
                if (Vector3.Dot(to / d, fwd) < 0.75f) continue;
                best = it; bestD = d;
            }
            if (best == null) return;
            var def = Weapons.Get(best.Weapon.Id);
            var old = s.GetSlot(def.Slot);
            WorldItems.Remove(best);
            if (!old.IsEmpty) SpawnItem(old, eye - new Vector3(0, 0.3f, 0), fwd * 2f + new Vector3(0, 1f, 0), p.Id);
            s.SetSlot(def.Slot, best.Weapon);
            s.Active = WeaponSlotKind.None;
            WeaponLogic.SelectSlot(ref s, def.Slot, p.PlayerTime(Dt));
        }

        private void StepItems()
        {
            for (int i = WorldItems.Count - 1; i >= 0; i--)
            {
                var it = WorldItems[i];
                if (!it.Rest)
                {
                    it.Velocity.Y -= SimConstants.Gravity * Dt;
                    var step = it.Velocity * Dt;
                    float len = step.Length();
                    if (len > 1e-5f && World.Raycast(it.Position, step / len, len + 0.05f, out var hit))
                    {
                        it.Position = hit.Point + hit.Normal * 0.05f;
                        if (hit.Normal.Y > 0.7f) { it.Rest = true; it.Velocity = Vector3.Zero; }
                        else it.Velocity = Vector3.Reflect(it.Velocity, hit.Normal) * 0.3f;
                    }
                    else it.Position += step;
                    if (it.Position.Y < -50) { WorldItems.RemoveAt(i); continue; }
                }
                // walk-over pickup into an empty slot
                foreach (var p in _players)
                {
                    if (!p.State.Alive || (p.Id == it.OwnerId && Tick < it.NoPickupUntil)) continue;
                    var d = p.State.Position - it.Position;
                    if (d.X * d.X + d.Z * d.Z > 1.0f || d.Y > 0.4f || d.Y < -1.5f) continue;
                    var def = Weapons.Get(it.Weapon.Id);
                    if (!p.State.GetSlot(def.Slot).IsEmpty) continue;
                    p.State.SetSlot(def.Slot, it.Weapon);
                    if (p.State.Active == WeaponSlotKind.Melee || (p.State.Active == WeaponSlotKind.Secondary && def.Slot == WeaponSlotKind.Primary))
                        WeaponLogic.SelectSlot(ref p.State, def.Slot, p.PlayerTime(Dt));
                    WorldItems.RemoveAt(i);
                    break;
                }
            }
        }

        private void WriteItemsSnapshot(NetWriter w)
        {
            int n = Math.Min(WorldItems.Count, 40);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) { var it = WorldItems[i]; w.UShort((ushort)it.Id); w.Byte((byte)it.Weapon.Id); w.Vec3(it.Position); }
        }

        // ---------------- state messages ----------------
        private void WriteMatchState()
        {
            _w.Reset(); _w.Byte((byte)Msg.MatchState);
            _w.Byte((byte)Config.Mode); _w.Byte((byte)Round); _w.Byte((byte)_score[1]); _w.Byte((byte)_score[2]);
            _w.Byte((byte)Config.MaxRounds); _w.Byte((byte)Config.WinRounds); _w.Byte((byte)Config.HalfRounds);
            _w.Byte((byte)Math.Min(History.Count, 60));
            for (int i = Math.Max(0, History.Count - 60); i < History.Count; i++) { _w.Byte((byte)History[i].Winner); _w.Byte((byte)History[i].Reason); }
            WriteTournamentState();
        }
        private void SendMatchState(Player p) { WriteMatchState(); SendTo(p, Delivery.ReliableOrdered); }
        private void SendMatchStateAll() { WriteMatchState(); SendAll(Delivery.ReliableOrdered); }

        private void SendScoreboards()
        {
            foreach (var to in _players)
            {
                if (to.Peer < 0) continue;
                _w.Reset(); _w.Byte((byte)Msg.Scoreboard); _w.Byte((byte)_players.Count(x => !x.Hidden));
                foreach (var p in _players)
                {
                    if (p.Hidden) continue;
                    bool showMoney = Config.Mode != GameMode.Competitive && Config.Mode != GameMode.Casual || p.State.Team == to.State.Team || to.State.Team == Team.None || Phase == GamePhase.MatchOver;
                    Protocol.WriteScore(_w, new ScoreEntry
                    {
                        Id = p.Id, Team = p.State.Team, Alive = p.State.Alive, IsBot = p.IsBot, Money = showMoney ? p.Money : -1,
                        Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists, Mvps = p.Mvps, Score = p.Score, Damage = p.Damage, Headshots = p.Headshots,
                    });
                }
                SendTo(to, Delivery.ReliableOrdered);
            }
        }

        // ---------------- bot helpers ----------------
        private void BotBuy(Player p) => ServerBot.Buy(this, p);
        internal bool BotTryBuy(Player p, ItemId item) => TryBuy(p, item);
        internal int LossCounter(Team t) => _loss[(int)t];
        internal Random Rng => _rng;
    }
}
