using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Vexa.Client
{
    /// <summary>
    /// Collects player input. View angles are updated every rendered frame (for responsive aim),
    /// buttons are sampled into a <see cref="PlayerInput"/> once per simulation tick.
    /// Edge-triggered actions (weapon selection) are latched between ticks so no key press is lost.
    /// PC: keyboard + mouse (CS-style sensitivity, m_yaw 0.022). Mobile: virtual joystick + look drag + on-screen buttons.
    /// </summary>
    public sealed class InputSampler
    {
        public float Yaw, Pitch;
        public float Sensitivity = 2.0f;           // CS sensitivity units
        public float TouchSensitivity = 0.18f;     // degrees per pixel
        public bool MobileControls;
        public bool Enabled = true;
        public float ZoomSensitivityScale = 1f;

        private WeaponSelect _pendingSelect;
        private Buttons _latched;                  // pressed at least once since last tick
        private readonly TouchControls _touch = new TouchControls();

        public TouchControls Touch => _touch;

        public void SetCursorLock(bool locked)
        {
            if (MobileControls) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Call every frame.</summary>
        public void UpdateFrame()
        {
            if (!Enabled) { _touch.Reset(); return; }
            if (MobileControls)
            {
                _touch.Update(Screen.width, Screen.height);
                Yaw += _touch.LookDelta.x * TouchSensitivity * ZoomSensitivityScale;
                Pitch += _touch.LookDelta.y * TouchSensitivity * ZoomSensitivityScale;
                if (UI.VexaSettings.Gyro)
                {
                    // gyro aim on top of touch look, like most mobile shooters
                    var g = GyroReader.ViewRate(Time.unscaledDeltaTime);
                    float gs = UI.VexaSettings.GyroSensitivity * ZoomSensitivityScale;
                    Yaw += g.x * gs * (UI.VexaSettings.GyroInvertX ? -1 : 1);
                    Pitch += g.y * gs * (UI.VexaSettings.GyroInvertY ? -1 : 1);
                }
                if (_touch.SelectRequest != WeaponSelect.None) _pendingSelect = _touch.SelectRequest;
                _latched |= _touch.Buttons & (Buttons.Jump | Buttons.Attack | Buttons.Attack2 | Buttons.Reload);
            }
            else
            {
                Vector2 md = PcInput.MouseDelta();
                float k = Sensitivity * 0.022f * ZoomSensitivityScale;
                Yaw += md.x * k;
                Pitch += md.y * k;
                var sel = PcInput.WeaponKeys();
                if (sel != WeaponSelect.None) _pendingSelect = sel;
                _latched |= PcInput.Buttons() & (Buttons.Jump | Buttons.Attack | Buttons.Attack2 | Buttons.Reload | Buttons.Inspect | Buttons.Drop | Buttons.Use);
            }
            Yaw = VMath.NormalizeAngle(Yaw);
            Pitch = Mathf.Clamp(Pitch, -89f, 89f);
        }

        /// <summary>Called by the simulation once per tick.</summary>
        public PlayerInput SampleCommand()
        {
            var cmd = new PlayerInput();
            if (Enabled)
            {
                var held = MobileControls ? _touch.Buttons : PcInput.Buttons();
                // a tap shorter than one tick still registers
                cmd.Buttons = held | _latched;
                cmd.Select = _pendingSelect;
            }
            _latched = Buttons.None;
            _pendingSelect = WeaponSelect.None;
            cmd.SetAngles(Yaw, Pitch);
            return cmd;
        }
    }

    /// <summary>
    /// Mobile controls: left half = floating joystick, right half = look drag, plus on-screen buttons.
    /// Layout is in normalized screen coordinates so it scales on every device.
    /// </summary>
    public sealed class TouchControls
    {
        public struct Btn { public string Id, Label; public Rect Norm; public Buttons Button; public WeaponSelect Select; public bool Toggle; }

        public Buttons Buttons;
        public Vector2 LookDelta;
        public WeaponSelect SelectRequest;
        public Vector2 StickCenter, StickPos;
        public bool StickActive;
        public bool CrouchToggled;
        public readonly List<Btn> Layout = DefaultLayout();

        public static List<Btn> DefaultLayout() => new List<Btn>
        {
            new Btn { Id = "fire", Label = "ATEŞ", Norm = new Rect(0.80f, 0.55f, 0.13f, 0.2f), Button = Core.Buttons.Attack },
            new Btn { Id = "fire2", Label = "ATEŞ", Norm = new Rect(0.04f, 0.30f, 0.09f, 0.14f), Button = Core.Buttons.Attack },
            new Btn { Id = "jump", Label = "ZIPLA", Norm = new Rect(0.86f, 0.78f, 0.1f, 0.15f), Button = Core.Buttons.Jump },
            new Btn { Id = "duck", Label = "EĞİL", Norm = new Rect(0.74f, 0.80f, 0.1f, 0.15f), Button = Core.Buttons.Duck, Toggle = true },
            new Btn { Id = "reload", Label = "DOLDUR", Norm = new Rect(0.68f, 0.62f, 0.09f, 0.12f), Button = Core.Buttons.Reload },
            new Btn { Id = "scope", Label = "NİŞAN", Norm = new Rect(0.80f, 0.36f, 0.09f, 0.13f), Button = Core.Buttons.Attack2 },
            new Btn { Id = "weapon", Label = "SİLAH", Norm = new Rect(0.45f, 0.86f, 0.1f, 0.11f), Select = WeaponSelect.Next },
            new Btn { Id = "use", Label = "KULLAN", Norm = new Rect(0.68f, 0.45f, 0.09f, 0.12f), Button = Core.Buttons.Use },
            new Btn { Id = "nade", Label = "BOMBA", Norm = new Rect(0.56f, 0.86f, 0.1f, 0.11f), Select = WeaponSelect.Grenade },
        };

        /// <summary>"id:x,y,w,h;..." (normalized, invariant culture). Unknown ids are ignored.</summary>
        public static string Serialize(List<Btn> layout)
        {
            var sb = new System.Text.StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var b in layout)
                sb.Append(b.Id).Append(':').Append(b.Norm.x.ToString("0.####", ci)).Append(',').Append(b.Norm.y.ToString("0.####", ci))
                  .Append(',').Append(b.Norm.width.ToString("0.####", ci)).Append(',').Append(b.Norm.height.ToString("0.####", ci)).Append(';');
            return sb.ToString();
        }

        public void ApplySaved(string saved)
        {
            var def = DefaultLayout();
            Layout.Clear();
            Layout.AddRange(def);
            if (string.IsNullOrEmpty(saved)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var entry in saved.Split(';'))
            {
                var kv = entry.Split(':');
                if (kv.Length != 2) continue;
                var n = kv[1].Split(',');
                if (n.Length != 4) continue;
                if (!float.TryParse(n[0], System.Globalization.NumberStyles.Float, ci, out var x) || !float.TryParse(n[1], System.Globalization.NumberStyles.Float, ci, out var y)
                    || !float.TryParse(n[2], System.Globalization.NumberStyles.Float, ci, out var w) || !float.TryParse(n[3], System.Globalization.NumberStyles.Float, ci, out var h)) continue;
                int i = Layout.FindIndex(b => b.Id == kv[0]);
                if (i < 0) continue;
                var btn = Layout[i];
                btn.Norm = new Rect(Mathf.Clamp01(x), Mathf.Clamp01(y), Mathf.Clamp(w, 0.04f, 0.4f), Mathf.Clamp(h, 0.05f, 0.4f));
                Layout[i] = btn;
            }
        }

        private int _stickFinger = -1, _lookFinger = -1;
        private Vector2 _lastLook;
        private readonly HashSet<int> _btnFingers = new HashSet<int>();

        public void Reset() { Buttons = Core.Buttons.None; LookDelta = Vector2.zero; StickActive = false; _stickFinger = _lookFinger = -1; }

        public void Update(int w, int h)
        {
            Buttons = CrouchToggled ? Core.Buttons.Duck : Core.Buttons.None;
            LookDelta = Vector2.zero;
            SelectRequest = WeaponSelect.None;
            bool stickSeen = false, lookSeen = false;
            foreach (var t in TouchReader.Read())
            {
                var p = t.Position;
                var norm = new Vector2(p.x / w, 1f - p.y / h); // GUI-style (top-left origin)
                if (t.Began)
                {
                    bool onButton = false;
                    foreach (var b in Layout)
                    {
                        if (!b.Norm.Contains(norm)) continue;
                        onButton = true;
                        if (b.Toggle) CrouchToggled = !CrouchToggled;
                        if (b.Select != WeaponSelect.None) SelectRequest = b.Select;
                        _btnFingers.Add(t.Id);
                    }
                    if (!onButton)
                    {
                        if (norm.x < 0.4f && _stickFinger < 0) { _stickFinger = t.Id; StickCenter = p; }
                        else if (norm.x >= 0.4f && _lookFinger < 0) { _lookFinger = t.Id; _lastLook = p; }
                    }
                }
                if (_btnFingers.Contains(t.Id) && !t.Ended)
                {
                    foreach (var b in Layout)
                        if (!b.Toggle && b.Norm.Contains(norm)) Buttons |= b.Button;
                }
                if (t.Id == _stickFinger)
                {
                    stickSeen = !t.Ended;
                    StickPos = p;
                    var d = (p - StickCenter) / (Mathf.Min(w, h) * 0.12f);
                    if (d.magnitude > 1f) d.Normalize();
                    if (d.y > 0.3f) Buttons |= Core.Buttons.Forward;
                    if (d.y < -0.3f) Buttons |= Core.Buttons.Back;
                    if (d.x > 0.3f) Buttons |= Core.Buttons.Right;
                    if (d.x < -0.3f) Buttons |= Core.Buttons.Left;
                    if (d.magnitude < 0.75f) Buttons |= Core.Buttons.Walk; // small push = quiet walk
                }
                if (t.Id == _lookFinger)
                {
                    lookSeen = !t.Ended;
                    LookDelta += new Vector2(p.x - _lastLook.x, p.y - _lastLook.y);
                    _lastLook = p;
                }
                if (t.Ended) _btnFingers.Remove(t.Id);
            }
            if (!stickSeen) _stickFinger = -1;
            if (!lookSeen) _lookFinger = -1;
            StickActive = _stickFinger >= 0;
        }
    }

    public struct TouchPoint { public int Id; public Vector2 Position; public bool Began, Ended; }

    public static class TouchReader
    {
        static readonly List<TouchPoint> _list = new List<TouchPoint>(10);
        public static List<TouchPoint> Read()
        {
            _list.Clear();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var ts = Touchscreen.current;
            if (ts == null) return _list;
            foreach (var t in ts.touches)
            {
                var ph = t.phase.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.None) continue;
                _list.Add(new TouchPoint
                {
                    Id = t.touchId.ReadValue(), Position = t.position.ReadValue(),
                    Began = ph == UnityEngine.InputSystem.TouchPhase.Began,
                    Ended = ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled,
                });
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                _list.Add(new TouchPoint
                {
                    Id = t.fingerId, Position = t.position,
                    Began = t.phase == UnityEngine.TouchPhase.Began,
                    Ended = t.phase == UnityEngine.TouchPhase.Ended || t.phase == UnityEngine.TouchPhase.Canceled,
                });
            }
#endif
            return _list;
        }
    }

    /// <summary>Gyroscope as a look input: returns (yaw, pitch) degrees for this frame in landscape.</summary>
    public static class GyroReader
    {
        static bool _enabled;

        static Vector3 RateRad()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var g = UnityEngine.InputSystem.Gyroscope.current;
            if (g == null) return Vector3.zero;
            if (!_enabled) { InputSystem.EnableDevice(g); _enabled = true; }
            return g.angularVelocity.ReadValue();
#else
            if (!SystemInfo.supportsGyroscope) return Vector3.zero;
            if (!_enabled) { Input.gyro.enabled = true; _enabled = true; }
            return Input.gyro.rotationRateUnbiased;
#endif
        }

        public static Vector2 ViewRate(float dt)
        {
            var r = RateRad() * Mathf.Rad2Deg * dt;
            // landscape-left: device +x points up the screen (turning = rotation about x), screen-right is -y (tilting = rotation about -y)
            var v = new Vector2(-r.x, -r.y);
            if (Screen.orientation == ScreenOrientation.LandscapeRight) v = -v;
            return v;
        }
    }
}
