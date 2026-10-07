// Fully synthesized positional audio (WebAudio). Sounds are pre-rendered with
// OfflineAudioContext into buffers at startup, then played through panners.
import { settings } from './settings.js';

const SR = 44100;
let ctx = null, master = null, sfxBus = null;
const buffers = {};
let listenerPos = { x: 0, y: 0, z: 0 };

function noiseArray(len, seed = 1) {
  const a = new Float32Array(len);
  let s = seed;
  for (let i = 0; i < len; i++) { s = (s * 16807) % 2147483647; a[i] = (s / 2147483647) * 2 - 1; }
  return a;
}

async function render(dur, build) {
  const oc = new OfflineAudioContext(1, Math.ceil(SR * dur), SR);
  build(oc, oc.destination);
  return oc.startRendering();
}

function noiseSrc(oc, dur, seed) {
  const b = oc.createBuffer(1, Math.ceil(SR * dur), SR);
  b.copyToChannel(noiseArray(b.length, seed), 0);
  const s = oc.createBufferSource(); s.buffer = b; return s;
}
function env(oc, node, t0, a, peak, d, sustain = 0) {
  const g = oc.createGain();
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.exponentialRampToValueAtTime(peak, t0 + a);
  g.gain.exponentialRampToValueAtTime(Math.max(0.0001, sustain || 0.0001), t0 + a + d);
  node.connect(g);
  return g;
}
function filt(oc, type, f, q = 1) { const b = oc.createBiquadFilter(); b.type = type; b.frequency.value = f; b.Q.value = q; return b; }

// ---- recipes ----
function gunRecipe({ dur = 0.5, crack = 2500, crackQ = 0.8, body = 110, bodyDrop = 45, bodyGain = 0.9, noiseGain = 1, decay = 0.25, tail = 0.4, tailF = 900, seed = 3 }) {
  return (oc, out) => {
    const t = 0;
    // sharp crack
    const n1 = noiseSrc(oc, dur, seed);
    const f1 = filt(oc, 'bandpass', crack, crackQ);
    n1.connect(f1);
    env(oc, f1, t, 0.001, noiseGain, decay * 0.5).connect(out);
    n1.start(t);
    // body thump
    const o = oc.createOscillator(); o.type = 'sine';
    o.frequency.setValueAtTime(body, t); o.frequency.exponentialRampToValueAtTime(bodyDrop, t + decay);
    env(oc, o, t, 0.002, bodyGain, decay).connect(out);
    o.start(t); o.stop(t + dur);
    // low noise tail (reverb-ish)
    const n2 = noiseSrc(oc, dur, seed + 7);
    const f2 = filt(oc, 'lowpass', tailF, 0.5);
    n2.connect(f2);
    env(oc, f2, t, 0.004, tail, dur * 0.9).connect(out);
    n2.start(t);
  };
}

