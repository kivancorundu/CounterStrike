// Throwables: HE, flashbang, volumetric smoke (floods into the level and can be
// cleared by HE / poked by bullets), molotov/incendiary fire, decoy.
import * as THREE from './vendor/three.module.min.js';
import { world, GRAVITY, DEG, clamp, isEnemy } from './state.js';
import { GRENADES, WEAPONS } from './weapons.js';
import { buildGun } from './models.js';
import { fx } from './effects.js';
import { audio } from './audio.js';
import { damage } from './combat.js';
import { CELL } from './state.js';

const HE_DEF = { id: 'hegrenade', name: 'HE', cat: 'grenade', ap: 0.5, reward: 300 };
const FIRE_DEF = { id: 'molotov', name: 'Molotof', cat: 'grenade', reward: 300 };
const SMOKE_TIME = 18;
const FIRE_TIME = 7;

export function throwGrenade(ch, gid, strength = 1, overrideVel) {
  const eye = ch.eyePos(new THREE.Vector3());
  let vel;
  if (overrideVel) vel = overrideVel.clone();
  else {
    // CS-style throw angle adjustment (aims a little higher than the crosshair)
    let srcPitch = -ch.pitch / DEG;
    if (srcPitch < 0) srcPitch = -10 + srcPitch * (80 / 90);
    else srcPitch = -10 + srcPitch * (100 / 90);
    const p = -srcPitch * DEG;
    const dir = new THREE.Vector3(-Math.sin(ch.yaw) * Math.cos(p), Math.sin(p), -Math.cos(ch.yaw) * Math.cos(p));
    const speed = 675 * (strength * 0.7 + 0.3);
    vel = dir.multiplyScalar(speed).add(ch.vel.clone().multiplyScalar(1.25));
  }
  const start = eye.clone();
  // step a bit forward if not blocked
  const fwd = vel.clone().normalize();
  const mh = world.map.raycast(start.x, start.y, start.z, fwd.x, fwd.y, fwd.z, 18);
  if (!mh) start.addScaledVector(fwd, 16);
  const def = GRENADES[gid];
  const mesh = buildGun(def).group;
  mesh.position.copy(start);
  world.scene.add(mesh);
  const p = { gid, def, pos: start, vel, mesh, owner: ch, born: world.time, rest: false, restTime: 0, done: false, trail: world.mode === 'practice' ? [start.clone()] : null, lastTrail: world.time };
  world.projectiles.push(p);
  return p;
}

