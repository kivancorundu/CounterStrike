using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>Shared top bar with the logo and page navigation.</summary>
    public sealed class TopBar : VisualElement
    {
        public readonly List<UButton> Nav = new List<UButton>();
        private readonly Label _name;
        private readonly Label _initial;

        public TopBar(string[] pages, Action<int> onNav)
        {
            this.Abs(0, 0, 0).H(72).Dir(FlexDirection.Row).Align(Align.Center).Pad(0, 40).BorderBottom(1, Theme.Line).Bg(new Color(0.03f, 0.04f, 0.055f, 0.92f));
            var logo = U.Row(14).Align(Align.Center);
            logo.Kids(U.Diamond(18, Theme.Accent), U.Head("VEXA", 34, null, Fonts.Display, 6));
            var nav = U.Row(6).Margin(0, 0, 0, 40);
            for (int i = 0; i < pages.Length; i++)
            {
                int k = i;
                var b = new UButton(pages[i], ButtonStyle.Nav, () => onNav(k));
                Nav.Add(b);
                nav.Kids(b);
            }
            var chip = U.Row(14).Align(Align.Center).Pad(8, 14, 8, 8).Bg(Theme.Raised).Border(1, Theme.Line);
            _initial = U.Head("O", 22, Theme.Ink).Size(40, 40).Bg(Theme.T).TextAlign(TextAnchor.MiddleCenter);
            var who = U.Col();
            _name = U.Text("", 16, Fonts.BodyBold);
            who.Kids(_name, U.Text("Yerel profil", 13, Fonts.Body, Theme.Muted));
            chip.Kids(_initial, who);
            hierarchy.Add(logo); hierarchy.Add(nav); hierarchy.Add(U.Spacer()); hierarchy.Add(chip);
            Refresh();
        }

        public void Select(int i) { for (int k = 0; k < Nav.Count; k++) Nav[k].Selected = k == i; }

        public void Refresh()
        {
            _name.text = VexaSettings.Name;
            _initial.text = string.IsNullOrEmpty(VexaSettings.Name) ? "?" : VexaSettings.Name.Substring(0, 1).ToUpperInvariant();
        }
    }

    /// <summary>Main menu: home, play setup and settings. The 3D map flythrough renders behind it.</summary>
    public sealed class MainMenuView : VisualElement
    {
        private readonly TopBar _top;
        private readonly VisualElement _home, _play, _settingsPage, _leftShade;
        private readonly PlayPage _playPage;
        private readonly Label _error;
        private readonly SettingsView _settingsView = new SettingsView();

        public MainMenuView()
        {
            this.Fill();
            pickingMode = PickingMode.Position;
            _leftShade = U.Box().Abs(0, 0, null, 0).W(640).Bg(new Color(0.03f, 0.04f, 0.055f, 0.88f)).BorderRight(1, Theme.Line);
            hierarchy.Add(_leftShade);

            _home = BuildHome();
            _playPage = new PlayPage();
            _play = U.Box().Abs(0, 72, 0, 0).Bg(Theme.Ink);
            _play.Add(_playPage);
            _settingsPage = U.Box().Abs(0, 72, 0, 0).Bg(Theme.Ink).Pad(40, 64);
            _settingsPage.Add(_settingsView);
            hierarchy.Add(_home); hierarchy.Add(_play); hierarchy.Add(_settingsPage);

            _top = new TopBar(new[] { "ANA SAYFA", "OYNA", "AYARLAR" }, ShowPage);
            hierarchy.Add(_top);

            var errWrap = U.Box().CenterX(null, 40);
            _error = U.Text("", 16, Fonts.BodySemi).Bg(Theme.PanelStrong).Pad(10, 18).BorderLeft(3, Theme.Danger);
            errWrap.Add(_error);
            _error.Show(false);
            hierarchy.Add(errWrap);
            VexaSettings.Changed += _top.Refresh;
            ShowPage(0);
        }

        public void ShowPage(int page)
        {
            _top.Select(page);
            _home.Show(page == 0); _leftShade.Show(page == 0);
            _play.Show(page == 1);
            if (page == 2 && !_settingsPage.Visible()) _settingsView.Refresh();
            _settingsPage.Show(page == 2);
            if (page == 1) _playPage.Refresh();
        }

        public void ShowError(string msg)
        {
            _error.text = msg;
            _error.Show(!string.IsNullOrEmpty(msg));
        }

        VisualElement BuildHome()
        {
            var root = U.Box().Fill();
            var col = U.Col(10).Abs(72, 150).W(500);
            col.Add(U.Eyebrow("TAKTİKSEL 5'E 5 · ALFA", Theme.Accent));
            var title = U.Head("HER MERMİ\nSAYILIR.", 104, null, Fonts.Display, -1);
            title.style.whiteSpace = WhiteSpace.Normal;
            col.Add(title);
            var desc = U.Text("Sunucu otoriteli, 128 tick'e hazır rekabetçi atış. Ekonomi, bomba, el bombaları ve botlarla tam maç.", 18, Fonts.Body, Theme.Muted).W(440);
            desc.style.whiteSpace = WhiteSpace.Normal;
            col.Add(desc);
            var play = new UButton("OYNA", ButtonStyle.Primary, () => ShowPage(1), 36);
            play.W(360).H(76).Margin(28, 0, 0, 0);
            play.style.justifyContent = Justify.SpaceBetween;
            play.Add(Arrow(Theme.Ink));
            col.Add(play);
            var list = U.Col().Margin(18, 0, 0, 0);
            list.Add(new UButton("ANTRENMAN", ButtonStyle.MenuItem, () => { _playPage.PickMode(3); ShowPage(1); }).WithSub("BOTLARLA"));
            list.Add(new UButton("SUNUCUYA KATIL", ButtonStyle.MenuItem, () => ShowPage(1)).WithSub("IP / PORT"));
            list.Add(new UButton("AYARLAR", ButtonStyle.MenuItem, () => ShowPage(2)).WithSub("NİŞANGAH · GÖRÜNTÜ"));
            var quit = new UButton("ÇIKIŞ", ButtonStyle.MenuItem, Application.Quit);
            quit.style.borderBottomWidth = 0;
            list.Add(quit);
            col.Add(list);
            root.Add(col);

            var cards = U.Row(16).Abs(null, null, 64, 56);
            cards.Kids(InfoCard("TURNUVALAR", "rally.gg ile turnuva", "Topluluk turnuvaları için kayıt ve eşleşme entegrasyonu yakında.", Theme.Accent, 360),
                       InfoCard("YENİ HARİTA", "KASABA", "İki bomba bölgesi, orta koridor ve yeraltı geçidi.", Theme.Muted, 300));
            root.Add(cards);
            root.Add(U.Text("VEXA 0.1 ALFA · Yapım aşamasında", 13, Fonts.Body, Theme.Dim).Abs(72, null, null, 40));
            return root;
        }

        static VisualElement InfoCard(string eyebrow, string title, string body, Color eyebrowColor, float width)
        {
            var c = U.Col(6).W(width).Bg(Theme.PanelStrong).Border(1, Theme.Line).Pad(22, 24);
            var b = U.Text(body, 15, Fonts.Body, Theme.Muted);
            b.style.whiteSpace = WhiteSpace.Normal;
            c.Kids(U.Head(eyebrow, 15, eyebrowColor, Fonts.DisplayBold, 3), U.Head(title, 32), b);
            return c;
        }

        static VisualElement Arrow(Color c)
        {
            var a = U.Box().Size(28, 20);
            a.Add(U.Box().Abs(0, 8.5f).Size(24, 3).Bg(c));
            var head = U.Box().Abs(14, 2).Size(12, 12).BorderTop(3, c).BorderRight(3, c).Rotate(45);
            a.Add(head);
            return a;
        }
    }

    /// <summary>Mode / map / side / difficulty selection and the join-server form.</summary>
    public sealed class PlayPage : VisualElement
    {
        static readonly (GameMode mode, string tag, string name, string desc)[] Modes =
        {
            (GameMode.Competitive, "SIRALI · MR12", "REKABETÇİ", "5'e 5, 13 raund kazanan alır. Tam ekonomi, bomba, uzatmalar."),
            (GameMode.Casual, "RAHAT · MR8", "BASİT", "Kısa maç, ücretsiz zırh ve kit, dost ateşi kapalı."),
            (GameMode.Deathmatch, "ISINMA · 10 DK", "ÖLÜM MAÇI", "Anında yeniden doğ, her silah serbest. Nişan ısınması için."),
            (GameMode.Practice, "SERBEST", "ANTRENMAN", "Süresiz alan. Bomba atışları ve sprey kontrolü çalış."),
        };
        static readonly (string file, string name, string desc)[] Maps =
        {
            ("kasaba", "KASABA", "Bomba haritası · A ve B bölgesi · 5'e 5"),
            ("training", "EĞİTİM", "Hareket ve atış alanı · ölüm maçı / antrenman"),
        };

        private int _mode, _map;
        private readonly List<VisualElement> _modeCards = new List<VisualElement>();
        private readonly List<VisualElement> _mapCards = new List<VisualElement>();
        private readonly Label _sumMode, _sumMap;
        private readonly VisualElement _sumRows, _teamSizeRow, _dmBotsRow, _sideRow;
        private readonly Segmented _side, _difficulty, _teamSize, _dmBots, _tick;
        private readonly UInput _host, _port;

        public PlayPage()
        {
            this.Fill();
            var main = U.Col(28).Abs(64, 40, 520);

            var modeSec = U.Col(12);
            modeSec.Add(U.Eyebrow("01 · OYUN MODU"));
            var modeRow = U.Row(12);
            for (int i = 0; i < Modes.Length; i++)
            {
                int k = i;
                var m = Modes[i];
                var card = U.Col(4).Grow().W(0).H(176).Pad(20).OnClick(() => PickMode(k));
                card.Kids(U.Head(m.tag, 14, null, Fonts.DisplayBold, 3), U.Head(m.name, 34, null, Fonts.Display, 1));
                var d = U.Text(m.desc, 14, Fonts.Body);
                d.style.whiteSpace = WhiteSpace.Normal;
                d.style.marginTop = 6;
                card.Add(d);
                _modeCards.Add(card);
                modeRow.Kids(card);
            }
            modeSec.Add(modeRow);

            var mapSec = U.Col(12);
            mapSec.Add(U.Eyebrow("02 · HARİTA"));
            var mapRow = U.Row(12);
            for (int i = 0; i < Maps.Length; i++)
            {
                int k = i;
                var card = U.Col(2).Grow().W(0).H(140).Pad(20, 22).Justify(Justify.FlexEnd).OnClick(() => PickMap(k));
                card.Kids(U.Head(Maps[i].name, 40), U.Text(Maps[i].desc, 14, Fonts.Body, Theme.Muted));
                _mapCards.Add(card);
                mapRow.Kids(card);
            }
            mapSec.Add(mapRow);

            var opts = U.Row(28);
            _sideRow = U.Col(12).Grow().W(0);
            _side = new Segmented(new[] { "OTOMATİK", "SALDIRI · T", "SAVUNMA · CT" }, VexaSettings.LastSide, new[] { Theme.Muted, Theme.T, Theme.CT });
            _side.Changed += v => { VexaSettings.LastSide = v; VexaSettings.Save(); };
            _sideRow.Kids(U.Eyebrow("03 · TARAF"), _side);
            var diffCol = U.Col(12).Grow().W(0);
            _difficulty = new Segmented(new[] { "KOLAY", "NORMAL", "ZOR", "UZMAN" }, VexaSettings.LastDifficulty);
            _difficulty.Changed += v => { VexaSettings.LastDifficulty = v; VexaSettings.Save(); };
            diffCol.Kids(U.Eyebrow("04 · BOT ZORLUĞU"), _difficulty);
            opts.Kids(_sideRow, diffCol);

            _teamSizeRow = U.Col(12);
            _teamSize = new Segmented(new[] { "1'E 1", "2'YE 2", "3'E 3", "4'E 4", "5'E 5" }, Mathf.Clamp(VexaSettings.LastTeamSize, 1, 5) - 1);
            _teamSize.Changed += v => { VexaSettings.LastTeamSize = v + 1; VexaSettings.Save(); Refresh(); };
            _teamSizeRow.Kids(U.Eyebrow("05 · TAKIM BOYUTU"), _teamSize);
            _dmBotsRow = U.Col(12);
            int[] dmCounts = { 0, 3, 5, 7, 9 };
            int dmIdx = Array.IndexOf(dmCounts, VexaSettings.LastDmBots);
            _dmBots = new Segmented(new[] { "BOTSUZ", "3 BOT", "5 BOT", "7 BOT", "9 BOT" }, dmIdx < 0 ? 3 : dmIdx);
            _dmBots.Changed += v => { VexaSettings.LastDmBots = dmCounts[v]; VexaSettings.Save(); };
            _dmBotsRow.Kids(U.Eyebrow("05 · BOT SAYISI"), _dmBots);

            main.Kids(modeSec, mapSec, opts, _teamSizeRow, _dmBotsRow);
            hierarchy.Add(main);

            // ---- summary panel ----
            var side = U.Col(18).Abs(null, 0, 0, 0).W(456).Bg(Theme.Ink2).BorderLeft(1, Theme.Line).Pad(40);
            side.Add(U.Eyebrow("MAÇ ÖZETİ"));
            var sumTitle = U.Col();
            _sumMode = U.Head("", 56, null, Fonts.Display);
            _sumMap = U.Head("", 56, Theme.Accent, Fonts.Display);
            sumTitle.Kids(_sumMode, _sumMap);
            side.Add(sumTitle);
            _sumRows = U.Col();
            side.Add(_sumRows);
            var tickRow = U.Row(12).Align(Align.Center);
            _tick = new Segmented(new[] { "64 TICK", "128 TICK" }, VexaSettings.LastTick == 128 ? 1 : 0).W(260);
            _tick.Changed += v => { VexaSettings.LastTick = v == 1 ? 128 : 64; VexaSettings.Save(); };
            tickRow.Kids(U.Text("Sunucu", 15, Fonts.Body, Theme.Muted).W(70), _tick);
            side.Add(tickRow);
            side.Add(U.Spacer());
            var start = new UButton("MAÇI BAŞLAT", ButtonStyle.Primary, Start, 34);
            start.H(80);
            side.Add(start);
            var join = U.Col(10).BorderTop(1, Theme.Line).Pad(18, 0, 0, 0);
            join.Add(U.Head("SUNUCUYA KATIL", 15, Theme.Muted, Fonts.DisplayBold, 3));
            var jr = U.Row(8).Align(Align.FlexEnd);
            _host = new UInput("Adres", VexaSettings.LastHost, 64);
            _host.Grow();
            _port = new UInput("Port", VexaSettings.LastPort.ToString(), 5, 96);
            jr.Kids(_host, _port, new UButton("BAĞLAN", ButtonStyle.Secondary, Join).H(42));
            join.Add(jr);
            side.Add(join);
            hierarchy.Add(side);

            _mode = Mathf.Clamp(VexaSettings.LastMode, 0, Modes.Length - 1);
            _map = Mathf.Clamp(VexaSettings.LastMap, 0, Maps.Length - 1);
            Refresh();
        }

        public void PickMode(int i)
        {
            _mode = i;
            // the training range has no bomb sites: rounds modes default to kasaba
            if ((Modes[i].mode == GameMode.Competitive || Modes[i].mode == GameMode.Casual) && Maps[_map].file == "training") _map = 0;
            VexaSettings.LastMode = _mode; VexaSettings.LastMap = _map; VexaSettings.Save();
            Refresh();
        }

        void PickMap(int i)
        {
            _map = i;
            // no bomb sites on the training range: fall back to deathmatch there
            var m = Modes[_mode].mode;
            if (Maps[i].file == "training" && (m == GameMode.Competitive || m == GameMode.Casual)) _mode = 2;
            VexaSettings.LastMode = _mode;
            VexaSettings.LastMap = i; VexaSettings.Save();
            Refresh();
        }

        public void Refresh()
        {
            for (int i = 0; i < _modeCards.Count; i++) PaintCard(_modeCards[i], i == _mode, true);
            for (int i = 0; i < _mapCards.Count; i++) PaintCard(_mapCards[i], i == _map, false);
            var m = Modes[_mode].mode;
            bool rounds = m == GameMode.Competitive || m == GameMode.Casual;
            _teamSizeRow.Show(rounds);
            _dmBotsRow.Show(m == GameMode.Deathmatch);
            _sideRow.SetEnabled(m != GameMode.Deathmatch);
            _sumMode.text = Modes[_mode].name;
            _sumMap.text = Maps[_map].name;
            _sumRows.Clear();
            int ts = _teamSize.Value + 1;
            (string, string)[] rows;
            switch (m)
            {
                case GameMode.Competitive: rows = new[] { ("Format", "MR12 · 24 raund"), ("Takımlar", $"{ts}'e {ts} (botlar tamamlar)"), ("Başlangıç parası", "$800"), ("Raund süresi", "1:55"), ("Uzatma", "MR3 · $12 500") }; break;
                case GameMode.Casual: rows = new[] { ("Format", "MR8 · 15 raund"), ("Takımlar", $"{ts}'e {ts} (botlar tamamlar)"), ("Başlangıç parası", "$1 000"), ("Zırh ve kit", "Ücretsiz"), ("Dost ateşi", "Kapalı") }; break;
                case GameMode.Deathmatch: rows = new[] { ("Süre", "10 dakika"), ("Para", "Sınırsız"), ("Yeniden doğma", "2 sn"), ("Dost ateşi", "Kapalı") }; break;
                default: rows = new[] { ("Süre", "Sınırsız"), ("Para", "Sınırsız"), ("Satın alma", "Her yerde"), ("Botlar", "Yok") }; break;
            }
            foreach (var (k, v) in rows)
            {
                var r = U.Row().Justify(Justify.SpaceBetween).Pad(11, 0).BorderBottom(1, Theme.Line);
                r.Kids(U.Text(k, 16, Fonts.Body, Theme.Muted), U.Text(v, 16, Fonts.BodySemi));
                _sumRows.Add(r);
            }
        }

        static void PaintCard(VisualElement card, bool selected, bool invert)
        {
            if (invert && selected) card.Bg(Theme.Accent).Border(0, Color.clear);
            else card.Bg(Theme.Raised).Border(selected ? 2 : 1, selected ? Theme.Accent : Theme.Line);
            var labels = card.Query<Label>().ToList();
            for (int i = 0; i < labels.Count; i++)
            {
                Color col;
                if (invert && selected) col = Theme.Ink;
                else if (invert) col = i == 1 ? Theme.Text : Theme.Muted;
                else col = i == 0 ? Theme.Text : Theme.Muted;
                labels[i].style.color = col;
            }
        }

        void Start()
        {
            var m = Modes[_mode].mode;
            var setup = new GameSession.MatchSetup
            {
                Mode = m,
                Map = Maps[_map].file,
                Port = int.TryParse(_port.Value, out var port) ? port : 27015,
                TickRate = VexaSettings.LastTick,
                TeamSize = _teamSize.Value + 1,
                DeathmatchBots = VexaSettings.LastDmBots,
                BotDifficulty = new[] { 0.15f, 0.45f, 0.7f, 0.95f }[_difficulty.Value],
                Side = _side.Value == 1 ? Team.T : _side.Value == 2 ? Team.CT : Team.None,
                PlayerName = VexaSettings.Name,
            };
            GameSession.Host(setup);
        }

        void Join()
        {
            if (!int.TryParse(_port.Value, out var port)) port = 27015;
            VexaSettings.LastHost = _host.Value.Trim();
            VexaSettings.LastPort = port;
            VexaSettings.Save();
            GameSession.Join(VexaSettings.LastHost, port, VexaSettings.Name);
        }
    }

    /// <summary>Connecting / loading screen.</summary>
    public sealed class LoadingView : VisualElement
    {
        static readonly string[] Tips =
        {
            "Durmadan ateş etme: hareket ederken isabet ciddi şekilde düşer. Ateşten önce dur.",
            "Ters yöne kısa bir dokunuş (karşı adım) seni anında durdurur.",
            "Sis bombası molotofu söndürür; yangının üstüne sis at.",
            "Kitle bomba 5 saniyede, kitsiz 10 saniyede imha edilir.",
            "Kaybettiğin raundlar kayıp bonusunu artırır: ekonomini takımınla planla.",
            "Flaşa sırtını dönersen çok daha kısa süre kör olursun.",
        };
        private readonly Label _title, _mode, _status, _tip;
        private readonly VisualElement _bar;
        public event Action Cancel;

        public LoadingView()
        {
            this.Fill().Bg(Theme.Ink);
            pickingMode = PickingMode.Position;
            var col = U.Col(10).Abs(96, null, null, 120).W(900);
            _mode = U.Eyebrow("", Theme.Accent);
            _title = U.Head("", 140, null, Fonts.Display, -1);
            _status = U.Text("", 18, Fonts.BodySemi, Theme.Muted);
            var track = U.Box().H(4).W(420).Bg(Theme.Line).Margin(14, 0, 0, 0);
            track.style.overflow = Overflow.Hidden;
            _bar = U.Box().Abs(0, 0, null, 0).W(120).Bg(Theme.Accent);
            track.Add(_bar);
            _tip = U.Text("", 16, Fonts.Body, Theme.Muted).Margin(30, 0, 0, 0);
            _tip.style.whiteSpace = WhiteSpace.Normal;
            col.Kids(_mode, _title, _status, track, _tip);
            hierarchy.Add(col);
            var cancel = new UButton("İPTAL", ButtonStyle.Ghost, () => Cancel?.Invoke()).Abs(null, null, 64, 64);
            hierarchy.Add(cancel);
        }

        public void Open(GameSession s)
        {
            _tip.text = "İPUCU · " + Tips[UnityEngine.Random.Range(0, Tips.Length)];
        }

        public void Tick(GameSession s)
        {
            string map = string.IsNullOrEmpty(s.MapName) ? "SUNUCU" : s.MapName.ToUpperInvariant();
            _title.text = map == "TRAINING" ? "EĞİTİM" : map;
            string mode = s.Setup == null ? "ÇEVRİMİÇİ MAÇ" : s.Setup.Mode == GameMode.Competitive ? "REKABETÇİ" : s.Setup.Mode == GameMode.Casual ? "BASİT" : s.Setup.Mode == GameMode.Deathmatch ? "ÖLÜM MAÇI" : "ANTRENMAN";
            _mode.text = mode;
            _status.text = string.IsNullOrEmpty(s.Status) ? (s.Client != null && s.Client.Welcomed ? "Harita yükleniyor..." : "Bağlanılıyor...") : s.Status;
            float t = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f);
            _bar.style.left = Mathf.Lerp(-120, 420, t);
        }
    }

    /// <summary>In-game Esc menu.</summary>
    public sealed class PauseView : VisualElement
    {
        private readonly VisualElement _main, _settings, _teamRow;
        private readonly SettingsView _settingsView = new SettingsView();
        private readonly Label _info;
        public event Action Resume, Leave;
        public Action<Team> PickTeam;

        public PauseView()
        {
            this.Fill().Bg(Theme.Scrim);
            pickingMode = PickingMode.Position;
            _main = U.Col(10).Abs(96, 120).W(420);
            _main.Add(U.Eyebrow("MAÇ ARKADA DEVAM EDİYOR", Theme.Accent));
            _main.Add(U.Head("MENÜ", 96, null, Fonts.Display));
            var resume = new UButton("DEVAM ET", ButtonStyle.Primary, () => Resume?.Invoke());
            resume.Margin(18, 0, 0, 0);
            _main.Add(resume);
            _teamRow = U.Col(8).Margin(16, 0, 0, 0);
            var tr = U.Row(8);
            tr.Kids(TeamButton("SALDIRI · T", Theme.T, Team.T), TeamButton("SAVUNMA · CT", Theme.CT, Team.CT));
            _teamRow.Kids(U.Eyebrow("TAKIM DEĞİŞTİR"), tr);
            _main.Add(_teamRow);
            _main.Add(new UButton("AYARLAR", ButtonStyle.Ghost, () => ShowSettings(true)).Margin(16, 0, 0, 0));
            _main.Add(new UButton("MAÇTAN AYRIL", ButtonStyle.Danger, () => Leave?.Invoke()).Margin(8, 0, 0, 0));
            _info = U.Text("", 15, Fonts.Body, Theme.Muted).Margin(24, 0, 0, 0);
            _info.style.whiteSpace = WhiteSpace.Normal;
            _main.Add(_info);
            hierarchy.Add(_main);

            _settings = U.Col(20).Abs(64, 64, 64, 64);
            var head = U.Row(16).Align(Align.Center);
            head.Kids(new UButton("GERİ", ButtonStyle.Ghost, () => ShowSettings(false)), U.Head("AYARLAR", 48, null, Fonts.Display, 2));
            _settings.Kids(head, _settingsView);
            hierarchy.Add(_settings);
            ShowSettings(false);
        }

        UButton TeamButton(string text, Color c, Team t)
        {
            var b = new UButton(text, ButtonStyle.Ghost, () => PickTeam?.Invoke(t)).Grow();
            b.Label.style.color = c;
            b.W(0);
            return b;
        }

        public void ShowSettings(bool on)
        {
            if (on && !_settings.Visible()) _settingsView.Refresh();
            _settings.Show(on); _main.Show(!on);
        }
        public bool SettingsOpen => _settings.Visible();

        public void Open(ClientGame c)
        {
            ShowSettings(false);
            bool rounds = c != null && (c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual);
            _teamRow.Show(rounds);
            _info.text = c == null ? "" : $"Ping {c.PingMs} ms · {c.TickRate} tick sunucu · Raund sırasında takım değiştirirsen ölürsün ve yeni takımında sonraki raundda doğarsın.";
        }
    }

    /// <summary>Victory / defeat screen.</summary>
    public sealed class MatchEndView : VisualElement
    {
        private readonly Label _result, _score, _sub;
        private readonly VisualElement _band, _players;
        public event Action Leave;

        public MatchEndView()
        {
            this.Fill().Bg(new Color(0.02f, 0.03f, 0.05f, 0.9f));
            pickingMode = PickingMode.Position;
            var col = U.Col(18).CenterX(140);
            _band = U.Col().Align(Align.Center).Pad(18, 80);
            _result = U.Head("", 120, Theme.Ink, Fonts.Display, 10);
            _band.Add(_result);
            _score = U.Head("", 64, null, Fonts.Display, 4);
            _sub = U.Text("", 17, Fonts.BodySemi, Theme.Muted);
            _players = U.Row(16).Margin(20, 0, 0, 0);
            var leave = new UButton("ANA MENÜ", ButtonStyle.Primary, () => Leave?.Invoke());
            leave.W(320).Margin(30, 0, 0, 0);
            col.Kids(_band, _score, _sub, _players, leave);
            hierarchy.Add(col);
        }

        public void Open(ClientGame c)
        {
            bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
            var entries = rounds ? c.Scores.Values.OrderByDescending(e => e.Score).ThenByDescending(e => e.Kills).ToList()
                                 : c.Scores.Values.OrderByDescending(e => e.Kills).ThenBy(e => e.Deaths).ToList(); // deathmatch winner = most kills
            if (rounds)
            {
                int mine = c.LocalTeam == Team.T ? c.ScoreT : c.ScoreCT, theirs = c.LocalTeam == Team.T ? c.ScoreCT : c.ScoreT;
                bool draw = mine == theirs, win = mine > theirs;
                _result.text = draw ? "BERABERE" : win ? "ZAFER" : "MAĞLUBİYET";
                _band.Bg(draw ? Theme.Muted : win ? Theme.Accent : Theme.Danger);
                _score.text = $"{mine} : {theirs}";
                _sub.text = $"{(c.Map?.Name ?? "").ToUpperInvariant()} · {c.History.Count} raund";
            }
            else
            {
                var best = entries.FirstOrDefault();
                bool me = best.Id == c.LocalId;
                _result.text = me ? "BİRİNCİSİN" : "MAÇ BİTTİ";
                _band.Bg(me ? Theme.Accent : Theme.Muted);
                _score.text = entries.Count > 0 ? $"{c.NameOf(best.Id)} · {best.Kills} öldürme" : "";
                _sub.text = "Ölüm maçı";
            }
            _players.Clear();
            for (int i = 0; i < Mathf.Min(3, entries.Count); i++)
            {
                var e = entries[i];
                var card = U.Col(4).W(240).Bg(Theme.Raised).Border(1, i == 0 ? Theme.Accent : Theme.Line).Pad(18, 20);
                card.Kids(U.Head(i == 0 ? "★ MAÇIN OYUNCUSU" : "#" + (i + 1), 14, i == 0 ? Theme.Accent : Theme.Muted, Fonts.DisplayBold, 3),
                          U.Head(c.NameOf(e.Id), 30, Theme.TeamColor(e.Team), Fonts.Display),
                          U.Text($"{e.Kills} Ö · {e.Assists} A · {e.Deaths} Ö(D) · {e.Score} puan", 15, Fonts.Body, Theme.Muted));
                _players.Kids(card);
            }
        }
    }
}
