// Bot AI: perception, aiming, combat, navigation, team strategy, buying, utility.
import * as THREE from './vendor/three.module.min.js';
import { world, isEnemy, clamp, angleDiff, anglesFromDir, pick, rand, DEG, shuffle, dirFromAngles } from './state.js';
import { losToChar, rayChars } from './combat.js';
import { solveThrow, fireAt, pointInSmoke } from './grenades.js';
import { WEAPONS } from './weapons.js';

export const DIFFICULTY = {
  easy: { react: 0.7, turn: 200, err: 3.4, hs: 0.08, spray: 0.2, stop: 0.15, fov: 100, hear: 0.6, util: 0.3 },
  normal: { react: 0.45, turn: 320, err: 2.2, hs: 0.22, spray: 0.5, stop: 0.55, fov: 110, hear: 0.8, util: 0.6 },
  hard: { react: 0.3, turn: 480, err: 1.3, hs: 0.4, spray: 0.75, stop: 0.85, fov: 120, hear: 1.0, util: 0.85 },
  expert: { react: 0.2, turn: 650, err: 0.7, hs: 0.55, spray: 0.92, stop: 1.0, fov: 130, hear: 1.1, util: 1.0 },
};

export const BOT_NAMES = ['Kartal', 'Poyraz', 'Bozkurt', 'Atlas', 'Toprak', 'Yıldırım', 'Kaya', 'Demir', 'Fırtına', 'Doruk', 'Alaz', 'Tuna', 'Efe', 'Mert', 'Baran', 'Arda', 'Kuzey', 'Deniz', 'Ozan', 'Volkan', 'Rüzgar', 'Çınar', 'Aras', 'Bora'];

const SAY = {
  spot: ['Düşman görüldü!', 'Burada biri var!', 'Kontak!', 'Düşman burada!'],
  plantA: ['Bomba A\'ya kuruldu!', 'C4 A\'da, koruyun!'],
  plantB: ['Bomba B\'ye kuruldu!', 'C4 B\'de, koruyun!'],
  defuse: ['İmha ediyorum, koruyun!', 'Bombayı söküyorum!'],
  smoke: ['Sis atıyorum!', 'Sis geliyor!'],
  flash: ['Flaş geliyor!', 'Flaş!'],
  molly: ['Molotof atıyorum!', 'Yakıyorum!'],
  goA: ['A\'ya gidiyoruz!', 'Hepsi A!'],
  goB: ['B\'ye gidiyoruz!', 'Rush B!'],
  bomb: ['Bombayı alıyorum.', 'C4 bende.'],
  rotate: ['Rotasyon yapıyorum!', 'Geliyorum!'],
  last: ['Son kişi kaldı!', 'Bir kişi kaldı!'],
  nice: ['Güzel!', 'Tamamdır.', 'İyi atış!'],
};
function say(ch, key, chance = 1) {
  if (Math.random() > chance) return;
  const now = world.time;
  if (ch.ai && now - (ch.ai.lastSay || -10) < 3) return;
  if (ch.ai) ch.ai.lastSay = now;
  world.hud?.chat(ch, pick(SAY[key]));
}

const _e = new THREE.Vector3(), _f = new THREE.Vector3(), _t = new THREE.Vector3();

export class BotBrain {
  constructor(ch) { this.ch = ch; this.reset(); }
  get d() { return DIFFICULTY[world.difficulty] || DIFFICULTY.normal; }
  reset() {
    this.path = null; this.pathIdx = 0; this.goal = null; this.goalKey = ''; this.look = null;
    this.target = null; this.targetVisible = false; this.reactLeft = 0; this.lastSeenPos = null; this.lastSeenTime = -10;
    this.errP = 0; this.errY = 0; this.aimHead = false; this.nextPercept = 0; this.nextPlan = 0; this.repathAt = 0;
    this.heard = null; this.strafeT = 0; this.strafeDir = 1; this.burst = 0; this.burstPause = 0; this.tapToggle = false;
    this.stuckCheck = 0; this.stuckPos = null; this.blindUntil = 0; this.role = null; this.utilDone = false; this.ctUtilDone = false;
    this.arrived = false; this.wait = 0; this.engageStop = Math.random(); this.spotSaid = false; this.wander = null; this.defuseCommit = false;
  }
  onDamaged(att) {
    if (att && isEnemy(att, this.ch)) { this.heard = { pos: att.pos.clone(), time: world.time, kind: 'hurt' }; }
  }
  onFlashed(dur) { this.blindUntil = world.time + dur * 0.85; }
  hear(pos, kind) {
    if (this.targetVisible) return;
    if (!this.heard || world.time - this.heard.time > 0.5 || kind === 'shot') this.heard = { pos: pos.clone(), time: world.time, kind };
  }

