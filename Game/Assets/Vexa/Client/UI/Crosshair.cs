using UnityEngine;
using UnityEngine.UIElements;

namespace Vexa.Client.UI
{
    /// <summary>
    /// Crosshair built from four bars (+ optional dot) centered on its own origin.
    /// Sizes are in "crosshair units"; <see cref="Scale"/> converts to UI pixels (2 at 1080p).
    /// </summary>
    public sealed class CrosshairView : VisualElement
    {
        private readonly VisualElement[] _bars = new VisualElement[5];
        private readonly VisualElement[] _hit = new VisualElement[4];
        private CrosshairStyle _style;
        private float _extraGap = -1;
        public float Scale = 2f;

        public CrosshairView()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.width = 0; style.height = 0;
            for (int i = 0; i < _bars.Length; i++) { _bars[i] = U.Box(); _bars[i].style.position = Position.Absolute; hierarchy.Add(_bars[i]); }
            // hit marker: four short diagonal ticks
            for (int i = 0; i < 4; i++)
            {
                var pivot = U.Box().Abs(0, 0).Rotate(45 + 90 * i);
                var tick = U.Box().Abs(-1.5f, 8).Size(3, 9).Bg(Color.white);
                pivot.Add(tick);
                _hit[i] = pivot;
                hierarchy.Add(pivot);
            }
            SetStyle(VexaSettings.Crosshair, true);
            SetHit(0, false);
        }

        public void SetStyle(CrosshairStyle s, bool force = false)
        {
            if (!force && s.Equals(_style)) return;
            _style = s;
            var col = s.Color;
            var outline = new Color(0, 0, 0, 0.9f);
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i].Bg(col);
                float o = s.Outline * Scale * 0.5f;
                _bars[i].Border(o, o > 0 ? outline : Color.clear);
            }
            _extraGap = -1;
            Layout(0);
        }

        /// <summary>Extra gap from movement/firing (dynamic crosshair), in crosshair units.</summary>
        public void Layout(float extraGap)
        {
            if (Mathf.Abs(extraGap - _extraGap) < 0.05f) return;
            _extraGap = extraGap;
            var s = _style;
            float k = Scale, o = s.Outline * k * 0.5f;
            float L = s.Length * k, T = Mathf.Max(1f, s.Thickness * k), G = (s.Gap + 4f + (s.Dynamic ? extraGap : 0)) * k * 0.5f;
            void Bar(int i, float x, float y, float w, float h, bool show)
            {
                var b = _bars[i];
                b.Show(show && w > 0 && h > 0);
                b.style.left = x - o; b.style.top = y - o;
                b.style.width = w + 2 * o; b.style.height = h + 2 * o;
            }
            Bar(0, G, -T / 2, L, T, L > 0);
            Bar(1, -G - L, -T / 2, L, T, L > 0);
            Bar(2, -T / 2, G, T, L, L > 0);
            Bar(3, -T / 2, -G - L, T, L, L > 0 && !s.TStyle);
            Bar(4, -T / 2, -T / 2, T, T, s.Dot);
        }

        private bool _barsVisible = true;

        public void SetBarsVisible(bool on)
        {
            if (on == _barsVisible) return;
            _barsVisible = on;
            foreach (var b in _bars) b.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
        }

        public void SetHit(float alpha, bool kill)
        {
            var c = kill ? Theme.Danger : Color.white;
            c.a = alpha;
            foreach (var h in _hit)
            {
                h.Show(alpha > 0.01f);
                if (alpha > 0.01f) h[0].style.backgroundColor = c;
            }
        }
    }
}
