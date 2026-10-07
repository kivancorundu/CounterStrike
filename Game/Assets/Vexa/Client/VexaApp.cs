using UnityEngine;
using Vexa.Core;

namespace Vexa.Client
{
    /// <summary>
    /// Entry point. Created automatically when any scene starts, so the project runs without
    /// hand-made scenes. Provides a temporary developer menu and HUD (OnGUI) until the real UI is built.
    /// </summary>
    public sealed class VexaApp : MonoBehaviour
    {
        public static bool MenuOpen { get; private set; } = true;
        static VexaApp _instance;

        string _name, _host = "127.0.0.1", _port = "27015", _sens;
        int _tick = 64, _bots = 5;
        static bool _buyOpen;
        public static bool BuyOpen => _buyOpen;
        GUIStyle _big, _mid, _small, _center;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("VexaApp");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VexaApp>();
        }

        void Awake()
        {
            _name = PlayerPrefs.GetString("vexa.name", "Oyuncu");
            _sens = PlayerPrefs.GetFloat("vexa.sens", 2f).ToString("0.00");
            PlatformProfile.Apply();
            if (Camera.main == null)
            {
                var go = new GameObject("Main Camera"); go.tag = "MainCamera";
                go.AddComponent<Camera>(); go.AddComponent<AudioListener>();
                go.transform.position = new Vector3(0, 12, -22); go.transform.rotation = Quaternion.Euler(28, 0, 0);
            }
            if (FindLight() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.2f; l.shadows = LightShadows.Soft;
                l.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
        }

        static Light FindLight()
        {
            foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) return l;
            return null;
        }

        void Update()
        {
            var s = GameSession.Current;
            if (PcInput.KeyDown(KeyCode.Escape))
            {
                if (_buyOpen) _buyOpen = false;
                else if (s != null && s.Client != null && s.Client.Welcomed) MenuOpen = !MenuOpen;
            }
            if (s != null && s.Client != null && s.Client.Welcomed && !MenuOpen && PcInput.KeyDown(KeyCode.B)) _buyOpen = !_buyOpen;
            bool playing = s != null && s.Client != null && s.Client.Welcomed && !MenuOpen && !_buyOpen;
            s?.Input.SetCursorLock(playing);
        }

