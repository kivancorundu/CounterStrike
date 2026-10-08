using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>
    /// Owns the UI Toolkit document (PanelSettings created at runtime, no assets needed) and decides
    /// which screen is visible: main menu, loading, HUD with its overlays (buy, scoreboard, pause, match end).
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        enum Screen { Menu, Loading, Game }

        private UIDocument _doc;
        private VisualElement _root;
        private MainMenuView _menu;
        private LoadingView _loading;
        private HudView _hud;
        private BuyMenuView _buy;
        private ScoreboardView _score;
        private PauseView _pause;
        private MatchEndView _matchEnd;
        private MenuBackdrop _backdrop;
        private DemoControlsView _demo;
        private Vexa.Core.Client.ClientGame _boundClient;
        private int _chatClosedFrame = -10;
        private Screen _screen = (Screen)(-1);
        private GameSession _session;
        private bool _buyOpen, _pauseOpen, _scoreToggled, _matchEndShown;

        public static UiRoot Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = Application.isMobilePlatform ? 1f : 0.5f;
            ps.sortingOrder = 100;
            // Unity's default runtime theme, if the project has one in Resources; otherwise our widgets are fully self-styled
            var theme = Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme");
            ps.themeStyleSheet = theme != null ? theme : ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = ps;
            _root = _doc.rootVisualElement;
            _root.style.unityFontDefinition = FontDefinition.FromFont(Fonts.Body);
            _root.style.color = Theme.Text;
            _root.style.fontSize = 16;

            _hud = new HudView();
            _buy = new BuyMenuView();
            _score = new ScoreboardView();
            _pause = new PauseView();
            _matchEnd = new MatchEndView();
            _loading = new LoadingView();
            _menu = new MainMenuView();
            _demo = new DemoControlsView();
            foreach (var v in new VisualElement[] { _hud, _demo, _score, _buy, _pause, _matchEnd, _loading, _menu })
            {
                _root.Add(v);
                v.Show(false);
            }

            _pause.Resume += () => _pauseOpen = false;
            _pause.Leave += LeaveMatch;
            _pause.PickTeam = t => { _session?.Client?.SelectTeam(t); _pauseOpen = false; };
            _pause.Command = cmd => { _session?.Client?.SendChat(cmd, false); _pauseOpen = false; };
            _demo.Exit += LeaveMatch;
            _matchEnd.Leave += LeaveMatch;
            _buy.Close += () => _buyOpen = false;
            _loading.Cancel += LeaveMatch;
            _hud.MobilePause = () => _pauseOpen = true;
            _hud.MobileBuy = () => ToggleBuy();
            _hud.MobileScore = () => _scoreToggled = !_scoreToggled;
            _backdrop = new MenuBackdrop();
            UiSound.Clicked += () => Audio.GameAudio.PlayUi("ui_click", 0.5f);
            UiSound.Hovered += () => Audio.GameAudio.PlayUi("ui_hover", 0.18f);
        }

        void LeaveMatch()
        {
            GameSession.Current?.Shutdown();
            _pauseOpen = _buyOpen = false;
        }

        void OnApplicationQuit() => VexaSettings.Flush();
        void OnApplicationPause(bool paused) { if (paused) VexaSettings.Flush(); }

        void Update()
        {
            VexaSettings.FlushIfDue();
            var s = GameSession.Current;
            if (s != null && s.Failed)
            {
                _menu.ShowError(s.Status);
                s.Shutdown();
                s = null;
            }
            Screen want = s == null ? Screen.Menu : s.Ready ? Screen.Game : Screen.Loading;
            if (want != _screen || s != _session) Enter(want, s);

            switch (_screen)
            {
                case Screen.Menu:
                    _backdrop.Tick(Time.unscaledDeltaTime);
                    GameSession.InputBlocked = true;
                    UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
                    break;
                case Screen.Loading:
                    _loading.Tick(s);
                    GameSession.InputBlocked = true;
                    break;
                case Screen.Game:
                    TickGame(s);
                    break;
            }
        }

        void Enter(Screen screen, GameSession s)
        {
            _screen = screen;
            _session = s;
            _menu.Show(screen == Screen.Menu);
            _loading.Show(screen == Screen.Loading);
            _hud.Show(screen == Screen.Game);
            _demo.Show(screen == Screen.Game && s != null && s.IsDemo);
            _buy.Show(false); _score.Show(false); _pause.Show(false); _matchEnd.Show(false);
            _buyOpen = _pauseOpen = _scoreToggled = _matchEndShown = false;
            if (screen == Screen.Menu) { _backdrop.Enable(); _menu.ShowPage(0); }
            else _backdrop.Disable();
            if (screen == Screen.Loading) { _menu.ShowError(""); _loading.Open(s); }
            if (screen == Screen.Game) BindClient(s);
        }

        void BindClient(GameSession s)
        {
            _boundClient = s.Client;
            _hud.Bind(s);
            _buy.Bind(s.Client);
            _score.Bind(s.Client);
            if (s.IsDemo) _demo.Bind(s);
        }

        void ToggleBuy()
        {
            var c = _session?.Client;
            if (c == null) return;
            if (_buyOpen) { _buyOpen = false; return; }
            if (!c.Predicted.Alive) return;
            if (!c.CanBuyNow)
            {
                bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
                _hud.Toast(rounds && !c.InBuyZone ? "Satın alma bölgesinde değilsin" : "Satın alma süresi doldu");
                return;
            }
            _buyOpen = true;
            _buy.ResetKeys();
        }

        void TickGame(GameSession s)
        {
            if (s.Client != _boundClient) BindClient(s); // demo seeking rebuilds the client
            var c = s.Client;
            bool matchOver = c.Header.Phase == GamePhase.MatchOver;
            bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
            bool chat = _hud.ChatOpen;
            if (chat) _chatClosedFrame = Time.frameCount;
            bool typingJustEnded = Time.frameCount - _chatClosedFrame <= 1;

            // ---- keys (none while typing in chat) ----
            if (!chat && !typingJustEnded)
            {
                if (PcInput.KeyDown(KeyCode.Escape))
                {
                    if (_pauseOpen && _pause.SettingsOpen) _pause.ShowSettings(false);
                    else if (_buyOpen) _buyOpen = false;
                    else if (!matchOver) { _pauseOpen = !_pauseOpen; if (_pauseOpen) _pause.Open(c, s); }
                }
                bool free = !_pauseOpen && !matchOver;
                if (!s.IsDemo)
                {
                    if (free && PcInput.Down(InputAction.Buy)) ToggleBuy();
                    if (_buyOpen)
                    {
                        for (int d = 1; d <= 9; d++) if (PcInput.KeyDown(KeyCode.Alpha0 + d)) _buy.Digit(d);
                        if (!c.CanBuyNow) _buyOpen = false;
                    }
                    if (free && !_buyOpen && PcInput.Down(InputAction.TeamMenu) && rounds) { _pauseOpen = true; _pause.Open(c, s); }
                    if (free && !_buyOpen && PcInput.Down(InputAction.ChatAll)) _hud.OpenChat(false);
                    else if (free && !_buyOpen && PcInput.Down(InputAction.ChatTeam) && c.LocalTeam != Team.None) _hud.OpenChat(true);
                }
                else if (!_pauseOpen) _demo.Tick();
            }
            if (_pauseOpen && !_pause.Visible()) _pause.Open(c, s);

            if (matchOver && !_matchEndShown && !s.IsDemo) { _matchEndShown = true; _matchEnd.Open(c); _buyOpen = _pauseOpen = false; _hud.CloseChat(); }

            bool showScore = !_pauseOpen && !_buyOpen && !chat && (PcInput.Held(InputAction.Scoreboard) || _scoreToggled);

            // ---- visibility ----
            _buy.Show(_buyOpen);
            _pause.Show(_pauseOpen);
            _score.Show(showScore && !_matchEndShown);
            _matchEnd.Show(_matchEndShown);
            _demo.Show(s.IsDemo && !_pauseOpen);
            if (_buyOpen) { _buy.Money = _hud.LocalMoney; _buy.Tick(); }
            if (showScore) _score.Tick();

            _hud.Tick(Time.unscaledDeltaTime);

            bool blocked = _buyOpen || _pauseOpen || _matchEndShown || _hud.ChatOpen;
            GameSession.InputBlocked = blocked;
            // demos keep the mouse free for the playback bar
            s.Input.SetCursorLock(!blocked && !s.IsDemo);
        }

        void OnDestroy()
        {
            _backdrop?.Disable();
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>Slow camera orbit over a map behind the main menu.</summary>
    public sealed class MenuBackdrop
    {
        private GameObject _map;
        private Vector3 _center;
        private float _radius = 40f, _angle = 30f;
        private bool _enabled;

        public void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            try
            {
                var data = MapLoader.Load("kasaba");
                _map = MapBuilder.Build(data);
                var b = new Bounds();
                bool first = true;
                foreach (var box in data.Boxes)
                {
                    var bb = new Bounds((box.Min.ToU() + box.Max.ToU()) * 0.5f, box.Max.ToU() - box.Min.ToU());
                    if (first) { b = bb; first = false; } else b.Encapsulate(bb);
                }
                _center = b.center;
                _radius = Mathf.Max(b.extents.x, b.extents.z) * 0.75f;
            }
            catch (System.Exception e) { Debug.LogWarning("[ui] menu backdrop unavailable: " + e.Message); }
        }

        public void Disable()
        {
            _enabled = false;
            if (_map != null) Object.Destroy(_map);
            _map = null;
        }

        public void Tick(float dt)
        {
            var cam = Camera.main;
            if (cam == null || _map == null) return;
            _angle += dt * 2.5f;
            float a = _angle * Mathf.Deg2Rad;
            var pos = _center + new Vector3(Mathf.Cos(a) * _radius, _radius * 0.45f + 6f, Mathf.Sin(a) * _radius);
            cam.transform.position = pos;
            cam.transform.LookAt(_center + Vector3.up * 2f);
            cam.fieldOfView = 50f;
        }
    }
}