  update(dt) {
    const ch = this.ch;
    const input = { fwd: 0, side: 0, jump: false, crouch: false, walk: false, attack: false, attack2: false, reload: false, use: false };
    if (!ch.alive) return input;
    const now = world.time;
    if (now >= this.nextPercept) { this.nextPercept = now + 0.1 + Math.random() * 0.03; this.perceive(); }
    if (now >= this.nextPlan) { this.nextPlan = now + 0.5; this.plan(); }
    if (world.match?.freeze) { this.idleLook(dt); return input; }

    const blind = ch.blindAmount() > 0.5;
    if (blind) {
      // flashed: panic, back off, spray at last known position
      input.fwd = -0.6; input.side = Math.sin(now * 3) ;
      if (this.lastSeenPos && now - this.lastSeenTime < 2) { this.aimAt(this.lastSeenPos.clone().setY(this.lastSeenPos.y + 50), dt, 0.3); input.attack = Math.random() < 0.5; }
      else ch.yaw += dt * 2;
      return input;
    }

    if (this.target && this.target.alive && (this.targetVisible || now - this.lastSeenTime < 1.6)) {
      this.combat(dt, input);
    } else {
      this.target = null;
      this.objective(dt, input);
    }
    this.weaponUpkeep(input);
    return input;
  }

  // ---------------- perception ----------------
  perceive() {
    const ch = this.ch, now = world.time;
    const eye = ch.eyePos(_e).clone();
    const fwd = ch.viewDir(_f).clone();
    const cosF = Math.cos((this.d.fov / 2) * DEG);
    let best = null, bestScore = Infinity, bestVis = null;
    if (ch.blindAmount() < 0.6) {
      for (const e of world.chars) {
        if (!e.alive || !isEnemy(ch, e)) continue;
        _t.set(e.pos.x - eye.x, e.pos.y + 40 - eye.y, e.pos.z - eye.z);
        const d = _t.length();
        if (d > 5000) continue;
        _t.divideScalar(d);
        const dot = _t.dot(fwd);
        if (dot < cosF && d > 160 && e !== this.target) continue;
        const vis = losToChar(eye, e);
        if (!vis) continue;
        const score = d * (e === this.target ? 0.6 : 1) * (dot > 0.9 ? 0.8 : 1);
        if (score < bestScore) { best = e; bestScore = score; bestVis = vis; }
      }
    }
    if (best) {
      if (best !== this.target) {
        const d = best.pos.distanceTo(ch.pos);
        const surprised = this.lastSeenTime < now - 3;
        this.target = best;
        this.reactLeft = this.d.react * rand(0.75, 1.35) * (surprised ? 1 : 0.6) + (d > 2000 ? 0.1 : 0);
        this.errP = (Math.random() - 0.5) * this.d.err * 2.2 * DEG;
        this.errY = (Math.random() - 0.5) * this.d.err * 3.0 * DEG;
        this.aimHead = Math.random() < this.d.hs;
        this.engageStop = Math.random();
        this.burst = 0; this.burstPause = 0;
        if (!this.spotSaid && world.mode !== 'dm') { say(ch, 'spot', 0.35); this.spotSaid = true; }
      }
      this.targetVisible = true;
      this.lastSeenPos = best.pos.clone();
      this.lastSeenTime = now;
      this.visPoint = bestVis;
      world.game?.spotted(best, ch);
    } else {
      this.targetVisible = false;
    }
  }

