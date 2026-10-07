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

    /// <summary>Keyboard / mouse (supports the legacy Input Manager and the new Input System).</summary>
    public static class PcInput
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 MouseDelta()
        {
            var m = Mouse.current;
            if (m == null || Cursor.lockState != CursorLockMode.Locked) return Vector2.zero;
            var d = m.delta.ReadValue();
            return new Vector2(d.x, -d.y);
        }
        static bool K(Key k) => Keyboard.current != null && Keyboard.current[k].isPressed;
        static bool KD(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        public static Buttons Buttons()
        {
            var b = Core.Buttons.None;
            if (K(Key.W)) b |= Core.Buttons.Forward;
            if (K(Key.S)) b |= Core.Buttons.Back;
            if (K(Key.A)) b |= Core.Buttons.Left;
            if (K(Key.D)) b |= Core.Buttons.Right;
            if (K(Key.Space)) b |= Core.Buttons.Jump;
            if (K(Key.LeftCtrl) || K(Key.C)) b |= Core.Buttons.Duck;
            if (K(Key.LeftShift)) b |= Core.Buttons.Walk;
            if (K(Key.R)) b |= Core.Buttons.Reload;
            if (K(Key.E)) b |= Core.Buttons.Use;
            if (K(Key.F)) b |= Core.Buttons.Inspect;
            if (K(Key.G)) b |= Core.Buttons.Drop;
            var m = Mouse.current;
            if (m != null && Cursor.lockState == CursorLockMode.Locked)
            {
                if (m.leftButton.isPressed) b |= Core.Buttons.Attack;
                if (m.rightButton.isPressed) b |= Core.Buttons.Attack2;
            }
            return b;
        }
        public static WeaponSelect WeaponKeys()
        {
            if (KD(Key.Digit1)) return WeaponSelect.Primary;
            if (KD(Key.Digit2)) return WeaponSelect.Secondary;
            if (KD(Key.Digit3)) return WeaponSelect.Melee;
            if (KD(Key.Digit4)) return WeaponSelect.Grenade;
            if (KD(Key.Digit5)) return WeaponSelect.Bomb;
            if (KD(Key.Q)) return WeaponSelect.LastUsed;
            var m = Mouse.current;
            if (m != null)
            {
                float s = m.scroll.ReadValue().y;
                if (s > 0) return WeaponSelect.Previous;
                if (s < 0) return WeaponSelect.Next;
            }
            return WeaponSelect.None;
        }
        static Key ToKey(KeyCode k)
        {
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return Key.Digit1 + (k - KeyCode.Alpha1);
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return Key.F1 + (k - KeyCode.F1);
            switch (k)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Space: return Key.Space;
                case KeyCode.BackQuote: return Key.Backquote;
                default: return Key.None;
            }
        }
        public static bool KeyDown(KeyCode k) { var key = ToKey(k); return key != Key.None && KD(key); }
        public static bool KeyHeld(KeyCode k) { var key = ToKey(k); return key != Key.None && K(key); }
        public static bool AnyMouseDown() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        public static Vector2 MouseDelta()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return Vector2.zero;
            // legacy axes are already scaled by 0.1 per count in the default Input Manager
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        }
        public static Buttons Buttons()
        {
            var b = Core.Buttons.None;
            if (Input.GetKey(KeyCode.W)) b |= Core.Buttons.Forward;
            if (Input.GetKey(KeyCode.S)) b |= Core.Buttons.Back;
            if (Input.GetKey(KeyCode.A)) b |= Core.Buttons.Left;
            if (Input.GetKey(KeyCode.D)) b |= Core.Buttons.Right;
            if (Input.GetKey(KeyCode.Space)) b |= Core.Buttons.Jump;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) b |= Core.Buttons.Duck;
            if (Input.GetKey(KeyCode.LeftShift)) b |= Core.Buttons.Walk;
            if (Input.GetKey(KeyCode.R)) b |= Core.Buttons.Reload;
            if (Input.GetKey(KeyCode.E)) b |= Core.Buttons.Use;
            if (Input.GetKey(KeyCode.F)) b |= Core.Buttons.Inspect;
            if (Input.GetKey(KeyCode.G)) b |= Core.Buttons.Drop;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (Input.GetMouseButton(0)) b |= Core.Buttons.Attack;
                if (Input.GetMouseButton(1)) b |= Core.Buttons.Attack2;
            }
            return b;
        }
        public static WeaponSelect WeaponKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) return WeaponSelect.Primary;
            if (Input.GetKeyDown(KeyCode.Alpha2)) return WeaponSelect.Secondary;
            if (Input.GetKeyDown(KeyCode.Alpha3)) return WeaponSelect.Melee;
            if (Input.GetKeyDown(KeyCode.Alpha4)) return WeaponSelect.Grenade;
            if (Input.GetKeyDown(KeyCode.Alpha5)) return WeaponSelect.Bomb;
            if (Input.GetKeyDown(KeyCode.Q)) return WeaponSelect.LastUsed;
            float s = Input.mouseScrollDelta.y;
            if (s > 0) return WeaponSelect.Previous;
            if (s < 0) return WeaponSelect.Next;
            return WeaponSelect.None;
        }
        public static bool KeyDown(KeyCode k) => Input.GetKeyDown(k);
        public static bool KeyHeld(KeyCode k) => Input.GetKey(k);
        public static bool AnyMouseDown() => Input.GetMouseButtonDown(0);
#endif
    }

    /// <summary>
    /// Mobile controls: left half = floating joystick, right half = look drag, plus on-screen buttons.
    /// Layout is in normalized screen coordinates so it scales on every device.
    /// </summary>
    public sealed class TouchControls
    {
        public struct Btn { public string Label; public Rect Norm; public Buttons Button; public WeaponSelect Select; public bool Toggle; }

        public Buttons Buttons;
        public Vector2 LookDelta;
        public WeaponSelect SelectRequest;
        public Vector2 StickCenter, StickPos;
        public bool StickActive;
        public bool CrouchToggled;
        public readonly List<Btn> Layout = new List<Btn>
        {
            new Btn { Label = "ATEŞ", Norm = new Rect(0.80f, 0.55f, 0.13f, 0.2f), Button = Core.Buttons.Attack },
            new Btn { Label = "ATEŞ", Norm = new Rect(0.04f, 0.30f, 0.09f, 0.14f), Button = Core.Buttons.Attack },
            new Btn { Label = "ZIPLA", Norm = new Rect(0.86f, 0.78f, 0.1f, 0.15f), Button = Core.Buttons.Jump },
            new Btn { Label = "EĞİL", Norm = new Rect(0.74f, 0.80f, 0.1f, 0.15f), Button = Core.Buttons.Duck, Toggle = true },
            new Btn { Label = "DOLDUR", Norm = new Rect(0.68f, 0.62f, 0.09f, 0.12f), Button = Core.Buttons.Reload },
            new Btn { Label = "NİŞAN", Norm = new Rect(0.80f, 0.36f, 0.09f, 0.13f), Button = Core.Buttons.Attack2 },
            new Btn { Label = "SİLAH", Norm = new Rect(0.45f, 0.86f, 0.1f, 0.11f), Select = WeaponSelect.Next },
            new Btn { Label = "KULLAN", Norm = new Rect(0.68f, 0.45f, 0.09f, 0.12f), Button = Core.Buttons.Use },
            new Btn { Label = "BOMBA", Norm = new Rect(0.56f, 0.86f, 0.1f, 0.11f), Select = WeaponSelect.Grenade },
        };

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
}
