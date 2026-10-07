using System;

namespace Vexa.Core
{
    [Flags]
    public enum Buttons : ushort
    {
        None = 0,
        Forward = 1 << 0,
        Back = 1 << 1,
        Left = 1 << 2,
        Right = 1 << 3,
        Jump = 1 << 4,
        Duck = 1 << 5,
        Walk = 1 << 6,
        Attack = 1 << 7,
        Attack2 = 1 << 8,
        Reload = 1 << 9,
        Use = 1 << 10,
        Inspect = 1 << 11,
        Drop = 1 << 12,
    }

    public enum WeaponSelect : byte
    {
        None = 0, Primary = 1, Secondary = 2, Melee = 3, Grenade = 4, Bomb = 5, LastUsed = 6, Next = 7, Previous = 8,
    }

    /// <summary>
    /// One user command, produced by the client every simulation tick (CS "usercmd").
    /// View angles are stored quantized so the client predicts with exactly what the server simulates.
    /// </summary>
    public struct PlayerInput
    {
        public int Tick;              // client command number (player's own timeline)
        public Buttons Buttons;
        public ushort YawQ;
        public short PitchQ;
        public WeaponSelect Select;
        public float InterpTick;      // server tick the client was rendering others at (lag compensation)

        public float Yaw => VMath.DequantizeYaw(YawQ);
        public float Pitch => VMath.DequantizePitch(PitchQ);
        public bool Has(Buttons b) => (Buttons & b) != 0;

        public void SetAngles(float yawDeg, float pitchDeg)
        {
            YawQ = VMath.QuantizeYaw(yawDeg);
            PitchQ = VMath.QuantizePitch(pitchDeg);
        }
    }
}
