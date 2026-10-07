using System;
using System.Collections.Generic;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Authoritative game server (Source-style): clients send user commands, the server simulates them
    /// with the shared <see cref="PlayerSimulation"/>, resolves shots with lag compensation and
    /// sends snapshots. Never trusts client positions, hits or fire rate.
    /// </summary>
    public sealed class ServerGame : IShotSink
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
            public double RespawnAt = -1;
            public ServerBot Bot;
            public int Kills, Deaths;
            public int CmdsProcessed, ShotsFired, RejectedCmds;
            internal float TimeCredit = -1;            // ticks of command time the player may still consume
            public bool IsBot => Peer < 0;
        }

        public readonly int TickRate;
        public readonly float Dt;
        public int Tick { get; private set; }
        public readonly MapData Map;
        public readonly CollisionWorld World;
        public bool FriendlyFire = false;
        public float RespawnDelay = 3f;
        public int MaxRewindTicks;
        public const int MaxCmdBudget = 10;   // max commands per player per tick burst (anti speed-hack)
        public const float MaxTimeCreditSeconds = 0.5f; // command clock may never get more than this ahead of real time

        private readonly ITransport _net;
        private readonly Dictionary<int, Player> _byPeer = new Dictionary<int, Player>();
        private readonly Dictionary<int, Player> _byId = new Dictionary<int, Player>();
        private readonly List<Player> _players = new List<Player>();
        private readonly PoseHistory _history = new PoseHistory();
        private readonly NetWriter _w = new NetWriter(2048);
        private readonly NetReader _r = new NetReader();
        private readonly SimContext _ctx;
        private readonly Random _rng = new Random(1234);
        private Player _currentShooter;
        private double _acc;
        private int _nextId = 1;

        public IReadOnlyList<Player> Players => _players;
        public event Action<string> Log;
        public event Action<KillEvent> OnKill;
        public event Action<HitEvent> OnHit;

        public ServerGame(ITransport net, MapData map, int tickRate = 64)
        {
            _net = net;
            Map = map;
            World = map.BuildCollision();
            TickRate = tickRate;
            Dt = 1f / tickRate;
            MaxRewindTicks = (int)(0.25f * tickRate);
            _ctx = new SimContext { World = World, Dt = Dt, Shots = this };
            _net.Connected += OnConnected;
            _net.Disconnected += OnDisconnected;
            _net.Received += OnReceived;
        }

        public Player GetPlayer(int id) => _byId.TryGetValue(id, out var p) ? p : null;

        public Player AddBot(string name, Team team = Team.None)
        {
            var p = NewPlayer(-1, name, team);
            p.Bot = new ServerBot(this, p, _rng.Next());
            p.Ready = true;
            return p;
        }

        private Player NewPlayer(int peer, string name, Team team)
        {
            var p = new Player { Id = _nextId++, Peer = peer, Name = name };
            p.State = SpawnState(team);
            _players.Add(p);
            _byId[p.Id] = p;
            if (peer >= 0) _byPeer[peer] = p;
            BroadcastPlayerInfo(p);
            return p;
        }

        // ---------------- networking ----------------
        private void OnConnected(int peer) { Log?.Invoke($"peer {peer} connected"); }

        private void OnDisconnected(int peer)
        {
            if (!_byPeer.TryGetValue(peer, out var p)) return;
            _byPeer.Remove(peer); _byId.Remove(p.Id); _players.Remove(p); _history.Remove(p.Id);
            _w.Reset(); _w.Byte((byte)Msg.PlayerLeft); _w.Byte((byte)p.Id);
            SendAll(Delivery.ReliableOrdered);
            Log?.Invoke($"{p.Name} left");
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
                        p = NewPlayer(peer, name, Team.None);
                        p.Ready = true;
                        _w.Reset(); _w.Byte((byte)Msg.Welcome); _w.Byte((byte)p.Id); _w.UShort((ushort)TickRate); _w.Int(Tick); _w.String(Map.Name);
                        _net.Send(peer, _w.Data, _w.Length, Delivery.ReliableOrdered);
                        foreach (var o in _players) if (o != p) SendPlayerInfo(peer, o);
                        Log?.Invoke($"{name} joined as #{p.Id}");
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
                    {
                        if (p == null || !p.State.Alive) return;
                        var id = (WeaponId)_r.Byte();
                        if (id <= WeaponId.None || id >= WeaponId.Count || id == WeaponId.Knife) return;
                        // milestone 1: free loadouts (economy rules come with the competitive mode)
                        WeaponLogic.Give(ref p.State, id, p.LastCmdTick * Dt);
                        break;
                    }
            }
        }

        private void SendAll(Delivery d)
        {
            foreach (var p in _players) if (p.Peer >= 0) _net.Send(p.Peer, _w.Data, _w.Length, d);
        }
        private void SendAllExcept(Player except, Delivery d)
        {
            foreach (var p in _players) if (p.Peer >= 0 && p != except) _net.Send(p.Peer, _w.Data, _w.Length, d);
        }

        private void BroadcastPlayerInfo(Player p)
        {
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
            double now = Tick * (double)Dt;

            foreach (var p in _players)
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
                // anything past the budget is dropped (client running too fast)
                p.Pending.RemoveRange(0, Math.Max(used, p.Pending.Count > 32 ? p.Pending.Count - 32 : 0));
            }

            // deaths caused by the player's own simulation (falling)
            foreach (var p in _players)
                if (!p.State.Alive && p.RespawnAt < 0) { p.RespawnAt = now + RespawnDelay; p.Deaths++; }

            // respawns
            foreach (var p in _players)
                if (!p.State.Alive && p.RespawnAt >= 0 && now >= p.RespawnAt)
                {
                    var team = p.State.Team;
                    var keepPrimary = p.State.Primary;
                    p.State = SpawnState(team);
                    p.RespawnAt = -1;
                    if (!keepPrimary.IsEmpty) WeaponLogic.Give(ref p.State, keepPrimary.Id, p.LastCmdTick * Dt);
                }

            foreach (var p in _players) _history.Record(Tick, p.Id, HitPose.From(p.State));
            SendSnapshots();
        }

        private void Simulate(Player p, PlayerInput cmd)
        {
            _ctx.PlayerId = p.Id;
            _ctx.Frozen = false;
            _currentShooter = p;
            p.LastInterpTick = cmd.InterpTick;
            PlayerSimulation.Step(ref p.State, cmd, _ctx);
            p.LastCmdTick = cmd.Tick;
            p.CmdsProcessed++;
            _currentShooter = null;
        }

        private PlayerState SpawnState(Team team)
        {
            var spawns = Map.Spawns;
            SpawnPoint best = default;
            float bestScore = float.MinValue;
            for (int k = 0; k < Math.Max(1, spawns.Count); k++)
            {
                if (spawns.Count == 0) break;
                var sp = spawns[_rng.Next(spawns.Count)];
                if (team != Team.None && sp.Team != Team.None && sp.Team != team) continue;
                float dmin = 999f;
                foreach (var o in _players) if (o.State.Alive) dmin = MathF.Min(dmin, Vector3.Distance(o.State.Position, sp.Position));
                if (dmin > bestScore) { bestScore = dmin; best = sp; }
            }
            var st = PlayerState.Spawn(team == Team.None ? Team.T : team, best.Position, best.Yaw);
            st.Team = team;
            st.Armor = 100; st.Helmet = true;
            return st;
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

            if ((shot.Flags & ShotFlags.Melee) != 0) { ResolveMelee(shooter, shot, def, rewind); return; }
            if ((shot.Flags & ShotFlags.Taser) != 0)
            {
                var dir = shot.PelletDirection(0);
                if (TraceTarget(shooter, shot.Origin, dir, def.Range, rewind, null, out var victim, out float t, out var g, out _))
                    ApplyDamage(shooter, victim, def, g, 500, 0, false, shot.Origin + dir * t);
                return;
            }
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
                        ApplyDamage(shooter, victim, def, group, dmg.Health, dmg.Armor, wall, o + dir * t);
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

        private void ApplyDamage(Player attacker, Player victim, WeaponDef def, HitGroup group, int health, int armor, bool wallbang, Vector3 point)
        {
            if (!victim.State.Alive) return;
            if (!FriendlyFire && attacker != victim && attacker.State.Team != Team.None && attacker.State.Team == victim.State.Team) return;
            victim.State.Health -= (short)health;
            victim.State.Armor = (short)Math.Max(0, victim.State.Armor - armor);
            victim.State.VelocityModifier = MathF.Min(victim.State.VelocityModifier, group == HitGroup.LeftLeg || group == HitGroup.RightLeg ? 0.55f : 0.45f);
            if (victim.State.Health < 0) victim.State.Health = 0;
            var he = new HitEvent { Attacker = attacker.Id, Victim = victim.Id, Group = group, Point = point, Damage = health, VictimHealth = victim.State.Health };
            _w.Reset(); _w.Byte((byte)Msg.Hit); _w.Byte((byte)he.Attacker); _w.Byte((byte)he.Victim); _w.Byte((byte)he.Group); _w.Vec3(point); _w.Short((short)health); _w.Short(victim.State.Health);
            SendAll(Delivery.ReliableOrdered);
            OnHit?.Invoke(he);
            if (victim.State.Health <= 0)
            {
                victim.State.Alive = false;
                victim.Deaths++;
                victim.RespawnAt = Tick * (double)Dt + RespawnDelay;
                if (attacker != victim) attacker.Kills++;
                var ke = new KillEvent { Killer = attacker.Id, Victim = victim.Id, Weapon = def.Id, Headshot = group == HitGroup.Head, Wallbang = wallbang };
                _w.Reset(); _w.Byte((byte)Msg.Kill); _w.Byte((byte)ke.Killer); _w.Byte((byte)ke.Victim); _w.Byte((byte)ke.Weapon); _w.Bool(ke.Headshot); _w.Bool(ke.Wallbang);
                SendAll(Delivery.ReliableOrdered);
                OnKill?.Invoke(ke);
                Log?.Invoke($"{attacker.Name} [{def.Name}{(ke.Headshot ? " HS" : "")}{(wallbang ? " WB" : "")}] {victim.Name}");
            }
        }

        // ---------------- snapshots ----------------
        private void SendSnapshots()
        {
            foreach (var p in _players)
            {
                if (p.Peer < 0 || !p.Ready) continue;
                _w.Reset();
                _w.Byte((byte)Msg.Snapshot);
                _w.Int(Tick);
                _w.Int(p.LastCmdTick);
                Protocol.WriteState(_w, p.State);
                _w.Byte((byte)(_players.Count - 1));
                foreach (var o in _players)
                    if (o != p) Protocol.WriteRemote(_w, o.Id, o.State);
                _net.Send(p.Peer, _w.Data, _w.Length, Delivery.Sequenced);
            }
        }
    }
}
