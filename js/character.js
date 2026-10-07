// Character: shared by the local player and bots. Source-style movement, weapon state machine,
// recoil / inaccuracy model, hitboxes, third-person model animation.
import * as THREE from './vendor/three.module.min.js';
import {
  world, PLAYER_RADIUS, STAND_HEIGHT, CROUCH_HEIGHT, STAND_EYE, CROUCH_EYE, GRAVITY, JUMP_VELOCITY, STEP_HEIGHT,
  FRICTION, STOP_SPEED, ACCELERATE, AIR_ACCELERATE, AIR_WISHSPEED, WALK_MULT, DUCK_MULT, DEG,
  clamp, lerp, dirFromAngles, after,
} from './state.js';
import { WEAPONS, GRENADES, GRENADE_ORDER, MAX_GRENADES, recoilPattern } from './weapons.js';
import { audio, gunSound } from './audio.js';
import { fx } from './effects.js';
import { buildCharacter, buildGun, makeNameTag } from './models.js';
import { fireBullet, meleeAttack, taserShot } from './combat.js';
import { throwGrenade } from './grenades.js';
import { spawnItem } from './items.js';
import { MAT_SOUND } from './map.js';

let nextId = 1;
const _v = new THREE.Vector3(), _d = new THREE.Vector3(), _e = new THREE.Vector3();
const RECOIL_SCALE = 2.0;      // weapon_recoil_scale
const VIEW_TRACK = 0.45;       // view_recoil_tracking -> camera shows punch * 0.9

export class WeaponInst {
  constructor(id) {
    this.id = id;
    this.def = WEAPONS[id];
    this.clip = this.def.clip;
    this.reserve = this.def.reserve;
    this.silenced = !!this.def.silencer;
    this.burstMode = false;
  }
}

function patternAt(def, idx) {
  const pts = recoilPattern(def);
  const i0 = Math.floor(idx), f = idx - i0;
  const a = pts[Math.min(i0, pts.length - 1)], b = pts[Math.min(i0 + 1, pts.length - 1)];
  return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f];
}

export class Character {
  constructor(opts) {
    this.id = nextId++;
    this.name = opts.name;
    this.team = opts.team;
    this.isBot = !!opts.isBot;
    this.isLocal = !!opts.isLocal;
    this.pos = new THREE.Vector3();
    this.vel = new THREE.Vector3();
    this.yaw = 0; this.pitch = 0;
    this.duck = 0; this.height = STAND_HEIGHT; this.onGround = true;
    this.alive = false;
    this.health = 100; this.armor = 0; this.helmet = false; this.kit = false;
    this.money = 800;
    this.stats = { kills: 0, deaths: 0, assists: 0, score: 0, mvps: 0, damage: 0, hs: 0 };
    this.round = { kills: 0, damage: 0, dmgTaken: new Map(), dmgDealt: new Map() };
    this.resetInventory();
    this.model = null; this.tag = null; this.ai = null;
    this.resetState();
  }

  resetInventory() {
    this.inv = { primary: null, secondary: null, knife: new WeaponInst('knife'), taser: null, grenades: [], c4: false };
    this.active = 'knife'; this.activeGrenade = null; this.lastSlot = 'knife';
  }

  resetState() {
    const now = world.time;
    this.nextAttack = now; this.deployUntil = now; this.reloading = false; this.reloadUntil = 0;
    this.recoilIndex = 0; this.recoilInacc = 0; this.punchP = 0; this.punchY = 0; this.viewKick = 0;
    this.lastShot = -10; this.zoom = 0; this.resumeZoom = 0; this.resumeZoomAt = 0;
    this.burstLeft = 0; this.nextBurst = 0; this.primeStart = 0; this.silencerUntil = 0;
    this.pinPulled = false; this.pinTime = 0; this.throwStr = 1; this.throwAnim = -10; this.pendingSwitchAt = 0;
    this.planting = false; this.plantStart = 0; this.plantProgress = 0; this.plantBeep = 0;
    this.defusing = false; this.defuseProgress = 0;
    this.inspectUntil = 0; this.knifeAnim = null; this.fireAnim = -10;
    this.prevAtk = false; this.prevAtk2 = false; this.prevReload = false; this.prevInspect = false; this.prevJump = false;
    this.landTime = -10; this.stepDist = 0; this.stepSide = 0; this.stepSmooth = 0;
    this.velMod = 1; this.flashUntil = 0; this.flashFull = 0; this.flashStart = 0;
    this.walkPhase = 0; this.deathT = 0; this.protectUntil = 0; this.airDucked = false;
    this.lastDamageTime = -10; this.lastAttacker = null;
  }

