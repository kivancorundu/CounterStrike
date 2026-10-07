// In-game HUD (DOM + canvas): top score bar, radar, money, health/armor, ammo, crosshair,
// kill feed, chat, damage indicators, overlays, scoreboard, death panel.
import * as THREE from './vendor/three.module.min.js';
import { world, fmtTime, clamp, DEG, isEnemy } from './state.js';
import { settings } from './settings.js';
import { GRENADES, WEAPONS } from './weapons.js';
import { pointInSmoke } from './grenades.js';

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const cache = {};
function setText(id, v) { if (cache[id] !== v) { cache[id] = v; $(id).textContent = v; } }
function setHTML(id, v) { if (cache['h' + id] !== v) { cache['h' + id] = v; $(id).innerHTML = v; } }
function toggle(id, show) { const k = 'v' + id; if (cache[k] !== show) { cache[k] = show; $(id).classList.toggle('hidden', !show); } }

export const hud = {
  radarImg: null,
  hintUntil: 0, centerUntil: 0,
  invShowUntil: 0,
  fpsT: 0, fpsN: 0, fps: 0,
  hurtA: 0,
  scoreboardOpen: false,
  lastXhairKey: '',

  init() {
    this.radar = $('radar').getContext('2d');
    this.xh = $('crosshair').getContext('2d');
  },
  setMap(map) {
    this.radarImg = map.renderRadar(4);
  },
  show(v) { $('hud').classList.toggle('hidden', !v); },

  // ---------------- messages ----------------
  hint(text, dur = 2) { setText('hint-msg', text); this.hintUntil = world.time + dur; },
  center(text, dur = 3, color) {
    const el = $('center-msg');
    el.textContent = text; el.style.color = color || '#fff';
    cache['center-msg'] = text;
    this.centerUntil = world.time + dur;
  },
  chat(ch, text) {
    const el = document.createElement('div');
    el.className = 'line';
    const cls = ch ? (world.mode === 'dm' ? 'sys' : ch.team.toLowerCase()) : 'sys';
    el.innerHTML = ch ? `<span class="${cls}">${world.mode === 'dm' ? '' : '(TAKIM) '}${esc(ch.name)}${ch.alive ? '' : ' *ÖLÜ*'}:</span> ${esc(text)}` : `<span class="sys">${esc(text)}</span>`;
    if (ch && world.local && ch.team !== world.local.team && world.mode !== 'dm') return; // team chat only
    const box = $('chat');
    box.appendChild(el);
    while (box.children.length > 7) box.firstChild.remove();
    setTimeout(() => el.remove(), 8200);
  },
  moneyDelta(n, reason) {
    const el = $('money-delta');
    const span = document.createElement('div');
    span.className = n >= 0 ? 'plus' : 'minus';
    span.textContent = (n >= 0 ? '+$' : '-$') + Math.abs(n) + (reason ? ' ' + reasonText(reason) : '');
    el.innerHTML = ''; el.appendChild(span);
    clearTimeout(this._md); this._md = setTimeout(() => { el.innerHTML = ''; }, 2500);
  },
  killfeed(attacker, victim, def, flags) {
    const box = $('killfeed');
    const row = document.createElement('div');
    row.className = 'kf';
    const local = world.local;
    if (attacker === local && attacker !== victim) row.classList.add('mine');
    if (victim === local) row.classList.add('victim-me');
    const cls = (c) => world.mode === 'dm' ? 'dm' : c.team.toLowerCase();
    let html = '';
    if (flags.blind && attacker) html += '<span class="tag bl">KÖR</span>';
    if (attacker && attacker !== victim) html += `<span class="${cls(attacker)}">${esc(attacker.name)}</span>`;
    let wname = def ? (def.name || def.id) : '';
    if (flags.bomb) wname = 'C4';
    if (flags.fall) wname = 'Düşme';
    if (flags.fire) wname = def?.id === 'incgrenade' ? 'Yangın' : 'Molotof';
    if (flags.backstab) wname += ' (sırttan)';
    html += `<span class="wpn">${esc(wname || '☠')}</span>`;
    if (flags.noscope) html += '<span class="tag ns">DÜRBÜNSÜZ</span>';
    if (flags.smoke) html += '<span class="tag sm">SİS</span>';
    if (flags.wallbang) html += '<span class="tag wb">DUVAR</span>';
    if (flags.headshot) html += '<span class="tag hs">KAFA</span>';
    html += `<span class="${cls(victim)}">${esc(victim.name)}</span>`;
    row.innerHTML = html;
    box.appendChild(row);
    while (box.children.length > 6) box.firstChild.remove();
    setTimeout(() => row.remove(), (row.classList.contains('mine') || row.classList.contains('victim-me')) ? 9000 : 6000);
  },
  clearFeed() { $('killfeed').innerHTML = ''; $('chat').innerHTML = ''; },
  hurt(attacker, dmg) {
    this.hurtA = Math.min(0.9, this.hurtA + 0.25 + dmg / 120);
    const me = world.local;
    if (!attacker || attacker === me) return;
    const ang = Math.atan2(attacker.pos.x - me.pos.x, attacker.pos.z - me.pos.z);
    // relative to view: forward is -Z (yaw 0)
    const rel = -(ang - (me.yaw + Math.PI));
    const el = document.createElement('div');
    el.className = 'dmg-ind';
    el.style.transform = `rotate(${rel}rad)`;
    $('dmg-indicators').appendChild(el);
    setTimeout(() => { el.style.opacity = '0'; }, 900);
    setTimeout(() => el.remove(), 1200);
  },
  flashInventory() { this.invShowUntil = world.time + 2.2; },
  banner(winnerTeam, title, sub) {
    const b = $('round-banner');
    b.className = winnerTeam ? winnerTeam.toLowerCase() : '';
    $('banner-title').textContent = title;
    $('banner-sub').innerHTML = sub || '';
  },
  hideBanner() { $('round-banner').className = 'hidden'; },
  deathPanel(victim, killer, def, flags) {
    const el = $('death-panel');
    if (!killer || killer === victim) {
      el.innerHTML = `<b>${flags.bomb ? 'C4 patlamasında öldün' : flags.fall ? 'Düşerek öldün' : 'Öldün'}</b>`;
    } else {
      const given = victim.round.dmgDealt.get(killer.id) || { dmg: 0, hits: 0 };
      const taken = victim.round.dmgTaken.get(killer.id) || { dmg: 0, hits: 0 };
      const wn = flags.fire ? 'Molotof' : def ? def.name : '';
      el.innerHTML = `<b>${esc(killer.name)}</b> seni <b>${esc(wn)}</b> ile öldürdü${flags.headshot ? ' (kafadan)' : ''}${flags.wallbang ? ' (duvardan)' : ''}<br>`
        + `Rakibin kalan canı: <b>${killer.alive ? killer.health : 0}</b><br>`
        + `Verdiğin hasar: <b>${given.dmg}</b> (${given.hits} isabet) · Aldığın hasar: <b>${taken.dmg}</b> (${taken.hits} isabet)`;
    }
    el.classList.remove('hidden');
    cache.vdeath = true;
    clearTimeout(this._dp);
    this._dp = setTimeout(() => { el.classList.add('hidden'); cache.vdeath = false; }, 6000);
  },
  hideDeathPanel() { $('death-panel').classList.add('hidden'); },

  // ---------------- per frame ----------------
  update(dt) {
    const me = world.local;
    const m = world.match;
    if (!me || !m) return;
    const now = world.time;
    const view = world.spectating && !me.alive ? world.spectating : me;

    // fps
    this.fpsN++; this.fpsT += dt;
    if (this.fpsT >= 0.5) { this.fps = Math.round(this.fpsN / this.fpsT); this.fpsN = 0; this.fpsT = 0; }
    setText('fps', settings.showFps ? this.fps + ' FPS' : '');

    // ---- top bar ----
    this.updateTop(me, m, now);

    // ---- money / buy zone ----
    setText('money', '$' + me.money);
    const canBuy = m.canBuy(me);
    $('buy-icon').style.visibility = canBuy ? 'visible' : 'hidden';
    setText('radar-zone', world.map.areaName(view.pos.x, view.pos.z));

    // ---- health / armor ----
    const hp = Math.max(0, Math.ceil(view.health));
    setText('hp-val', String(view.alive ? hp : 0));
    $('hp-val').classList.toggle('low', hp <= 25);
    $('hp-bar').style.width = (view.alive ? hp : 0) + '%';
    setText('armor-val', String(view.armor));
    $('armor-bar').style.width = view.armor + '%';
    setText('armor-ico', view.helmet ? '⛑' : '⛨');
    toggle('kit-ico', view.kit && view.alive);

    // ---- ammo ----
    const def = view.activeDef(), inst = view.activeInst();
    if (view.alive) {
      setText('weapon-name', def.cat === 'grenade' ? GRENADES[view.activeGrenade]?.name || '' : def.name);
      if (inst && def.clip) {
        setText('ammo-clip', String(inst.clip)); setText('ammo-sep', ' / '); setText('ammo-reserve', String(inst.reserve));
        $('ammo-clip').classList.toggle('low', inst.clip <= Math.ceil(def.clip * 0.2));
      } else if (def.cat === 'grenade') {
        setText('ammo-clip', String(view.grenadeCount(view.activeGrenade))); setText('ammo-sep', ''); setText('ammo-reserve', '');
      } else { setText('ammo-clip', ''); setText('ammo-sep', ''); setText('ammo-reserve', ''); }
      let mode = '';
      if (inst && def.burst) mode = inst.burstMode ? 'SERİ ATIŞ' : (def.id === 'glock' ? 'YARI OTOMATİK' : 'OTOMATİK');
      if (inst && def.silencer && !def.fixedSilencer) mode = inst.silenced ? 'SUSTURUCULU' : 'SUSTURUCUSUZ';
      if (view.reloading) mode = 'ŞARJÖR DEĞİŞTİRİLİYOR';
      setText('fire-mode', mode);
    }
    toggle('ammo-panel', view.alive);
    toggle('c4-carry', view.alive && view.inv.c4 && world.mode !== 'dm');

    // ---- inventory ----
    const inv = $('inventory');
    if (view.isLocal && view.alive) {
      const rows = [];
      const slot = (k, key, name) => rows.push(`<div class="slot${view.active === key ? ' active' : ''}"><span class="k">${k}</span>${esc(name)}</div>`);
      if (view.inv.primary) slot(1, 'primary', view.inv.primary.def.name);
      if (view.inv.secondary) slot(2, 'secondary', view.inv.secondary.def.name);
      slot(3, 'knife', 'Bıçak');
      if (view.inv.taser) slot(3, 'taser', 'Zeus x27');
      if (view.inv.grenades.length) rows.push(`<div class="slot${view.active === 'grenade' ? ' active' : ''}"><span class="k">4</span>${[...new Set(view.inv.grenades)].map(g => GRENADES[g].short + (view.grenadeCount(g) > 1 ? '×' + view.grenadeCount(g) : '')).join(' · ')}</div>`);
      if (view.inv.c4) slot(5, 'c4', 'C4');
      setHTML('inventory', rows.join(''));
      inv.style.opacity = now < this.invShowUntil || this.scoreboardOpen ? '1' : '0';
    } else inv.style.opacity = '0';

    // ---- crosshair ----
    this.drawCrosshair(view);

    // ---- progress (plant / defuse) ----
    const b = world.bomb;
    let prog = null;
    if (view.planting) prog = ['C4 kuruluyor...', view.plantProgress];
    else if (b && b.state === 'planted' && b.defuser === view) prog = [view.kit ? 'İmha ediliyor (kit)...' : 'İmha ediliyor...', b.defuseProgress()];
    toggle('progress-wrap', !!prog);
    if (prog) { setText('progress-label', prog[0]); $('progress-fill').style.width = (clamp(prog[1], 0, 1) * 100).toFixed(1) + '%'; }

    // ---- defuse hint ----
    if (b && b.state === 'planted' && view.isLocal && view.alive && view.team === 'CT' && !b.defuser && b.pos.distanceTo(view.pos) < 90) this.hint('İmha etmek için [E] basılı tut', 0.2);
    if (view.isLocal && view.alive && view.inv.c4 && view.active === 'c4' && world.map.zoneAt(view.pos.x, view.pos.z) && !view.planting) this.hint('Kurmak için [Sol Tık] basılı tut', 0.2);

    // ---- bomb status ----
    if (b && b.state === 'planted') { toggle('bomb-status', true); setText('bomb-status', `C4 KURULDU — ${b.site} BÖLGESİ`); }
    else toggle('bomb-status', false);

    // ---- messages ----
    if (now > this.hintUntil) setText('hint-msg', '');
    if (now > this.centerUntil) setText('center-msg', '');

    // ---- spectate info ----
    if (!me.alive && world.spectating) {
      toggle('spectate-info', true);
      const s = world.spectating;
      setHTML('spectate-info', `${esc(s.name)} izleniyor · ${s.health} HP · ${esc(s.activeDef().name)}<small>[Sol/Sağ tık] oyuncu değiştir</small>`);
    } else toggle('spectate-info', false);

    // ---- overlays ----
    const blind = view.alive ? view.blindAmount() : 0;
    $('flash-overlay').style.opacity = blind ? Math.min(1, blind * 1.05).toFixed(3) : '0';
    const cam = world.camera.position;
    $('smoke-overlay').style.opacity = pointInSmoke(cam.x, cam.y, cam.z) ? '0.92' : '0';
    this.hurtA = Math.max(0, this.hurtA - dt * 1.6);
    const lowHp = me.alive && me.health < 30 ? 0.25 + Math.sin(now * 4) * 0.08 : 0;
    $('hurt-overlay').style.opacity = Math.max(this.hurtA, lowHp).toFixed(3);
    const scoped = view.alive && view.zoom > 0 && def.scopeType;
    const sc = $('scope-overlay');
    sc.classList.toggle('hidden', !scoped);
    if (scoped) sc.classList.toggle('aug', def.scopeType === 'aug');
    $('crosshair').style.display = (scoped && def.scopeType === 'sniper') || !view.alive ? 'none' : 'block';

    // ---- radar ----
    this.drawRadar(view, me);
    if (this.scoreboardOpen) this.renderScoreboard();
  },

  updateTop(me, m, now) {
    const dm = world.mode === 'dm';
    let timer = '', sub = '';
    const el = $('round-timer');
    el.classList.remove('bomb', 'low');
    if (dm) { timer = fmtTime(m.timeLeft()); sub = 'ÖLÜM MAÇI'; }
    else if (m.phase === 'freeze') { timer = fmtTime(m.phaseEnd - now); sub = 'DONMA SÜRESİ'; }
    else if (m.phase === 'live') { timer = fmtTime(m.timeLeft()); sub = `TUR ${m.round}`; if (m.timeLeft() < 10) el.classList.add('low'); }
    else if (m.phase === 'planted') { timer = '💣'; sub = 'BOMBA'; el.classList.add('bomb'); }
    else if (m.phase === 'end') { timer = fmtTime(m.phaseEnd - now); sub = 'TUR SONU'; }
    else if (m.phase === 'warmup') { timer = fmtTime(m.phaseEnd - now); sub = 'ISINMA'; }
    else if (m.phase === 'over') { timer = '—'; sub = 'MAÇ BİTTİ'; }
    if (world.mode === 'practice') { timer = '∞'; sub = 'ANTRENMAN'; }
    setText('round-timer', timer); setText('round-sub', sub);
    if (dm) {
      const sorted = [...world.chars].sort((a, b) => b.stats.kills - a.stats.kills);
      setText('ct-score', String(me.stats.kills));
      setText('t-score', String(sorted[0] === me ? (sorted[1]?.stats.kills ?? 0) : sorted[0].stats.kills));
      setHTML('ct-avatars', ''); setHTML('t-avatars', '');
      return;
    }
    setText('ct-score', String(m.score.CT)); setText('t-score', String(m.score.T));
    const av = (team) => world.chars.filter(c => c.team === team).map(c => {
      const bomb = c.inv.c4 && (me.team === 'T') ? '<span class="bomb">●</span>' : '';
      return `<div class="av${c.alive ? '' : ' dead'}${c === me ? ' me' : ''}" title="${esc(c.name)}">${bomb}${esc(c.name.slice(0, 3))}<div class="hpb" style="width:${c.alive ? c.health : 0}%"></div></div>`;
    }).join('');
    setHTML('ct-avatars', av('CT'));
    setHTML('t-avatars', av('T'));
  },

  drawCrosshair(view) {
    const c = settings.crosshair;
    let gapExtra = 0;
    if (c.style === 'dynamic' && view.alive) {
      const def = view.activeDef();
      if (def.inacc) {
        const pxPerRad = (window.innerHeight / 2) / Math.tan(world.camera.fov * DEG / 2);
        gapExtra = Math.min(40, view.currentInaccuracy(def) / 1000 * pxPerRad * 0.6);
      }
    }
    const key = JSON.stringify(c) + '|' + Math.round(gapExtra);
    if (key === this.lastXhairKey) return;
    this.lastXhairKey = key;
    drawXhair(this.xh, 96, c, gapExtra);
  },

  drawRadar(view, me) {
    const ctx = this.radar, W = 220, map = world.map;
    if (!this.radarImg) return;
    ctx.clearRect(0, 0, W, W);
    const scalePx = 4 / 64; // radar px per world unit at zoom 1
    const zoom = 0.9 + settings.radarZoom * 1.4;
    ctx.save();
    ctx.beginPath(); ctx.rect(0, 0, W, W); ctx.clip();
    ctx.translate(W / 2, W / 2);
    if (settings.radarRotate) ctx.rotate(view.yaw);
    ctx.scale(zoom, zoom);
    const px = (view.pos.x - map.ox) * scalePx, pz = (view.pos.z - map.oz) * scalePx;
    ctx.translate(-px, -pz);
    ctx.globalAlpha = 0.9;
    ctx.drawImage(this.radarImg, 0, 0);
    ctx.globalAlpha = 1;
    const now = world.time;
    const dot = (x, z, color, r, ring) => {
      ctx.beginPath(); ctx.arc((x - map.ox) * scalePx, (z - map.oz) * scalePx, r / zoom, 0, Math.PI * 2);
      ctx.fillStyle = color; ctx.fill();
      if (ring) { ctx.lineWidth = 1.2 / zoom; ctx.strokeStyle = ring; ctx.stroke(); }
    };
    const dm = world.mode === 'dm';
    for (const c of world.chars) {
      if (c === view) continue;
      const friendly = !dm && c.team === me.team;
      if (!c.alive) {
        if (friendly && now - (c.diedAt || 0) < 5) {
          const x = (c.pos.x - map.ox) * scalePx, z = (c.pos.z - map.oz) * scalePx, s = 3 / zoom;
          ctx.strokeStyle = '#ccc'; ctx.lineWidth = 1.2 / zoom;
          ctx.beginPath(); ctx.moveTo(x - s, z - s); ctx.lineTo(x + s, z + s); ctx.moveTo(x + s, z - s); ctx.lineTo(x - s, z + s); ctx.stroke();
        }
        continue;
      }
      if (friendly) dot(c.pos.x, c.pos.z, c.team === 'CT' ? '#6fa0ff' : '#ffc04a', 3.4, '#000');
      else if ((c.spottedUntil || 0) > now) dot(c.pos.x, c.pos.z, '#ff3b30', 3.4, '#000');
    }
    // decoys look like enemies
    for (const p of world.projectiles) if (p.decoyStart && !p.done && isEnemy(p.owner, me) && Math.sin(now * 8) > -0.3) dot(p.pos.x, p.pos.z, '#ff3b30', 3.4, '#000');
    // bomb
    const b = world.bomb;
    if (b && !dm) {
      let bp = null;
      if (b.state === 'planted' && (me.team === 'T' || b.spottedByCT)) bp = b.pos;
      if (b.state === 'dropped' && me.team === 'T') bp = b.pos;
      if (b.state === 'carried' && me.team === 'T' && b.carrier) bp = b.carrier.pos;
      if (bp) { const x = (bp.x - map.ox) * scalePx, z = (bp.z - map.oz) * scalePx, s = 3.5 / zoom; ctx.fillStyle = b.state === 'planted' && Math.sin(now * 8) > 0 ? '#ff2a1a' : '#e55'; ctx.fillRect(x - s, z - s, s * 2, s * 2); }
    }
    ctx.restore();
    // self arrow
    ctx.save();
    ctx.translate(W / 2, W / 2);
    if (!settings.radarRotate) ctx.rotate(-view.yaw);
    ctx.beginPath(); ctx.moveTo(0, -6); ctx.lineTo(4.5, 5); ctx.lineTo(0, 2.5); ctx.lineTo(-4.5, 5); ctx.closePath();
    ctx.fillStyle = '#fff'; ctx.fill(); ctx.strokeStyle = '#000'; ctx.lineWidth = 1; ctx.stroke();
    ctx.restore();
  },

  // ---------------- scoreboard ----------------
  showScoreboard(v) {
    if (world.match?.phase === 'over') return;
    this.scoreboardOpen = v;
    $('scoreboard').classList.toggle('hidden', !v);
    if (v) this.renderScoreboard();
  },
  renderScoreboard(extra = '') {
    const m = world.match, me = world.local;
    if (!m) return;
    const row = (c) => {
      const hsp = c.stats.kills > 0 ? Math.round(c.stats.hs / c.stats.kills * 100) : 0;
      const adr = Math.round(c.stats.damage / Math.max(1, m.roundsPlayed || 1));
      const showMoney = world.mode !== 'dm' && (c.team === me.team || m.phase === 'over');
      return `<tr class="${c.alive ? '' : 'dead'} ${c === me ? 'me' : ''}"><td>${esc(c.name)}${c.isBot ? ' <small style="color:#777">BOT</small>' : ''}${c.inv.c4 && me.team === 'T' ? ' <span style="color:#f55">C4</span>' : ''}</td>`
        + `<td class="money">${showMoney ? '$' + c.money : ''}</td><td>${c.stats.kills}</td><td>${c.stats.assists}</td><td>${c.stats.deaths}</td>`
        + `<td>${c.stats.mvps ? '★' + c.stats.mvps : ''}</td><td>${hsp}%</td><td>${world.mode === 'dm' ? '' : adr}</td><td>${c.stats.score}</td></tr>`;
    };
    const head = '<tr><th>Oyuncu</th><th>Para</th><th>Ö</th><th>A</th><th>Ö(D)</th><th>MVP</th><th>KAFA%</th><th>ADR</th><th>SKOR</th></tr>';
    let html = `<div class="sb-head"><span>${world.map.name} · ${modeName(world.mode)}</span><span class="big">${world.mode === 'dm' ? fmtTime(m.timeLeft()) : `${m.score.CT} : ${m.score.T}`}</span><span>Tur ${m.round}${m.maxRounds ? ' / ' + m.maxRounds : ''}</span></div>`;
    html += extra;
    const sorter = (a, b) => b.stats.score - a.stats.score || b.stats.kills - a.stats.kills;
    if (world.mode === 'dm') {
      html += `<div class="sb-team dm"><div class="sb-team-title"><span>OYUNCULAR</span></div><table class="sb-table">${head}${[...world.chars].sort((a, b) => b.stats.kills - a.stats.kills).map(row).join('')}</table></div>`;
    } else {
      for (const t of ['CT', 'T']) {
        const list = world.chars.filter(c => c.team === t).sort(sorter);
        html += `<div class="sb-team ${t.toLowerCase()}"><div class="sb-team-title"><span>${t === 'CT' ? 'TERÖRLE MÜCADELE' : 'TERÖRİSTLER'}</span><span>${m.score[t]}</span></div><table class="sb-table">${head}${list.map(row).join('')}</table></div>`;
      }
      if (m.history.length) {
        html += '<div class="sb-rounds">' + m.history.map((h, i) => `<span class="${h.winner.toLowerCase()}${i === m.halfRounds ? ' half' : ''}" title="${esc(h.reason)}">${h.icon}</span>`).join('') + '</div>';
      }
    }
    $('scoreboard').innerHTML = html;
  },
  matchEnd(title, sub) {
    this.scoreboardOpen = false;
    const extra = `<div class="match-end-title">${esc(title)}</div><div style="text-align:center;color:#ccc;margin-bottom:10px">${esc(sub)}</div><div class="match-end-btns"><button class="btn primary" id="me-again">Tekrar Oyna</button><button class="btn" id="me-menu">Ana Menü</button></div>`;
    this.renderScoreboard(extra);
    $('scoreboard').classList.remove('hidden');
    $('scoreboard').style.pointerEvents = 'auto';
    $('me-again').onclick = () => world.ui.restartMatch();
    $('me-menu').onclick = () => world.ui.quitToMenu();
  },
  hideScoreboard() { $('scoreboard').classList.add('hidden'); $('scoreboard').style.pointerEvents = ''; this.scoreboardOpen = false; },
};

