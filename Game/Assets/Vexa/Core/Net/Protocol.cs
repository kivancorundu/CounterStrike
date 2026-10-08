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
        MatchState = 12,  // S->C  scores / round / mode (reliable, on change)
        Scoreboard = 13,  // S->C  per player stats + money (teammates only)
        RoundEnd = 14,    // S->C  winner, reason, MVP
        Event = 15,       // S->C  bomb planted/defused, grenade detonations, purchases...
        TeamSelect = 16,  // C->S  0 auto / 1 T / 2 CT
    }

    public enum GameMode : byte { Deathmatch = 0, Competitive = 1, Casual = 2, Practice = 3 }
    public enum GamePhase : byte { Warmup = 0, Freeze = 1, Live = 2, RoundEnd = 3, MatchOver = 4 }
    public enum RoundEndReason : byte { None = 0, Elimination, BombExploded, BombDefused, TimeExpired }
    public enum BombState : byte { None = 0, Carried, Dropped, Planted, Defused, Exploded }
    public enum GameEventType : byte
    {
        RoundStart = 1, BombPlanted, BombDefused, BombExploded, BombDropped, BombPickedUp,
        Detonation, Purchase, PurchaseDenied, Halftime, Message, DefuseStarted, MatchOver,
    }

    [System.Flags]
    public enum MatchFlags : byte
    {
        None = 0, Tournament = 1, WaitingReady = 2, KnifeRound = 4, SidePick = 8,
        TacticalPause = 16, TechnicalPause = 32, PausePending = 64,
    }

    /// <summary>Chat line. SenderId 0 = server message.</summary>
    public struct ChatMessage { public int SenderId; public Team SenderTeam; public bool TeamOnly, Dead; public string Text; }

    public struct BombInfo
    {
        public BombState State;
        public int CarrierId;
        public Vector3 Position;
        public string Site;
        public int PlantTick, ExplodeTick;
        public int DefuserId, DefuseStartTick, DefuseEndTick;
    }

    public struct ProjectileInfo { public int Id; public GrenadeType Type; public Vector3 Position; }
    public struct AreaInfo { public int Id; public GrenadeType Type; public Vector3 Center; public float Radius; public int StartTick, EndTick; }
    public struct ItemInfo { public int Id; public WeaponId Weapon; public Vector3 Position; }

    public struct ScoreEntry
    {
        public int Id; public Team Team; public bool Alive, IsBot;
        public int Money;   // -1 when hidden (enemy)
        public int Kills, Deaths, Assists, Mvps, Score, Damage, Headshots;
    }

    public struct RoundResult { public Team Winner; public RoundEndReason Reason; }

    public struct MatchHeader
    {
        public GamePhase Phase;
        public int PhaseEndTick;
        public int BuyEndTick;
    }

    public struct GameEvent
    {
        public GameEventType Type;
        public int A, B;
        public Vector3 Position;
        public string Text;
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
        public short Health, Armor;
        public bool HasC4, Planting, Defusing, HasKit, Helmet;
        public GrenadeType Grenade;
    }

    public struct HitEvent { public int Attacker, Victim; public HitGroup Group; public Vector3 Point; public int Damage; public int VictimHealth; }
    public struct KillEvent { public int Killer, Victim; public WeaponId Weapon; public bool Headshot, Wallbang, ThroughSmoke, AttackerBlind; public GrenadeType Grenade; }

    /// <summary>Wire encoding for every message. Shared by client and server.</summary>
    public static class Protocol
    {
        public const ushort Version = 2;
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
            w.Byte(s.NadeHE); w.Byte(s.NadeFlash); w.Byte(s.NadeSmoke); w.Byte(s.NadeFire); w.Byte(s.NadeDecoy);
            w.Byte((byte)s.ActiveGrenade); w.Float(s.PinTime); w.Float(s.ThrowStrength); w.Float(s.SwitchBackTime);
            w.Byte((byte)((s.HasC4 ? 1 : 0) | (s.HasKit ? 2 : 0) | (s.Planting ? 4 : 0) | (s.Defusing ? 8 : 0) | (s.DropHeld ? 16 : 0) | (s.UseHeld ? 32 : 0)));
            w.Float(s.PlantStartTime); w.Float(s.FlashEndTime); w.Float(s.FlashFullEndTime);
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
            s.NadeHE = r.Byte(); s.NadeFlash = r.Byte(); s.NadeSmoke = r.Byte(); s.NadeFire = r.Byte(); s.NadeDecoy = r.Byte();
            s.ActiveGrenade = (GrenadeType)r.Byte(); s.PinTime = r.Float(); s.ThrowStrength = r.Float(); s.SwitchBackTime = r.Float();
            byte h = r.Byte();
            s.HasC4 = (h & 1) != 0; s.HasKit = (h & 2) != 0; s.Planting = (h & 4) != 0; s.Defusing = (h & 8) != 0; s.DropHeld = (h & 16) != 0; s.UseHeld = (h & 32) != 0;
            s.PlantStartTime = r.Float(); s.FlashEndTime = r.Float(); s.FlashFullEndTime = r.Float();
            return s;
        }

        public static void WriteRemote(NetWriter w, int id, in PlayerState s)
        {
            w.Byte((byte)id); w.Byte((byte)s.Team);
            w.Byte((byte)((s.Alive ? 1 : 0) | (s.Ducked ? 2 : 0) | (s.OnGround ? 4 : 0) | (s.Reloading ? 8 : 0) | (s.Zoom > 0 ? 16 : 0)));
            w.Vec3(s.Position); w.Vec3(s.Velocity);
            w.UShort(VMath.QuantizeYaw(s.Yaw)); w.Short(VMath.QuantizePitch(s.Pitch));
            w.Byte((byte)(VMath.Clamp01(s.DuckAmount) * 255f));
            w.Byte((byte)s.VisibleWeapon);
            w.Short(s.Health);
            w.Byte((byte)((s.HasC4 ? 1 : 0) | (s.Planting ? 2 : 0) | (s.Defusing ? 4 : 0) | (s.HasKit ? 8 : 0) | (s.Helmet ? 16 : 0)));
            w.Short(s.Armor);
            w.Byte((byte)s.ActiveGrenade);
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
            byte g = r.Byte();
            o.HasC4 = (g & 1) != 0; o.Planting = (g & 2) != 0; o.Defusing = (g & 4) != 0; o.HasKit = (g & 8) != 0; o.Helmet = (g & 16) != 0;
            o.Armor = r.Short();
            o.Grenade = (GrenadeType)r.Byte();
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

        public static void WriteHeader(NetWriter w, in MatchHeader h) { w.Byte((byte)h.Phase); w.Int(h.PhaseEndTick); w.Int(h.BuyEndTick); }
        public static MatchHeader ReadHeader(NetReader r) => new MatchHeader { Phase = (GamePhase)r.Byte(), PhaseEndTick = r.Int(), BuyEndTick = r.Int() };

        public static void WriteBomb(NetWriter w, in BombInfo b)
        {
            w.Byte((byte)b.State); w.Byte((byte)b.CarrierId); w.Vec3(b.Position); w.String(b.Site ?? "");
            w.Int(b.PlantTick); w.Int(b.ExplodeTick); w.Byte((byte)b.DefuserId); w.Int(b.DefuseStartTick); w.Int(b.DefuseEndTick);
        }
        public static BombInfo ReadBomb(NetReader r) => new BombInfo
        {
            State = (BombState)r.Byte(), CarrierId = r.Byte(), Position = r.Vec3(), Site = r.String(),
            PlantTick = r.Int(), ExplodeTick = r.Int(), DefuserId = r.Byte(), DefuseStartTick = r.Int(), DefuseEndTick = r.Int(),
        };

        public static void WriteArea(NetWriter w, in AreaInfo a) { w.UShort((ushort)a.Id); w.Byte((byte)a.Type); w.Vec3(a.Center); w.Float(a.Radius); w.Int(a.StartTick); w.Int(a.EndTick); }
        public static AreaInfo ReadArea(NetReader r) => new AreaInfo { Id = r.UShort(), Type = (GrenadeType)r.Byte(), Center = r.Vec3(), Radius = r.Float(), StartTick = r.Int(), EndTick = r.Int() };

        public static void WriteChat(NetWriter w, in ChatMessage m)
        {
            w.Reset(); w.Byte((byte)Msg.Chat); w.Byte((byte)m.SenderId); w.Byte((byte)m.SenderTeam);
            w.Byte((byte)((m.TeamOnly ? 1 : 0) | (m.Dead ? 2 : 0))); w.String(m.Text ?? "");
        }
        public static ChatMessage ReadChat(NetReader r)
        {
            var m = new ChatMessage { SenderId = r.Byte(), SenderTeam = (Team)r.Byte() };
            byte f = r.Byte();
            m.TeamOnly = (f & 1) != 0; m.Dead = (f & 2) != 0;
            m.Text = r.String();
            return m;
        }

        public static void WriteEvent(NetWriter w, in GameEvent e) { w.Byte((byte)Msg.Event); w.Byte((byte)e.Type); w.Int(e.A); w.Int(e.B); w.Vec3(e.Position); w.String(e.Text ?? ""); }
        public static GameEvent ReadEvent(NetReader r) => new GameEvent { Type = (GameEventType)r.Byte(), A = r.Int(), B = r.Int(), Position = r.Vec3(), Text = r.String() };

        public static void WriteScore(NetWriter w, in ScoreEntry e)
        {
            w.Byte((byte)e.Id); w.Byte((byte)e.Team); w.Byte((byte)((e.Alive ? 1 : 0) | (e.IsBot ? 2 : 0)));
            w.Int(e.Money); w.Short((short)e.Kills); w.Short((short)e.Deaths); w.Short((short)e.Assists); w.Short((short)e.Mvps);
            w.Short((short)e.Score); w.Int(e.Damage); w.Short((short)e.Headshots);
        }
        public static ScoreEntry ReadScore(NetReader r)
        {
            var e = new ScoreEntry { Id = r.Byte(), Team = (Team)r.Byte() };
            byte f = r.Byte(); e.Alive = (f & 1) != 0; e.IsBot = (f & 2) != 0;
            e.Money = r.Int(); e.Kills = r.Short(); e.Deaths = r.Short(); e.Assists = r.Short(); e.Mvps = r.Short();
            e.Score = r.Short(); e.Damage = r.Int(); e.Headshots = r.Short();
            return e;
        }
    }
}