const RECIPES = {
  ak: [0.7, gunRecipe({ crack: 1800, body: 130, bodyDrop: 50, bodyGain: 1.0, decay: 0.22, tail: 0.5, tailF: 700, seed: 11 })],
  m4: [0.6, gunRecipe({ crack: 2600, body: 150, bodyDrop: 60, bodyGain: 0.8, decay: 0.18, tail: 0.4, tailF: 1000, seed: 21 })],
  rifle: [0.6, gunRecipe({ crack: 2200, body: 140, bodyDrop: 55, bodyGain: 0.85, decay: 0.2, tail: 0.45, tailF: 850, seed: 31 })],
  smg: [0.45, gunRecipe({ crack: 3200, body: 180, bodyDrop: 80, bodyGain: 0.6, decay: 0.12, tail: 0.3, tailF: 1300, seed: 41 })],
  pistol: [0.45, gunRecipe({ crack: 3000, body: 200, bodyDrop: 70, bodyGain: 0.6, decay: 0.13, tail: 0.3, tailF: 1200, seed: 51 })],
  deagle: [0.8, gunRecipe({ crack: 1500, body: 110, bodyDrop: 40, bodyGain: 1.1, decay: 0.3, tail: 0.6, tailF: 600, seed: 61 })],
  shotgun: [0.8, gunRecipe({ crack: 1200, crackQ: 0.5, body: 90, bodyDrop: 35, bodyGain: 1.2, decay: 0.3, tail: 0.7, tailF: 600, seed: 71 })],
  awp: [1.4, gunRecipe({ dur: 1.4, crack: 1100, crackQ: 0.6, body: 80, bodyDrop: 28, bodyGain: 1.4, decay: 0.45, tail: 0.9, tailF: 500, seed: 81 })],
  scout: [1.0, gunRecipe({ dur: 1.0, crack: 2000, body: 120, bodyDrop: 40, bodyGain: 1.0, decay: 0.3, tail: 0.6, tailF: 800, seed: 91 })],
  silenced: [0.3, gunRecipe({ dur: 0.3, crack: 1200, crackQ: 1.5, body: 300, bodyDrop: 120, bodyGain: 0.25, noiseGain: 0.35, decay: 0.08, tail: 0.05, tailF: 1500, seed: 101 })],
  taser: [0.5, (oc, out) => {
    const o = oc.createOscillator(); o.type = 'sawtooth'; o.frequency.value = 60;
    const g = env(oc, o, 0, 0.005, 0.5, 0.45, 0.2); g.connect(out); o.start(); o.stop(0.5);
    const n = noiseSrc(oc, 0.5, 5); const f = filt(oc, 'highpass', 3000); n.connect(f); env(oc, f, 0, 0.002, 0.5, 0.4).connect(out); n.start();
  }],
  click: [0.08, (oc, out) => { const n = noiseSrc(oc, 0.08, 9); const f = filt(oc, 'bandpass', 4000, 3); n.connect(f); env(oc, f, 0, 0.001, 0.6, 0.03).connect(out); n.start(); }],
  magout: [0.2, (oc, out) => { const n = noiseSrc(oc, 0.2, 19); const f = filt(oc, 'bandpass', 1800, 4); n.connect(f); env(oc, f, 0, 0.002, 0.5, 0.08).connect(out); n.start(); }],
  magin: [0.25, (oc, out) => { const n = noiseSrc(oc, 0.25, 29); const f = filt(oc, 'bandpass', 1200, 3); n.connect(f); env(oc, f, 0, 0.002, 0.8, 0.1).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.value = 240; env(oc, o, 0, 0.002, 0.2, 0.06).connect(out); o.start(); o.stop(0.2); }],
  bolt: [0.3, (oc, out) => { for (const [t, f] of [[0, 2200], [0.12, 1500]]) { const n = noiseSrc(oc, 0.3, 39 + f); const b = filt(oc, 'bandpass', f, 5); n.connect(b); env(oc, b, t, 0.002, 0.7, 0.05).connect(out); n.start(t); } }],
  deploy: [0.25, (oc, out) => { const n = noiseSrc(oc, 0.25, 49); const f = filt(oc, 'bandpass', 2500, 2); n.connect(f); env(oc, f, 0, 0.01, 0.25, 0.15).connect(out); n.start(); }],
  zoom: [0.12, (oc, out) => { const n = noiseSrc(oc, 0.12, 59); const f = filt(oc, 'bandpass', 5000, 6); n.connect(f); env(oc, f, 0, 0.001, 0.35, 0.05).connect(out); n.start(); }],
  step: [0.18, (oc, out) => { const n = noiseSrc(oc, 0.18, 69); const f = filt(oc, 'lowpass', 700, 1); n.connect(f); env(oc, f, 0, 0.004, 0.9, 0.09).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.setValueAtTime(90, 0); o.frequency.exponentialRampToValueAtTime(50, 0.08); env(oc, o, 0, 0.002, 0.5, 0.07).connect(out); o.start(); o.stop(0.18); }],
  step2: [0.18, (oc, out) => { const n = noiseSrc(oc, 0.18, 79); const f = filt(oc, 'lowpass', 900, 1); n.connect(f); env(oc, f, 0, 0.004, 0.8, 0.08).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.setValueAtTime(110, 0); o.frequency.exponentialRampToValueAtTime(55, 0.08); env(oc, o, 0, 0.002, 0.45, 0.07).connect(out); o.start(); o.stop(0.18); }],
  stepmetal: [0.25, (oc, out) => { const n = noiseSrc(oc, 0.25, 89); const f = filt(oc, 'bandpass', 2200, 6); n.connect(f); env(oc, f, 0, 0.002, 0.5, 0.15).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.value = 420; env(oc, o, 0, 0.002, 0.2, 0.15).connect(out); o.start(); o.stop(0.25); }],
  land: [0.3, (oc, out) => { const n = noiseSrc(oc, 0.3, 99); const f = filt(oc, 'lowpass', 500, 1); n.connect(f); env(oc, f, 0, 0.004, 1.0, 0.15).connect(out); n.start(); }],
  knife: [0.3, (oc, out) => { const n = noiseSrc(oc, 0.3, 109); const f = filt(oc, 'bandpass', 3000, 1); f.frequency.setValueAtTime(1500, 0); f.frequency.exponentialRampToValueAtTime(5000, 0.18); n.connect(f); env(oc, f, 0, 0.05, 0.5, 0.12).connect(out); n.start(); }],
  knifehit: [0.3, (oc, out) => { const n = noiseSrc(oc, 0.3, 119); const f = filt(oc, 'lowpass', 1200); n.connect(f); env(oc, f, 0, 0.002, 1.0, 0.12).connect(out); n.start(); }],
  flesh: [0.2, (oc, out) => { const n = noiseSrc(oc, 0.2, 129); const f = filt(oc, 'lowpass', 900, 2); n.connect(f); env(oc, f, 0, 0.002, 0.9, 0.08).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.setValueAtTime(160, 0); o.frequency.exponentialRampToValueAtTime(60, 0.1); env(oc, o, 0, 0.002, 0.5, 0.1).connect(out); o.start(); o.stop(0.2); }],
  helmet: [0.5, (oc, out) => { for (const f of [2900, 4100, 5300]) { const o = oc.createOscillator(); o.type = 'sine'; o.frequency.value = f; env(oc, o, 0, 0.001, 0.25, 0.4).connect(out); o.start(); o.stop(0.5); } const n = noiseSrc(oc, 0.1, 139); const b = filt(oc, 'highpass', 3000); n.connect(b); env(oc, b, 0, 0.001, 0.6, 0.04).connect(out); n.start(); }],
  headshot: [0.35, (oc, out) => { const o = oc.createOscillator(); o.type = 'triangle'; o.frequency.setValueAtTime(1800, 0); o.frequency.exponentialRampToValueAtTime(900, 0.2); env(oc, o, 0, 0.001, 0.4, 0.25).connect(out); o.start(); o.stop(0.35); const n = noiseSrc(oc, 0.15, 149); const f = filt(oc, 'lowpass', 1500); n.connect(f); env(oc, f, 0, 0.001, 0.8, 0.08).connect(out); n.start(); }],
  impact: [0.15, (oc, out) => { const n = noiseSrc(oc, 0.15, 159); const f = filt(oc, 'bandpass', 2500, 1.5); n.connect(f); env(oc, f, 0, 0.001, 0.5, 0.06).connect(out); n.start(); }],
  impactwood: [0.18, (oc, out) => { const n = noiseSrc(oc, 0.18, 169); const f = filt(oc, 'bandpass', 900, 2); n.connect(f); env(oc, f, 0, 0.001, 0.7, 0.07).connect(out); n.start(); }],
  impactmetal: [0.4, (oc, out) => { const o = oc.createOscillator(); o.frequency.value = 1700; env(oc, o, 0, 0.001, 0.2, 0.3).connect(out); o.start(); o.stop(0.4); const n = noiseSrc(oc, 0.1, 179); const f = filt(oc, 'highpass', 2500); n.connect(f); env(oc, f, 0, 0.001, 0.5, 0.04).connect(out); n.start(); }],
  ricochet: [0.4, (oc, out) => { const o = oc.createOscillator(); o.frequency.setValueAtTime(3500, 0); o.frequency.exponentialRampToValueAtTime(1200, 0.35); env(oc, o, 0, 0.001, 0.15, 0.35).connect(out); o.start(); o.stop(0.4); }],
  explosion: [2.5, (oc, out) => {
    const n = noiseSrc(oc, 2.5, 189); const f = filt(oc, 'lowpass', 1200, 0.7); f.frequency.setValueAtTime(3000, 0); f.frequency.exponentialRampToValueAtTime(200, 2.0); n.connect(f); env(oc, f, 0, 0.005, 1.4, 2.2).connect(out); n.start();
    const o = oc.createOscillator(); o.frequency.setValueAtTime(70, 0); o.frequency.exponentialRampToValueAtTime(25, 1.2); env(oc, o, 0, 0.005, 1.5, 1.3).connect(out); o.start(); o.stop(2.5);
  }],
  flashbang: [1.0, (oc, out) => { const n = noiseSrc(oc, 1, 199); const f = filt(oc, 'highpass', 1500); n.connect(f); env(oc, f, 0, 0.001, 1.2, 0.5).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.setValueAtTime(140, 0); o.frequency.exponentialRampToValueAtTime(50, 0.3); env(oc, o, 0, 0.002, 1.0, 0.35).connect(out); o.start(); o.stop(1); }],
  ring: [5.0, (oc, out) => { const o = oc.createOscillator(); o.frequency.value = 3400; const g = env(oc, o, 0, 0.02, 0.25, 4.8); g.connect(out); o.start(); o.stop(5); const o2 = oc.createOscillator(); o2.frequency.value = 3420; env(oc, o2, 0, 0.02, 0.15, 4.5).connect(out); o2.start(); o2.stop(5); }],
  smoke: [3.0, (oc, out) => { const n = noiseSrc(oc, 3, 209); const f = filt(oc, 'bandpass', 2500, 0.6); n.connect(f); env(oc, f, 0, 0.1, 0.7, 2.8, 0.05).connect(out); n.start(); }],
  bounce: [0.12, (oc, out) => { const o = oc.createOscillator(); o.frequency.value = 700; env(oc, o, 0, 0.001, 0.3, 0.08).connect(out); o.start(); o.stop(0.12); const n = noiseSrc(oc, 0.1, 219); const f = filt(oc, 'bandpass', 2000, 2); n.connect(f); env(oc, f, 0, 0.001, 0.4, 0.04).connect(out); n.start(); }],
  glass: [0.6, (oc, out) => { const n = noiseSrc(oc, 0.6, 229); const f = filt(oc, 'highpass', 4000); n.connect(f); env(oc, f, 0, 0.001, 0.8, 0.4).connect(out); n.start(); for (const fr of [4200, 5600, 7100]) { const o = oc.createOscillator(); o.frequency.value = fr; env(oc, o, Math.random() * 0.05, 0.001, 0.12, 0.25).connect(out); o.start(); o.stop(0.6); } }],
  fire: [2.0, (oc, out) => { const n = noiseSrc(oc, 2, 239); const f = filt(oc, 'lowpass', 600, 0.5); n.connect(f); const g = oc.createGain(); g.gain.value = 0.5; f.connect(g); g.connect(out); n.start(); const n2 = noiseSrc(oc, 2, 249); const f2 = filt(oc, 'bandpass', 3000, 1); n2.connect(f2); const g2 = oc.createGain(); g2.gain.value = 0.12; f2.connect(g2); g2.connect(out); n2.start(); }],
  pin: [0.2, (oc, out) => { const o = oc.createOscillator(); o.frequency.value = 2600; env(oc, o, 0, 0.001, 0.2, 0.1).connect(out); o.start(); o.stop(0.2); }],
  throw: [0.3, (oc, out) => { const n = noiseSrc(oc, 0.3, 259); const f = filt(oc, 'bandpass', 1200, 1); n.connect(f); env(oc, f, 0, 0.06, 0.4, 0.15).connect(out); n.start(); }],
  beep: [0.15, (oc, out) => { const o = oc.createOscillator(); o.type = 'square'; o.frequency.value = 2900; const f = filt(oc, 'lowpass', 5000); o.connect(f); env(oc, f, 0, 0.002, 0.18, 0.1).connect(out); o.start(); o.stop(0.15); }],
  keypress: [0.08, (oc, out) => { const o = oc.createOscillator(); o.type = 'square'; o.frequency.value = 1700; const f = filt(oc, 'lowpass', 4000); o.connect(f); env(oc, f, 0, 0.001, 0.12, 0.05).connect(out); o.start(); o.stop(0.08); }],
  armed: [0.6, (oc, out) => { for (let k = 0; k < 3; k++) { const o = oc.createOscillator(); o.type = 'square'; o.frequency.value = 1500 + k * 400; const f = filt(oc, 'lowpass', 4000); o.connect(f); env(oc, f, k * 0.15, 0.002, 0.15, 0.1).connect(out); o.start(k * 0.15); o.stop(k * 0.15 + 0.14); } }],
  defuse: [0.4, (oc, out) => { const n = noiseSrc(oc, 0.4, 269); const f = filt(oc, 'bandpass', 3500, 6); n.connect(f); env(oc, f, 0, 0.002, 0.3, 0.2).connect(out); n.start(); }],
  pickup: [0.2, (oc, out) => { const n = noiseSrc(oc, 0.2, 279); const f = filt(oc, 'bandpass', 1500, 3); n.connect(f); env(oc, f, 0, 0.005, 0.5, 0.1).connect(out); n.start(); }],
  buy: [0.25, (oc, out) => { const n = noiseSrc(oc, 0.25, 289); const f = filt(oc, 'bandpass', 2000, 2); n.connect(f); env(oc, f, 0, 0.004, 0.5, 0.12).connect(out); n.start(); const o = oc.createOscillator(); o.frequency.value = 880; env(oc, o, 0.05, 0.002, 0.1, 0.1).connect(out); o.start(0.05); o.stop(0.25); }],
  ui: [0.08, (oc, out) => { const o = oc.createOscillator(); o.frequency.value = 1200; env(oc, o, 0, 0.001, 0.12, 0.05).connect(out); o.start(); o.stop(0.08); }],
  deny: [0.2, (oc, out) => { const o = oc.createOscillator(); o.type = 'square'; o.frequency.value = 180; const f = filt(oc, 'lowpass', 1200); o.connect(f); env(oc, f, 0, 0.002, 0.15, 0.15).connect(out); o.start(); o.stop(0.2); }],
  roundstart: [1.6, (oc, out) => { const notes = [392, 523.25, 659.25]; notes.forEach((fq, k) => { const o = oc.createOscillator(); o.type = 'sawtooth'; o.frequency.value = fq; const f = filt(oc, 'lowpass', 1800); o.connect(f); env(oc, f, k * 0.12, 0.02, 0.12, 1.1).connect(out); o.start(k * 0.12); o.stop(1.6); }); }],
  winct: [2.5, (oc, out) => { const notes = [[293.66, 0], [369.99, 0.18], [440, 0.36], [587.33, 0.54]]; notes.forEach(([fq, t]) => { for (const det of [0, 3]) { const o = oc.createOscillator(); o.type = 'sawtooth'; o.frequency.value = fq + det; const f = filt(oc, 'lowpass', 2200); o.connect(f); env(oc, f, t, 0.03, 0.09, 1.8).connect(out); o.start(t); o.stop(2.5); } }); }],
  wint: [2.5, (oc, out) => { const notes = [[220, 0], [261.63, 0.18], [329.63, 0.36], [392, 0.54]]; notes.forEach(([fq, t]) => { for (const det of [0, -3]) { const o = oc.createOscillator(); o.type = 'square'; o.frequency.value = fq + det; const f = filt(oc, 'lowpass', 1600); o.connect(f); env(oc, f, t, 0.03, 0.07, 1.8).connect(out); o.start(t); o.stop(2.5); } }); }],
  lose: [2.0, (oc, out) => { [[329.63, 0], [293.66, 0.25], [220, 0.5]].forEach(([fq, t]) => { const o = oc.createOscillator(); o.type = 'triangle'; o.frequency.value = fq; env(oc, o, t, 0.03, 0.2, 1.2).connect(out); o.start(t); o.stop(2); }); }],
  tenSec: [0.5, (oc, out) => { const o = oc.createOscillator(); o.type = 'triangle'; o.frequency.value = 660; env(oc, o, 0, 0.005, 0.25, 0.4).connect(out); o.start(); o.stop(0.5); }],
  hurt: [0.25, (oc, out) => { const n = noiseSrc(oc, 0.25, 299); const f = filt(oc, 'lowpass', 600); n.connect(f); env(oc, f, 0, 0.003, 0.8, 0.15).connect(out); n.start(); }],
  death: [0.6, (oc, out) => { const o = oc.createOscillator(); o.type = 'sawtooth'; o.frequency.setValueAtTime(220, 0); o.frequency.exponentialRampToValueAtTime(70, 0.5); const f = filt(oc, 'lowpass', 900); o.connect(f); env(oc, f, 0, 0.01, 0.25, 0.5).connect(out); o.start(); o.stop(0.6); }],
};

