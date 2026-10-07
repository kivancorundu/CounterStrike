// Weapon definitions – values follow CS2 (prices, damage, armor penetration, fire rate,
// magazine sizes, movement speeds, kill rewards). Inaccuracy is expressed in milliradians.

function W(o) {
  return Object.assign({
    slot: 'primary', team: null, hs: 4, range: 8192, rangeMod: 0.98, pellets: 1, auto: true,
    speed: 220, scopedSpeed: 0, reward: 300, pen: 150, deploy: 1.0, zoom: null, silencer: false,
    burst: null, shellReload: false, tracer: true, sound: 'rifle', model: 'rifle',
    inacc: { stand: 5, crouch: 3.6, move: 140, jump: 250, land: 60, fire: 8, recover: 0.4, spread: 0.6 },
    recoil: { climb: 0.25, climbShots: 9, sway: 0.8, period: 7, kick: 1.0, jit: 0.08 },
  }, o);
}

export const WEAPONS = {
  // ---------------- PISTOLS ----------------
  glock: W({ id: 'glock', name: 'Glock-18', slot: 'secondary', cat: 'pistol', team: 'T', price: 200, dmg: 30, ap: 0.47, rangeMod: 0.85, rpm: 400, auto: false, clip: 20, reserve: 120, reload: 2.27, speed: 240, reward: 300, pen: 80, deploy: 0.9, burst: { shots: 3, interval: 0.05, rpmMode: 120 }, sound: 'pistol', model: 'pistol', color: 0x2b2b2b,
    inacc: { stand: 5.6, crouch: 4.2, move: 16, jump: 110, land: 20, fire: 34, recover: 0.33, spread: 2 }, recoil: { climb: 0.9, climbShots: 20, sway: 0.4, period: 4, kick: 1, jit: 0.25 } }),
  usp: W({ id: 'usp', name: 'USP-S', slot: 'secondary', cat: 'pistol', team: 'CT', price: 200, dmg: 35, ap: 0.505, rangeMod: 0.99, rpm: 352, auto: false, clip: 12, reserve: 24, reload: 2.17, speed: 240, reward: 300, pen: 80, deploy: 0.9, silencer: true, sound: 'pistol', model: 'pistol', color: 0x30343a,
    inacc: { stand: 3.7, crouch: 2.9, move: 13, jump: 100, land: 20, fire: 32, recover: 0.3, spread: 1.5 }, recoil: { climb: 1.0, climbShots: 20, sway: 0.3, period: 4, kick: 1, jit: 0.2 } }),
  p250: W({ id: 'p250', name: 'P250', slot: 'secondary', cat: 'pistol', price: 300, dmg: 38, ap: 0.64, rangeMod: 0.85, rpm: 400, auto: false, clip: 13, reserve: 26, reload: 2.2, speed: 240, reward: 300, pen: 90, deploy: 0.9, sound: 'pistol', model: 'pistol', color: 0x3a3a34,
    inacc: { stand: 6.0, crouch: 4.5, move: 16, jump: 110, land: 20, fire: 52, recover: 0.33, spread: 2 }, recoil: { climb: 1.3, climbShots: 20, sway: 0.4, period: 4, kick: 1, jit: 0.25 } }),
  elite: W({ id: 'elite', name: 'Dual Berettas', slot: 'secondary', cat: 'pistol', price: 300, dmg: 38, ap: 0.575, rangeMod: 0.79, rpm: 500, auto: false, clip: 30, reserve: 120, reload: 3.6, speed: 240, reward: 300, pen: 80, deploy: 1.0, sound: 'pistol', model: 'dualies', color: 0x9a9a9a,
    inacc: { stand: 6.5, crouch: 5, move: 18, jump: 120, land: 20, fire: 30, recover: 0.35, spread: 2.5 }, recoil: { climb: 0.9, climbShots: 30, sway: 0.5, period: 4, kick: 1, jit: 0.3 } }),
  tec9: W({ id: 'tec9', name: 'Tec-9', slot: 'secondary', cat: 'pistol', team: 'T', price: 500, dmg: 33, ap: 0.903, rangeMod: 0.79, rpm: 500, auto: false, clip: 18, reserve: 90, reload: 2.5, speed: 240, reward: 300, pen: 100, deploy: 0.9, sound: 'pistol', model: 'pistol', color: 0x6b6b5a,
    inacc: { stand: 7.0, crouch: 5.2, move: 14, jump: 110, land: 20, fire: 40, recover: 0.35, spread: 2.5 }, recoil: { climb: 1.1, climbShots: 20, sway: 0.5, period: 4, kick: 1, jit: 0.3 } }),
  fiveseven: W({ id: 'fiveseven', name: 'Five-SeveN', slot: 'secondary', cat: 'pistol', team: 'CT', price: 500, dmg: 32, ap: 0.911, rangeMod: 0.81, rpm: 400, auto: false, clip: 20, reserve: 100, reload: 2.2, speed: 240, reward: 300, pen: 100, deploy: 0.9, sound: 'pistol', model: 'pistol', color: 0x4a4f52,
    inacc: { stand: 5.2, crouch: 4.0, move: 14, jump: 110, land: 20, fire: 38, recover: 0.33, spread: 2 }, recoil: { climb: 1.1, climbShots: 20, sway: 0.4, period: 4, kick: 1, jit: 0.25 } }),
  cz75: W({ id: 'cz75', name: 'CZ75-Auto', slot: 'secondary', cat: 'pistol', price: 500, dmg: 31, ap: 0.776, rangeMod: 0.85, rpm: 600, auto: true, clip: 12, reserve: 12, reload: 2.7, speed: 240, reward: 100, pen: 90, deploy: 1.0, sound: 'pistol', model: 'pistol', color: 0x55524a,
    inacc: { stand: 6.5, crouch: 5, move: 18, jump: 110, land: 20, fire: 28, recover: 0.4, spread: 2.5 }, recoil: { climb: 0.6, climbShots: 12, sway: 0.7, period: 4, kick: 1, jit: 0.3 } }),
  deagle: W({ id: 'deagle', name: 'Desert Eagle', slot: 'secondary', cat: 'pistol', price: 700, dmg: 53, ap: 0.932, rangeMod: 0.81, rpm: 267, auto: false, clip: 7, reserve: 35, reload: 2.2, speed: 230, reward: 300, pen: 200, deploy: 1.0, sound: 'deagle', model: 'deagle', color: 0x8d8d8d,
    inacc: { stand: 3.2, crouch: 2.6, move: 70, jump: 160, land: 40, fire: 105, recover: 0.55, spread: 1.5 }, recoil: { climb: 2.6, climbShots: 7, sway: 0.6, period: 3, kick: 1.6, jit: 0.5 } }),
  r8: W({ id: 'r8', name: 'R8 Revolver', slot: 'secondary', cat: 'pistol', price: 600, dmg: 86, ap: 0.932, rangeMod: 0.98, rpm: 120, auto: false, clip: 8, reserve: 8, reload: 2.3, speed: 220, reward: 300, pen: 200, deploy: 1.0, prime: 0.4, sound: 'deagle', model: 'deagle', color: 0x55534f,
    inacc: { stand: 3.0, crouch: 2.4, move: 60, jump: 150, land: 40, fire: 80, recover: 0.6, spread: 1.0 }, recoil: { climb: 2.4, climbShots: 8, sway: 0.5, period: 3, kick: 1.6, jit: 0.4 } }),

  // ---------------- SMGs ----------------
  mac10: W({ id: 'mac10', name: 'MAC-10', cat: 'smg', team: 'T', price: 1050, dmg: 29, ap: 0.575, rangeMod: 0.80, rpm: 800, clip: 30, reserve: 100, reload: 2.6, speed: 240, reward: 600, pen: 100, sound: 'smg', model: 'smg', color: 0x3a3a3a,
    inacc: { stand: 10, crouch: 8, move: 24, jump: 110, land: 30, fire: 6, recover: 0.35, spread: 1.5 }, recoil: { climb: 0.2, climbShots: 9, sway: 1.1, period: 6, kick: 0.6, jit: 0.1 } }),
  mp9: W({ id: 'mp9', name: 'MP9', cat: 'smg', team: 'CT', price: 1250, dmg: 26, ap: 0.60, rangeMod: 0.87, rpm: 857, clip: 30, reserve: 120, reload: 2.1, speed: 240, reward: 600, pen: 100, sound: 'smg', model: 'smg', color: 0x2c2f33,
    inacc: { stand: 9, crouch: 7, move: 22, jump: 110, land: 30, fire: 6, recover: 0.33, spread: 1.5 }, recoil: { climb: 0.19, climbShots: 9, sway: 1.0, period: 6, kick: 0.6, jit: 0.1 } }),
  mp7: W({ id: 'mp7', name: 'MP7', cat: 'smg', price: 1500, dmg: 29, ap: 0.625, rangeMod: 0.85, rpm: 750, clip: 30, reserve: 120, reload: 3.1, speed: 220, reward: 600, pen: 100, sound: 'smg', model: 'smg', color: 0x34362f,
    inacc: { stand: 7, crouch: 5.5, move: 28, jump: 110, land: 30, fire: 5.5, recover: 0.33, spread: 1.2 }, recoil: { climb: 0.17, climbShots: 10, sway: 0.8, period: 6, kick: 0.6, jit: 0.1 } }),
  mp5sd: W({ id: 'mp5sd', name: 'MP5-SD', cat: 'smg', price: 1500, dmg: 27, ap: 0.625, rangeMod: 0.85, rpm: 750, clip: 30, reserve: 120, reload: 2.9, speed: 235, reward: 600, pen: 100, silencer: true, fixedSilencer: true, sound: 'smg', model: 'smg', color: 0x2a2c2e,
    inacc: { stand: 7, crouch: 5.5, move: 26, jump: 110, land: 30, fire: 5.5, recover: 0.33, spread: 1.2 }, recoil: { climb: 0.17, climbShots: 10, sway: 0.8, period: 6, kick: 0.6, jit: 0.1 } }),
  ump45: W({ id: 'ump45', name: 'UMP-45', cat: 'smg', price: 1200, dmg: 35, ap: 0.65, rangeMod: 0.75, rpm: 666, clip: 25, reserve: 100, reload: 3.5, speed: 230, reward: 600, pen: 100, sound: 'smg', model: 'smg', color: 0x3b3d40,
    inacc: { stand: 8, crouch: 6.5, move: 30, jump: 110, land: 30, fire: 7, recover: 0.35, spread: 1.5 }, recoil: { climb: 0.24, climbShots: 9, sway: 0.9, period: 6, kick: 0.7, jit: 0.1 } }),
  p90: W({ id: 'p90', name: 'P90', cat: 'smg', price: 2350, dmg: 26, ap: 0.69, rangeMod: 0.86, rpm: 857, clip: 50, reserve: 100, reload: 3.4, speed: 230, reward: 300, pen: 100, sound: 'smg', model: 'p90', color: 0x2d3a2d,
    inacc: { stand: 8, crouch: 6.5, move: 25, jump: 110, land: 30, fire: 4.5, recover: 0.35, spread: 1.4 }, recoil: { climb: 0.13, climbShots: 12, sway: 0.9, period: 8, kick: 0.5, jit: 0.1 } }),
  bizon: W({ id: 'bizon', name: 'PP-Bizon', cat: 'smg', price: 1400, dmg: 27, ap: 0.575, rangeMod: 0.80, rpm: 750, clip: 64, reserve: 120, reload: 2.4, speed: 240, reward: 600, pen: 100, sound: 'smg', model: 'smg', color: 0x3d3a34,
    inacc: { stand: 9, crouch: 7.5, move: 26, jump: 110, land: 30, fire: 5, recover: 0.35, spread: 1.8 }, recoil: { climb: 0.14, climbShots: 12, sway: 0.9, period: 8, kick: 0.5, jit: 0.1 } }),

  // ---------------- HEAVY ----------------
  nova: W({ id: 'nova', name: 'Nova', cat: 'shotgun', price: 1050, dmg: 26, pellets: 9, ap: 0.50, rangeMod: 0.70, range: 3000, rpm: 68, auto: false, clip: 8, reserve: 32, reload: 0.5, shellReload: true, speed: 220, reward: 900, pen: 50, sound: 'shotgun', model: 'shotgun', color: 0x3a3a3a,
    inacc: { stand: 12, crouch: 10, move: 30, jump: 80, land: 20, fire: 30, recover: 0.4, spread: 40 }, recoil: { climb: 2.6, climbShots: 8, sway: 0.3, period: 3, kick: 2, jit: 0.3 } }),
  xm1014: W({ id: 'xm1014', name: 'XM1014', cat: 'shotgun', price: 2000, dmg: 20, pellets: 6, ap: 0.80, rangeMod: 0.70, range: 3000, rpm: 171, auto: true, clip: 7, reserve: 32, reload: 0.45, shellReload: true, speed: 215, reward: 900, pen: 50, sound: 'shotgun', model: 'shotgun', color: 0x2a2a2a,
    inacc: { stand: 14, crouch: 11, move: 30, jump: 80, land: 20, fire: 20, recover: 0.4, spread: 38 }, recoil: { climb: 1.6, climbShots: 8, sway: 0.4, period: 3, kick: 1.6, jit: 0.4 } }),
  sawedoff: W({ id: 'sawedoff', name: 'Sawed-Off', cat: 'shotgun', team: 'T', price: 1100, dmg: 32, pellets: 8, ap: 0.75, rangeMod: 0.45, range: 1400, rpm: 71, auto: false, clip: 7, reserve: 32, reload: 0.5, shellReload: true, speed: 210, reward: 900, pen: 50, sound: 'shotgun', model: 'shotgun', color: 0x4a3626,
    inacc: { stand: 18, crouch: 15, move: 30, jump: 80, land: 20, fire: 30, recover: 0.4, spread: 60 }, recoil: { climb: 2.8, climbShots: 8, sway: 0.3, period: 3, kick: 2, jit: 0.3 } }),
  mag7: W({ id: 'mag7', name: 'MAG-7', cat: 'shotgun', team: 'CT', price: 1300, dmg: 30, pellets: 8, ap: 0.75, rangeMod: 0.45, range: 1400, rpm: 71, auto: false, clip: 5, reserve: 32, reload: 2.4, speed: 225, reward: 900, pen: 50, sound: 'shotgun', model: 'shotgun', color: 0x33363a,
    inacc: { stand: 12, crouch: 10, move: 30, jump: 80, land: 20, fire: 30, recover: 0.4, spread: 40 }, recoil: { climb: 2.8, climbShots: 8, sway: 0.3, period: 3, kick: 2, jit: 0.3 } }),
  m249: W({ id: 'm249', name: 'M249', cat: 'mg', price: 5200, dmg: 32, ap: 0.80, rangeMod: 0.97, rpm: 750, clip: 100, reserve: 200, reload: 5.7, speed: 195, reward: 300, pen: 200, deploy: 1.2, sound: 'rifle', model: 'mg', color: 0x2f322d,
    inacc: { stand: 9, crouch: 7, move: 160, jump: 260, land: 60, fire: 4, recover: 0.5, spread: 2 }, recoil: { climb: 0.16, climbShots: 12, sway: 1.4, period: 9, kick: 0.8, jit: 0.15 } }),
  negev: W({ id: 'negev', name: 'Negev', cat: 'mg', price: 1700, dmg: 35, ap: 0.71, rangeMod: 0.97, rpm: 800, clip: 150, reserve: 300, reload: 5.7, speed: 150, reward: 300, pen: 200, deploy: 1.2, sound: 'rifle', model: 'mg', color: 0x3a3c36,
    inacc: { stand: 10, crouch: 8, move: 170, jump: 260, land: 60, fire: 3, recover: 0.6, spread: 2 }, recoil: { climb: 0.14, climbShots: 10, sway: 1.2, period: 9, kick: 0.8, jit: 0.15 } }),

  // ---------------- RIFLES ----------------
  galil: W({ id: 'galil', name: 'Galil AR', cat: 'rifle', team: 'T', price: 1800, dmg: 30, ap: 0.775, rpm: 666, clip: 35, reserve: 90, reload: 3.0, speed: 215, pen: 200, model: 'rifle', color: 0x4b4636,
    inacc: { stand: 6.0, crouch: 4.6, move: 130, jump: 250, land: 60, fire: 7.5, recover: 0.4, spread: 0.6 }, recoil: { climb: 0.24, climbShots: 9, sway: 0.95, period: 7, kick: 1, jit: 0.08 } }),
  famas: W({ id: 'famas', name: 'FAMAS', cat: 'rifle', team: 'CT', price: 2050, dmg: 30, ap: 0.70, rangeMod: 0.96, rpm: 666, clip: 25, reserve: 90, reload: 3.3, speed: 220, pen: 200, burst: { shots: 3, interval: 0.075 }, model: 'bullpup', color: 0x3c4146,
    inacc: { stand: 5.5, crouch: 4.2, move: 120, jump: 250, land: 60, fire: 7.5, recover: 0.4, spread: 0.6 }, recoil: { climb: 0.22, climbShots: 9, sway: 0.8, period: 7, kick: 1, jit: 0.08 } }),
  ak47: W({ id: 'ak47', name: 'AK-47', cat: 'rifle', team: 'T', price: 2700, dmg: 36, ap: 0.775, rpm: 600, clip: 30, reserve: 90, reload: 2.43, speed: 215, pen: 200, model: 'ak', color: 0x2a2a2a, wood: 0x7a4a25, sound: 'ak',
    inacc: { stand: 4.8, crouch: 3.6, move: 140, jump: 260, land: 60, fire: 7.8, recover: 0.38, spread: 0.6 }, recoil: { climb: 0.32, climbShots: 9, sway: 1.25, period: 7, kick: 1.2, jit: 0.07, startDir: -1 } }),
  m4a4: W({ id: 'm4a4', name: 'M4A4', cat: 'rifle', team: 'CT', price: 3100, dmg: 33, ap: 0.70, rangeMod: 0.97, rpm: 666, clip: 30, reserve: 90, reload: 3.07, speed: 225, pen: 200, model: 'm4', color: 0x2c2e30, sound: 'm4',
    inacc: { stand: 4.0, crouch: 3.0, move: 115, jump: 250, land: 60, fire: 7.0, recover: 0.36, spread: 0.6 }, recoil: { climb: 0.26, climbShots: 10, sway: 0.85, period: 7, kick: 1.0, jit: 0.07, startDir: 1 } }),
  m4a1s: W({ id: 'm4a1s', name: 'M4A1-S', cat: 'rifle', team: 'CT', price: 2900, dmg: 38, ap: 0.70, rangeMod: 0.99, rpm: 600, clip: 20, reserve: 80, reload: 3.07, speed: 225, pen: 200, silencer: true, model: 'm4', color: 0x34373a, sound: 'm4',
    inacc: { stand: 3.4, crouch: 2.6, move: 105, jump: 250, land: 60, fire: 7.6, recover: 0.36, spread: 0.5 }, recoil: { climb: 0.22, climbShots: 9, sway: 0.6, period: 6, kick: 0.9, jit: 0.06, startDir: 1 } }),
  sg553: W({ id: 'sg553', name: 'SG 553', cat: 'rifle', team: 'T', price: 3000, dmg: 30, ap: 1.0, rpm: 545, clip: 30, reserve: 90, reload: 2.8, speed: 210, scopedSpeed: 150, pen: 200, zoom: [45], scopeType: 'aug', model: 'rifle', color: 0x50503f,
    inacc: { stand: 4.5, crouch: 3.4, move: 130, jump: 250, land: 60, fire: 7.5, recover: 0.4, spread: 0.5, scoped: 1.6 }, recoil: { climb: 0.24, climbShots: 9, sway: 0.8, period: 7, kick: 1, jit: 0.07 } }),
  aug: W({ id: 'aug', name: 'AUG', cat: 'rifle', team: 'CT', price: 3300, dmg: 28, ap: 0.90, rpm: 600, clip: 30, reserve: 90, reload: 3.8, speed: 220, scopedSpeed: 150, pen: 200, zoom: [45], scopeType: 'aug', model: 'bullpup', color: 0x4c5a4a,
    inacc: { stand: 4.0, crouch: 3.0, move: 120, jump: 250, land: 60, fire: 7.2, recover: 0.4, spread: 0.5, scoped: 1.5 }, recoil: { climb: 0.22, climbShots: 9, sway: 0.75, period: 7, kick: 1, jit: 0.07 } }),
  ssg08: W({ id: 'ssg08', name: 'SSG 08', cat: 'sniper', price: 1700, dmg: 88, ap: 0.85, rangeMod: 0.98, rpm: 48, auto: false, clip: 10, reserve: 90, reload: 3.7, speed: 230, scopedSpeed: 230, reward: 300, pen: 250, deploy: 1.1, zoom: [40, 15], scopeType: 'sniper', model: 'sniper', color: 0x3a3f46, sound: 'scout',
    inacc: { stand: 22, crouch: 20, move: 60, jump: 150, land: 50, fire: 40, recover: 0.4, spread: 0.3, scoped: 1.5, jumpApex: 3 }, recoil: { climb: 2.2, climbShots: 3, sway: 0.2, period: 3, kick: 1.5, jit: 0.2 } }),
  awp: W({ id: 'awp', name: 'AWP', cat: 'sniper', price: 4750, dmg: 115, ap: 0.975, rangeMod: 0.99, rpm: 41, auto: false, clip: 5, reserve: 30, reload: 3.6, speed: 200, scopedSpeed: 100, reward: 100, pen: 300, deploy: 1.25, zoom: [40, 10], scopeType: 'sniper', model: 'awp', color: 0x3c5236, sound: 'awp',
    inacc: { stand: 80, crouch: 75, move: 170, jump: 300, land: 80, fire: 60, recover: 0.5, spread: 0.2, scoped: 1.2 }, recoil: { climb: 3.4, climbShots: 3, sway: 0.2, period: 3, kick: 2, jit: 0.2 } }),
  g3sg1: W({ id: 'g3sg1', name: 'G3SG1', cat: 'sniper', team: 'T', price: 5000, dmg: 80, ap: 0.825, rpm: 240, auto: true, clip: 20, reserve: 90, reload: 4.7, speed: 215, scopedSpeed: 120, reward: 300, pen: 250, deploy: 1.2, zoom: [40, 15], scopeType: 'sniper', model: 'sniper', color: 0x2e2e2a, sound: 'scout',
    inacc: { stand: 30, crouch: 25, move: 150, jump: 280, land: 70, fire: 20, recover: 0.35, spread: 0.3, scoped: 1.6 }, recoil: { climb: 0.9, climbShots: 6, sway: 0.4, period: 4, kick: 1.2, jit: 0.2 } }),
  scar20: W({ id: 'scar20', name: 'SCAR-20', cat: 'sniper', team: 'CT', price: 5000, dmg: 80, ap: 0.825, rpm: 240, auto: true, clip: 20, reserve: 90, reload: 3.1, speed: 215, scopedSpeed: 120, reward: 300, pen: 250, deploy: 1.2, zoom: [40, 15], scopeType: 'sniper', model: 'sniper', color: 0x2c2f33, sound: 'scout',
    inacc: { stand: 30, crouch: 25, move: 150, jump: 280, land: 70, fire: 20, recover: 0.35, spread: 0.3, scoped: 1.6 }, recoil: { climb: 0.9, climbShots: 6, sway: 0.4, period: 4, kick: 1.2, jit: 0.2 } }),

  // ---------------- MELEE / UTILITY ----------------
  knife: W({ id: 'knife', name: 'Bıçak', slot: 'knife', cat: 'knife', price: 0, dmg: 40, ap: 0.85, speed: 250, reward: 1500, deploy: 0.6, model: 'knife', clip: 0, reserve: 0, rpm: 150, sound: 'knife' }),
  taser: W({ id: 'taser', name: 'Zeus x27', slot: 'taser', cat: 'taser', price: 200, dmg: 500, ap: 1, range: 183, rpm: 30, auto: false, clip: 1, reserve: 0, reload: 0, speed: 220, reward: 0, pen: 0, deploy: 1.0, model: 'taser', sound: 'taser',
    inacc: { stand: 2, crouch: 2, move: 6, jump: 30, land: 0, fire: 0, recover: 0.3, spread: 1 }, recoil: { climb: 1, climbShots: 1, sway: 0, period: 1, kick: 1, jit: 0 } }),
  c4: W({ id: 'c4', name: 'C4', slot: 'c4', cat: 'c4', price: 0, speed: 250, model: 'c4', deploy: 0.8, clip: 0, reserve: 0, rpm: 60 }),
};

