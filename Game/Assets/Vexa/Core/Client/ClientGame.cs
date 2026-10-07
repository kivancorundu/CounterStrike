using System;
using System.Collections.Generic;
using System.Numerics;
using Vexa.Core.Net;

namespace Vexa.Core.Client
{
    /// <summary>
    /// Client-side game logic, independent of Unity:
    ///  * client-side prediction of the local player (movement AND weapons: firing, ammo, reload, switching, recoil)
    ///  * reconciliation: when the server's authoritative state for an acknowledged command differs,
    ///    rewind to it and replay the unacknowledged commands
    ///  * snapshot interpolation for other players, rendered slightly in the past
    ///  * reports its render time with every command so the server can lag-compensate shots
    /// The Unity layer only feeds input, reads state for rendering and listens to events.
    /// </summary>
    public sealed class ClientGame : IShotSink
    {
        public sealed class Remote
        {
            public int Id;
            public string Name = "?";
            public Team Team;
            public bool IsBot;
            internal readonly List<(int tick, RemoteState s)> Snaps = new List<(int, RemoteState)>();
            public RemoteState Latest;
        }

        private const int BufSize = 512;
        private readonly ITransport _net;
        private readonly NetWriter _w = new NetWriter(512);
        private readonly NetReader _r = new NetReader();
        private readonly PlayerInput[] _cmds = new PlayerInput[BufSize];
        private readonly PlayerState[] _states = new PlayerState[BufSize];
        private readonly bool[] _valid = new bool[BufSize];
        private readonly SimContext _ctx;
        private readonly SimContext _replayCtx;
        private readonly Dictionary<int, Remote> _remotes = new Dictionary<int, Remote>();
        private double _acc;
        private int _lastAck = -1;
        private int _firstCmdTick = int.MaxValue;
        private int _latestSnapTick = -1;
        private double _timeSinceSnap;

        public readonly string PlayerName;
        public MapData Map { get; private set; }
        public CollisionWorld World { get; private set; }
        public int LocalId { get; private set; } = -1;
        public bool Welcomed { get; private set; }
        public int TickRate { get; private set; } = 64;
        public float Dt { get; private set; } = 1f / 64;
        public int CmdTick { get; private set; }
        public PlayerState Predicted;
        public PlayerState PreviousPredicted;
        public double ServerTickEstimate { get; private set; }
        public float InterpDelayTicks = 3f;
        public float RenderTick => (float)(ServerTickEstimate - InterpDelayTicks);
        /// <summary>Fraction between the previous and current predicted tick, for smooth rendering.</summary>
        public float Alpha => (float)(_acc / Dt);
        /// <summary>Visual offset left over after a correction; decays to zero so corrections are not visible as snaps.</summary>
        public Vector3 CorrectionOffset;

        // stats
        public int Mispredictions { get; private set; }
        public int SnapshotsReceived { get; private set; }
        public float LastCorrection { get; private set; }

        public IEnumerable<Remote> Remotes => _remotes.Values;
        public event Action<ShotInfo, bool> ShotFired;  // (shot, isLocalPrediction)
        public event Action<HitEvent> Hit;
        public event Action<KillEvent> Killed;
        public event Action<string> Log;

        public Func<string, MapData> MapLoader;

        public ClientGame(ITransport net, string playerName, Func<string, MapData> mapLoader)
        {
            _net = net;
            PlayerName = playerName;
            MapLoader = mapLoader;
            _ctx = new SimContext { Shots = this };
            _replayCtx = new SimContext { Shots = null };
            _net.Connected += _ => SendHello();
            _net.Received += OnReceived;
            _net.Disconnected += _ => Log?.Invoke("disconnected");
        }

        public Remote GetRemote(int id) => _remotes.TryGetValue(id, out var r) ? r : null;
        public int PingMs => _net.GetRttMs(0);

        private void SendHello()
        {
            _w.Reset(); _w.Byte((byte)Msg.Hello); _w.UShort(Protocol.Version); _w.String(PlayerName);
            _net.Send(0, _w.Data, _w.Length, Delivery.ReliableOrdered);
        }

        public void RequestBuy(WeaponId id)
        {
            _w.Reset(); _w.Byte((byte)Msg.Buy); _w.Byte((byte)id);
            _net.Send(0, _w.Data, _w.Length, Delivery.ReliableOrdered);
        }

