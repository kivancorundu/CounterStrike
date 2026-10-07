// Procedurally generated textures (no external image assets).
import * as THREE from './vendor/three.module.min.js';

function seeded(seed) {
  let s = seed >>> 0;
  return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
}

function canvas(w, h) {
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  return c;
}

function grain(ctx, w, h, rnd, amount, alpha) {
  const img = ctx.getImageData(0, 0, w, h);
  const d = img.data;
  for (let i = 0; i < d.length; i += 4) {
    const n = (rnd() - 0.5) * amount;
    d[i] = Math.max(0, Math.min(255, d[i] + n));
    d[i + 1] = Math.max(0, Math.min(255, d[i + 1] + n));
    d[i + 2] = Math.max(0, Math.min(255, d[i + 2] + n * 0.9));
    if (alpha) d[i + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
}

function blotches(ctx, w, h, rnd, count, colors, rMin, rMax, alpha) {
  for (let i = 0; i < count; i++) {
    const x = rnd() * w, y = rnd() * h, r = rMin + rnd() * (rMax - rMin);
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    const col = colors[Math.floor(rnd() * colors.length)];
    g.addColorStop(0, col.replace('A', alpha));
    g.addColorStop(1, col.replace('A', 0));
    ctx.fillStyle = g;
    // wrap around for tiling
    for (const ox of [-w, 0, w]) for (const oy of [-h, 0, h]) {
      ctx.save(); ctx.translate(ox, oy); ctx.fillRect(x - r, y - r, r * 2, r * 2); ctx.restore();
    }
  }
}

function toTex(c, repeat = true) {
  const t = new THREE.CanvasTexture(c);
  if (repeat) { t.wrapS = t.wrapT = THREE.RepeatWrapping; }
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

export function makeSand() {
  const S = 256, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(11);
  ctx.fillStyle = '#c4a172'; ctx.fillRect(0, 0, S, S);
  blotches(ctx, S, S, rnd, 40, ['rgba(150,118,78,A)', 'rgba(214,186,140,A)', 'rgba(170,140,98,A)'], 20, 70, 0.35);
  for (let i = 0; i < 900; i++) {
    ctx.fillStyle = rnd() > 0.5 ? 'rgba(90,70,45,0.35)' : 'rgba(235,215,175,0.35)';
    ctx.fillRect(rnd() * S, rnd() * S, 1 + rnd() * 2, 1 + rnd() * 2);
  }
  grain(ctx, S, S, rnd, 22);
  return toTex(c);
}

export function makeTile() {
  const S = 256, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(23);
  ctx.fillStyle = '#b39a76'; ctx.fillRect(0, 0, S, S);
  const n = 4, ts = S / n;
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
    const v = 0.85 + rnd() * 0.3;
    ctx.fillStyle = `rgb(${Math.floor(178 * v)},${Math.floor(156 * v)},${Math.floor(122 * v)})`;
    ctx.fillRect(x * ts + 2, y * ts + 2, ts - 4, ts - 4);
  }
  ctx.strokeStyle = 'rgba(70,55,38,0.8)'; ctx.lineWidth = 3;
  for (let i = 0; i <= n; i++) {
    ctx.beginPath(); ctx.moveTo(i * ts, 0); ctx.lineTo(i * ts, S); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(0, i * ts); ctx.lineTo(S, i * ts); ctx.stroke();
  }
  blotches(ctx, S, S, rnd, 25, ['rgba(90,72,50,A)', 'rgba(220,200,165,A)'], 15, 50, 0.25);
  grain(ctx, S, S, rnd, 18);
  return toTex(c);
}

export function makePlaster(base = '#d6c097', seed = 5) {
  const S = 256, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(seed);
  ctx.fillStyle = base; ctx.fillRect(0, 0, S, S);
  blotches(ctx, S, S, rnd, 50, ['rgba(160,130,90,A)', 'rgba(240,225,195,A)', 'rgba(140,110,80,A)'], 10, 60, 0.3);
  // cracks
  ctx.strokeStyle = 'rgba(90,70,50,0.35)'; ctx.lineWidth = 1;
  for (let i = 0; i < 8; i++) {
    let x = rnd() * S, y = rnd() * S;
    ctx.beginPath(); ctx.moveTo(x, y);
    for (let k = 0; k < 6; k++) { x += (rnd() - 0.5) * 30; y += rnd() * 20; ctx.lineTo(x, y); }
    ctx.stroke();
  }
  // exposed bricks patches
  for (let i = 0; i < 3; i++) {
    const px = rnd() * S, py = rnd() * S;
    for (let by = 0; by < 3; by++) for (let bx = 0; bx < 3; bx++) {
      if (rnd() < 0.4) continue;
      ctx.fillStyle = 'rgba(150,100,70,0.55)';
      ctx.fillRect(px + bx * 18 + (by % 2) * 9, py + by * 9, 16, 7);
    }
  }
  grain(ctx, S, S, rnd, 20);
  return toTex(c);
}

export function makeBrick() {
  const S = 256, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(77);
  ctx.fillStyle = '#8a7358'; ctx.fillRect(0, 0, S, S);
  const bh = 16, bw = 42;
  for (let y = 0; y < S / bh; y++) {
    for (let x = -1; x < S / bw + 1; x++) {
      const v = 0.8 + rnd() * 0.35;
      ctx.fillStyle = `rgb(${Math.floor(196 * v)},${Math.floor(162 * v)},${Math.floor(118 * v)})`;
      ctx.fillRect(x * bw + (y % 2) * (bw / 2) + 1.5, y * bh + 1.5, bw - 3, bh - 3);
    }
  }
  blotches(ctx, S, S, rnd, 20, ['rgba(80,60,40,A)'], 20, 50, 0.25);
  grain(ctx, S, S, rnd, 18);
  return toTex(c);
}

export function makeWood() {
  const S = 128, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(31);
  ctx.fillStyle = '#9c7444'; ctx.fillRect(0, 0, S, S);
  const planks = 4, ph = S / planks;
  for (let i = 0; i < planks; i++) {
    const v = 0.85 + rnd() * 0.25;
    ctx.fillStyle = `rgb(${Math.floor(165 * v)},${Math.floor(122 * v)},${Math.floor(72 * v)})`;
    ctx.fillRect(0, i * ph + 1, S, ph - 2);
    ctx.strokeStyle = 'rgba(80,50,25,0.3)';
    for (let k = 0; k < 6; k++) {
      ctx.beginPath(); const y = i * ph + rnd() * ph;
      ctx.moveTo(0, y); ctx.bezierCurveTo(S * 0.3, y + rnd() * 4 - 2, S * 0.6, y + rnd() * 4 - 2, S, y); ctx.stroke();
    }
  }
  // frame + diagonal brace
  ctx.fillStyle = '#6b4a28';
  ctx.fillRect(0, 0, S, 9); ctx.fillRect(0, S - 9, S, 9); ctx.fillRect(0, 0, 9, S); ctx.fillRect(S - 9, 0, 9, S);
  ctx.save(); ctx.translate(S / 2, S / 2); ctx.rotate(Math.PI / 4); ctx.fillRect(-S * 0.7, -5, S * 1.4, 10); ctx.restore();
  ctx.fillStyle = 'rgba(30,20,10,0.6)';
  for (const [x, y] of [[4, 4], [S - 5, 4], [4, S - 5], [S - 5, S - 5]]) { ctx.beginPath(); ctx.arc(x, y, 2, 0, 7); ctx.fill(); }
  grain(ctx, S, S, rnd, 16);
  return toTex(c);
}

export function makeStone() {
  const S = 256, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(41);
  ctx.fillStyle = '#a99273'; ctx.fillRect(0, 0, S, S);
  const rows = 4, rh = S / rows;
  for (let r = 0; r < rows; r++) {
    let x = -rnd() * 40;
    while (x < S) {
      const w = 40 + rnd() * 50, v = 0.8 + rnd() * 0.3;
      ctx.fillStyle = `rgb(${Math.floor(180 * v)},${Math.floor(158 * v)},${Math.floor(126 * v)})`;
      ctx.fillRect(x + 2, r * rh + 2, w - 4, rh - 4);
      x += w;
    }
  }
  blotches(ctx, S, S, rnd, 30, ['rgba(90,70,50,A)', 'rgba(230,210,180,A)'], 10, 40, 0.25);
  grain(ctx, S, S, rnd, 20);
  return toTex(c);
}

export function makeConcrete() {
  const S = 128, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(51);
  ctx.fillStyle = '#9d9a92'; ctx.fillRect(0, 0, S, S);
  blotches(ctx, S, S, rnd, 25, ['rgba(70,70,65,A)', 'rgba(200,198,190,A)'], 8, 30, 0.3);
  ctx.fillStyle = 'rgba(60,60,55,0.5)'; ctx.fillRect(0, 0, S, 4);
  grain(ctx, S, S, rnd, 26);
  return toTex(c);
}

export function makeMetal(color = '#3f6f8f') {
  const S = 128, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(61);
  ctx.fillStyle = color; ctx.fillRect(0, 0, S, S);
  for (let x = 0; x < S; x += 16) {
    const g = ctx.createLinearGradient(x, 0, x + 16, 0);
    g.addColorStop(0, 'rgba(0,0,0,0.25)'); g.addColorStop(0.5, 'rgba(255,255,255,0.12)'); g.addColorStop(1, 'rgba(0,0,0,0.25)');
    ctx.fillStyle = g; ctx.fillRect(x, 0, 16, S);
  }
  blotches(ctx, S, S, rnd, 18, ['rgba(120,70,40,A)'], 5, 20, 0.35);
  grain(ctx, S, S, rnd, 14);
  return toTex(c);
}

export function makeBeam() {
  const S = 128, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(71);
  ctx.fillStyle = '#5a4130'; ctx.fillRect(0, 0, S, S);
  for (let i = 0; i < 12; i++) {
    ctx.fillStyle = `rgba(${rnd() > 0.5 ? '30,20,10' : '120,90,60'},0.25)`;
    ctx.fillRect(0, rnd() * S, S, 1 + rnd() * 3);
  }
  grain(ctx, S, S, rnd, 18);
  return toTex(c);
}

export function makeSoftCircle() {
  const S = 64, c = canvas(S, S), ctx = c.getContext('2d');
  const g = ctx.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.45, 'rgba(255,255,255,0.6)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = g; ctx.fillRect(0, 0, S, S);
  const t = new THREE.CanvasTexture(c);
  return t;
}

export function makeSmokePuff() {
  const S = 128, c = canvas(S, S), ctx = c.getContext('2d'), rnd = seeded(91);
  for (let i = 0; i < 26; i++) {
    const a = rnd() * Math.PI * 2, d = rnd() * S * 0.22;
    const x = S / 2 + Math.cos(a) * d, y = S / 2 + Math.sin(a) * d, r = S * (0.14 + rnd() * 0.18);
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0, 'rgba(255,255,255,0.32)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g; ctx.fillRect(0, 0, S, S);
  }
  return new THREE.CanvasTexture(c);
}

export function makeBulletHole() {
  const S = 32, c = canvas(S, S), ctx = c.getContext('2d');
  const g = ctx.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
  g.addColorStop(0, 'rgba(10,8,6,1)');
  g.addColorStop(0.25, 'rgba(25,20,15,0.95)');
  g.addColorStop(0.5, 'rgba(60,50,40,0.5)');
  g.addColorStop(1, 'rgba(60,50,40,0)');
  ctx.fillStyle = g; ctx.fillRect(0, 0, S, S);
  return new THREE.CanvasTexture(c);
}

export function makeTextTexture(text, opts = {}) {
  const w = opts.w || 256, h = opts.h || 256;
  const c = canvas(w, h), ctx = c.getContext('2d');
  ctx.clearRect(0, 0, w, h);
  ctx.font = `bold ${opts.size || 200}px "Barlow Condensed", Impact, sans-serif`;
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.globalAlpha = opts.alpha ?? 0.85;
  ctx.fillStyle = opts.color || '#7a1e14';
  ctx.fillText(text, w / 2, h / 2 + 8);
  // spray paint drips / roughness
  ctx.globalCompositeOperation = 'destination-out';
  const rnd = seeded(text.charCodeAt(0));
  for (let i = 0; i < 400; i++) { ctx.fillStyle = `rgba(0,0,0,${rnd() * 0.5})`; ctx.fillRect(rnd() * w, rnd() * h, 2, 2); }
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

export function makeArrowTexture(label, color = '#f2efe6') {
  const w = 256, h = 128, c = canvas(w, h), ctx = c.getContext('2d');
  ctx.fillStyle = 'rgba(30,30,30,0.0)'; ctx.fillRect(0, 0, w, h);
  ctx.fillStyle = color; ctx.globalAlpha = 0.85;
  ctx.font = 'bold 64px "Barlow Condensed", Impact, sans-serif';
  ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
  ctx.fillText(label, 16, h / 2 + 4);
  const tw = ctx.measureText(label).width + 30;
  ctx.beginPath(); ctx.moveTo(tw, h / 2 - 22); ctx.lineTo(tw + 60, h / 2); ctx.lineTo(tw, h / 2 + 22); ctx.closePath(); ctx.fill();
  ctx.fillRect(tw - 20, h / 2 - 8, 22, 16);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