  // ---------------- model ----------------
  buildModel() {
    if (this.model) world.scene.remove(this.model);
    this.model = buildCharacter(world.mode === 'dm' ? (this.isLocal ? 'CT' : 'T') : this.team, this.id);
    world.scene.add(this.model);
    this.tpGun = null; this.tpGunKey = '';
    if (this.tag) { this.tag.material.map.dispose(); this.tag = null; }
  }
  ensureTag() {
    if (this.tag || this.isLocal) return;
    const col = this.team === 'CT' ? '#8db8ff' : '#ffd08a';
    this.tag = makeNameTag(this.name, col);
    this.model.add(this.tag);
    this.tag.position.y = 84;
  }

  // ---------------- inventory ----------------
  activeDef() {
    if (this.active === 'grenade') return GRENADES[this.activeGrenade];
    if (this.active === 'c4') return WEAPONS.c4;
    const w = this.inv[this.active];
    return w ? w.def : WEAPONS.knife;
  }
  activeInst() {
    if (this.active === 'grenade' || this.active === 'c4') return null;
    return this.inv[this.active] || null;
  }
  has(slot) {
    if (slot === 'grenade') return this.inv.grenades.length > 0;
    if (slot === 'c4') return this.inv.c4;
    return !!this.inv[slot];
  }
  bestSlot() {
    if (this.inv.primary) return 'primary';
    if (this.inv.secondary) return 'secondary';
    return 'knife';
  }
  grenadeCount(id) { return this.inv.grenades.filter(g => g === id).length; }
  canTakeGrenade(id) {
    const g = GRENADES[id];
    if (!g) return false;
    if (this.inv.grenades.length >= MAX_GRENADES) return false;
    return this.grenadeCount(id) < g.max;
  }
  switchTo(slot, gid) {
    if (!this.has(slot)) return false;
    if (slot === this.active && slot !== 'grenade' && slot !== 'knife') return false;
    if (slot === 'grenade') {
      const list = GRENADE_ORDER.filter(g => this.inv.grenades.includes(g));
      if (!gid) {
        if (this.active === 'grenade') { const k = list.indexOf(this.activeGrenade); gid = list[(k + 1) % list.length]; }
        else gid = list.includes(this.activeGrenade) ? this.activeGrenade : list[0];
      }
      if (this.active === 'grenade' && gid === this.activeGrenade) return false;
      this.activeGrenade = gid;
    }
    if (slot === 'knife' && this.active === 'knife' && this.inv.taser) slot = 'taser';
    else if (slot === 'knife' && this.active === 'taser') slot = 'knife';
    if (this.active !== slot) this.lastSlot = this.active;
    this.active = slot;
    const def = this.activeDef();
    this.deployUntil = world.time + (def.deploy || 0.8);
    this.nextAttack = Math.max(this.nextAttack, this.deployUntil);
    this.reloading = false; this.zoom = 0; this.resumeZoomAt = 0; this.pinPulled = false;
    this.planting = false; this.burstLeft = 0; this.inspectUntil = 0; this.primeStart = 0;
    if (this.isLocal) audio.play('deploy', { volume: 0.6 });
    return true;
  }
  quickSwitch() {
    const t = this.lastSlot;
    if (this.has(t) && t !== this.active) this.switchTo(t);
    else if (this.active !== this.bestSlot()) this.switchTo(this.bestSlot());
  }
  cycle(dirn) {
    const order = ['primary', 'secondary', 'knife', 'taser', 'grenade', 'c4'].filter(s => this.has(s));
    let k = order.indexOf(this.active);
    k = (k + dirn + order.length) % order.length;
    this.switchTo(order[k]);
  }
  give(id) {
    const def = WEAPONS[id];
    if (GRENADES[id]) { if (this.canTakeGrenade(id)) { this.inv.grenades.push(id); return true; } return false; }
    if (!def) return false;
    if (def.slot === 'taser') { this.inv.taser = new WeaponInst(id); return true; }
    if (def.slot === 'primary' || def.slot === 'secondary') {
      if (this.inv[def.slot]) this.dropSlot(def.slot);
      this.inv[def.slot] = new WeaponInst(id);
      if (this.alive) { this.active = '';  this.switchTo(def.slot); }
      return true;
    }
    return false;
  }
  dropSlot(slot, toss = 1) {
    const eye = this.eyePos(_e);
    const dir = this.viewDir(_d);
    const vel = new THREE.Vector3(dir.x * 220 * toss, 120 * toss, dir.z * 220 * toss).add(this.vel);
    const pos = new THREE.Vector3(eye.x + dir.x * 10, eye.y - 10, eye.z + dir.z * 10);
    if (slot === 'c4') {
      if (!this.inv.c4) return;
      this.inv.c4 = false;
      spawnItem('c4', null, pos, vel, this);
      world.game?.onBombDropped(this);
    } else if (slot === 'grenade') {
      const gid = this.activeGrenade;
      const k = this.inv.grenades.indexOf(gid);
      if (k < 0) return;
      this.inv.grenades.splice(k, 1);
      spawnItem('grenade', gid, pos, vel, this);
    } else {
      const inst = this.inv[slot];
      if (!inst) return;
      this.inv[slot] = null;
      spawnItem('weapon', inst, pos, vel, this);
    }
    if (this.active === slot && !(slot === 'grenade' && this.inv.grenades.length)) {
      this.active = '';
      this.switchTo(this.bestSlot());
    } else if (slot === 'grenade' && this.inv.grenades.length) {
      this.activeGrenade = this.inv.grenades[0];
    }
  }
  dropActive() {
    if (['primary', 'secondary', 'c4', 'grenade'].includes(this.active)) this.dropSlot(this.active);
  }
  addMoney(n, reason) {
    const max = world.match?.maxMoney ?? 16000;
    const before = this.money;
    this.money = clamp(this.money + n, 0, max);
    if (this.isLocal && this.money !== before) world.hud?.moneyDelta(this.money - before, reason);
  }

