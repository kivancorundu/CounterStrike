// Procedural low-poly models: weapons, characters, C4. All dimensions in game units.
import * as THREE from './vendor/three.module.min.js';

const matCache = new Map();
export function mat(color, opts = {}) {
  const key = color + JSON.stringify(opts);
  if (!matCache.has(key)) matCache.set(key, new THREE.MeshLambertMaterial({ color, ...opts }));
  return matCache.get(key);
}
const boxGeoCache = new Map();
function boxGeo(w, h, d) {
  const k = `${w}|${h}|${d}`;
  if (!boxGeoCache.has(k)) boxGeoCache.set(k, new THREE.BoxGeometry(w, h, d));
  return boxGeoCache.get(k);
}
function B(parent, w, h, d, color, x, y, z, rx = 0, ry = 0, rz = 0) {
  const m = new THREE.Mesh(boxGeo(w, h, d), typeof color === 'object' ? color : mat(color));
  m.position.set(x, y, z); m.rotation.set(rx, ry, rz);
  parent.add(m);
  return m;
}
function C(parent, r1, r2, len, color, x, y, z, axis = 'z', seg = 8) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r1, r2, len, seg), typeof color === 'object' ? color : mat(color));
  m.position.set(x, y, z);
  if (axis === 'z') m.rotation.x = Math.PI / 2;
  if (axis === 'x') m.rotation.z = Math.PI / 2;
  parent.add(m);
  return m;
}

const DARK = 0x1e1f21, METAL = 0x3a3d40, GRIP = 0x232220;