  // ---------------- aiming ----------------
  aimAt(point, dt, speedMul = 1) {
    const ch = this.ch;
    const eye = ch.eyePos(_e);
    const a = anglesFromDir(point.x - eye.x, point.y - eye.y, point.z - eye.z);
    return this.turnTo(a.yaw, a.pitch, dt, speedMul);
  }
  turnTo(yaw, pitch, dt, speedMul = 1) {
    const ch = this.ch;
    const dy = angleDiff(ch.yaw, yaw), dp = pitch - ch.pitch;
    const maxStep = this.d.turn * DEG * dt * speedMul;
    // ease-out near the target, capped by turn speed
    const sy = Math.sign(dy) * Math.min(Math.abs(dy), Math.max(maxStep * 0.15, Math.min(maxStep, Math.abs(dy) * Math.min(1, dt * 14))));
    const sp = Math.sign(dp) * Math.min(Math.abs(dp), Math.max(maxStep * 0.15, Math.min(maxStep, Math.abs(dp) * Math.min(1, dt * 14))));
    ch.yaw += sy; ch.pitch = clamp(ch.pitch + sp, -1.5, 1.5);
    return Math.hypot(angleDiff(ch.yaw, yaw), pitch - ch.pitch);
  }
  idleLook(dt) {
    if (this.look) this.aimAt(this.look, dt, 0.5);
  }

