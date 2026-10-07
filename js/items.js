// Dropped weapons / C4 lying in the world, pick-up logic.
import * as THREE from './vendor/three.module.min.js';
import { world, GRAVITY } from './state.js';
import { WEAPONS, GRENADES } from './weapons.js';
import { buildGun } from './models.js';
import { audio } from './audio.js';

// item: { kind:'weapon'|'c4'|'grenade', inst?, gid?, pos, vel, mesh, rest, noPickupFor, noPickupChar }
export function spawnItem(kind, data, pos, vel, owner) {
  const def = kind === 'c4' ? WEAPONS.c4 : kind === 'grenade' ? GRENADES[data] : data.def;
  const built = buildGun(def, { silenced: data && data.silenced });
  const mesh = built.group;
  mesh.position.copy(pos);
  mesh.rotation.y = Math.random() * Math.PI * 2;
  world.scene.add(mesh);
  const it = { kind, inst: kind === 'weapon' ? data : null, gid: kind === 'grenade' ? data : null, pos: pos.clone(), vel: vel.clone(), mesh, rest: false, noPickupUntil: world.time + 1.0, owner, def, born: world.time };
  if (kind === 'c4') { it.led = mesh.userData.led; }
  world.dropped.push(it);
  if (world.dropped.length > 40) removeItem(world.dropped.find(i => i.kind !== 'c4'));
  return it;
}

export function removeItem(it) {
  if (!it) return;
  world.scene.remove(it.mesh);
  const k = world.dropped.indexOf(it);
  if (k >= 0) world.dropped.splice(k, 1);
}

export function clearItems() {
  for (const it of [...world.dropped]) removeItem(it);
}

export function updateItems(dt) {
  const map = world.map;
  for (const it of world.dropped) {
    if (!it.rest) {
      it.vel.y -= GRAVITY * dt;
      const nx = it.pos.x + it.vel.x * dt, nz = it.pos.z + it.vel.z * dt;
      if (map.solidAt(nx, it.pos.y + 2, it.pos.z)) it.vel.x *= -0.3; else it.pos.x = nx;
      if (map.solidAt(it.pos.x, it.pos.y + 2, nz)) it.vel.z *= -0.3; else it.pos.z = nz;
      it.pos.y += it.vel.y * dt;
      const fl = map.floorAt(it.pos.x, it.pos.z);
      if (it.pos.y <= fl + 1.5) {
        it.pos.y = fl + 1.5;
        if (Math.abs(it.vel.y) < 60) { it.rest = true; it.vel.set(0, 0, 0); }
        else { it.vel.y *= -0.25; it.vel.x *= 0.5; it.vel.z *= 0.5; }
      }
      it.mesh.position.copy(it.pos);
      it.mesh.rotation.z = it.rest ? Math.PI / 2 : it.mesh.rotation.z + dt * 6;
    }
    if (it.led) it.led.visible = Math.sin(world.time * 6) > 0;
  }
}

// automatic pickup when walking over an item (if the slot is empty)
export function autoPickup(ch) {
  if (!ch.alive) return;
  for (const it of world.dropped) {
    if (world.time < it.noPickupUntil && it.owner === ch) continue;
    const dx = it.pos.x - ch.pos.x, dz = it.pos.z - ch.pos.z, dy = it.pos.y - ch.pos.y;
    if (dx * dx + dz * dz > 32 * 32 || dy > 50 || dy < -20) continue;
    if (it.kind === 'c4') {
      if (ch.team === 'T' && world.mode !== 'dm') { ch.inv.c4 = true; removeItem(it); world.game?.onBombPickup(ch); if (ch.isLocal) audio.play('pickup'); return; }
      continue;
    }
    if (it.kind === 'grenade') {
      if (ch.canTakeGrenade(it.gid)) { ch.inv.grenades.push(it.gid); removeItem(it); if (ch.isLocal) audio.play('pickup'); return; }
      continue;
    }
    const slot = it.inst.def.slot;
    if (slot === 'primary' || slot === 'secondary') {
      if (!ch.inv[slot]) {
        ch.inv[slot] = it.inst;
        removeItem(it);
        if (ch.isLocal) audio.play('pickup');
        if (ch.active === 'knife' || (ch.active === 'secondary' && slot === 'primary')) ch.switchTo(slot);
        return;
      }
    }
  }
}

// E: swap with the item the character is looking at
export function usePickup(ch) {
  let best = null, bestD = 110;
  const eye = ch.eyePos(new THREE.Vector3());
  const dir = ch.viewDir(new THREE.Vector3());
  for (const it of world.dropped) {
    if (it.kind === 'c4') continue;
    const v = it.pos.clone().sub(eye);
    const d = v.length();
    if (d > bestD) continue;
    v.normalize();
    if (v.dot(dir) < 0.8) continue;
    best = it; bestD = d;
  }
  if (!best) return false;
  if (best.kind === 'grenade') {
    if (!ch.canTakeGrenade(best.gid)) return false;
    ch.inv.grenades.push(best.gid); removeItem(best);
  } else {
    const slot = best.inst.def.slot;
    if (ch.inv[slot]) ch.dropSlot(slot);
    ch.inv[slot] = best.inst;
    removeItem(best);
    ch.switchTo(slot);
  }
  if (ch.isLocal) audio.play('pickup');
  return true;
}