        void Styles()
        {
            if (_big != null) return;
            float k = Screen.height / 720f;
            _big = new GUIStyle(GUI.skin.label) { fontSize = (int)(48 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = (int)(26 * k), fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = (int)(15 * k) };
            _center = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
            GUI.skin.button.fontSize = (int)(18 * k);
            GUI.skin.textField.fontSize = (int)(18 * k);
        }

        void OnGUI()
        {
            Styles();
            var s = GameSession.Current;
            bool inGame = s != null && s.Client != null && s.Client.Welcomed;
            if (inGame) DrawHud(s);
            if (MenuOpen || !inGame) DrawMenu(s, inGame);
            else if (_buyOpen) DrawBuy(s);
        }

        // ---------------- menu ----------------
        void DrawMenu(GameSession s, bool inGame)
        {
            float w = Mathf.Min(560, Screen.width - 40), h = Screen.height;
            var r = new Rect((Screen.width - w) / 2, h * 0.08f, w, h * 0.84f);
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 10, r.width - 40, r.height - 20));
            GUILayout.Label("VEXA", _big);
            GUILayout.Label("Geliştirici menüsü · prototip", _small);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(); GUILayout.Label("İsim", _small, GUILayout.Width(110)); _name = GUILayout.TextField(_name, 16); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Hassasiyet", _small, GUILayout.Width(110)); _sens = GUILayout.TextField(_sens, 5); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Tick", _small, GUILayout.Width(110));
            if (GUILayout.Toggle(_tick == 64, "64")) _tick = 64;
            if (GUILayout.Toggle(_tick == 128, "128")) _tick = 128;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Bot sayısı: " + _bots, _small, GUILayout.Width(110)); _bots = (int)GUILayout.HorizontalSlider(_bots, 0, 9); GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (inGame && GUILayout.Button("Oyuna dön", GUILayout.Height(44))) MenuOpen = false;
            if (GUILayout.Button("Antrenman / Sunucu kur (bu bilgisayarda)", GUILayout.Height(44)))
            {
                Save();
                GameSession.Host(int.Parse(_port), _tick, _bots, _name);
                MenuOpen = false;
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Adres", _small, GUILayout.Width(60)); _host = GUILayout.TextField(_host);
            GUILayout.Label("Port", _small, GUILayout.Width(40)); _port = GUILayout.TextField(_port, 5, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Sunucuya bağlan", GUILayout.Height(44)))
            {
                Save();
                GameSession.Join(_host, int.Parse(_port), _name);
                MenuOpen = false;
            }
            if (inGame && GUILayout.Button("Oturumu kapat", GUILayout.Height(36))) { s.Shutdown(); MenuOpen = true; }
            if (s != null && !string.IsNullOrEmpty(s.Status)) GUILayout.Label(s.Status, _small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("PC: WASD · Fare · Shift yürü · Ctrl eğil · R doldur · 1/2/3 silah · Q son silah · B satın al · Esc menü", _small);
            GUILayout.EndArea();
        }

        void Save()
        {
            PlayerPrefs.SetString("vexa.name", _name);
            if (float.TryParse(_sens, out float sv)) { PlayerPrefs.SetFloat("vexa.sens", sv); }
            PlayerPrefs.Save();
        }

        // ---------------- buy (dev) ----------------
        static readonly WeaponId[] BuyList =
        {
            WeaponId.Ak47, WeaponId.M4a4, WeaponId.M4a1s, WeaponId.Awp, WeaponId.Ssg08, WeaponId.Galil, WeaponId.Famas, WeaponId.Sg553, WeaponId.Aug,
            WeaponId.Mp9, WeaponId.Mac10, WeaponId.Mp7, WeaponId.Ump45, WeaponId.P90, WeaponId.Nova, WeaponId.Xm1014, WeaponId.Negev,
            WeaponId.Deagle, WeaponId.P250, WeaponId.FiveSeven, WeaponId.Tec9, WeaponId.Usp, WeaponId.Glock, WeaponId.Cz75, WeaponId.Taser,
        };

        void DrawBuy(GameSession s)
        {
            var r = new Rect(Screen.width * 0.1f, Screen.height * 0.1f, Screen.width * 0.8f, Screen.height * 0.8f);
            GUI.Box(r, "SATIN AL (prototip: ücretsiz) — Esc/B kapat");
            int cols = 5;
            float bw = (r.width - 40) / cols, bh = Mathf.Max(40, (r.height - 60) / 6f);
            for (int i = 0; i < BuyList.Length; i++)
            {
                var d = Weapons.Get(BuyList[i]);
                var br = new Rect(r.x + 20 + (i % cols) * bw, r.y + 40 + (i / cols) * bh, bw - 6, bh - 6);
                if (GUI.Button(br, $"{d.Name}\n${d.Price}")) { s.Client.RequestBuy(d.Id); _buyOpen = false; }
            }
        }

        // ---------------- HUD (temporary) ----------------
        void DrawHud(GameSession s)
        {
            var c = s.Client;
            var st = c.Predicted;
            float sw = Screen.width, sh = Screen.height;
            // crosshair
            if (st.Alive && !(st.Zoom > 0 && st.ActiveDef.Category == WeaponCategory.Sniper))
            {
                var col = Time.unscaledTime < s.HitMarkerUntil ? Color.red : new Color(0.3f, 1f, 0.3f);
                float g = 4, l = 6, t = 2;
                Fill(new Rect(sw / 2 + g, sh / 2 - t / 2, l, t), col); Fill(new Rect(sw / 2 - g - l, sh / 2 - t / 2, l, t), col);
                Fill(new Rect(sw / 2 - t / 2, sh / 2 + g, t, l), col); Fill(new Rect(sw / 2 - t / 2, sh / 2 - g - l, t, l), col);
            }
            if (st.Alive && st.Zoom > 0 && st.ActiveDef.Category == WeaponCategory.Sniper)
            {
                Fill(new Rect(0, sh / 2, sw, 1), Color.black); Fill(new Rect(sw / 2, 0, 1, sh), Color.black);
            }
            if (Time.unscaledTime < s.DamageFlashUntil) Fill(new Rect(0, 0, sw, sh), new Color(0.8f, 0, 0, 0.18f));
            // vitals
            GUI.Label(new Rect(20, sh - 70, 400, 60), $"✚ {Mathf.Max(0, (int)st.Health)}    ⛨ {st.Armor}", _mid);
            var slot = st.ActiveSlot;
            var def = st.ActiveDef;
            string ammo = def.IsGun ? $"{slot.Clip} / {slot.Reserve}" : "";
            GUI.Label(new Rect(sw - 320, sh - 90, 300, 30), def.Name + (st.Reloading ? "  (dolduruluyor)" : ""), _small);
            GUI.Label(new Rect(sw - 320, sh - 66, 300, 60), ammo, _mid);
            if (!st.Alive) GUI.Label(new Rect(0, sh * 0.3f, sw, 60), "ÖLDÜN — yeniden doğuluyor...", _center);
            // net stats
            GUI.Label(new Rect(10, 6, 700, 24), $"{(int)(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))} FPS · ping {c.PingMs} ms · {c.TickRate} tick · düzeltme {c.Mispredictions} · son {c.LastCorrection * 100:0.0} cm", _small);
            // kill feed
            for (int i = 0; i < s.KillFeed.Count; i++) GUI.Label(new Rect(sw - 520, 6 + i * 22, 510, 22), s.KillFeed[i], _small);
            // scoreboard (Tab)
            if (PcInput.KeyHeld(KeyCode.Tab) && s.HostServer != null)
            {
                var r = new Rect(sw * 0.25f, sh * 0.2f, sw * 0.5f, sh * 0.5f);
                GUI.Box(r, "SKOR");
                int i = 0;
                foreach (var p in s.HostServer.Players) GUI.Label(new Rect(r.x + 20, r.y + 30 + 24 * i++, r.width - 40, 24), $"{p.Name,-16}  Ö {p.Kills}   Ö(D) {p.Deaths}", _small);
            }
            if (s.Input.MobileControls) DrawTouch(s);
        }

        void DrawTouch(GameSession s)
        {
            var tc = s.Input.Touch;
            float sw = Screen.width, sh = Screen.height;
            foreach (var b in tc.Layout)
            {
                var r = new Rect(b.Norm.x * sw, b.Norm.y * sh, b.Norm.width * sw, b.Norm.height * sh);
                bool on = (tc.Buttons & b.Button) != 0 && b.Button != Buttons.None;
                Fill(r, on ? new Color(1, 1, 1, 0.35f) : new Color(0, 0, 0, 0.3f));
                GUI.Label(r, b.Label, _center);
            }
            if (tc.StickActive)
            {
                var c = new Vector2(tc.StickCenter.x, sh - tc.StickCenter.y);
                var p = new Vector2(tc.StickPos.x, sh - tc.StickPos.y);
                Fill(new Rect(c.x - 60, c.y - 60, 120, 120), new Color(1, 1, 1, 0.12f));
                Fill(new Rect(p.x - 25, p.y - 25, 50, 50), new Color(1, 1, 1, 0.4f));
            }
        }

        static Texture2D _white;
        static void Fill(Rect r, Color c)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old;
        }
    }

    /// <summary>Separate quality targets for the PC and mobile builds (same code base).</summary>
    public static class PlatformProfile
    {
        public static bool IsMobile => Application.isMobilePlatform;

        public static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            if (IsMobile)
            {
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                QualitySettings.shadowDistance = 25f;
            }
            else
            {
                Application.targetFrameRate = 0; // uncapped for competitive play
                QualitySettings.shadowDistance = 80f;
            }
        }
    }
}