  // ---------------- combat ----------------
  combat(dt, input) {
    const ch = this.ch, t = this.target, now = world.time;
    const dist = t.pos.distanceTo(ch.pos);
    // pick a weapon
    if (['knife', 'grenade', 'c4', 'taser'].includes(ch.active) && !ch.pinPulled) {
      const s = ch.bestSlot();
      if (s !== 'knife' && s !== ch.active) ch.switchTo(s);
    }
    const def = ch.activeDef(), inst = ch.activeInst();
    if (inst && inst.clip === 0 && inst.reserve === 0) {
      const other = ch.active === 'primary' ? 'secondary' : 'primary';
      if (ch.inv[other] && (ch.inv[other].clip > 0 || ch.inv[other].reserve > 0)) ch.switchTo(other);
    }
    if (!this.targetVisible) {
      // lost sight – hold the angle where the enemy disappeared, then push carefully
      const p = this.lastSeenPos.clone(); p.y += 58;
      this.aimAt(p, dt, 0.7);
      if (now - this.lastSeenTime > 0.8 && world.mode !== 'dm' && Math.random() < 0.02) this.setGoal(this.lastSeenPos.clone(), 'chase');
      this.followPath(dt, input, true);
      input.walk = true;
      return;
    }
    this.reactLeft -= dt;
    // aim point
    const head = new THREE.Vector3(t.pos.x, t.pos.y + t.eyeHeight() + 2.5, t.pos.z);
    const chest = new THREE.Vector3(t.pos.x, t.pos.y + t.height * 0.66, t.pos.z);
    let aimP = this.aimHead ? head : chest;
    if (this.visPoint && this.visPoint.y < chest.y - 1 && !this.aimHead) aimP = chest;
    // lead slightly for moving targets
    aimP = aimP.clone().addScaledVector(t.vel, 0.05);
    const eye = ch.eyePos(_e);
    const a = anglesFromDir(aimP.x - eye.x, aimP.y - eye.y, aimP.z - eye.z);
    // error decays over time while tracking
    const decay = Math.exp(-dt * (2.2 + this.d.spray * 2));
    this.errP *= decay; this.errY *= decay;
    // recoil compensation (bullets land at view + punch*2)
    const comp = this.d.spray;
    const wantYaw = a.yaw + this.errY + ch.punchY * 2 * DEG * comp;
    const wantPitch = a.pitch + this.errP - ch.punchP * 2 * DEG * comp;
    const err = this.turnTo(wantYaw, wantPitch, dt, 1);

    // movement while fighting
    const sniper = def.cat === 'sniper';
    const stop = this.engageStop < this.d.stop || sniper;
    const shooting = this.burst > 0 || now < this.burstPause;
    if (stop) {
      // counter-strafe: stop moving to shoot accurately; strafe between bursts on harder difficulty
      if (now > this.burstPause && this.burst === 0 && this.d.stop > 0.8 && !sniper && dist > 300) {
        this.strafeT -= dt;
        if (this.strafeT <= 0) { this.strafeT = rand(0.25, 0.55); this.strafeDir *= -1; }
        input.side = this.strafeDir;
      }
      if (ch.speed2d() > 70 && this.burst === 0 && !shooting) { /* still slowing down */ }
      if (dist > 900 && this.d.stop > 0.5 && def.cat === 'rifle' && Math.random() < 0.004) this.crouchFight = !this.crouchFight;
      input.crouch = !!this.crouchFight && dist > 900;
    } else {
      this.strafeT -= dt;
      if (this.strafeT <= 0) { this.strafeT = rand(0.4, 0.9); this.strafeDir *= -1; }
      input.side = this.strafeDir;
      input.fwd = dist > 800 ? 0.5 : 0;
    }
    // snipers scope in
    if (def.zoom && dist > 350 && ch.zoom === 0 && now >= ch.nextAttack) input.attack2 = !this.zoomToggle, this.zoomToggle = !this.zoomToggle;
    if (inst && inst.clip === 0) { input.reload = true; return; }
    if (this.reactLeft > 0 || !inst) return;
    // fire decision
    const tol = Math.atan2(11, dist) * 1.4 + 0.004;
    const moving = ch.speed2d() > (def.speed || 250) * 0.36;
    if (err > tol * 1.8) { this.burst = 0; return; }
    if (stop && moving && dist > 250 && !(def.cat === 'smg' || def.cat === 'shotgun')) {
      // wait to slow down (counter-strafe)
      input.side = 0; input.fwd = 0;
      if (ch.speed2d() > (def.speed || 250) * 0.45) return;
    }
    if (def.cat === 'shotgun' && dist > 900) return;
    if (sniper && (ch.zoom === 0 && dist > 350)) return;
    // don't shoot teammates
    const dir = ch.viewDir(_f);
    const blocker = rayChars(eye.x, eye.y, eye.z, dir.x, dir.y, dir.z, dist, new Set([ch]));
    if (blocker && !isEnemy(ch, blocker.ch) && world.mode !== 'dm') return;
    if (now < this.burstPause) return;
    const auto = def.auto && !(inst.burstMode);
    if (!auto) {
      // semi-auto: toggle the trigger
      const interval = sniper ? 0 : clamp(dist / 4000, 0.12, 0.45);
      if (now - ch.lastShot > interval + 60 / (def.rpm || 400)) { input.attack = !this.tapToggle; this.tapToggle = !this.tapToggle; }
      return;
    }
    // automatic weapons: tap at long range, burst at mid, spray close
    const maxBurst = dist > 1600 ? 1 : dist > 900 ? 3 : dist > 500 ? 6 : 30;
    if (this.burst >= maxBurst) { this.burst = 0; this.burstPause = now + (dist > 1600 ? 0.35 : 0.28) * rand(0.8, 1.3); return; }
    input.attack = true;
    if (now - ch.lastShot < 0.02) this.burst++;
  }

  // ---------------- objectives / navigation ----------------
  setGoal(pos, key, look) {
    if (this.goalKey === key && this.goal && this.goal.distanceToSquared(pos) < 400) { if (look) this.look = look; return; }
    this.goal = pos.clone(); this.goalKey = key; this.look = look || null;
    this.path = null; this.arrived = false; this.repathAt = 0;
  }

