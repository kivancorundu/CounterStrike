using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>Main menu list of recorded matches (.vxdemo) with play / delete.</summary>
    public sealed class DemosPage : VisualElement
    {
        private readonly VisualElement _list;
        private readonly Label _folder;
        private readonly Segmented _record;
        private string _confirmDelete;

        public DemosPage()
        {
            style.flexGrow = 1;
            var head = U.Row(16).Align(Align.FlexEnd).Margin(0, 0, 18, 0);
            var titles = U.Col(4);
            titles.Kids(U.Eyebrow("MAÇ KAYITLARI"), U.Head("DEMOLAR", 56, null, Fonts.Display, 2));
            var recCol = U.Col(6).Align(Align.FlexEnd);
            _record = new Segmented(new[] { "KAYIT KAPALI", "MAÇLARIMI KAYDET" }, VexaSettings.RecordDemos ? 1 : 0).W(380);
            _record.Changed += v => { VexaSettings.RecordDemos = v == 1; VexaSettings.Save(); };
            recCol.Kids(U.Text("Kendi kurduğun maçlar otomatik kaydedilir", 13, Fonts.Body, Theme.Muted), _record);
            head.Kids(titles, U.Spacer(), recCol);
            Add(head);

            var cols = U.Row().Pad(8, 16).BorderBottom(1, Theme.LineStrong);
            cols.Kids(Col("HARİTA", 0, true), Col("TARİH", 200), Col("SÜRE", 110), Col("RAUND", 90), Col("BOYUT", 100), Col("", 260));
            Add(cols);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _list = scroll.contentContainer;
            Add(scroll);
            _folder = U.Text("", 13, Fonts.Body, Theme.Dim).Margin(12, 0, 0, 0);
            Add(_folder);
        }

        static Label Col(string text, float w, bool grow = false)
        {
            var l = U.Head(text, 15, Theme.Muted, Fonts.DisplayBold, 2);
            if (grow) l.Grow(); else l.W(w);
            return l;
        }

        public void Refresh()
        {
            _list.Clear();
            _record.Set(VexaSettings.RecordDemos ? 1 : 0, false);
            string dir = GameSession.DemoFolder;
            _folder.text = "Klasör: " + dir + "  ·  Sunucu kayıtları için: vexa-server --record dosya.vxdemo";
            var files = Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles("*" + DemoFormat.Extension).OrderByDescending(f => f.LastWriteTimeUtc).ToList()
                : new List<FileInfo>();
            if (files.Count == 0)
            {
                var empty = U.Col(6).Pad(40, 16);
                empty.Kids(U.Head("HENÜZ DEMO YOK", 28, null, Fonts.Display, 2),
                           U.Text("Yukarıdan “MAÇLARIMI KAYDET” seçeneğini aç ve bir maç oyna; kayıt maç bitince burada görünür.", 16, Fonts.Body, Theme.Muted));
                _list.Add(empty);
                return;
            }
            foreach (var f in files) _list.Add(Row(f));
        }

        VisualElement Row(FileInfo f)
        {
            var row = U.Row().Align(Align.Center).Pad(10, 16).BorderBottom(1, Theme.Line).Bg(Theme.WithAlpha(Theme.Raised, 0.6f));
            string map = "?", dur = "—", rounds = "—";
            try
            {
                var info = DemoFile.ReadInfo(f.FullName);
                map = (info.Map ?? "?").ToUpperInvariant();
                int s = (int)info.InfoDuration;
                dur = $"{s / 60}:{s % 60:00}";
                rounds = info.Rounds.Count.ToString();
            }
            catch (Exception) { map = "BOZUK DOSYA"; }
            var name = U.Col(2).Grow();
            name.Kids(U.Head(map, 22, null, Fonts.Display, 1), U.Text(f.Name, 12, Fonts.Body, Theme.Dim));
            row.Kids(name,
                U.Text(f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"), 15, Fonts.BodySemi).W(200),
                U.Text(dur, 15, Fonts.BodySemi).W(110),
                U.Text(rounds, 15, Fonts.BodySemi).W(90),
                U.Text($"{f.Length / (1024f * 1024f):0.0} MB", 15, Fonts.BodySemi, Theme.Muted).W(100));
            var actions = U.Row(8).W(260).Justify(Justify.FlexEnd);
            var play = new UButton("İZLE", ButtonStyle.Secondary, () => GameSession.PlayDemo(f.FullName), 17);
            bool confirming = _confirmDelete == f.FullName;
            var del = new UButton(confirming ? "EMİN MİSİN?" : "SİL", confirming ? ButtonStyle.Danger : ButtonStyle.Ghost, () =>
            {
                if (_confirmDelete == f.FullName)
                {
                    try { f.Delete(); } catch (Exception e) { Debug.LogWarning("[demo] delete failed: " + e.Message); }
                    _confirmDelete = null;
                }
                else _confirmDelete = f.FullName;
                Refresh();
            }, 17);
            actions.Kids(play, del);
            row.Add(actions);
            return row;
        }
    }
}