export const GRENADES = {
  hegrenade: { id: 'hegrenade', name: 'HE Bombası', short: 'HE', price: 300, max: 1, team: null, fuse: 1.5, color: 0x4b5a32 },
  flashbang: { id: 'flashbang', name: 'Flaş Bombası', short: 'Flaş', price: 200, max: 2, team: null, fuse: 1.5, color: 0x9aa0a6 },
  smokegrenade: { id: 'smokegrenade', name: 'Sis Bombası', short: 'Sis', price: 300, max: 1, team: null, fuse: 1.5, color: 0x6f7f72 },
  molotov: { id: 'molotov', name: 'Molotof', short: 'Molotof', price: 400, max: 1, team: 'T', fuse: 2.0, color: 0x8a5a2a },
  incgrenade: { id: 'incgrenade', name: 'Yangın Bombası', short: 'Yangın', price: 500, max: 1, team: 'CT', fuse: 2.0, color: 0x8a3a2a },
  decoy: { id: 'decoy', name: 'Dekoy', short: 'Dekoy', price: 50, max: 1, team: null, fuse: 2.0, color: 0x6a6a5a },
};
export const GRENADE_ORDER = ['hegrenade', 'flashbang', 'smokegrenade', 'molotov', 'incgrenade', 'decoy'];
export const MAX_GRENADES = 4;
for (const g of Object.values(GRENADES)) {
  g.slot = 'grenade'; g.cat = 'grenade'; g.speed = 245; g.deploy = 0.6; g.model = 'grenade'; g.reward = 300;
}