  followPath(dt, input, noLook) {
    const ch = this.ch, now = world.time, map = world.map;
    if (!this.goal) return false;
    if (!this.path || now > this.repathAt) {
      this.path = map.findPath(ch.pos, this.goal);
      this.pathIdx = 0; this.repathAt = now + 4 + Math.random() * 2;
      if (!this.path) { this.goal = null; return false; }
    }
    if (this.pathIdx >= this.path.length) { this.arrived = true; return false; }
    const p = this.path[this.pathIdx];
    const dx = p.x - ch.pos.x, dz = p.z - ch.pos.z;
    const d = Math.hypot(dx, dz);
    if (d < (this.pathIdx === this.path.length - 1 ? 14 : 28)) { this.pathIdx++; if (this.pathIdx >= this.path.length) { this.arrived = true; return false; } return true; }
    const mx = dx / d, mz = dz / d;
    const fx = -Math.sin(ch.yaw), fz = -Math.cos(ch.yaw), rx = Math.cos(ch.yaw), rz = -Math.sin(ch.yaw);
    input.fwd = mx * fx + mz * fz;
    input.side = mx * rx + mz * rz;
    if (!noLook) {
      // look ahead along the path (slightly up) or at heard noises
      let lookP;
      if (this.heard && now - this.heard.time < 2.5) lookP = this.heard.pos.clone().setY(this.heard.pos.y + 55);
      else {
        const ahead = this.path[Math.min(this.pathIdx + 1, this.path.length - 1)];
        lookP = new THREE.Vector3(ahead.x, ch.pos.y + 60, ahead.z);
        if (Math.hypot(ahead.x - ch.pos.x, ahead.z - ch.pos.z) < 60 && this.look) lookP = this.look.clone().setY(this.look.y + 60);
      }
      this.aimAt(lookP, dt, 0.55);
    }
    // stuck detection
    if (now > this.stuckCheck) {
      if (this.stuckPos && this.stuckPos.distanceTo(ch.pos) < 10) { input.jump = true; this.path = null; this.repathAt = 0; ch.pos.x += (Math.random() - 0.5) * 4; }
      this.stuckPos = ch.pos.clone(); this.stuckCheck = now + 1.0;
    }
    return true;
  }

  plan() {
    const ch = this.ch, now = world.time, m = world.match, map = world.map;
    if (!m) return;
    if (world.mode === 'dm') {
      if (this.heard && now - this.heard.time < 3) this.setGoal(this.heard.pos, 'heard' + Math.round(this.heard.time));
      else if (!this.goal || this.arrived || now - (this.wanderT || 0) > 15) { this.setGoal(map.randomWalkPos(), 'wander' + now); this.wanderT = now; }
      return;
    }
    const bomb = world.bomb;
    const tp = world.game.teamPlan[ch.team];
    if (ch.team === 'T') {
      if (bomb.state === 'planted') {
        const spots = map.spots['post' + bomb.site];
        const sp = spots[(this.slot || 0) % spots.length];
        if (bomb.defuser) this.setGoal(bomb.pos, 'bombfight');
        else this.setGoal(sp.pos, 'post' + bomb.site, sp.look);
        return;
      }
      if (bomb.state === 'dropped') {
        const ts = world.chars.filter(c => c.alive && c.team === 'T' && c.isBot);
        ts.sort((a, b) => a.pos.distanceTo(bomb.pos) - b.pos.distanceTo(bomb.pos));
        if (ts[0] === ch) { this.setGoal(bomb.pos, 'getbomb'); if (!this.saidBomb) { say(ch, 'bomb', 0.5); this.saidBomb = true; } return; }
      }
      const execute = now >= tp.executeAt || m.timeLeft() < 40;
      const route = tp.routes[(this.slot || 0) % tp.routes.length];
      if (ch.inv.c4) {
        if (!execute) { const st = map.spots[route[0]][0]; this.setGoal(st.pos.clone().add(new THREE.Vector3(30, 0, 30)), 'stage'); }
        else { const ps = map.spots['plant' + tp.site][this.plantIdx ?? (this.plantIdx = Math.floor(Math.random() * 3))]; this.setGoal(ps.pos, 'plant'); }
        return;
      }
      if (!execute) {
        const st = map.spots[route[0]][0];
        const off = new THREE.Vector3(((this.slot || 0) % 3 - 1) * 50, 0, (((this.slot || 0) >> 1) % 2) * 50);
        this.setGoal(st.pos.clone().add(off), 'stage' + route[0], st.look);
      } else {
        const entry = map.spots[route[1]][0];
        if (!this.passedEntry && ch.pos.distanceTo(entry.pos) < 120) this.passedEntry = true;
        if (!this.passedEntry) this.setGoal(entry.pos, 'entry' + route[1], entry.look);
        else { const sp = map.spots['post' + tp.site][(this.slot || 0) % 4]; this.setGoal(sp.pos, 'site' + tp.site, sp.look); }
      }
      return;
    }
    // ---- CT ----
    if (bomb.state === 'planted') {
      this.setGoal(bomb.pos, 'retake');
      return;
    }
    const intel = world.game.intel.CT;
    let role = this.role || 'ctA';
    if (intel && now - intel.time < 12 && this.rotator && !role.endsWith(intel.site)) {
      if (this.rotatedTo !== intel.site) { this.rotatedTo = intel.site; say(ch, 'rotate', 0.4); }
      role = 'ct' + intel.site;
    }
    const spots = map.spots[role];
    const sp = spots[(this.slot || 0) % spots.length];
    this.setGoal(sp.pos, 'hold' + role + this.slot, sp.look);
  }

