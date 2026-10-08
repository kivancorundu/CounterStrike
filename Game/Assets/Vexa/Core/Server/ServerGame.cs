using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Authoritative game server (Source-style): clients send user commands, the server simulates them
    /// with the shared <see cref="PlayerSimulation"/>, resolves shots with lag compensation and
    /// sends snapshots. Never trusts client positions, hits or fire rate.
    /// Split into partial files: networking + simulation (this file), match rules (ServerMatch.cs),
    /// grenades (ServerGrenades.cs).
    /// </summary>
    public sealed partial class ServerGame : IShotSink, ISimEvents
    {
        public sealed class Player
        {
            public int Id;
            public int Peer = -1;           // -1 = bot
            public string Name;
            public bool Ready;
            public PlayerState State;
            public readonly List<PlayerInput> Pending = new List<PlayerInput>();
            public int LastCmdTick = -1;
            public int Budget;
            public float LastInterpTick;
            public Buttons LastButtons;
            public double RespawnAt = -1;
            public ServerBot Bot;
            public int CmdsProcessed, ShotsFired, RejectedCmds;
            internal float TimeCredit = -1;            // ticks of command time the player may still consume
            public bool IsBot => Peer < 0;
            public Team PreferredTeam;
            public bool MatchReady;          // typed .ready in a tournament warmup
            public bool Hidden;              // the demo recorder ("VEXA TV"): receives everything, appears nowhere

            // match stats
            public int Money;
            public int Kills, Deaths, Assists, Mvps, Score, Damage, Headshots;
            public int RoundKills, RoundDamage;
            public readonly Dictionary<int, int> DamageTaken = new Dictionary<int, int>(); // attacker id -> damage this round
            public double SpawnTime;
            public List<ItemId> DmLoadout = new List<ItemId>();

            /// <summary>Current time on this player's own command timeline.</summary>
            public float PlayerTime(float dt) => LastCmdTick * dt;
        }

        public readonly int TickRate;
        public readonly float Dt;
        public int Tick { get; private set; }
        public readonly MapData Map;
        public readonly CollisionWorld World;
        public readonly MatchConfig Config;
        public int MaxRewindTicks;
        public const int MaxCmdBudget = 10;   // max commands per player per tick burst (anti speed-hack)
        public const float MaxTimeCreditSeconds = 0.5f; // command clock may never get more than this ahead of real time

        private readonly ITransport _net;
        private readonly DemoTap _tap;
        private Player _recorder;
        public bool IsRecording => _recorder != null;
        private readonly Dictionary<int, Player> _byPeer = new Dictionary<int, Player>();
        private readonly Dictionary<int, Player> _byId = new Dictionary<int, Player>();
        private readonly List<Player> _players = new List<Player>();
        private readonly PoseHistory _history = new PoseHistory();
        private readonly NetWriter _w = new NetWriter(4096);
        private readonly NetReader _r = new NetReader();
        private readonly SimContext _ctx;
        private readonly Random _rng;
        private Player _currentShooter;
        private double _acc;
        private int _nextId = 1;

        public IReadOnlyList<Player> Players => _players;
        private AI.NavGrid _nav;
        public AI.NavGrid Nav => _nav ?? (_nav = AI.NavGrid.Build(World));
        public event Action<string> Log;
        public event Action<KillEvent> OnKill;
        public event Action<HitEvent> OnHit;

        public ServerGame(ITransport net, MapData map, int tickRate = 64, MatchConfig config = null, int seed = 1234)
        {
            _net = _tap = new DemoTap(net);
            Map = map;
            World = map.BuildCollision();
            TickRate = tickRate;
            Dt = 1f / tickRate;
            Config = config ?? MatchConfig.Deathmatch();
            _rng = new Random(seed);
            MaxRewindTicks = (int)(0.25f * tickRate);
            _ctx = new SimContext { World = World, Dt = Dt, Shots = this, Events = this, Map = map };
            _net.Connected += OnConnected;
            _net.Disconnected += OnDisconnected;
            _net.Received += OnReceived;
            InitMatch();
            if (!string.IsNullOrEmpty(Config.DemoPath))
            {
                try { StartRecording(Config.DemoPath); }
                catch (Exception e) { Info("could not start demo recording: " + e.Message); }
            }
        }

        public Player GetPlayer(int id) => id >= 0 && _byId.TryGetValue(id, out var p) ? p : null;
        void Info(string m) => Log?.Invoke(m);

        public Player AddBot(string name, Team team = Team.None, bool brain = true)
        {
            var p = NewPlayer(-1, name, team == Team.None ? AutoTeam() : team);
            if (brain) p.Bot = new ServerBot(this, p, _rng.Next());
            p.Ready = true;
            OnPlayerJoined(p);
            return p;
        }

        private Player NewPlayer(int peer, string name, Team team, bool hidden = false)
        {
            var p = new Player { Id = _nextId++, Peer = peer, Name = name, Hidden = hidden };
            if (Config.Mode == GameMode.Deathmatch) team = Team.None;
            p.State = PlayerState.Spawn(team == Team.None ? Team.T : team, Vector3.Zero, 0);
            p.State.Team = team;
            p.State.Alive = false;
            p.Money = Config.StartMoney;
            _players.Add(p);
            _byId[p.Id] = p;
            if (peer >= 0) _byPeer[peer] = p;
            BroadcastPlayerInfo(p);
            return p;
        }

        public void RemovePlayer(Player p)
        {
            if (p.State.HasC4) DropBomb(p);
            _byId.Remove(p.Id); _players.Remove(p); _history.Remove(p.Id);
            if (p.Peer >= 0) _byPeer.Remove(p.Peer);
            if (p.Hidden) return;
            _w.Reset(); _w.Byte((byte)Msg.PlayerLeft); _w.Byte((byte)p.Id);
            SendAll(Delivery.ReliableOrdered);
        }

        // ---------------- networking ----------------
        private void OnConnected(int peer) { Info($"peer {peer} connected"); }

        private void OnDisconnected(int peer)
        {
            if (!_byPeer.TryGetValue(peer, out var p)) return;
            RemovePlayer(p);
            Info($"{p.Name} left");
            OnPlayerLeft(p);
        }

        private void OnReceived(int peer, byte[] data, int len)
        {
            _r.Set(data, len);
            var msg = (Msg)_r.Byte();
            _byPeer.TryGetValue(peer, out var p);
            switch (msg)
            {
                case Msg.Hello:
                    {
                        ushort ver = _r.UShort();
                        string name = _r.String();
                        if (ver != Protocol.Version || p != null) { _net.Disconnect(peer); return; }
                        if (string.IsNullOrWhiteSpace(name)) name = "Player";
                        if (name.Length > 24) name = name.Substring(0, 24);
                        var team = TeamForJoin(name);
                        MakeRoomOnTeam(team);
                        p = Join(peer, name, team, false);
                        Info($"{name} joined as #{p.Id} ({team})");
                        OnPlayerJoined(p);
                        SendMatchState(p);
                        break;
                    }
                case Msg.Input:
                    {
                        if (p == null) return;
                        int count = _r.Byte();
                        if (count > Protocol.InputRedundancy) return;
                        for (int i = 0; i < count; i++)
                        {
                            var c = Protocol.ReadInput(_r);
                            if (_r.Error) return;
                            if (c.Tick <= p.LastCmdTick) continue;
                            bool dup = false;
                            foreach (var q in p.Pending) if (q.Tick == c.Tick) { dup = true; break; }
                            if (!dup && p.Pending.Count < 64) p.Pending.Add(c);
                        }
                        break;
                    }
                case Msg.Buy:
                    if (p != null) TryBuy(p, (ItemId)_r.Byte());
                    break;
                case Msg.TeamSelect:
                    if (p != null) RequestTeam(p, (Team)_r.Byte());
                    break;
                case Msg.Chat:
                    {
                        if (p == null) return;
                        bool teamOnly = _r.Bool();
                        string text = _r.String();
                        if (!_r.Error) OnChat(p, teamOnly, text);
                        break;
                    }
            }
        }

        private Player Join(int peer, string name, Team team, bool hidden)
        {
            var p = NewPlayer(peer, name, team, hidden);
            p.Ready = true;
            _w.Reset(); _w.Byte((byte)Msg.Welcome); _w.Byte((byte)p.Id); _w.UShort((ushort)TickRate); _w.Int(Tick); _w.String(Map.Name);
            _net.Send(peer, _w.Data, _w.Length, Delivery.ReliableOrdered);
            foreach (var o in _players) if (o != p && !o.Hidden) SendPlayerInfo(peer, o);
            return p;
        }

        // ---------------- demo recording ("VEXA TV") ----------------

        /// <summary>Records everything a hidden spectator receives into <paramref name="stream"/> (see <see cref="DemoFile"/>).</summary>
        public void StartRecording(System.IO.Stream stream)
        {
            if (_recorder != null) StopRecording();
            _tap.Writer = new DemoWriter(stream, Map.Name, TickRate, () => Tick * (double)Dt);
            _recorder = Join(DemoTap.Peer, "VEXA TV", Team.None, true);
            SendMatchState(_recorder);
            SendScoreboards();
            Info("demo recording started");
        }

        public void StartRecording(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            StartRecording(System.IO.File.Create(path));
        }

        public void StopRecording()
        {
            if (_recorder == null) return;
            RemovePlayer(_recorder);
            _recorder = null;
            _tap.Writer?.Dispose();
            _tap.Writer = null;
            Info("demo recording stopped");
        }

        internal void SendAll(Delivery d)
        {
            foreach (var p in _players) if (p.Peer >= 0) _net.Send(p.Peer, _w.Data, _w.Length, d);
        }
        internal void SendAllExcept(Player except, Delivery d)
        {
            foreach (var p in _players) if (p.Peer >= 0 && p != except) _net.Send(p.Peer, _w.Data, _w.Length, d);
        }
        internal void SendTo(Player p, Delivery d)
        {
            if (p.Peer >= 0) _net.Send(p.Peer, _w.Data, _w.Length, d);
        }

        internal void BroadcastPlayerInfo(Player p)
        {
            if (p.Hidden) return;
            WritePlayerInfo(p);
            SendAll(Delivery.ReliableOrdered);
        }
        private void SendPlayerInfo(int peer, Player p)
        {
            WritePlayerInfo(p);
            _net.Send(peer, _w.Data, _w.Length, Delivery.ReliableOrdered);
        }
        private void WritePlayerInfo(Player p)
        {
            _w.Reset(); _w.Byte((byte)Msg.PlayerInfo); _w.Byte((byte)p.Id); _w.Byte((byte)p.State.Team); _w.String(p.Name); _w.Bool(p.IsBot);
        }

        internal void Broadcast(in GameEvent e)
        {
            _w.Reset(); Protocol.WriteEvent(_w, e);
            SendAll(Delivery.ReliableOrdered);
        }
        internal void SendEvent(Player p, in GameEvent e)
        {
            _w.Reset(); Protocol.WriteEvent(_w, e);
            SendTo(p, Delivery.ReliableOrdered);
        }

        // ---------------- simulation ----------------
        public void Update(double realDt)
        {
            _acc += realDt;
            int n = 0;
            while (_acc >= Dt && n < 8) { _acc -= Dt; Step(); n++; }
            if (n == 8) _acc = 0;
        }

        public void Step()
        {
            _net.Poll();
            Tick++;

            foreach (var p in _players.ToArray())
            {
                if (!p.Ready) continue;
                p.Budget = Math.Min(p.Budget + 1, MaxCmdBudget);
                float maxCredit = MaxTimeCreditSeconds * TickRate;
                p.TimeCredit = p.TimeCredit < 0 ? maxCredit : Math.Min(p.TimeCredit + 1, maxCredit);
                if (p.IsBot)
                {
                    if (p.Bot != null) Simulate(p, p.Bot.Think(Tick));
                    continue;
                }
                if (p.Pending.Count == 0) continue;
                p.Pending.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                int used = 0;
                for (int i = 0; i < p.Pending.Count && p.Budget > 0; i++)
                {
                    var cmd = p.Pending[i];
                    used = i + 1;
                    if (cmd.Tick <= p.LastCmdTick) continue;
                    // The command clock drives weapon timers, so it must never run faster than real time.
                    // Each server tick earns one tick of credit (capped); each command spends the time it
                    // advances. Blocks speed hacks and rapid fire by skipping command numbers.
                    int advance = p.LastCmdTick < 0 ? 1 : cmd.Tick - p.LastCmdTick;
                    if (advance > p.TimeCredit) { p.RejectedCmds++; continue; }
                    p.TimeCredit -= advance;
                    // clamp lag-compensation time to the allowed window
                    cmd.InterpTick = VMath.Clamp(cmd.InterpTick, Tick - MaxRewindTicks, Tick);
                    Simulate(p, cmd);
                    p.Budget--;
                }
                p.Pending.RemoveRange(0, Math.Max(used, p.Pending.Count > 32 ? p.Pending.Count - 32 : 0));
            }

            StepGrenades();
            StepMatch();

            foreach (var p in _players) _history.Record(Tick, p.Id, HitPose.From(p.State));
            SendSnapshots();
        }

        private void Simulate(Player p, PlayerInput cmd)
        {
            bool wasAlive = p.State.Alive;
            _ctx.PlayerId = p.Id;
            _ctx.Frozen = Phase == GamePhase.Freeze || Phase == GamePhase.MatchOver;
            _ctx.PlantAllowed = Phase == GamePhase.Live && Bomb.State == BombState.Carried;
            _currentShooter = p;
            p.LastInterpTick = cmd.InterpTick;
            _ctx.Obstacles.Clear();
            foreach (var o in _players)
                if (o != p && o.State.Alive) _ctx.Obstacles.Add(SimContext.HullOf(o.State));
            PlayerSimulation.Step(ref p.State, cmd, _ctx);
            p.LastCmdTick = cmd.Tick;
            p.CmdsProcessed++;
            _currentShooter = null;
            var prev = p.LastButtons;
            p.LastButtons = cmd.Buttons;
            if (wasAlive && !p.State.Alive) OnDeath(p, null, null, false, false); // fell to death
            if (p.State.Alive)
            {
                if (cmd.Has(Buttons.Drop) && (prev & Buttons.Drop) == 0) DropActive(p);
                if (cmd.Has(Buttons.Use) && (prev & Buttons.Use) == 0) UsePickup(p);
            }
        }

        public void OnThrow(in GrenadeThrow t)
        {
            var p = _currentShooter;
            if (p == null) return;
            SpawnProjectile(p, t);
        }

        private void BotHearShot(Player shooter, in ShotInfo shot)
        {
            ServerBot.HearNoise(this, shooter, shot.Origin, (shot.Flags & ShotFlags.Silenced) != 0 ? 12f : 45f);
        }

        public void OnPlanted(int playerId, Vector3 position, string site)
        {
            var p = GetPlayer(playerId);
            if (p != null) PlantBomb(p, position, site);
        }

        // ---------------- shots (lag compensated) ----------------
        public void OnShot(in ShotInfo shot)
        {
            var shooter = _currentShooter;
            if (shooter == null) return;
            shooter.ShotsFired++;
            var def = Weapons.Get(shot.Weapon);
            float rewind = shooter.IsBot ? Tick : VMath.Clamp(shot.InterpTick, Tick - MaxRewindTicks, Tick);

            // let everyone else see / hear the shot
            _w.Reset(); _w.Byte((byte)Msg.ShotFx); Protocol.WriteShot(_w, shot);
            SendAllExcept(shooter, Delivery.Unreliable);
            BotHearShot(shooter, shot);

            if ((shot.Flags & ShotFlags.Melee) != 0) { ResolveMelee(shooter, shot, def, rewind); return; }
            if ((shot.Flags & ShotFlags.Taser) != 0)
            {
                var dir = shot.PelletDirection(0);
                if (TraceTarget(shooter, shot.Origin, dir, def.Range, rewind, null, out var victim, out float t, out var g, out _))
                    ApplyDamage(shooter, victim, def, g, 500, 0, false, shot.Origin + dir * t);
                return;
            }
            bool smokeShot = SegmentInSmoke(shot.Origin, VMath.Forward(shot.Yaw, shot.Pitch), 30f);
            var hitThisPellet = new HashSet<Player>();
            for (int i = 0; i < shot.Pellets; i++)
            {
                var dir = shot.PelletDirection(i);
                Vector3 o = shot.Origin;
                float traveled = 0f, power = 1f, pen = def.Penetration;
                bool wall = false;
                hitThisPellet.Clear();
                for (int iter = 0; iter < 8; iter++)
                {
                    float rem = def.Range - traveled;
                    if (rem <= 0.01f) break;
                    bool hasWorld = World.Raycast(o, dir, rem, out var wh);
                    float lim = hasWorld ? wh.Distance : rem;
                    if (TraceTarget(shooter, o, dir, lim, rewind, hitThisPellet, out var victim, out float t, out var group, out _))
                    {
                        hitThisPellet.Add(victim);
                        float dist = traveled + t;
                        var dmg = DamageModel.Compute(def, group, dist, power, victim.State.Armor, victim.State.Helmet);
                        ApplyDamage(shooter, victim, def, group, dmg.Health, dmg.Armor, wall, o + dir * t, smokeShot);
                        o += dir * (t + 0.01f); traveled = dist + 0.01f; power *= 0.6f;
                        continue;
                    }
                    if (!hasWorld) break;
                    float thickHU = (wh.ExitDistance - wh.Distance) / VMath.HU;
                    float cost = thickHU * Surfaces.PenetrationCostPerHU(wh.Material);
                    if (cost >= pen) break;
                    power *= (1f - cost / pen) * 0.85f;
                    pen -= cost;
                    wall = true;
                    float adv = wh.ExitDistance + 0.005f;
                    o += dir * adv; traveled += adv;
                    if (power < 0.02f) break;
                }
            }
        }

        private bool TraceTarget(Player shooter, Vector3 o, Vector3 dir, float maxDist, float rewindTick, HashSet<Player> exclude,
            out Player victim, out float t, out HitGroup group, out HitPose pose)
        {
            victim = null; t = maxDist; group = HitGroup.None; pose = default;
            foreach (var p in _players)
            {
                if (p == shooter || !p.State.Alive || (exclude != null && exclude.Contains(p))) continue;
                if (!_history.TryGet(p.Id, rewindTick, out var ps)) ps = HitPose.From(p.State);
                if (!ps.Alive) continue;
                if (Hitboxes.Raycast(ps, o, dir, t, out float ti, out var g) && ti < t)
                {
                    victim = p; t = ti; group = g; pose = ps;
                }
            }
            return victim != null;
        }

        private void ResolveMelee(Player shooter, in ShotInfo shot, WeaponDef def, float rewind)
        {
            bool heavy = (shot.Flags & ShotFlags.Heavy) != 0;
            float range = (heavy ? 48f : 64f) * VMath.HU;
            foreach (float off in new[] { 0f, -7f, 7f, -14f, 14f })
            {
                var dir = VMath.Forward(shot.Yaw + off, shot.Pitch);
                float lim = World.Raycast(shot.Origin, dir, range, out var wh) ? wh.Distance : range;
                if (!TraceTarget(shooter, shot.Origin, dir, lim, rewind, null, out var victim, out float t, out _, out var pose)) continue;
                var vf = VMath.FlatForward(pose.Yaw);
                var to = Vector3.Normalize(new Vector3(pose.Position.X - shooter.State.Position.X, 0, pose.Position.Z - shooter.State.Position.Z));
                bool back = Vector3.Dot(vf, to) > 0.475f;
                int raw = heavy ? (back ? 180 : 65) : (back ? 90 : 40);
                var dmg = DamageModel.ApplyArmor(raw, def.ArmorPen, HitGroup.Chest, victim.State.Armor, victim.State.Helmet);
                ApplyDamage(shooter, victim, def, HitGroup.Chest, dmg.Health, dmg.Armor, false, shot.Origin + dir * t);
                return;
            }
        }

        internal void ApplyDamage(Player attacker, Player victim, WeaponDef def, HitGroup group, int health, int armor, bool wallbang, Vector3 point, bool throughSmoke = false)
        {
            if (!victim.State.Alive || health <= 0) return;
            if (Phase == GamePhase.MatchOver) return;
            if (Config.Mode == GameMode.Deathmatch && Tick * (double)Dt - victim.SpawnTime < 1.5) return; // spawn protection
            bool teamHit = attacker != null && attacker != victim && victim.State.Team != Team.None && attacker.State.Team == victim.State.Team;
            if (teamHit)
            {
                if (!Config.FriendlyFire) return;
                health = Math.Max(1, (int)(health * (def != null && def.Category == WeaponCategory.Grenade ? 0.85f : 0.33f)));
            }
            int real = Math.Min(health, victim.State.Health);
            victim.State.Health -= (short)health;
            victim.State.Armor = (short)Math.Max(0, victim.State.Armor - armor);
            victim.State.VelocityModifier = MathF.Min(victim.State.VelocityModifier, group == HitGroup.LeftLeg || group == HitGroup.RightLeg ? 0.55f : 0.45f);
            if (victim.State.Health < 0) victim.State.Health = 0;
            if (attacker != null && attacker != victim)
            {
                victim.DamageTaken.TryGetValue(attacker.Id, out int prev);
                victim.DamageTaken[attacker.Id] = prev + real;
                if (!teamHit) { attacker.Damage += real; attacker.RoundDamage += real; }
            }
            var he = new HitEvent { Attacker = attacker?.Id ?? 0, Victim = victim.Id, Group = group, Point = point, Damage = health, VictimHealth = victim.State.Health };
            _w.Reset(); _w.Byte((byte)Msg.Hit); _w.Byte((byte)he.Attacker); _w.Byte((byte)he.Victim); _w.Byte((byte)he.Group); _w.Vec3(point); _w.Short((short)health); _w.Short(victim.State.Health);
            SendAll(Delivery.ReliableOrdered);
            OnHit?.Invoke(he);
            victim.Bot?.OnDamaged(attacker);
            if (victim.State.Health <= 0) OnDeath(victim, attacker, def, group == HitGroup.Head, wallbang, throughSmoke);
        }

        private void OnDeath(Player victim, Player attacker, WeaponDef def, bool headshot, bool wallbang, bool throughSmoke = false)
        {
            victim.State.Alive = false;
            victim.State.Health = 0;
            victim.State.Planting = false; victim.State.Defusing = false;
            var weapon = def?.Id ?? WeaponId.None;
            var ke = new KillEvent { Killer = attacker?.Id ?? 0, Victim = victim.Id, Weapon = weapon, Headshot = headshot, Wallbang = wallbang, ThroughSmoke = throughSmoke, AttackerBlind = attacker != null && attacker.State.IsBlind(attacker.PlayerTime(Dt)), Grenade = _killGrenade };
            _w.Reset(); _w.Byte((byte)Msg.Kill); _w.Byte((byte)ke.Killer); _w.Byte((byte)ke.Victim); _w.Byte((byte)ke.Weapon);
            _w.Byte((byte)((ke.Headshot ? 1 : 0) | (ke.Wallbang ? 2 : 0) | (ke.ThroughSmoke ? 4 : 0) | (ke.AttackerBlind ? 8 : 0)));
            _w.Byte((byte)ke.Grenade);
            SendAll(Delivery.ReliableOrdered);
            OnKill?.Invoke(ke);
            Info($"{attacker?.Name ?? "world"} [{def?.Name ?? "-"}{(headshot ? " HS" : "")}{(wallbang ? " WB" : "")}] {victim.Name}");
            OnKilled(victim, attacker, def, headshot);
        }

        // ---------------- snapshots ----------------
        private void SendSnapshots()
        {
            var header = new MatchHeader { Phase = Phase, PhaseEndTick = PhaseEndTick, BuyEndTick = BuyEndTick };
            foreach (var p in _players)
            {
                if (p.Peer < 0 || !p.Ready) continue;
                _w.Reset();
                _w.Byte((byte)Msg.Snapshot);
                _w.Int(Tick);
                _w.Int(p.LastCmdTick);
                Protocol.WriteState(_w, p.State);
                _w.Byte((byte)(_players.Count(o => o != p && !o.Hidden)));
                foreach (var o in _players)
                {
                    if (o == p || o.Hidden) continue;
                    var st = o.State;
                    // don't leak who carries the bomb to the enemy team (spectators see everything)
                    if (st.Team != p.State.Team && p.State.Team != Team.None && Config.Mode != GameMode.Practice) st.HasC4 = false;
                    Protocol.WriteRemote(_w, o.Id, st);
                }
                Protocol.WriteHeader(_w, header);
                var bomb = Bomb;
                if (bomb.State == BombState.Carried && p.State.Team != Team.None && GetPlayer(bomb.CarrierId)?.State.Team != p.State.Team) { bomb.CarrierId = 0; bomb.Position = Vector3.Zero; }
                Protocol.WriteBomb(_w, bomb);
                WriteGrenadeSnapshot(_w);
                WriteItemsSnapshot(_w);
                _net.Send(p.Peer, _w.Data, _w.Length, Delivery.Sequenced);
            }
        }
    }
}
