using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vexa.Client.UI
{
    /// <summary>Settings with tabs (game, crosshair editor, video, audio, controls). Used in the main and pause menus.</summary>
    public sealed class SettingsView : VisualElement
    {
        private readonly VisualElement _content;
        private readonly List<UButton> _tabs = new List<UButton>();
        private readonly Func<VisualElement>[] _pages;
        private CrosshairView _preview;
        private UInput _code;
        private readonly List<USlider> _xhSliders = new List<USlider>();
        private readonly List<UToggle> _xhToggles = new List<UToggle>();
        private readonly List<VisualElement> _swatches = new List<VisualElement>();

        public SettingsView()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;
            var tabCol = U.Col(4).W(220).NoShrink();
            string[] names = { "OYUN", "NİŞANGAH", "GÖRÜNTÜ", "SES", "TUŞLAR", "MOBİL" };
            _pages = new Func<VisualElement>[] { GamePage, CrosshairPage, VideoPage, AudioPage, ControlsPage, MobilePage };
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                var b = new UButton(names[i], ButtonStyle.Tab, () => Select(k));
                _tabs.Add(b);
                tabCol.Kids(b);
            }
            _content = U.Col().Grow().Margin(0, 0, 0, 36);
            hierarchy.Add(tabCol);
            hierarchy.Add(_content);
            Select(0);
            RegisterCallback<DetachFromPanelEvent>(_ => EndRebind());
        }

        private int _current;

        /// <summary>Rebuild the open tab so it shows values changed elsewhere (the other settings screen).</summary>
        public void Refresh() => Select(_current);

        public void Select(int i)
        {
            EndRebind();
            _current = i;
            for (int k = 0; k < _tabs.Count; k++) _tabs[k].Selected = k == i;
            _content.Clear();
            _xhSliders.Clear(); _xhToggles.Clear(); _swatches.Clear(); _preview = null; _code = null;
            _content.Add(_pages[i]());
        }

        static void Changed() => VexaSettings.Save();

        static USlider Slider(string label, float min, float max, float step, float value, Action<float> set, string fmt = "0.##")
        {
            var s = new USlider(label, min, max, step, value, fmt, 190);
            s.Changed += v => { set(v); Changed(); };
            return s;
        }

        static VisualElement ToggleRow(string label, bool value, Action<bool> set)
        {
            var row = U.Row(16).Align(Align.Center).H(44);
            row.Add(U.Text(label, 17, Fonts.BodySemi).W(190));
            var seg = new Segmented(new[] { "KAPALI", "AÇIK" }, value ? 1 : 0).W(240);
            seg.Changed += v => { set(v == 1); Changed(); };
            row.Add(seg);
            return row;
        }

        // ---------------- pages ----------------

        VisualElement GamePage()
        {
            var p = U.Col(16).W(620);
            p.Add(U.Eyebrow("PROFİL"));
            var name = new UInput("Oyuncu adı", VexaSettings.Name, 16, 360);
            name.Field.RegisterValueChangedCallback(e => { var v = e.newValue.Trim(); if (v.Length > 0) { VexaSettings.Name = v; Changed(); } });
            p.Add(name);
            p.Add(U.Eyebrow("FARE").Margin(12, 0, 0, 0));
            p.Add(Slider("Hassasiyet", 0.1f, 8f, 0.01f, VexaSettings.Sensitivity, v => VexaSettings.Sensitivity = v));
            p.Add(Slider("Dürbün çarpanı", 0.5f, 1.5f, 0.01f, VexaSettings.ZoomSensitivity, v => VexaSettings.ZoomSensitivity = v));
            p.Add(U.Text("Hassasiyet CS ile aynı ölçekte (m_yaw 0.022); oradaki değerini doğrudan girebilirsin.", 14, Fonts.Body, Theme.Muted));
            p.Add(U.Eyebrow("RADAR").Margin(12, 0, 0, 0));
            p.Add(ToggleRow("Radar dönsün", VexaSettings.RadarRotate, v => VexaSettings.RadarRotate = v));
            p.Add(Slider("Radar yakınlaştırma", 0.5f, 2f, 0.05f, VexaSettings.RadarZoom, v => VexaSettings.RadarZoom = v));
            p.Add(U.Eyebrow("ARAYÜZ").Margin(12, 0, 0, 0));
            p.Add(ToggleRow("Ağ istatistikleri", VexaSettings.ShowNetStats, v => VexaSettings.ShowNetStats = v));
            p.Add(U.Eyebrow("KAYIT").Margin(12, 0, 0, 0));
            p.Add(ToggleRow("Maçlarımı kaydet (demo)", VexaSettings.RecordDemos, v => VexaSettings.RecordDemos = v));
            return p;
        }

        VisualElement CrosshairPage()
        {
            var p = U.Row(40).Align(Align.FlexStart);
            // preview
            var left = U.Col(14).W(560);
            left.Add(U.Eyebrow("ÖNİZLEME"));
            var box = U.Box().H(360).Bg(Theme.Hex(0x2A3038)).Border(1, Theme.Line);
            box.style.overflow = Overflow.Hidden;
            box.Add(U.Box().Abs(0, 0, 0).HPct(50).Bg(Theme.Hex(0x3B4553)));
            var floorL = U.Box().Abs(0, null, null, 0).WPct(50).HPct(50).Bg(Theme.Hex(0xC9B48C));
            var floorR = U.Box().Abs(null, null, 0, 0).WPct(50).HPct(50).Bg(Theme.Hex(0x1C2128));
            box.Kids(floorL, floorR);
            _preview = new CrosshairView { Scale = 4f };
            _preview.style.left = new Length(50, LengthUnit.Percent);
            _preview.style.top = new Length(50, LengthUnit.Percent);
            _preview.SetStyle(VexaSettings.Crosshair, true);
            box.Add(_preview);
            left.Add(box);
            left.Add(U.Text("Önizleme 2 kat büyütülmüştür.", 13, Fonts.Body, Theme.Muted));
            var presets = U.Row(8);
            (string, CrosshairStyle)[] list = { ("Klasik", CrosshairStyle.Classic), ("Nokta", CrosshairStyle.DotOnly), ("Küçük", CrosshairStyle.Small), ("T", CrosshairStyle.T) };
            foreach (var (n, st) in list)
            {
                var s = st;
                presets.Kids(new UButton(n, ButtonStyle.Ghost, () => SetCrosshair(s, true)));
            }
            left.Add(presets);
            var codeRow = U.Row(8).Align(Align.FlexEnd);
            _code = new UInput("Paylaşım kodu", VexaSettings.Crosshair.ToCode(), 40);
            _code.Grow();
            var apply = new UButton("UYGULA", ButtonStyle.Secondary, () =>
            {
                if (CrosshairStyle.TryParse(_code.Value, out var parsed)) SetCrosshair(parsed, true);
                else _code.Value = VexaSettings.Crosshair.ToCode();
            });
            codeRow.Kids(_code, apply);
            left.Add(codeRow);

            // controls
            var right = U.Col(12).Grow();
            right.Add(U.Eyebrow("ŞEKİL"));
            var x = VexaSettings.Crosshair;
            _xhSliders.Add(XhSlider("Uzunluk", 0, 20, 0.5f, x.Length, (ref CrosshairStyle s, float v) => s.Length = v));
            _xhSliders.Add(XhSlider("Boşluk", -3, 10, 0.5f, x.Gap, (ref CrosshairStyle s, float v) => s.Gap = v));
            _xhSliders.Add(XhSlider("Kalınlık", 0.5f, 6, 0.5f, x.Thickness, (ref CrosshairStyle s, float v) => s.Thickness = v));
            _xhSliders.Add(XhSlider("Dış çizgi", 0, 3, 1, x.Outline, (ref CrosshairStyle s, float v) => s.Outline = v));
            foreach (var s in _xhSliders) right.Add(s);
            var toggles = U.Row(10).Margin(6, 0, 0, 0);
            _xhToggles.Add(XhToggle("NOKTA", x.Dot, (ref CrosshairStyle s, bool v) => s.Dot = v));
            _xhToggles.Add(XhToggle("T-STİLİ", x.TStyle, (ref CrosshairStyle s, bool v) => s.TStyle = v));
            _xhToggles.Add(XhToggle("DİNAMİK", x.Dynamic, (ref CrosshairStyle s, bool v) => s.Dynamic = v));
            foreach (var t in _xhToggles) toggles.Kids(t);
            right.Add(toggles);
            right.Add(U.Eyebrow("RENK").Margin(10, 0, 0, 0));
            var sw = U.Row(10);
            for (int i = 0; i < CrosshairStyle.Palette.Length; i++)
            {
                int k = i;
                var b = U.Box().Size(44, 44).Bg(CrosshairStyle.Palette[i]).OnClick(() =>
                {
                    var s = VexaSettings.Crosshair; s.ColorIndex = k; SetCrosshair(s, false);
                });
                b.tooltip = CrosshairStyle.PaletteNames[i];
                _swatches.Add(b);
                sw.Kids(b);
            }
            right.Add(sw);
            right.Add(U.Text("Dinamik açıkken nişangah hareket ve atışta açılır; kapalıyken sabit kalır.", 14, Fonts.Body, Theme.Muted).Margin(6, 0, 0, 0));
            PaintSwatches();
            p.Kids(left, right);
            return p;
        }

        delegate void XhSet<T>(ref CrosshairStyle s, T v);

        USlider XhSlider(string label, float min, float max, float step, float value, XhSet<float> set)
        {
            var s = new USlider(label, min, max, step, value, "0.#", 130);
            s.Changed += v => { var st = VexaSettings.Crosshair; set(ref st, v); SetCrosshair(st, false); };
            return s;
        }

        UToggle XhToggle(string label, bool value, XhSet<bool> set)
        {
            var t = new UToggle(label, value);
            t.Changed += v => { var st = VexaSettings.Crosshair; set(ref st, v); SetCrosshair(st, false); };
            return t;
        }

        void SetCrosshair(CrosshairStyle s, bool syncControls)
        {
            VexaSettings.Crosshair = s;
            Changed();
            _preview?.SetStyle(s, true);
            if (_code != null) _code.Value = s.ToCode();
            if (syncControls && _xhSliders.Count == 4)
            {
                _xhSliders[0].SetValue(s.Length, false); _xhSliders[1].SetValue(s.Gap, false);
                _xhSliders[2].SetValue(s.Thickness, false); _xhSliders[3].SetValue(s.Outline, false);
                _xhToggles[0].On = s.Dot; _xhToggles[1].On = s.TStyle; _xhToggles[2].On = s.Dynamic;
            }
            PaintSwatches();
        }

        void PaintSwatches()
        {
            for (int i = 0; i < _swatches.Count; i++)
                _swatches[i].Border(i == VexaSettings.Crosshair.ColorIndex ? 3 : 1, i == VexaSettings.Crosshair.ColorIndex ? Theme.Text : Theme.LineStrong);
        }

        VisualElement VideoPage()
        {
            var p = U.Col(16).W(640);
            p.Add(U.Eyebrow("GÖRÜNTÜ KALİTESİ"));
            int q = VexaSettings.Quality < 0 ? (Application.isMobilePlatform ? 1 : 2) : VexaSettings.Quality;
            var quality = new Segmented(VexaSettings.QualityNames, q);
            quality.Changed += v => { VexaSettings.Quality = v; Changed(); };
            p.Add(quality);
            p.Add(U.Text(Application.isMobilePlatform ? "Mobil sürüm low-poly görünüm için ayarlıdır; DÜŞÜK/ORTA pil ömrünü uzatır." : "PC sürümünde ULTRA tam gölge mesafesi ve en yüksek doku kalitesini açar.", 14, Fonts.Body, Theme.Muted));
            p.Add(U.Eyebrow("FPS SINIRI").Margin(12, 0, 0, 0));
            int capIdx = Array.IndexOf(VexaSettings.FpsCaps, VexaSettings.FpsCap);
            var cap = new Segmented(new[] { "SINIRSIZ", "60", "144", "240" }, capIdx < 0 ? 0 : capIdx);
            cap.Changed += v => { VexaSettings.FpsCap = VexaSettings.FpsCaps[v]; Changed(); };
            p.Add(cap);
            if (!Application.isMobilePlatform)
            {
                p.Add(U.Eyebrow("EKRAN").Margin(12, 0, 0, 0));
                var fs = new Segmented(new[] { "PENCERE", "TAM EKRAN" }, Screen.fullScreen ? 1 : 0);
                fs.Changed += v => Screen.fullScreen = v == 1;
                p.Add(fs);
            }
            return p;
        }

        VisualElement AudioPage()
        {
            var p = U.Col(16).W(620);
            p.Add(U.Eyebrow("SES"));
            p.Add(Slider("Ana ses", 0, 1, 0.01f, VexaSettings.Volume, v => VexaSettings.Volume = v, "0%"));
            p.Add(U.Text("Silah, adım ve bomba sesleri ses paketiyle birlikte gelecek.", 14, Fonts.Body, Theme.Muted));
            return p;
        }

        // ---------------- key bindings ----------------
        private InputAction? _rebinding;
        private UButton _rebindButton;

        VisualElement ControlsPage()
        {
            var p = U.Col().W(700);
            var head = U.Row(12).Align(Align.Center).Margin(0, 0, 10, 0);
            head.Kids(U.Eyebrow("TUŞ ATAMALARI"), U.Spacer(), new UButton("VARSAYILANLAR", ButtonStyle.Ghost, () => { KeyBindings.ResetDefaults(); Select(_current); }, 16));
            p.Add(head);
            p.Add(U.Text("Değiştirmek için tuşa tıkla, sonra yeni tuşa (ya da fare tuşuna) bas. Esc iptal eder.", 14, Fonts.Body, Theme.Muted).Margin(0, 0, 8, 0));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 640;
            foreach (var (action, label, _) in KeyBindings.Defaults)
            {
                var a = action;
                var row = U.Row().Align(Align.Center).Justify(Justify.SpaceBetween).Pad(6, 0).BorderBottom(1, Theme.Line);
                UButton key = null;
                key = new UButton(KeyBindings.KeyName(KeyBindings.Get(a)), ButtonStyle.Ghost, () => BeginRebind(a, key), 17);
                key.W(200);
                row.Kids(U.Text(label, 17, Fonts.BodySemi), key);
                scroll.Add(row);
            }
            p.Add(scroll);
            return p;
        }

        /// <summary>The settings view currently waiting for a key (polled every frame by UiRoot).</summary>
        static SettingsView _activeRebind;
        private int _rebindStartFrame;
        static int _escConsumedFrame = -1;
        /// <summary>True while waiting for a key, and on the frame Esc cancelled it (so Esc doesn't also close menus).</summary>
        public static bool Rebinding => _activeRebind != null || _escConsumedFrame == Time.frameCount;

        void BeginRebind(InputAction a, UButton button)
        {
            EndRebind();
            _rebinding = a;
            _rebindButton = button;
            _rebindStartFrame = Time.frameCount;
            button.Text = "TUŞA BAS... (Esc iptal)";
            _activeRebind = this;
        }

        static bool Displayed(VisualElement e)
        {
            if (e.panel == null) return false;
            for (var v = e; v != null; v = v.parent) if (v.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        /// <summary>Called once per frame. Captures the next key; mouse buttons only count while the pointer is over the button.</summary>
        public static void PollActiveRebind()
        {
            var v = _activeRebind;
            if (v == null) return;
            if (v._rebinding == null || v._rebindButton == null || !Displayed(v._rebindButton)) { v.EndRebind(); return; }
            if (Time.frameCount - v._rebindStartFrame < 2) return; // ignore the click that started it
            if (PcInput.KeyDown(KeyCode.Escape)) { _escConsumedFrame = Time.frameCount; v.EndRebind(); return; }
            if (!PcInput.AnyKeyDown(out var k)) return;
            bool mouse = k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
            if (mouse && !v._rebindButton.Hovered) { v.EndRebind(); return; } // clicked somewhere else: cancel
            KeyBindings.Set(v._rebinding.Value, k);
            v.EndRebind();
            v.Select(v._current); // other actions may have lost this key
        }

        void EndRebind()
        {
            if (_rebinding != null && _rebindButton != null) _rebindButton.Text = KeyBindings.KeyName(KeyBindings.Get(_rebinding.Value));
            _rebinding = null; _rebindButton = null;
            if (_activeRebind == this) _activeRebind = null;
        }

        // ---------------- mobile ----------------

        VisualElement MobilePage()
        {
            var p = U.Col(16).W(640);
            p.Add(U.Eyebrow("DOKUNMATİK"));
            p.Add(Slider("Bakış hassasiyeti", 0.05f, 0.6f, 0.01f, VexaSettings.TouchSensitivity, v => VexaSettings.TouchSensitivity = v));
            p.Add(Slider("Buton opaklığı", 0.2f, 1f, 0.05f, VexaSettings.TouchOpacity, v => VexaSettings.TouchOpacity = v, "0%"));
            var edit = new UButton("BUTON YERLEŞİMİNİ DÜZENLE", ButtonStyle.Secondary, () => TouchLayoutEditor.Open(this));
            edit.Self(Align.FlexStart);
            p.Add(edit);
            p.Add(U.Eyebrow("JİROSKOP").Margin(12, 0, 0, 0));
            p.Add(ToggleRow("Jiroskopla nişan", VexaSettings.Gyro, v => VexaSettings.Gyro = v));
            p.Add(Slider("Jiroskop hassasiyeti", 0.2f, 3f, 0.05f, VexaSettings.GyroSensitivity, v => VexaSettings.GyroSensitivity = v));
            p.Add(ToggleRow("Yatay ters", VexaSettings.GyroInvertX, v => VexaSettings.GyroInvertX = v));
            p.Add(ToggleRow("Dikey ters", VexaSettings.GyroInvertY, v => VexaSettings.GyroInvertY = v));
            var note = U.Text("Jiroskop dokunmatik bakışa eklenir: büyük dönüşleri parmakla, ince nişanı telefonu eğerek yaparsın.", 14, Fonts.Body, Theme.Muted);
            note.style.whiteSpace = WhiteSpace.Normal;
            p.Add(note);
            if (!Application.isMobilePlatform) p.Add(U.Text("Bu ayarlar mobil sürümde geçerli; yerleşimi burada önizleyebilirsin.", 14, Fonts.Body, Theme.Dim));
            return p;
        }
    }
}
