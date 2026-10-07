using System.Numerics;

namespace Vexa.Core.Net
{
    public enum Msg : byte
    {
        Hello = 1,        // C->S  protocol, name
        Welcome = 2,      // S->C  your id, tick rate, server tick, map
        Input = 3,        // C->S  redundant user commands
        Snapshot = 4,     // S->C  world state + your authoritative state
        ShotFx = 5,       // S->C  someone else fired (for tracers / sounds / impacts)
        Hit = 6,          // S->C  damage confirmation
        Kill = 7,         // S->C  kill feed
        PlayerInfo = 8,   // S->C  name / team of a player
        PlayerLeft = 9,   // S->C
        Buy = 10,         // C->S  request an item
        Chat = 11,
    }

    public enum Delivery : byte { Unreliable = 0, ReliableOrdered = 1, Sequenced = 2 }

    public struct RemoteState
    {
        public int Id;
        public Team Team;
        public bool Alive, Ducked, OnGround, Reloading, Scoped;
        public Vector3 Position, Velocity;
        public float Yaw, Pitch, DuckAmount;
        public WeaponId Weapon;
        public short Health;
    }

    public struct HitEvent { public int Attacker, Victim; public HitGroup Group; public Vector3 Point; public int Damage; public int VictimHealth; }
    public struct KillEvent { public int Killer, Victim; public WeaponId Weapon; public bool Headshot, Wallbang; }

    /// <summary>Wire encoding for every message. Shared by client and server.</summary>
    public static class Protocol
    {
        public const ushort Version = 1;
        public const string ConnectKey = "VEXA";
        public const int InputRedundancy = 4;

        public static void WriteInput(NetWriter w, in PlayerInput c)
        {
            w.Int(c.Tick); w.UShort((ushort)c.Buttons); w.UShort(c.YawQ); w.Short(c.PitchQ); w.Byte((byte)c.Select); w.Float(c.InterpTick);
        }
        public static PlayerInput ReadInput(NetReader r) => new PlayerInput
        {
            Tick = r.Int(), Buttons = (Buttons)r.UShort(), YawQ = r.UShort(), PitchQ = r.Short(), Select = (WeaponSelect)r.Byte(), InterpTick = r.Float(),
        };

        static void WriteSlot(NetWriter w, in WeaponSlot s) { w.Byte((byte)s.Id); w.Short(s.Clip); w.Short(s.Reserve); w.Byte((byte)((s.Silenced ? 1 : 0) | (s.BurstMode ? 2 : 0))); }
        static WeaponSlot ReadSlot(NetReader r)
        {
            var s = new WeaponSlot { Id = (WeaponId)r.Byte(), Clip = r.Short(), Reserve = r.Short() };
            byte f = r.Byte(); s.Silenced = (f & 1) != 0; s.BurstMode = (f & 2) != 0;
            return s;
        }

        /// <summary>Full predicted state of the receiving player (needed for exact reconciliation).</summary>
        public static void WriteState(NetWriter w, in PlayerState s)
        {
            w.Vec3(s.Position); w.Vec3(s.Velocity); w.Float(s.Yaw); w.Float(s.Pitch);
            w.Byte((byte)((s.OnGround ? 1 : 0) | (s.Ducked ? 2 : 0) | (s.JumpHeld ? 4 : 0) | (s.Alive ? 8 : 0) | (s.Helmet ? 16 : 0) | (s.Reloading ? 32 : 0)));
            w.Byte((byte)((s.AttackHeld ? 1 : 0) | (s.Attack2Held ? 2 : 0) | (s.ReloadHeld ? 4 : 0) | (s.InspectHeld ? 8 : 0)));
            w.Float(s.DuckAmount); w.Float(s.Stamina); w.Float(s.VelocityModifier); w.Float(s.LastLandTime);
            w.Byte((byte)s.Team); w.Short(s.Health); w.Short(s.Armor);
            WriteSlot(w, s.Primary); WriteSlot(w, s.Secondary); WriteSlot(w, s.Melee);
            w.Byte((byte)s.Active); w.Byte((byte)s.LastActive);
            w.Float(s.NextAttackTime); w.Float(s.DeployEndTime); w.Float(s.ReloadStartTime); w.Float(s.ReloadEndTime);
            w.Float(s.RecoilIndex); w.Float(s.AccumInaccuracy); w.Float(s.LastShotTime);
            w.Byte(s.Zoom); w.Byte(s.ZoomResume); w.Float(s.ZoomResumeTime);
            w.Byte(s.BurstLeft); w.Float(s.NextBurstTime); w.Float(s.SilencerEndTime); w.Float(s.PrimeStartTime); w.Float(s.InspectEndTime);
            w.UShort(s.ShotCounter);
        }