  // ---------------- geometry helpers ----------------
  eyeHeight() { return lerp(STAND_EYE, CROUCH_EYE, this.duck) - this.stepSmooth; }
  eyePos(out) { return out.set(this.pos.x, this.pos.y + this.eyeHeight(), this.pos.z); }
  viewDir(out) { return dirFromAngles(this.yaw, this.pitch, out); }
  speed2d() { return Math.hypot(this.vel.x, this.vel.z); }

  maxSpeed() {
    const def = this.activeDef();
    let s = def.speed || 250;
    if (this.zoom > 0 && def.scopedSpeed) s = def.scopedSpeed;
    return s;
  }

  blindAmount() {
    const now = world.time;
    if (now >= this.flashUntil) return 0;
    const full = this.flashStart + this.flashFull;
    if (now < full) return 1;
    return clamp((this.flashUntil - now) / Math.max(0.01, this.flashUntil - full), 0, 1);
  }

  // ---------------- per-frame ----------------
  update(dt, input) {
    if (!this.alive) { this.vel.set(0, 0, 0); return; }
    this.movement(dt, input);
    this.weaponThink(dt, input);
  }

  movement(dt, input) {
    const map = world.map;
    const frozen = world.match?.freeze || this.planting || this.defusing;
    // ---- ducking ----
    const wantDuck = input.crouch ? 1 : 0;
    if (wantDuck > this.duck) {
      if (!this.onGround && !this.airDucked && this.duck < 0.05) {
        // crouch-jump: pull the legs up
        if (!map.hullBlocked(this.pos.x, this.pos.z, this.pos.y + 18, CROUCH_HEIGHT, PLAYER_RADIUS, 0)) { this.pos.y += 18; this.stepSmooth -= 18; this.airDucked = true; this.duck = 1; }
      }
      this.duck = Math.min(1, this.duck + dt / 0.12);
    } else if (wantDuck < this.duck) {
      // need headroom to stand up
      const ceil = map.ceilingAbove(this.pos.x, this.pos.z, PLAYER_RADIUS - 0.5, this.pos.y);
      let canStand = ceil - this.pos.y >= STAND_HEIGHT;
      if (!this.onGround && this.airDucked) {
        if (!map.hullBlocked(this.pos.x, this.pos.z, this.pos.y - 18, STAND_HEIGHT, PLAYER_RADIUS, 0) && this.pos.y - 18 > map.groundUnder(this.pos.x, this.pos.z, PLAYER_RADIUS - 0.5)) {
          this.pos.y -= 18; this.stepSmooth += 18; this.airDucked = false;
        } else canStand = false;
      }
      if (canStand) this.duck = Math.max(0, this.duck - dt / 0.12);
    }
    this.height = lerp(STAND_HEIGHT, CROUCH_HEIGHT, this.duck);

    // ---- wish direction ----
    let fwd = frozen ? 0 : input.fwd, side = frozen ? 0 : input.side;
    const sy = Math.sin(this.yaw), cy = Math.cos(this.yaw);
    let wx = -sy * fwd + cy * side, wz = -cy * fwd - sy * side;
    const wl = Math.hypot(wx, wz);
    if (wl > 0) { wx /= wl; wz /= wl; }
    let maxSpd = this.maxSpeed() * this.velMod;
    if (input.walk) maxSpd *= WALK_MULT;
    if (this.duck > 0.5 && this.onGround) maxSpd *= DUCK_MULT;
    const wishSpeed = wl > 0 ? maxSpd : 0;
    this.velMod = Math.min(1, this.velMod + dt * 1.2);

    // ---- jump ----
    if (input.jump && !this.prevJump && this.onGround && !frozen) {
      this.vel.y = JUMP_VELOCITY;
      this.onGround = false;
      this.airDucked = false;
      if (!input.walk) this.makeNoise(700, 'jump');
    }
    this.prevJump = input.jump;

    if (this.onGround) {
      // friction
      const sp = Math.hypot(this.vel.x, this.vel.z);
      if (sp > 0.1) {
        const control = Math.max(sp, STOP_SPEED);
        const drop = control * FRICTION * dt;
        const ns = Math.max(sp - drop, 0) / sp;
        this.vel.x *= ns; this.vel.z *= ns;
      } else { this.vel.x = 0; this.vel.z = 0; }
      // accelerate
      if (wishSpeed > 0) {
        const cur = this.vel.x * wx + this.vel.z * wz;
        const add = wishSpeed - cur;
        if (add > 0) {
          const acc = Math.min(ACCELERATE * dt * wishSpeed, add);
          this.vel.x += acc * wx; this.vel.z += acc * wz;
        }
      }
      // clamp to max (e.g. after scoping in)
      const s2 = Math.hypot(this.vel.x, this.vel.z);
      if (s2 > maxSpd && s2 > 1) { const k = maxSpd / s2; this.vel.x *= lerp(1, k, Math.min(1, dt * 10)); this.vel.z *= lerp(1, k, Math.min(1, dt * 10)); }
    } else if (wishSpeed > 0) {
      const ws = Math.min(wishSpeed, AIR_WISHSPEED);
      const cur = this.vel.x * wx + this.vel.z * wz;
      const add = ws - cur;
      if (add > 0) {
        const acc = Math.min(AIR_ACCELERATE * wishSpeed * dt, add);
        this.vel.x += acc * wx; this.vel.z += acc * wz;
      }
    }

    // ---- horizontal move with collision ----
    this.moveAxis(0, this.vel.x * dt);
    this.moveAxis(2, this.vel.z * dt);

    // ---- vertical ----
    const R = PLAYER_RADIUS - 0.5;
    const ground = map.groundUnder(this.pos.x, this.pos.z, R);
    if (this.onGround) {
      if (ground > this.pos.y + 0.01) {
        this.stepSmooth += ground - this.pos.y; // smooth camera on step-up
        this.pos.y = ground;
      } else if (ground < this.pos.y - 0.01) {
        if (this.pos.y - ground <= STEP_HEIGHT + 0.5) { this.stepSmooth -= this.pos.y - ground; this.pos.y = ground; }
        else { this.onGround = false; this.vel.y = 0; }
      }
    }
    if (!this.onGround) {
      this.vel.y -= GRAVITY * dt * 0.5;
      this.pos.y += this.vel.y * dt;
      this.vel.y -= GRAVITY * dt * 0.5;
      const ceil = map.ceilingAbove(this.pos.x, this.pos.z, R, this.pos.y - 1);
      if (this.pos.y + this.height > ceil) { this.pos.y = ceil - this.height; if (this.vel.y > 0) this.vel.y = 0; }
      const g2 = map.groundUnder(this.pos.x, this.pos.z, R);
      if (this.pos.y <= g2) {
        const fallSpeed = -this.vel.y;
        this.pos.y = g2; this.vel.y = 0; this.onGround = true; this.airDucked = false;
        this.landTime = world.time;
        if (fallSpeed > 580) {
          const dmg = Math.round((fallSpeed - 580) * (100 / (1024 - 580)));
          world.game?.damage(this, null, dmg, 'generic', null, { fall: true });
        }
        if (fallSpeed > 200) {
          this.velMod = Math.min(this.velMod, 0.82);
          audio.play('land', { pos: this.isLocal ? null : this.pos, volume: this.isLocal ? 0.5 : 0.9, maxDist: 1200 });
          if (fallSpeed > 300) this.makeNoise(900, 'land');
        }
      }
    }
    // smooth step offset back to zero
    this.stepSmooth *= Math.exp(-dt * 14);
    if (Math.abs(this.stepSmooth) < 0.05) this.stepSmooth = 0;

    // ---- footsteps ----
    const hs = Math.hypot(this.vel.x, this.vel.z);
    if (this.onGround && hs > 136 && !input.walk && this.duck < 0.5) {
      this.stepDist += hs * dt;
      if (this.stepDist > 78) {
        this.stepDist = 0;
        this.stepSide ^= 1;
        const top = map.topMat[map.idx(map.col(this.pos.x), map.row(this.pos.z))];
        const s = MAT_SOUND[top] === 'metal' ? 'stepmetal' : (this.stepSide ? 'step' : 'step2');
        audio.play(s, { pos: this.isLocal ? null : this.pos, volume: this.isLocal ? 0.28 : 1.0, maxDist: 1300, jitter: 0.15, ref: 120 });
        this.makeNoise(1100, 'step');
      }
    } else this.stepDist = Math.min(this.stepDist, 60);
  }

