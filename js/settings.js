// Persistent user settings (localStorage).
const KEY = 'webstrike.settings.v1';

const DEFAULTS = {
  playerName: 'Oyuncu',
  sens: 1.6,             // CS-style sensitivity (m_yaw 0.022)
  zoomSensRatio: 1.0,
  invertY: false,
  volume: 0.7,
  graphics: 'medium',    // low | medium | high
  showFps: true,
  radarRotate: true,
  radarZoom: 0.65,
  vmFov: 68,
  vmBob: true,
  mode: 'competitive',
  difficulty: 'normal',
  crosshair: {
    style: 'static',     // static | dynamic
    color: '#4cff4c',
    size: 3,
    thickness: 1,
    gap: -1,
    dot: false,
    outline: true,
    alpha: 1,
    tStyle: false,
  },
};

function deepMerge(base, over) {
  const out = Array.isArray(base) ? [...base] : { ...base };
  if (!over || typeof over !== 'object') return out;
  for (const k of Object.keys(base)) {
    if (over[k] === undefined) continue;
    if (base[k] && typeof base[k] === 'object' && !Array.isArray(base[k])) out[k] = deepMerge(base[k], over[k]);
    else out[k] = over[k];
  }
  return out;
}

function load() {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return deepMerge(DEFAULTS, JSON.parse(raw));
  } catch (e) { /* storage unavailable */ }
  return deepMerge(DEFAULTS, {});
}

export const settings = load();

export function saveSettings() {
  try { localStorage.setItem(KEY, JSON.stringify(settings)); } catch (e) { /* ignore */ }
}

export function resetSettings() {
  const d = deepMerge(DEFAULTS, {});
  for (const k of Object.keys(d)) settings[k] = d[k];
  saveSettings();
}