        /// <summary>Advance by real time. <paramref name="sampleInput"/> is asked for one command per tick.</summary>
        public void Update(double realDt, Func<PlayerInput> sampleInput)
        {
            _net.Poll();
            if (!Welcomed) return;
            _acc += realDt;
            _timeSinceSnap += realDt;
            // server clock estimate: advance in real time, gently pulled toward the newest snapshot
            ServerTickEstimate += realDt * TickRate;
            if (_latestSnapTick >= 0)
            {
                double target = _latestSnapTick + _timeSinceSnap * TickRate;
                double err = target - ServerTickEstimate;
                if (Math.Abs(err) > TickRate * 0.5) ServerTickEstimate = target;   // big jump: snap
                else ServerTickEstimate += err * Math.Min(1.0, realDt * 4.0);
            }
            int steps = 0;
            while (_acc >= Dt && steps < 8)
            {
                _acc -= Dt;
                RunTick(sampleInput());
                steps++;
            }
            if (steps == 8) _acc = 0;
            float decay = (float)Math.Exp(-realDt * 15.0);
            CorrectionOffset *= decay;
        }

        private void RunTick(PlayerInput cmd)
        {
            CmdTick++;
            if (_firstCmdTick == int.MaxValue) _firstCmdTick = CmdTick;
            cmd.Tick = CmdTick;
            cmd.InterpTick = RenderTick;
            int i = CmdTick % BufSize;
            _cmds[i] = cmd;
            PreviousPredicted = Predicted;
            _ctx.World = World; _ctx.Dt = Dt; _ctx.PlayerId = LocalId;
            PlayerSimulation.Step(ref Predicted, cmd, _ctx);
            _states[i] = Predicted;
            _valid[i] = true;
            // send this command plus a few previous ones (covers packet loss)
            _w.Reset();
            _w.Byte((byte)Msg.Input);
            // only resend commands that really exist and that the server hasn't acknowledged yet
            int n = 1;
            while (n < Protocol.InputRedundancy)
            {
                int t = CmdTick - n;
                if (t <= _lastAck || t < _firstCmdTick || _cmds[t % BufSize].Tick != t) break;
                n++;
            }
            _w.Byte((byte)n);
            for (int k = n - 1; k >= 0; k--) Protocol.WriteInput(_w, _cmds[(CmdTick - k) % BufSize]);
            _net.Send(0, _w.Data, _w.Length, Delivery.Unreliable);
        }

        // local prediction effects (never fired during replays)
        public void OnShot(in ShotInfo shot) => ShotFired?.Invoke(shot, true);

        private void OnReceived(int peer, byte[] data, int len)
        {
            _r.Set(data, len);
            var msg = (Msg)_r.Byte();
            switch (msg)
            {
                case Msg.Welcome:
                    {
                        LocalId = _r.Byte();
                        TickRate = _r.UShort();
                        Dt = 1f / TickRate;
                        int serverTick = _r.Int();
                        string map = _r.String();
                        Map = MapLoader(map);
                        World = Map.BuildCollision();
                        ServerTickEstimate = serverTick;
                        CmdTick = serverTick;  // start our command timeline near the server clock
                        InterpDelayTicks = MathF.Max(2f, 0.05f * TickRate);
                        Welcomed = true;
                        Log?.Invoke($"welcome #{LocalId} map={map} tick={TickRate}");
                        break;
                    }
                case Msg.Snapshot: ReadSnapshot(); break;
                case Msg.PlayerInfo:
                    {
                        int id = _r.Byte();
                        var team = (Team)_r.Byte();
                        string name = _r.String();
                        bool bot = _r.Bool();
                        if (id == LocalId) break;
                        if (!_remotes.TryGetValue(id, out var rm)) { rm = new Remote { Id = id }; _remotes[id] = rm; }
                        rm.Name = name; rm.Team = team; rm.IsBot = bot;
                        break;
                    }
                case Msg.PlayerLeft: _remotes.Remove(_r.Byte()); break;
                case Msg.ShotFx:
                    {
                        var shot = Protocol.ReadShot(_r);
                        if (shot.ShooterId != LocalId) ShotFired?.Invoke(shot, false);
                        break;
                    }
                case Msg.Hit:
                    {
                        var h = new HitEvent { Attacker = _r.Byte(), Victim = _r.Byte(), Group = (HitGroup)_r.Byte(), Point = _r.Vec3(), Damage = _r.Short(), VictimHealth = _r.Short() };
                        Hit?.Invoke(h);
                        break;
                    }
                case Msg.Kill:
                    {
                        var k = new KillEvent { Killer = _r.Byte(), Victim = _r.Byte(), Weapon = (WeaponId)_r.Byte(), Headshot = _r.Bool(), Wallbang = _r.Bool() };
                        Killed?.Invoke(k);
                        break;
                    }
            }
        }