  moveAxis(axis, d) {
    if (Math.abs(d) < 1e-6) return;
    const map = world.map, R = PLAYER_RADIUS;
    const step = this.onGround ? STEP_HEIGHT : 0;
    const x0 = this.pos.x, z0 = this.pos.z;
    const test = (k) => map.hullBlocked(axis === 0 ? x0 + d * k : x0, axis === 2 ? z0 + d * k : z0, this.pos.y, this.height, R, step);
    if (!test(1)) { if (axis === 0) this.pos.x = x0 + d; else this.pos.z = z0 + d; return; }
    let lo = 0, hi = 1;
    for (let i = 0; i < 10; i++) { const m = (lo + hi) / 2; if (test(m)) hi = m; else lo = m; }
    if (axis === 0) { this.pos.x = x0 + d * lo; this.vel.x = 0; } else { this.pos.z = z0 + d * lo; this.vel.z = 0; }
  }

  makeNoise(radius, kind) { world.game?.noise(this, radius, kind); }

  // ---------------- weapons ----------------
  cycleTime(def) { return 60 / (def.rpm || 600); }

  currentInaccuracy(def = this.activeDef()) {
    const ia = def.inacc;
    if (!ia) return 0;
    const scoped = this.zoom > 0 && ia.scoped !== undefined;
    let base = scoped ? ia.scoped : (this.duck > 0.8 && this.onGround ? ia.crouch : ia.stand);
    const maxSpd = def.speed || 250;
    const hs = this.speed2d();
    const f = clamp((hs - maxSpd * 0.34) / (maxSpd * 0.66), 0, 1);
    base += ia.move * f;
    if (!this.onGround) {
      if (ia.jumpApex !== undefined && Math.abs(this.vel.y) < 25) base += ia.jumpApex;
      else base += ia.jump;
    }
    const sinceLand = world.time - this.landTime;
    if (sinceLand < 0.35) base += ia.land * (1 - sinceLand / 0.35);
    return base + this.recoilInacc;
  }