  objective(dt, input) {
    const ch = this.ch, now = world.time, bomb = world.bomb, map = world.map;
    // CT defuse
    if (world.mode !== 'dm' && ch.team === 'CT' && bomb && bomb.state === 'planted') {
      const d = bomb.pos.distanceTo(ch.pos);
      if (d < 55) {
        const needed = ch.kit ? 5 : 10;
        if (bomb.explodeAt - now > needed + 0.2 || this.defuseCommit) {
          this.defuseCommit = true;
          input.use = true; input.crouch = Math.random() < 0.5 || input.crouch;
          if (!this.saidDefuse) { say(ch, 'defuse', 0.6); this.saidDefuse = true; }
          this.aimAt(bomb.pos, dt, 0.5);
          return;
        }
      }
    }
    // T planting
    if (ch.inv.c4 && this.goalKey === 'plant') {
      const site = map.zoneAt(ch.pos.x, ch.pos.z);
      if (site && (this.arrived || ch.pos.distanceTo(this.goal) < 60)) {
        if (ch.active !== 'c4') ch.switchTo('c4');
        input.attack = true;
        this.aimAt(new THREE.Vector3(ch.pos.x - Math.sin(ch.yaw) * 40, ch.pos.y, ch.pos.z - Math.cos(ch.yaw) * 40), dt, 0.3);
        return;
      }
    }
    // pick up a better weapon nearby when unarmed
    if (!ch.inv.primary && world.dropped.length && !this.goalKey.startsWith('loot')) {
      for (const it of world.dropped) {
        if (it.kind === 'weapon' && it.inst.def.slot === 'primary' && it.pos.distanceTo(ch.pos) < 450 && it.rest) { this.setGoal(it.pos, 'loot' + it.born); break; }
      }
    }
    // utility
    this.useUtility(dt);
    if (ch.pinPulled || this.throwing) return;
    const moving = this.followPath(dt, input);
    if (!moving) {
      // holding: watch the assigned angle or noises
      if (this.heard && now - this.heard.time < 3) this.aimAt(this.heard.pos.clone().setY(this.heard.pos.y + 55), dt, 0.6);
      else if (this.look) this.aimAt(this.look.clone().setY(this.look.y + 58), dt, 0.4);
      // CT holds crouched sometimes at long angles
      input.crouch = this.holdCrouch;
      if (this.ch.team === 'CT' && Math.random() < 0.002) this.holdCrouch = !this.holdCrouch;
    }
    // walk silently when close to enemies on CT side or when Ts are lurking before execute
    const tp = world.game.teamPlan?.[ch.team];
    if (world.mode !== 'dm' && ch.team === 'T' && tp && now < tp.executeAt - 3 && this.goalKey.startsWith('stage')) {
      const st = this.goal; if (st && st.distanceTo(ch.pos) < 500) input.walk = true;
    }
    // avoid standing in fire
    if (fireAt(ch.pos.x, ch.pos.z)) { input.fwd = -1; input.walk = false; }
    // equip the best weapon when not doing anything else
    if (ch.active === 'knife' && ch.bestSlot() !== 'knife' && !this.knifeRun) ch.switchTo(ch.bestSlot());
    if (ch.active === 'c4' && this.goalKey !== 'plant') ch.switchTo(ch.bestSlot());
  }

