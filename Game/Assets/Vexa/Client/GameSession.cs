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
        public readonly List<string> KillFeed = new List<string>();
        public float HitMarkerUntil, DamageFlashUntil;

        private LiteNetTransport _clientNet, _serverNet;
        private GameObject _mapRoot;
        private readonly Dictionary<int, PlayerAvatar> _avatars = new Dictionary<int, PlayerAvatar>();
        private LocalPlayerCamera _camera;
        private ShotEffects _effects;
        private float _connectStarted;

        public static GameSession Host(int port, int tickRate, int bots, string playerName, string map = "training")
        {
            var s = Create();
            try
            {
                var mapData = MapLoader.Load(map);
                s._serverNet = LiteNetTransport.StartServer(port);
                s.HostServer = new ServerGame(s._serverNet, mapData, tickRate);
                s.HostServer.Log += m => Debug.Log("[server] " + m);
                for (int i = 0; i < bots; i++) s.HostServer.AddBot("Bot " + (i + 1));
                s.Connect("127.0.0.1", port, playerName);
            }
            catch (Exception e) { s.Status = "Sunucu başlatılamadı: " + e.Message; Debug.LogException(e); }
            return s;
        }

        public static GameSession Join(string host, int port, string playerName)
        {
            var s = Create();
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
            s.Input.Sensitivity = PlayerPrefs.GetFloat("vexa.sens", 2f);
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
            Client.Killed += OnKill;
        }

        void Awake()
        {
            _camera = new LocalPlayerCamera();
            _effects = new ShotEffects();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            HostServer?.Update(dt);
            if (Client == null) return;

            Input.Enabled = !VexaApp.MenuOpen && !VexaApp.BuyOpen;
            if (Client.Welcomed)
            {
                var def = Client.Predicted.ActiveDef;
                Input.ZoomSensitivityScale = Client.Predicted.Zoom > 0 && def.ZoomFov != null ? def.ZoomFov[Client.Predicted.Zoom - 1] / 90f : 1f;
            }
            Input.UpdateFrame();
            Client.Update(dt, Input.SampleCommand);

            if (!Client.Welcomed)
            {
                if (Time.unscaledTime - _connectStarted > 10f) Status = "Bağlantı zaman aşımı. Sunucu adresini kontrol et.";
                return;
            }
            Status = "";
            if (_mapRoot == null) _mapRoot = MapBuilder.Build(Client.Map);
            SyncAvatars();
            _camera.Update(this, dt);
            _effects.Update(dt);
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

        void OnHit(HitEvent h)
        {
            if (h.Attacker == Client.LocalId) HitMarkerUntil = Time.unscaledTime + 0.15f;
            if (h.Victim == Client.LocalId) DamageFlashUntil = Time.unscaledTime + 0.25f;
            _effects.Blood(h.Point.ToU(), h.Group == HitGroup.Head);
        }

        void OnKill(KillEvent k)
        {
            string N(int id) => id == Client.LocalId ? Client.PlayerName : (Client.GetRemote(id)?.Name ?? ("#" + id));
            var line = $"{N(k.Killer)}  [{Weapons.Get(k.Weapon)?.Name}{(k.Headshot ? " • KAFA" : "")}{(k.Wallbang ? " • DUVAR" : "")}]  {N(k.Victim)}";
            KillFeed.Add(line);
            if (KillFeed.Count > 6) KillFeed.RemoveAt(0);
        }

        public void Shutdown()
        {
            foreach (var a in _avatars.Values) if (a != null) Destroy(a.gameObject);
            _avatars.Clear();
            if (_mapRoot != null) Destroy(_mapRoot);
            _effects?.Clear();
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
