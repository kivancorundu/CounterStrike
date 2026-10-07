// Bullets (with wall penetration), melee, taser, damage & armor model, kills.
import * as THREE from './vendor/three.module.min.js';
import { world, isEnemy, clamp } from './state.js';
import { fx } from './effects.js';
import { audio } from './audio.js';
import { MAT_SOUND } from './map.js';
import { WEAPONS } from './weapons.js';
import { pokeSmokes, segmentInSmoke } from './grenades.js';

const HIT_MULT = { head: 4, chest: 1, stomach: 1.25, legs: 0.75, generic: 1 };
const _o = new THREE.Vector3();

// ray vs character hit spheres. returns nearest {t, ch, g, x, y, z}
export function rayChars(ox, oy, oz, dx, dy, dz, maxT, exclude) {
  let best = null;
  for (const ch of world.chars) {
    if (!ch.alive || (exclude && exclude.has(ch))) continue;
    // broad phase against a vertical capsule around the character
    const cx = ch.pos.x - ox, cy = ch.pos.y + 36 - oy, cz = ch.pos.z - oz;
    const tc = cx * dx + cy * dy + cz * dz;
    if (tc < -50 || tc > maxT + 50) continue;
    const px = cx - dx * tc, py = cy - dy * tc, pz = cz - dz * tc;
    if (px * px + py * py * 0.3 + pz * pz > 60 * 60) continue;
    for (const s of ch.hitShapes()) {
      const lx = s.x - ox, ly = s.y - oy, lz = s.z - oz;
      const tca = lx * dx + ly * dy + lz * dz;
      if (tca < 0) continue;
      const d2 = lx * lx + ly * ly + lz * lz - tca * tca;
      const r2 = s.r * s.r;
      if (d2 > r2) continue;
      const t = tca - Math.sqrt(r2 - d2);
      if (t < 0 || t > maxT) continue;
      // prefer head when spheres overlap at nearly the same depth
      if (!best || t < best.t - (s.g === 'head' ? 2 : 0)) best = { t, ch, g: s.g, x: ox + dx * t, y: oy + dy * t, z: oz + dz * t };
    }
  }
  return best;
}

export function fireBullet(shooter, def, inst, origin, dir, primary) {
  const map = world.map;
  let ox = origin.x, oy = origin.y, oz = origin.z;
  const dx = dir.x, dy = dir.y, dz = dir.z;
  let dmg = def.dmg, pen = def.pen, traveled = 0, penetrated = 0;
  const range = def.range;
  const hitSet = new Set([shooter]);
  let end = null;
  const throughSmoke = segmentInSmoke(origin, dir, Math.min(range, 4000));
  for (let iter = 0; iter < 10; iter++) {
    const rem = range - traveled;
    if (rem <= 1) break;
    const mh = map.raycast(ox, oy, oz, dx, dy, dz, rem);
    const lim = mh ? mh.t : rem;
    const hit = rayChars(ox, oy, oz, dx, dy, dz, lim, hitSet);
    if (hit) {
      hitSet.add(hit.ch);
      const dist = traveled + hit.t;
      bulletHit(shooter, hit, def, dmg, dist, dir, { wallbang: penetrated > 0, smoke: throughSmoke, noscope: def.cat === 'sniper' && shooter.zoom === 0 && !shooter.resumeZoomAt, blind: shooter.blindAmount() > 0.5 });
      dmg *= 0.6; // bullet keeps going through the body with reduced power
      ox = hit.x + dx; oy = hit.y + dy; oz = hit.z + dz;
      traveled = dist + 1;
      continue;
    }
    if (!mh) { end = new THREE.Vector3(ox + dx * rem, oy + dy * rem, oz + dz * rem); break; }
    const ms = MAT_SOUND[mh.mat];
    fx.impact(mh, ms);
    end = new THREE.Vector3(mh.x, mh.y, mh.z);
    if (Math.random() < 0.5) {
      const snd = ms === 'wood' ? 'impactwood' : ms === 'metal' ? 'impactmetal' : 'impact';
      audio.play(snd, { pos: end, volume: 0.5, maxDist: 1500 });
    }
    if (pen <= 0) break;
    const thick = map.thicknessAt(mh.x, mh.y, mh.z, dx, dy, dz);
    if (!isFinite(thick)) break;
    const cost = thick * map.density(mh.mat);
    if (cost >= pen) break;
    dmg *= (1 - cost / pen) * 0.85;
    pen -= cost;
    penetrated++;
    const ex = mh.x + dx * thick, ey = mh.y + dy * thick, ez = mh.z + dz * thick;
    fx.decals.add(ex, ey, ez, -dx, -dy, -dz, 3);
    ox = ex + dx * 0.5; oy = ey + dy * 0.5; oz = ez + dz * 0.5;
    traveled += mh.t + thick;
    if (dmg < 1) break;
  }
  if (!end) end = new THREE.Vector3(ox + dx * 50, oy + dy * 50, oz + dz * 50);
  // tracers & smoke holes
  if (primary) {
    const start = _o.set(origin.x + dx * 30, origin.y + dy * 30 - 5, origin.z + dz * 30);
    if (shooter.isLocal) { start.x += Math.cos(shooter.yaw) * 5; start.z -= Math.sin(shooter.yaw) * 5; }
    if (!(shooter.isLocal && !world.spectating) || Math.random() < 0.4) {
      if (def.tracer && !(shooter.activeInst()?.silenced && shooter.isLocal)) fx.tracers.add(start.clone(), end, shooter.isLocal ? 0.6 : 1);
    }
    if (throughSmoke) pokeSmokes(origin, dir, origin.distanceTo(end));
  }
  return end;
}

