using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vexa.Client.UI
{
    /// <summary>
    /// Full-screen editor for the mobile buttons: drag to move, drag the corner to resize.
    /// Positions are saved normalized to the screen, so they fit every device.
    /// </summary>
    public sealed class TouchLayoutEditor : VisualElement
    {
        sealed class Item { public TouchControls.Btn Btn; public VisualElement El; }

        private readonly List<Item> _items = new List<Item>();
        private readonly VisualElement _area;
        private Item _drag;
        private bool _resize;
        private Vector2 _grab;

        static TouchLayoutEditor _current;
        public static bool IsOpen => _current != null && _current.panel != null;

        public static void Open(VisualElement anywhere)
        {
            var root = anywhere?.panel?.visualTree;
            if (root == null) return;
            CloseCurrent();
            _current = new TouchLayoutEditor();
            root.Add(_current);
        }

        public static void CloseCurrent()
        {
            _current?.RemoveFromHierarchy();
            _current = null;
        }

        public TouchLayoutEditor()
        {
            this.Fill().Bg(Theme.Hex(0x2E3540));
            pickingMode = PickingMode.Position;
            _area = U.Box().Fill();
            _area.pickingMode = PickingMode.Position;
            hierarchy.Add(_area);
            // a hint of a game view so the layout can be judged
            _area.Add(U.Box().Abs(0, 0, 0).HPct(48).Bg(Theme.Hex(0x6E7C8E)));
            var bar = U.Row(10).CenterX(24);
            var inner = U.Row(10).Align(Align.Center).Bg(Theme.PanelStrong).Pad(10, 16).Pickable();
            inner.Kids(U.Head("BUTON YERLEŞİMİ", 22, null, Fonts.Display, 2),
                       U.Text("Sürükle: taşı · sağ alt köşe: boyutlandır", 14, Fonts.Body, Theme.Muted).Margin(0, 12, 0, 12),
                       new UButton("VARSAYILAN", ButtonStyle.Ghost, () => Load(TouchControls.DefaultLayout()), 16),
                       new UButton("İPTAL", ButtonStyle.Ghost, CloseCurrent, 16),
                       new UButton("KAYDET", ButtonStyle.Primary, Save, 18).H(44));
            bar.Add(inner);
            hierarchy.Add(bar);

            var current = new TouchControls();
            current.ApplySaved(VexaSettings.TouchLayout);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            Load(current.Layout);

            _area.RegisterCallback<PointerMoveEvent>(OnMove);
            _area.RegisterCallback<PointerUpEvent>(e => { _drag = null; _area.ReleasePointer(e.pointerId); });
        }

        void Load(List<TouchControls.Btn> layout)
        {
            foreach (var it in _items) it.El.RemoveFromHierarchy();
            _items.Clear();
            foreach (var b in layout)
            {
                var it = new Item { Btn = b };
                var el = U.Box().Align(Align.Center).Justify(Justify.Center).Radius(8)
                    .Bg(Theme.WithAlpha(Theme.Ink, VexaSettings.TouchOpacity * 0.8f)).Border(2, Theme.WithAlpha(Theme.Accent, 0.7f));
                el.style.position = Position.Absolute;
                el.pickingMode = PickingMode.Position;
                el.Add(U.Head(b.Label, 18, null, Fonts.DisplayBold, 2));
                var handle = U.Box().Abs(null, null, -2, -2).Size(18, 18).Bg(Theme.Accent);
                handle.pickingMode = PickingMode.Position;
                el.Add(handle);
                el.RegisterCallback<PointerDownEvent>(e => Begin(it, e, false));
                handle.RegisterCallback<PointerDownEvent>(e => { Begin(it, e, true); e.StopPropagation(); });
                it.El = el;
                _items.Add(it);
                _area.Add(el);
            }
            Layout();
        }

        void Begin(Item it, PointerDownEvent e, bool resize)
        {
            _drag = it; _resize = resize;
            var p = this.WorldToLocal(e.position);
            float w = resolvedStyle.width, h = resolvedStyle.height;
            _grab = resize ? Vector2.zero : new Vector2(p.x / w - it.Btn.Norm.x, p.y / h - it.Btn.Norm.y);
            _area.CapturePointer(e.pointerId);
            it.El.BringToFront();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_drag == null) return;
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (w <= 0 || h <= 0) return;
            var p = this.WorldToLocal(e.position);
            var n = _drag.Btn.Norm;
            if (_resize)
            {
                n.width = Mathf.Clamp(p.x / w - n.x, 0.04f, Mathf.Min(0.3f, 1f - n.x));
                n.height = Mathf.Clamp(p.y / h - n.y, 0.05f, Mathf.Min(0.3f, 1f - n.y));
            }
            else
            {
                n.x = Mathf.Clamp(p.x / w - _grab.x, 0, 1 - n.width);
                n.y = Mathf.Clamp(p.y / h - _grab.y, 0, 1 - n.height);
            }
            _drag.Btn.Norm = n;
            Layout();
        }

        void Layout()
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0) return;
            foreach (var it in _items)
            {
                var n = it.Btn.Norm;
                it.El.style.left = n.x * w; it.El.style.top = n.y * h;
                it.El.style.width = n.width * w; it.El.style.height = n.height * h;
            }
        }

        void Save()
        {
            var list = new List<TouchControls.Btn>();
            foreach (var it in _items) list.Add(it.Btn);
            VexaSettings.TouchLayout = TouchControls.Serialize(list);
            VexaSettings.Save();
            GameSession.Current?.Input.Touch.ApplySaved(VexaSettings.TouchLayout);
            CloseCurrent();
        }
    }
}