  useUtility(dt) {
    const ch = this.ch, now = world.time, map = world.map;
    if (world.mode === 'dm' || Math.random() > this.d.util * 0.5) return;
    if (this.throwing) {
      // aim then throw
      const err = this.aimAt(this.throwing.aim, dt, 1.2);
      if (err < 0.08 || now - this.throwing.start > 1.2) {
        if (ch.active !== 'grenade' || ch.activeGrenade !== this.throwing.gid) ch.switchTo('grenade', this.throwing.gid);
        else if (now >= ch.deployUntil) { ch.doThrow(this.throwing.gid, 1, this.throwing.vel); this.throwing = null; }
      }
      if (this.throwing && now - this.throwing.start > 3) this.throwing = null;
      return;
    }
    const tp = world.game.teamPlan[ch.team];
    if (ch.team === 'T' && !this.utilDone && now >= tp.executeAt - 2 && world.bomb.state !== 'planted') {
      const center = map.siteCenter(tp.site);
      if (ch.pos.distanceTo(center) < 1300) {
        this.utilDone = true;
        const u = map.util[tp.site];
        const opts = [];
        if (ch.grenadeCount('smokegrenade')) opts.push(['smokegrenade', pick(u.smoke), 'smoke']);
        if (ch.grenadeCount('flashbang')) opts.push(['flashbang', pick(u.flash), 'flash']);
        if (ch.grenadeCount('molotov')) opts.push(['molotov', pick(u.molly), 'molly']);
        if (opts.length) {
          const [gid, cell, sayKey] = pick(opts);
          this.planThrow(gid, new THREE.Vector3(map.cx(cell[0]), map.floor[map.idx(cell[0], cell[1])] + 10, map.cz(cell[1])), sayKey);
        }
      }
    }
    if (ch.team === 'CT' && !this.ctUtilDone && this.heard && now - this.heard.time < 1 && this.heard.kind === 'step') {
      const d = this.heard.pos.distanceTo(ch.pos);
      if (d > 350 && d < 1300) {
        const gid = ch.grenadeCount('incgrenade') ? 'incgrenade' : ch.grenadeCount('hegrenade') ? 'hegrenade' : null;
        if (gid) { this.ctUtilDone = true; this.planThrow(gid, this.heard.pos.clone(), gid === 'incgrenade' ? 'molly' : null); }
      }
    }
  }

  planThrow(gid, target, sayKey) {
    const ch = this.ch;
    const eye = ch.eyePos(new THREE.Vector3());
    const high = gid === 'smokegrenade' || gid === 'flashbang' || Math.random() < 0.5;
    const vel = solveThrow(eye, target, high);
    if (!vel) return;
    const a = anglesFromDir(vel.x, vel.y, vel.z);
    const aim = eye.clone().add(dirFromAngles(a.yaw, Math.min(a.pitch, 1.2), new THREE.Vector3()).multiplyScalar(200));
    this.throwing = { gid, vel, aim, start: world.time };
    if (sayKey) say(ch, sayKey, 0.6);
  }

  weaponUpkeep(input) {
    const ch = this.ch;
    const inst = ch.activeInst();
    if (!this.targetVisible && inst && inst.def.clip && inst.clip < inst.def.clip * 0.4 && inst.reserve > 0 && !ch.reloading) {
      if (!this.target || world.time - this.lastSeenTime > 2) input.reload = true;
    }
    if (!this.targetVisible && ch.zoom > 0 && Math.random() < 0.02 && ch.activeDef().cat !== 'sniper') input.attack2 = true;
  }
}