  weaponThink(dt, input) {
    const now = world.time;
    if (this.pendingSwitchAt && now >= this.pendingSwitchAt) {
      this.pendingSwitchAt = 0;
      if (this.active === 'grenade' && !this.inv.grenades.includes(this.activeGrenade)) {
        const back = this.has(this.lastSlot) && this.lastSlot !== 'grenade' ? this.lastSlot : this.bestSlot();
        this.active = ''; this.switchTo(back);
      }
    }
    const def = this.activeDef();
    const inst = this.activeInst();
    const atk = !!input.attack, atk2 = !!input.attack2;
    const atkP = atk && !this.prevAtk, atk2P = atk2 && !this.prevAtk2;
    this.prevAtk = atk; this.prevAtk2 = atk2;
    const reloadP = input.reload && !this.prevReload; this.prevReload = !!input.reload;
    const inspP = input.inspect && !this.prevInspect; this.prevInspect = !!input.inspect;

    // recoil recovery
    if (def.inacc) this.recoilInacc *= Math.exp(-dt * 2.6 / (def.inacc.recover || 0.4));
    if (this.recoilIndex > 0 && now - this.lastShot > this.cycleTime(def) * 1.05) {
      this.recoilIndex = Math.max(0, this.recoilIndex - dt * Math.max(9, this.recoilIndex * 4.2));
    }
    if (def.recoil && def.cat !== 'knife' && def.cat !== 'grenade' && def.cat !== 'c4') {
      const pp = patternAt(def, this.recoilIndex);
      this.punchP = pp[0]; this.punchY = pp[1];
    } else { this.punchP = 0; this.punchY = 0; }
    this.viewKick *= Math.exp(-dt * 14);

    if (this.resumeZoomAt && now >= this.resumeZoomAt) {
      if (!this.reloading && inst && def.zoom) this.zoom = this.resumeZoom;
      this.resumeZoomAt = 0;
    }
    if (inspP && !this.reloading && now >= this.deployUntil) this.inspectUntil = now + 3.2;
    if (atk || atk2 || this.reloading) this.inspectUntil = 0;

    const frozen = world.match?.freeze;
    if (this.planting && (!atk || this.active !== 'c4' || !this.onGround)) { this.planting = false; this.plantProgress = 0; }
    if (frozen || this.defusing) return;
    if (now < this.deployUntil) return;

    switch (def.cat) {
      case 'knife': this.thinkKnife(atk, atk2, now); break;
      case 'taser': this.thinkTaser(atkP, now, inst); break;
      case 'grenade': this.thinkGrenade(atk, atk2, now); break;
      case 'c4': this.thinkC4(atk, atkP, now); break;
      default: this.thinkGun(def, inst, atk, atkP, atk2P, reloadP, now); break;
    }
  }

  startReload(def, inst) {
    if (this.reloading || !inst || inst.clip >= def.clip || inst.reserve <= 0 || !def.reload) return;
    this.reloading = true;
    this.reloadStart = world.time;
    this.reloadUntil = world.time + (def.shellReload ? def.reload + 0.35 : def.reload);
    if (this.zoom) { this.zoom = 0; }
    this.resumeZoomAt = 0;
    this.burstLeft = 0;
    if (this.isLocal) { audio.play('magout', { volume: 0.7 }); if (!def.shellReload) after(def.reload * 0.65, () => { if (this.reloading && this.isLocal) audio.play('magin', { volume: 0.8 }); }); }
    else audio.play('magout', { pos: this.pos, volume: 0.6, maxDist: 700 });
    this.makeNoise(450, 'reload');
  }

