using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;
using NVec3 = System.Numerics.Vector3;

namespace Vexa.Client.Audio
{
    /// <summary>
    /// Game sounds: 3D positional gunshots, footsteps (by surface), reloads, grenades, the bomb's beeping
    /// (faster as it runs out), hit/kill feedback and round stingers. Footsteps follow CS rules: running is
    /// loud, walking (Shift) and crouching are silent.
    /// </summary>
    public sealed class GameAudio
    {
        const int PoolSize = 40;
        const float FootstepSpeed = 140f * VMath.HU;    // below this (walk / crouch) steps are silent
        const float StrideMeters = 2.2f;

        sealed class Mover { public float Stride; public bool OnGround = true; public bool Reloading; public int Alternate; }

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;
        private GameObject _root;
        private ClientGame _c;
        private GameSession _s;
        private readonly Dictionary<int, Mover> _movers = new Dictionary<int, Mover>();
        private readonly Mover _local = new Mover();
        private readonly Dictionary<int, AudioSource> _fireLoops = new Dictionary<int, AudioSource>();
        private readonly HashSet<int> _seenFires = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private float _nextBeep;
        private bool _wasFlashed, _wasPlanting;

        static AudioSource _ui;

        /// <summary>2D sounds for the menus (works without a game session).</summary>
        public static void PlayUi(string clip, float volume = 0.6f)
        {
            if (_ui == null)
            {
                var go = new GameObject("UiAudio");
                Object.DontDestroyOnLoad(go);
                _ui = go.AddComponent<AudioSource>();
                _ui.spatialBlend = 0; _ui.playOnAwake = false;
            }
            var c = SoundSynth.Get(clip);
            if (c != null) _ui.PlayOneShot(c, volume);
        }

        public void Bind(GameSession s)
        {
            if (_c != null)
            {
                _c.ShotFired -= OnShot; _c.Hit -= OnHit; _c.Killed -= OnKill; _c.GameEventReceived -= OnEvent;
                _c.RoundEnded -= OnRoundEnd; _c.LocalThrow -= OnThrow; _c.ChatReceived -= OnChat;
            }
            _s = s; _c = s.Client;
            _movers.Clear();
            if (_c == null) return;
            _c.ShotFired += OnShot; _c.Hit += OnHit; _c.Killed += OnKill; _c.GameEventReceived += OnEvent;
            _c.RoundEnded += OnRoundEnd; _c.LocalThrow += OnThrow; _c.ChatReceived += OnChat;
            if (_root == null) _root = new GameObject("GameAudio");
        }

        bool Quiet => _s == null || _s.Seeking;

        // ---------------- playback ----------------

