using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vexa.Core.Net
{
    /// <summary>
    /// Demo file (.vxdemo): every packet a hidden spectator ("VEXA TV") received from the server, with its
    /// server time. Playing it back feeds those packets to an ordinary <see cref="Client.ClientGame"/>, so a
    /// demo looks exactly like live spectating.
    ///
    /// Layout: "VXDM", u16 version, string map, u16 tick rate, string created (ISO-8601), then records of
    /// [f64 time][u16 length][bytes] until the end of the file.
    /// </summary>
    public static class DemoFormat
    {
        public const ushort Version = 1;
        public static readonly byte[] Magic = { (byte)'V', (byte)'X', (byte)'D', (byte)'M' };
        public const string Extension = ".vxdemo";
    }

    public sealed class DemoWriter : IDisposable
    {
        private readonly BinaryWriter _w;
        private readonly Func<double> _clock;
        public int Packets { get; private set; }
        public long Bytes => _w.BaseStream.Position;

        public DemoWriter(Stream stream, string map, int tickRate, Func<double> clock)
        {
            _clock = clock;
            _w = new BinaryWriter(stream, Encoding.UTF8, false);
            _w.Write(DemoFormat.Magic);
            _w.Write(DemoFormat.Version);
            _w.Write(map ?? "");
            _w.Write((ushort)tickRate);
            _w.Write(DateTime.UtcNow.ToString("o"));
        }

        public void Write(byte[] data, int length)
        {
            _w.Write(_clock());
            _w.Write((ushort)length);
            _w.Write(data, 0, length);
            Packets++;
            if ((Packets & 255) == 0) _w.Flush();
        }

        public void Dispose()
        {
            _w.Flush();
            _w.Dispose();
        }
    }

    /// <summary>Transport decorator on the server: packets sent to <see cref="Peer"/> go into the demo file.</summary>
    public sealed class DemoTap : ITransport
    {
        public const int Peer = 1 << 20;
        private readonly ITransport _inner;
        public DemoWriter Writer;

        public DemoTap(ITransport inner)
        {
            _inner = inner;
            _inner.Connected += p => Connected?.Invoke(p);
            _inner.Disconnected += p => Disconnected?.Invoke(p);
            _inner.Received += (p, d, l) => Received?.Invoke(p, d, l);
        }

        public void Poll() => _inner.Poll();
        public void Send(int peer, byte[] data, int length, Delivery delivery)
        {
            if (peer == Peer) { Writer?.Write(data, length); return; }
            _inner.Send(peer, data, length, delivery);
        }
        public void Disconnect(int peer) { if (peer != Peer) _inner.Disconnect(peer); }
        public int GetRttMs(int peer) => peer == Peer ? 0 : _inner.GetRttMs(peer);
        public event Action<int> Connected;
        public event Action<int> Disconnected;
        public event Action<int, byte[], int> Received;
    }

    public sealed class DemoFile
    {
        public string Map;
        public int TickRate;
        public string Created;
        public readonly List<(double time, byte[] data)> Packets = new List<(double, byte[])>();
        public double Duration => Packets.Count == 0 ? 0 : Packets[Packets.Count - 1].time - Packets[0].time;
        public double StartTime => Packets.Count == 0 ? 0 : Packets[0].time;
        /// <summary>Server times at which rounds started (for "next round" seeking).</summary>
        public readonly List<(double time, int round)> Rounds = new List<(double, int)>();

        public static DemoFile Read(Stream stream)
        {
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            var magic = r.ReadBytes(4);
            for (int i = 0; i < 4; i++) if (magic.Length < 4 || magic[i] != DemoFormat.Magic[i]) throw new InvalidDataException("not a VEXA demo");
            ushort ver = r.ReadUInt16();
            if (ver != DemoFormat.Version) throw new InvalidDataException("unsupported demo version " + ver);
            var f = new DemoFile { Map = r.ReadString(), TickRate = r.ReadUInt16(), Created = r.ReadString() };
            while (stream.Position < stream.Length)
            {
                double t;
                ushort len;
                try { t = r.ReadDouble(); len = r.ReadUInt16(); }
                catch (EndOfStreamException) { break; } // a demo cut off by a crash still plays up to that point
                var data = r.ReadBytes(len);
                if (data.Length < len) break;
                f.Packets.Add((t, data));
                if (len >= 6 && data[0] == (byte)Msg.Event && data[1] == (byte)GameEventType.RoundStart)
                    f.Rounds.Add((t, BitConverter.ToInt32(data, 2)));
            }
            return f;
        }

        /// <summary>Header, duration and round count without loading the packets (for demo lists).</summary>
        public static DemoFile ReadInfo(string path)
        {
            using (var stream = System.IO.File.OpenRead(path))
            {
                var r = new BinaryReader(stream, Encoding.UTF8, true);
                var magic = r.ReadBytes(4);
                for (int i = 0; i < 4; i++) if (magic.Length < 4 || magic[i] != DemoFormat.Magic[i]) throw new InvalidDataException("not a VEXA demo");
                if (r.ReadUInt16() != DemoFormat.Version) throw new InvalidDataException("unsupported demo version");
                var f = new DemoFile { Map = r.ReadString(), TickRate = r.ReadUInt16(), Created = r.ReadString() };
                double first = -1, last = 0;
                var head = new byte[2];
                while (stream.Position + 10 <= stream.Length)
                {
                    double t = r.ReadDouble();
                    ushort len = r.ReadUInt16();
                    if (stream.Position + len > stream.Length) break;
                    if (len >= 2) { stream.Read(head, 0, 2); stream.Seek(len - 2, SeekOrigin.Current); }
                    else stream.Seek(len, SeekOrigin.Current);
                    if (first < 0) first = t;
                    last = t;
                    if (len >= 6 && head[0] == (byte)Msg.Event && head[1] == (byte)GameEventType.RoundStart) f.Rounds.Add((t, 0));
                }
                f.InfoDuration = first < 0 ? 0 : last - first;
                return f;
            }
        }

        /// <summary>Duration from <see cref="ReadInfo"/> (packets aren't loaded there).</summary>
        public double InfoDuration;

        public static DemoFile Read(string path)
        {
            using (var s = File.OpenRead(path)) return Read(s);
        }
    }

    /// <summary>Client-side transport that replays a demo with a controllable clock (pause, speed, seek forward).</summary>
    public sealed class DemoPlayback : ITransport
    {
        public readonly DemoFile File;
        private int _next;
        private bool _connected;
        public double Time { get; private set; }
        public double Elapsed => Time - File.StartTime;
        public bool Finished => _next >= File.Packets.Count;

        public DemoPlayback(DemoFile file)
        {
            File = file;
            Time = file.StartTime;
        }

        public void Advance(double dt) => Time += dt;

        public void Poll()
        {
            if (!_connected) { _connected = true; Connected?.Invoke(0); }
            while (_next < File.Packets.Count && File.Packets[_next].time <= Time)
            {
                var p = File.Packets[_next++];
                Received?.Invoke(0, p.data, p.data.Length);
            }
        }

        public void Send(int peer, byte[] data, int length, Delivery delivery) { } // a demo can't be talked to
        public void Disconnect(int peer) { }
        public int GetRttMs(int peer) => 0;
        public event Action<int> Connected;
        public event Action<int> Disconnected { add { } remove { } }
        public event Action<int, byte[], int> Received;
    }
}
