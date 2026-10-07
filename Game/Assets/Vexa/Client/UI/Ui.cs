using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vexa.Client.UI
{
    /// <summary>VEXA visual identity: dark ink ground, acid-lime accent, amber attackers, blue defenders.</summary>
    public static class Theme
    {
        public static readonly Color Ink = Hex(0x0A0D12);
        public static readonly Color Ink2 = Hex(0x0E1219);
        public static readonly Color Raised = Hex(0x151B24);
        public static readonly Color Panel = new Color(0.04f, 0.05f, 0.07f, 0.80f);
        public static readonly Color PanelStrong = new Color(0.04f, 0.05f, 0.07f, 0.92f);
        public static readonly Color Line = new Color(1, 1, 1, 0.09f);
        public static readonly Color LineStrong = new Color(1, 1, 1, 0.16f);
        public static readonly Color Text = Hex(0xEEF1F5);
        public static readonly Color Muted = Hex(0x9AA3B2);
        public static readonly Color Dim = Hex(0x5E6676);
        public static readonly Color Accent = Hex(0xD7FF3C);
        public static readonly Color AccentSoft = new Color(0.84f, 1f, 0.24f, 0.10f);
        public static readonly Color T = Hex(0xF2A33A);
        public static readonly Color CT = Hex(0x5AB0FF);
        public static readonly Color Danger = Hex(0xFF5A4E);
        public static readonly Color Scrim = new Color(0.02f, 0.03f, 0.05f, 0.86f);

        public static Color TeamColor(Core.Team t) => t == Core.Team.T ? T : t == Core.Team.CT ? CT : Text;
        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }

    /// <summary>Barlow / Barlow Condensed (SIL OFL), loaded from Resources/Fonts.</summary>
    public static class Fonts
    {
        static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();

        static Font Get(string file)
        {
            if (_cache.TryGetValue(file, out var f)) return f;
            f = Resources.Load<Font>("Fonts/" + file);
            if (f == null) Debug.LogWarning("[ui] font missing: " + file);
            _cache[file] = f;
            return f;
        }

        public static Font Display => Get("BarlowCondensed_800ExtraBold");
        public static Font DisplayBold => Get("BarlowCondensed_700Bold");
        public static Font DisplaySemi => Get("BarlowCondensed_600SemiBold");
        public static Font DisplayMedium => Get("BarlowCondensed_500Medium");
        public static Font Body => Get("Barlow_500Medium");
        public static Font BodyRegular => Get("Barlow_400Regular");
        public static Font BodySemi => Get("Barlow_600SemiBold");
        public static Font BodyBold => Get("Barlow_700Bold");
    }

    /// <summary>Fluent helpers so screens can be written compactly in C# (no USS files needed at runtime).</summary>
    public static class U
    {
        // ---------- creation ----------
        public static VisualElement Box(string name = null) => new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        public static VisualElement Row(float gap = 0) => new VisualElement { pickingMode = PickingMode.Ignore }.Dir(FlexDirection.Row).Gap(gap);
        public static VisualElement Col(float gap = 0) => new VisualElement { pickingMode = PickingMode.Ignore }.Dir(FlexDirection.Column).Gap(gap);
        public static VisualElement Spacer() { var s = Box(); s.style.flexGrow = 1; return s; }

        public static Label Text(string s, float size, Font font = null, Color? color = null)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = color ?? Theme.Text;
            l.style.unityFontDefinition = FontDefinition.FromFont(font ?? Fonts.Body);
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            return l;
        }

        /// <summary>Condensed uppercase heading.</summary>
        public static Label Head(string s, float size, Color? color = null, Font font = null, float spacing = 1f)
        {
            var l = Text(s, size, font ?? Fonts.Display, color);
            l.style.letterSpacing = spacing;
            return l;
        }

        /// <summary>Small tracked label above a section ("01 · OYUN MODU").</summary>
        public static Label Eyebrow(string s, Color? color = null) => Head(s, 15, color ?? Theme.Muted, Fonts.DisplayBold, 4f);

        // ---------- layout ----------
        public static T Dir<T>(this T e, FlexDirection d) where T : VisualElement { e.style.flexDirection = d; return e; }
        public static T Grow<T>(this T e, float g = 1) where T : VisualElement { e.style.flexGrow = g; e.style.flexShrink = g > 0 ? 1 : 0; return e; }
        public static T NoShrink<T>(this T e) where T : VisualElement { e.style.flexShrink = 0; return e; }
        public static T Align<T>(this T e, Align a) where T : VisualElement { e.style.alignItems = a; return e; }
        public static T Justify<T>(this T e, Justify j) where T : VisualElement { e.style.justifyContent = j; return e; }
        public static T Self<T>(this T e, Align a) where T : VisualElement { e.style.alignSelf = a; return e; }
        public static T Wrap<T>(this T e) where T : VisualElement { e.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap; return e; }

        /// <summary>
        /// UI Toolkit has no CSS gap: emulate it with a margin on every child but the first. Children added with
        /// <see cref="Kids"/> get it immediately; children added any other way get it after the first layout pass.
        /// A child given its own margin with <see cref="Margin{T}"/> keeps it.
        /// </summary>
        public static T Gap<T>(this T e, float gap) where T : VisualElement
        {
            if (gap <= 0) return e;
            e.userData = gap;
            e.RegisterCallback<GeometryChangedEvent>(_ => ApplyGap(e, gap));
            return e;
        }

        const string CustomMargin = "vx-margin";

        static void ApplyGap(VisualElement e, float gap)
        {
            bool row = e.resolvedStyle.flexDirection == FlexDirection.Row || e.resolvedStyle.flexDirection == FlexDirection.RowReverse;
            int i = 0;
            foreach (var c in e.Children())
            {
                // like CSS gap: absolutely positioned and hidden children take no part in it
                if (c.resolvedStyle.position == Position.Absolute || c.resolvedStyle.display == DisplayStyle.None) continue;
                if (!c.ClassListContains(CustomMargin))
                {
                    if (row) c.style.marginLeft = i == 0 ? 0 : gap;
                    else c.style.marginTop = i == 0 ? 0 : gap;
                }
                i++;
            }
        }

        public static T Kids<T>(this T parent, params VisualElement[] children) where T : VisualElement
        {
            foreach (var c in children)
            {
                if (c == null) continue;
                if (parent.userData is float gap && parent.childCount > 0 && !c.ClassListContains(CustomMargin))
                {
                    if (parent.style.flexDirection.value == FlexDirection.Row) c.style.marginLeft = gap;
                    else c.style.marginTop = gap;
                }
                parent.hierarchy.Add(c);
            }
            return parent;
        }

        public static T Size<T>(this T e, float w, float h) where T : VisualElement { if (w >= 0) e.style.width = w; if (h >= 0) e.style.height = h; return e; }
        public static T W<T>(this T e, float w) where T : VisualElement { e.style.width = w; return e; }
        public static T H<T>(this T e, float h) where T : VisualElement { e.style.height = h; return e; }
        public static T WPct<T>(this T e, float pct) where T : VisualElement { e.style.width = new Length(pct, LengthUnit.Percent); return e; }
        public static T HPct<T>(this T e, float pct) where T : VisualElement { e.style.height = new Length(pct, LengthUnit.Percent); return e; }
        public static T MinW<T>(this T e, float w) where T : VisualElement { e.style.minWidth = w; return e; }

        public static T Abs<T>(this T e, float? left = null, float? top = null, float? right = null, float? bottom = null) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            return e;
        }

        public static T Fill<T>(this T e) where T : VisualElement => e.Abs(0, 0, 0, 0);

        /// <summary>Absolutely positioned, horizontally centered at the given top.</summary>
        public static T CenterX<T>(this T e, float? top = null, float? bottom = null) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0;
            if (top.HasValue) e.style.top = top.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            e.style.alignItems = UnityEngine.UIElements.Align.Center;
            return e;
        }

        public static T Pad<T>(this T e, float v, float h) where T : VisualElement
        {
            e.style.paddingTop = e.style.paddingBottom = v;
            e.style.paddingLeft = e.style.paddingRight = h;
            return e;
        }
        public static T Pad<T>(this T e, float all) where T : VisualElement => e.Pad(all, all);
        public static T Pad<T>(this T e, float top, float right, float bottom, float left) where T : VisualElement
        {
            e.style.paddingTop = top; e.style.paddingRight = right; e.style.paddingBottom = bottom; e.style.paddingLeft = left;
            return e;
        }
        public static T Margin<T>(this T e, float top, float right, float bottom, float left) where T : VisualElement
        {
            e.style.marginTop = top; e.style.marginRight = right; e.style.marginBottom = bottom; e.style.marginLeft = left;
            e.AddToClassList(CustomMargin);
            return e;
        }

        // ---------- paint ----------
        public static T Bg<T>(this T e, Color c) where T : VisualElement { e.style.backgroundColor = c; return e; }
        public static T Fg<T>(this T e, Color c) where T : VisualElement { e.style.color = c; return e; }
        public static T Opacity<T>(this T e, float o) where T : VisualElement { e.style.opacity = o; return e; }
        public static T Radius<T>(this T e, float r) where T : VisualElement
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
            return e;
        }
        public static T Border<T>(this T e, float w, Color c) where T : VisualElement
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
            return e;
        }
        public static T BorderBottom<T>(this T e, float w, Color c) where T : VisualElement { e.style.borderBottomWidth = w; e.style.borderBottomColor = c; return e; }
        public static T BorderTop<T>(this T e, float w, Color c) where T : VisualElement { e.style.borderTopWidth = w; e.style.borderTopColor = c; return e; }
        public static T BorderLeft<T>(this T e, float w, Color c) where T : VisualElement { e.style.borderLeftWidth = w; e.style.borderLeftColor = c; return e; }
        public static T BorderRight<T>(this T e, float w, Color c) where T : VisualElement { e.style.borderRightWidth = w; e.style.borderRightColor = c; return e; }

        public static T Font<T>(this T e, Font f, float size) where T : VisualElement
        {
            e.style.unityFontDefinition = FontDefinition.FromFont(f);
            e.style.fontSize = size;
            return e;
        }
        public static T Spacing<T>(this T e, float s) where T : VisualElement { e.style.letterSpacing = s; return e; }
        public static T TextAlign<T>(this T e, TextAnchor a) where T : VisualElement { e.style.unityTextAlign = a; return e; }
        public static T Outline<T>(this T e, float w = 1f) where T : VisualElement
        {
            e.style.unityTextOutlineWidth = w;
            e.style.unityTextOutlineColor = new Color(0, 0, 0, 0.65f);
            return e;
        }

        public static T Show<T>(this T e, bool visible) where T : VisualElement
        {
            var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
            return e;
        }
        public static bool Visible(this VisualElement e) => e.style.display != DisplayStyle.None;

        public static T Rotate<T>(this T e, float deg) where T : VisualElement { e.style.rotate = new Rotate(new Angle(deg)); return e; }
        public static T Move<T>(this T e, float x, float y) where T : VisualElement { e.style.translate = new Translate(x, y, 0); return e; }

        public static T Fade<T>(this T e, float seconds) where T : VisualElement
        {
            e.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> { new StylePropertyName("opacity") });
            e.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(seconds) });
            return e;
        }

        public static T Pickable<T>(this T e) where T : VisualElement { e.pickingMode = PickingMode.Position; return e; }

        public static T OnClick<T>(this T e, Action a) where T : VisualElement
        {
            e.pickingMode = PickingMode.Position;
            e.RegisterCallback<ClickEvent>(_ => { UiSound.Click(); a(); });
            return e;
        }

        /// <summary>Simple stroke icons built from rectangles (no image assets needed).</summary>
        public static VisualElement Diamond(float size, Color c)
        {
            var d = Box().Size(size, size).Bg(c);
            d.style.rotate = new Rotate(new Angle(45));
            return d;
        }

        public static string Money(int v)
        {
            // "3 450" style grouping, like the Turkish/European thin-space convention
            string s = Math.Abs(v).ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return (v < 0 ? "-$" : "$") + sb;
        }

        public static string Clock(float seconds)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }

    /// <summary>Hook for UI sounds (audio assets come with the art pass).</summary>
    public static class UiSound
    {
        public static event Action Clicked, Hovered;
        public static void Click() => Clicked?.Invoke();
        public static void Hover() => Hovered?.Invoke();
    }

    // =====================================================================================
    // Widgets
    // =====================================================================================

    public enum ButtonStyle { Primary, Secondary, Ghost, Nav, Segment, Tab, Danger, MenuItem }

    /// <summary>Clickable block with hover/selected/disabled states.</summary>
    public class UButton : VisualElement
    {
        public readonly Label Label;
        public Label Sub;
        private readonly ButtonStyle _style;
        private bool _hover, _selected, _enabled = true;
        private Action _onClick;
        public Color? TintOverride;

        public UButton(string text, ButtonStyle kind, Action onClick = null, float fontSize = 0)
        {
            _style = kind;
            _onClick = onClick;
            pickingMode = PickingMode.Position;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.justifyContent = kind == ButtonStyle.MenuItem || kind == ButtonStyle.Tab ? Justify.FlexStart : Justify.Center;
            Label = U.Text(text, 0, Fonts.DisplayBold);
            Label.style.letterSpacing = 2;
            hierarchy.Add(Label);
            switch (kind)
            {
                case ButtonStyle.Primary: this.H(64).Pad(0, 28); Label.Font(Fonts.Display, fontSize > 0 ? fontSize : 30).Spacing(4); break;
                case ButtonStyle.Secondary: this.H(48).Pad(0, 22); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 20); break;
                case ButtonStyle.Ghost: this.H(44).Pad(0, 16); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 18); break;
                case ButtonStyle.Danger: this.H(48).Pad(0, 22); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 20); break;
                case ButtonStyle.Nav: this.H(40).Pad(0, 18); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 20); break;
                case ButtonStyle.Segment: this.H(46).Grow(); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 19); break;
                case ButtonStyle.Tab: this.H(48).Pad(0, 18); Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 22); break;
                case ButtonStyle.MenuItem:
                    this.Pad(12, 0).BorderBottom(1, Theme.Line);
                    Label.Font(Fonts.DisplayBold, fontSize > 0 ? fontSize : 28).Spacing(3);
                    break;
            }
            RegisterCallback<PointerEnterEvent>(_ => { _hover = true; if (_enabled) UiSound.Hover(); Refresh(); });
            RegisterCallback<PointerLeaveEvent>(_ => { _hover = false; Refresh(); });
            RegisterCallback<ClickEvent>(_ => { if (_enabled && _onClick != null) { UiSound.Click(); _onClick(); } });
            Refresh();
        }

        public UButton WithSub(string text)
        {
            Sub = U.Text(text, 17, Fonts.DisplayBold, Theme.Dim);
            Sub.style.letterSpacing = 2;
            hierarchy.Add(U.Spacer());
            hierarchy.Add(Sub);
            return this;
        }

        public void SetClick(Action a) => _onClick = a;
        public bool Selected { get => _selected; set { if (_selected != value) { _selected = value; Refresh(); } } }
        public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; Refresh(); } } }
        public string Text { get => Label.text; set => Label.text = value; }

        public void Refresh()
        {
            Color bg = Color.clear, fg = Theme.Text, border = Color.clear;
            float borderW = 0;
            switch (_style)
            {
                case ButtonStyle.Primary:
                    bg = _hover ? Color.Lerp(Theme.Accent, Color.white, 0.35f) : Theme.Accent; fg = Theme.Ink; break;
                case ButtonStyle.Secondary:
                    bg = _hover ? Color.white : Theme.Text; fg = Theme.Ink; break;
                case ButtonStyle.Danger:
                    bg = _hover ? Color.Lerp(Theme.Danger, Color.white, 0.2f) : Theme.Danger; fg = Theme.Ink; break;
                case ButtonStyle.Ghost:
                    bg = _hover ? Theme.WithAlpha(Color.white, 0.08f) : Theme.WithAlpha(Theme.Raised, 0.9f); borderW = 1; border = Theme.Line; break;
                case ButtonStyle.Nav:
                    bg = _selected ? Theme.Accent : (_hover ? Theme.WithAlpha(Color.white, 0.06f) : Color.clear);
                    fg = _selected ? Theme.Ink : (_hover ? Theme.Text : Theme.Muted); break;
                case ButtonStyle.Segment:
                    bg = _selected ? Theme.Text : (_hover ? Theme.WithAlpha(Color.white, 0.06f) : Color.clear);
                    fg = _selected ? Theme.Ink : (TintOverride ?? Theme.Muted); break;
                case ButtonStyle.Tab:
                    bg = _selected ? Theme.Raised : (_hover ? Theme.WithAlpha(Color.white, 0.04f) : Color.clear);
                    fg = _selected || _hover ? Theme.Text : Theme.Muted;
                    style.borderLeftWidth = 3; style.borderLeftColor = _selected ? Theme.Accent : Color.clear;
                    break;
                case ButtonStyle.MenuItem:
                    fg = _hover ? Theme.Accent : Theme.Text;
                    Label.style.translate = new Translate(_hover ? 8 : 0, 0, 0);
                    break;
            }
            if (!_enabled) { fg = Theme.WithAlpha(fg, 0.35f); if (bg.a > 0) bg = Theme.WithAlpha(bg, bg.a * 0.35f); }
            style.backgroundColor = bg;
            Label.style.color = fg;
            if (borderW > 0) this.Border(borderW, border);
        }
    }

    /// <summary>Row of mutually exclusive options.</summary>
    public sealed class Segmented : VisualElement
    {
        private readonly List<UButton> _buttons = new List<UButton>();
        public int Value { get; private set; }
        public event Action<int> Changed;

        public Segmented(string[] options, int value, Color[] tints = null)
        {
            style.flexDirection = FlexDirection.Row;
            this.Border(1, Theme.LineStrong);
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var b = new UButton(options[i], ButtonStyle.Segment, () => Set(k, true));
                if (tints != null && i < tints.Length) { b.TintOverride = tints[i]; b.Refresh(); }
                _buttons.Add(b);
                hierarchy.Add(b);
            }
            Set(value, false);
        }

        public void Set(int v, bool notify)
        {
            Value = Mathf.Clamp(v, 0, _buttons.Count - 1);
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].Selected = i == Value;
            if (notify) Changed?.Invoke(Value);
        }
    }

    /// <summary>Horizontal slider with a value readout; drag or click the track.</summary>
    public sealed class USlider : VisualElement
    {
        private readonly VisualElement _track, _fill, _thumb;
        private readonly Label _value;
        private readonly float _min, _max, _step;
        private readonly string _format;
        private bool _drag;
        public float Value { get; private set; }
        public event Action<float> Changed;

        public USlider(string label, float min, float max, float step, float value, string format = "0.##", float labelWidth = 150)
        {
            _min = min; _max = max; _step = step; _format = format;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 40;
            var l = U.Text(label, 17, Fonts.BodySemi).W(labelWidth).NoShrink();
            _track = new VisualElement { pickingMode = PickingMode.Position }.Grow().H(28);
            _track.style.justifyContent = Justify.Center;
            var rail = U.Box().H(4).Bg(Theme.LineStrong);
            _fill = U.Box().Abs(0, 0, null, 0).Bg(Theme.Accent);
            rail.Add(_fill);
            _thumb = U.Box().Size(14, 14).Bg(Theme.Text).Abs(null, 7);
            _thumb.style.marginLeft = -7;
            _track.hierarchy.Add(rail);
            _track.hierarchy.Add(_thumb);
            _value = U.Text("", 22, Fonts.DisplayBold, Theme.Accent).W(64).TextAlign(TextAnchor.MiddleRight).NoShrink();
            hierarchy.Add(l); hierarchy.Add(_track); hierarchy.Add(_value);

            _track.RegisterCallback<PointerDownEvent>(e => { _drag = true; _track.CapturePointer(e.pointerId); FromPointer(e.localPosition.x); });
            _track.RegisterCallback<PointerMoveEvent>(e => { if (_drag) FromPointer(e.localPosition.x); });
            _track.RegisterCallback<PointerUpEvent>(e => { _drag = false; _track.ReleasePointer(e.pointerId); });
            _track.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            SetValue(value, false);
        }

        void FromPointer(float x)
        {
            float w = _track.resolvedStyle.width;
            if (w <= 1) return;
            SetValue(Mathf.Lerp(_min, _max, Mathf.Clamp01(x / w)), true);
        }

        public void SetValue(float v, bool notify)
        {
            if (_step > 0) v = Mathf.Round(v / _step) * _step;
            v = Mathf.Clamp(v, _min, _max);
            bool changed = !Mathf.Approximately(v, Value);
            Value = v;
            _value.text = v.ToString(_format, System.Globalization.CultureInfo.InvariantCulture);
            Layout();
            if (notify && changed) Changed?.Invoke(v);
        }

        void Layout()
        {
            float t = Mathf.InverseLerp(_min, _max, Value);
            _fill.style.width = new Length(t * 100f, LengthUnit.Percent);
            _thumb.style.left = new Length(t * 100f, LengthUnit.Percent);
        }
    }

    /// <summary>On/off switch shown as a labelled block.</summary>
    public sealed class UToggle : UButton
    {
        private bool _on;
        public event Action<bool> Changed;
        public bool On { get => _on; set { _on = value; Paint(); } }

        public UToggle(string text, bool on) : base(text, ButtonStyle.Segment)
        {
            this.Border(1, Theme.Line);
            style.flexGrow = 1;
            SetClick(() => { _on = !_on; Paint(); Changed?.Invoke(_on); });
            _on = on;
            Paint();
        }

        void Paint() => Selected = _on;
    }

    /// <summary>Styled text field (works without a runtime theme).</summary>
    public sealed class UInput : VisualElement
    {
        public readonly TextField Field;
        public string Value { get => Field.value; set => Field.SetValueWithoutNotify(value); }

        public UInput(string label, string value, int maxLength, float width = -1)
        {
            style.flexDirection = FlexDirection.Column;
            if (width > 0) style.width = width;
            if (!string.IsNullOrEmpty(label)) hierarchy.Add(U.Text(label, 13, Fonts.Body, Theme.Muted).Margin(0, 0, 4, 0));
            Field = new TextField { maxLength = maxLength, value = value };
            Field.style.marginLeft = Field.style.marginRight = Field.style.marginTop = Field.style.marginBottom = 0;
            Field.style.height = 42;
            Field.style.unityFontDefinition = FontDefinition.FromFont(Fonts.BodySemi);
            Field.style.fontSize = 17;
            Field.style.color = Theme.Text;
            var input = Field.Q(className: TextField.inputUssClassName) ?? Field;
            input.Bg(Theme.Raised).Border(1, Theme.LineStrong).Pad(0, 12);
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            input.style.color = Theme.Text;
            Field.RegisterCallback<FocusInEvent>(_ => input.Border(1, Theme.Accent));
            Field.RegisterCallback<FocusOutEvent>(_ => input.Border(1, Theme.LineStrong));
            hierarchy.Add(Field);
        }
    }
}
