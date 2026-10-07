// In-game overlays: buy menu, pause menu, team select, click-to-play.
import { world } from './state.js';
import { BUY_LAYOUT, resolveBuyItem, itemPrice, itemName, itemTeam, WEAPONS, GRENADES } from './weapons.js';
import { audio } from './audio.js';
import { player } from './player.js';

const $ = (id) => document.getElementById(id);

// small original silhouettes for buy-menu cards
const ICONS = {
  rifle: 'M2 14h40l6-3h14v3h-8v3H40l-4 9h-6l2-9h-6l-3 6h-5l2-6H6l-4 4z',
  ak: 'M2 13h44l4-2h12v3h-9v3H41l-5 10h-5l2-10h-6l-5 5h-5l3-5H6l-4 5z',
  bullpup: 'M8 11h38l4-2h12v4h-8v4H40l-2 6h-6l1-6H18l-2 7h-6l2-7H8z',
  smg: 'M10 12h30l3-2h8v3h-6v3H34l-2 8h-5l1-8h-6l-1 9h-5l1-9H10z',
  p90: 'M8 10h40v3h8v3h-8v3H36l-2 6h-6l1-6H14l-2 5H8z',
  shotgun: 'M2 12h52l2-1h6v3H44v3H22l-4 6h-8l3-6H6l-4 3z',
  mg: 'M2 13h48l4-2h8v3h-8v3H38v7h-8v-7H22l-3 6h-6l2-6H6l-4 4z',
  sniper: 'M2 14h50l4-2h6v3H38v3H26l-4 6h-6l2-6H8l-6 4zM18 8h16v4H18z',
  awp: 'M2 14h52l3-2h5v3H38v3H26l-4 7h-7l3-7H8l-6 5zM16 7h18v5H16z',
  pistol: 'M14 10h26v5H28l-3 10h-7l3-10h-7z',
  deagle: 'M12 9h30v6H28l-3 10h-8l3-10h-8z',
  dualies: 'M6 10h20v4h-9l-2 8h-5l2-8H6zM36 10h20v4h-9l-2 8h-5l2-8h-6z',
  knife: 'M6 16l30-6h18l-4 4H34l-6 4H16z',
  taser: 'M16 10h22v6H30l-2 9h-6l2-9h-8z',
  grenade: 'M26 6h10v4h-10zM31 10a9 9 0 1 0 0.01 0z',
  vest: 'M20 4h22l6 8v14H14V12z',
  vesthelm: 'M20 9h22l6 6v13H14V15zM24 2h14l3 5H21z',
  defuser: 'M18 8l12 10-12 10M44 8L32 18l12 10M26 16h10v4H26z',
};
function iconFor(id) {
  const w = WEAPONS[id];
  let k;
  if (GRENADES[id]) k = 'grenade';
  else if (id === 'vest' || id === 'vesthelm' || id === 'defuser') k = id;
  else if (w) k = w.model === 'sniper' ? 'sniper' : (ICONS[w.model] ? w.model : (w.cat === 'pistol' ? 'pistol' : 'rifle'));
  return `<svg viewBox="0 0 64 30" width="96" height="45"><path d="${ICONS[k] || ICONS.rifle}" fill="#d9dde3"/></svg>`;
}