  thinkGun(def, inst, atk, atkP, atk2P, reloadP, now) {
    if (!inst) return;
    if (this.reloading) {
      if (def.shellReload) {
        if (atkP && inst.clip > 0) { this.reloading = false; }
        else if (now >= this.reloadUntil) {
          inst.clip++; inst.reserve--;
          if (this.isLocal) audio.play('magin', { volume: 0.6 });
          if (inst.clip < def.clip && inst.reserve > 0) this.reloadUntil = now + def.reload;
          else this.reloading = false;
        }
        if (this.reloading) return;
      } else {
        if (now < this.reloadUntil) return;
        const take = Math.min(def.clip - inst.clip, inst.reserve);
        inst.clip += take; inst.reserve -= take;
        this.reloading = false;
      }
    }
    if (now < this.silencerUntil) return;
    // alternate fire
    if (atk2P) {
      if (def.zoom) {
        this.zoom = (this.zoom + 1) % (def.zoom.length + 1);
        this.resumeZoomAt = 0;
        if (this.isLocal) audio.play('zoom', { volume: 0.7 });
      } else if (def.silencer && !def.fixedSilencer) {
        this.silencerUntil = now + 1.6;
        this.silencerAnim = now;
        after(0.9, () => { inst.silenced = !inst.silenced; });
        if (this.isLocal) world.hud?.hint(inst.silenced ? 'Susturucu çıkarılıyor...' : 'Susturucu takılıyor...', 1.4);
        return;
      } else if (def.burst) {
        inst.burstMode = !inst.burstMode;
        if (this.isLocal) { audio.play('click'); world.hud?.hint(inst.burstMode ? 'Seri atış moduna geçildi' : (def.id === 'glock' ? 'Yarı otomatik moda geçildi' : 'Tam otomatik moda geçildi'), 1.2); }
      } else if (def.prime && now >= this.nextAttack && inst.clip > 0) {
        this.fireShot(def, inst, 2.5); // revolver fan fire
        this.nextAttack = now + 0.4;
        return;
      }
    }
    if (reloadP) { this.startReload(def, inst); if (this.reloading) return; }
    // burst continuation
    if (this.burstLeft > 0 && now >= this.nextBurst) {
      if (inst.clip > 0) { this.fireShot(def, inst); this.burstLeft--; this.nextBurst += def.burst.interval; }
      else this.burstLeft = 0;
      return;
    }
    if (!atk) { this.primeStart = 0; return; }
    if (inst.clip <= 0) {
      if (atkP) {
        if (this.isLocal) audio.play('click', { volume: 0.7 });
        if (inst.reserve > 0) this.startReload(def, inst);
        this.nextAttack = now + 0.2;
      }
      return;
    }
    if (now < this.nextAttack) return;
    if (def.prime) {
      if (!this.primeStart) { this.primeStart = now; if (this.isLocal) audio.play('click', { volume: 0.4 }); }
      if (now - this.primeStart < def.prime) return;
      this.primeStart = 0;
      this.fireShot(def, inst);
      return;
    }
    const burst = inst.burstMode && def.burst;
    if (!(def.auto && !burst) && !atkP) return;
    this.fireShot(def, inst);
    if (burst) {
      this.burstLeft = def.burst.shots - 1;
      this.nextBurst = now + def.burst.interval;
      this.nextAttack = now + (def.id === 'glock' ? 0.5 : 0.55);
    }
  }

  fireShot(def, inst, inaccMul = 1) {
    const now = world.time;
    inst.clip--;
    this.lastShot = now;
    this.fireAnim = now;
    this.nextAttack = now + this.cycleTime(def);
    const eye = this.eyePos(_e).clone();
    const inacc = this.currentInaccuracy(def) * inaccMul;
    this.recoilInacc += def.inacc.fire;
    const pp = patternAt(def, this.recoilIndex);
    this.recoilIndex += 1;
    const spread = def.inacc.spread;
    const dir = new THREE.Vector3();
    let lastEnd = null;
    for (let p = 0; p < def.pellets; p++) {
      const a1 = Math.random() * Math.PI * 2, r1 = Math.random();
      const a2 = Math.random() * Math.PI * 2, r2 = Math.random();
      const ox = (Math.cos(a1) * r1 * inacc + Math.cos(a2) * r2 * spread) / 1000;
      const oy = (Math.sin(a1) * r1 * inacc + Math.sin(a2) * r2 * spread) / 1000;
      const yaw = this.yaw - pp[1] * DEG * RECOIL_SCALE + ox;
      const pitch = this.pitch + pp[0] * DEG * RECOIL_SCALE + oy;
      dirFromAngles(yaw, pitch, dir);
      lastEnd = fireBullet(this, def, inst, eye, dir, p === 0);
    }
    this.viewKick += def.recoil.kick;
    const snd = gunSound(def, inst.silenced);
    const big = def.id === 'awp' ? 9000 : 6000;
    audio.play(snd, this.isLocal ? { volume: 0.9 } : { pos: eye, volume: 1.0, maxDist: inst.silenced ? 1400 : big, ref: 300, rolloff: 0.9 });
    this.makeNoise(inst.silenced ? 500 : (def.cat === 'sniper' ? 3500 : 2600), 'shot');
    if (!this.isLocal || world.spectating) {
      const md = this.viewDir(_d);
      const mp = _v.set(eye.x + md.x * 30 - Math.cos(this.yaw) * -4, eye.y - 7, eye.z + md.z * 30);
      if (!inst.silenced) fx.muzzle(mp, md, def.cat === 'sniper' ? 1.4 : 1);
    }
    if (def.cat === 'sniper' && !def.auto) {
      if (this.zoom > 0) { this.resumeZoom = this.zoom; this.zoom = 0; this.resumeZoomAt = this.nextAttack - 0.05; }
      if (this.isLocal) after(0.45, () => audio.play('bolt', { volume: 0.6 }));
    }
    if (def.shellReload && this.reloading) this.reloading = false;
  }