        AudioSource Source()
        {
            if (_pool.Count < PoolSize)
            {
                var go = new GameObject("Snd");
                go.transform.SetParent(_root.transform, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.dopplerLevel = 0; a.rolloffMode = AudioRolloffMode.Linear;
                _pool.Add(a);
                return a;
            }
            // reuse the oldest voice
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            return src;
        }

        public void Play3D(string clip, Vector3 pos, float volume = 1f, float maxDistance = 60f, float pitchJitter = 0.04f)
        {
            if (Quiet) return;
            var c = SoundSynth.Get(clip);
            if (c == null) return;
            var a = Source();
            a.transform.position = pos;
            a.spatialBlend = 1f;
            a.minDistance = 1.5f; a.maxDistance = maxDistance;
            a.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            a.volume = volume;
            a.clip = c;
            a.loop = false;
            a.Play();
        }

        public void Play2D(string clip, float volume = 1f, float pitch = 1f)
        {
            if (Quiet) return;
            var c = SoundSynth.Get(clip);
            if (c == null) return;
            var a = Source();
            a.spatialBlend = 0f;
            a.pitch = pitch;
            a.volume = volume;
            a.clip = c;
            a.loop = false;
            a.Play();
        }

        // ---------------- events ----------------

        static string GunClip(WeaponDef def, bool silenced)
        {
            if (def == null) return "gun_rifle";
            if (silenced) return "gun_silenced";
            switch (def.Category)
            {
                case WeaponCategory.Pistol: return "gun_pistol";
                case WeaponCategory.Smg: return "gun_smg";
                case WeaponCategory.Shotgun: return "gun_shotgun";
                case WeaponCategory.MachineGun: return "gun_heavy";
                case WeaponCategory.Sniper: return "gun_sniper";
                case WeaponCategory.Melee: return "knife";
                default: return "gun_rifle";
            }
        }

        void OnShot(ShotInfo shot, bool local)
        {
            var def = Weapons.Get(shot.Weapon);
            bool silenced = (shot.Flags & ShotFlags.Silenced) != 0;
            string clip = (shot.Flags & ShotFlags.Melee) != 0 ? "knife" : GunClip(def, silenced);
            // heavier pistols and the scout get their own pitch so they read differently
            float pitch = def == null ? 1f : def.Id == WeaponId.Deagle || def.Id == WeaponId.R8 ? 0.8f : def.Id == WeaponId.Ssg08 ? 1.15f : 1f;
            if (local) Play2D(clip, 0.9f, pitch);
            else
            {
                Play3D(clip, shot.Origin.ToU(), 1f, silenced ? 25f : (def != null && def.Category == WeaponCategory.Sniper ? 140f : 90f), 0.03f);
                if (_pool.Count > 0) _pool[(_next + _pool.Count - 1) % _pool.Count].pitch *= pitch;
            }
        }

        void OnHit(HitEvent h)
        {
            if (h.Attacker == _c.LocalId && h.Victim != _c.LocalId)
                Play2D(h.Group == HitGroup.Head ? "hit_head" : "hit_body", h.Group == HitGroup.Head ? 0.8f : 0.5f);
            else if (h.Victim == _c.LocalId) Play2D("hurt", 0.8f);
            else Play3D(h.Group == HitGroup.Head ? "hit_head" : "hit_body", h.Point.ToU(), 0.6f, 25f);
        }

        void OnKill(KillEvent k)
        {
            if (k.Killer == _c.LocalId && k.Victim != _c.LocalId) Play2D("kill", 0.55f);
        }

        void OnThrow(GrenadeThrow t) => Play2D("pin", 0.6f);

        void OnChat(ChatMessage m) => Play2D("chat", 0.35f);

        void OnRoundEnd(ClientGame.RoundEndInfo info)
        {
            var mine = _c.LocalTeam;
            if (mine == Team.None) return;
            Play2D(info.Winner == mine ? "round_win" : "round_lose", 0.45f);
        }

        void OnEvent(GameEvent e)
        {
            var pos = e.Position.ToU();
            switch (e.Type)
            {
                case GameEventType.Detonation:
                    switch ((GrenadeType)e.A)
                    {
                        case GrenadeType.HE: Play3D("explosion", pos, 1f, 120f, 0.05f); break;
                        case GrenadeType.Flash: Play3D("flashbang", pos, 1f, 100f, 0.03f); break;
                        case GrenadeType.Smoke: Play3D("smoke", pos, 0.8f, 40f); break;
                        case GrenadeType.Molotov: case GrenadeType.Incendiary: Play3D("molotov", pos, 0.9f, 50f); break;
                        case GrenadeType.Decoy: Play3D("nade_bounce", pos, 0.5f, 30f); break;
                    }
                    break;
                case GameEventType.BombPlanted: Play3D("bomb_planted", pos, 1f, 60f, 0); break;
                case GameEventType.BombDefused: Play3D("bomb_defused", pos, 1f, 60f, 0); break;
                case GameEventType.BombExploded: Play3D("c4_explosion", pos, 1f, 250f, 0); break;
                case GameEventType.RoundStart: Play2D("round_start", 0.35f); break;
                case GameEventType.Purchase: Play2D("buy", 0.5f); break;
                case GameEventType.PurchaseDenied: Play2D("denied", 0.5f); break;
            }
        }

        // ---------------- per frame ----------------

        public void Update(float dt)
        {
            if (_c == null || !_c.Welcomed || Quiet) return;
            // remote footsteps / landings / reloads
            foreach (var r in _c.Remotes)
            {
                if (!_c.TryGetRemotePose(r.Id, out var pose) || !pose.Alive) continue;
                if (!_movers.TryGetValue(r.Id, out var m)) _movers[r.Id] = m = new Mover();
                Steps(m, pose.Position, pose.Velocity, pose.OnGround, false, dt);
                if (pose.Reloading && !m.Reloading) Play3D("reload", pose.Position.ToU() + Vector3.up, 0.6f, 20f);
                m.Reloading = pose.Reloading;
            }
            // local player
            var st = _c.Predicted;
            if (st.Alive)
            {
                Steps(_local, st.Position, st.Velocity, st.OnGround, true, dt);
                if (st.Reloading && !_local.Reloading) Play2D("reload", 0.6f);
                _local.Reloading = st.Reloading;
                if (st.Planting && !_wasPlanting) Play2D("bomb_press", 0.6f);
                _wasPlanting = st.Planting;
                bool flashed = _c.PlayerTime < st.FlashFullEndTime;
                if (flashed && !_wasFlashed) Play2D("flash_ring", Mathf.Clamp01((st.FlashEndTime - _c.PlayerTime) / 3f) * 0.5f);
                _wasFlashed = flashed;
            }

            // the bomb beeps faster as it runs out (~1 s -> ~0.12 s)
            if (_c.Bomb.State == BombState.Planted)
            {
                float left = _c.BombTimeLeft;
                float period = Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(left / 40f));
                if (Time.time >= _nextBeep && left > 0.05f)
                {
                    Play3D("bomb_beep", _c.Bomb.Position.ToU() + Vector3.up * 0.1f, 1f, 45f, 0);
                    _nextBeep = Time.time + period;
                }
            }

            // fires: looping crackle while they burn
            _seenFires.Clear();
            foreach (var f in _c.Fires)
            {
                _seenFires.Add(f.Id);
                if (_fireLoops.ContainsKey(f.Id)) continue;
                var a = Source();
                a.transform.position = f.Center.ToU();
                a.spatialBlend = 1; a.minDistance = 2; a.maxDistance = 30; a.pitch = 1; a.volume = 0.7f;
                a.clip = SoundSynth.Get("fire_loop"); a.loop = true; a.Play();
                _fireLoops[f.Id] = a;
            }
            _gone.Clear();
            foreach (var kv in _fireLoops) if (!_seenFires.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone) { var a = _fireLoops[id]; if (a != null) { a.loop = false; a.Stop(); } _fireLoops.Remove(id); }
        }

