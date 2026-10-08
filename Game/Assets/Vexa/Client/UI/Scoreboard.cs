using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>Tab scoreboard: both teams with K/A/D, MVPs, HS%, ADR, score, money (own team only) and round history.</summary>
    public sealed class ScoreboardView : VisualElement
    {
        static readonly float[] ColW = { 0, 90, 56, 56, 56, 56, 70, 70, 70 };
        static readonly string[] ColNames = { "", "PARA", "Ö", "A", "Ö(D)", "MVP", "KAFA%", "ORT.H", "PUAN" };

        private ClientGame _c;
        private readonly Label _scoreCt, _scoreT, _title, _sub;
        private readonly VisualElement _tableCt, _tableT, _tableDm, _history, _ctWrap, _tWrap;
        private readonly Label _ctHeadName, _tHeadName;
        private float _nextRefresh;

        public ScoreboardView()
        {
            this.Fill().Bg(new Color(0.02f, 0.03f, 0.05f, 0.78f));
            pickingMode = PickingMode.Ignore; // display only: taps pass through to the HUD buttons below
            var wrap = U.Col(14).CenterX(60);
            var inner = U.Col(14).W(1180);
            wrap.Add(inner);

            var header = U.Row(24).Align(Align.Center);
            _scoreCt = U.Head("0", 72, Theme.CT);
            _scoreT = U.Head("0", 72, Theme.T);
            var mid = U.Col(2).Grow().Align(Align.Center);
            _title = U.Head("", 30, null, Fonts.Display, 4);
            _sub = U.Text("", 14, Fonts.Body, Theme.Muted);
            mid.Kids(_title, _sub);
            header.Kids(_scoreCt, mid, _scoreT);
            inner.Add(header);

            _ctWrap = Table(Theme.CT, "SAVUNMA · CT", out _tableCt, out _ctHeadName);
            _history = U.Row(3).Justify(Justify.Center).Align(Align.Center).Pad(8, 0);
            _tWrap = Table(Theme.T, "SALDIRI · T", out _tableT, out _tHeadName);
            var dmWrap = Table(Theme.Accent, "OYUNCULAR", out _tableDm, out _);
            _tableDm.userData = dmWrap;
            inner.Kids(_ctWrap, _history, _tWrap, dmWrap);
            inner.Add(U.Text("Ö: öldürme · A: asist · Ö(D): ölüm · ORT.H: raund başına ortalama hasar · rakip parası gizli", 13, Fonts.Body, Theme.Muted).Self(Align.Center));
            hierarchy.Add(wrap);
        }

        static VisualElement Table(Color col, string title, out VisualElement body, out Label nameHead)
        {
            var t = U.Col();
            var head = U.Row().Pad(8, 16).BorderBottom(2, col).Bg(Theme.WithAlpha(col, 0.10f));
            for (int i = 0; i < ColW.Length; i++)
            {
                var l = U.Head(i == 0 ? title : ColNames[i], 15, col, Fonts.DisplayBold, 2);
                if (i == 0) l.Grow(); else l.W(ColW[i]).TextAlign(TextAnchor.MiddleRight);
                head.Add(l);
            }
            nameHead = (Label)head[0];
            body = U.Col();
            t.Kids(head, body);
            return t;
        }

        public void Bind(ClientGame c) { _c = c; _nextRefresh = 0; }

        public void Tick()
        {
            var c = _c;
            if (c == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            bool rounds = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
            _scoreCt.text = c.ScoreCT.ToString(); _scoreT.text = c.ScoreT.ToString();
            _scoreCt.Show(rounds); _scoreT.Show(rounds);
            string mode = c.Mode == GameMode.Competitive ? "REKABETÇİ" : c.Mode == GameMode.Casual ? "BASİT" : c.Mode == GameMode.Deathmatch ? "ÖLÜM MAÇI" : "ANTRENMAN";
            _title.text = ((c.Map?.Name ?? "").ToUpperInvariant() + " · " + mode).Trim(' ', '·');
            _sub.text = rounds ? $"Raund {Mathf.Max(1, c.Round)} / {c.MaxRounds} · {c.WinRounds} raund kazanan alır"
                               : c.Mode == GameMode.Deathmatch ? "Süre bitince en çok öldüren kazanır" : "Serbest antrenman";
            _ctWrap.Show(rounds); _tWrap.Show(rounds); _history.Show(rounds);
            bool tour = (c.Flags & MatchFlags.Tournament) != 0;
            _ctHeadName.text = tour ? c.NameOfTeam(Team.CT).ToUpperInvariant() + " · CT" : "SAVUNMA · CT";
            _tHeadName.text = tour ? c.NameOfTeam(Team.T).ToUpperInvariant() + " · T" : "SALDIRI · T";
            ((VisualElement)_tableDm.userData).Show(!rounds);

            int played = Mathf.Max(1, c.History.Count);
            var entries = c.Scores.Values.ToList();
            if (!entries.Any(e => e.Id == c.LocalId))
                entries.Add(new ScoreEntry { Id = c.LocalId, Team = c.LocalTeam, Alive = c.Predicted.Alive, Money = -1 });
            entries.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Kills.CompareTo(a.Kills));
            if (rounds)
            {
                Rows(_tableCt, entries.Where(e => e.Team == Team.CT), Theme.CT, played);
                Rows(_tableT, entries.Where(e => e.Team == Team.T), Theme.T, played);
                History();
            }
            else Rows(_tableDm, entries, Theme.Accent, played);
        }

        void Rows(VisualElement body, IEnumerable<ScoreEntry> list, Color col, int played)
        {
            var c = _c;
            body.Clear();
            foreach (var e in list)
            {
                bool me = e.Id == c.LocalId;
                var row = U.Row().Align(Align.Center).Pad(9, 16).BorderBottom(1, Theme.WithAlpha(Color.white, 0.06f));
                row.Bg(me ? Theme.AccentSoft : Theme.WithAlpha(Theme.Ink2, 0.85f));
                if (me) { row.style.borderLeftWidth = 3; row.style.borderLeftColor = Theme.Accent; }
                var fg = e.Alive ? Theme.Text : Theme.WithAlpha(Theme.Text, 0.45f);
                var nameCell = U.Row(10).Align(Align.Center).Grow();
                var dot = U.Box().Size(8, 8).Border(1, col).Bg(e.Alive ? col : Color.clear);
                nameCell.Kids(dot, U.Text(c.NameOf(e.Id), 17, Fonts.BodySemi, fg));
                if (e.IsBot) nameCell.Add(U.Head("BOT", 12, Theme.Dim, Fonts.DisplayBold, 2));
                row.Add(nameCell);
                string money = e.Money >= 0 ? U.Money(e.Money) : "—";
                string hs = e.Kills > 0 ? Mathf.RoundToInt(100f * e.Headshots / e.Kills) + "%" : "0%";
                string[] cells = { money, e.Kills.ToString(), e.Assists.ToString(), e.Deaths.ToString(), e.Mvps > 0 ? "★" + e.Mvps : "", hs, Mathf.RoundToInt(e.Damage / (float)played).ToString(), e.Score.ToString() };
                for (int i = 0; i < cells.Length; i++)
                {
                    var l = U.Text(cells[i], 17, i == cells.Length - 1 ? Fonts.BodyBold : Fonts.BodySemi, i == 0 && e.Money >= 0 ? Theme.Accent : fg)
                        .W(ColW[i + 1]).TextAlign(TextAnchor.MiddleRight);
                    row.Add(l);
                }
                body.Add(row);
            }
        }

        void History()
        {
            var c = _c;
            _history.Clear();
            int total = Mathf.Max(c.MaxRounds, c.History.Count);
            for (int i = 0; i < total; i++)
            {
                if (i == c.HalfRounds && i > 0) _history.Add(U.Box().Size(2, 30).Bg(Theme.WithAlpha(Color.white, 0.35f)).Margin(0, 6, 0, 6));
                var box = U.Box().Size(30, 30).Align(Align.Center).Justify(Justify.Center);
                box.style.marginLeft = 3;
                if (i < c.History.Count)
                {
                    var r = c.History[i];
                    box.Bg(Theme.TeamColor(r.Winner));
                    string g = r.Reason == RoundEndReason.BombExploded ? "B" : r.Reason == RoundEndReason.BombDefused ? "İ" : r.Reason == RoundEndReason.TimeExpired ? "S" : "×";
                    box.Add(U.Head(g, 16, Theme.Ink, Fonts.Display));
                }
                else box.Bg(Theme.WithAlpha(Color.white, 0.06f));
                _history.Add(box);
            }
        }
    }
}