export const audio = {
  ready: false,
  async init() {
    if (ctx) return;
    ctx = new (window.AudioContext || window.webkitAudioContext)();
    master = ctx.createGain();
    master.gain.value = settings.volume;
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -10; comp.ratio.value = 4;
    master.connect(comp); comp.connect(ctx.destination);
    sfxBus = master;
    const jobs = Object.entries(RECIPES).map(async ([k, [dur, fn]]) => { try { buffers[k] = await render(dur, fn); } catch (e) { /* skip */ } });
    await Promise.all(jobs);
    this.ready = true;
  },
  resume() { if (ctx && ctx.state !== 'running') ctx.resume(); },
  setVolume(v) { if (master) master.gain.value = v; },
  setListener(pos, fwd) {
    if (!ctx) return;
    listenerPos = pos;
    const L = ctx.listener;
    if (L.positionX) {
      L.positionX.value = pos.x; L.positionY.value = pos.y; L.positionZ.value = pos.z;
      L.forwardX.value = fwd.x; L.forwardY.value = fwd.y; L.forwardZ.value = fwd.z;
      L.upX.value = 0; L.upY.value = 1; L.upZ.value = 0;
    } else {
      L.setPosition(pos.x, pos.y, pos.z);
      L.setOrientation(fwd.x, fwd.y, fwd.z, 0, 1, 0);
    }
  },
  // play(name, {pos, volume, rate, maxDist, muffle})
  play(name, opts = {}) {
    if (!ctx || !buffers[name] || ctx.state !== 'running') return null;
    const src = ctx.createBufferSource();
    src.buffer = buffers[name];
    src.playbackRate.value = (opts.rate || 1) * (opts.jitter ? 1 + (Math.random() - 0.5) * opts.jitter : 1);
    const g = ctx.createGain();
    g.gain.value = opts.volume ?? 1;
    let node = src;
    if (opts.pos) {
      const dx = opts.pos.x - listenerPos.x, dy = opts.pos.y - listenerPos.y, dz = opts.pos.z - listenerPos.z;
      const d = Math.sqrt(dx * dx + dy * dy + dz * dz);
      const maxD = opts.maxDist || 3000;
      if (d > maxD) return null;
      // distance muffling
      const lp = ctx.createBiquadFilter(); lp.type = 'lowpass';
      lp.frequency.value = Math.max(600, 16000 * Math.pow(1 - Math.min(d / maxD, 1), 2) + (opts.muffle ? -12000 : 0));
      const p = ctx.createPanner();
      p.panningModel = 'equalpower'; p.distanceModel = 'inverse';
      p.refDistance = opts.ref || 180; p.rolloffFactor = opts.rolloff ?? 1.1; p.maxDistance = maxD;
      if (p.positionX) { p.positionX.value = opts.pos.x; p.positionY.value = opts.pos.y; p.positionZ.value = opts.pos.z; }
      else p.setPosition(opts.pos.x, opts.pos.y, opts.pos.z);
      src.connect(lp); lp.connect(p); p.connect(g);
    } else {
      src.connect(g);
    }
    g.connect(sfxBus);
    src.start();
    return { src, gain: g };
  },
  loop(name, pos, volume = 1) {
    const h = this.play(name, { pos, volume, maxDist: 2000 });
    if (h) h.src.loop = true;
    return h;
  },
  stop(h) { try { if (h) h.src.stop(); } catch (e) { /* already stopped */ } },
};

export function gunSound(def, silenced) {
  if (silenced) return 'silenced';
  return def.sound || 'rifle';
}
