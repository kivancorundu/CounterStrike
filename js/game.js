// Match / round logic, economy, bomb, game modes.
import * as THREE from './vendor/three.module.min.js';
import { world, isEnemy, clamp, pick, shuffle, fmtTime, after, clearTimers, DEG } from './state.js';
import { settings } from './settings.js';
import { Character, WeaponInst } from './character.js';
import { BotBrain, BOT_NAMES, newRoundPlans, botBuy, DIFFICULTY } from './bots.js';
import { WEAPONS, GRENADES, EQUIPMENT, itemPrice, itemTeam, itemName } from './weapons.js';
import { damage as dealDamage, losToChar } from './combat.js';
import { clearGrenades, updateProjectiles } from './grenades.js';
import { clearItems, updateItems, autoPickup, removeItem } from './items.js';
import { fx } from './effects.js';
import { audio } from './audio.js';
import { buildGun } from './models.js';
import { modeName } from './hud.js';

const MODES = {
  competitive: { maxRounds: 24, halfRounds: 12, winRounds: 13, startMoney: 800, maxMoney: 16000, roundTime: 115, freezeTime: 15, buyTime: 20, friendlyFire: true, overtime: true, otMoney: 12500, endDelay: 7, bots: 5 },
  casual: { maxRounds: 15, halfRounds: 7, winRounds: 8, startMoney: 1000, maxMoney: 10000, roundTime: 135, freezeTime: 6, buyTime: 45, friendlyFire: false, freeArmor: true, freeKit: true, endDelay: 6, bots: 5 },
  dm: { duration: 600, startMoney: 16000, maxMoney: 16000, friendlyFire: false, bots: 10 },
  practice: { startMoney: 16000, maxMoney: 16000, friendlyFire: false, bots: 0 },
};

class Match {
  constructor(mode) {
    this.mode = mode;
    Object.assign(this, MODES[mode]);
    this.economy = mode === 'competitive' || mode === 'casual';
    this.rewardMul = 1;
    this.phase = 'init';
    this.phaseEnd = 0;
    this.round = 0;
    this.roundsPlayed = 0;
    this.score = { CT: 0, T: 0 };
    this.history = [];
    this.loss = { CT: 1, T: 1 };
    this.freeze = false;
    this.roundStart = 0;
    this.liveStart = 0;
    this.roundEnd = 0;
  }
  timeLeft() {
    const now = world.time;
    if (this.mode === 'dm') return Math.max(0, this.endAt - now);
    if (this.phase === 'live') return Math.max(0, this.roundEnd - now);
    if (this.phase === 'freeze') return this.roundTime;
    return 0;
  }
  isPistolRound() {
    if (this.mode !== 'competitive' && this.mode !== 'casual') return false;
    return this.roundsPlayed === 0 || this.roundsPlayed === this.halfRounds;
  }
  canBuy(ch) {
    if (!ch.alive) return false;
    if (this.mode === 'practice') return true;
    if (this.mode === 'dm') return world.time - (ch.spawnTime || 0) < 15;
    if (!(this.phase === 'freeze' || (this.phase === 'live' && world.time < this.liveStart + this.buyTime))) return false;
    return world.map.inBuyZone(ch.team, ch.pos.x, ch.pos.z);
  }
  buyBlockedReason(ch) {
    if (this.mode === 'dm') return 'Satın alma süresi doldu (yeniden doğunca açılır).';
    if (!(this.phase === 'freeze' || (this.phase === 'live' && world.time < this.liveStart + this.buyTime))) return 'Satın alma süresi doldu.';
    return 'Satın alma bölgesinde değilsin.';
  }
  buyTimeText() {
    if (this.mode === 'practice') return 'Sınırsız';
    if (this.mode === 'dm') return 'Kalan: ' + Math.max(0, Math.ceil(15 - (world.time - (world.local?.spawnTime || 0)))) + ' sn';
    const end = this.phase === 'freeze' ? this.phaseEnd + this.buyTime : this.liveStart + this.buyTime;
    return 'Kalan süre: ' + Math.max(0, Math.ceil(end - world.time)) + ' sn';
  }
  priceFor(ch, id) {
    if (this.mode === 'dm') return 0;
    if (id === 'vesthelm' && ch.armor >= 100 && !ch.helmet) return 350;
    return itemPrice(id);
  }
}

