// Local player: input, first-person camera, viewmodel animation, spectating.
import * as THREE from './vendor/three.module.min.js';
import { world, DEG, clamp, lerp } from './state.js';
import { settings } from './settings.js';
import { audio } from './audio.js';
import { fx } from './effects.js';
import { buildGun, buildHands } from './models.js';
import { usePickup } from './items.js';

const keys = new Set();
const mouse = [false, false, false];
let mdx = 0, mdy = 0;
let swayX = 0, swayY = 0;
const BASE_VFOV = 73.74; // CS: 90° horizontal at 4:3

export function hfov43ToV(h) { return 2 * Math.atan(Math.tan(h * DEG / 2) * 0.75) / DEG; }

export const player = {
  locked: false,
  wantLock: false,
  intentionalUnlock: false,
  vm: null,
  vmKey: '',
  muzzleFlash: null,
  bobPhase: 0,
  specIdx: 0,

  init(canvas) {
    this.canvas = canvas;
    document.addEventListener('keydown', e => this.onKey(e, true));
    document.addEventListener('keyup', e => this.onKey(e, false));
    document.addEventListener('mousedown', e => {
      if (!world.running || world.inMenu) return;
      if (!this.locked) {
        if (world.ui?.overlayOpen()) return;
        this.lock();
        return;
      }
      mouse[e.button] = true;
      if (e.button === 0) this.onPrimaryClick();
      if (e.button === 2) this.onSecondaryClick();
    });
    document.addEventListener('mouseup', e => { mouse[e.button] = false; });
    document.addEventListener('contextmenu', e => { if (world.running) e.preventDefault(); });
    document.addEventListener('mousemove', e => {
      if (!this.locked) return;
      mdx += e.movementX; mdy += e.movementY;
    });
    document.addEventListener('wheel', e => {
      if (!this.locked || !world.local) return;
      const ch = world.local;
      if (!ch.alive) return;
      ch.cycle(e.deltaY > 0 ? 1 : -1);
    }, { passive: true });
    document.addEventListener('pointerlockchange', () => {
      this.locked = document.pointerLockElement === this.canvas;
      if (!this.locked) {
        keys.clear(); mouse[0] = mouse[1] = mouse[2] = false;
        if (world.running && !this.intentionalUnlock && !world.inMenu) world.ui?.openPause();
        this.intentionalUnlock = false;
      }
      world.ui?.updateClickToPlay();
    });
    window.addEventListener('blur', () => { keys.clear(); mouse[0] = mouse[1] = mouse[2] = false; });
  },

  lock() {
    try { const p = this.canvas.requestPointerLock({ unadjustedMovement: true }); if (p && p.catch) p.catch(() => this.canvas.requestPointerLock()); }
    catch (e) { this.canvas.requestPointerLock(); }
  },
  unlock(intentional = true) {
    this.intentionalUnlock = intentional;
    if (document.pointerLockElement) document.exitPointerLock();
  },

  onKey(e, down) {
    if (!world.running || world.inMenu) return;
    if (e.target && e.target.tagName === 'INPUT') return;
    const code = e.code;
    if (['Tab', 'Space', 'KeyB', 'Digit1', 'Digit2', 'Digit3', 'Digit4', 'Digit5', 'Digit6', 'KeyQ', 'KeyG', 'KeyE', 'KeyF', 'KeyR'].includes(code) || code.startsWith('Arrow')) e.preventDefault();
    if (code === 'Tab') { world.hud?.showScoreboard(down); return; }
    const buyOpen = world.ui?.buyOpen();
    if (down) {
      if (e.repeat) { keys.add(code); return; }
      keys.add(code);
      if (buyOpen && (code.startsWith('Digit') || code === 'Escape' || code === 'KeyB')) { world.ui.buyKey(code); return; }
      if (code === 'KeyB') { world.ui?.toggleBuy(); return; }
      if (code === 'Escape') { return; }
      const ch = world.local;
      if (!ch || !ch.alive || !this.locked && !buyOpen) return;
      switch (code) {
        case 'Digit1': ch.switchTo('primary'); break;
        case 'Digit2': ch.switchTo('secondary'); break;
        case 'Digit3': ch.switchTo('knife'); break;
        case 'Digit4': ch.switchTo('grenade'); break;
        case 'Digit5': ch.switchTo('c4'); break;
        case 'KeyQ': ch.quickSwitch(); break;
        case 'KeyG': ch.dropActive(); break;
        case 'KeyE': {
          const b = world.bomb;
          if (!(b && b.state === 'planted' && ch.team === 'CT' && b.pos.distanceTo(ch.pos) < 90)) usePickup(ch);
          break;
        }
      }
      world.hud?.flashInventory();
    } else {
      keys.delete(code);
    }
  },

  onPrimaryClick() {
    if (!world.local?.alive) this.cycleSpectate(1);
  },
  onSecondaryClick() {
    if (!world.local?.alive) this.cycleSpectate(-1);
  },

  cycleSpectate(dirn) {
    const list = world.chars.filter(c => c.alive && c !== world.local && (world.mode === 'dm' || c.team === world.local.team || !world.chars.some(t => t.alive && t.team === world.local.team)));
    if (!list.length) { world.spectating = null; return; }
    let k = list.indexOf(world.spectating);
    k = (k + dirn + list.length) % list.length;
    world.spectating = list[k];
  },

  buildInput() {
    const k = (c) => keys.has(c);
    const allowMove = this.locked || world.ui?.buyOpen();
    const inp = {
      fwd: allowMove ? (k('KeyW') ? 1 : 0) - (k('KeyS') ? 1 : 0) : 0,
      side: allowMove ? (k('KeyD') ? 1 : 0) - (k('KeyA') ? 1 : 0) : 0,
      jump: allowMove && k('Space'),
      crouch: allowMove && (k('ControlLeft') || k('ControlRight') || k('KeyC')),
      walk: allowMove && (k('ShiftLeft') || k('ShiftRight')),
      attack: this.locked && mouse[0],
      attack2: this.locked && mouse[2],
      reload: allowMove && k('KeyR'),
      use: allowMove && k('KeyE'),
      inspect: allowMove && k('KeyF'),
    };
    return inp;
  },

  // mouse look – called every frame before simulation
  applyLook() {
    const ch = world.local;
    if (!ch) { mdx = mdy = 0; return; }
    let sens = settings.sens * 0.022 * DEG;
    if (ch.alive && ch.zoom > 0) {
      const def = ch.activeDef();
      const z = def.zoom ? def.zoom[ch.zoom - 1] : 90;
      sens *= (z / 90) * settings.zoomSensRatio;
    }
    if (ch.alive) {
      ch.yaw -= mdx * sens;
      ch.pitch -= mdy * sens * (settings.invertY ? -1 : 1);
      ch.pitch = clamp(ch.pitch, -89 * DEG, 89 * DEG);
    }
    swayX = clamp(swayX + mdx * 0.0025, -1.2, 1.2);
    swayY = clamp(swayY + mdy * 0.0025, -1.2, 1.2);
    mdx = mdy = 0;
  },

  // which character the camera follows
  viewChar() {
    const ch = world.local;
    if (!ch) return null;
    if (ch.alive) { world.spectating = null; return ch; }
    if (world.spectating && !world.spectating.alive) world.spectating = null;
    if (!world.spectating && world.time - (ch.diedAt || 0) > 2.5) this.cycleSpectate(1);
    return world.spectating || ch;
  },

  updateCamera(dt) {
    const cam = world.camera;
    const v = this.viewChar();
    if (!v) return;
    for (const c of world.chars) if (c.model) c.model.visible = !(c === v && c.alive);
    const eye = v.eyePos(new THREE.Vector3());
    let yaw = v.yaw, pitch = v.pitch;
    if (!v.alive) {
      // dead local player with nobody to watch: slowly rising death cam
      const t = Math.min(1, (world.time - (v.diedAt || 0)) / 1.5);
      eye.y = v.pos.y + lerp(64, 110, t);
      pitch = lerp(v.pitch, -0.6, t);
    }
    const shake = fx.shakeAmt;
    cam.position.copy(eye);
    if (shake > 0) cam.position.add(new THREE.Vector3((Math.random() - 0.5) * shake * 2, (Math.random() - 0.5) * shake * 2, 0));
    cam.rotation.order = 'YXZ';
    cam.rotation.y = yaw - v.punchY * DEG * 0.9;
    cam.rotation.x = pitch + v.punchP * DEG * 0.9 + v.viewKick * 0.25 * DEG;
    cam.rotation.z = 0;
    // fov / zoom
    let fov = BASE_VFOV;
    const def = v.alive ? v.activeDef() : null;
    if (def && def.zoom && v.zoom > 0) fov = hfov43ToV(def.zoom[v.zoom - 1]);
    if (Math.abs(cam.fov - fov) > 0.01) { cam.fov = lerp(cam.fov, fov, Math.min(1, dt * 30)); if (Math.abs(cam.fov - fov) < 0.3) cam.fov = fov; cam.updateProjectionMatrix(); }
    const fwd = new THREE.Vector3(); cam.getWorldDirection(fwd);
    audio.setListener(eye, fwd);
    this.updateViewmodel(v, dt);
  },

  // ---------------- viewmodel ----------------
  updateViewmodel(ch, dt) {
    const vmScene = world.vmScene;
    const now = world.time;
    if (!ch.alive) { if (this.vm) this.vm.root.visible = false; return; }
    const def = ch.activeDef();
    const inst = ch.activeInst();
    const team = world.mode === 'dm' ? (ch.isLocal ? 'CT' : 'T') : ch.team;
    const key = def.id + '|' + (inst && inst.silenced ? 's' : '') + '|' + team + '|' + ch.id;
    if (key !== this.vmKey) {
      if (this.vm) vmScene.remove(this.vm.root);
      const root = new THREE.Group();
      const gun = buildGun(def, { silenced: inst && inst.silenced });
      const hands = buildHands(team);
      const holder = new THREE.Group();
      holder.add(gun.group);
      const r = hands.right; r.position.set(0.2, -1.4, 1.2); gun.group.add(r);
      const l = hands.left; l.position.copy(gun.left); l.position.x -= 0.6;
      if (def.cat === 'knife' || def.cat === 'grenade' || def.cat === 'taser' || def.model === 'deagle' || def.model === 'pistol' || def.model === 'dualies') {
        l.position.set(-2.4, -2.6, 3); l.rotation.set(0.2, 0.5, 0.3);
      }
      if (def.cat === 'knife' || def.cat === 'grenade') l.visible = false;
      gun.group.add(l);
      if (def.cat !== 'knife' && def.cat !== 'grenade' && def.cat !== 'c4') gun.group.scale.setScalar(def.cat === 'pistol' || def.cat === 'taser' ? 0.9 : 0.82);
      root.add(holder);
      // muzzle flash sprite
      const mf = new THREE.Mesh(new THREE.PlaneGeometry(9, 9), new THREE.MeshBasicMaterial({ map: fx.soft, color: 0xffb060, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
      mf.position.copy(gun.muzzle); mf.visible = false;
      gun.group.add(mf);
      const mf2 = mf.clone(); mf2.rotation.y = Math.PI / 2; mf2.scale.set(1.6, 0.6, 1); gun.group.add(mf2);
      root.traverse(o => { if (o.isMesh) o.frustumCulled = false; });
      vmScene.add(root);
      this.vm = { root, holder, gun, mf, mf2, def, base: vmBase(def) };
      this.vmKey = key;
      this.lastFire = -10;
    }
    const vm = this.vm;
    const base = vm.base;
    const scoped = ch.zoom > 0 && def.scopeType === 'sniper';
    vm.root.visible = !scoped && !(def.cat === 'grenade' && now - ch.throwAnim < 0.35 && !ch.pinPulled && !ch.inv.grenades.includes(ch.activeGrenade));
    // compose offsets
    let px = base.x, py = base.y, pz = base.z, rx = 0, ry = 0, rz = 0;
    // deploy
    const dt0 = now - (ch.deployUntil - (def.deploy || 0.8));
    const dk = clamp(dt0 / Math.min(0.5, def.deploy || 0.8), 0, 1);
    const de = 1 - Math.pow(1 - dk, 3);
    py -= (1 - de) * 10; rx -= (1 - de) * 0.8;
    // bob
    const hs = ch.speed2d();
    const amt = clamp(hs / 250, 0, 1) * (ch.onGround ? 1 : 0.2) * (settings.vmBob ? 1 : 0.2);
    this.bobPhase += hs * dt * 0.035;
    px += Math.sin(this.bobPhase) * 0.5 * amt;
    py += -Math.abs(Math.cos(this.bobPhase)) * 0.45 * amt;
    // breathing idle
    py += Math.sin(now * 1.6) * 0.08;
    // mouse sway
    swayX *= Math.exp(-dt * 8); swayY *= Math.exp(-dt * 8);
    if (ch.isLocal) { px -= swayX * 0.8; py += swayY * 0.6; ry += swayX * 0.04; }
    // jump / land
    if (!ch.onGround) py += clamp(ch.vel.y / 300, -1, 1) * -0.8;
    // crouch tilt
    rz += ch.duck * 0.04;
    // fire kick
    const sf = now - ch.fireAnim;
    if (sf < 0.12 && def.cat !== 'knife') {
      const k = 1 - sf / 0.12;
      const pistol = def.cat === 'pistol';
      pz += k * (pistol ? 1.4 : 1.8) * (def.cat === 'sniper' ? 2 : 1);
      rx += k * (pistol ? 0.16 : 0.05) * (def.cat === 'sniper' || def.cat === 'shotgun' ? 2.5 : 1);
      if (sf !== this.lastFire && ch.fireAnim !== this.lastFireAnim) {
        this.lastFireAnim = ch.fireAnim;
        if (!(inst && inst.silenced) && def.cat !== 'taser') {
          vm.mf.visible = vm.mf2.visible = true;
          vm.mf.rotation.z = Math.random() * 6.28;
          const s = 0.8 + Math.random() * 0.5; vm.mf.scale.set(s, s, 1);
          const eye = ch.eyePos(new THREE.Vector3());
          const fwd = ch.viewDir(new THREE.Vector3());
          fx.flashLight(eye.addScaledVector(fwd, 40), 2.2, 0.06);
        }
      }
    }
    if (sf > 0.035) vm.mf.visible = vm.mf2.visible = false;
    // reload
    if (ch.reloading && inst) {
      const total = def.shellReload ? def.reload : def.reload;
      const p = def.shellReload ? clamp((now - (ch.reloadUntil - total)) / total, 0, 1) : clamp((now - ch.reloadStart) / def.reload, 0, 1);
      const s = Math.sin(p * Math.PI);
      if (def.shellReload) { rx -= 0.25; rz += 0.3; py -= 1.2 - s * 0.5; }
      else {
        rx -= s * 0.45; rz += s * 0.55; py -= s * 2.5; px -= s * 1.0;
        if (vm.gun.mag) vm.gun.mag.position.y = (vm.gun.mag.userData.y0 ??= vm.gun.mag.position.y) - (p > 0.2 && p < 0.65 ? 8 : 0);
      }
    } else if (vm.gun.mag && vm.gun.mag.userData.y0 !== undefined) vm.gun.mag.position.y = vm.gun.mag.userData.y0;
    // silencer
    if (now < ch.silencerUntil) { const p = 1 - (ch.silencerUntil - now) / 1.6; const s = Math.sin(p * Math.PI); ry -= s * 0.7; rz -= s * 0.4; px -= s * 1.5; }
    // inspect
    if (now < ch.inspectUntil) {
      const p = 1 - (ch.inspectUntil - now) / 3.2;
      const s = Math.sin(Math.min(1, p * 1.4) * Math.PI);
      const s2 = Math.sin(clamp((p - 0.45) * 1.8, 0, 1) * Math.PI);
      ry += s * 0.9 - s2 * 0.6; rz += s * 0.7 + s2 * 0.5; rx += s2 * 0.4; px -= s * 2.5; py += s * 1.5;
    }
    // knife swing
    if (def.cat === 'knife' && ch.knifeAnim) {
      const t = (now - ch.knifeAnim.t) / (ch.knifeAnim.heavy ? 0.6 : 0.35);
      if (t < 1) {
        const s = Math.sin(t * Math.PI);
        if (ch.knifeAnim.heavy) { pz -= s * 7; rx -= s * 0.5; py += s * 2; }
        else { ry += Math.sin(t * Math.PI * 2 - 0.5) * 0.9; rz += s * 0.8; px -= s * 5; pz -= s * 3; }
      }
    }
    // grenade pin / throw
    if (def.cat === 'grenade') {
      if (ch.pinPulled) { pz += 2.5; py += 1.5; rx -= 0.5; px += 1; }
      const t = (now - ch.throwAnim) / 0.3;
      if (t < 1) { pz -= Math.sin(t * Math.PI) * 10; py += Math.sin(t * Math.PI) * 6; rx += t * 1.2; }
    }
    // c4 planting
    if (def.cat === 'c4') { py -= 1; rx -= 0.2; if (ch.planting) { rx -= 0.9; py -= 4; pz += 1; } }
    // defusing hides weapon down
    if (ch.defusing) { py -= 12; rx -= 1; }
    vm.holder.position.set(px, py, pz);
    vm.holder.rotation.set(rx, ry + base.ry, rz + base.rz);
    // vm camera fov
    const vf = hfov43ToV(settings.vmFov);
    if (world.vmCamera.fov !== vf) { world.vmCamera.fov = vf; world.vmCamera.updateProjectionMatrix(); }
  },
};

function vmBase(def) {
  switch (def.cat) {
    case 'pistol': return { x: 5.6, y: -6.2, z: -13, ry: 0.06, rz: 0 };
    case 'knife': return { x: 6.5, y: -6.5, z: -11, ry: 0.35, rz: -0.15 };
    case 'grenade': return { x: 6, y: -7, z: -12, ry: 0.15, rz: 0 };
    case 'c4': return { x: 2, y: -8, z: -13, ry: 0.4, rz: 0 };
    case 'taser': return { x: 5.6, y: -6, z: -12, ry: 0.06, rz: 0 };
    case 'sniper': return { x: 6.4, y: -7.4, z: -16, ry: 0.05, rz: 0 };
    case 'mg': return { x: 6.5, y: -8, z: -16, ry: 0.05, rz: 0 };
    case 'shotgun': return { x: 6.2, y: -7.4, z: -15, ry: 0.05, rz: 0 };
    default: return { x: 6.2, y: -7.2, z: -15, ry: 0.05, rz: 0 };
  }
}

export function keyDown(code) { return keys.has(code); }