        public static PlayerState ReadState(NetReader r)
        {
            var s = new PlayerState();
            s.Position = r.Vec3(); s.Velocity = r.Vec3(); s.Yaw = r.Float(); s.Pitch = r.Float();
            byte f = r.Byte();
            s.OnGround = (f & 1) != 0; s.Ducked = (f & 2) != 0; s.JumpHeld = (f & 4) != 0; s.Alive = (f & 8) != 0; s.Helmet = (f & 16) != 0; s.Reloading = (f & 32) != 0;
            byte g = r.Byte();
            s.AttackHeld = (g & 1) != 0; s.Attack2Held = (g & 2) != 0; s.ReloadHeld = (g & 4) != 0; s.InspectHeld = (g & 8) != 0;
            s.DuckAmount = r.Float(); s.Stamina = r.Float(); s.VelocityModifier = r.Float(); s.LastLandTime = r.Float();
            s.Team = (Team)r.Byte(); s.Health = r.Short(); s.Armor = r.Short();
            s.Primary = ReadSlot(r); s.Secondary = ReadSlot(r); s.Melee = ReadSlot(r);
            s.Active = (WeaponSlotKind)r.Byte(); s.LastActive = (WeaponSlotKind)r.Byte();
            s.NextAttackTime = r.Float(); s.DeployEndTime = r.Float(); s.ReloadStartTime = r.Float(); s.ReloadEndTime = r.Float();
            s.RecoilIndex = r.Float(); s.AccumInaccuracy = r.Float(); s.LastShotTime = r.Float();
            s.Zoom = r.Byte(); s.ZoomResume = r.Byte(); s.ZoomResumeTime = r.Float();
            s.BurstLeft = r.Byte(); s.NextBurstTime = r.Float(); s.SilencerEndTime = r.Float(); s.PrimeStartTime = r.Float(); s.InspectEndTime = r.Float();
            s.ShotCounter = r.UShort();
            return s;
        }

        public static void WriteRemote(NetWriter w, int id, in PlayerState s)
        {
            w.Byte((byte)id); w.Byte((byte)s.Team);
            w.Byte((byte)((s.Alive ? 1 : 0) | (s.Ducked ? 2 : 0) | (s.OnGround ? 4 : 0) | (s.Reloading ? 8 : 0) | (s.Zoom > 0 ? 16 : 0)));
            w.Vec3(s.Position); w.Vec3(s.Velocity);
            w.UShort(VMath.QuantizeYaw(s.Yaw)); w.Short(VMath.QuantizePitch(s.Pitch));
            w.Byte((byte)(VMath.Clamp01(s.DuckAmount) * 255f));
            var slot = s.GetSlot(s.Active);
            w.Byte((byte)(slot.IsEmpty ? WeaponId.Knife : slot.Id));
            w.Short(s.Health);
        }

        public static RemoteState ReadRemote(NetReader r)
        {
            var o = new RemoteState { Id = r.Byte(), Team = (Team)r.Byte() };
            byte f = r.Byte();
            o.Alive = (f & 1) != 0; o.Ducked = (f & 2) != 0; o.OnGround = (f & 4) != 0; o.Reloading = (f & 8) != 0; o.Scoped = (f & 16) != 0;
            o.Position = r.Vec3(); o.Velocity = r.Vec3();
            o.Yaw = VMath.DequantizeYaw(r.UShort()); o.Pitch = VMath.DequantizePitch(r.Short());
            o.DuckAmount = r.Byte() / 255f;
            o.Weapon = (WeaponId)r.Byte();
            o.Health = r.Short();
            return o;
        }

        public static void WriteShot(NetWriter w, in ShotInfo s)
        {
            w.Byte((byte)s.ShooterId); w.Byte((byte)s.Weapon); w.Vec3(s.Origin); w.Float(s.Yaw); w.Float(s.Pitch);
            w.Float(s.Inaccuracy); w.Float(s.Spread); w.UInt(s.Seed); w.Byte((byte)s.Pellets); w.Byte((byte)s.Flags);
        }
        public static ShotInfo ReadShot(NetReader r) => new ShotInfo
        {
            ShooterId = r.Byte(), Weapon = (WeaponId)r.Byte(), Origin = r.Vec3(), Yaw = r.Float(), Pitch = r.Float(),
            Inaccuracy = r.Float(), Spread = r.Float(), Seed = r.UInt(), Pellets = r.Byte(), Flags = (ShotFlags)r.Byte(),
        };
    }
}