function bulletHit(shooter, hit, def, dmg, dist, dir, flags) {
  const v = hit.ch;
  let d = dmg * Math.pow(def.rangeMod, dist / 500);
  const mult = hit.g === 'head' ? (def.hs || 4) : HIT_MULT[hit.g];
  d *= mult;
  const helmetHit = hit.g === 'head' && v.helmet && v.armor > 0;
  const real = damage(v, shooter, d, hit.g, def, { ...flags, headshot: hit.g === 'head' });
  fx.blood(new THREE.Vector3(hit.x, hit.y, hit.z), dir, hit.g === 'head');
  // hit sounds
  const local = shooter.isLocal || v.isLocal;
  if (hit.g === 'head') audio.play(helmetHit ? 'helmet' : 'headshot', local ? { volume: 0.7 } : { pos: v.pos, maxDist: 1600 });
  else audio.play('flesh', local ? { volume: 0.55 } : { pos: v.pos, maxDist: 1200 });
  // tagging (slowdown when hit)
  v.velMod = Math.min(v.velMod, hit.g === 'legs' ? 0.55 : 0.45);
  v.viewKick += hit.g === 'head' ? 3 : 1.2;
  return real;
}

// generic damage entry point (bullets, grenades, fire, bomb, fall)
export function damage(victim, attacker, raw, group, def, flags = {}) {
  if (!victim.alive || raw <= 0) return 0;
  const now = world.time;
  if (victim.protectUntil > now) return 0;
  const m = world.match;
  if (attacker && attacker !== victim && !isEnemy(attacker, victim)) {
    if (!m || !m.friendlyFire) return 0;
    raw *= def && def.cat === 'grenade' ? 0.85 : 0.33;
  }
  let dmg = raw;
  const protectedArea = group === 'head' ? victim.helmet : (group === 'chest' || group === 'stomach' || group === 'generic');
  if (protectedArea && victim.armor > 0 && def && def.ap !== undefined) {
    let newDmg = dmg * def.ap;
    let armorDmg = (dmg - newDmg) * 0.5;
    if (armorDmg > victim.armor) { armorDmg = victim.armor; newDmg = dmg - armorDmg / 0.5; }
    victim.armor = Math.max(0, Math.round(victim.armor - armorDmg));
    dmg = newDmg;
  }
  dmg = Math.max(1, Math.floor(dmg));
  const real = Math.min(dmg, victim.health);
  victim.health -= dmg;
  victim.lastDamageTime = now;
  if (attacker && attacker !== victim) {
    victim.lastAttacker = attacker;
    const rec = victim.round.dmgTaken.get(attacker.id) || { dmg: 0, hits: 0, ch: attacker };
    rec.dmg += real; rec.hits++; victim.round.dmgTaken.set(attacker.id, rec);
    const rec2 = attacker.round.dmgDealt.get(victim.id) || { dmg: 0, hits: 0, ch: victim };
    rec2.dmg += real; rec2.hits++; attacker.round.dmgDealt.set(victim.id, rec2);
    if (isEnemy(attacker, victim)) { attacker.stats.damage += real; attacker.round.damage += real; }
  }
  if (victim.isLocal) world.hud?.hurt(attacker, real);
  victim.ai?.onDamaged(attacker);
  if (victim.health <= 0) {
    victim.health = 0;
    kill(victim, attacker, def, flags);
  }
  return real;
}

