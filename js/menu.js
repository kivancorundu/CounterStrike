// Main menu, mode/difficulty selection, settings (incl. crosshair editor).
import { world } from './state.js';
import { settings, saveSettings, resetSettings } from './settings.js';
import { audio } from './audio.js';
import { drawXhair } from './hud.js';

const $ = (id) => document.getElementById(id);

export const menu = {
  settingsInGame: false,
  init() {
    $('player-name').value = settings.playerName;
    $('player-name').addEventListener('input', e => { settings.playerName = e.target.value.trim().slice(0, 16) || 'Oyuncu'; saveSettings(); });
    document.querySelectorAll('.nav-btn').forEach(b => b.onclick = () => { audio.play('ui'); this.tab(b.dataset.tab); });
    document.querySelectorAll('.mode-card').forEach(c => {
      c.classList.toggle('active', c.dataset.mode === settings.mode);
      c.onclick = () => { audio.play('ui'); settings.mode = c.dataset.mode; saveSettings(); document.querySelectorAll('.mode-card').forEach(x => x.classList.toggle('active', x === c)); };
    });
    document.querySelectorAll('#diff-seg button').forEach(b => {
      b.classList.toggle('active', b.dataset.v === settings.difficulty);
      b.onclick = () => { audio.play('ui'); settings.difficulty = b.dataset.v; saveSettings(); document.querySelectorAll('#diff-seg button').forEach(x => x.classList.toggle('active', x === b)); };
    });
    $('go-btn').onclick = () => this.go();
    this.buildSettings();
    // map thumbnail
    const th = $('map-thumb').getContext('2d');
    const img = world.map.renderRadar(5);
    th.fillStyle = '#1a1712'; th.fillRect(0, 0, 300, 320);
    th.drawImage(img, 0, 0, img.width, img.height, 0, 0, 300, 320);
  },
  tab(name) {
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
    document.querySelectorAll('.mm-tab').forEach(t => t.classList.toggle('hidden', t.id !== 'tab-' + name));
  },
  show() {
    world.inMenu = true;
    world.running = false;
    this.settingsInGame = false;
    $('main-menu').classList.remove('hidden');
    $('main-menu').style.background = '';
    document.querySelector('.mm-nav').style.display = '';
    this.tab('play');
    world.ui.updateClickToPlay();
  },
  hide() { $('main-menu').classList.add('hidden'); },
  async go() {
    await audio.init(); audio.resume();
    audio.play('ui');
    const mode = settings.mode;
    const start = (team) => {
      this.hide();
      world.inMenu = false;
      this.loading(() => {
        world.game.start(mode, team, settings.difficulty);
        world.player.lock();
      });
    };
    if (mode === 'competitive' || mode === 'casual' || mode === 'practice') {
      this.hide();
      world.ui.showTeamSelect(start);
    } else start('DM');
  },
  loading(cb) {
    const el = $('loading');
    el.classList.remove('hidden');
    $('loading-text').textContent = 'Harita yükleniyor...';
    $('ld-fill').style.width = '0%';
    let p = 0;
    const iv = setInterval(() => { p = Math.min(100, p + 25); $('ld-fill').style.width = p + '%'; }, 80);
    setTimeout(() => { clearInterval(iv); el.classList.add('hidden'); cb(); }, 450);
  },

  openSettingsInGame() {
    this.settingsInGame = true;
    $('main-menu').classList.remove('hidden');
    $('main-menu').style.background = 'rgba(8,10,14,.92)';
    document.querySelector('.mm-nav').style.display = 'none';
    this.tab('settings');
    if (!$('set-back')) {
      const b = document.createElement('button');
      b.id = 'set-back'; b.className = 'btn primary'; b.textContent = 'Oyuna Dön';
      b.style.cssText = 'position:absolute;right:40px;bottom:30px';
      b.onclick = () => { this.closeSettingsInGame(); world.ui.openPause(); };
      $('tab-settings').appendChild(b);
    }
    $('set-back').style.display = '';
  },
  closeSettingsInGame() {
    this.settingsInGame = false;
    $('main-menu').classList.add('hidden');
    $('main-menu').style.background = '';
    document.querySelector('.mm-nav').style.display = '';
    if ($('set-back')) $('set-back').style.display = 'none';
  },

  buildSettings() {
    const root = $('settings-root');
    const col = (title, rows) => `<div class="set-col"><h3>${title}</h3>${rows.join('')}</div>`;
    const range = (key, label, min, max, step, obj = 'settings') => `<div class="set-row"><label>${label}</label><span><input type="range" data-k="${key}" data-o="${obj}" min="${min}" max="${max}" step="${step}"><span class="val" data-v="${key}"></span></span></div>`;
    const check = (key, label, obj = 'settings') => `<div class="set-row"><label>${label}</label><input type="checkbox" data-k="${key}" data-o="${obj}"></div>`;
    const select = (key, label, opts, obj = 'settings') => `<div class="set-row"><label>${label}</label><select data-k="${key}" data-o="${obj}">${opts.map(([v, t]) => `<option value="${v}">${t}</option>`).join('')}</select></div>`;
    root.innerHTML =
      col('FARE & GÖRÜNTÜ', [
        range('sens', 'Hassasiyet', 0.1, 8, 0.05),
        range('zoomSensRatio', 'Dürbün hassasiyet oranı', 0.2, 2, 0.05),
        check('invertY', 'Fareyi ters çevir'),
        select('graphics', 'Grafik kalitesi', [['low', 'Düşük (en hızlı)'], ['medium', 'Orta'], ['high', 'Yüksek']]),
        range('vmFov', 'Silah görüş açısı (viewmodel FOV)', 54, 68, 1),
        check('vmBob', 'Silah sallanması'),
        check('showFps', 'FPS göster'),
        check('radarRotate', 'Radar döner'),
        range('radarZoom', 'Radar yakınlaştırma', 0, 1, 0.05),
        range('volume', 'Ses düzeyi', 0, 1, 0.01),
        '<div class="set-row"><label>Varsayılanlara dön</label><button class="btn" id="set-reset">Sıfırla</button></div>',
      ]) +
      col('NİŞANGAH', [
        '<div class="xhair-preview"><canvas id="xh-prev" width="96" height="96"></canvas></div>',
        select('style', 'Stil', [['static', 'Klasik statik'], ['dynamic', 'Klasik dinamik']], 'crosshair'),
        '<div class="set-row"><label>Renk</label><input type="color" data-k="color" data-o="crosshair"></div>',
        range('size', 'Uzunluk', 0, 10, 0.5, 'crosshair'),
        range('thickness', 'Kalınlık', 0.5, 4, 0.5, 'crosshair'),
        range('gap', 'Boşluk', -4, 8, 1, 'crosshair'),
        range('alpha', 'Opaklık', 0.2, 1, 0.05, 'crosshair'),
        check('dot', 'Merkez nokta', 'crosshair'),
        check('outline', 'Kontur', 'crosshair'),
        check('tStyle', 'T stili', 'crosshair'),
      ]);
    const getObj = (o) => o === 'crosshair' ? settings.crosshair : settings;
    const refresh = () => {
      root.querySelectorAll('[data-k]').forEach(el => {
        const o = getObj(el.dataset.o), k = el.dataset.k;
        if (el.type === 'checkbox') el.checked = !!o[k]; else el.value = o[k];
        const v = root.querySelector(`[data-v="${k}"]`);
        if (v && el.dataset.o === (v.closest('.set-row').querySelector('[data-k]').dataset.o)) v.textContent = typeof o[k] === 'number' ? (+o[k]).toFixed(el.step && el.step < 1 ? 2 : 0) : o[k];
      });
      drawXhair($('xh-prev').getContext('2d'), 96, settings.crosshair);
    };
    root.querySelectorAll('[data-k]').forEach(el => {
      el.addEventListener('input', () => {
        const o = getObj(el.dataset.o), k = el.dataset.k;
        if (el.type === 'checkbox') o[k] = el.checked;
        else if (el.type === 'range') o[k] = parseFloat(el.value);
        else o[k] = el.value;
        saveSettings();
        if (k === 'volume') audio.setVolume(settings.volume);
        if (k === 'graphics') world.applyGraphics?.();
        world.hud && (world.hud.lastXhairKey = '');
        refresh();
      });
    });
    $('set-reset').onclick = () => { resetSettings(); audio.setVolume(settings.volume); world.applyGraphics?.(); refresh(); };
    refresh();
  },
};
