using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>
    /// In-game HUD: round bar, radar, money, kill feed, banners, crosshair, vitals, weapons, progress bars,
    /// damage direction, flash and scope overlays. Reads everything from <see cref="ClientGame"/> each frame.
    /// </summary>
    public sealed partial class HudView : VisualElement
    {
        private ClientGame _c;
        private GameSession _s;

        // top bar
        private readonly VisualElement _ctSlots, _tSlots, _scoreBoxCt, _scoreBoxT, _bombIcon;
        private readonly Label _scoreCt, _scoreT, _clock, _roundLabel;
        private readonly List<VisualElement> _slotPool = new List<VisualElement>();
        // left
        private readonly RadarView _radar;
        private readonly Label _money, _moneyDelta;
        private float _moneyDeltaUntil;
        private int _shownMoney = int.MinValue;
        public int LocalMoney { get; private set; }
        // kill feed
        private readonly VisualElement _feed;
        private readonly List<(VisualElement el, float born)> _feedItems = new List<(VisualElement, float)>();
        // center
        private readonly CrosshairView _crosshair;
        private readonly VisualElement _banner, _bannerBar;
        private readonly Label _bannerTitle, _bannerSub;
        private float _bannerUntil;
        private int _bannerPriority;
        private readonly VisualElement _roundEnd, _roundEndBand;
        private readonly Label _roundEndTitle, _roundEndReason, _roundEndMvp;
        private float _roundEndUntil;
        private readonly VisualElement _progress, _progressFill;
        private readonly Label _progressLabel, _hint, _toast;
        private float _toastUntil;
        private readonly VisualElement _deathCard;
        private readonly Label _deathTitle, _deathSub;
        private string _killedBy = "";
        // bottom
        private readonly VisualElement _vitals;
        private float _diedAt = -1;
        private readonly Label _hp, _armor, _armorTag, _kitTag, _clip, _reserve, _weaponName, _netStats;
        private readonly VisualElement _hpBar, _armorBar, _armorBlock, _inventory, _ammoBox;
        private readonly List<Label> _invRows = new List<Label>();
        // overlays
        private readonly VisualElement _flash, _scope, _scopeCenter, _damageLayer;
        private readonly List<(VisualElement el, float born)> _damageMarks = new List<(VisualElement, float)>();
        private static Texture2D _scopeTex;
        // mobile
        private readonly VisualElement _touchLayer;
        private readonly List<(VisualElement el, TouchControls.Btn btn)> _touchButtons = new List<(VisualElement, TouchControls.Btn)>();
        private readonly VisualElement _stickBase, _stickKnob;
        public System.Action MobilePause, MobileBuy, MobileScore;

        public HudView()
        {
            pickingMode = PickingMode.Ignore;
            this.Fill();

            // ---------- overlays at the back ----------
            _damageLayer = U.Box().Fill();
            _scope = BuildScope();
            hierarchy.Add(_scope);
            hierarchy.Add(_damageLayer);

            // ---------- top bar ----------
            var top = U.Box().CenterX(14);
            var bar = U.Row().Align(Align.Stretch);
            _ctSlots = U.Row(4).Align(Align.Center).Pad(0, 10).Justify(Justify.FlexEnd).W(186);
            _tSlots = U.Row(4).Align(Align.Center).Pad(0, 10).W(186);
            _scoreBoxCt = ScoreBox(Theme.CT, out _scoreCt);
            _scoreBoxT = ScoreBox(Theme.T, out _scoreT);
            var clockBox = U.Col().Align(Align.Center).Justify(Justify.Center).W(124).Bg(Theme.PanelStrong).Pad(6, 0);
            var clockRow = U.Row(6).Align(Align.Center);
            _bombIcon = BombGlyph();
            _clock = U.Head("0:00", 32);
            clockRow.Kids(_bombIcon, _clock);
            _roundLabel = U.Head("RAUND 1", 13, Theme.Muted, Fonts.DisplayBold, 2);
            clockBox.Kids(clockRow, _roundLabel);
            bar.Kids(_ctSlots, _scoreBoxCt, clockBox, _scoreBoxT, _tSlots);
            top.Add(bar);
            hierarchy.Add(top);

            // ---------- radar + money ----------
            _radar = new RadarView(250);
            _radar.Abs(20, 20);
            hierarchy.Add(_radar);
            var moneyRow = U.Row(10).Abs(22, 280).Align(Align.Center);
            _money = U.Head("$0", 30, Theme.Accent).Outline(0.6f);
            _moneyDelta = U.Head("", 22, Theme.Accent, Fonts.DisplayBold).Outline(0.6f);
            moneyRow.Kids(_money, _moneyDelta);
            hierarchy.Add(moneyRow);

            // ---------- kill feed ----------
            _feed = U.Col(4).Abs(null, 20, 20).Align(Align.FlexEnd);
            hierarchy.Add(_feed);

            // ---------- banners ----------
            var bannerWrap = U.Box().CenterX(118);
            _banner = U.Col().Align(Align.Center).Bg(Theme.PanelStrong).Pad(10, 28);
            _bannerBar = _banner;
            _bannerTitle = U.Head("", 28, null, Fonts.Display, 4);
            _bannerSub = U.Text("", 15, Fonts.Body, Theme.Muted);
            _banner.Kids(_bannerTitle, _bannerSub);
            bannerWrap.Add(_banner);
            _banner.Show(false);
            hierarchy.Add(bannerWrap);

            var reWrap = U.Box().CenterX(190);
            _roundEnd = U.Col().Align(Align.Center).W(720);
            _roundEndBand = U.Box().Align(Align.Center).Pad(12, 40).Self(Align.Stretch);
            _roundEndTitle = U.Head("", 64, Theme.Ink, Fonts.Display, 6);
            _roundEndBand.Add(_roundEndTitle);
            var reInfo = U.Col(4).Align(Align.Center).Bg(Theme.PanelStrong).Pad(12, 24).Self(Align.Stretch);
            _roundEndReason = U.Head("", 22, Theme.Text, Fonts.DisplayBold, 3);
            _roundEndMvp = U.Text("", 17, Fonts.BodySemi, Theme.Accent);
            reInfo.Kids(_roundEndReason, _roundEndMvp);
            _roundEnd.Kids(_roundEndBand, reInfo);
            _roundEnd.Show(false);
            reWrap.Add(_roundEnd);
            hierarchy.Add(reWrap);

            // ---------- crosshair ----------
            _crosshair = new CrosshairView();
            _crosshair.style.left = new Length(50, LengthUnit.Percent);
            _crosshair.style.top = new Length(50, LengthUnit.Percent);
            hierarchy.Add(_crosshair);

            // ---------- progress / hints / toast / death ----------
            _progress = U.Col(6).Align(Align.Center).W(400);
            _progressLabel = U.Head("", 18, null, Fonts.DisplayBold, 3).Outline(0.5f);
            var track = U.Box().H(8).Self(Align.Stretch).Bg(Theme.Panel);
            _progressFill = U.Box().Abs(0, 0, null, 0).Bg(Theme.CT);
            track.Add(_progressFill);
            _progress.Kids(_progressLabel, track);
            var progWrap = U.Box().CenterX(null, null);
            progWrap.style.top = new Length(62, LengthUnit.Percent);
            progWrap.Add(_progress);
            _progress.Show(false);
            hierarchy.Add(progWrap);

            var hintWrap = U.Box().CenterX(null, 170);
            _hint = U.Text("", 18, Fonts.BodySemi).Bg(Theme.Panel).Pad(6, 16);
            hintWrap.Add(_hint);
            hierarchy.Add(hintWrap);

            var toastWrap = U.Box().CenterX(null, 220);
            _toast = U.Text("", 17, Fonts.BodySemi, Theme.Text).Bg(Theme.PanelStrong).Pad(8, 18).BorderLeft(3, Theme.Danger);
            toastWrap.Add(_toast);
            _toast.Show(false);
            hierarchy.Add(toastWrap);

            var deathWrap = U.Box().CenterX(null, 120);
            _deathCard = U.Col(4).Align(Align.Center).Bg(Theme.PanelStrong).Pad(14, 32).BorderTop(3, Theme.Danger);
            _deathTitle = U.Head("ÖLDÜN", 30, Theme.Text, Fonts.Display, 4);
            _deathSub = U.Text("", 17, Fonts.BodySemi, Theme.Muted);
            _deathCard.Kids(_deathTitle, _deathSub);
            deathWrap.Add(_deathCard);
            _deathCard.Show(false);
            hierarchy.Add(deathWrap);

            // ---------- vitals ----------
            var vitals = U.Row(2).Abs(20, null, null, 20).Align(Align.Stretch);
            _vitals = vitals;
            var hpBlock = VitalBlock(out _hp, out _hpBar, HealthIcon(), 176);
            _armorBlock = VitalBlock(out _armor, out _armorBar, ArmorIcon(), 156);
            _armorTag = U.Head("KASK", 13, Theme.Muted, Fonts.DisplayBold, 2).Abs(null, 10, 14);
            _armorBlock.Add(_armorTag);
            _kitTag = U.Head("KİT", 17, Theme.CT, Fonts.DisplayBold, 2);
            var kitBox = U.Row().Align(Align.Center).Bg(Theme.Panel).Pad(0, 16);
            kitBox.Add(_kitTag);
            vitals.Kids(hpBlock, _armorBlock, kitBox);
            hierarchy.Add(vitals);

            // ---------- weapons + ammo ----------
            var wp = U.Col(8).Abs(null, null, 20, 20).Align(Align.FlexEnd);
            _inventory = U.Col(3).Align(Align.FlexEnd);
            for (int i = 0; i < 5; i++)
            {
                var row = U.Text("", 17, Fonts.DisplayBold).Pad(3, 12).Spacing(1);
                _invRows.Add(row);
                _inventory.Add(row);
            }
            _ammoBox = U.Col().Align(Align.FlexEnd).Bg(Theme.Panel).Pad(8, 18, 10, 18);
            _weaponName = U.Head("", 15, Theme.Muted, Fonts.DisplayBold, 2);
            var ammoRow = U.Row(10).Align(Align.FlexEnd);
            _clip = U.Head("", 56, Theme.Text);
            _reserve = U.Head("", 26, Theme.Muted, Fonts.DisplaySemi);
            _reserve.style.marginBottom = 6;
            ammoRow.Kids(_clip, _reserve);
            _ammoBox.Kids(_weaponName, ammoRow);
            wp.Kids(_inventory, _ammoBox);
            hierarchy.Add(wp);

            // ---------- net stats ----------
            var netWrap = U.Box().CenterX(null, 8);
            _netStats = U.Text("", 12, Fonts.Body, Theme.WithAlpha(Theme.Text, 0.75f)).Bg(Theme.WithAlpha(Theme.Ink, 0.55f)).Pad(3, 10).Spacing(1);
            netWrap.Add(_netStats);
            hierarchy.Add(netWrap);

            // ---------- flash (on top of the HUD, like the real thing) ----------
            _flash = U.Box().Fill().Bg(Color.white).Opacity(0);
            hierarchy.Add(_flash);

            // ---------- mobile ----------
            _touchLayer = U.Box().Fill();
            _stickBase = U.Box().Size(130, 130).Radius(65).Bg(Theme.WithAlpha(Color.white, 0.10f)).Border(2, Theme.WithAlpha(Color.white, 0.25f));
            _stickBase.style.position = Position.Absolute;
            _stickKnob = U.Box().Size(56, 56).Radius(28).Bg(Theme.WithAlpha(Color.white, 0.45f));
            _stickKnob.style.position = Position.Absolute;
            _touchLayer.Kids(_stickBase, _stickKnob);
            var mobileBar = U.Row(6).Abs(290, 20);
            mobileBar.Kids(MobileButton("MENÜ", () => MobilePause?.Invoke()), MobileButton("SATIN AL", () => MobileBuy?.Invoke()), MobileButton("SKOR", () => MobileScore?.Invoke()), MobileButton("SOHBET", () => OpenChat(false)));
            _touchLayer.Add(mobileBar);
            hierarchy.Add(_touchLayer);
            _touchLayer.Show(false);
            BuildExtras();
        }

        // ================= building blocks =================

        static VisualElement ScoreBox(Color c, out Label score)
        {
            var b = U.Box().W(64).Bg(Theme.Panel).BorderBottom(3, c).Align(Align.Center).Justify(Justify.Center);
            score = U.Head("0", 38, c);
            b.Add(score);
            return b;
        }

        static VisualElement BombGlyph()
        {
            var g = U.Box().Size(16, 14).Border(2, Theme.Danger);
            g.Add(U.Box().Abs(3, -6).Size(6, 4).BorderTop(2, Theme.Danger).BorderLeft(2, Theme.Danger).BorderRight(2, Theme.Danger));
            g.Add(U.Box().Abs(5, 2).Size(2, 6).Bg(Theme.Danger));
            return g;
        }

        static VisualElement HealthIcon()
        {
            var g = U.Box().Size(22, 22);
            g.Add(U.Box().Abs(8, 0).Size(6, 22).Bg(Theme.Text));
            g.Add(U.Box().Abs(0, 8).Size(22, 6).Bg(Theme.Text));
            return g;
        }

        static VisualElement ArmorIcon()
        {
            var g = U.Box().Size(20, 23).Border(2.5f, Theme.Text);
            g.style.borderBottomLeftRadius = g.style.borderBottomRightRadius = 10;
            return g;
        }

        static VisualElement VitalBlock(out Label value, out VisualElement fill, VisualElement icon, float minWidth)
        {
            var b = U.Col(6).Bg(Theme.Panel).Pad(10, 18, 12, 18).MinW(minWidth);
            var row = U.Row(12).Align(Align.Center);
            value = U.Head("100", 50);
            row.Kids(icon, value);
            var track = U.Box().H(4).Bg(Theme.WithAlpha(Color.white, 0.12f));
            fill = U.Box().Abs(0, 0, null, 0).Bg(Theme.Text);
            track.Add(fill);
            b.Kids(row, track);
            return b;
        }

        static UButton MobileButton(string text, System.Action a)
        {
            var b = new UButton(text, ButtonStyle.Ghost, a, 16);
            b.H(44);
            return b;
        }

        static VisualElement BuildScope()
        {
            if (_scopeTex == null)
            {
                const int N = 512;
                _scopeTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                float r = N * 0.5f - 2, c = (N - 1) * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        // soft vignette inside the glass, solid black outside
                        float a = d > r ? 1f : Mathf.Clamp01((d - r * 0.82f) / (r * 0.18f)) * 0.55f;
                        px[y * N + x] = new Color32(0, 0, 0, (byte)(a * 255));
                    }
                _scopeTex.SetPixels32(px);
                _scopeTex.Apply();
            }
            var root = U.Row().Fill();
            var left = U.Box().Grow().Bg(Color.black);
            var right = U.Box().Grow().Bg(Color.black);
            var center = U.Box().HPct(100).NoShrink();
            center.style.backgroundImage = new StyleBackground(_scopeTex);
            root.Kids(left, center, right);
            root.Add(U.Box().Abs(0, null, 0).H(1.5f).Bg(Color.black).Self(Align.Center));
            var hLine = root[root.childCount - 1];
            hLine.style.top = new Length(50, LengthUnit.Percent);
            var vLine = U.Box().Abs(null, 0, null, 0).W(1.5f).Bg(Color.black);
            vLine.style.left = new Length(50, LengthUnit.Percent);
            root.Add(vLine);
            root.Show(false);
            root.userData = center;
            return root;
        }

        // ================= binding =================

        public void Bind(GameSession s)
        {
            if (_s == s && _c == s?.Client) return;
            if (_c != null)
            {
                _c.Killed -= OnKill; _c.Hit -= OnHit; _c.RoundEnded -= OnRoundEnd; _c.GameEventReceived -= OnEvent;
                _c.ChatReceived -= OnChat;
            }
            _s = s; _c = s?.Client;
            ClearTransient();
            ClearExtras();
            if (_c == null) return;
            _c.Killed += OnKill; _c.Hit += OnHit; _c.RoundEnded += OnRoundEnd; _c.GameEventReceived += OnEvent;
            _c.ChatReceived += OnChat;
        }

        void ClearTransient()
        {
            foreach (var (el, _) in _feedItems) el.RemoveFromHierarchy();
            _feedItems.Clear();
            foreach (var (el, _) in _damageMarks) el.RemoveFromHierarchy();
            _damageMarks.Clear();
            _bannerUntil = _roundEndUntil = _toastUntil = 0;
            _shownMoney = int.MinValue;
            _killedBy = "";
        }

        public void Toast(string text, Color? accent = null, float seconds = 2.2f)
        {
            _toast.text = text;
            _toast.style.borderLeftColor = accent ?? Theme.Danger;
            _toastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Show a center banner. A lower-priority banner never replaces a higher one that is still up.</summary>
        public void Banner(string title, string sub, Color color, float seconds = 3f, int priority = 0)
        {
            if (Time.unscaledTime < _bannerUntil && priority < _bannerPriority) return;
            _bannerPriority = priority;
            _bannerTitle.text = title;
            _bannerSub.text = sub ?? "";
            _bannerSub.Show(!string.IsNullOrEmpty(sub));
            _bannerBar.BorderTop(3, color);
            _bannerUntil = Time.unscaledTime + seconds;
        }

        // ================= events =================

        void OnKill(KillEvent k)
        {
            if (_s != null && _s.Seeking) return;
            var c = _c;
            var row = U.Row(10).Align(Align.Center).Bg(Theme.Panel).Pad(5, 12);
            bool mine = k.Killer == c.LocalId || k.Victim == c.LocalId;
            if (mine) row.Border(2, k.Victim == c.LocalId ? Theme.Danger : Theme.Accent);
            bool world = k.Killer == 0 || k.Killer == k.Victim;
            if (!world) row.Add(U.Text(c.NameOf(k.Killer), 16, Fonts.BodySemi, Theme.TeamColor(c.TeamOf(k.Killer))));
            if (k.AttackerBlind && !world) row.Add(Tag("KÖR", false));
            string weapon = k.Grenade != GrenadeType.None ? GrenadeShort(k.Grenade) : (Weapons.Get(k.Weapon)?.Name ?? "");
            if (k.Weapon == WeaponId.C4) weapon = "C4";
            row.Add(U.Head(weapon.ToUpperInvariant(), 15, Theme.Text, Fonts.DisplayBold, 1));
            if (k.ThroughSmoke) row.Add(Tag("SİS", false));
            if (k.Wallbang) row.Add(Tag("DUVAR", false));
            if (k.Headshot) row.Add(Tag("KAFA", true));
            row.Add(U.Text(c.NameOf(k.Victim), 16, Fonts.BodySemi, Theme.TeamColor(c.TeamOf(k.Victim))));
            row.Fade(0.6f);
            _feed.Add(row);
            _feedItems.Add((row, Time.unscaledTime));
            while (_feedItems.Count > 6) { _feedItems[0].el.RemoveFromHierarchy(); _feedItems.RemoveAt(0); }

            if (k.Victim == c.LocalId)
            {
                _killedBy = world ? "Kendi hatan" : $"{c.NameOf(k.Killer)} · {weapon}{(k.Headshot ? " · kafadan" : "")}{(k.Wallbang ? " · duvardan" : "")}";
            }
        }

        static VisualElement Tag(string text, bool solid)
        {
            var t = U.Head(text, 12, solid ? Theme.Ink : Theme.Text, Fonts.Display, 1).Pad(1, 5);
            if (solid) t.Bg(Theme.Text); else t.Border(1, Theme.Text);
            return t;
        }

        static string GrenadeShort(GrenadeType g)
        {
            switch (g)
            {
                case GrenadeType.HE: return "HE";
                case GrenadeType.Molotov: return "Molotof";
                case GrenadeType.Incendiary: return "Yangın";
                case GrenadeType.Flash: return "Flaş";
                case GrenadeType.Smoke: return "Sis";
                case GrenadeType.Decoy: return "Dekoy";
                default: return "";
            }
        }

        void OnHit(HitEvent h)
        {
            if (_s != null && _s.Seeking) return;
            if (h.Victim != _c.LocalId || h.Attacker == _c.LocalId || h.Attacker == 0) return;
            if (!_c.TryGetRemotePose(h.Attacker, out var pose)) return;
            var d = pose.Position - _c.Predicted.Position;
            float worldYaw = Mathf.Atan2(d.X, d.Z) * Mathf.Rad2Deg;
            var pivot = U.Box().Size(0, 0);
            pivot.style.left = new Length(50, LengthUnit.Percent);
            pivot.style.top = new Length(50, LengthUnit.Percent);
            pivot.style.position = Position.Absolute;
            var arc = U.Box().Abs(-70, -190).Size(140, 7).Bg(Theme.Danger);
            arc.style.borderTopLeftRadius = arc.style.borderTopRightRadius = 7;
            pivot.Add(arc);
            pivot.userData = worldYaw;
            _damageLayer.Add(pivot);
            _damageMarks.Add((pivot, Time.unscaledTime));
            if (_damageMarks.Count > 6) { _damageMarks[0].el.RemoveFromHierarchy(); _damageMarks.RemoveAt(0); }
        }

        void OnRoundEnd(ClientGame.RoundEndInfo info)
        {
            if (_s != null && _s.Seeking) return;
            string team = info.Winner == Team.T ? "SALDIRANLAR KAZANDI" : info.Winner == Team.CT ? "SAVUNANLAR KAZANDI" : "BERABERE";
            _roundEndTitle.text = team;
            _roundEndBand.Bg(Theme.TeamColor(info.Winner));
            string reason;
            switch (info.Reason)
            {
                case RoundEndReason.Elimination: reason = "RAKİP TAKIM YOK EDİLDİ"; break;
                case RoundEndReason.BombExploded: reason = "BOMBA PATLADI"; break;
                case RoundEndReason.BombDefused: reason = "BOMBA İMHA EDİLDİ"; break;
                case RoundEndReason.TimeExpired: reason = "SÜRE DOLDU"; break;
                default: reason = ""; break;
            }
            _roundEndReason.text = reason;
            string mvp = "";
            if (info.MvpId != 0)
            {
                string why = info.MvpReason == 1 ? "bombayı kurdu" : info.MvpReason == 2 ? "bombayı imha etti" : $"{info.MvpKills} öldürme";
                mvp = $"★ RAUNDUN OYUNCUSU: {_c.NameOf(info.MvpId)} — {why}";
            }
            _roundEndMvp.text = mvp;
            _roundEndMvp.Show(mvp.Length > 0);
            _roundEndUntil = Time.unscaledTime + 5f;
            _bannerUntil = 0;
        }

        void OnEvent(GameEvent e)
        {
            if (_s != null && _s.Seeking) return;
            var c = _c;
            switch (e.Type)
            {
                case GameEventType.RoundStart:
                    _roundEndUntil = 0;
                    Banner("RAUND " + e.A, c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual ? "Satın alma süresi" : null, Theme.Accent, 2f);
                    break;
                case GameEventType.BombPlanted:
                    Banner($"BOMBA {e.Text} BÖLGESİNE KURULDU", "40 saniye içinde patlayacak", Theme.Danger, 3.5f, 1);
                    break;
                case GameEventType.BombDefused:
                    Banner("BOMBA İMHA EDİLDİ", null, Theme.CT, 3f, 1);
                    break;
                case GameEventType.BombDropped:
                    if (c.LocalTeam == Team.T) Toast("Bomba yere düştü", Theme.T);
                    break;
                case GameEventType.BombPickedUp:
                    if (e.A == c.LocalId) Toast("Bomba sende — bölgeye götür", Theme.T);
                    break;
                case GameEventType.Halftime:
                    Banner(string.IsNullOrEmpty(e.Text) ? "DEVRE ARASI" : e.Text, "Takımlar taraf değiştiriyor", Theme.Accent, 4f, 2);
                    break;
                case GameEventType.Message:
                    Banner(e.Text, null, Theme.Accent, 4f, 2);
                    break;
                case GameEventType.Purchase:
                    SetMoney(e.B, true);
                    break;
                case GameEventType.PurchaseDenied:
                    Toast(string.IsNullOrEmpty(e.Text) ? "Satın alınamadı" : e.Text);
                    break;
            }
        }

        void SetMoney(int money, bool fromPurchase)
        {
            if (_shownMoney != int.MinValue && money != _shownMoney)
            {
                int delta = money - _shownMoney;
                _moneyDelta.text = (delta > 0 ? "+" : "-") + U.Money(Mathf.Abs(delta));
                _moneyDelta.style.color = delta > 0 ? Theme.Accent : Theme.Danger;
                _moneyDeltaUntil = Time.unscaledTime + 1.6f;
            }
            _shownMoney = money;
            LocalMoney = money;
            _money.text = U.Money(money);
        }

        // ================= per frame =================

        public void Tick(float dt)
        {
            var c = _c;
            if (c == null || !c.Welcomed) return;
            var s = _s;
            var st = c.Predicted;
            float now = Time.unscaledTime;
            bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;

            // ---- top bar ----
            _scoreBoxCt.Show(rounds); _scoreBoxT.Show(rounds); _ctSlots.Show(rounds); _tSlots.Show(rounds);
            _scoreCt.text = c.ScoreCT.ToString();
            _scoreT.text = c.ScoreT.ToString();
            if (rounds) UpdateSlots();
            bool planted = c.Bomb.State == BombState.Planted;
            _bombIcon.Show(planted);
            if (planted) _bombIcon.style.opacity = Mathf.Repeat(now, 1f) < 0.5f ? 1f : 0.35f;
            switch (c.Header.Phase)
            {
                case GamePhase.Warmup: _clock.text = U.Clock(c.PhaseTimeLeft); _roundLabel.text = "ISINMA"; break;
                case GamePhase.Freeze: _clock.text = U.Clock(c.PhaseTimeLeft); _roundLabel.text = "HAZIRLIK · RAUND " + c.Round; break;
                case GamePhase.Live:
                    _clock.text = planted ? "BOMBA" : (c.Mode == GameMode.Practice ? "∞" : U.Clock(c.PhaseTimeLeft));
                    _roundLabel.text = rounds ? "RAUND " + c.Round + " / " + c.MaxRounds : (c.Mode == GameMode.Deathmatch ? DmLabel() : "ANTRENMAN");
                    break;
                case GamePhase.RoundEnd: _clock.text = "0:00"; _roundLabel.text = "RAUND SONU"; break;
                case GamePhase.MatchOver: _clock.text = "—"; _roundLabel.text = "MAÇ BİTTİ"; break;
            }
            _clock.style.color = planted ? Theme.Danger : (c.Header.Phase == GamePhase.Live && c.PhaseTimeLeft < 10 && c.Mode != GameMode.Practice ? Theme.Danger : Theme.Text);

            // ---- radar & money ----
            _radar.Tick(c, s.Input.Yaw);
            if (c.Scores.TryGetValue(c.LocalId, out var me) && me.Money >= 0 && me.Money != _shownMoney) SetMoney(me.Money, false);
            _money.Show(rounds);
            _moneyDelta.Show(rounds && now < _moneyDeltaUntil);

            // ---- kill feed fade ----
            for (int i = _feedItems.Count - 1; i >= 0; i--)
            {
                float age = now - _feedItems[i].born;
                if (age > 8f) { _feedItems[i].el.RemoveFromHierarchy(); _feedItems.RemoveAt(i); }
                else if (age > 7f) _feedItems[i].el.style.opacity = 0f;
            }

            // ---- banners ----
            _banner.Show(now < _bannerUntil && now >= _roundEndUntil);
            _roundEnd.Show(now < _roundEndUntil);
            _toast.Show(now < _toastUntil);

            // ---- crosshair ----
            bool scoped = st.Alive && st.Zoom > 0 && st.ActiveDef.Category == WeaponCategory.Sniper;
            bool showCross = st.Alive && !scoped;
            _crosshair.Show(st.Alive);          // stays visible while scoped so the hit marker still shows
            _crosshair.SetBarsVisible(showCross);
            if (showCross)
            {
                _crosshair.SetStyle(VexaSettings.Crosshair);
                var def = st.ActiveDef;
                float extra = def.IsGun ? Mathf.Clamp(WeaponLogic.CurrentInaccuracy(st, def, c.PlayerTime) * 0.08f, 0f, 24f) : 0f;
                _crosshair.Layout(extra);
            }
            float hit = Mathf.Clamp01((s.HitMarkerUntil - now) / 0.15f);
            bool kill = now < s.HitMarkerKillUntil;
            _crosshair.SetHit(kill ? 1f : hit, kill);

            // ---- scope ----
            _scope.Show(scoped);
            if (scoped && _scope.userData is VisualElement center)
            {
                float h = resolvedStyle.height;
                if (h > 0) center.style.width = h;
            }

            // ---- flash ----
            float pt = c.PlayerTime;
            float flash = 0;
            if (pt < st.FlashFullEndTime) flash = 1f;
            else if (pt < st.FlashEndTime) flash = Mathf.Clamp01((st.FlashEndTime - pt) / Mathf.Max(0.1f, st.FlashEndTime - st.FlashFullEndTime));
            _flash.style.opacity = flash * 0.97f;

            // ---- damage direction ----
            for (int i = _damageMarks.Count - 1; i >= 0; i--)
            {
                var (el, born) = _damageMarks[i];
                float age = now - born;
                if (age > 1.4f) { el.RemoveFromHierarchy(); _damageMarks.RemoveAt(i); continue; }
                float rel = (float)el.userData - s.Input.Yaw;
                el.style.rotate = new Rotate(new Angle(rel));
                el.style.opacity = 1f - age / 1.4f;
            }

            // ---- vitals (the watched player's while spectating) ----
            RemoteState tp = default;
            bool spec = s.Spectating && c.TryGetRemotePose(s.Spectate.Target, out tp);
            if (spec)
            {
                st.Health = tp.Health; st.Armor = tp.Armor; st.Helmet = tp.Helmet; st.HasKit = tp.HasKit; st.HasC4 = tp.HasC4;
            }
            _vitals.Show(st.Alive || spec);
            int hp = Mathf.Max(0, st.Health);
            _hp.text = hp.ToString();
            _hp.style.color = hp <= 25 ? Theme.Danger : Theme.Text;
            _hpBar.style.width = new Length(Mathf.Clamp(hp, 0, 100), LengthUnit.Percent);
            _hpBar.style.backgroundColor = hp <= 25 ? Theme.Danger : Theme.Text;
            _armor.text = st.Armor.ToString();
            _armorBar.style.width = new Length(Mathf.Clamp(st.Armor, 0, 100), LengthUnit.Percent);
            _armorBar.style.backgroundColor = Theme.Muted;
            _armorTag.Show(st.Helmet && st.Armor > 0);
            _kitTag.parent.Show(st.HasKit || st.HasC4);
            _kitTag.text = st.HasC4 ? "C4" : "KİT";
            _kitTag.style.color = st.HasC4 ? Theme.T : Theme.CT;

            // ---- weapons ----
            UpdateInventory(st);
            var adef = st.ActiveDef;
            var slot = st.ActiveSlot;
            _weaponName.text = (adef?.Name ?? "").ToUpperInvariant() + (st.Reloading ? " · DOLDURULUYOR" : "");
            if (adef != null && adef.IsGun)
            {
                _clip.text = slot.Clip.ToString();
                _reserve.text = "/ " + slot.Reserve;
                _reserve.Show(true);
                _clip.style.color = slot.Clip <= Mathf.Max(1, adef.ClipSize / 5) ? Theme.Danger : Theme.Text;
            }
            else if (st.Active == WeaponSlotKind.Grenade)
            {
                _clip.text = st.GrenadeCount(st.ActiveGrenade).ToString();
                _clip.style.color = Theme.Text;
                _reserve.Show(false);
                _weaponName.text = Items.GrenadeName(st.ActiveGrenade).ToUpperInvariant();
            }
            else { _clip.text = ""; _reserve.Show(false); }
            _ammoBox.Show(st.Alive || spec);
            _inventory.Show(st.Alive && !spec);
            if (spec)
            {
                _weaponName.text = ((Weapons.Get(tp.Weapon)?.Name ?? "") + (tp.Reloading ? " · DOLDURULUYOR" : "")).ToUpperInvariant();
                _clip.text = ""; _reserve.Show(false);
            }

            // ---- progress (plant / defuse) ----
            float prog = -1; string progText = ""; Color progColor = Theme.CT;
            if (st.Planting) { prog = Mathf.Clamp01((pt - st.PlantStartTime) / WeaponLogic.PlantTime); progText = "BOMBA KURULUYOR"; progColor = Theme.T; }
            else if (c.Bomb.DefuserId != 0 && (c.Bomb.DefuserId == c.LocalId || c.LocalTeam == Team.CT) && c.Bomb.State == BombState.Planted)
            {
                prog = c.DefuseProgress;
                progText = c.Bomb.DefuserId == c.LocalId ? (st.HasKit ? "İMHA EDİLİYOR · KİT" : "İMHA EDİLİYOR") : c.NameOf(c.Bomb.DefuserId).ToUpperInvariant() + " İMHA EDİYOR";
            }
            _progress.Show(prog >= 0);
            if (prog >= 0)
            {
                _progressLabel.text = progText;
                _progressFill.style.width = new Length(prog * 100f, LengthUnit.Percent);
                _progressFill.style.backgroundColor = progColor;
            }

            // ---- hint ----
            string hint = HintFor(c, st);
            _hint.text = hint;
            _hint.Show(hint.Length > 0 && prog < 0);

            // ---- death card ----
            bool dead = !st.Alive && c.Header.Phase != GamePhase.Warmup || (!st.Alive && c.Mode == GameMode.Deathmatch);
            if (!dead || c.LocalTeam == Team.None) _diedAt = -1;
            else if (_diedAt < 0) _diedAt = now;
            // the death card stays a moment, then spectating takes over
            bool showDeath = dead && c.LocalTeam != Team.None && c.Header.Phase != GamePhase.MatchOver && (!s.Spectating || now - _diedAt < 2.5f);
            _deathCard.Show(showDeath);
            if (dead)
            {
                _deathTitle.text = "ÖLDÜN";
                string next = c.Mode == GameMode.Deathmatch || c.Mode == GameMode.Practice ? "Yeniden doğuluyor..." : "Sonraki raundu bekliyorsun";
                _deathSub.text = (_killedBy.Length > 0 ? _killedBy + "  ·  " : "") + next;
            }
            else _killedBy = st.Alive ? "" : _killedBy;

            // ---- net stats ----
            _netStats.Show(VexaSettings.ShowNetStats);
            if (VexaSettings.ShowNetStats)
                _netStats.text = $"{Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))} FPS · {c.PingMs} ms · {c.TickRate} tick · düzeltme {c.Mispredictions}";

            TickExtras(dt, spec && !showDeath);

            // ---- mobile ----
            bool mobile = s.Input.MobileControls && !s.IsDemo;
            _touchLayer.Show(mobile);
            if (mobile) UpdateTouch(s);
        }

        string DmLabel()
        {
            int mine = 0, best = 0;
            foreach (var e in _c.Scores.Values) { best = Mathf.Max(best, e.Kills); if (e.Id == _c.LocalId) mine = e.Kills; }
            return $"SEN {mine} · LİDER {best}";
        }

        string HintFor(ClientGame c, in PlayerState st)
        {
            if (!st.Alive) return "";
            bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
            if (c.Header.Phase == GamePhase.Freeze && c.CanBuyNow) return "[B] SATIN AL";
            if (c.Header.Phase == GamePhase.Warmup && rounds) return "Isınma — maç birazdan başlıyor";
            if (st.HasC4 && c.Header.Phase == GamePhase.Live && c.Map.SiteAt(st.Position) != null)
                return st.Active == WeaponSlotKind.Bomb ? "Sol tık basılı tut: BOMBAYI KUR" : "[5] Bombayı seç ve kur";
            if (c.LocalTeam == Team.CT && c.Bomb.State == BombState.Planted && c.Bomb.DefuserId != c.LocalId
                && System.Numerics.Vector3.Distance(c.Bomb.Position, st.Position) < 1.6f)
                return "[E] basılı tut: BOMBAYI İMHA ET";
            foreach (var it in c.WorldItems)
                if (System.Numerics.Vector3.Distance(it.Position, st.Position) < 1.6f)
                    return "[E] " + (Weapons.Get(it.Weapon)?.Name ?? "Silah") + " al";
            return "";
        }

        void UpdateSlots()
        {
            var ct = new List<(int id, bool alive, int hp, bool mate)>();
            var t = new List<(int id, bool alive, int hp, bool mate)>();
            var c = _c;
            void AddP(int id, Team team, bool alive, int hp)
            {
                bool mate = team == c.LocalTeam;
                if (team == Team.CT) ct.Add((id, alive, hp, mate));
                else if (team == Team.T) t.Add((id, alive, hp, mate));
            }
            AddP(c.LocalId, c.LocalTeam, c.Predicted.Alive, c.Predicted.Health);
            foreach (var r in c.Remotes)
            {
                if (c.TryGetRemotePose(r.Id, out var pose)) AddP(r.Id, pose.Team, pose.Alive, pose.Health);
                else AddP(r.Id, r.Team, false, 0);
            }
            ct.Sort((a, b) => a.id.CompareTo(b.id));
            t.Sort((a, b) => a.id.CompareTo(b.id));
            int used = 0;
            Fill(_ctSlots, ct, Theme.CT, ref used, true);
            Fill(_tSlots, t, Theme.T, ref used, false);
            for (int i = used; i < _slotPool.Count; i++) _slotPool[i].Show(false);
        }

        void Fill(VisualElement parent, List<(int id, bool alive, int hp, bool mate)> list, Color col, ref int used, bool reverse)
        {
            int n = Mathf.Min(list.Count, 5);
            for (int k = 0; k < n; k++)
            {
                var p = list[reverse ? n - 1 - k : k];
                if (used >= _slotPool.Count)
                {
                    var el = U.Box().Size(30, 40);
                    el.Add(U.Box().Abs(0, null, 0, 0));
                    _slotPool.Add(el);
                }
                var slot = _slotPool[used++];
                if (slot.parent != parent) parent.Add(slot);
                slot.BringToFront();
                slot.Show(true);
                slot.Bg(p.alive ? Theme.Panel : Theme.WithAlpha(Theme.Ink, 0.45f));
                slot.BorderBottom(3, p.alive ? col : Theme.WithAlpha(Color.white, 0.15f));
                var fill = slot[0];
                fill.style.height = new Length(p.alive && p.mate ? Mathf.Clamp(p.hp, 0, 100) : (p.alive ? 100 : 0), LengthUnit.Percent);
                fill.style.backgroundColor = Theme.WithAlpha(col, p.mate ? 0.35f : 0.18f);
                slot.style.marginLeft = k == 0 ? 0 : 4;
            }
        }

        void UpdateInventory(PlayerState st)
        {
            int i = 0;
            void Row(string text, string key, bool active)
            {
                if (i >= _invRows.Count) return;
                var r = _invRows[i++];
                r.text = text + "   " + key;
                r.Show(true);
                r.style.backgroundColor = active ? Theme.PanelStrong : Color.clear;
                r.style.color = active ? Theme.Text : Theme.WithAlpha(Theme.Text, 0.55f);
                r.style.borderRightWidth = 3;
                r.style.borderRightColor = active ? Theme.Accent : Color.clear;
            }
            if (!st.Primary.IsEmpty) Row(Weapons.Get(st.Primary.Id).Name.ToUpperInvariant(), "1", st.Active == WeaponSlotKind.Primary);
            if (!st.Secondary.IsEmpty) Row(Weapons.Get(st.Secondary.Id).Name.ToUpperInvariant(), "2", st.Active == WeaponSlotKind.Secondary);
            Row("BIÇAK", "3", st.Active == WeaponSlotKind.Melee);
            if (st.GrenadeTotal > 0)
            {
                var parts = new List<string>();
                void G(GrenadeType g, string n) { int k = st.GrenadeCount(g); if (k > 0) parts.Add(k > 1 ? n + " ×" + k : n); }
                G(GrenadeType.HE, "HE"); G(GrenadeType.Flash, "FLAŞ"); G(GrenadeType.Smoke, "SİS");
                G(GrenadeType.Molotov, "MOLOTOF"); G(GrenadeType.Incendiary, "YANGIN"); G(GrenadeType.Decoy, "DEKOY");
                Row(string.Join(" · ", parts), "4", st.Active == WeaponSlotKind.Grenade);
            }
            if (st.HasC4) Row("C4", "5", st.Active == WeaponSlotKind.Bomb);
            for (; i < _invRows.Count; i++) _invRows[i].Show(false);
        }

        void UpdateTouch(GameSession s)
        {
            var tc = s.Input.Touch;
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (w <= 0 || h <= 0) return;
            if (_touchButtons.Count != tc.Layout.Count)
            {
                foreach (var (old, _) in _touchButtons) old.RemoveFromHierarchy();
                _touchButtons.Clear();
                foreach (var b in tc.Layout)
                {
                    var el = U.Box().Align(Align.Center).Justify(Justify.Center).Radius(8).Border(1, Theme.WithAlpha(Color.white, 0.25f));
                    el.style.position = Position.Absolute;
                    el.Add(U.Head(b.Label, 18, Theme.Text, Fonts.DisplayBold, 2));
                    _touchLayer.Insert(0, el);
                    _touchButtons.Add((el, b));
                }
            }
            float alpha = VexaSettings.TouchOpacity;
            for (int i = 0; i < _touchButtons.Count; i++)
            {
                var el = _touchButtons[i].el;
                var b = tc.Layout[i]; // read live: the layout editor can change it
                el.style.left = b.Norm.x * w; el.style.top = b.Norm.y * h;
                el.style.width = b.Norm.width * w; el.style.height = b.Norm.height * h;
                bool on = b.Button != Buttons.None && (tc.Buttons & b.Button) != 0;
                el.style.backgroundColor = on ? Theme.WithAlpha(Theme.Accent, 0.45f * alpha + 0.2f) : Theme.WithAlpha(Theme.Ink, 0.55f * alpha);
                el.style.opacity = Mathf.Clamp01(alpha + 0.2f);
            }
            _stickBase.Show(tc.StickActive); _stickKnob.Show(tc.StickActive);
            if (tc.StickActive)
            {
                float sx = w / Screen.width, sy = h / Screen.height;
                _stickBase.style.left = tc.StickCenter.x * sx - 65; _stickBase.style.top = (Screen.height - tc.StickCenter.y) * sy - 65;
                _stickKnob.style.left = tc.StickPos.x * sx - 28; _stickKnob.style.top = (Screen.height - tc.StickPos.y) * sy - 28;
            }
        }
    }
}