export function updateProjectiles(dt) {
  const map = world.map;
  for (const p of world.projectiles) {
    if (p.done) continue;
    const age = world.time - p.born;
    if (!p.rest) {
      p.vel.y -= GRAVITY * 0.4 * dt;
      let remaining = dt;
      for (let it = 0; it < 3 && remaining > 0; it++) {
        const sp = p.vel.length();
        if (sp < 0.01) break;
        const dist = sp * remaining;
        const d = p.vel.clone().divideScalar(sp);
        const h = map.raycast(p.pos.x, p.pos.y, p.pos.z, d.x, d.y, d.z, dist + 2);
        if (!h || h.t > dist) { p.pos.addScaledVector(d, dist); break; }
        p.pos.set(h.x + h.nx * 1.5, h.y + h.ny * 1.5, h.z + h.nz * 1.5);
        remaining -= h.t / sp;
        // molotov breaks on the ground
        if ((p.gid === 'molotov' || p.gid === 'incgrenade') && h.ny > 0.7) { detonate(p, h); break; }
        const vn = p.vel.x * h.nx + p.vel.y * h.ny + p.vel.z * h.nz;
        p.vel.x -= 2 * vn * h.nx; p.vel.y -= 2 * vn * h.ny; p.vel.z -= 2 * vn * h.nz;
        p.vel.multiplyScalar(0.45);
        if (Math.abs(vn) > 40) audio.play('bounce', { pos: p.pos, volume: 0.6, maxDist: 1200 });
        if (h.ny > 0.7 && p.vel.length() < 25) { p.rest = true; p.restTime = world.time; p.vel.set(0, 0, 0); p.pos.y = h.y + 2; break; }
      }
      if (!p.done) {
        p.mesh.position.copy(p.pos);
        p.mesh.rotation.x += dt * 9; p.mesh.rotation.z += dt * 5;
        if (p.trail && world.time - p.lastTrail > 0.02) { p.trail.push(p.pos.clone()); p.lastTrail = world.time; }
      }
    }
    if (p.done) continue;
    switch (p.gid) {
      case 'hegrenade': case 'flashbang':
        if (age >= p.def.fuse) detonate(p);
        break;
      case 'smokegrenade':
        if ((p.rest && world.time - p.restTime > 0.3) || age > 10) detonate(p);
        break;
      case 'molotov': case 'incgrenade':
        if (age >= p.def.fuse) detonate(p, null);
        break;
      case 'decoy':
        if (p.rest && !p.decoyStart) { p.decoyStart = world.time; p.nextDecoy = world.time + 0.3; }
        if (p.decoyStart) {
          if (world.time >= p.nextDecoy) {
            const w = p.owner.inv.primary?.def || p.owner.inv.secondary?.def || WEAPONS.glock;
            const burst = 1 + Math.floor(Math.random() * 3);
            for (let k = 0; k < burst; k++) setTimeout(() => audio.play(w.sound || 'rifle', { pos: p.pos, maxDist: 5000, ref: 300 }), k * 110);
            world.game?.noise(p.owner, 2600, 'shot', p.pos);
            p.nextDecoy = world.time + 0.4 + Math.random() * 1.2;
          }
          if (world.time - p.decoyStart > 15) { fx.explosion(p.pos, 0.25); audio.play('flashbang', { pos: p.pos, volume: 0.4 }); finish(p); }
        }
        break;
    }
  }
  // remove finished
  for (let i = world.projectiles.length - 1; i >= 0; i--) {
    const p = world.projectiles[i];
    if (p.done && (!p.trail || world.time - p.doneAt > 10)) {
      if (p.trailLine) world.scene.remove(p.trailLine);
      world.projectiles.splice(i, 1);
    }
  }
  updateSmokes(dt);
  updateFires(dt);
}

function finish(p) {
  p.done = true; p.doneAt = world.time;
  world.scene.remove(p.mesh);
  if (p.trail && p.trail.length > 1) {
    const g = new THREE.BufferGeometry().setFromPoints(p.trail);
    p.trailLine = new THREE.Line(g, new THREE.LineBasicMaterial({ color: 0xffd040 }));
    world.scene.add(p.trailLine);
  }
}

function detonate(p, hit) {
  const pos = p.pos.clone();
  switch (p.gid) {
    case 'hegrenade': explodeHE(pos, p.owner); break;
    case 'flashbang': explodeFlash(pos, p.owner); break;
    case 'smokegrenade': createSmoke(pos, p.owner); break;
    case 'molotov': case 'incgrenade':
      if (hit) createFire(pos, p.owner, p.gid);
      else { fx.explosion(pos, 0.3); audio.play('glass', { pos, maxDist: 1500 }); }
      break;
  }
  finish(p);
}

