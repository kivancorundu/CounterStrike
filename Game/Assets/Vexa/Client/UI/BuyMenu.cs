using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client.UI
{
    /// <summary>
    /// Buy menu: six columns from <see cref="Items.BuyMenu"/> for the local team. Mouse or keyboard
    /// (column number, then item number). Card states: owned, affordable, too expensive, full.
    /// </summary>
    public sealed class BuyMenuView : VisualElement
    {
        sealed class Card
        {
            public ItemId Item;
            public VisualElement El;
            public Label Key, Tag, Name, Price;
            public bool Hover;
        }

        private ClientGame _c;
        private Team _builtTeam = (Team)255;
        private readonly VisualElement _columns, _loadout;
        private readonly Label _teamLabel, _money, _time, _timeCaption;
        private readonly List<List<Card>> _cards = new List<List<Card>>();
        private readonly List<VisualElement> _colHeads = new List<VisualElement>();
        private readonly List<(Label k, Label v)> _loadoutRows = new List<(Label, Label)>();
        private int _pendingColumn = -1;
        public int Money;
        public event System.Action Close;

        public BuyMenuView()
        {
            this.Fill().Bg(Theme.Scrim);
            pickingMode = PickingMode.Position;

            var header = U.Row(32).Abs(56, 40, 56).Align(Align.FlexEnd).Pad(0, 0, 18, 0).BorderBottom(1, Theme.Line);
            var titles = U.Col();
            _teamLabel = U.Eyebrow("");
            titles.Kids(_teamLabel, U.Head("SATIN AL", 56, null, Fonts.Display, 2));
            var timeBox = U.Col().Align(Align.FlexEnd);
            _timeCaption = U.Text("Satın alma süresi", 13, Fonts.Body, Theme.Muted);
            _time = U.Head("0:00", 30, null, Fonts.DisplayBold);
            timeBox.Kids(_timeCaption, _time);
            var moneyBox = U.Col().Align(Align.FlexEnd);
            _money = U.Head("$0", 44, Theme.Accent);
            moneyBox.Kids(U.Text("Paran", 13, Fonts.Body, Theme.Muted), _money);
            var close = new UButton("KAPAT", ButtonStyle.Ghost, () => Close?.Invoke());
            close.Self(Align.Center);
            header.Kids(titles, U.Spacer(), timeBox, moneyBox, close);
            hierarchy.Add(header);

            _columns = U.Row(10).Abs(56, 170, 400);
            hierarchy.Add(_columns);

            _loadout = U.Col(10).Abs(null, 170, 56).W(310);
            _loadout.Add(U.Head("EKİPMANIN", 17, Theme.Muted, Fonts.DisplayBold, 2).Margin(0, 0, 6, 0));
            foreach (var k in new[] { "Birincil", "İkincil", "Zırh", "Bombalar", "Kit" })
            {
                var row = U.Row().Align(Align.Center).Justify(Justify.SpaceBetween).Pad(12, 14).Bg(Theme.WithAlpha(Theme.Raised, 0.92f)).Border(1, Theme.Line);
                var kl = U.Text(k, 13, Fonts.Body, Theme.Muted);
                var vl = U.Head("—", 20, null, Fonts.DisplayBold);
                row.Kids(kl, vl);
                _loadout.Add(row);
                _loadoutRows.Add((kl, vl));
            }
            hierarchy.Add(_loadout);

            hierarchy.Add(U.Text("Sütun için 1–6, ürün için tekrar numara · B / Esc kapat", 14, Fonts.Body, Theme.Muted).Abs(56, null, null, 36));
        }

        public void Bind(ClientGame c) { _c = c; _builtTeam = (Team)255; }

        void Build(Team team)
        {
            _builtTeam = team;
            _columns.Clear();
            _cards.Clear();
            _colHeads.Clear();
            _pendingColumn = -1;
            for (int ci = 0; ci < Items.BuyMenu.Length; ci++)
            {
                var (title, items) = Items.BuyMenu[ci];
                var col = U.Col(6).Grow().W(0);
                col.style.marginLeft = ci == 0 ? 0 : 10;
                var head = U.Row(8).Align(Align.Center).Pad(0, 2, 6, 2);
                var key = U.Head((ci + 1).ToString(), 14, null, Fonts.Display).Size(22, 22).Bg(Theme.WithAlpha(Color.white, 0.10f)).TextAlign(TextAnchor.MiddleCenter);
                head.Kids(key, U.Head(title, 17, Theme.Muted, Fonts.DisplayBold, 2));
                col.Add(head);
                _colHeads.Add(key);
                var list = new List<Card>();
                int n = 0;
                foreach (var (t, ct) in items)
                {
                    var item = team == Team.CT ? ct : t;
                    if (team == Team.None) item = t != ItemId.None ? t : ct;
                    if (item == ItemId.None) continue;
                    var card = MakeCard(item, ++n);
                    col.Add(card.El);
                    list.Add(card);
                }
                _cards.Add(list);
                _columns.Add(col);
            }
        }

        Card MakeCard(ItemId item, int index)
        {
            var c = new Card { Item = item };
            var el = new VisualElement().Dir(FlexDirection.Column).Pad(9, 12, 10, 12);
            el.pickingMode = PickingMode.Position;
            var top = U.Row().Justify(Justify.SpaceBetween);
            c.Key = U.Text(index.ToString(), 12, Fonts.Body, Theme.Muted);
            c.Tag = U.Text("", 12, Fonts.BodySemi, Theme.Muted);
            top.Kids(c.Key, c.Tag);
            c.Name = U.Head(Items.Name(item), 21, null, Fonts.DisplayBold, 0.5f);
            c.Price = U.Text("", 15, Fonts.BodyBold, Theme.Accent);
            el.Kids(top, c.Name, c.Price);
            el.style.marginTop = 6;
            el.RegisterCallback<PointerEnterEvent>(_ => { c.Hover = true; UiSound.Hover(); });
            el.RegisterCallback<PointerLeaveEvent>(_ => c.Hover = false);
            el.RegisterCallback<ClickEvent>(_ => Buy(c.Item));
            c.El = el;
            return c;
        }

        void Buy(ItemId item)
        {
            if (_c == null) return;
            UiSound.Click();
            _c.RequestBuy(item);
        }

        /// <summary>Keyboard: first digit picks a column, second an item in it.</summary>
        public void Digit(int d)
        {
            if (d < 1) return;
            if (_pendingColumn < 0)
            {
                if (d <= _cards.Count) _pendingColumn = d - 1;
                return;
            }
            var list = _cards[_pendingColumn];
            if (d <= list.Count) Buy(list[d - 1].Item);
            _pendingColumn = -1;
        }

        public void ResetKeys() => _pendingColumn = -1;

        enum State { Normal, Owned, Expensive, Full }

        State StateOf(ItemId item, in PlayerState st, int price, out string tag)
        {
            tag = "";
            if (Items.IsWeapon(item))
            {
                var w = Items.ToWeapon(item);
                if (st.Primary.Id == w || st.Secondary.Id == w) { tag = "SENDE"; return State.Owned; }
            }
            else if (Items.IsGrenade(item))
            {
                var g = Items.ToGrenade(item);
                int have = st.GrenadeCount(g), max = Items.GrenadeMax(g);
                if (have > 0) tag = have + " / " + max;
                if (have >= max) return State.Owned;
                if (st.GrenadeTotal >= Items.MaxGrenades) { tag = "DOLU"; return State.Full; }
            }
            else switch (item)
            {
                case ItemId.Vest: if (st.Armor >= 100) { tag = "SENDE"; return State.Owned; } break;
                case ItemId.VestHelmet:
                    if (st.Armor >= 100 && st.Helmet) { tag = "SENDE"; return State.Owned; }
                    if (st.Armor >= 100) tag = "SADECE KASK";
                    break;
                case ItemId.DefuseKit: if (st.HasKit) { tag = "SENDE"; return State.Owned; } break;
            }
            return price > Money ? State.Expensive : State.Normal;
        }

        public void Tick()
        {
            var c = _c;
            if (c == null) return;
            var st = c.Predicted;
            var team = c.Mode == GameMode.Deathmatch ? Team.None : st.Team;
            if (team != _builtTeam) Build(team);
            _teamLabel.text = team == Team.CT ? "SAVUNMA · CT" : team == Team.T ? "SALDIRI · T" : "ÖLÜM MAÇI";
            _teamLabel.style.color = Theme.TeamColor(team);
            bool free = c.Mode == GameMode.Deathmatch;
            _money.text = free ? "SINIRSIZ" : U.Money(Money);
            bool timed = c.Mode == GameMode.Competitive || c.Mode == GameMode.Casual;
            _time.Show(timed); _timeCaption.Show(timed);
            if (timed)
            {
                float left = Mathf.Max(0f, (float)((c.Header.BuyEndTick - c.ServerTickEstimate) / c.TickRate));
                _time.text = U.Clock(left);
            }

            for (int ci = 0; ci < _cards.Count; ci++)
            {
                _colHeads[ci].style.backgroundColor = ci == _pendingColumn ? Theme.Accent : Theme.WithAlpha(Color.white, 0.10f);
                _colHeads[ci].style.color = ci == _pendingColumn ? Theme.Ink : Theme.Text;
                foreach (var card in _cards[ci])
                {
                    int price = free ? 0 : (card.Item == ItemId.VestHelmet && st.Armor >= 100 && !st.Helmet ? Items.HelmetOnlyPrice : Items.Price(card.Item));
                    var s = StateOf(card.Item, st, price, out var tag);
                    card.Tag.text = tag;
                    card.Price.text = free ? "ÜCRETSİZ" : U.Money(price);
                    Color bg, fg, priceCol;
                    var el = card.El;
                    el.style.borderLeftWidth = 0;
                    el.Border(0, Color.clear);
                    switch (s)
                    {
                        case State.Owned:
                            bg = Theme.AccentSoft; fg = Theme.Text; priceCol = Theme.Muted;
                            el.style.borderLeftWidth = 3; el.style.borderLeftColor = Theme.Accent;
                            break;
                        case State.Expensive:
                        case State.Full:
                            bg = Theme.WithAlpha(Theme.Raised, 0.55f); fg = Theme.WithAlpha(Theme.Text, 0.35f); priceCol = s == State.Full ? Theme.Muted : Theme.Danger;
                            break;
                        default:
                            if (card.Hover) { bg = Theme.Text; fg = Theme.Ink; priceCol = Theme.Ink; }
                            else { bg = Theme.WithAlpha(Theme.Raised, 0.92f); fg = Theme.Text; priceCol = Theme.Accent; el.Border(1, Theme.Line); }
                            break;
                    }
                    el.style.backgroundColor = bg;
                    card.Name.style.color = fg;
                    card.Price.style.color = priceCol;
                    card.Key.style.color = card.Tag.style.color = Theme.WithAlpha(fg, 0.6f);
                }
            }

            string W(WeaponSlot s) => s.IsEmpty ? "—" : Weapons.Get(s.Id).Name;
            _loadoutRows[0].v.text = W(st.Primary);
            _loadoutRows[1].v.text = W(st.Secondary);
            _loadoutRows[2].v.text = st.Armor > 0 ? st.Armor + (st.Helmet ? " · kasklı" : " · kasksız") : "—";
            var g = new List<string>();
            if (st.NadeHE > 0) g.Add("HE"); if (st.NadeFlash > 0) g.Add(st.NadeFlash > 1 ? "Flaş ×2" : "Flaş"); if (st.NadeSmoke > 0) g.Add("Sis");
            if (st.NadeFire > 0) g.Add(st.Team == Team.CT ? "Yangın" : "Molotof"); if (st.NadeDecoy > 0) g.Add("Dekoy");
            _loadoutRows[3].v.text = g.Count > 0 ? string.Join(" · ", g) : "—";
            _loadoutRows[4].v.text = st.HasKit ? "Var" : (st.Team == Team.CT ? "Yok" : "—");
        }
    }
}