  thinkKnife(atk, atk2, now) {
    if (now < this.nextAttack) return;
    if (atk) {
      const hit = meleeAttack(this, false);
      this.nextAttack = now + (hit ? 0.5 : 0.4);
      this.knifeAnim = { t: now, heavy: false };
    } else if (atk2) {
      meleeAttack(this, true);
      this.nextAttack = now + 1.0;
      this.knifeAnim = { t: now, heavy: true };
    }
  }

  thinkTaser(atkP, now, inst) {
    if (!inst || !atkP || now < this.nextAttack || inst.clip <= 0) return;
    inst.clip--;
    this.fireAnim = now;
    this.nextAttack = now + 1;
    taserShot(this);
    audio.play('taser', this.isLocal ? { volume: 0.8 } : { pos: this.pos, maxDist: 1500 });
    after(0.6, () => { if (this.inv.taser === inst) { this.inv.taser = null; if (this.active === 'taser') { this.active = ''; this.switchTo(this.bestSlot()); } } });
  }

  thinkGrenade(atk, atk2, now) {
    if (!this.inv.grenades.includes(this.activeGrenade)) return;
    if (!this.pinPulled) {
      if ((atk || atk2) && now >= this.nextAttack) {
        this.pinPulled = true; this.pinTime = now;
        if (this.isLocal) audio.play('pin', { volume: 0.6 });
      }
      return;
    }
    if (atk || atk2) { this.throwStr = atk && atk2 ? 0.5 : (atk ? 1.0 : 0.25); return; }
    if (now - this.pinTime < 0.2) return;
    this.doThrow(this.activeGrenade, this.throwStr);
  }

  doThrow(gid, strength, overrideDir) {
    const k = this.inv.grenades.indexOf(gid);
    if (k < 0) return false;
    this.inv.grenades.splice(k, 1);
    throwGrenade(this, gid, strength, overrideDir);
    this.pinPulled = false;
    this.throwAnim = world.time;
    this.nextAttack = world.time + 0.5;
    this.pendingSwitchAt = world.time + 0.4;
    audio.play('throw', this.isLocal ? { volume: 0.6 } : { pos: this.pos, volume: 0.6, maxDist: 900 });
    return true;
  }

  thinkC4(atk, atkP, now) {
    const site = world.map.zoneAt(this.pos.x, this.pos.z);
    if (!atk) { this.planting = false; this.plantProgress = 0; return; }
    if (!site || world.mode === 'dm') {
      if (atkP && this.isLocal) world.hud?.hint('C4 yalnızca bomba bölgesine kurulabilir.', 2);
      return;
    }
    if (!this.onGround) return;
    if (world.bomb && world.bomb.state === 'planted') return;
    if (!this.planting) {
      this.planting = true; this.plantStart = now; this.plantBeep = now;
      this.vel.set(0, 0, 0);
      if (this.isLocal) audio.play('keypress', { volume: 0.7 });
    }
    this.plantProgress = (now - this.plantStart) / 3.2;
    if (now - this.plantBeep > 0.42) { this.plantBeep = now; audio.play('keypress', this.isLocal ? { volume: 0.6 } : { pos: this.pos, maxDist: 900, volume: 0.7 }); }
    if (this.plantProgress >= 1) {
      this.planting = false; this.plantProgress = 0;
      this.inv.c4 = false;
      world.game?.plantBomb(this, site);
      this.active = '';
      this.switchTo(this.bestSlot());
    }
  }