export const game = {
  teamPlan: { T: {}, CT: {} },
  intel: { CT: null, T: null },
  started: false,
  cfg: null,

  // ---------------- setup ----------------
  start(mode, team, difficulty) {
    this.cfg = { mode, team, difficulty };
    world.mode = mode;
    world.difficulty = difficulty;
    this.cleanup();
    const m = new Match(mode);
    world.match = m;
    world.game = this;
    // characters
    const name = settings.playerName || 'Oyuncu';
    const local = new Character({ name, team: mode === 'dm' ? 'DM' : team, isLocal: true });
    world.local = local;
    world.chars = [local];
    const names = shuffle([...BOT_NAMES]);
    let ni = 0;
    if (mode === 'competitive' || mode === 'casual') {
      for (let i = 0; i < 4; i++) this.addBot(names[ni++], team);
      for (let i = 0; i < 5; i++) this.addBot(names[ni++], team === 'CT' ? 'T' : 'CT');
    } else if (mode === 'dm') {
      for (let i = 0; i < 9; i++) this.addBot(names[ni++], 'DM');
    }
    for (const c of world.chars) { c.money = m.startMoney; c.buildModel(); }
    world.running = true;
    world.paused = false;
    world.inMenu = false;
    world.menu?.hide();
    world.hud.show(true);
    world.hud.clearFeed();
    if (mode === 'dm') this.startDM();
    else if (mode === 'practice') this.startPractice();
    else this.startRound();
  },
  addBot(name, team) {
    const b = new Character({ name, team, isBot: true });
    b.ai = new BotBrain(b);
    world.chars.push(b);
    return b;
  },
  enableAutopilot() { world.autopilot = true; const l = world.local; l.ai = new BotBrain(l); },
  restart() {
    const c = this.cfg;
    this.start(c.mode, c.team, c.difficulty);
  },
  stop() {
    world.running = false;
    this.cleanup();
    world.hud.show(false);
  },
  cleanup() {
    clearTimers();
    for (const c of world.chars) if (c.model) world.scene.remove(c.model);
    world.chars = [];
    clearItems();
    clearGrenades();
    fx.clear();
    if (world.bomb?.mesh) world.scene.remove(world.bomb.mesh);
    world.bomb = { state: 'none' };
    world.spectating = null;
    world.hud?.hideBanner();
    world.hud?.hideDeathPanel();
  },

  // ---------------- rounds ----------------
  startRound() {
    const m = world.match, map = world.map, now = world.time;
    m.round = m.roundsPlayed + 1;
    m.phase = 'freeze';
    m.freeze = true;
    m.phaseEnd = now + m.freezeTime;
    m.roundStart = now;
    m.bombPlanted = false;
    clearItems(); clearGrenades(); fx.clear();
    if (world.bomb?.mesh) world.scene.remove(world.bomb.mesh);
    world.bomb = makeBomb();
    world.spectating = null;
    world.hud.hideBanner();
    world.hud.hideDeathPanel();
    const idx = { T: 0, CT: 0 };
    const spawns = { T: shuffle([...map.spawns.T]), CT: shuffle([...map.spawns.CT]) };
    for (const c of world.chars) {
      const keep = c.alive && !m.resetInventories;
      if (!keep) {
        c.resetInventory();
        c.inv.secondary = new WeaponInst(c.team === 'T' ? 'glock' : 'usp');
        if (m.resetInventories) { c.armor = 0; c.helmet = false; c.kit = false; }
      }
      c.inv.c4 = false;
      c.alive = true; c.health = 100;
      c.resetState();
      c.round = { kills: 0, damage: 0, dmgTaken: new Map(), dmgDealt: new Map() };
      c.purchases = [];
      const sp = spawns[c.team][idx[c.team]++ % spawns[c.team].length];
      c.pos.set(sp.pos.x, map.floorAt(sp.pos.x, sp.pos.z), sp.pos.z);
      c.vel.set(0, 0, 0); c.yaw = sp.yaw + (Math.random() - 0.5) * 0.4; c.pitch = 0; c.duck = 0; c.onGround = true;
      if (m.freeArmor) { c.armor = 100; c.helmet = true; }
      if (m.freeKit && c.team === 'CT') c.kit = true;
      c.active = ''; c.switchTo(c.bestSlot()); c.deployUntil = now;
      if (c.ai) c.ai.reset();
      if (c.model && c.model.userData.team !== c.team) c.buildModel();
      if (!c.isLocal && c.team === world.local.team) c.ensureTag();
      c.spottedUntil = 0;
    }
    m.resetInventories = false;
    // bomb to a random terrorist
    const ts = world.chars.filter(c => c.team === 'T');
    if (ts.length) { const carrier = pick(ts); carrier.inv.c4 = true; world.bomb.state = 'carried'; world.bomb.carrier = carrier; }
    // bots buy
    this.teamPlan = newRoundPlans();
    this.intel = { CT: null, T: null };
    for (const team of ['T', 'CT']) {
      const mode = this.teamBuyMode(team);
      for (const c of world.chars.filter(c => c.isBot && c.team === team)) after(0.5 + Math.random() * 2.5, () => { if (m.phase === 'freeze' || m.phase === 'live') botBuy(c, mode); });
    }
    // messages
    const last = m.roundsPlayed === m.halfRounds - 1 ? 'Devre arasından önceki son tur' : null;
    const mp = m.score.CT === m.winRounds - 1 || m.score.T === m.winRounds - 1 ? 'MAÇ SAYISI' : null;
    world.hud.center(`TUR ${m.round}`, 3);
    if (last || mp) world.hud.hint(mp || last, 4);
    if (world.local.inv.c4) world.hud.hint('Bomba sende! [5] ile seç, bölgede [Sol Tık] basılı tutarak kur.', 5);
  },

  teamBuyMode(team) {
    const m = world.match;
    if (m.isPistolRound()) return 'pistol';
    const members = world.chars.filter(c => c.team === team);
    const avg = members.reduce((s, c) => s + c.money, 0) / Math.max(1, members.length);
    const critical = m.roundsPlayed === m.halfRounds - 1 || m.score[team === 'CT' ? 'T' : 'CT'] === m.winRounds - 1;
    if (avg >= 3900) return 'full';
    if (avg >= 2600 && (m.loss[team] >= 2 || critical)) return 'force';
    if (critical && avg >= 1500) return 'force';
    return 'eco';
  },

  endRound(winner, reason) {
    const m = world.match, now = world.time;
    if (m.phase === 'end' || m.phase === 'over') return;
    const bomb = world.bomb;
    m.phase = 'end';
    m.freeze = false;
    m.phaseEnd = now + m.endDelay;
    m.score[winner]++;
    m.roundsPlayed++;
    const loser = winner === 'CT' ? 'T' : 'CT';
    const icons = { elim: '☠', bomb: '✹', defuse: '✂', time: '⏱' };
    const reasons = { elim: 'Rakip takım yok edildi', bomb: 'Bomba patladı', defuse: 'Bomba imha edildi', time: 'Süre doldu' };
    m.history.push({ winner, reason: reasons[reason], icon: icons[reason] });
    // ---- economy ----
    if (m.economy) {
      const winMoney = reason === 'bomb' || reason === 'defuse' ? 3500 : 3250;
      const lossMoney = 1400 + 500 * Math.min(m.loss[loser], 4);
      for (const c of world.chars) {
        if (c.team === winner) c.addMoney(winMoney, 'win');
        else {
          // terrorists who survive a time-out get no loss bonus
          if (reason === 'time' && c.team === 'T' && c.alive) { c.addMoney(0); }
          else c.addMoney(lossMoney, 'loss');
          if (c.team === 'T' && m.bombPlanted) c.addMoney(800, 'plant');
        }
      }
      m.loss[loser] = Math.min(m.loss[loser] + 1, 4);
      m.loss[winner] = Math.max(m.loss[winner] - 1, 0);
    }
    // ---- MVP ----
    let mvp = null;
    if (reason === 'bomb') mvp = bomb.planter;
    else if (reason === 'defuse') mvp = bomb.defusedBy;
    if (!mvp || mvp.team !== winner) {
      const list = world.chars.filter(c => c.team === winner).sort((a, b) => b.round.kills - a.round.kills || b.round.damage - a.round.damage);
      mvp = list[0];
    }
    if (mvp) mvp.stats.mvps++;
    const title = winner === 'CT' ? 'TERÖRLE MÜCADELE KAZANDI' : 'TERÖRİSTLER KAZANDI';
    const mvpText = mvp ? `<br>★ Turun MVP'si: <b>${mvp.name}</b>${reason === 'bomb' ? ' (bombayı kurdu)' : reason === 'defuse' ? ' (bombayı imha etti)' : ` (${mvp.round.kills} öldürme)`}` : '';
    world.hud.banner(winner, title, reasons[reason] + mvpText);
    audio.play(winner === 'CT' ? 'winct' : 'wint', { volume: 0.55 });
    // ---- match end / half time ----
    const r = m.roundsPlayed;
    let over = null;
    if (m.mode === 'competitive') {
      if (r <= 24) { if (m.score.CT >= 13 || m.score.T >= 13) over = m.score.CT > m.score.T ? 'CT' : 'T'; }
      else {
        const k = Math.ceil((r - 24) / 6);
        const target = 12 + 3 * (k - 1) + 4;
        if (m.score.CT >= target || m.score.T >= target) over = m.score.CT > m.score.T ? 'CT' : 'T';
      }
    } else if (m.score.CT >= m.winRounds || m.score.T >= m.winRounds || r >= m.maxRounds) {
      over = m.score.CT > m.score.T ? 'CT' : m.score.T > m.score.CT ? 'T' : 'draw';
    }
    if (over) { after(m.endDelay - 1, () => this.matchOver(over)); return; }
    after(m.endDelay, () => this.nextRound());
  },

  nextRound() {
    const m = world.match;
    if (!world.running || m.phase === 'over') return;
    const r = m.roundsPlayed;
    if (r === m.halfRounds) {
      this.swapTeams(m.startMoney);
      world.hud.center('DEVRE ARASI — Taraflar değişti', 4, '#f0d070');
    } else if (m.mode === 'competitive' && r >= 24 && (r - 24) % 3 === 0) {
      const pos = (r - 24) % 6;
      if (pos === 0) { for (const c of world.chars) { c.money = m.otMoney; c.resetInventory(); c.armor = 0; c.helmet = false; c.kit = false; } m.resetInventories = true; m.loss = { CT: 1, T: 1 }; world.hud.center('UZATMA — Herkese $12.500', 4, '#f0d070'); }
      else { this.swapTeams(m.otMoney); world.hud.center('UZATMA DEVRE ARASI', 4, '#f0d070'); }
    }
    this.startRound();
  },

  swapTeams(money) {
    const m = world.match;
    for (const c of world.chars) {
      c.team = c.team === 'CT' ? 'T' : 'CT';
      c.money = money;
      c.resetInventory(); c.armor = 0; c.helmet = false; c.kit = false;
      c.buildModel();
    }
    m.score = { CT: m.score.T, T: m.score.CT };
    m.loss = { CT: 1, T: 1 };
    m.resetInventories = true;
    for (const h of m.history) h.winner = h.winner === 'CT' ? 'T' : 'CT';
  },

  matchOver(winner) {
    const m = world.match;
    m.phase = 'over';
    m.freeze = true;
    world.ui.closeBuy?.();
    const me = world.local;
    let title, sub;
    if (world.mode === 'dm') {
      const top = [...world.chars].sort((a, b) => b.stats.kills - a.stats.kills)[0];
      title = top === me ? 'KAZANDIN!' : `${top.name} KAZANDI`;
      sub = `Öldürme: ${me.stats.kills} · Ölüm: ${me.stats.deaths}`;
    } else if (winner === 'draw') { title = 'BERABERE'; sub = `${m.score.CT} : ${m.score.T}`; }
    else {
      title = winner === me.team ? 'MAÇI KAZANDIN!' : 'MAÇI KAYBETTİN';
      sub = `Skor ${m.score[me.team]} : ${m.score[me.team === 'CT' ? 'T' : 'CT']} · Öldürme ${me.stats.kills} · Ölüm ${me.stats.deaths} · ★${me.stats.mvps}`;
    }
    audio.play(winner === me.team || (world.mode === 'dm' && title === 'KAZANDIN!') ? 'winct' : 'lose', { volume: 0.6 });
    world.hud.matchEnd(title, sub);
    world.player.unlock(true);
  },

  // ---------------- deathmatch / practice ----------------
  startDM() {
    const m = world.match;
    m.phase = 'live'; m.freeze = false;
    m.endAt = world.time + m.duration;
    m.round = 1;
    world.bomb = { state: 'none' };
    for (const c of world.chars) { c.dmLoadout = []; this.respawn(c); }
    world.hud.center('ÖLÜM MAÇI', 3);
    world.hud.hint('[B] ile istediğin silahı bedavaya al (doğduktan sonra 15 sn).', 5);
  },
  startPractice() {
    const m = world.match;
    m.phase = 'live'; m.freeze = false; m.round = 1;
    world.bomb = makeBomb();
    const me = world.local;
    this.respawn(me);
    if (me.team === 'T') { me.inv.c4 = true; world.bomb.state = 'carried'; world.bomb.carrier = me; }
    world.hud.center('ANTRENMAN', 3);
    world.hud.hint('[B] her yerde açılır, para sınırsız. Bombalarının yolu sarı çizgiyle gösterilir.', 6);
  },
  respawn(c) {
    const map = world.map, now = world.time;
    c.resetInventory();
    c.inv.secondary = new WeaponInst(c.team === 'T' || (world.mode === 'dm' && c.isBot) ? 'glock' : 'usp');
    c.alive = true; c.health = 100; c.armor = 100; c.helmet = true;
    c.resetState();
    c.round = { kills: 0, damage: 0, dmgTaken: new Map(), dmgDealt: new Map() };
    let pos;
    if (world.mode === 'dm') {
      let best = null, bestD = -1;
      for (let k = 0; k < 12; k++) {
        const p = map.randomWalkPos();
        let dmin = Infinity;
        for (const o of world.chars) if (o !== c && o.alive) dmin = Math.min(dmin, o.pos.distanceTo(p));
        if (dmin > bestD) { bestD = dmin; best = p; }
      }
      pos = best;
      c.protectUntil = now + 1.5;
    } else {
      const sp = pick(map.spawns[c.team]);
      pos = sp.pos.clone();
    }
    c.pos.set(pos.x, map.floorAt(pos.x, pos.z), pos.z);
    c.vel.set(0, 0, 0); c.yaw = Math.random() * Math.PI * 2; c.pitch = 0;
    c.spawnTime = now;
    c.money = 16000;
    if (c.dmLoadout) for (const id of c.dmLoadout) this.tryBuy(c, id, true, true);
    if (c.ai) { c.ai.reset(); if (world.mode === 'dm') after(0.3, () => botBuy(c, 'full')); }
    c.active = ''; c.switchTo(c.bestSlot()); c.deployUntil = now;
    if (c.isLocal) world.spectating = null;
  },

  // ---------------- buying ----------------
  owns(ch, id) {
    if (id === 'vest') return ch.armor >= 100;
    if (id === 'vesthelm') return ch.armor >= 100 && ch.helmet;
    if (id === 'defuser') return ch.kit;
    if (id === 'taser') return !!ch.inv.taser;
    if (GRENADES[id]) return ch.grenadeCount(id) > 0;
    const w = WEAPONS[id];
    return !!(w && ch.inv[w.slot] && ch.inv[w.slot].id === id);
  },
  canHold(ch, id) {
    if (GRENADES[id]) return ch.canTakeGrenade(id);
    if (id === 'defuser') return ch.team === 'CT' && !ch.kit;
    return !this.owns(ch, id);
  },
  tryBuy(ch, id, silent, free) {
    const m = world.match;
    if (!m || !m.canBuy(ch) && !free) return false;
    const team = itemTeam(id);
    if (team && team !== ch.team && world.mode !== 'dm') return false;
    const price = free ? 0 : m.priceFor(ch, id);
    if (price > ch.money) { if (ch.isLocal && !silent) world.hud.hint('Yeterli paran yok.', 1.5); return false; }
    if (!this.canHold(ch, id)) return false;
    let given = true;
    if (id === 'vest') { ch.armor = 100; }
    else if (id === 'vesthelm') { ch.armor = 100; ch.helmet = true; }
    else if (id === 'defuser') { ch.kit = true; }
    else given = ch.give(id);
    if (!given) return false;
    ch.money -= price;
    (ch.purchases ||= []).push({ id, price, at: world.time });
    if (world.mode === 'dm' && !free && WEAPONS[id] && (WEAPONS[id].slot === 'primary' || WEAPONS[id].slot === 'secondary')) {
      ch.dmLoadout = (ch.dmLoadout || []).filter(x => WEAPONS[x]?.slot !== WEAPONS[id].slot);
      ch.dmLoadout.push(id);
    }
    if (ch.isLocal) audio.play('buy', { volume: 0.6 });
    return true;
  },
  refund(ch, id) {
    const m = world.match;
    if (!m.canBuy(ch) || !ch.purchases) return false;
    const k = ch.purchases.map(p => p.id).lastIndexOf(id);
    if (k < 0) return false;
    const p = ch.purchases[k];
    if (GRENADES[id]) { const i = ch.inv.grenades.indexOf(id); if (i < 0) return false; ch.inv.grenades.splice(i, 1); if (ch.active === 'grenade' && !ch.inv.grenades.length) { ch.active = ''; ch.switchTo(ch.bestSlot()); } }
    else if (WEAPONS[id]) {
      const w = WEAPONS[id]; const slot = w.slot === 'taser' ? 'taser' : w.slot;
      const inst = ch.inv[slot];
      if (!inst || inst.id !== id || inst.clip !== w.clip) return false;
      ch.inv[slot] = null;
      if (ch.active === slot) { ch.active = ''; ch.switchTo(ch.bestSlot()); }
    } else if (id === 'vest' || id === 'vesthelm') { if (ch.armor < 100) return false; ch.armor = 0; ch.helmet = false; }
    else if (id === 'defuser') { ch.kit = false; }
    ch.purchases.splice(k, 1);
    ch.money += p.price;
    return true;
  },

  // ---------------- bomb ----------------
  plantBomb(ch, site) {
    const m = world.match, b = world.bomb, now = world.time;
    b.state = 'planted';
    b.site = site;
    b.planter = ch;
    b.carrier = null;
    b.pos = new THREE.Vector3(ch.pos.x - Math.sin(ch.yaw) * 14, world.map.floorAt(ch.pos.x, ch.pos.z) + 0.5, ch.pos.z - Math.cos(ch.yaw) * 14);
    b.plantedAt = now;
    b.explodeAt = now + 40;
    b.nextBeep = now;
    b.defuser = null;
    const mesh = buildGun(WEAPONS.c4).group;
    mesh.position.copy(b.pos); mesh.rotation.y = ch.yaw;
    world.scene.add(mesh);
    b.mesh = mesh; b.led = mesh.userData.led;
    m.bombPlanted = true;
    if (m.phase === 'live') m.phase = 'planted';
    if (m.economy) ch.addMoney(300, 'plant');
    ch.stats.score += 2;
    audio.play('armed', { pos: b.pos, maxDist: 4000 });
    world.hud.center('BOMBA KURULDU', 3, '#ff5a48');
    if (ch.isBot) world.hud.chat(ch, site === 'A' ? 'Bomba A\'ya kuruldu!' : 'Bomba B\'ye kuruldu!');
    this.noise(ch, 2500, 'plant');
  },
  onBombDropped(ch, died) {
    const b = world.bomb;
    if (!b) return;
    b.state = 'dropped'; b.carrier = null;
    const item = world.dropped.find(i => i.kind === 'c4');
    b.item = item;
    if (world.local.team === 'T' && world.mode !== 'practice') world.hud.hint(died ? `${ch.name} öldü, bomba düştü!` : `${ch.name} bombayı bıraktı.`, 2.5);
  },
  onBombPickup(ch) {
    const b = world.bomb;
    b.state = 'carried'; b.carrier = ch; b.item = null;
    if (ch.isLocal) world.hud.hint('Bombayı aldın.', 2);
  },
  updateBomb(dt) {
    const b = world.bomb, m = world.match, now = world.time;
    if (!b) return;
    if (b.state === 'dropped' && b.item) b.pos = b.item.pos;
    if (b.state === 'carried' && b.carrier) b.pos = b.carrier.pos;
    if (b.state !== 'planted') return;
    // beeping speeds up
    const left = b.explodeAt - now;
    if (b.led) b.led.visible = now - (b.lastBeep || 0) < 0.12;
    if (now >= b.nextBeep && left > 0) {
      b.lastBeep = now;
      audio.play('beep', { pos: b.pos, maxDist: 3500, ref: 200, volume: left < 10 ? 1 : 0.75 });
      const t = clamp(left / 40, 0, 1);
      b.nextBeep = now + (left < 1.5 ? 0.06 : 0.12 + 0.9 * Math.pow(t, 1.6));
    }
    // spotted by CT when anyone is close
    if (!b.spottedByCT) for (const c of world.chars) if (c.alive && c.team === 'CT' && c.pos.distanceTo(b.pos) < 600 && world.map.los(c.eyePos(new THREE.Vector3()), b.pos.clone().setY(b.pos.y + 4))) b.spottedByCT = true;
    // defusing
    if (b.defuser) {
      const d = b.defuser;
      if (!d.alive || !d._use || d.pos.distanceTo(b.pos) > 75 || m.phase === 'end') { d.defusing = false; b.defuser = null; }
      else if (now >= b.defuseEnd) {
        b.state = 'defused'; b.defusedBy = d; d.defusing = false; b.defuser = null;
        if (m.economy) d.addMoney(300, 'defuse');
        d.stats.score += 2;
        audio.play('defuse', { pos: b.pos, volume: 1, maxDist: 4000 });
        world.hud.center('BOMBA İMHA EDİLDİ', 3, '#8db8ff');
        if (world.mode === 'practice') { this.practiceBombReset(); return; }
        this.endRound('CT', 'defuse');
        return;
      }
    }
    if (!b.defuser && m.phase !== 'end') {
      for (const c of world.chars) {
        if (!c.alive || c.team !== 'CT' || !c._use || !c.onGround) continue;
        if (c.pos.distanceTo(b.pos) > 70) continue;
        b.defuser = c; c.defusing = true; c.vel.set(0, 0, 0);
        b.defuseStart = now; b.defuseEnd = now + (c.kit ? 5 : 10);
        audio.play('defuse', { pos: b.pos, maxDist: 1500 });
        this.noise(c, 1400, 'defuse');
        if (c.isLocal) c.zoom = 0;
        break;
      }
    }
    if (now >= b.explodeAt) this.explodeBomb();
  },
  explodeBomb() {
    const b = world.bomb, m = world.match;
    b.state = 'exploded';
    if (b.defuser) { b.defuser.defusing = false; b.defuser = null; }
    if (b.mesh) world.scene.remove(b.mesh);
    fx.explosion(b.pos, 3.2);
    audio.play('explosion', { volume: 1.2, rate: 0.7 });
    fx.shake(2.5);
    const def = { id: 'c4', name: 'C4', cat: 'c4', ap: 0.5, reward: 0 };
    const sigma = 1750 / 3;
    for (const c of world.chars) {
      if (!c.alive) continue;
      const d = c.pos.distanceTo(b.pos);
      const dmg = 500 * Math.exp(-(d * d) / (2 * sigma * sigma));
      if (dmg >= 1) dealDamage(c, b.planter && b.planter.team === 'T' ? null : null, dmg, 'chest', def, { bomb: true });
    }
    if (world.mode === 'practice') { this.practiceBombReset(); return; }
    if (m.phase !== 'end') this.endRound('T', 'bomb');
  },
  practiceBombReset() {
    const b = world.bomb;
    if (b.mesh) world.scene.remove(b.mesh);
    after(3, () => {
      world.bomb = makeBomb();
      const me = world.local;
      if (me.team === 'T' && me.alive) { me.inv.c4 = true; world.bomb.state = 'carried'; world.bomb.carrier = me; }
      world.match.phase = 'live';
    });
  },

  // ---------------- events ----------------
  damage(victim, attacker, amount, group, def, flags) { return dealDamage(victim, attacker, amount, group, def, flags); },
  onKill(victim, attacker, def, flags) {
    const now = world.time;
    victim.diedAt = now;
    world.hud.killfeed(attacker, victim, def, flags);
    if (victim.isLocal) {
      world.hud.deathPanel(victim, attacker, def, flags);
      world.ui.closeBuy?.();
    }
    if (attacker && attacker.isBot && isEnemy(attacker, victim) && Math.random() < 0.12) world.hud.chat(attacker, pick(['Bir tane düştü!', 'Hallettim!', 'Temiz.', 'Birini indirdim.']));
    if (world.mode === 'dm') {
      if (attacker && attacker !== victim) { attacker.health = Math.min(100, attacker.health + 0); attacker.money = 16000; }
      after(2, () => { if (world.running && world.mode === 'dm' && !victim.alive) this.respawn(victim); });
      return;
    }
    if (world.mode === 'practice') { after(2, () => { if (world.running && !victim.alive) { this.respawn(victim); if (victim.team === 'T' && world.bomb.state !== 'planted' && !world.dropped.some(i => i.kind === 'c4')) { victim.inv.c4 = true; world.bomb = makeBomb(); world.bomb.state = 'carried'; world.bomb.carrier = victim; } } }); return; }
    // last alive callout
    const team = victim.team;
    const left = world.chars.filter(c => c.team === team && c.alive);
    if (left.length === 1 && left[0].isLocal) world.hud.hint('Takımında hayatta kalan son kişi sensin!', 3);
  },
  noise(src, radius, kind, posOverride) {
    const pos = posOverride || src.pos;
    for (const c of world.chars) {
      if (!c.alive || !c.ai || c === src) continue;
      if (!isEnemy(src, c)) continue;
      const hearMul = (DIFFICULTY[world.difficulty] || DIFFICULTY.normal).hear;
      if (c.pos.distanceTo(pos) < radius * hearMul) c.ai.hear(pos, kind);
    }
  },
  spotted(enemy, by) {
    enemy.spottedUntil = world.time + 1.5;
    if (world.mode === 'dm') return;
    if (by.team === 'CT' && enemy.team === 'T') {
      const map = world.map;
      for (const s of ['A', 'B']) {
        const c = map.siteCenter(s);
        if (map.zoneAt(enemy.pos.x, enemy.pos.z) === s || c.distanceTo(enemy.pos) < 1100) { this.intel.CT = { site: s, time: world.time }; break; }
      }
    }
  },

  // ---------------- per frame ----------------
  update(dt) {
    const m = world.match;
    if (!m) return;
    const now = world.time;
    // phases
    if (m.phase === 'freeze' && now >= m.phaseEnd) {
      m.phase = 'live'; m.freeze = false; m.liveStart = now; m.roundEnd = now + m.roundTime;
      audio.play('roundstart', { volume: 0.45 });
      world.hud.center('', 0);
    }
    if (m.mode === 'practice') world.local.money = 16000;
    // characters
    const steps = Math.max(1, Math.ceil(dt / (1 / 90)));
    const sdt = dt / steps;
    const inputs = new Map();
    for (const c of world.chars) {
      if (!c.alive) continue;
      const inp = c.isLocal && !world.autopilot ? world.player.buildInput() : c.ai.update(dt);
      if (c.isLocal && (world.ui.pauseVisible || m.phase === 'over')) { inp.fwd = inp.side = 0; inp.attack = inp.attack2 = false; }
      if (c.isLocal && world.ui.buyOpen()) { inp.attack = inp.attack2 = false; }
      c._use = inp.use;
      if (inp.attack && c.protectUntil > now) c.protectUntil = 0;
      inputs.set(c, inp);
    }
    for (let s = 0; s < steps; s++) {
      for (const c of world.chars) {
        if (!c.alive) continue;
        const inp = inputs.get(c);
        if (!inp) continue;
        if (s > 0) { inp.attack2 = inp.attack2 && false; }
        c.movement(sdt, inp);
      }
      this.separateChars();
    }
    for (const c of world.chars) {
      if (!c.alive) continue;
      const inp = inputs.get(c);
      if (inp) c.weaponThink(dt, inp);
      autoPickup(c);
    }
    // local player spots enemies for the radar
    if (now - (this._spotT || 0) > 0.2) {
      this._spotT = now;
      const me = world.local;
      if (me.alive) {
        const eye = me.eyePos(new THREE.Vector3()), fwd = me.viewDir(new THREE.Vector3());
        for (const e of world.chars) {
          if (!e.alive || !isEnemy(me, e)) continue;
          const to = e.pos.clone().setY(e.pos.y + 40).sub(eye).normalize();
          if (to.dot(fwd) < 0.45) continue;
          if (losToChar(eye, e)) this.spotted(e, me);
        }
      }
    }
    this.updateBomb(dt);
    // win conditions
    if (m.economy && (m.phase === 'live' || m.phase === 'planted')) {
      const aliveT = world.chars.filter(c => c.team === 'T' && c.alive).length;
      const aliveCT = world.chars.filter(c => c.team === 'CT' && c.alive).length;
      if (m.phase === 'live') {
        if (aliveT === 0) this.endRound('CT', 'elim');
        else if (aliveCT === 0) this.endRound('T', 'elim');
        else if (now >= m.roundEnd) this.endRound('CT', 'time');
      } else if (m.phase === 'planted') {
        if (aliveCT === 0) this.endRound('T', 'elim');
      }
    }
    if (m.mode === 'dm' && m.phase === 'live' && now >= m.endAt) this.matchOver('dm');
  },

  separateChars() {
    const L = world.chars;
    for (let i = 0; i < L.length; i++) {
      const a = L[i]; if (!a.alive) continue;
      for (let j = i + 1; j < L.length; j++) {
        const b = L[j]; if (!b.alive) continue;
        const dx = b.pos.x - a.pos.x, dz = b.pos.z - a.pos.z;
        const d2 = dx * dx + dz * dz;
        if (d2 > 31 * 31 || d2 < 1e-6) continue;
        if (Math.abs(a.pos.y - b.pos.y) > 60) continue;
        const d = Math.sqrt(d2), push = (31 - d) * 0.5;
        const nx = dx / d, nz = dz / d;
        a.moveAxis(0, -nx * push); a.moveAxis(2, -nz * push);
        b.moveAxis(0, nx * push); b.moveAxis(2, nz * push);
      }
    }
  },
};

function makeBomb() {
  return { state: 'none', pos: new THREE.Vector3(), carrier: null, defuser: null, defuseProgress() { return this.defuser ? clamp((world.time - this.defuseStart) / (this.defuseEnd - this.defuseStart), 0, 1) : 0; } };
}