        private void ReadSnapshot()
        {
            int tick = _r.Int();
            int ack = _r.Int();
            var serverState = Protocol.ReadState(_r);
            int count = _r.Byte();
            if (tick <= _latestSnapTick) return; // out of order / duplicate
            SnapshotsReceived++;
            _latestSnapTick = tick;
            _timeSinceSnap = 0;
            for (int i = 0; i < count; i++)
            {
                var rs = Protocol.ReadRemote(_r);
                if (_r.Error) return;
                if (!_remotes.TryGetValue(rs.Id, out var rm)) { rm = new Remote { Id = rs.Id, Team = rs.Team }; _remotes[rs.Id] = rm; }
                rm.Latest = rs; rm.Team = rs.Team;
                rm.Snaps.Add((tick, rs));
                if (rm.Snaps.Count > 64) rm.Snaps.RemoveAt(0);
            }
            Reconcile(ack, serverState);
        }

        private void Reconcile(int ack, in PlayerState server)
        {
            if (ack < 0) { Predicted = server; PreviousPredicted = server; return; }
            if (ack <= _lastAck) return;
            _lastAck = ack;
            int i = ack % BufSize;
            if (ack > CmdTick || !_valid[i] || _cmds[i].Tick != ack)
            {
                // we have no record of that command (e.g. just connected): adopt the server state
                Predicted = server; PreviousPredicted = server;
                if (ack > CmdTick) CmdTick = ack;
                return;
            }
            if (PlayerState.NearlyEqual(_states[i], server)) return;

            // misprediction: rewind to the authoritative state and replay newer commands
            Mispredictions++;
            var before = Predicted.Position;
            var state = server;
            _replayCtx.World = World; _replayCtx.Dt = Dt; _replayCtx.PlayerId = LocalId;
            _states[i] = server;
            var prev = server;
            for (int t = ack + 1; t <= CmdTick; t++)
            {
                int k = t % BufSize;
                if (!_valid[k] || _cmds[k].Tick != t) continue;
                prev = state;
                PlayerSimulation.Step(ref state, _cmds[k], _replayCtx);
                _states[k] = state;
            }
            Predicted = state;
            PreviousPredicted = prev;
            var delta = before - Predicted.Position;
            LastCorrection = delta.Length();
            // small corrections are smoothed visually, big ones (teleports/respawns) snap
            CorrectionOffset = delta.Length() < 1.0f ? CorrectionOffset + delta : Vector3.Zero;
        }

        /// <summary>Interpolated state of another player at the current render time.</summary>
        public bool TryGetRemotePose(int id, out RemoteState pose)
        {
            pose = default;
            if (!_remotes.TryGetValue(id, out var rm) || rm.Snaps.Count == 0) return false;
            float rt = RenderTick;
            var snaps = rm.Snaps;
            // drop snapshots that are too old, keeping one before render time
            while (snaps.Count > 2 && snaps[1].tick <= rt) snaps.RemoveAt(0);
            if (snaps.Count == 1 || rt <= snaps[0].tick) { pose = snaps[0].s; return true; }
            for (int i = 0; i < snaps.Count - 1; i++)
            {
                var a = snaps[i]; var b = snaps[i + 1];
                if (rt >= a.tick && rt <= b.tick)
                {
                    float t = (rt - a.tick) / Math.Max(1, b.tick - a.tick);
                    pose = a.s;
                    pose.Position = Vector3.Lerp(a.s.Position, b.s.Position, t);
                    pose.Yaw = a.s.Yaw + VMath.AngleDelta(a.s.Yaw, b.s.Yaw) * t;
                    pose.Pitch = VMath.Lerp(a.s.Pitch, b.s.Pitch, t);
                    pose.DuckAmount = VMath.Lerp(a.s.DuckAmount, b.s.DuckAmount, t);
                    if (t > 0.5f) { pose.Alive = b.s.Alive; pose.Weapon = b.s.Weapon; pose.Health = b.s.Health; }
                    return true;
                }
            }
            // render time is ahead of what we have (packet loss): hold the newest (no extrapolation for fairness)
            pose = snaps[snaps.Count - 1].s;
            return true;
        }

        /// <summary>Local player position to render this frame (interpolated between ticks + correction smoothing).</summary>
        public Vector3 RenderPosition => Vector3.Lerp(PreviousPredicted.Position, Predicted.Position, Math.Min(1f, Alpha)) + CorrectionOffset;
        public float RenderEyeHeight => VMath.Lerp(PreviousPredicted.EyeHeight, Predicted.EyeHeight, Math.Min(1f, Alpha));
    }
}