export const EQUIPMENT = {
  vest: { id: 'vest', name: 'Kevlar Yelek', price: 650 },
  vesthelm: { id: 'vesthelm', name: 'Kevlar + Kask', price: 1000 },
  defuser: { id: 'defuser', name: 'İmha Kiti', price: 400, team: 'CT' },
};

export function getDef(id) { return WEAPONS[id] || GRENADES[id]; }

// Buy menu layout (columns like the CS2 buy menu)
export const BUY_LAYOUT = [
  { title: 'EKİPMAN', items: ['vest', 'vesthelm', 'taser', 'defuser'] },
  { title: 'TABANCALAR', items: ['glock|usp', 'elite', 'p250', 'tec9|fiveseven', 'cz75', 'deagle', 'r8'] },
  { title: 'HAFİF MAKİNELİ', items: ['mac10|mp9', 'mp7', 'mp5sd', 'ump45', 'p90', 'bizon'] },
  { title: 'AĞIR', items: ['nova', 'xm1014', 'sawedoff|mag7', 'm249', 'negev'] },
  { title: 'TÜFEKLER', items: ['galil|famas', 'ak47|m4a4', 'm4a1s', 'ssg08', 'sg553|aug', 'awp', 'g3sg1|scar20'] },
  { title: 'BOMBALAR', items: ['hegrenade', 'flashbang', 'smokegrenade', 'molotov|incgrenade', 'decoy'] },
];

