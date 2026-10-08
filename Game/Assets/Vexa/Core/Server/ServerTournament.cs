using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Esports layer: chat (all / team, dead players only reach the dead), chat commands, ready-up, knife round
    /// with side pick, tactical and technical pauses (taken at freeze time, like CS), team names and rosters,
    /// server console commands and a results JSON at the end of the match.
    /// </summary>
    public sealed partial class ServerGame
    {
        public enum PauseKind : byte { None, Tactical, Technical }

        private bool _waitingReady, _knifeRound, _knifeDone, _sidePick, _sidePickSwap;
        private Team _knifeWinner;
        private bool _teamAIsT = true;
        private PauseKind _pendingPause, _activePause;
        private Team _pauseTeam;
        private int _pauseHoldTicks;
        private readonly HashSet<Team> _unpauseVotes = new HashSet<Team>();
        private int _timeoutsA, _timeoutsB;
        private readonly Dictionary<int, double> _lastChat = new Dictionary<int, double>();
        private readonly List<(int round, bool teamAWon, Team winnerSide, RoundEndReason reason)> _roundLog = new List<(int, bool, Team, RoundEndReason)>();
        private DateTime _matchStartedUtc;

        /// <summary>Raised when a match ends, with the results JSON (also written to <see cref="MatchConfig.ResultsPath"/>).</summary>
        public event Action<string> MatchFinished;

        public Team SideOfA => _teamAIsT ? Team.T : Team.CT;
        public string TeamName(Team side)
        {
            if (side != Team.T && side != Team.CT) return "";
            if (!Config.Tournament) return side == Team.T ? "SALDIRANLAR" : "SAVUNANLAR";
            return side == SideOfA ? Config.TeamA : Config.TeamB;
        }
        public bool WaitingForReady => _waitingReady;
        public bool KnifeRoundActive => _knifeRound;
        public bool SidePickActive => _sidePick;
        public PauseKind ActivePause => _activePause;
        public PauseKind PendingPause => _pendingPause;
        public int TimeoutsLeft(Team side) => side == SideOfA ? _timeoutsA : _timeoutsB;

        private void InitTournament()
        {
            _teamAIsT = !Config.TeamAStartsCT;
            _timeoutsA = _timeoutsB = Config.TacticalTimeouts;
            _matchStartedUtc = DateTime.UtcNow;
            if (Config.Tournament && Config.HasRounds && Config.RequireReady)
            {
                _waitingReady = true;
                PhaseEndTick = int.MaxValue;
            }
        }

        // ---------------- joining ----------------

        /// <summary>Tournament rosters decide the side; players not on a roster join as spectators.</summary>
        private Team TeamForJoin(string name)
        {
            if (Config.Tournament && Config.HasRounds && (Config.RosterA.Count > 0 || Config.RosterB.Count > 0))
            {
                if (Config.RosterA.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return SideOfA;
                if (Config.RosterB.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return SideOfA == Team.T ? Team.CT : Team.T;
                return Team.None;
            }
            return AutoTeam();
        }

        private bool TeamsLocked => Config.Tournament && (Config.RosterA.Count > 0 || Config.RosterB.Count > 0);

        // ---------------- ready / warmup ----------------

        private IEnumerable<Player> TeamHumans => _players.Where(p => !p.IsBot && (p.State.Team == Team.T || p.State.Team == Team.CT));
        public int ReadyCount => TeamHumans.Count(p => p.MatchReady);
        public int ReadyNeeded => TeamHumans.Count();

        private void StepWarmup()
        {
            double now = Tick * (double)Dt;
            foreach (var p in _players)
                if (!p.State.Alive && p.State.Team != Team.None && p.RespawnAt >= 0 && now >= p.RespawnAt)
                {
                    SpawnForRound(p, false, PickSpawn(p.State.Team, null));
                    p.Money = Config.MaxMoney;
                }
            if (_waitingReady)
            {
                if (ReadyNeeded > 0 && ReadyCount == ReadyNeeded)
                {
                    _waitingReady = false;
                    PhaseEndTick = Tick + 5 * TickRate;
                    SystemChat("Herkes hazır. Maç 5 saniye içinde başlıyor.");
                    SendMatchStateAll();
                }
                return;
            }
            if (Tick >= PhaseEndTick) BeginMatch();
        }

        /// <summary>Clears everything warmup produced and starts round 1 (or the knife round).</summary>
        private void BeginMatch()
        {
            _score[1] = _score[2] = 0;
            _loss[1] = _loss[2] = 1;
            RoundsPlayed = 0;
            History.Clear();
            _roundLog.Clear();
            foreach (var p in _players)
            {
                p.Money = Config.StartMoney;
                p.Kills = p.Deaths = p.Assists = p.Mvps = p.Score = p.Damage = p.Headshots = 0;
            }
            _resetInventories = true;
            _knifeRound = Config.Tournament && Config.KnifeRound && Config.HasRounds && !_knifeDone;
            if (!_knifeRound) _matchStartedUtc = DateTime.UtcNow;
            StartRound();
        }

        // ---------------- knife round ----------------

        private void PrepareKnifeRound()
        {
            foreach (var p in _players)
            {
                if (p.State.Team == Team.None) continue;
                p.State.Primary = default; p.State.Secondary = default;
                p.State.NadeHE = p.State.NadeFlash = p.State.NadeSmoke = p.State.NadeFire = p.State.NadeDecoy = 0;
                p.State.HasC4 = false;
                p.State.Active = WeaponSlotKind.Melee; p.State.LastActive = WeaponSlotKind.Melee;
                p.State.Armor = 100; p.State.Helmet = true;
            }
            Bomb = new BombInfo();
            Broadcast(new GameEvent { Type = GameEventType.Message, Text = "BIÇAK RAUNDU — kazanan taraf seçer" });
        }

        private Team KnifeTimeWinner()
        {
            int Alive(Team t) => _players.Count(p => p.State.Team == t && p.State.Alive);
            int Hp(Team t) => _players.Where(p => p.State.Team == t && p.State.Alive).Sum(p => (int)p.State.Health);
            int at = Alive(Team.T), ac = Alive(Team.CT);
            if (at != ac) return at > ac ? Team.T : Team.CT;
            int ht = Hp(Team.T), hc = Hp(Team.CT);
            if (ht != hc) return ht > hc ? Team.T : Team.CT;
            return _rng.Next(2) == 0 ? Team.T : Team.CT;
        }

        /// <summary>Called from EndRound while the knife round is running. Returns true if handled.</summary>
        private bool EndKnifeRound(Team winner, RoundEndReason reason)
        {
            if (!_knifeRound) return false;
            _knifeRound = false;
            _knifeDone = true;
            _sidePick = true;
            _sidePickSwap = false;
            _knifeWinner = winner;
            bool botsOnly = !_players.Any(p => !p.IsBot && p.State.Team == winner);
            PhaseEndTick = Tick + (int)((botsOnly ? 3f : Config.SidePickTime) * TickRate);
            _w.Reset(); _w.Byte((byte)Msg.RoundEnd); _w.Byte((byte)winner); _w.Byte((byte)reason); _w.Byte(0); _w.Byte(0); _w.Byte(0);
            SendAll(Delivery.ReliableOrdered);
            SystemChat($"{TeamName(winner)} bıçak raundunu kazandı. Taraf seçimi: .stay (kal) veya .switch (değiştir). {(int)Config.SidePickTime} sn içinde seçilmezse taraf aynı kalır.");
            Info($"knife round won by {TeamName(winner)} ({winner})");
            SendMatchStateAll();
            return true;
        }

        /// <summary>Called from NextRound. Returns true if the side pick consumed the transition.</summary>
        private bool ResolveSidePick()
        {
            if (!_sidePick) return false;
            _sidePick = false;
            if (_sidePickSwap)
            {
                SwapTeams(Config.StartMoney);
                SystemChat($"{TeamName(_knifeWinner == Team.T ? Team.CT : Team.T)} taraf değiştirmeyi seçti.");
            }
            else SystemChat("Taraflar aynı kalıyor.");
            BeginMatch();
            return true;
        }

        // ---------------- pauses ----------------

        private void RequestPause(PauseKind kind, Team team, string by)
        {
            if (_activePause != PauseKind.None || _pendingPause != PauseKind.None) { SystemChat("Zaten bir mola var ya da istendi."); return; }
            if (kind == PauseKind.Tactical)
            {
                if (team != Team.T && team != Team.CT) return;
                if (TimeoutsLeft(team) <= 0) { SystemChat($"{TeamName(team)} için taktik mola hakkı kalmadı."); return; }
            }
            _pendingPause = kind;
            _pauseTeam = team;
            string what = kind == PauseKind.Tactical ? "taktik mola" : "teknik duraklatma";
            SystemChat(Phase == GamePhase.Freeze ? $"{by}: {what}." : $"{by} {what} istedi; sonraki donma süresinde başlayacak.");
            if (Phase == GamePhase.Freeze) ApplyPendingPause();
            SendMatchStateAll();
        }

        private void ApplyPendingPause()
        {
            if (_pendingPause == PauseKind.None || Phase != GamePhase.Freeze) return;
            _activePause = _pendingPause;
            _pendingPause = PauseKind.None;
            if (_activePause == PauseKind.Tactical)
            {
                if (_pauseTeam == SideOfA) _timeoutsA--; else _timeoutsB--;
                int add = (int)(Config.TacticalTimeoutTime * TickRate);
                PhaseEndTick += add; BuyEndTick += add;
                SystemChat($"{TeamName(_pauseTeam)} taktik mola aldı ({(int)Config.TacticalTimeoutTime} sn). Kalan hak: {TimeoutsLeft(_pauseTeam)}");
            }
            else
            {
                _pauseHoldTicks = Math.Max(TickRate, PhaseEndTick - Tick);
                _unpauseVotes.Clear();
                SystemChat("Maç duraklatıldı. Devam etmek için iki takım da .unpause yazmalı.");
            }
            SendMatchStateAll();
        }

        /// <summary>Freeze-time upkeep: a technical pause holds the clock still.</summary>
        private void StepPause()
        {
            if (_activePause != PauseKind.Technical) return;
            int buyLead = BuyEndTick - PhaseEndTick;
            PhaseEndTick = Tick + _pauseHoldTicks;
            BuyEndTick = PhaseEndTick + buyLead;
        }

        private void Unpause(Team team, bool admin)
        {
            if (_pendingPause != PauseKind.None && (admin || team == _pauseTeam))
            {
                _pendingPause = PauseKind.None;
                SystemChat("Mola isteği iptal edildi.");
                SendMatchStateAll();
                return;
            }
            if (_activePause != PauseKind.Technical) return;
            if (!admin)
            {
                _unpauseVotes.Add(team);
                bool HasHumans(Team t) => _players.Any(p => !p.IsBot && p.State.Team == t);
                bool tOk = _unpauseVotes.Contains(Team.T) || !HasHumans(Team.T);
                bool ctOk = _unpauseVotes.Contains(Team.CT) || !HasHumans(Team.CT);
                if (!(tOk && ctOk)) { SystemChat($"{TeamName(team)} devam etmeye hazır. Diğer takım bekleniyor."); return; }
            }
            _activePause = PauseKind.None;
            SystemChat("Maç devam ediyor.");
            SendMatchStateAll();
        }

        // ---------------- chat ----------------

        private void OnChat(Player p, bool teamOnly, string text)
        {
            if (text == null) return;
            text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length == 0) return;
            if (text.Length > 120) text = text.Substring(0, 120);
            double now = Tick * (double)Dt;
            if (_lastChat.TryGetValue(p.Id, out var last) && now - last < 0.4) return; // flood control
            _lastChat[p.Id] = now;

            bool deadChat = !p.State.Alive && Config.HasRounds && Phase == GamePhase.Live;
            var msg = new ChatMessage { SenderId = p.Id, SenderTeam = p.State.Team, TeamOnly = teamOnly, Dead = deadChat, Text = text };
            Protocol.WriteChat(_w, msg);
            foreach (var to in _players)
            {
                if (to.Peer < 0) continue;
                if (teamOnly && to.State.Team != p.State.Team) continue;
                // like CS competitive: the dead can't talk to the living during a live round
                if (deadChat && to.State.Alive && to.State.Team != Team.None) continue;
                SendTo(to, Delivery.ReliableOrdered);
            }
            Info($"{(teamOnly ? "[takım] " : "")}{p.Name}: {text}");
            if (text[0] == '.' || text[0] == '!') ChatCommand(p, text.Substring(1));
        }

        /// <summary>Server message in chat (sender id 0), to everyone or one player.</summary>
        public void SystemChat(string text, Player to = null)
        {
            Protocol.WriteChat(_w, new ChatMessage { SenderId = 0, Text = text });
            if (to != null) SendTo(to, Delivery.ReliableOrdered);
            else SendAll(Delivery.ReliableOrdered);
            Info("[server] " + text);
        }

        private void ChatCommand(Player p, string cmd)
        {
            var parts = cmd.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var team = p.State.Team;
            switch (parts[0])
            {
                case "ready": case "r": case "hazır": case "hazir":
                    if (!_waitingReady || p.MatchReady || team == Team.None) return;
                    p.MatchReady = true;
                    SystemChat($"{p.Name} hazır ({ReadyCount}/{ReadyNeeded}).");
                    SendMatchStateAll();
                    break;
                case "unready": case "notready":
                    if (!_waitingReady || !p.MatchReady) return;
                    p.MatchReady = false;
                    SystemChat($"{p.Name} artık hazır değil ({ReadyCount}/{ReadyNeeded}).");
                    SendMatchStateAll();
                    break;
                case "stay": case "kal":
                case "switch": case "swap": case "değiştir": case "degistir":
                    if (!_sidePick || team != _knifeWinner) return;
                    _sidePickSwap = parts[0] != "stay" && parts[0] != "kal";
                    PhaseEndTick = Tick; // resolve on the next tick
                    break;
                case "tac": case "timeout": case "mola":
                    if (!Config.Tournament || !Config.HasRounds || team == Team.None) return;
                    RequestPause(PauseKind.Tactical, team, TeamName(team));
                    break;
                case "tech": case "pause": case "teknik":
                    if (!Config.Tournament || !Config.HasRounds || team == Team.None) return;
                    RequestPause(PauseKind.Technical, team, p.Name);
                    break;
                case "unpause": case "devam":
                    if (!Config.Tournament || team == Team.None) return;
                    Unpause(team, false);
                    break;
                case "help": case "yardım": case "yardim":
                    SystemChat(Config.Tournament
                        ? "Komutlar: .ready / .unready, .stay / .switch (bıçak raundu sonrası), .tac (taktik mola), .tech (teknik duraklatma), .unpause"
                        : "Turnuva komutları yalnızca turnuva sunucularında açık.", p);
                    break;
            }
        }

        // ---------------- server console ----------------

        /// <summary>Admin commands typed into the dedicated server console. Returns a reply for the console.</summary>
        public string ConsoleCommand(string line)
        {
            var parts = (line ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "";
            string arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0].ToLowerInvariant())
            {
                case "status":
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"{Config.Mode} · {Map.Name} · {Phase} · round {Round} · {TeamName(Team.T)} (T) {_score[1]} : {_score[2]} {TeamName(Team.CT)} (CT)");
                        if (_waitingReady) sb.AppendLine($"waiting for ready: {ReadyCount}/{ReadyNeeded}");
                        foreach (var p in _players)
                            sb.AppendLine($"  #{p.Id,-3} {p.Name,-20} {p.State.Team,-4} {(p.IsBot ? "BOT" : "   ")} {(p.MatchReady ? "ready" : "")}  K {p.Kills} D {p.Deaths} ${p.Money}");
                        return sb.ToString().TrimEnd();
                    }
                case "say":
                    SystemChat(arg);
                    return "ok";
                case "pause":
                    RequestPause(PauseKind.Technical, Team.None, "Yönetici");
                    return "pause requested (starts at freeze time)";
                case "unpause":
                    Unpause(Team.None, true);
                    return "unpaused";
                case "forceready":
                    if (!_waitingReady) return "not waiting for ready";
                    foreach (var p in _players) p.MatchReady = true;
                    return "everyone marked ready";
                case "skipknife":
                    _knifeDone = true;
                    return "knife round will be skipped";
                case "restart":
                    if (!Config.HasRounds) return "rounds modes only";
                    _knifeDone = !Config.KnifeRound;
                    Phase = GamePhase.Warmup;
                    BeginMatch();
                    return "match restarted";
                case "kick":
                    {
                        var p = _players.FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase) || x.Id.ToString() == arg);
                        if (p == null) return "no such player";
                        if (p.Peer >= 0) _net.Disconnect(p.Peer);
                        else { RemovePlayer(p); OnPlayerLeft(p); }
                        return "kicked " + p.Name;
                    }
                case "record":
                    if (string.IsNullOrEmpty(arg)) return "usage: record <file.vxdemo>";
                    try { StartRecording(arg); return "recording to " + arg; } catch (Exception e) { return "failed: " + e.Message; }
                case "stoprecord":
                    StopRecording();
                    return "recording stopped";
                case "help":
                    return "status | say <text> | pause | unpause | forceready | skipknife | restart | kick <name|id> | record <file> | stoprecord";
                default:
                    return "unknown command (help)";
            }
        }

        // ---------------- results ----------------

        private void RecordRound(Team winner, RoundEndReason reason) => _roundLog.Add((Round, winner == SideOfA, winner, reason));

        /// <summary>Results document for tournament platforms (team scores, player stats, every round).</summary>
        public string BuildResultsJson(Team winnerSide)
        {
            var sideA = SideOfA;
            var sideB = sideA == Team.T ? Team.CT : Team.T;
            int scoreA = _roundLog.Count(r => r.teamAWon), scoreB = _roundLog.Count - scoreA;
            string winner = winnerSide == Team.None ? "draw" : (winnerSide == sideA ? "A" : "B");
            int played = Math.Max(1, RoundsPlayed);
            List<object> Players(Team side) => _players.Where(p => p.State.Team == side).OrderByDescending(p => p.Score).Select(p => (object)new Dictionary<string, object>
            {
                ["name"] = p.Name, ["bot"] = p.IsBot, ["kills"] = p.Kills, ["deaths"] = p.Deaths, ["assists"] = p.Assists,
                ["headshots"] = p.Headshots, ["damage"] = p.Damage, ["adr"] = Math.Round(p.Damage / (double)played, 1),
                ["mvps"] = p.Mvps, ["score"] = p.Score,
            }).ToList();
            var doc = new Dictionary<string, object>
            {
                ["matchId"] = Config.MatchId,
                ["game"] = "VEXA",
                ["map"] = Map.Name,
                ["mode"] = Config.Mode.ToString().ToLowerInvariant(),
                ["startedAt"] = _matchStartedUtc.ToString("o", CultureInfo.InvariantCulture),
                ["endedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["rounds"] = RoundsPlayed,
                ["winner"] = winner,
                ["teamA"] = new Dictionary<string, object> { ["name"] = Config.TeamA, ["score"] = scoreA, ["finalSide"] = sideA.ToString(), ["players"] = Players(sideA) },
                ["teamB"] = new Dictionary<string, object> { ["name"] = Config.TeamB, ["score"] = scoreB, ["finalSide"] = sideB.ToString(), ["players"] = Players(sideB) },
                ["roundHistory"] = _roundLog.Select(r => (object)new Dictionary<string, object>
                {
                    ["round"] = r.round, ["winner"] = r.teamAWon ? "A" : "B", ["winnerSide"] = r.winnerSide.ToString(), ["reason"] = r.reason.ToString(),
                }).ToList(),
            };
            return MiniJson.Write(doc);
        }

        private void PublishResults(Team winnerSide)
        {
            if (!Config.Tournament && string.IsNullOrEmpty(Config.ResultsPath) && MatchFinished == null) return;
            string json = BuildResultsJson(winnerSide);
            if (!string.IsNullOrEmpty(Config.ResultsPath))
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(Config.ResultsPath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(Config.ResultsPath, json);
                    Info("results written to " + Config.ResultsPath);
                }
                catch (Exception e) { Info("could not write results: " + e.Message); }
            }
            MatchFinished?.Invoke(json);
        }

        // ---------------- match state extension ----------------

        private void WriteTournamentState()
        {
            var f = MatchFlags.None;
            if (Config.Tournament) f |= MatchFlags.Tournament;
            if (_waitingReady) f |= MatchFlags.WaitingReady;
            if (_knifeRound) f |= MatchFlags.KnifeRound;
            if (_sidePick) f |= MatchFlags.SidePick;
            if (_activePause == PauseKind.Tactical) f |= MatchFlags.TacticalPause;
            if (_activePause == PauseKind.Technical) f |= MatchFlags.TechnicalPause;
            if (_pendingPause != PauseKind.None) f |= MatchFlags.PausePending;
            _w.Byte((byte)f);
            _w.String(Config.Tournament ? TeamName(Team.T) : "");
            _w.String(Config.Tournament ? TeamName(Team.CT) : "");
            _w.Byte((byte)Math.Min(255, ReadyCount)); _w.Byte((byte)Math.Min(255, ReadyNeeded));
            _w.Byte((byte)Math.Max(0, TimeoutsLeft(Team.T))); _w.Byte((byte)Math.Max(0, TimeoutsLeft(Team.CT)));
            _w.Byte((byte)_knifeWinner);
        }
    }
}