// ---------------- round setup for bot teams ----------------
export function newRoundPlans() {
  const map = world.map;
  const plans = {};
  const site = Math.random() < 0.5 ? 'A' : 'B';
  const now = world.time;
  const routes = shuffle([...map.routes[site]]);
  plans.T = { site, routes, executeAt: now + world.match.freezeTime + rand(22, 50) };
  plans.CT = {};
  // assign slots / roles
  const ts = world.chars.filter(c => c.team === 'T' && c.isBot);
  shuffle(ts).forEach((c, i) => { if (c.ai) { c.ai.slot = i; c.ai.passedEntry = false; c.ai.utilDone = false; c.ai.saidBomb = false; } });
  const cts = shuffle(world.chars.filter(c => c.team === 'CT' && c.isBot));
  const roles = ['ctA', 'ctB', 'ctA', 'ctB', 'ctMid', 'ctShort', 'ctMid', 'ctB', 'ctA', 'ctMid'];
  cts.forEach((c, i) => { if (c.ai) { c.ai.role = roles[i]; c.ai.slot = i; c.ai.rotator = roles[i] === 'ctMid' || roles[i] === 'ctShort' || i >= 2; c.ai.rotatedTo = null; } });
  const tAlive = ts.filter(c => c.alive);
  if (tAlive.length && Math.random() < 0.6) setTimeout(() => world.hud?.chat(pick(tAlive), pick(SAY['go' + site])), (world.match.freezeTime + 2) * 1000);
  return plans;
}

// ---------------- buying ----------------
export function botBuy(ch, mode) {
  const g = world.game;
  const T = ch.team === 'T';
  const buy = (id) => g.tryBuy(ch, id, true);
  if (world.mode === 'dm') {
    const opts = Math.random() < 0.5 ? ['ak47', 'ak47', 'galil', 'awp', 'sg553', 'mac10', 'deagle'] : ['m4a4', 'm4a1s', 'famas', 'awp', 'aug', 'mp9', 'deagle'];
    const id = pick(opts);
    if (WEAPONS[id].slot === 'secondary') buy(id); else buy(id);
    buy('vesthelm');
    return;
  }
  const money = () => ch.money;
  const pistolRound = world.match.isPistolRound();
  if (pistolRound) {
    const r = Math.random();
    if (r < 0.45) buy('vest');
    else if (r < 0.7) { buy('p250'); buy('flashbang'); }
    else { buy('flashbang'); buy('smokegrenade'); }
    if (!T && Math.random() < 0.3) buy('defuser');
    return;
  }
  if (mode === 'eco') {
    if (money() > 2500 && Math.random() < 0.5) buy(Math.random() < 0.5 ? 'p250' : 'deagle');
    return;
  }
  const hasPrimary = !!ch.inv.primary;
  if (mode === 'force' && !hasPrimary) {
    const opts = T ? ['galil', 'mac10', 'ump45', 'mp7'] : ['famas', 'mp9', 'ump45', 'mp7'];
    for (const id of opts) { if (money() >= WEAPONS[id].price + 650) { buy(id); break; } }
    buy(money() >= 1000 ? 'vesthelm' : 'vest');
    if (money() >= 300) buy('flashbang');
    return;
  }
  // full buy
  if (!hasPrimary) {
    const awp = (ch.ai && ch.ai.slot === 0 && money() >= 6000 && Math.random() < 0.5);
    if (awp) buy('awp');
    else if (T) buy(money() >= 2700 ? 'ak47' : 'galil');
    else buy(money() >= 3100 ? (Math.random() < 0.5 ? 'm4a4' : 'm4a1s') : 'famas');
  }
  if (ch.armor < 70 || !ch.helmet) buy(money() >= 1000 ? 'vesthelm' : 'vest');
  if (!T) buy('defuser');
  const nades = T ? ['smokegrenade', 'flashbang', 'molotov', 'hegrenade', 'flashbang'] : ['smokegrenade', 'flashbang', 'incgrenade', 'hegrenade', 'flashbang'];
  for (const n of nades) if (money() >= 400 || (money() >= 200 && n === 'flashbang')) buy(n);
}