// returns { group, muzzle(Vector3, local), mag(Mesh|null), leftHand(Vector3) }
export function buildGun(def, opts = {}) {
  const g = new THREE.Group();
  const col = def.color ?? METAL;
  let muzzle = new THREE.Vector3(0, 2, -24), mag = null, left = new THREE.Vector3(0, 1, -12);
  const kind = def.model;
  const silenced = opts.silenced;
  switch (kind) {
    case 'ak': {
      const wood = def.wood ?? 0x7a4a25;
      B(g, 2.2, 3.0, 13, col, 0, 1.6, -4);
      B(g, 2.0, 0.9, 13, 0x111111, 0, 3.3, -4);
      B(g, 2.5, 2.6, 7, wood, 0, 1.7, -13);
      C(g, 0.45, 0.45, 16, DARK, 0, 2.3, -22);
      C(g, 0.6, 0.6, 9, DARK, 0, 3.3, -16);
      B(g, 0.5, 1.6, 0.5, DARK, 0, 3.6, -26);
      B(g, 2.0, 3.6, 10, wood, 0, 1.0, 7.5, 0.12);
      B(g, 1.6, 4.2, 2.2, wood, 0, -1.6, 0.5, 0.35);
      mag = B(g, 1.7, 7, 2.8, 0x2a2016, 0, -2.6, -6.5, 0.35);
      muzzle.set(0, 2.3, -30); left.set(0, 0.5, -13);
      break;
    }
    case 'm4': {
      B(g, 2.2, 3.2, 13, col, 0, 1.6, -4);
      B(g, 0.9, 1.4, 9, DARK, 0, 3.8, -3);
      B(g, 2.8, 2.8, 9, DARK, 0, 1.8, -14);
      if (silenced) { C(g, 0.9, 0.9, 13, 0x151515, 0, 2.0, -24); muzzle.set(0, 2.0, -31); }
      else { C(g, 0.45, 0.45, 10, DARK, 0, 2.0, -22); C(g, 0.7, 0.7, 2, DARK, 0, 2.0, -27); muzzle.set(0, 2.0, -28.5); }
      B(g, 0.6, 2.2, 0.6, DARK, 0, 3.4, -18);
      B(g, 2.0, 3.2, 3, DARK, 0, 1.4, 3.5);
      B(g, 1.4, 1.4, 6, DARK, 0, 1.7, 7);
      B(g, 2.2, 3.8, 4, col, 0, 1.0, 10.5);
      B(g, 1.6, 4.2, 2.2, GRIP, 0, -1.6, 0.5, 0.35);
      mag = B(g, 1.6, 6.5, 2.6, DARK, 0, -2.4, -6.5, 0.12);
      left.set(0, 0.4, -14);
      break;
    }
    case 'bullpup': {
      B(g, 2.6, 4.2, 18, col, 0, 1.6, 2);
      B(g, 1.0, 1.6, 12, DARK, 0, 4.4, -2);
      C(g, 0.45, 0.45, 9, DARK, 0, 2.2, -11);
      B(g, 1.6, 4, 2.2, GRIP, 0, -1.6, -4, 0.3);
      mag = B(g, 1.6, 6, 2.6, DARK, 0, -2.2, 4, 0.1);
      muzzle.set(0, 2.2, -16); left.set(0, 0.2, -8);
      break;
    }
    case 'smg': {
      B(g, 2.2, 3.4, 11, col, 0, 1.4, -2);
      C(g, 0.5, 0.5, 6, DARK, 0, 2.0, -10);
      if (silenced) { C(g, 1.0, 1.0, 9, 0x141414, 0, 2.0, -13); muzzle.set(0, 2.0, -18); } else muzzle.set(0, 2.0, -13.5);
      B(g, 1.6, 4, 2.2, GRIP, 0, -1.6, 1, 0.3);
      mag = B(g, 1.5, 7, 2.2, DARK, 0, -2.8, -4, 0.05);
      B(g, 1.2, 2.2, 7, DARK, 0, 1.2, 6.5);
      left.set(0, -0.5, -6);
      break;
    }
    case 'p90': {
      B(g, 3, 4.8, 18, col, 0, 1.6, 0);
      mag = B(g, 2.2, 1.2, 12, 0x2a2a2a, 0, 4.5, -1);
      C(g, 0.5, 0.5, 4, DARK, 0, 2, -11);
      muzzle.set(0, 2, -13); left.set(0, 0, -6);
      break;
    }
    case 'shotgun': {
      B(g, 2.4, 3.4, 11, col, 0, 1.6, -2);
      C(g, 0.65, 0.65, 18, DARK, 0, 2.6, -15);
      C(g, 0.7, 0.7, 13, 0x2a2a2a, 0, 1.1, -14);
      const pump = B(g, 2.2, 2.2, 6, 0x4a3324, 0, 1.1, -14);
      B(g, 2, 3.6, 10, 0x4a3324, 0, 0.8, 8, 0.15);
      B(g, 1.6, 4, 2.2, GRIP, 0, -1.6, 2, 0.35);
      mag = pump;
      muzzle.set(0, 2.6, -24.5); left.set(0, 0.2, -14);
      break;
    }
    case 'mg': {
      B(g, 3, 4, 16, col, 0, 1.6, -2);
      C(g, 0.6, 0.6, 18, DARK, 0, 2.2, -19);
      B(g, 2.8, 2.8, 8, DARK, 0, 1.8, -13);
      mag = B(g, 4, 5, 6, 0x3a3a2a, -1.5, -2.4, -3);
      B(g, 2, 3.6, 9, DARK, 0, 1.2, 10);
      B(g, 1.6, 4.2, 2.2, GRIP, 0, -1.6, 2, 0.35);
      muzzle.set(0, 2.2, -28); left.set(0, 0.2, -13);
      break;
    }
    case 'sniper':
    case 'awp': {
      const big = kind === 'awp';
      B(g, 2.4, 3.4, 14, col, 0, 1.6, -3);
      C(g, big ? 0.65 : 0.5, big ? 0.65 : 0.5, big ? 26 : 22, DARK, 0, 2.2, big ? -23 : -21);
      C(g, 1.3, 1.3, 13, 0x151515, 0, 5.4, -3);
      C(g, 1.7, 1.5, 2.5, 0x151515, 0, 5.4, -10);
      C(g, 1.5, 1.4, 2, 0x151515, 0, 5.4, 4);
      B(g, 2.2, 4.4, 12, col, 0, 0.6, 9, 0.1);
      B(g, 1.6, 4, 2.2, GRIP, 0, -1.6, 1, 0.35);
      B(g, 0.5, 0.5, 2.5, 0x666666, 1.6, 2.4, 1);
      mag = B(g, 1.6, 3.6, 3, DARK, 0, -1.4, -4);
      muzzle.set(0, 2.2, big ? -36 : -32); left.set(0, 0.3, -13);
      break;
    }
    case 'deagle': {
      B(g, 1.6, 2.2, 9, col, 0, 2.2, -2.5);
      B(g, 1.6, 1.4, 6, col, 0, 0.8, -2);
      B(g, 1.5, 4.2, 2.4, GRIP, 0, -1.5, 0.8, 0.25);
      mag = B(g, 1.2, 3.5, 1.8, DARK, 0, -1.4, 0.8, 0.25);
      muzzle.set(0, 2.2, -7.5); left.set(0, -1.5, 1);
      break;
    }
    case 'dualies':
    case 'pistol': {
      B(g, 1.4, 1.8, 7.5, col, 0, 2.0, -1.8);
      B(g, 1.3, 1.1, 5.5, DARK, 0, 0.8, -1.8);
      if (silenced) { C(g, 0.7, 0.7, 6, 0x111111, 0, 2.0, -8.5); muzzle.set(0, 2, -11.6); } else muzzle.set(0, 2.0, -5.8);
      B(g, 1.3, 3.8, 2.2, GRIP, 0, -1.3, 0.8, 0.22);
      mag = B(g, 1.0, 3.2, 1.6, DARK, 0, -1.2, 0.8, 0.22);
      left.set(0, -1.5, 1);
      break;
    }
    case 'knife': {
      const blade = new THREE.Mesh(new THREE.BoxGeometry(0.35, 1.5, 9), mat(0xb8bcc2));
      blade.position.set(0, 0.5, -6.5);
      blade.geometry = blade.geometry.clone();
      const p = blade.geometry.attributes.position;
      for (let i = 0; i < p.count; i++) if (p.getZ(i) < 0 && p.getY(i) > 0) p.setY(i, p.getY(i) - 1.2);
      p.needsUpdate = true; blade.geometry.computeVertexNormals();
      g.add(blade);
      B(g, 2.2, 0.6, 0.8, 0x222222, 0, 0.3, -1.8);
      B(g, 1.1, 1.4, 5, 0x2a2a2a, 0, 0.1, 1);
      muzzle.set(0, 0.5, -11);
      break;
    }
    case 'taser': {
      B(g, 1.4, 2.2, 6, 0x222222, 0, 1.8, -1);
      B(g, 1.5, 1.2, 1.5, 0xd4c21a, 0, 2.0, -4.6);
      B(g, 1.3, 3.6, 2, GRIP, 0, -0.8, 1, 0.25);
      muzzle.set(0, 2, -5.5);
      break;
    }
    case 'c4': {
      B(g, 7, 3.2, 5, 0x5b5a3a, 0, 1.6, 0);
      B(g, 3.2, 0.4, 2.6, 0x1a1a1a, 0, 3.3, 0.4);
      for (let i = 0; i < 3; i++) for (let k = 0; k < 3; k++) B(g, 0.7, 0.3, 0.5, 0xc8c8c8, -0.9 + i * 0.9, 3.5, -0.4 + k * 0.8);
      B(g, 2, 0.3, 0.8, 0x2d6b3a, 0, 3.5, -1.2);
      C(g, 0.25, 0.25, 7.2, 0xa02020, 0, 2.6, 2.6, 'x');
      C(g, 0.25, 0.25, 7.2, 0x2040a0, 0, 2.0, 2.6, 'x');
      const led = new THREE.Mesh(new THREE.SphereGeometry(0.4, 6, 6), new THREE.MeshBasicMaterial({ color: 0xff2010 }));
      led.position.set(2.6, 3.4, -1.6); g.add(led);
      g.userData.led = led;
      muzzle.set(0, 3, -3);
      break;
    }
    case 'grenade': {
      const gc = def.color ?? 0x556644;
      if (def.id === 'hegrenade') { const s = new THREE.Mesh(new THREE.SphereGeometry(1.9, 10, 8), mat(gc)); s.position.y = 1.8; g.add(s); B(g, 0.6, 2.4, 0.9, 0x888888, 1.4, 2.6, 0, 0, 0, -0.3); C(g, 0.6, 0.6, 1, 0x777777, 0, 3.8, 0, 'y'); }
      else if (def.id === 'molotov') { C(g, 1.6, 1.6, 5, mat(0x6b4a1f, { transparent: true, opacity: 0.85 }), 0, 2.5, 0, 'y'); C(g, 0.6, 1.2, 2.5, mat(0x6b4a1f, { transparent: true, opacity: 0.85 }), 0, 6, 0, 'y'); B(g, 1.2, 2.5, 1.2, 0xc9bfa5, 0, 8.2, 0); }
      else { C(g, 1.35, 1.35, 4.6, gc, 0, 2.3, 0, 'y'); C(g, 1.4, 1.4, 0.6, 0x222222, 0, 3.9, 0, 'y'); B(g, 0.6, 2.6, 0.8, 0x888888, 1.2, 3.4, 0, 0, 0, -0.25); if (def.id === 'smokegrenade') C(g, 1.42, 1.42, 0.8, 0xc9c9c9, 0, 1.6, 0, 'y'); if (def.id === 'flashbang') C(g, 1.42, 1.42, 0.5, 0x3b6db0, 0, 1.2, 0, 'y'); }
      muzzle.set(0, 2, -2);
      break;
    }
    default: {
      B(g, 2.2, 3.2, 14, col, 0, 1.6, -4);
      C(g, 0.45, 0.45, 12, DARK, 0, 2.2, -16);
      B(g, 1.0, 1.2, 8, DARK, 0, 3.6, -3);
      B(g, 2, 3.6, 9, col, 0, 1.0, 7.5, 0.1);
      B(g, 1.6, 4.2, 2.2, GRIP, 0, -1.6, 0.5, 0.35);
      mag = B(g, 1.6, 6.5, 2.6, DARK, 0, -2.4, -6.5, 0.2);
      muzzle.set(0, 2.2, -22.5); left.set(0, 0.4, -13);
    }
  }
  return { group: g, muzzle, mag, left };
}

