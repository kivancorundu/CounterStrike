using System;
using System.Numerics;
using System.Text;

namespace Vexa.Core.Net
{
    public sealed class NetWriter
    {
        private byte[] _buf;
        public int Length { get; private set; }
        public byte[] Data => _buf;

        public NetWriter(int capacity = 1024) { _buf = new byte[capacity]; }
        public void Reset() => Length = 0;

        private void Ensure(int n)
        {
            if (Length + n <= _buf.Length) return;
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, Length + n));
        }

        public void Byte(byte v) { Ensure(1); _buf[Length++] = v; }
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void UShort(ushort v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }
        public void Short(short v) => UShort((ushort)v);
        public void Int(int v) { Ensure(4); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); _buf[Length++] = (byte)(v >> 16); _buf[Length++] = (byte)(v >> 24); }
        public void UInt(uint v) => Int((int)v);
        public void Float(float v) => Int(BitConverter.SingleToInt32Bits(v));
        public void Vec3(Vector3 v) { Float(v.X); Float(v.Y); Float(v.Z); }
        public void String(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, 255);
            Byte((byte)n); Ensure(n);
            Buffer.BlockCopy(b, 0, _buf, Length, n); Length += n;
        }
        public byte[] ToArray() { var a = new byte[Length]; Buffer.BlockCopy(_buf, 0, a, 0, Length); return a; }
    }

    public sealed class NetReader
    {
        private byte[] _buf;
        private int _pos, _end;
        public bool Error { get; private set; }

        public NetReader() { }
        public NetReader(byte[] data, int length) { Set(data, length); }
        public void Set(byte[] data, int length) { _buf = data; _pos = 0; _end = length; Error = false; }
        public int Remaining => _end - _pos;

        private bool Need(int n) { if (_pos + n > _end) { Error = true; return false; } return true; }
        public byte Byte() => Need(1) ? _buf[_pos++] : (byte)0;
        public bool Bool() => Byte() != 0;
        public ushort UShort() { if (!Need(2)) return 0; ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }
        public short Short() => (short)UShort();
        public int Int() { if (!Need(4)) return 0; int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24); _pos += 4; return v; }
        public uint UInt() => (uint)Int();
        public float Float() => BitConverter.Int32BitsToSingle(Int());
        public Vector3 Vec3() => new Vector3(Float(), Float(), Float());
        public string String()
        {
            int n = Byte();
            if (!Need(n)) return "";
            var s = Encoding.UTF8.GetString(_buf, _pos, n); _pos += n; return s;
        }
    }
}
