using System;
using System.Collections.Generic;

namespace Vexa.Core.Net
{
    /// <summary>
    /// Minimal transport abstraction. The game code only talks to this, so the same server/client
    /// logic runs over LiteNetLib UDP in production and over <see cref="LoopbackNetwork"/> in tests.
    /// On the client side the server is always peer 0.
    /// </summary>
    public interface ITransport
    {
        void Poll();
        void Send(int peer, byte[] data, int length, Delivery delivery);
        void Disconnect(int peer);
        int GetRttMs(int peer);
        event Action<int> Connected;
        event Action<int> Disconnected;
        event Action<int, byte[], int> Received;
    }

    /// <summary>
    /// In-memory network with configurable one-way latency, jitter and packet loss, driven by a manual clock.
    /// Reliable messages are never dropped and keep their order.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        public double Now;
        public double Latency = 0.04;
        public double Jitter = 0.0;
        public double Loss = 0.0;
        private readonly Random _rng;
        private int _nextPeer = 1;
        internal readonly List<Packet> InFlight = new List<Packet>();
        public readonly Endpoint Server;

        public LoopbackNetwork(int seed = 1)
        {
            _rng = new Random(seed);
            Server = new Endpoint(this, true);
        }

        private Endpoint CreateClient()
        {
            var c = new Endpoint(this, false) { ServerPeerIdForMe = _nextPeer++ };
            return c;
        }

        public void Advance(double dt) => Now += dt;

        internal sealed class Packet
        {
            public double DeliverAt;
            public Endpoint To;
            public int FromPeer;     // peer id as seen by the receiver
            public byte[] Data;
            public int Kind;         // 0 data, 1 connect, 2 disconnect
        }

        private readonly Dictionary<(Endpoint, Endpoint), double> _lastReliable = new Dictionary<(Endpoint, Endpoint), double>();

        internal void Enqueue(Endpoint from, Endpoint to, int fromPeer, byte[] data, int len, Delivery d, int kind = 0)
        {
            bool reliable = d == Delivery.ReliableOrdered || kind != 0;
            if (!reliable && _rng.NextDouble() < Loss) return;
            double at = Now + Latency + (Jitter > 0 ? (_rng.NextDouble() * 2 - 1) * Jitter : 0);
            if (at < Now) at = Now;
            if (reliable)
            {
                _lastReliable.TryGetValue((from, to), out double last);
                if (at < last) at = last;
                _lastReliable[(from, to)] = at;
            }
            var copy = new byte[len];
            if (len > 0) Buffer.BlockCopy(data, 0, copy, 0, len);
            InFlight.Add(new Packet { DeliverAt = at, To = to, FromPeer = fromPeer, Data = copy, Kind = kind });
        }

        public sealed class Endpoint : ITransport
        {
            private readonly LoopbackNetwork _net;
            private readonly bool _isServer;
            internal int ServerPeerIdForMe;      // client: my id on the server
            private readonly Dictionary<int, Endpoint> _peers = new Dictionary<int, Endpoint>();
            public event Action<int> Connected;
            public event Action<int> Disconnected;
            public event Action<int, byte[], int> Received;

            internal Endpoint(LoopbackNetwork net, bool server) { _net = net; _isServer = server; }

            public void Connect()
            {
                if (_isServer) throw new InvalidOperationException();
                _net.Enqueue(this, _net.Server, ServerPeerIdForMe, Array.Empty<byte>(), 0, Delivery.ReliableOrdered, 1);
            }

            public void Poll()
            {
                var list = _net.InFlight;
                // deliver in time order
                list.Sort((a, b) => a.DeliverAt.CompareTo(b.DeliverAt));
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.To != this || p.DeliverAt > _net.Now) continue;
                    list.RemoveAt(i); i--;
                    if (p.Kind == 1)
                    {
                        if (_isServer)
                        {
                            Endpoint client = null;
                            foreach (var q in _net._clients) if (q.ServerPeerIdForMe == p.FromPeer) client = q;
                            if (client == null) continue;
                            _peers[p.FromPeer] = client;
                            Connected?.Invoke(p.FromPeer);
                            _net.Enqueue(this, client, 0, Array.Empty<byte>(), 0, Delivery.ReliableOrdered, 1);
                        }
                        else Connected?.Invoke(0);
                    }
                    else if (p.Kind == 2) Disconnected?.Invoke(p.FromPeer);
                    else Received?.Invoke(p.FromPeer, p.Data, p.Data.Length);
                }
            }

            public void Send(int peer, byte[] data, int length, Delivery delivery)
            {
                if (_isServer)
                {
                    if (_peers.TryGetValue(peer, out var c)) _net.Enqueue(this, c, 0, data, length, delivery);
                }
                else _net.Enqueue(this, _net.Server, ServerPeerIdForMe, data, length, delivery);
            }

            public void Disconnect(int peer)
            {
                if (_isServer) { if (_peers.TryGetValue(peer, out var c)) { _net.Enqueue(this, c, 0, Array.Empty<byte>(), 0, Delivery.ReliableOrdered, 2); _peers.Remove(peer); } }
                else _net.Enqueue(this, _net.Server, ServerPeerIdForMe, Array.Empty<byte>(), 0, Delivery.ReliableOrdered, 2);
            }

            public int GetRttMs(int peer) => (int)(_net.Latency * 2000);
        }

        private readonly List<Endpoint> _clients = new List<Endpoint>();
        public Endpoint AddClient()
        {
            var c = CreateClient();
            _clients.Add(c);
            return c;
        }
    }
}