export function kill(victim, attacker, def, flags) {
  victim.die(attacker);
  victim.stats.deaths++;
  const m = world.match;
  if (attacker && attacker !== victim) {
    if (isEnemy(attacker, victim)) {
      attacker.stats.kills++; attacker.round.kills++; attacker.stats.score += 2;
      if (flags.headshot) attacker.stats.hs++;
      if (m && m.economy && def) attacker.addMoney(Math.round((def.reward ?? 300) * (m.rewardMul || 1)), 'kill');
    } else {
      attacker.stats.kills--; attacker.stats.score -= 2;
      if (m && m.economy) attacker.addMoney(-300, 'teamkill');
    }
  } else {
    victim.stats.score -= 1;
  }
  // assists (>= 41 damage)
  for (const [, rec] of victim.round.dmgTaken) {
    const a = rec.ch;
    if (a === attacker || a === victim || !isEnemy(a, victim)) continue;
    if (rec.dmg >= 41) { a.stats.assists++; a.stats.score += 1; }
  }
  audio.play('death', victim.isLocal ? { volume: 0.4 } : { pos: victim.pos, volume: 0.5, maxDist: 1200 });
  world.game?.onKill(victim, attacker, def, flags);
}

export function meleeAttack(ch, heavy) {
  const eye = ch.eyePos(new THREE.Vector3());
  const range = heavy ? 48 : 64;
  let hit = null;
  const map = world.map;
  for (const off of [0, -0.12, 0.12, -0.25, 0.25]) {
    const dir = new THREE.Vector3();
    const yaw = ch.yaw + off, p = ch.pitch;
    dir.set(-Math.sin(yaw) * Math.cos(p), Math.sin(p), -Math.cos(yaw) * Math.cos(p));
    const mh = map.raycast(eye.x, eye.y, eye.z, dir.x, dir.y, dir.z, range);
    const h = rayChars(eye.x, eye.y, eye.z, dir.x, dir.y, dir.z, mh ? mh.t : range, new Set([ch]));
    if (h) { hit = h; hit.dir = dir; break; }
    if (off === 0 && mh) {
      fx.impact(mh, MAT_SOUND[mh.mat]);
      audio.play('knifehit', ch.isLocal ? { volume: 0.5 } : { pos: eye, maxDist: 800 });
    }
  }
  const knife = WEAPONS.knife;
  if (!hit) {
    audio.play('knife', ch.isLocal ? { volume: 0.6 } : { pos: eye, maxDist: 700 });
    return false;
  }
  const v = hit.ch;
  const vf = new THREE.Vector3(-Math.sin(v.yaw), 0, -Math.cos(v.yaw));
  const to = new THREE.Vector3(v.pos.x - ch.pos.x, 0, v.pos.z - ch.pos.z).normalize();
  const back = vf.dot(to) > 0.475;
  const combo = world.time - (ch.lastKnifeHit || -10) < 1.2;
  const dmg = heavy ? (back ? 180 : 65) : (back ? 90 : (combo ? 25 : 40));
  ch.lastKnifeHit = world.time;
  damage(v, ch, dmg, 'generic', knife, { backstab: back });
  fx.blood(new THREE.Vector3(hit.x, hit.y, hit.z), hit.dir, false);
  audio.play('knifehit', ch.isLocal || v.isLocal ? { volume: 0.8 } : { pos: v.pos, maxDist: 900 });
  return true;
}

export function taserShot(ch) {
  const eye = ch.eyePos(new THREE.Vector3());
  const dir = ch.viewDir(new THREE.Vector3());
  const def = WEAPONS.taser;
  const mh = world.map.raycast(eye.x, eye.y, eye.z, dir.x, dir.y, dir.z, def.range);
  const h = rayChars(eye.x, eye.y, eye.z, dir.x, dir.y, dir.z, mh ? mh.t : def.range, new Set([ch]));
  const endT = h ? h.t : (mh ? mh.t : def.range);
  for (let t = 20; t < endT; t += 24) fx.spark(new THREE.Vector3(eye.x + dir.x * t, eye.y + dir.y * t - 4, eye.z + dir.z * t));
  if (h) damage(h.ch, ch, 500, 'generic', def, {});
}

export function losToChar(from, ch) {
  // checks head and chest visibility, considering map and smoke
  const map = world.map;
  const head = new THREE.Vector3(ch.pos.x, ch.pos.y + ch.eyeHeight() + 2, ch.pos.z);
  const chest = new THREE.Vector3(ch.pos.x, ch.pos.y + ch.height * 0.62, ch.pos.z);
  let vis = null;
  for (const p of [head, chest]) {
    if (map.los(from, p)) {
      const d = p.clone().sub(from);
      const len = d.length();
      d.divideScalar(len);
      if (!segmentInSmoke(from, d, len, true)) { vis = p; break; }
    }
  }
  return vis;
}

export { clamp };
