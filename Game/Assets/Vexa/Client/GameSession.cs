using System;
using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;
using Vexa.Core.Server;
using Vexa.Net;

namespace Vexa.Client
{
    /// <summary>
    /// Owns one play session: optionally an in-process host server, the network client,
    /// input, camera, player visuals, effects and HUD. Unity is only the presentation layer;
    /// all gameplay runs in Vexa.Core.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Current { get; private set; }

        public ClientGame Client { get; private set; }
        public ServerGame HostServer { get; private set; }
        public InputSampler Input { get; } = new InputSampler();
        public string Status = "";
        public bool Failed;
        public float HitMarkerUntil, HitMarkerKillUntil, DamageFlashUntil;
        /// <summary>Set by the UI: true while a menu/overlay owns the mouse and keyboard.</summary>
        public static bool InputBlocked;
        public bool Ready => Client != null && Client.Welcomed && _mapRoot != null;

        private LiteNetTransport _clientNet, _serverNet;
        private GameObject _mapRoot;
        private readonly Dictionary<int, PlayerAvatar> _avatars = new Dictionary<int, PlayerAvatar>();
        private LocalPlayerCamera _camera;
        private ShotEffects _effects;
        private WorldVisuals _world;
        private float _connectStarted;

        /// <summary>Everything the play screen chooses for a locally hosted match.</summary>
        public sealed class MatchSetup
        {
            public GameMode Mode = GameMode.Competitive;
            public string Map = "kasaba";
            public int Port = 27015;
            public int TickRate = 64;
            public int TeamSize = 5;           // competitive / casual: bots fill both teams to this size
            public int DeathmatchBots = 7;
            public float BotDifficulty = 0.5f;
            public Team Side = Team.None;      // preferred side, None = auto
            public string PlayerName = "Oyuncu";

            public MatchConfig ToConfig()
            {
                MatchConfig c;
                switch (Mode)
                {
                    case GameMode.Casual: c = MatchConfig.Casual(); break;
                    case GameMode.Deathmatch: c = MatchConfig.Deathmatch(); break;
                    case GameMode.Practice: c = MatchConfig.Practice(); break;
                    default: c = MatchConfig.Competitive(); break;
                }
                if (c.HasRounds) c.TeamSize = Mathf.Clamp(TeamSize, 1, 5);
                c.BotDifficulty = Mathf.Clamp01(BotDifficulty);
                return c;
            }
        }

        public MatchSetup Setup { get; private set; }
        public string MapName { get; private set; } = "";
        private bool _sideRequested;

        public static GameSession Host(MatchSetup setup)
        {
            var s = Create();
            s.Setup = setup;
            s.MapName = setup.Map;
            try
            {
                var mapData = MapLoader.Load(setup.Map);
                var config = setup.ToConfig();
                s._serverNet = LiteNetTransport.StartServer(setup.Port);
                s.HostServer = new ServerGame(s._serverNet, mapData, setup.TickRate, config);
                s.HostServer.Log += m => Debug.Log("[server] " + m);
                if (setup.Mode == GameMode.Deathmatch || setup.Mode == GameMode.Practice)
                {
                    int bots = setup.Mode == GameMode.Deathmatch ? setup.DeathmatchBots : 0;
                    for (int i = 0; i < bots; i++) s.HostServer.AddBot(BotName(i));
                }
                s.Connect("127.0.0.1", setup.Port, setup.PlayerName);
            }
            catch (Exception e) { s.Status = "Sunucu başlatılamadı: " + e.Message; s.Failed = true; Debug.LogException(e); }
            return s;
        }

        static readonly string[] DmBotNames = { "Kartal", "Poyraz", "Bozkurt", "Atlas", "Toprak", "Yıldırım", "Kaya", "Demir", "Fırtına", "Doruk", "Alaz", "Tuna", "Efe", "Baran", "Kuzey" };
        static string BotName(int i) => DmBotNames[i % DmBotNames.Length] + (i >= DmBotNames.Length ? " " + (i / DmBotNames.Length + 1) : "");

        public static GameSession Join(string host, int port, string playerName)
        {
            var s = Create();
            s.MapName = "";
            s.Connect(host, port, playerName);
            return s;
        }

        static GameSession Create()
        {
            if (Current != null) Current.Shutdown();
            var go = new GameObject("VexaSession");
            var s = go.AddComponent<GameSession>();
            Current = s;
            s.Input.MobileControls = Application.isMobilePlatform;
            s.Input.Sensitivity = UI.VexaSettings.Sensitivity;
            s.Input.TouchSensitivity = UI.VexaSettings.TouchSensitivity;
            return s;
        }

        void Connect(string host, int port, string name)
        {
            Status = $"Bağlanılıyor {host}:{port} ...";
            _connectStarted = Time.unscaledTime;
            _clientNet = LiteNetTransport.Connect(host, port);
            Client = new ClientGame(_clientNet, name, MapLoader.Load);
            Client.Log += m => Debug.Log("[client] " + m);
            Client.ShotFired += OnShot;
            Client.Hit += OnHit;
            Client.Spawned += OnSpawned;
        }