// ---------------- HE ----------------
function explodeHE(pos, owner) {
  fx.explosion(pos, 0.8);
  audio.play('explosion', { pos, maxDist: 6000, ref: 400, volume: 1 });
  world.game?.noise(owner, 3000, 'explosion', pos);
  const R = 350;
  const c = new THREE.Vector3(pos.x, pos.y + 8, pos.z);
  for (const ch of world.chars) {
    if (!ch.alive) continue;
    const target = new THREE.Vector3(ch.pos.x, ch.pos.y + ch.height * 0.5, ch.pos.z);
    const d = target.distanceTo(c);
    if (d > R) continue;
    if (!world.map.los(c, target) && !world.map.los(c, new THREE.Vector3(ch.pos.x, ch.pos.y + ch.eyeHeight(), ch.pos.z))) continue;
    const dmg = 98 * (1 - d / R);
    damage(ch, owner, dmg, 'chest', HE_DEF, { grenade: true });
    if (ch.isLocal) fx.shake(1.5 * (1 - d / R) + 0.3);
  }
  // CS2: HE pushes smoke away for a moment
  for (const s of world.smokes) s.blast(pos, 210, 2.6);
  // HE also extinguishes nothing; but damages
  if (world.local && world.local.pos.distanceTo(pos) < 700) fx.shake(0.8);
}

// ---------------- flashbang ----------------
function explodeFlash(pos, owner) {
  fx.flashbang(pos);
  audio.play('flashbang', { pos, maxDist: 5000, ref: 400 });
  const now = world.time;
  for (const ch of world.chars) {
    if (!ch.alive) continue;
    const eye = ch.eyePos(new THREE.Vector3());
    const to = pos.clone().sub(eye);
    const d = to.length();
    if (d > 2400) continue;
    if (!world.map.los(eye, pos)) continue;
    to.divideScalar(d);
    if (segmentInSmoke(eye, to, d, true)) continue;
    const view = ch.viewDir(new THREE.Vector3());
    const dot = view.dot(to);
    let facing;
    if (dot > 0.6) facing = 1; else if (dot > -0.2) facing = 0.35 + 0.65 * (dot + 0.2) / 0.8; else facing = 0.12;
    const distF = d < 400 ? 1 : clamp(1 - (d - 400) / 2000, 0.15, 1);
    const dur = 4.9 * facing * distF;
    if (dur < 0.25) continue;
    const remaining = ch.flashUntil - now;
    if (dur > remaining) {
      ch.flashStart = now;
      ch.flashUntil = now + dur;
      ch.flashFull = facing >= 1 ? Math.min(dur * 0.45, 2.2) : dur * 0.15;
    }
    if (ch.isLocal && dur > 1.2) audio.play('ring', { volume: clamp(dur / 4.9, 0.2, 1) });
    if (ch.isBot) ch.ai?.onFlashed(dur);
    if (owner && isEnemy(owner, ch) && dur > 1.5) owner._flashedEnemies = (owner._flashedEnemies || 0) + 1;
  }
}

