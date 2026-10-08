using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vexa.Client.UI
{
    /// <summary>Demo playback bar: play/pause, speed, round jumps and a seek track.</summary>
    public sealed class DemoControlsView : VisualElement
    {
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        private GameSession _s;
        private readonly UButton _play;
        private readonly Segmented _speed;
        private readonly Label _time, _title;
        private readonly VisualElement _track, _fill, _marks;
        private int _marksFor;
        public event Action Exit;

        public DemoControlsView()
        {
            pickingMode = PickingMode.Ignore;
            this.Fill();
            var bar = U.Col(10).CenterX(null, 18);
            var panel = U.Col(10).W(980).Bg(Theme.PanelStrong).Border(1, Theme.Line).Pad(12, 16);
            panel.pickingMode = PickingMode.Position;

            var top = U.Row(12).Align(Align.Center);
            _title = U.Head("DEMO", 18, Theme.Accent, Fonts.DisplayBold, 3);
            _time = U.Head("0:00 / 0:00", 20, null, Fonts.DisplayBold, 1);
            top.Kids(_title, U.Spacer(), _time);

            _track = new VisualElement { pickingMode = PickingMode.Position }.H(18);
            _track.style.justifyContent = Justify.Center;
            var rail = U.Box().H(6).Bg(Theme.LineStrong);
            _fill = U.Box().Abs(0, 0, null, 0).Bg(Theme.Accent);
            rail.Add(_fill);
            _marks = U.Box().Fill();
            _track.hierarchy.Add(rail);
            _track.hierarchy.Add(_marks);
            _track.RegisterCallback<PointerDownEvent>(e => SeekTo(e.localPosition.x));

            var buttons = U.Row(8).Align(Align.Center);
            var prev = new UButton("ÖNCEKİ RAUND", ButtonStyle.Ghost, () => _s?.SeekRound(-1), 16);
            _play = new UButton("DURAKLAT", ButtonStyle.Secondary, TogglePause, 18);
            _play.W(150);
            var next = new UButton("SONRAKİ RAUND", ButtonStyle.Ghost, () => _s?.SeekRound(1), 16);
            _speed = new Segmented(new[] { "0.25×", "0.5×", "1×", "2×", "4×" }, 2);
            _speed.W(340);
            _speed.Changed += i => { if (_s != null) _s.DemoSpeed = Speeds[i]; };
            var exit = new UButton("ÇIK", ButtonStyle.Danger, () => Exit?.Invoke(), 16);
            buttons.Kids(prev, _play, next, U.Spacer(), _speed, exit);

            var keys = U.Text("Boşluk: duraklat · ↑/↓: hız · PgUp/PgDn: raund · ←/→: oyuncu · C: kamera", 13, Fonts.Body, Theme.Dim);
            panel.Kids(top, _track, buttons, keys);
            bar.Add(panel);
            hierarchy.Add(bar);
        }

        public void Bind(GameSession s)
        {
            _s = s;
            _marksFor = -1;
            _speed.Set(Array.IndexOf(Speeds, s.DemoSpeed) is int i && i >= 0 ? i : 2, false);
        }

        void TogglePause() { if (_s != null) _s.DemoPaused = !_s.DemoPaused; }

        void SeekTo(float x)
        {
            if (_s?.Demo == null) return;
            float w = _track.resolvedStyle.width;
            if (w <= 1) return;
            var f = _s.Demo.File;
            _s.SeekDemo(f.StartTime + f.Duration * Mathf.Clamp01(x / w));
        }

        public void Tick()
        {
            var s = _s;
            if (s?.Demo == null) return;
            // keyboard
            if (PcInput.KeyDown(KeyCode.Space)) TogglePause();
            if (PcInput.KeyDown(KeyCode.UpArrow)) _speed.Set(Mathf.Min(_speed.Value + 1, Speeds.Length - 1), true);
            if (PcInput.KeyDown(KeyCode.DownArrow)) _speed.Set(Mathf.Max(_speed.Value - 1, 0), true);
            if (PcInput.KeyDown(KeyCode.PageDown)) s.SeekRound(1);
            if (PcInput.KeyDown(KeyCode.PageUp)) s.SeekRound(-1);
            if (PcInput.KeyDown(KeyCode.RightArrow)) s.Spectate.Next(1);
            if (PcInput.KeyDown(KeyCode.LeftArrow)) s.Spectate.Next(-1);
            if (PcInput.KeyDown(KeyCode.C)) s.Spectate.ToggleMode();

            var f = s.Demo.File;
            double el = Math.Max(0, s.Demo.Elapsed);
            _time.text = $"{Clock(el)} / {Clock(f.Duration)}";
            _title.text = $"DEMO · {(f.Map ?? "").ToUpperInvariant()} · RAUND {Mathf.Max(1, s.Client?.Round ?? 1)}";
            _play.Text = s.DemoPaused ? "OYNAT" : "DURAKLAT";
            float t = f.Duration > 0 ? (float)(el / f.Duration) : 0;
            _fill.style.width = new Length(Mathf.Clamp01(t) * 100f, LengthUnit.Percent);

            // round start ticks on the track
            if (_marksFor != f.Rounds.Count && f.Duration > 0)
            {
                _marksFor = f.Rounds.Count;
                _marks.Clear();
                foreach (var (time, _) in f.Rounds)
                {
                    float x = (float)((time - f.StartTime) / f.Duration) * 100f;
                    var m = U.Box().Abs(null, 2).Size(2, 14).Bg(Theme.WithAlpha(Theme.Text, 0.6f));
                    m.style.left = new Length(x, LengthUnit.Percent);
                    _marks.Add(m);
                }
            }
        }

        static string Clock(double seconds)
        {
            int s = (int)seconds;
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }
}