        void Awake()
        {
            _camera = new LocalPlayerCamera();
            _effects = new ShotEffects();
            _world = new WorldVisuals();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            HostServer?.Update(dt);
            if (Client == null) return;

            Input.Enabled = !InputBlocked;
            if (Client.Welcomed)
            {
                var def = Client.Predicted.ActiveDef;
                Input.ZoomSensitivityScale = Client.Predicted.Zoom > 0 && def.ZoomFov != null ? def.ZoomFov[Client.Predicted.Zoom - 1] / 90f * UI.VexaSettings.ZoomSensitivity : 1f;
            }
            Input.UpdateFrame();
            Client.Update(dt, Input.SampleCommand);

            if (Client.Disconnected)
            {
                Status = Client.Welcomed ? "Sunucu bağlantısı koptu." : "Sunucuya bağlanılamadı.";
                Failed = true;
                return;
            }
            if (!Client.Welcomed)
            {
                if (Time.unscaledTime - _connectStarted > 10f) { Status = "Bağlantı zaman aşımı. Sunucu adresini kontrol et."; Failed = true; }
                return;
            }
            Status = "";
            if (_mapRoot == null)
            {
                _mapRoot = MapBuilder.Build(Client.Map);
                if (string.IsNullOrEmpty(MapName)) MapName = Client.Map.Name;
            }
            if (!_sideRequested && Setup != null)
            {
                _sideRequested = true;
                if (Setup.Side != Team.None && Setup.Mode != GameMode.Deathmatch) Client.SelectTeam(Setup.Side);
            }
            SyncAvatars();
            _camera.Update(this, dt);
            _effects.Update(dt);
            _world.Update(Client, dt);
        }

        void SyncAvatars()
        {
            var seen = new HashSet<int>();
            foreach (var r in Client.Remotes)
            {
                seen.Add(r.Id);
                if (!Client.TryGetRemotePose(r.Id, out var pose)) continue;
                if (!_avatars.TryGetValue(r.Id, out var av)) { av = PlayerAvatar.Create(r.Name, pose.Team); _avatars[r.Id] = av; }
                av.Apply(pose, r.Name);
            }
            var gone = new List<int>();
            foreach (var kv in _avatars) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { Destroy(_avatars[id].gameObject); _avatars.Remove(id); }
        }

        void OnShot(ShotInfo shot, bool local)
        {
            if (Client?.World == null) return;
            Vector3 muzzle;
            if (local)
            {
                // tracer starts slightly right/below the eye, like a viewmodel muzzle
                var rot = UnityBridge.ViewRotation(Input.Yaw, Input.Pitch);
                muzzle = shot.Origin.ToU() + rot * new Vector3(0.12f, -0.1f, 0.4f);
            }
            else muzzle = shot.Origin.ToU() + UnityBridge.ViewRotation(shot.Yaw, shot.Pitch) * new Vector3(0.1f, -0.15f, 0.6f);
            _effects.Shot(Client.World, shot, muzzle, local);
        }

        /// <summary>Face the way the spawn point faces (our view angles drive the simulation, so the server's spawn yaw must be copied here).</summary>
        void OnSpawned()
        {
            var map = Client.Map;
            if (map == null) return;
            var pos = Client.Predicted.Position;
            float best = 2.0f;
            foreach (var sp in map.Spawns)
            {
                float d = System.Numerics.Vector3.Distance(sp.Position, pos);
                if (d < best) { best = d; Input.Yaw = sp.Yaw; Input.Pitch = 0; }
            }
        }

        void OnHit(HitEvent h)
        {
            if (h.Attacker == Client.LocalId && h.Victim != Client.LocalId)
            {
                HitMarkerUntil = Time.unscaledTime + 0.15f;
                if (h.VictimHealth <= 0) HitMarkerKillUntil = Time.unscaledTime + 0.35f;
            }
            if (h.Victim == Client.LocalId) DamageFlashUntil = Time.unscaledTime + 0.25f;
            _effects.Blood(h.Point.ToU(), h.Group == HitGroup.Head);
        }

        public void Shutdown()
        {
            foreach (var a in _avatars.Values) if (a != null) Destroy(a.gameObject);
            _avatars.Clear();
            if (_mapRoot != null) Destroy(_mapRoot);
            _effects?.Clear();
            _world?.Clear();
            _clientNet?.Dispose(); _clientNet = null;
            _serverNet?.Dispose(); _serverNet = null;
            Client = null; HostServer = null;
            if (Current == this) Current = null;
            Input.SetCursorLock(false);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            _clientNet?.Dispose();
            _serverNet?.Dispose();
            if (Current == this) Current = null;
        }
    }
}
