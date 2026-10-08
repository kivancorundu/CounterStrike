using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>HUD parts for chat, spectating, team names and tournament status.</summary>
    public sealed partial class HudView
    {
        // chat
        private VisualElement _chatFeed, _chatInputRow;
        private Label _chatPrefix;
        private TextField _chatField;
        private readonly List<(Label line, float born)> _chatLines = new List<(Label, float)>();
        private bool _chatTeam;
        public bool ChatOpen { get; private set; }

        // spectating
        private VisualElement _specPanel;
        private Label _specName, _specInfo, _specHint;

        // tournament
        private Label _nameCt, _nameT, _tourStrip;

        void BuildExtras()
        {
            // ---- chat (bottom left, above the vitals) ----
            var chatWrap = U.Col(4).Abs(20, null, null, 140).W(560);
            _chatFeed = U.Col(2);
            chatWrap.Add(_chatFeed);
            _chatInputRow = U.Row(8).Align(Align.Center).Bg(Theme.PanelStrong).Pad(4, 10).Margin(6, 0, 0, 0);
            _chatPrefix = U.Head("HERKES:", 16, Theme.Accent, Fonts.DisplayBold, 1);
            _chatField = new TextField { maxLength = 120 };
            _chatField.style.flexGrow = 1;
            _chatField.style.marginLeft = _chatField.style.marginRight = _chatField.style.marginTop = _chatField.style.marginBottom = 0;
            _chatField.style.unityFontDefinition = FontDefinition.FromFont(Fonts.BodySemi);
            _chatField.style.fontSize = 16;
            var input = _chatField.Q(className: TextField.inputUssClassName) ?? _chatField;
            input.Bg(Color.clear).Border(0, Color.clear);
            input.style.color = Theme.Text;
            _chatField.RegisterCallback<KeyDownEvent>(OnChatKey, TrickleDown.TrickleDown);
            var send = new UButton("GÖNDER", ButtonStyle.Ghost, SendChat, 15);
            send.H(34);
            _chatInputRow.Kids(_chatPrefix, _chatField, send);
            _chatInputRow.Show(false);
            chatWrap.Add(_chatInputRow);
            hierarchy.Add(chatWrap);

            // ---- spectator panel (bottom center) ----
            var specWrap = U.Box().CenterX(null, 118);
            _specPanel = U.Col(2).Align(Align.Center).Bg(Theme.PanelStrong).Pad(10, 28).BorderTop(3, Theme.Accent);
            _specPanel.Add(U.Eyebrow("İZLENİYOR"));
            _specName = U.Head("", 30, null, Fonts.Display, 2);
            _specInfo = U.Text("", 15, Fonts.BodySemi, Theme.Muted);
            _specHint = U.Text("Sol / sağ tık: oyuncu değiştir · Boşluk: kamera", 13, Fonts.Body, Theme.Dim);
            _specPanel.Kids(_specName, _specInfo, _specHint);
            _specPanel.Show(false);
            specWrap.Add(_specPanel);
            hierarchy.Add(specWrap);

            // ---- team names under the scores ----
            _nameCt = TeamNameLabel(Theme.CT);
            _nameT = TeamNameLabel(Theme.T);
            _scoreBoxCt.Add(_nameCt);
            _scoreBoxT.Add(_nameT);

            // ---- tournament status strip ----
            var stripWrap = U.Box().CenterX(108);
            _tourStrip = U.Text("", 16, Fonts.BodySemi).Bg(Theme.PanelStrong).Pad(7, 18).BorderLeft(3, Theme.Accent);
            _tourStrip.Show(false);
            stripWrap.Add(_tourStrip);
            hierarchy.Add(stripWrap);
        }

        static Label TeamNameLabel(Color c)
        {
            var l = U.Head("", 13, c, Fonts.DisplayBold, 2).Outline(0.5f);
            l.style.position = Position.Absolute;
            l.style.top = new Length(100, LengthUnit.Percent);
            l.style.left = -80; l.style.right = -80;
            l.style.marginTop = 4;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            return l;
        }

        void ClearExtras()
        {
            foreach (var (l, _) in _chatLines) l.RemoveFromHierarchy();
            _chatLines.Clear();
            CloseChat();
        }

        // ================= chat =================

        public void OpenChat(bool team)
        {
            if (_c == null || (_s != null && _s.IsDemo)) return;
            _chatTeam = team;
            _chatPrefix.text = team ? "TAKIM:" : "HERKES:";
            _chatPrefix.style.color = team ? Theme.TeamColor(_c.LocalTeam) : Theme.Accent;
            ChatOpen = true;
            _chatInputRow.Show(true);
            _chatField.SetValueWithoutNotify("");
            // focus next frame so the key that opened chat isn't typed into it
            _chatField.schedule.Execute(() => { _chatField.SetValueWithoutNotify(""); _chatField.Focus(); }).ExecuteLater(30);
        }

        public void CloseChat()
        {
            ChatOpen = false;
            _chatInputRow?.Show(false);
            _chatField?.Blur();
        }

        void OnChatKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                SendChat();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseChat();
                e.StopPropagation();
            }
        }

        void SendChat()
        {
            var text = _chatField.value;
            if (!string.IsNullOrWhiteSpace(text)) _c?.SendChat(text.Trim(), _chatTeam);
            CloseChat();
        }

        static string Esc(string s) => (s ?? "").Replace("<", "<​");
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        void OnChat(ChatMessage m)
        {
            if (_s != null && _s.Seeking) return;
            string line;
            if (m.SenderId == 0) line = $"<color=#{Hex(Theme.Accent)}>SUNUCU:</color> {Esc(m.Text)}";
            else
            {
                string tags = (m.Dead ? "*ÖLÜ* " : "") + (m.TeamOnly ? "(TAKIM) " : "");
                line = $"<color=#{Hex(Theme.Muted)}>{tags}</color><color=#{Hex(Theme.TeamColor(m.SenderTeam))}>{Esc(_c.NameOf(m.SenderId))}</color>: {Esc(m.Text)}";
            }
            var l = U.Text(line, 16, Fonts.BodySemi).Outline(0.6f);
            l.enableRichText = true;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.Fade(0.5f);
            _chatFeed.Add(l);
            _chatLines.Add((l, Time.unscaledTime));
            while (_chatLines.Count > 12) { _chatLines[0].line.RemoveFromHierarchy(); _chatLines.RemoveAt(0); }
        }

        // ================= per frame =================

        void TickExtras(float dt, bool showSpectator)
        {
            var c = _c;
            var s = _s;
            float now = Time.unscaledTime;

            // chat lines fade after 10 s (all recent ones show while typing)
            for (int i = 0; i < _chatLines.Count; i++)
            {
                var (l, born) = _chatLines[i];
                bool visible = ChatOpen ? i >= _chatLines.Count - 8 : now - born < 10f;
                l.style.opacity = visible ? 1f : 0f;
            }

            // spectator panel
            _specPanel.Show(showSpectator);
            if (showSpectator && c.TryGetRemotePose(s.Spectate.Target, out var p))
            {
                _specName.text = c.NameOf(s.Spectate.Target);
                _specName.style.color = Theme.TeamColor(p.Team);
                var w = Weapons.Get(p.Weapon)?.Name ?? "";
                _specInfo.text = $"{p.Health} CAN · {p.Armor} ZIRH{(p.Helmet && p.Armor > 0 ? " + KASK" : "")} · {w}{(s.Spectate.Mode == Spectator.ViewMode.Chase ? " · 3. ŞAHIS" : "")}";
                _specHint.text = s.IsDemo ? "← / →: oyuncu değiştir · C: kamera" : "Sol / sağ tık: oyuncu değiştir · Boşluk: kamera";
            }

            // team names (tournaments)
            bool names = (c.Flags & MatchFlags.Tournament) != 0;
            _nameCt.Show(names); _nameT.Show(names);
            if (names) { _nameCt.text = c.NameOfTeam(Team.CT); _nameT.text = c.NameOfTeam(Team.T); }

            // tournament status
            string strip = "";
            var f = c.Flags;
            if ((f & MatchFlags.WaitingReady) != 0)
                strip = $"HAZIRLIK · {c.ReadyCount}/{c.ReadyNeeded} oyuncu hazır — sohbete .ready yaz ya da Esc → HAZIRIM";
            else if ((f & MatchFlags.TechnicalPause) != 0) strip = "TEKNİK DURAKLATMA · devam için iki takım da .unpause yazmalı";
            else if ((f & MatchFlags.TacticalPause) != 0) strip = "TAKTİK MOLA";
            else if ((f & MatchFlags.SidePick) != 0)
                strip = c.LocalTeam == c.KnifeWinner ? "TARAFINI SEÇ · .stay (kal) ya da .switch (değiştir) — Esc menüsünde de var" : $"{c.NameOfTeam(c.KnifeWinner)} taraf seçiyor...";
            else if ((f & MatchFlags.KnifeRound) != 0) strip = "BIÇAK RAUNDU · kazanan taraf seçer";
            else if ((f & MatchFlags.PausePending) != 0) strip = "Mola istendi · sonraki donma süresinde başlayacak";
            _tourStrip.text = strip;
            _tourStrip.Show(strip.Length > 0);
        }
    }
}