// ---------------- smoke ----------------
class Smoke {
  constructor(pos, owner) {
    const map = world.map;
    this.center = pos.clone();
    this.owner = owner;
    this.born = world.time;
    this.cells = new Map(); // idx -> floor height
    this.puffs = [];
    this.clearUntil = new Map(); // idx -> time
    const c0 = map.col(pos.x), r0 = map.row(pos.z);
    const R = 165;
    const start = map.idx(c0, r0);
    const baseY = pos.y;
    const q = [[c0, r0]];
    const seen = new Set([start]);
    while (q.length) {
      const [c, r] = q.shift();
      const i = map.idx(c, r);
      const fl = map.floor[i];
      this.cells.set(i, Math.min(fl, baseY));
      for (const [dc, dr] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nc = c + dc, nr = r + dr;
        if (!map.inside(nc, nr)) continue;
        const j = map.idx(nc, nr);
        if (seen.has(j)) continue;
        const cxp = map.cx(nc), czp = map.cz(nr);
        if (Math.hypot(cxp - pos.x, czp - pos.z) > R + 20) continue;
        const nf = map.floor[j];
        if (nf > baseY + 90 || nf >= 200) continue; // walls block the smoke
        if (map.roof[j] < baseY + 20) continue;
        seen.add(j); q.push([nc, nr]);
      }
    }
    // puffs
    for (const [i, fl] of this.cells) {
      const c = i % map.W, r = (i / map.W) | 0;
      const cx = map.cx(c), cz = map.cz(r);
      const top = Math.min(fl + 150, map.roof[i] - 10);
      const n = 5;
      for (let k = 0; k < n; k++) {
        const tx = cx + (Math.random() - 0.5) * CELL * 0.9, tz = cz + (Math.random() - 0.5) * CELL * 0.9;
        const ty = fl + 20 + Math.random() * Math.max(10, top - fl - 40);
        const pf = fx.smoke.spawn({
          x: pos.x, y: pos.y + 10, z: pos.z, life: SMOKE_TIME + 3, size: 30, size1: 30, r: 0.62, g: 0.63, b: 0.62, a: 0.55, a1: 0.55, persist: true,
          rotV: (Math.random() - 0.5) * 0.15,
        });
        if (!pf) continue;
        const sz = 90 + Math.random() * 50;
        const delay = Math.hypot(tx - pos.x, tz - pos.z) / 300;
        pf.fn = (pp, dt) => this.puffFn(pp, dt, tx, ty, tz, sz, delay, i);
        this.puffs.push(pf);
      }
    }
    audio.play('smoke', { pos, maxDist: 2000 });
  }
  puffFn(p, dt, tx, ty, tz, sz, delay, cell) {
    const age = world.time - this.born;
    const k = clamp((age - delay * 0.5) / 1.4, 0, 1);
    const e = 1 - Math.pow(1 - k, 3);
    p.x = this.center.x + (tx - this.center.x) * e + Math.sin(age * 0.3 + tx) * 3;
    p.y = this.center.y + 10 + (ty - this.center.y - 10) * e;
    p.z = this.center.z + (tz - this.center.z) * e + Math.cos(age * 0.3 + tz) * 3;
    p.size = p.size1 = 30 + (sz - 30) * e;
    let a = 1;
    if (age > SMOKE_TIME) a = clamp(1 - (age - SMOKE_TIME) / 2.5, 0, 1);
    if (p.hole) { p.hole -= dt; a *= clamp(1 - p.hole / 0.4, 0.05, 1); if (p.hole < 0) p.hole = 0; }
    const cu = this.clearUntil.get(cell);
    if (cu && world.time < cu) a *= clamp((world.time - (cu - 2.6)) < 0.3 ? 0.08 : 0.08 + (1 - (cu - world.time) / 2.3) * 0.9, 0.05, 1);
    p.alphaMul = a;
    if (age > SMOKE_TIME + 2.6) p.dead = true;
  }
  get alive() { return world.time - this.born < SMOKE_TIME + 1.0; }
  // point inside dense smoke?
  contains(x, y, z) {
    const map = world.map;
    const i = map.idx(map.col(x), map.row(z));
    if (!this.cells.has(i)) return false;
    const fl = this.cells.get(i);
    if (y < fl - 5 || y > fl + 150) return false;
    const age = world.time - this.born;
    const cc = map.cx(i % map.W), cz = map.cz((i / map.W) | 0);
    const d = Math.hypot(cc - this.center.x, cz - this.center.z);
    if (age < 0.4 + d / 300 * 0.5) return false;
    const cu = this.clearUntil.get(i);
    if (cu && world.time < cu - 0.6) return false;
    return true;
  }
  blast(pos, radius, dur) {
    const map = world.map;
    for (const i of this.cells.keys()) {
      const cx = map.cx(i % map.W), cz = map.cz((i / map.W) | 0);
      if (Math.hypot(cx - pos.x, cz - pos.z) < radius) this.clearUntil.set(i, world.time + dur);
    }
  }
  poke(o, d, len) {
    for (const p of this.puffs) {
      const lx = p.x - o.x, ly = p.y - o.y, lz = p.z - o.z;
      const t = lx * d.x + ly * d.y + lz * d.z;
      if (t < 0 || t > len) continue;
      const px = lx - d.x * t, py = ly - d.y * t, pz = lz - d.z * t;
      if (px * px + py * py + pz * pz < 26 * 26) p.hole = 0.8;
    }
  }
}