        void Steps(Mover m, NVec3 pos, NVec3 vel, bool onGround, bool local, float dt)
        {
            float speed = Mathf.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
            if (onGround && !m.OnGround) Play(local, "land", pos, 0.8f, 30f);
            m.OnGround = onGround;
            if (!onGround || speed < FootstepSpeed) { m.Stride = Mathf.Min(m.Stride, StrideMeters * 0.5f); return; }
            m.Stride += speed * dt;
            if (m.Stride < StrideMeters) return;
            m.Stride = 0;
            m.Alternate ^= 1;
            Play(local, StepClip(pos) + (m.Alternate == 1 ? "2" : ""), pos, local ? 0.35f : 0.9f, 28f);
        }

        void Play(bool local, string clip, NVec3 pos, float volume, float range)
        {
            if (local) Play2D(clip, volume, 1f + Random.Range(-0.05f, 0.05f));
            else Play3D(clip, pos.ToU(), volume, range, 0.06f);
        }

        string StepClip(NVec3 pos)
        {
            var w = _c.World;
            if (w != null && w.Raycast(pos + new NVec3(0, 0.2f, 0), new NVec3(0, -1, 0), 0.6f, out var hit))
            {
                switch (hit.Material)
                {
                    case SurfaceMaterial.Metal: return "step_metal";
                    case SurfaceMaterial.Wood: return "step_wood";
                    case SurfaceMaterial.Sand: return "step_sand";
                }
            }
            return "step_concrete";
        }

        public void Clear()
        {
            foreach (var a in _fireLoops.Values) if (a != null) a.Stop();
            _fireLoops.Clear();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _pool.Clear();
            _movers.Clear();
        }
    }
}
