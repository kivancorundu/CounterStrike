using System;
using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Vexa.Client
{
    /// <summary>Everything a key can be bound to.</summary>
    public enum InputAction
    {
        Forward, Back, Left, Right, Jump, Duck, Walk, Attack, Attack2, Reload, Use, Inspect, Drop,
        Primary, Secondary, Melee, Grenade, Bomb, LastUsed,
        Buy, Scoreboard, ChatAll, ChatTeam, TeamMenu,
    }

    /// <summary>Key bindings (CS defaults), saved in PlayerPrefs. Mouse buttons are KeyCode.Mouse0..Mouse4.</summary>
    public static class KeyBindings
    {
        public static readonly (InputAction action, string label, KeyCode key)[] Defaults =
        {
            (InputAction.Forward, "İleri", KeyCode.W), (InputAction.Back, "Geri", KeyCode.S),
            (InputAction.Left, "Sola", KeyCode.A), (InputAction.Right, "Sağa", KeyCode.D),
            (InputAction.Jump, "Zıpla", KeyCode.Space), (InputAction.Duck, "Eğil", KeyCode.LeftControl),
            (InputAction.Walk, "Yürü (sessiz)", KeyCode.LeftShift),
            (InputAction.Attack, "Ateş", KeyCode.Mouse0), (InputAction.Attack2, "İkincil ateş / dürbün", KeyCode.Mouse1),
            (InputAction.Reload, "Doldur", KeyCode.R), (InputAction.Use, "Kullan / imha et", KeyCode.E),
            (InputAction.Inspect, "Silahı incele", KeyCode.F), (InputAction.Drop, "Silahı bırak", KeyCode.G),
            (InputAction.Primary, "Birincil silah", KeyCode.Alpha1), (InputAction.Secondary, "İkincil silah", KeyCode.Alpha2),
            (InputAction.Melee, "Bıçak", KeyCode.Alpha3), (InputAction.Grenade, "El bombaları", KeyCode.Alpha4),
            (InputAction.Bomb, "C4", KeyCode.Alpha5), (InputAction.LastUsed, "Son silah", KeyCode.Q),
            (InputAction.Buy, "Satın alma menüsü", KeyCode.B), (InputAction.Scoreboard, "Skor tablosu", KeyCode.Tab),
            (InputAction.ChatAll, "Sohbet (herkes)", KeyCode.Y), (InputAction.ChatTeam, "Sohbet (takım)", KeyCode.U),
            (InputAction.TeamMenu, "Takım menüsü", KeyCode.M),
        };

        static readonly Dictionary<InputAction, KeyCode> _keys = new Dictionary<InputAction, KeyCode>();
        public static event Action Changed;

        static KeyBindings() => Load();

        public static KeyCode Get(InputAction a) => _keys.TryGetValue(a, out var k) ? k : KeyCode.None;

        public static void Set(InputAction a, KeyCode k)
        {
            // one key, one action: whatever had this key loses it
            foreach (var (other, _, _) in Defaults)
                if (other != a && Get(other) == k) _keys[other] = KeyCode.None;
            _keys[a] = k;
            Save();
        }

        public static void Load()
        {
            foreach (var (a, _, def) in Defaults)
                _keys[a] = (KeyCode)PlayerPrefs.GetInt("vexa.bind." + a, (int)def);
        }

        public static void Save()
        {
            foreach (var kv in _keys) PlayerPrefs.SetInt("vexa.bind." + kv.Key, (int)kv.Value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ResetDefaults()
        {
            foreach (var (a, _, def) in Defaults) _keys[a] = def;
            Save();
        }

        public static string KeyName(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            switch (k)
            {
                case KeyCode.None: return "—";
                case KeyCode.Mouse0: return "Sol tık";
                case KeyCode.Mouse1: return "Sağ tık";
                case KeyCode.Mouse2: return "Orta tık";
                case KeyCode.Mouse3: return "Fare 4";
                case KeyCode.Mouse4: return "Fare 5";
                case KeyCode.Space: return "Boşluk";
                case KeyCode.LeftControl: return "Sol Ctrl";
                case KeyCode.RightControl: return "Sağ Ctrl";
                case KeyCode.LeftShift: return "Sol Shift";
                case KeyCode.RightShift: return "Sağ Shift";
                case KeyCode.LeftAlt: return "Sol Alt";
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.BackQuote: return "`";
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
                case KeyCode.LeftArrow: return "←";
                case KeyCode.RightArrow: return "→";
                default: return k.ToString().ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// Keyboard and mouse through the bindings. Works with the legacy Input Manager and the new Input System
    /// (the raw key layer is the only part that differs).
    /// </summary>
    public static class PcInput
    {
        /// <summary>Keys the rebinding screen can capture (and that the Input System layer maps).</summary>
        public static readonly KeyCode[] Bindable = BuildBindable();

        static KeyCode[] BuildBindable()
        {
            var l = new List<KeyCode>();
            for (var k = KeyCode.A; k <= KeyCode.Z; k++) l.Add(k);
            for (var k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) l.Add(k);
            for (var k = KeyCode.F1; k <= KeyCode.F12; k++) l.Add(k);
            l.AddRange(new[]
            {
                KeyCode.Space, KeyCode.Tab, KeyCode.Return, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
                KeyCode.LeftAlt, KeyCode.BackQuote, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4,
            });
            return l.ToArray();
        }

        // ---------------- raw layer ----------------
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 MouseDelta()
        {
            var m = Mouse.current;
            if (m == null || Cursor.lockState != CursorLockMode.Locked) return Vector2.zero;
            var d = m.delta.ReadValue();
            return new Vector2(d.x, d.y); // Input System: +y = mouse moved up = look up (pitch up positive)
        }
        public static float Scroll() => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        static UnityEngine.InputSystem.Controls.ButtonControl Control(KeyCode k)
        {
            var m = Mouse.current;
            switch (k)
            {
                case KeyCode.Mouse0: return m?.leftButton;
                case KeyCode.Mouse1: return m?.rightButton;
                case KeyCode.Mouse2: return m?.middleButton;
                case KeyCode.Mouse3: return m?.backButton;
                case KeyCode.Mouse4: return m?.forwardButton;
            }
            var kb = Keyboard.current;
            if (kb == null) return null;
            var key = ToKey(k);
            return key == Key.None ? null : kb[key];
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
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.Space: return Key.Space;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                default: return Key.None;
            }
        }

        public static bool KeyHeld(KeyCode k) { var c = Control(k); return c != null && c.isPressed; }
        public static bool KeyDown(KeyCode k) { var c = Control(k); return c != null && c.wasPressedThisFrame; }
#else
        public static Vector2 MouseDelta()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return Vector2.zero;
            // legacy axes are already scaled by 0.1 per count in the default Input Manager
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        }
        public static float Scroll() => Input.mouseScrollDelta.y;
        public static bool KeyHeld(KeyCode k) => k != KeyCode.None && Input.GetKey(k);
        public static bool KeyDown(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);
#endif

        public static bool AnyMouseDown() => KeyDown(KeyCode.Mouse0);

        /// <summary>For the rebinding screen: the first bindable key pressed this frame.</summary>
        public static bool AnyKeyDown(out KeyCode key)
        {
            foreach (var k in Bindable) if (KeyDown(k)) { key = k; return true; }
            key = KeyCode.None;
            return false;
        }

        // ---------------- bound actions ----------------
        public static bool Held(InputAction a) => KeyHeld(KeyBindings.Get(a));
        public static bool Down(InputAction a) => KeyDown(KeyBindings.Get(a));

        static bool MouseAction(InputAction a)
        {
            var k = KeyBindings.Get(a);
            return k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
        }

        public static Buttons Buttons()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            var b = Core.Buttons.None;
            void Map(InputAction a, Buttons bit)
            {
                // mouse buttons only count while the game owns the mouse (not while clicking UI)
                if (MouseAction(a) && !locked) return;
                if (Held(a)) b |= bit;
            }
            Map(InputAction.Forward, Core.Buttons.Forward);
            Map(InputAction.Back, Core.Buttons.Back);
            Map(InputAction.Left, Core.Buttons.Left);
            Map(InputAction.Right, Core.Buttons.Right);
            Map(InputAction.Jump, Core.Buttons.Jump);
            Map(InputAction.Duck, Core.Buttons.Duck);
            Map(InputAction.Walk, Core.Buttons.Walk);
            Map(InputAction.Attack, Core.Buttons.Attack);
            Map(InputAction.Attack2, Core.Buttons.Attack2);
            Map(InputAction.Reload, Core.Buttons.Reload);
            Map(InputAction.Use, Core.Buttons.Use);
            Map(InputAction.Inspect, Core.Buttons.Inspect);
            Map(InputAction.Drop, Core.Buttons.Drop);
            return b;
        }

        public static WeaponSelect WeaponKeys()
        {
            if (Down(InputAction.Primary)) return WeaponSelect.Primary;
            if (Down(InputAction.Secondary)) return WeaponSelect.Secondary;
            if (Down(InputAction.Melee)) return WeaponSelect.Melee;
            if (Down(InputAction.Grenade)) return WeaponSelect.Grenade;
            if (Down(InputAction.Bomb)) return WeaponSelect.Bomb;
            if (Down(InputAction.LastUsed)) return WeaponSelect.LastUsed;
            float s = Cursor.lockState == CursorLockMode.Locked ? Scroll() : 0f;
            if (s > 0) return WeaponSelect.Previous;
            if (s < 0) return WeaponSelect.Next;
            return WeaponSelect.None;
        }
    }
}