function createSmoke(pos, owner) {
  const s = new Smoke(pos, owner);
  world.smokes.push(s);
  // smoke extinguishes fires it covers
  for (const f of world.fires) {
    for (const i of f.cells.keys()) if (s.cells.has(i)) { f.extinguish(); break; }
  }
}

function updateSmokes() {
  for (let i = world.smokes.length - 1; i >= 0; i--) {
    if (world.time - world.smokes[i].born > SMOKE_TIME + 3) world.smokes.splice(i, 1);
  }
}

export function pointInSmoke(x, y, z) {
  for (const s of world.smokes) if (s.alive && s.contains(x, y, z)) return true;
  return false;
}

// does a segment pass through smoke? (strict = enough dense distance to block vision)
export function segmentInSmoke(o, d, len, strict = false) {
  if (!world.smokes.length) return false;
  for (const s of world.smokes) {
    if (!s.alive) continue;
    // quick reject by distance from smoke center to the segment
    const lx = s.center.x - o.x, lz = s.center.z - o.z, ly = s.center.y - o.y;
    let t = clamp(lx * d.x + ly * d.y + lz * d.z, 0, len);
    const px = o.x + d.x * t - s.center.x, pz = o.z + d.z * t - s.center.z;
    if (px * px + pz * pz > 260 * 260) continue;
    let inside = 0;
    const step = 16;
    for (let k = 0; k <= len; k += step) {
      if (s.contains(o.x + d.x * k, o.y + d.y * k, o.z + d.z * k)) { inside += step; if (inside >= (strict ? 56 : 24)) return true; }
    }
  }
  return false;
}

export function pokeSmokes(o, d, len) {
  for (const s of world.smokes) if (s.alive) s.poke(o, d, len);
}

// ---------------- fire ----------------
class Fire {
  constructor(pos, owner, gid) {
    const map = world.map;
    this.owner = owner; this.gid = gid;
    this.born = world.time;
    this.cells = new Map();
    this.nextTick = world.time;
    const c0 = map.col(pos.x), r0 = map.row(pos.z);
    const base = map.floor[map.idx(c0, r0)];
    const q = [[c0, r0]], seen = new Set([map.idx(c0, r0)]);
    while (q.length) {
      const [c, r] = q.shift();
      const i = map.idx(c, r);
      this.cells.set(i, map.floor[i]);
      for (const [dc, dr] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [-1, -1], [1, -1], [-1, 1]]) {
        const nc = c + dc, nr = r + dr;
        if (!map.inside(nc, nr)) continue;
        const j = map.idx(nc, nr);
        if (seen.has(j)) continue;
        if (Math.hypot(map.cx(nc) - pos.x, map.cz(nr) - pos.z) > 140) continue;
        if (!map.walkable(nc, nr) || Math.abs(map.floor[j] - base) > 40) continue;
        seen.add(j); q.push([nc, nr]);
      }
    }
    this.snd = audio.loop('fire', pos, 0.8);
    this.pos = pos.clone();
    audio.play('glass', { pos, maxDist: 1500 });
  }
  get alive() { return !this.out && world.time - this.born < FIRE_TIME; }
  extinguish() {
    if (this.out) return;
    this.out = true;
    audio.stop(this.snd);
    audio.play('smoke', { pos: this.pos, volume: 0.5, maxDist: 1500 });
    for (const i of this.cells.keys()) {
      const m = world.map;
      fx.normal.spawn({ x: m.cx(i % m.W), y: this.cells.get(i) + 20, z: m.cz((i / m.W) | 0), vy: 40, life: 1.5, size: 30, size1: 70, r: 0.5, g: 0.5, b: 0.5, a: 0.5, a1: 0 });
    }
  }
  update(dt) {
    if (!this.alive) { if (!this.out) { this.out = true; audio.stop(this.snd); } return; }
    const m = world.map;
    const age = world.time - this.born;
    const fade = age > FIRE_TIME - 1 ? (FIRE_TIME - age) : 1;
    for (const [i, fl] of this.cells) {
      if (Math.random() > 0.55 * fade) continue;
      const x = m.cx(i % m.W) + (Math.random() - 0.5) * CELL, z = m.cz((i / m.W) | 0) + (Math.random() - 0.5) * CELL;
      fx.additive.spawn({ x, y: fl + 4, z, vx: (Math.random() - 0.5) * 20, vy: 50 + Math.random() * 60, vz: (Math.random() - 0.5) * 20, life: 0.5 + Math.random() * 0.4, size: 14 + Math.random() * 10, size1: 4, r: 1, g: 0.45 + Math.random() * 0.25, b: 0.12, a: 0.9, a1: 0 });
      if (Math.random() < 0.08) fx.normal.spawn({ x, y: fl + 40, z, vy: 60, life: 1.6, size: 20, size1: 60, r: 0.15, g: 0.13, b: 0.12, a: 0.35, a1: 0 });
    }
    if (world.time >= this.nextTick) {
      this.nextTick = world.time + 0.25;
      for (const ch of world.chars) {
        if (!ch.alive) continue;
        const i = m.idx(m.col(ch.pos.x), m.row(ch.pos.z));
        if (!this.cells.has(i)) continue;
        if (ch.pos.y > this.cells.get(i) + 40) continue;
        damage(ch, this.owner, 10, 'legs', { ...FIRE_DEF, id: this.gid }, { fire: true });
      }
    }
  }
}

