// Global, shared game state. Every module imports `world` and reads/writes it.
// Units are Source-style "hammer units" (1 HU ≈ 1 inch) so CS values can be used directly.

export const CELL = 64;              // map grid cell size
export const PLAYER_RADIUS = 16;     // hull half-width (32x32 hull)
export const STAND_HEIGHT = 72;
export const CROUCH_HEIGHT = 54;
export const STAND_EYE = 64;
export const CROUCH_EYE = 46;
export const GRAVITY = 800;          // sv_gravity
export const JUMP_VELOCITY = 301.993;
export const STEP_HEIGHT = 18;
export const FRICTION = 5.2;         // sv_friction
export const STOP_SPEED = 80;        // sv_stopspeed
export const ACCELERATE = 5.5;       // sv_accelerate
export const AIR_ACCELERATE = 12;    // sv_airaccelerate
export const AIR_WISHSPEED = 30;     // sv_air_max_wishspeed
export const WALK_MULT = 0.52;
export const DUCK_MULT = 0.34;
export const DEG = Math.PI / 180;

export const world = {
  renderer: null, scene: null, camera: null,
  vmScene: null, vmCamera: null,
  map: null,
  chars: [],
  local: null,
  dropped: [],
  projectiles: [],
  smokes: [],
  fires: [],
  bomb: null,
  time: 0,
  dt: 0,
  mode: 'competitive',
  match: null,
  running: false,
  paused: false,
  inMenu: true,
  spectating: null,
  difficulty: 'normal',
};

export function isEnemy(a, b) {
  if (a === b) return false;
  if (world.mode === 'dm') return true;
  return a.team !== b.team;
}

export function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
export function lerp(a, b, t) { return a + (b - a) * t; }
export function rand(a, b) { return a + Math.random() * (b - a); }
export function randInt(a, b) { return Math.floor(rand(a, b + 1)); }
export function pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; }
export function angleDiff(a, b) {
  let d = (b - a) % (Math.PI * 2);
  if (d > Math.PI) d -= Math.PI * 2;
  if (d < -Math.PI) d += Math.PI * 2;
  return d;
}
export function shuffle(arr) {
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
  return arr;
}
// forward vector from yaw/pitch (yaw 0 = -Z, positive yaw turns left, like three.js)
export function dirFromAngles(yaw, pitch, out) {
  const cp = Math.cos(pitch);
  out.x = -Math.sin(yaw) * cp;
  out.y = Math.sin(pitch);
  out.z = -Math.cos(yaw) * cp;
  return out;
}
export function anglesFromDir(dx, dy, dz) {
  const yaw = Math.atan2(-dx, -dz);
  const pitch = Math.atan2(dy, Math.hypot(dx, dz));
  return { yaw, pitch };
}
export function fmtTime(sec) {
  sec = Math.max(0, Math.ceil(sec));
  const m = Math.floor(sec / 60), s = sec % 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}

// simple scheduled callbacks driven by game time
const timers = [];
export function after(delay, fn) { timers.push({ at: world.time + delay, fn }); }
export function runTimers() {
  for (let i = timers.length - 1; i >= 0; i--) {
    if (world.time >= timers[i].at) { const t = timers[i]; timers.splice(i, 1); try { t.fn(); } catch (e) { console.error(e); } }
  }
}
export function clearTimers() { timers.length = 0; }