function reasonText(r) {
  return { kill: 'öldürme', win: 'tur galibiyeti', loss: 'kayıp bonusu', plant: 'bomba kurma', defuse: 'imha', buy: '', teamkill: 'takım arkadaşı öldürme', refund: 'iade', bonus: '' }[r] ?? '';
}
export function modeName(m) { return { competitive: 'Rekabetçi', casual: 'Sıradan', dm: 'Ölüm Maçı', practice: 'Antrenman' }[m] || m; }

export function drawXhair(ctx, S, c, gapExtra = 0) {
  ctx.clearRect(0, 0, S, S);
  const cx = S / 2, cy = S / 2;
  const len = Math.max(0, c.size * 2);
  const th = Math.max(1, Math.round(c.thickness * 2) / 1);
  const gap = Math.max(0, 4 + c.gap + gapExtra);
  ctx.globalAlpha = c.alpha;
  const rects = [];
  if (len > 0) {
    rects.push([cx + gap, cy - th / 2, len, th]);
    rects.push([cx - gap - len, cy - th / 2, len, th]);
    rects.push([cx - th / 2, cy + gap, th, len]);
    if (!c.tStyle) rects.push([cx - th / 2, cy - gap - len, th, len]);
  }
  if (c.dot) rects.push([cx - th / 2, cy - th / 2, th, th]);
  if (c.outline) {
    ctx.fillStyle = '#000';
    for (const [x, y, w, h] of rects) ctx.fillRect(Math.round(x) - 1, Math.round(y) - 1, w + 2, h + 2);
  }
  ctx.fillStyle = c.color;
  for (const [x, y, w, h] of rects) ctx.fillRect(Math.round(x), Math.round(y), w, h);
  ctx.globalAlpha = 1;
}