function createFire(pos, owner, gid) {
  // landing inside a smoke -> fizzles out
  if (pointInSmoke(pos.x, pos.y + 20, pos.z)) {
    audio.play('smoke', { pos, volume: 0.5 });
    fx.normal.spawn({ x: pos.x, y: pos.y + 20, z: pos.z, vy: 40, life: 1.5, size: 30, size1: 80, r: 0.5, g: 0.5, b: 0.5, a: 0.5, a1: 0 });
    return;
  }
  const f = new Fire(pos, owner, gid);
  world.fires.push(f);
  world.game?.noise(owner, 1500, 'fire', pos);
}

function updateFires(dt) {
  for (const f of world.fires) f.update(dt);
  for (let i = world.fires.length - 1; i >= 0; i--) if (!world.fires[i].alive && world.fires[i].out) world.fires.splice(i, 1);
}

export function fireAt(x, z) {
  const m = world.map;
  const i = m.idx(m.col(x), m.row(z));
  for (const f of world.fires) if (f.alive && f.cells.has(i)) return true;
  return false;
}

export function clearGrenades() {
  for (const p of world.projectiles) { world.scene.remove(p.mesh); if (p.trailLine) world.scene.remove(p.trailLine); }
  world.projectiles.length = 0;
  for (const f of world.fires) { f.out = true; audio.stop(f.snd); }
  world.fires.length = 0;
  world.smokes.length = 0;
  fx.smoke.clear();
}

// ballistic helper for bots: velocity needed to land near target with the 0.4g grenade gravity
export function solveThrow(from, to, highArc = false) {
  const g = GRAVITY * 0.4;
  const dx = to.x - from.x, dz = to.z - from.z, dy = to.y - from.y;
  const dist = Math.hypot(dx, dz);
  for (const speed of [450, 560, 675, 760]) {
    const v2 = speed * speed;
    const disc = v2 * v2 - g * (g * dist * dist + 2 * dy * v2);
    if (disc < 0) continue;
    const root = Math.sqrt(disc);
    const ang = Math.atan((v2 + (highArc ? root : -root)) / (g * dist));
    const vx = Math.cos(ang) * speed, vy = Math.sin(ang) * speed;
    return new THREE.Vector3(dx / dist * vx, vy, dz / dist * vx);
  }
  return null;
}