// ---------------- characters ----------------
const TEAM_STYLE = {
  CT: { pants: 0x2d3846, shirt: 0x3a4a5e, vest: 0x26313d, skin: 0xc9a183, head: 0x2a3340, helmet: 0x34404c, glove: 0x1d1f22, boot: 0x1b1b1b, accent: 0x5b8fd6 },
  T: { pants: 0x6b5a3e, shirt: 0x5c5236, vest: 0x4a4130, skin: 0xb88a68, head: 0x1a1a1a, helmet: 0x8b2c1f, glove: 0x2a2520, boot: 0x2d241a, accent: 0xe0a53c },
  DM: { pants: 0x4a4a48, shirt: 0x5a5a56, vest: 0x3a3a38, skin: 0xc29a7a, head: 0x2a2a2a, helmet: 0x555555, glove: 0x222222, boot: 0x1b1b1b, accent: 0xcccccc },
};

export function buildCharacter(team, variant = 0) {
  const s = TEAM_STYLE[team] || TEAM_STYLE.DM;
  const root = new THREE.Group();
  const hips = new THREE.Group(); hips.position.y = 36; root.add(hips);
  B(hips, 15, 7, 9, s.pants, 0, 0, 0);
  const legs = [];
  for (const side of [-1, 1]) {
    const thigh = new THREE.Group(); thigh.position.set(side * 4.2, -1, 0); hips.add(thigh);
    B(thigh, 6.6, 18, 7, s.pants, 0, -9, 0);
    const knee = new THREE.Group(); knee.position.y = -17.5; thigh.add(knee);
    B(knee, 6, 17, 6.4, s.pants, 0, -8.5, 0);
    B(knee, 6.8, 4, 10, s.boot, 0, -16.5, -1.5);
    if (team === 'CT') B(knee, 6.8, 4, 3, 0x222831, 0, -2, -3);
    legs.push({ thigh, knee });
  }
  const torso = new THREE.Group(); torso.position.y = 3; hips.add(torso);
  B(torso, 16, 22, 9.5, s.shirt, 0, 11, 0);
  B(torso, 17.5, 15, 11, s.vest, 0, 13, 0);
  if (team === 'CT') { B(torso, 5, 4, 2, 0x1d242c, -4, 9, -5.8); B(torso, 5, 4, 2, 0x1d242c, 4, 9, -5.8); B(torso, 3, 2, 0.5, s.accent, 5, 18, -5.6); }
  else { B(torso, 3.5, 5, 2.5, 0x3b3424, -5, 8, -5.8); B(torso, 3.5, 5, 2.5, 0x3b3424, 0, 8, -5.8); B(torso, 18, 2, 11.5, 0x2b2418, 0, 3, 0); }
  const neck = new THREE.Group(); neck.position.y = 23; torso.add(neck);
  const head = new THREE.Group(); neck.add(head);
  if (team === 'CT') {
    B(head, 8.4, 9.6, 9.2, s.skin, 0, 5, 0);
    B(head, 10, 4.5, 11, s.helmet, 0, 9.2, 0.3);
    B(head, 10.4, 1.2, 11.6, s.helmet, 0, 7.4, 0.3);
    B(head, 8, 2, 1.4, 0x111518, 0, 5.4, -4.8);
    B(head, 8.6, 3.4, 9.4, 0x222a33, 0, 1.6, 0);
  } else {
    B(head, 8.6, 9.8, 9.4, s.head, 0, 5, 0);
    B(head, 8.8, 2.2, 1, s.skin, 0, 5.6, -4.5);
    B(head, 1.4, 1, 0.5, 0x111111, -2, 5.6, -5.05); B(head, 1.4, 1, 0.5, 0x111111, 2, 5.6, -5.05);
    if (variant % 2 === 0) B(head, 9.4, 2.2, 10, s.helmet, 0, 8.6, 0);
  }
  // arms reaching forward to hold the weapon
  const armR = new THREE.Group(); armR.position.set(8.5, 19, 0); torso.add(armR);
  B(armR, 4.4, 4.4, 11, s.shirt, 0, -2, -4.5, -0.5);
  B(armR, 4, 4, 9, s.shirt, -1, -5.5, -11, -0.1);
  B(armR, 3.6, 3.6, 3, s.glove, -1.5, -6, -16);
  const armL = new THREE.Group(); armL.position.set(-8.5, 19, 0); torso.add(armL);
  B(armL, 4.4, 4.4, 11, s.shirt, 0, -2, -4.5, -0.5, 0.0);
  B(armL, 4, 4, 11, s.shirt, 3, -5, -13, -0.05, -0.35);
  B(armL, 3.6, 3.6, 3, s.glove, 5, -4.8, -19);
  const gunMount = new THREE.Group(); gunMount.position.set(4, 13, -15); torso.add(gunMount);
  root.userData = { hips, torso, head, neck, legs, armR, armL, gunMount, team };
  root.traverse(o => { if (o.isMesh) { o.castShadow = true; } });
  return root;
}

export function makeNameTag(text, color) {
  const c = document.createElement('canvas'); c.width = 256; c.height = 48;
  const ctx = c.getContext('2d');
  ctx.font = 'bold 30px "Barlow Condensed", sans-serif';
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.lineWidth = 5; ctx.strokeStyle = 'rgba(0,0,0,0.8)'; ctx.strokeText(text, 128, 24);
  ctx.fillStyle = color; ctx.fillText(text, 128, 24);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true }));
  sp.scale.set(48, 9, 1);
  sp.renderOrder = 10;
  return sp;
}

// ---------------- viewmodel hands ----------------
export function buildHands(team) {
  const s = TEAM_STYLE[team] || TEAM_STYLE.DM;
  const right = new THREE.Group(), left = new THREE.Group();
  B(right, 3.0, 3.2, 3.6, s.glove, 0, 0, 0);
  B(right, 3.4, 3.4, 9, s.shirt, 0.6, -2.6, 5.6, 0.55, -0.1);
  B(left, 3.0, 3.2, 3.6, s.glove, 0, 0, 0);
  B(left, 3.2, 3.2, 7, s.shirt, -1.0, -3.2, 4.2, 0.75, 0.25);
  return { right, left };
}