export const ui = {
  buyVisible: false,
  pauseVisible: false,
  teamVisible: false,
  selCol: -1,

  init() {
    $('pm-resume').onclick = () => this.closePause();
    $('pm-settings').onclick = () => { this.closePause(true); world.menu.openSettingsInGame(); };
    $('pm-quit').onclick = () => this.quitToMenu();
    $('buymenu').addEventListener('contextmenu', e => e.preventDefault());
    document.addEventListener('keydown', e => {
      if (e.code === 'Escape' && world.running && !world.inMenu) {
        if (this.buyVisible) { this.closeBuy(); return; }
        if (world.menu?.settingsInGame) { world.menu.closeSettingsInGame(); this.openPause(); return; }
      }
    });
  },
  overlayOpen() { return this.buyVisible || this.pauseVisible || this.teamVisible || world.match?.phase === 'over' || world.menu?.settingsInGame; },
  buyOpen() { return this.buyVisible; },

  updateClickToPlay() {
    const show = world.running && !world.inMenu && !player.locked && !this.overlayOpen();
    $('click-to-play').classList.toggle('hidden', !show);
  },

  // ---------------- buy menu ----------------
  toggleBuy() {
    if (this.buyVisible) { this.closeBuy(); return; }
    const me = world.local, m = world.match;
    if (!me || !m) return;
    if (!me.alive) return;
    if (!m.canBuy(me)) {
      world.hud.hint(m.buyBlockedReason(me), 2);
      audio.play('deny', { volume: 0.5 });
      return;
    }
    this.buyVisible = true;
    this.selCol = -1;
    $('buymenu').classList.remove('hidden');
    this.renderBuy();
    player.unlock(true);
    this.updateClickToPlay();
  },
  closeBuy() {
    if (!this.buyVisible) return;
    this.buyVisible = false;
    $('buymenu').classList.add('hidden');
    if (world.running && !world.inMenu && !this.pauseVisible) player.lock();
    this.updateClickToPlay();
  },
  buyKey(code) {
    if (code === 'Escape' || code === 'KeyB') { this.closeBuy(); return; }
    const n = parseInt(code.replace('Digit', ''), 10);
    if (!n) return;
    if (this.selCol < 0) { if (n <= BUY_LAYOUT.length) { this.selCol = n - 1; this.renderBuy(); audio.play('ui', { volume: 0.4 }); } return; }
    const col = BUY_LAYOUT[this.selCol];
    const entry = col.items[n - 1];
    this.selCol = -1;
    if (entry) this.buy(resolveBuyItem(entry, world.local.team));
    this.renderBuy();
  },
  buy(id) {
    const me = world.local;
    const ok = world.game.tryBuy(me, id);
    if (!ok) audio.play('deny', { volume: 0.5 });
    this.renderBuy();
    if (ok && WEAPONS[id] && (WEAPONS[id].slot === 'primary') && world.mode !== 'practice') { /* keep menu open like CS2 */ }
  },
  renderBuy() {
    const me = world.local, m = world.match;
    if (!me || !m) return;
    $('bm-money').textContent = '$' + me.money;
    $('bm-time').textContent = m.buyTimeText();
    const grid = $('bm-grid');
    grid.style.gridTemplateColumns = `repeat(${BUY_LAYOUT.length}, 1fr)`;
    const dm = world.mode === 'dm';
    grid.innerHTML = BUY_LAYOUT.map((col, ci) => {
      const items = col.items.map((entry, ii) => {
        const id = resolveBuyItem(entry, me.team);
        const team = itemTeam(id);
        const na = team && team !== me.team && !dm;
        const price = m.priceFor(me, id);
        const owned = world.game.owns(me, id);
        const cant = !na && !owned && (price > me.money || !world.game.canHold(me, id));
        return `<div class="bm-item${na ? ' na' : ''}${cant ? ' cant' : ''}${owned ? ' owned' : ''}" data-id="${id}"><span class="k">${ii + 1}</span>${iconFor(id)}<div class="nm">${itemName(id)}</div><div class="pr">${price === 0 ? 'Bedava' : '$' + price}</div></div>`;
      }).join('');
      return `<div class="bm-col${this.selCol === ci ? ' sel' : ''}"><div class="bm-col-title"><span class="k">${ci + 1}</span>${col.title}</div>${items}</div>`;
    }).join('');
    grid.querySelectorAll('.bm-item').forEach(el => {
      el.onclick = () => this.buy(el.dataset.id);
      el.oncontextmenu = (e) => { e.preventDefault(); if (world.game.refund(me, el.dataset.id)) { audio.play('pickup', { volume: 0.5 }); } this.renderBuy(); };
    });
  },

  // ---------------- pause ----------------
  openPause() {
    if (!world.running || world.inMenu || world.match?.phase === 'over') return;
    if (this.buyVisible) { this.buyVisible = false; $('buymenu').classList.add('hidden'); }
    this.pauseVisible = true;
    world.paused = world.mode !== 'dm' || true;
    $('pause-menu').classList.remove('hidden');
    this.updateClickToPlay();
  },
  closePause(keepUnlocked) {
    this.pauseVisible = false;
    world.paused = false;
    $('pause-menu').classList.add('hidden');
    if (!keepUnlocked) player.lock();
    this.updateClickToPlay();
  },

  // ---------------- team select ----------------
  showTeamSelect(cb) {
    this.teamVisible = true;
    $('teamselect').classList.remove('hidden');
    document.querySelectorAll('.ts-card').forEach(el => {
      el.onclick = () => {
        audio.play('ui');
        let t = el.dataset.team;
        if (t === 'AUTO') t = Math.random() < 0.5 ? 'CT' : 'T';
        this.teamVisible = false;
        $('teamselect').classList.add('hidden');
        cb(t);
      };
    });
  },

  restartMatch() { world.hud.hideScoreboard(); world.game.restart(); player.lock(); },
  quitToMenu() {
    this.closePause(true);
    this.buyVisible = false; $('buymenu').classList.add('hidden');
    world.hud.hideScoreboard();
    world.game.stop();
    player.unlock(true);
    world.menu.show();
  },
};