// resolve "tId|ctId" entries for a team
export function resolveBuyItem(entry, team) {
  if (!entry.includes('|')) return entry;
  const [t, ct] = entry.split('|');
  return team === 'CT' ? ct : t;
}

export function itemPrice(id) {
  const d = WEAPONS[id] || GRENADES[id] || EQUIPMENT[id];
  return d ? d.price : 0;
}
export function itemName(id) {
  const d = WEAPONS[id] || GRENADES[id] || EQUIPMENT[id];
  return d ? d.name : id;
}
export function itemTeam(id) {
  const d = WEAPONS[id] || GRENADES[id] || EQUIPMENT[id];
  return d ? d.team : null;
}

// ----- deterministic spray patterns (aim-punch in degrees per shot index) -----
const patternCache = {};
export function recoilPattern(def) {
  if (patternCache[def.id]) return patternCache[def.id];
  const r = def.recoil;
  const n = Math.max(def.clip || 1, 1) + 2;
  let seed = 0;
  for (const ch of def.id) seed = (seed * 31 + ch.charCodeAt(0)) % 10007;
  const rnd = () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280 - 0.5; };
  const dir0 = r.startDir || (rnd() > 0 ? 1 : -1);
  const pts = [[0, 0]];
  let p = 0, y = 0;
  for (let i = 1; i < n; i++) {
    if (i <= r.climbShots) {
      p += r.climb * (i < 3 ? 0.75 : 1.0 + (i / r.climbShots) * 0.1);
      y += r.jit * rnd() * 2 + (i > r.climbShots * 0.6 ? 0.04 * -dir0 : 0);
    } else {
      p += r.climb * 0.08 * (rnd() + 0.6);
      const k = (i - r.climbShots) / r.period;
      const target = dir0 * r.sway * Math.sin(k * Math.PI) * (1 + 0.15 * Math.floor(k));
      y += (target - y) * 0.55 + r.jit * rnd();
    }
    pts.push([p, y]);
  }
  patternCache[def.id] = pts;
  return pts;
}
