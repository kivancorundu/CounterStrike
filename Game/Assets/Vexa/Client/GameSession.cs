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

        // ---- spectating (dead players, spectators, demos) ----
        public readonly Spectator Spectate = new Spectator();
        public bool Spectating { get; private set; }

        // ---- demo playback ----
        public DemoPlayback Demo { get; private set; }
        public bool IsDemo => Demo != null;
        public float DemoSpeed = 1f;
        public bool DemoPaused;
        /// <summary>True while fast-forwarding a demo: effects and HUD events should stay quiet.</summary>
        public bool Seeking { get; private set; }
        public string DemoPath { get; private set; }
        /// <summary>Raised when <see cref="Client"/> is replaced (demo seeking backwards rebuilds it).</summary>
        public event Action ClientReplaced;

        public static string DemoFolder => System.IO.Path.Combine(Application.persistentDataPath, "demos");

        private LiteNetTransport _clientNet, _serverNet;
        private GameObject _mapRoot;
        private readonly Dictionary<int, PlayerAvatar> _avatars = new Dictionary<int, PlayerAvatar>();
        private LocalPlayerCamera _camera;
        private ShotEffects _effects;
        private WorldVisuals _world;
        private readonly Audio.GameAudio _audio = new Audio.GameAudio();
        private readonly ViewModel _viewModel = new ViewModel();
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
                if (UI.VexaSettings.RecordDemos)
                {
                    try
                    {
                        string file = $"{DateTime.Now:yyyyMMdd-HHmm}_{setup.Map}_{setup.Mode.ToString().ToLowerInvariant()}{DemoFormat.Extension}";
                        s.HostServer.StartRecording(System.IO.Path.Combine(DemoFolder, file));
                    }
                    catch (Exception e) { Debug.LogWarning("[demo] recording failed: " + e.Message); }
                }
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

        /// <summary>Plays a .vxdemo file: a normal client fed from the file instead of the network.</summary>
        public static GameSession PlayDemo(string path)
        {
            var s = Create();
            try
            {
                var file = DemoFile.Read(path);
                s.DemoPath = path;
                s.MapName = file.Map;
                s.Status = "Demo yükleniyor...";
                s.StartDemoClient(file);
            }
            catch (Exception e) { s.Status = "Demo açılamadı: " + e.Message; s.Failed = true; Debug.LogException(e); }
            return s;
        }

        void StartDemoClient(DemoFile file)
        {
            Demo = new DemoPlayback(file);
            Client = new ClientGame(Demo, "izleyici", MapLoader.Load);
            Client.ShotFired += OnShot;
            Client.Hit += OnHit;
            Spectate.Reset();
            _audio.Bind(this);
        }

        /// <summary>Jump to a demo time (server seconds). Going backwards replays from the start.</summary>
        public void SeekDemo(double time)
        {
            if (Demo == null) return;
            time = Math.Max(Demo.File.StartTime, Math.Min(time, Demo.File.StartTime + Demo.File.Duration));
            Seeking = true;
            try
            {
                if (time < Demo.Time)
                {
                    int watched = Spectate.Target;
                    StartDemoClient(Demo.File);
                    Spectate.Prefer(watched);
                    _world.Clear();
                    _effects.Clear();
                    ClientReplaced?.Invoke();
                }
                // coarse steps: only the end state matters while seeking
                const double step = 0.1;
                Client.Update(0, () => default);
                while (Demo.Time + step < time)
                {
                    Demo.Advance(step);
                    Client.Update(step, () => default);
                }
                Demo.Advance(Math.Max(0, time - Demo.Time));
                Client.Update(1.0 / 64, () => default);
            }
            finally { Seeking = false; }
        }

        public void SeekRound(int dir)
        {
            if (Demo == null) return;
            var rounds = Demo.File.Rounds;
            if (rounds.Count == 0) return;
            double now = Demo.Time;
            double target = -1;
            if (dir > 0) { foreach (var r in rounds) if (r.time > now + 0.5) { target = r.time; break; } }
            else { for (int i = rounds.Count - 1; i >= 0; i--) if (rounds[i].time < now - 3) { target = rounds[i].time; break; } if (target < 0) target = Demo.File.StartTime; }
            if (target >= 0) SeekDemo(target);
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
            s.Input.Touch.ApplySaved(UI.VexaSettings.TouchLayout);
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
            _audio.Bind(this);
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

            if (IsDemo)
            {
                if (!DemoPaused)
                {
                    float d = dt * DemoSpeed;
                    Demo.Advance(d);
                    Client.Update(d, () => default);
                }
                Input.Enabled = false;
                Input.UpdateFrame();
            }
            else
            {
                Input.Enabled = !InputBlocked;
            if (Client.Welcomed)
            {
                var def = Client.Predicted.ActiveDef;
                Input.ZoomSensitivityScale = Client.Predicted.Zoom > 0 && def.ZoomFov != null ? def.ZoomFov[Client.Predicted.Zoom - 1] / 90f * UI.VexaSettings.ZoomSensitivity : 1f;
            }
                Input.UpdateFrame();
                Client.Update(dt, Input.SampleCommand);
            }

            if (!IsDemo && Client.Disconnected)
            {
                Status = Client.Welcomed ? "Sunucu bağlantısı koptu." : "Sunucuya bağlanılamadı.";
                Failed = true;
                return;
            }
            if (!Client.Welcomed)
            {
                if (!IsDemo && Time.unscaledTime - _connectStarted > 10f) { Status = "Bağlantı zaman aşımı. Sunucu adresini kontrol et."; Failed = true; }
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
            UpdateSpectating();
            SyncAvatars();
            _camera.Update(this, dt);
            _viewModel.Update(this, Camera.main, dt);
            _effects.Update(dt);
            _world.Update(Client, dt);
            _audio.Update(IsDemo ? (DemoPaused ? 0f : dt * DemoSpeed) : dt);
        }

        void UpdateSpectating()
        {
            bool canWatch = !Client.Predicted.Alive || Client.LocalTeam == Team.None;
            Spectating = canWatch && Spectate.Update(Client, Time.unscaledTime);
            if (!Spectating || InputBlocked || IsDemo) return;
            // dead in a match: mouse buttons cycle players, jump switches first/third person (like CS)
            if (PcInput.Down(InputAction.Attack)) Spectate.Next(1);
            else if (PcInput.Down(InputAction.Attack2)) Spectate.Next(-1);
            if (PcInput.Down(InputAction.Jump)) Spectate.ToggleMode();
        }

        void SyncAvatars()
        {
            var seen = new HashSet<int>();
            foreach (var r in Client.Remotes)
            {
                seen.Add(r.Id);
                if (!Client.TryGetRemotePose(r.Id, out var pose)) continue;
                if (!_avatars.TryGetValue(r.Id, out var av)) { av = PlayerAvatar.Create(r.Name, pose.Team); _avatars[r.Id] = av; }
                // like CS: names over teammates only (spectators see everyone's)
                bool showName = Client.LocalTeam == Team.None || pose.Team == Client.LocalTeam;
                av.Apply(pose, r.Name, showName);
                // first-person spectating: don't render the body we're looking out of
                bool hide = Spectating && Spectate.Mode == Spectator.ViewMode.InEye && Spectate.Target == r.Id && pose.Alive;
                if (av.gameObject.activeSelf == hide) av.gameObject.SetActive(!hide);
            }
            var gone = new List<int>();
            foreach (var kv in _avatars) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { Destroy(_avatars[id].gameObject); _avatars.Remove(id); }
        }

        void OnShot(ShotInfo shot, bool local)
        {
            if (Client?.World == null || Seeking) return;
            Vector3 muzzle;
            if (local)
            {
                _viewModel.OnLocalShot(Weapons.Get(shot.Weapon));
                // tracers start at the first-person weapon's muzzle (or near the eye without models)
                if (!_viewModel.TryGetMuzzle(Camera.main, out muzzle))
                    muzzle = shot.Origin.ToU() + UnityBridge.ViewRotation(Input.Yaw, Input.Pitch) * new Vector3(0.12f, -0.1f, 0.4f);
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
            if (Seeking) return;
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
            _audio.Clear();
            _viewModel.Clear();
            HostServer?.StopRecording();
            _clientNet?.Dispose(); _clientNet = null;
            _serverNet?.Dispose(); _serverNet = null;
            Client = null; HostServer = null;
            if (Current == this) Current = null;
            Input.SetCursorLock(false);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            HostServer?.StopRecording(); // flush the demo if the app quits mid-match
            _clientNet?.Dispose();
            _serverNet?.Dispose();
            if (Current == this) Current = null;
        }
    }
}
