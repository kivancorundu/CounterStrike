using System;
using System.Collections.Generic;
using LiteNetLib;
using Vexa.Core.Net;
using CoreDelivery = Vexa.Core.Net.Delivery;

namespace Vexa.Net
{
    /// <summary>UDP transport on LiteNetLib. Works in Unity and in the standalone .NET server.</summary>
    public sealed class LiteNetTransport : ITransport, IDisposable
    {
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _manager;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        private readonly bool _isServer;
        private NetPeer _server;
        private byte[] _rx = new byte[2048];

        public event Action<int> Connected;
        public event Action<int> Disconnected;
        public event Action<int, byte[], int> Received;

        public int MaxPlayers = 32;
        public bool IsRunning => _manager.IsRunning;

        private LiteNetTransport(bool server)
        {
            _isServer = server;
            _manager = new NetManager(_listener)
            {
                AutoRecycle = true,
                UpdateTime = 1,
                DisconnectTimeout = 8000,
                UnsyncedEvents = false,
            };
            _listener.ConnectionRequestEvent += req =>
            {
                if (!_isServer || _manager.ConnectedPeersCount >= MaxPlayers) { req.Reject(); return; }
                req.AcceptIfKey(Protocol.ConnectKey);
            };
            _listener.PeerConnectedEvent += peer =>
            {
                if (_isServer) { _peers[peer.Id] = peer; Connected?.Invoke(peer.Id); }
                else { _server = peer; Connected?.Invoke(0); }
            };
            _listener.PeerDisconnectedEvent += (peer, info) =>
            {
                if (_isServer) { _peers.Remove(peer.Id); Disconnected?.Invoke(peer.Id); }
                else { _server = null; Disconnected?.Invoke(0); }
            };
            _listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                int len = reader.AvailableBytes;
                if (_rx.Length < len) _rx = new byte[len * 2];
                reader.GetBytes(_rx, len);
                Received?.Invoke(_isServer ? peer.Id : 0, _rx, len);
            };
        }

        public static LiteNetTransport StartServer(int port, int maxPlayers = 32)
        {
            var t = new LiteNetTransport(true) { MaxPlayers = maxPlayers };
            if (!t._manager.Start(port)) throw new InvalidOperationException("could not bind UDP port " + port);
            return t;
        }

        public static LiteNetTransport Connect(string host, int port)
        {
            var t = new LiteNetTransport(false);
            t._manager.Start();
            t._manager.Connect(host, port, Protocol.ConnectKey);
            return t;
        }

        public void Poll() => _manager.PollEvents();

        public void Send(int peer, byte[] data, int length, CoreDelivery delivery)
        {
            var p = _isServer ? (_peers.TryGetValue(peer, out var x) ? x : null) : _server;
            if (p == null) return;
            DeliveryMethod m = delivery == CoreDelivery.ReliableOrdered ? DeliveryMethod.ReliableOrdered
                : delivery == CoreDelivery.Sequenced ? DeliveryMethod.Sequenced : DeliveryMethod.Unreliable;
            p.Send(data, 0, length, m);
        }

        public void Disconnect(int peer)
        {
            var p = _isServer ? (_peers.TryGetValue(peer, out var x) ? x : null) : _server;
            p?.Disconnect();
        }

        public int GetRttMs(int peer)
        {
            var p = _isServer ? (_peers.TryGetValue(peer, out var x) ? x : null) : _server;
            return p == null ? 0 : p.Ping * 2;
        }

        public void Dispose() => _manager.Stop();
    }
}