  // ---------------- hitboxes ----------------
  hitShapes() {
    const out = this._shapes || (this._shapes = []);
    out.length = 0;
    const p = this.pos, s = this.height / STAND_HEIGHT;
    const fx_ = -Math.sin(this.yaw), fz = -Math.cos(this.yaw), rx = Math.cos(this.yaw), rz = -Math.sin(this.yaw);
    const eh = lerp(STAND_EYE, CROUCH_EYE, this.duck);
    out.push({ x: p.x + fx_ * 1.5, y: p.y + eh + 2.5, z: p.z + fz * 1.5, r: 6.0, g: 'head' });
    for (const hy of [46, 52, 57]) out.push({ x: p.x, y: p.y + hy * s, z: p.z, r: 9.5, g: 'chest' });
    for (const hy of [33, 40]) out.push({ x: p.x, y: p.y + hy * s, z: p.z, r: 8.8, g: 'stomach' });
    for (const side of [-1, 1]) {
      for (const hy of [26, 17, 8, 2.5]) out.push({ x: p.x + rx * side * 4.4, y: p.y + hy * s, z: p.z + rz * side * 4.4, r: 5.4, g: 'legs' });
      out.push({ x: p.x + fx_ * 10 + rx * side * 6, y: p.y + 48 * s, z: p.z + fz * 10 + rz * side * 6, r: 4.2, g: 'chest' });
    }
    return out;
  }

  // ---------------- third person model ----------------
  updateModel(dt) {
    const m = this.model;
    if (!m) return;
    const u = m.userData;
    m.position.copy(this.pos);
    m.rotation.y = this.yaw;
    if (!this.alive) {
      this.deathT += dt;
      const k = Math.min(1, this.deathT / 0.55);
      m.rotation.x = this.deathDir * k * k * (Math.PI / 2);
      m.rotation.z = this.deathRoll * k;
      m.position.y = this.pos.y + 4 * k;
      u.legs.forEach(l => { l.thigh.rotation.x *= 0.9; l.knee.rotation.x *= 0.9; });
      if (this.tag) this.tag.visible = false;
      return;
    }
    m.rotation.x = 0; m.rotation.z = 0;
    // legs
    const hs = this.speed2d();
    const amt = clamp(hs / 220, 0, 1);
    this.walkPhase += hs * dt * 0.042;
    const sw = Math.sin(this.walkPhase) * 0.6 * amt;
    const crouch = this.duck;
    u.hips.position.y = lerp(36, 25, crouch);
    u.legs[0].thigh.rotation.x = sw + crouch * 1.0;
    u.legs[1].thigh.rotation.x = -sw + crouch * 1.0;
    u.legs[0].knee.rotation.x = -Math.max(0, -Math.sin(this.walkPhase)) * 0.9 * amt - crouch * 1.9;
    u.legs[1].knee.rotation.x = -Math.max(0, Math.sin(this.walkPhase)) * 0.9 * amt - crouch * 1.9;
    if (!this.onGround) { u.legs[0].thigh.rotation.x = 0.5; u.legs[1].thigh.rotation.x = 0.2; u.legs[0].knee.rotation.x = -0.9; u.legs[1].knee.rotation.x = -0.5; }
    u.torso.rotation.x = this.pitch * 0.45 + crouch * 0.1;
    u.neck.rotation.x = this.pitch * 0.4;
    // weapon in hands
    const def = this.activeDef();
    const inst = this.activeInst();
    const key = def.id + (inst && inst.silenced ? 's' : '');
    if (key !== this.tpGunKey) {
      if (this.tpGun) u.gunMount.remove(this.tpGun);
      this.tpGun = buildGun(def, { silenced: inst && inst.silenced }).group;
      u.gunMount.add(this.tpGun);
      this.tpGunKey = key;
    }
    // recoil / reload pose
    const sinceFire = world.time - this.fireAnim;
    u.gunMount.position.z = -15 + (sinceFire < 0.08 ? 2 : 0);
    u.gunMount.rotation.x = this.reloading ? -0.6 : 0;
    if (this.tag) this.tag.visible = true;
  }

  die(attacker) {
    this.alive = false;
    this.deathT = 0;
    this.deathDir = Math.random() < 0.7 ? 1 : -1;
    this.deathRoll = (Math.random() - 0.5) * 0.6;
    this.zoom = 0; this.reloading = false; this.planting = false; this.defusing = false; this.pinPulled = false;
    // drop the best gun and the bomb
    const slot = this.inv.primary ? 'primary' : (this.inv.secondary ? 'secondary' : null);
    const yaw = this.yaw;
    if (slot) {
      const inst = this.inv[slot]; this.inv[slot] = null;
      spawnItem('weapon', inst, new THREE.Vector3(this.pos.x, this.pos.y + 40, this.pos.z), new THREE.Vector3(-Math.sin(yaw) * 60, 80, -Math.cos(yaw) * 60), this);
    }
    if (this.inv.c4) {
      this.inv.c4 = false;
      spawnItem('c4', null, new THREE.Vector3(this.pos.x, this.pos.y + 30, this.pos.z), new THREE.Vector3(Math.random() * 40 - 20, 60, Math.random() * 40 - 20), this);
      world.game?.onBombDropped(this, true);
    }
    // everything else is lost on death
    this.inv.secondary = null; this.inv.grenades = []; this.inv.taser = null;
    this.armor = 0; this.helmet = false; this.kit = false;
    this.active = 'knife';
  }
}
