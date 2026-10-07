// Map: a column heightfield on a 64-unit grid. Each cell has a solid column from the
// ground up to `floor`, and optionally a roof slab [roof, roofTop] (tunnels / doorways).
// This makes collision, bullet ray casting, grenade bounces and bot navigation simple and fast.
import * as THREE from './vendor/three.module.min.js';
import { CELL, STEP_HEIGHT, STAND_HEIGHT, world } from './state.js';
import * as TX from './textures.js';

export const MAT = { SAND: 0, TILE: 1, PLASTER: 2, BRICK: 3, WOOD: 4, STONE: 5, CONCRETE: 6, METAL: 7, BEAM: 8, PLASTER2: 9, METAL2: 10 };
// penetration cost per unit of thickness
const DENSITY = [99, 99, 3.4, 4.0, 1.0, 4.0, 2.6, 2.0, 1.4, 3.4, 2.0];
export const MAT_SOUND = ['sand', 'stone', 'stone', 'stone', 'wood', 'stone', 'stone', 'metal', 'wood', 'stone', 'metal'];
const WALL = 256;
const BOTTOM = -64;

export class GameMap {
  constructor() {
    this.name = 'de_kasaba';
    this.W = 60; this.H = 64;
    const N = this.W * this.H;
    this.floor = new Float32Array(N).fill(WALL);
    this.roof = new Float32Array(N).fill(Infinity);
    this.roofTop = new Float32Array(N).fill(-Infinity);
    this.mat = new Uint8Array(N).fill(MAT.PLASTER);
    this.topMat = new Uint8Array(N).fill(MAT.PLASTER);
    this.ox = -this.W * CELL / 2;
    this.oz = -this.H * CELL / 2;
    this.zones = [];
    this.spawns = { T: [], CT: [] };
    this.spots = {};
    this.decals = [];
    this.layout();
    this.buildNav();
  }

  idx(c, r) { return r * this.W + c; }
  inside(c, r) { return c >= 0 && r >= 0 && c < this.W && r < this.H; }
  cx(c) { return this.ox + (c + 0.5) * CELL; }
  cz(r) { return this.oz + (r + 0.5) * CELL; }
  col(x) { return Math.floor((x - this.ox) / CELL); }
  row(z) { return Math.floor((z - this.oz) / CELL); }
  cellPos(c, r) { return new THREE.Vector3(this.cx(c), this.floor[this.idx(c, r)], this.cz(r)); }

  // ---------------- authoring helpers ----------------
  rect(c1, r1, c2, r2, h, mat, top) {
    for (let r = Math.min(r1, r2); r <= Math.max(r1, r2); r++)
      for (let c = Math.min(c1, c2); c <= Math.max(c1, c2); c++) {
        if (!this.inside(c, r)) continue;
        const i = this.idx(c, r);
        this.floor[i] = h; this.mat[i] = mat; this.topMat[i] = top ?? mat;
        this.roof[i] = Infinity; this.roofTop[i] = -Infinity;
      }
  }
  open(c1, r1, c2, r2, mat = MAT.SAND) { this.rect(c1, r1, c2, r2, 0, MAT.STONE, mat); }
  roofed(c1, r1, c2, r2, roofH, mat = MAT.BEAM, floorMat = MAT.STONE) {
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      const i = this.idx(c, r);
      this.floor[i] = 0; this.mat[i] = MAT.STONE; this.topMat[i] = floorMat;
      this.roof[i] = roofH; this.roofTop[i] = WALL - 32;
    }
  }
  box(c1, r1, c2, r2, h, mat = MAT.WOOD) {
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      const i = this.idx(c, r);
      this.floor[i] = this.floor[i] + h > WALL ? WALL : h; this.mat[i] = mat; this.topMat[i] = mat;
    }
  }
  // stairs rising from h0 to h1 along direction (dx,dr) through the rect
  stairs(c1, r1, c2, r2, h0, h1, axis, dirSign) {
    const len = axis === 'c' ? (c2 - c1 + 1) : (r2 - r1 + 1);
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      let k = axis === 'c' ? (c - c1) : (r - r1);
      if (dirSign < 0) k = len - 1 - k;
      const h = h0 + (h1 - h0) * (k + 1) / len;
      const i = this.idx(c, r);
      this.floor[i] = h; this.mat[i] = MAT.STONE; this.topMat[i] = MAT.STONE;
    }
  }
  zone(name, c1, r1, c2, r2, type) { this.zones.push({ name, c1, r1, c2, r2, type }); }
  spot(name, c, r, lookC, lookR) {
    (this.spots[name] ||= []).push({ c, r, pos: new THREE.Vector3(this.cx(c), 0, this.cz(r)), look: lookC !== undefined ? new THREE.Vector3(this.cx(lookC), 0, this.cz(lookR)) : null });
  }

  // ---------------- the level ----------------
  layout() {
    const M = MAT;
    // --- CT spawn ---
    this.open(25, 2, 35, 10, M.SAND);
    // --- CT -> A corridor ---
    this.open(15, 4, 24, 8, M.SAND);
    // --- A site ---
    this.open(3, 2, 14, 13, M.TILE);
    // --- A long ---
    this.open(3, 14, 7, 37, M.SAND);
    // --- Long doors (roofed doorway) ---
    this.roofed(8, 33, 10, 36, 136);
    // --- Outside long ---
    this.open(11, 31, 18, 40, M.SAND);
    this.open(13, 41, 18, 50, M.SAND);
    // --- T spawn ---
    this.open(13, 51, 40, 60, M.SAND);
    // --- T mid / mid ---
    this.open(25, 40, 31, 50, M.SAND);
    this.open(25, 11, 31, 39, M.SAND);
    // outside long -> T mid connector
    this.open(19, 42, 24, 44, M.SAND);
    // --- mid doors: wall across mid with a 2-wide doorway ---
    this.rect(25, 24, 27, 24, WALL, M.PLASTER);
    this.rect(30, 24, 31, 24, WALL, M.PLASTER);
    this.roofed(28, 24, 29, 24, 128);
    // --- Catwalk / A short ---
    this.stairs(21, 16, 24, 18, 0, 64, 'c', -1);   // rising westward from mid
    this.rect(15, 16, 20, 18, 64, M.STONE, M.STONE);  // walkway
    this.rect(15, 12, 17, 15, 64, M.STONE, M.STONE);  // turns north (ledge into A site)
    this.stairs(15, 9, 17, 11, 0, 48, 'r', 1);     // stairs down to CT corridor
    // --- B site ---
    this.open(42, 2, 55, 19, M.TILE);
    // --- CT -> B corridor ---
    this.open(36, 4, 41, 8, M.SAND);
    // --- mid -> B (B doors corridor) ---
    this.open(32, 15, 41, 17, M.SAND);
    this.rect(38, 15, 39, 15, WALL, M.PLASTER);
    this.roofed(38, 16, 39, 17, 128);
    // --- upper tunnel yard + tunnel ---
    this.open(38, 43, 51, 50, M.SAND);
    this.roofed(47, 20, 51, 42, 144, M.BEAM, M.STONE);
    // lower tunnel (to mid)
    this.roofed(32, 34, 46, 36, 136, M.BEAM, M.STONE);

    // ---------- cover / props ----------
    // A site
    this.box(7, 5, 8, 6, 112);           // big double crate
    this.box(9, 6, 9, 6, 56);
    this.box(4, 10, 6, 10, 40, M.CONCRETE);
    this.box(12, 3, 13, 3, 56);
    this.box(3, 2, 4, 3, 48, M.STONE);   // back platform
    this.box(11, 11, 11, 12, 56);
    // A long
    this.box(7, 20, 7, 21, 56);
    this.box(3, 27, 4, 29, 112, M.METAL); // container
    this.box(7, 31, 7, 31, 56);
    // outside long
    this.box(15, 35, 16, 35, 56);
    this.box(11, 38, 11, 39, 112, M.METAL2);
    // mid
    this.box(26, 31, 27, 32, 56);        // mid box
    this.box(31, 20, 31, 21, 56);
    this.box(25, 13, 25, 14, 112, M.METAL);
    // CT spawn
    this.box(33, 3, 34, 4, 112, M.METAL2);
    this.box(28, 6, 28, 6, 56);
    // B site
    this.box(45, 6, 46, 7, 112);
    this.box(47, 7, 47, 7, 56);
    this.box(51, 10, 52, 11, 112);
    this.box(50, 11, 50, 11, 56);
    this.box(49, 15, 51, 15, 40, M.CONCRETE);
    this.box(52, 2, 55, 4, 48, M.STONE);
    this.box(43, 13, 43, 14, 56);
    this.box(54, 16, 55, 17, 112, M.METAL);
    // B doors corridor
    this.box(34, 17, 34, 17, 56);
    // T spawn
    this.box(20, 53, 21, 54, 56);
    this.box(33, 56, 34, 57, 112);
    this.box(16, 58, 17, 59, 112, M.METAL2);
    this.box(37, 52, 37, 52, 56);
    // T mid sightline blocker + mid barricade (no spawn-to-spawn sightlines)
    this.box(28, 44, 29, 44, 112);
    this.box(27, 20, 30, 20, 112);
    // yard
    this.box(42, 46, 43, 46, 56);
    this.box(49, 48, 50, 49, 112);

    // wall material variety (brick blocks on some building walls)
    for (let r = 0; r < this.H; r++) for (let c = 0; c < this.W; c++) {
      const i = this.idx(c, r);
      if (this.floor[i] >= WALL) {
        const h = ((c >> 2) * 73856093 ^ (r >> 2) * 19349663) >>> 0;
        this.mat[i] = this.topMat[i] = (h % 5 === 0) ? MAT.BRICK : (h % 3 === 0 ? MAT.PLASTER2 : MAT.PLASTER);
        this.floor[i] = WALL + ((h >> 3) % 3) * 48;
      }
    }

    // zones
    this.zone('A', 3, 2, 14, 13, 'site');
    this.zone('B', 42, 2, 55, 19, 'site');
    this.zone('T', 13, 51, 40, 60, 'buyT');
    this.zone('CT', 25, 2, 35, 10, 'buyCT');

    // named areas for HUD callouts
    this.areas = [
      ['A Bölgesi', 3, 2, 14, 13], ['A Uzun', 3, 14, 7, 37], ['Uzun Kapılar', 8, 33, 10, 36], ['Dış Uzun', 11, 31, 18, 50],
      ['CT Doğuş', 25, 2, 35, 10], ['CT Koridoru', 15, 4, 24, 8], ['A Kısa', 15, 9, 24, 18], ['Orta', 25, 11, 31, 39],
      ['T Orta', 25, 40, 31, 50], ['T Doğuş', 13, 51, 40, 60], ['B Bölgesi', 42, 2, 55, 19], ['B Kapıları', 32, 15, 41, 17],
      ['CT-B Yolu', 36, 4, 41, 8], ['Üst Tünel', 47, 20, 51, 42], ['Alt Tünel', 32, 34, 46, 36], ['Tünel Avlusu', 38, 43, 51, 50],
      ['Bağlantı', 19, 42, 24, 44],
    ];

    // spawns
    const tsp = [[22, 56], [25, 57], [28, 56], [31, 57], [24, 59], [27, 59], [30, 59], [19, 57], [35, 55], [26, 54]];
    const ctsp = [[27, 5], [29, 4], [31, 5], [30, 7], [27, 8], [32, 8], [29, 9], [26, 3], [33, 6], [31, 3]];
    this.spawns.T = tsp.map(([c, r]) => ({ c, r, pos: new THREE.Vector3(this.cx(c), 0, this.cz(r)), yaw: 0 }));
    this.spawns.CT = ctsp.map(([c, r]) => ({ c, r, pos: new THREE.Vector3(this.cx(c), 0, this.cz(r)), yaw: Math.PI }));

    // ---- bot spots ----
    // plant spots
    this.spot('plantA', 9, 8); this.spot('plantA', 6, 4); this.spot('plantA', 10, 4);
    this.spot('plantB', 48, 8); this.spot('plantB', 49, 4); this.spot('plantB', 46, 11);
    // CT holds (pos, look target)
    this.spot('ctA', 6, 7, 5, 20); this.spot('ctA', 12, 9, 16, 14); this.spot('ctA', 4, 12, 5, 25);
    this.spot('ctB', 49, 13, 49, 24); this.spot('ctB', 44, 9, 34, 16); this.spot('ctB', 52, 13, 49, 22);
    this.spot('ctMid', 29, 9, 28, 30); this.spot('ctMid', 33, 9, 28, 28);
    this.spot('ctShort', 19, 7, 17, 15);
    // T post plant
    this.spot('postA', 10, 10, 14, 6); this.spot('postA', 5, 9, 18, 6); this.spot('postA', 12, 12, 16, 6); this.spot('postA', 5, 15, 9, 6);
    this.spot('postB', 44, 15, 40, 6); this.spot('postB', 48, 12, 42, 6); this.spot('postB', 53, 8, 42, 6); this.spot('postB', 49, 18, 46, 8);
    // T staging points per route
    this.spot('stageLong', 13, 36, 9, 34); this.spot('stageShort', 28, 30, 28, 15); this.spot('stageTunnel', 49, 40, 49, 22);
    this.spot('stageBdoors', 29, 27, 34, 16);
    // route waypoints to sites
    this.routes = {
      A: [['stageLong', 'longTop'], ['stageShort', 'shortTop']],
      B: [['stageTunnel', 'tunnelExit'], ['stageBdoors', 'bdoors']],
    };
    this.spot('longTop', 5, 16, 6, 6); this.spot('shortTop', 16, 13, 8, 8);
    this.spot('tunnelExit', 49, 21, 48, 8); this.spot('bdoors', 40, 16, 48, 8);
    // utility: smoke targets per site (to block CT views)
    this.util = {
      A: { smoke: [[10, 12], [13, 6]], flash: [[8, 8]], molly: [[4, 3], [12, 3]] },
      B: { smoke: [[43, 9], [47, 4]], flash: [[48, 10]], molly: [[53, 3], [45, 13]] },
      ctA: { molly: [[5, 18]], smoke: [[5, 22]] }, ctB: { molly: [[49, 22]], smoke: [[49, 24]] },
    };

    // site letter decals: [text, c, r, face('n','s','e','w'), height]
    this.decals = [
      ['A', 2, 6, 'e', 120], ['A', 9, 1, 's', 120], ['B', 56, 9, 'w', 120], ['B', 48, 1, 's', 120],
      ['A→', 7, 38, 'n', 90, 'arrow'], ['B→', 46, 51, 'n', 90, 'arrow'], ['A→', 24, 19, 'n', 90, 'arrow'], ['B→', 33, 14, 's', 90, 'arrow'],
      ['A', 2, 30, 'e', 110], ['B', 46, 25, 'e', 110],
    ];
  }

  zoneAt(x, z) {
    const c = this.col(x), r = this.row(z);
    for (const zn of this.zones) if (c >= zn.c1 && c <= zn.c2 && r >= zn.r1 && r <= zn.r2) {
      if (zn.type === 'site') return zn.name;
    }
    return null;
  }
  inBuyZone(team, x, z) {
    const c = this.col(x), r = this.row(z);
    const zn = this.zones.find(q => q.type === 'buy' + team);
    if (!zn) return false;
    return c >= zn.c1 - 1 && c <= zn.c2 + 1 && r >= zn.r1 - 1 && r <= zn.r2 + 1;
  }
  areaName(x, z) {
    const c = this.col(x), r = this.row(z);
    for (const [n, c1, r1, c2, r2] of this.areas) if (c >= c1 && c <= c2 && r >= r1 && r <= r2) return n;
    return '';
  }
  siteCenter(name) {
    const zn = this.zones.find(z => z.name === name);
    return new THREE.Vector3(this.cx((zn.c1 + zn.c2) / 2), 0, this.cz((zn.r1 + zn.r2) / 2));
  }

  // ---------------- queries ----------------
  floorAt(x, z) {
    const c = this.col(x), r = this.row(z);
    if (!this.inside(c, r)) return 9999;
    return this.floor[this.idx(c, r)];
  }
  solidAt(x, y, z) {
    const c = this.col(x), r = this.row(z);
    if (!this.inside(c, r)) return true;
    const i = this.idx(c, r);
    if (y <= this.floor[i]) return true;
    if (y >= this.roof[i] && y <= this.roofTop[i]) return true;
    return false;
  }

  // Ray cast through the heightfield (3D DDA). Returns {t, nx, ny, nz, mat, x, y, z} or null.
  raycast(ox, oy, oz, dx, dy, dz, maxDist) {
    const inv = CELL;
    let gx = (ox - this.ox) / inv, gz = (oz - this.oz) / inv;
    let c = Math.floor(gx), r = Math.floor(gz);
    const stepC = dx > 0 ? 1 : -1, stepR = dz > 0 ? 1 : -1;
    const tDeltaC = dx !== 0 ? Math.abs(inv / dx) : Infinity;
    const tDeltaR = dz !== 0 ? Math.abs(inv / dz) : Infinity;
    let tMaxC = dx !== 0 ? ((dx > 0 ? (c + 1 - gx) : (gx - c)) * inv) / Math.abs(dx) : Infinity;
    let tMaxR = dz !== 0 ? ((dz > 0 ? (r + 1 - gz) : (gz - r)) * inv) / Math.abs(dz) : Infinity;
    let t = 0;
    let lastAxis = -1;
    for (let iter = 0; iter < 512; iter++) {
      if (!this.inside(c, r)) {
        if (t === 0) return null;
        return this._hit(t, lastAxis, stepC, stepR, MAT.PLASTER, ox, oy, oz, dx, dy, dz);
      }
      const i = this.idx(c, r);
      const tExit = Math.min(tMaxC, tMaxR, maxDist);
      const fl = this.floor[i];
      const yEnter = oy + dy * t;
      // side entry into a solid column
      if (t > 0 && yEnter <= fl) return this._hit(t, lastAxis, stepC, stepR, this.mat[i], ox, oy, oz, dx, dy, dz);
      if (t > 0 && yEnter >= this.roof[i] && yEnter <= this.roofTop[i]) return this._hit(t, lastAxis, stepC, stepR, MAT.BEAM, ox, oy, oz, dx, dy, dz);
      // crossing the floor top from above
      if (dy < 0 && yEnter > fl) {
        const th = (fl - oy) / dy;
        if (th >= t && th <= tExit) return this._hit(th, 1, 0, 0, this.topMat[i], ox, oy, oz, dx, dy, dz, 1);
      }
      if (this.roof[i] !== Infinity) {
        if (dy > 0 && yEnter < this.roof[i]) {
          const th = (this.roof[i] - oy) / dy;
          if (th >= t && th <= tExit) return this._hit(th, 1, 0, 0, MAT.BEAM, ox, oy, oz, dx, dy, dz, -1);
        }
        if (dy < 0 && yEnter > this.roofTop[i]) {
          const th = (this.roofTop[i] - oy) / dy;
          if (th >= t && th <= tExit) return this._hit(th, 1, 0, 0, MAT.BEAM, ox, oy, oz, dx, dy, dz, 1);
        }
      }
      if (tExit >= maxDist) return null;
      if (tMaxC < tMaxR) { t = tMaxC; tMaxC += tDeltaC; c += stepC; lastAxis = 0; }
      else { t = tMaxR; tMaxR += tDeltaR; r += stepR; lastAxis = 2; }
    }
    return null;
  }
  _hit(t, axis, stepC, stepR, mat, ox, oy, oz, dx, dy, dz, ySign = 0) {
    const h = { t, mat, x: ox + dx * t, y: oy + dy * t, z: oz + dz * t, nx: 0, ny: 0, nz: 0 };
    if (axis === 0) h.nx = -stepC;
    else if (axis === 2) h.nz = -stepR;
    else h.ny = ySign || 1;
    return h;
  }
  // how far a ray stays inside solid starting at a hit point (for wall penetration)
  thicknessAt(x, y, z, dx, dy, dz, maxT = 160) {
    for (let t = 2; t <= maxT; t += 4) {
      if (!this.solidAt(x + dx * t, y + dy * t, z + dz * t)) return t;
    }
    return Infinity;
  }
  density(mat) { return DENSITY[mat] ?? 4; }

  // line of sight between two points
  los(a, b) {
    const dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
    const d = Math.hypot(dx, dy, dz);
    if (d < 1) return true;
    return !this.raycast(a.x, a.y, a.z, dx / d, dy / d, dz / d, d);
  }

  // ---------------- hull collision for characters ----------------
  // returns true if a hull at (x,z) with feet at `feet`, height h, would be blocked
  hullBlocked(x, z, feet, h, R, stepAllow) {
    const c1 = this.col(x - R), c2 = this.col(x + R), r1 = this.row(z - R), r2 = this.row(z + R);
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      if (!this.inside(c, r)) return true;
      const i = this.idx(c, r);
      const fl = this.floor[i];
      if (fl > feet + stepAllow + 0.01) return true;
      if (this.roof[i] < feet + h && this.roofTop[i] > feet) return true;
      if (fl > feet && this.roof[i] - fl < h) return true;
    }
    return false;
  }
  groundUnder(x, z, R) {
    const c1 = this.col(x - R), c2 = this.col(x + R), r1 = this.row(z - R), r2 = this.row(z + R);
    let g = -Infinity;
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      if (!this.inside(c, r)) continue;
      g = Math.max(g, this.floor[this.idx(c, r)]);
    }
    return g;
  }
  ceilingAbove(x, z, R, feet) {
    const c1 = this.col(x - R), c2 = this.col(x + R), r1 = this.row(z - R), r2 = this.row(z + R);
    let ce = Infinity;
    for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) {
      if (!this.inside(c, r)) continue;
      const i = this.idx(c, r);
      if (this.roof[i] > feet) ce = Math.min(ce, this.roof[i]);
    }
    return ce;
  }

  // ---------------- navigation ----------------
  walkable(c, r) {
    if (!this.inside(c, r)) return false;
    const i = this.idx(c, r);
    return this.floor[i] < 200 && (this.roof[i] - this.floor[i]) >= STAND_HEIGHT;
  }
  buildNav() {
    const W = this.W, H = this.H;
    this.navEdges = new Array(W * H);
    this.nearWall = new Uint8Array(W * H);
    const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [1, -1], [-1, 1], [-1, -1]];
    for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
      const i = this.idx(c, r);
      const list = [];
      if (this.walkable(c, r)) {
        const f = this.floor[i];
        for (const [dc, dr] of dirs) {
          const nc = c + dc, nr = r + dr;
          if (!this.walkable(nc, nr)) { this.nearWall[i] = 1; continue; }
          const j = this.idx(nc, nr);
          const nf = this.floor[j];
          if (nf - f > STEP_HEIGHT + 0.1) { this.nearWall[i] = 1; continue; }
          if (dc && dr) {
            // no corner cutting
            const a = this.idx(c + dc, r), b = this.idx(c, r + dr);
            if (!this.walkable(c + dc, r) || !this.walkable(c, r + dr)) continue;
            if (Math.abs(this.floor[a] - f) > STEP_HEIGHT || Math.abs(this.floor[b] - f) > STEP_HEIGHT) continue;
          }
          list.push(j);
        }
      }
      this.navEdges[i] = list;
    }
    this.walkCells = [];
    for (let i = 0; i < W * H; i++) if (this.navEdges[i].length) this.walkCells.push(i);
  }
  nearestWalkable(x, z) {
    let c = this.col(x), r = this.row(z);
    if (this.walkable(c, r) && this.navEdges[this.idx(c, r)].length) return this.idx(c, r);
    for (let rad = 1; rad < 6; rad++) {
      for (let dr = -rad; dr <= rad; dr++) for (let dc = -rad; dc <= rad; dc++) {
        const nc = c + dc, nr = r + dr;
        if (this.walkable(nc, nr) && this.navEdges[this.idx(nc, nr)].length) return this.idx(nc, nr);
      }
    }
    return -1;
  }
  findPath(from, to) {
    const s = this.nearestWalkable(from.x, from.z), g = this.nearestWalkable(to.x, to.z);
    if (s < 0 || g < 0) return null;
    if (s === g) return [new THREE.Vector3(to.x, this.floor[g], to.z)];
    const W = this.W, N = W * this.H;
    const gScore = this._g || (this._g = new Float32Array(N));
    const came = this._came || (this._came = new Int32Array(N));
    const closed = this._closed || (this._closed = new Uint8Array(N));
    gScore.fill(Infinity); came.fill(-1); closed.fill(0);
    const gc = g % W, gr = (g / W) | 0;
    const h = (i) => { const dc = Math.abs(i % W - gc), dr = Math.abs(((i / W) | 0) - gr); return Math.max(dc, dr) + 0.414 * Math.min(dc, dr); };
    const heap = [];
    const push = (i, f) => { heap.push([f, i]); let k = heap.length - 1; while (k > 0) { const p = (k - 1) >> 1; if (heap[p][0] <= heap[k][0]) break; [heap[p], heap[k]] = [heap[k], heap[p]]; k = p; } };
    const pop = () => { const top = heap[0]; const last = heap.pop(); if (heap.length) { heap[0] = last; let k = 0; for (;;) { const l = 2 * k + 1, rr = l + 1; let m = k; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (rr < heap.length && heap[rr][0] < heap[m][0]) m = rr; if (m === k) break; [heap[m], heap[k]] = [heap[k], heap[m]]; k = m; } } return top[1]; };
    gScore[s] = 0; push(s, h(s));
    let found = false, iters = 0;
    while (heap.length && iters++ < 20000) {
      const cur = pop();
      if (cur === g) { found = true; break; }
      if (closed[cur]) continue;
      closed[cur] = 1;
      const cc = cur % W, cr = (cur / W) | 0;
      for (const n of this.navEdges[cur]) {
        if (closed[n]) continue;
        const diag = (n % W !== cc) && (((n / W) | 0) !== cr);
        const cost = (diag ? 1.414 : 1) + (this.nearWall[n] ? 0.6 : 0);
        const ng = gScore[cur] + cost;
        if (ng < gScore[n]) { gScore[n] = ng; came[n] = cur; push(n, ng + h(n)); }
      }
    }
    if (!found) return null;
    const cells = [];
    for (let i = g; i !== -1; i = came[i]) cells.push(i);
    cells.reverse();
    // string pulling
    const pts = cells.map(i => new THREE.Vector3(this.cx(i % W), this.floor[i], this.cz((i / W) | 0)));
    pts[pts.length - 1].x = to.x; pts[pts.length - 1].z = to.z;
    const out = [];
    let a = 0;
    while (a < pts.length - 1) {
      let b = pts.length - 1;
      while (b > a + 1 && !this.walkLine(pts[a], pts[b])) b--;
      out.push(pts[b]);
      a = b;
    }
    return out;
  }
  // straight walk check: every sample's cell must be walkable and height changes step-able
  walkLine(a, b) {
    const dx = b.x - a.x, dz = b.z - a.z, d = Math.hypot(dx, dz);
    const n = Math.ceil(d / 16);
    let prevH = this.floorAt(a.x, a.z);
    for (let k = 1; k <= n; k++) {
      const x = a.x + dx * k / n, z = a.z + dz * k / n;
      for (const [ox, oz] of [[-18, -18], [18, -18], [-18, 18], [18, 18]]) {
        const c = this.col(x + ox), r = this.row(z + oz);
        if (!this.walkable(c, r)) return false;
        const fh = this.floor[this.idx(c, r)];
        if (fh - prevH > STEP_HEIGHT + 0.1 || prevH - fh > 40) return false;
      }
      const hh = this.floorAt(x, z);
      if (Math.abs(hh - prevH) > STEP_HEIGHT + 0.1) return false;
      prevH = hh;
    }
    return true;
  }
  randomWalkPos() {
    const i = this.walkCells[Math.floor(Math.random() * this.walkCells.length)];
    return new THREE.Vector3(this.cx(i % this.W), this.floor[i], this.cz((i / this.W) | 0));
  }

  // ---------------- rendering ----------------
  buildMesh(quality) {
    const group = new THREE.Group();
    const texSand = TX.makeSand(), texTile = TX.makeTile(), texPl = TX.makePlaster(), texPl2 = TX.makePlaster('#cdb38a', 9),
      texBrick = TX.makeBrick(), texWood = TX.makeWood(), texStone = TX.makeStone(), texConc = TX.makeConcrete(),
      texMetal = TX.makeMetal('#3f6f8f'), texMetal2 = TX.makeMetal('#7a3b2c'), texBeam = TX.makeBeam();
    const texs = [texSand, texTile, texPl, texBrick, texWood, texStone, texConc, texMetal, texBeam, texPl2, texMetal2];
    const scales = [1 / 256, 1 / 128, 1 / 192, 1 / 128, 1 / 64, 1 / 128, 1 / 64, 1 / 64, 1 / 64, 1 / 192, 1 / 64];
    const buckets = texs.map(() => ({ pos: [], nrm: [], uv: [], col: [] }));
    const W = this.W, H = this.H;
    const ao = (y, base) => Math.min(1, 0.62 + (y - base) / 90 * 0.38);

    const quad = (m, p, n, uvs, cols) => {
      const b = buckets[m];
      const order = [0, 1, 2, 0, 2, 3];
      for (const k of order) {
        b.pos.push(p[k][0], p[k][1], p[k][2]);
        b.nrm.push(n[0], n[1], n[2]);
        b.uv.push(uvs[k][0], uvs[k][1]);
        b.col.push(cols[k], cols[k], cols[k]);
      }
    };
    const intervals = (i) => {
      const iv = [[BOTTOM, this.floor[i]]];
      if (this.roof[i] !== Infinity) iv.push([this.roof[i], this.roofTop[i]]);
      return iv;
    };
    const subtract = (A, B) => {
      let res = A.map(a => [...a]);
      for (const [b0, b1] of B) {
        const nx = [];
        for (const [a0, a1] of res) {
          if (b1 <= a0 || b0 >= a1) { nx.push([a0, a1]); continue; }
          if (b0 > a0) nx.push([a0, b0]);
          if (b1 < a1) nx.push([b1, a1]);
        }
        res = nx;
      }
      return res.filter(([a, b]) => b - a > 0.01);
    };
    for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
      const i = this.idx(c, r);
      const x0 = this.ox + c * CELL, x1 = x0 + CELL, z0 = this.oz + r * CELL, z1 = z0 + CELL;
      const fl = this.floor[i];
      // skip cells fully surrounded by walls (interior of thick walls)
      const tm = this.topMat[i], sm = this.mat[i];
      // top face
      {
        const s = scales[tm];
        const shade = 1;
        quad(tm, [[x0, fl, z1], [x1, fl, z1], [x1, fl, z0], [x0, fl, z0]], [0, 1, 0],
          [[x0 * s, z1 * s], [x1 * s, z1 * s], [x1 * s, z0 * s], [x0 * s, z0 * s]], [shade, shade, shade, shade]);
      }
      if (this.roof[i] !== Infinity) {
        const s = scales[MAT.BEAM];
        const y = this.roof[i], yt = this.roofTop[i];
        quad(MAT.BEAM, [[x0, y, z0], [x1, y, z0], [x1, y, z1], [x0, y, z1]], [0, -1, 0],
          [[x0 * s, z0 * s], [x1 * s, z0 * s], [x1 * s, z1 * s], [x0 * s, z1 * s]], [0.55, 0.55, 0.55, 0.55]);
        const sp = scales[MAT.PLASTER];
        quad(MAT.PLASTER, [[x0, yt, z1], [x1, yt, z1], [x1, yt, z0], [x0, yt, z0]], [0, 1, 0],
          [[x0 * sp, z1 * sp], [x1 * sp, z1 * sp], [x1 * sp, z0 * sp], [x0 * sp, z0 * sp]], [1, 1, 1, 1]);
      }
      const mine = intervals(i);
      const sides = [
        [c + 1, r, [1, 0, 0], (y0, y1) => [[x1, y0, z1], [x1, y0, z0], [x1, y1, z0], [x1, y1, z1]], z1, z0],
        [c - 1, r, [-1, 0, 0], (y0, y1) => [[x0, y0, z0], [x0, y0, z1], [x0, y1, z1], [x0, y1, z0]], z0, z1],
        [c, r + 1, [0, 0, 1], (y0, y1) => [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]], x0, x1],
        [c, r - 1, [0, 0, -1], (y0, y1) => [[x1, y0, z0], [x0, y0, z0], [x0, y1, z0], [x1, y1, z0]], x1, x0],
      ];
      for (const [nc, nr, n, mk, u0, u1] of sides) {
        if (!this.inside(nc, nr)) continue;
        const j = this.idx(nc, nr);
        const vis = subtract(mine, intervals(j));
        const base = this.floor[j];
        for (const [y0, y1] of vis) {
          const isRoof = this.roof[i] !== Infinity && y0 >= this.roof[i] - 0.1;
          const m = isRoof ? MAT.PLASTER : sm;
          const s = scales[m];
          const ya = Math.max(y0, base), yb = y1;
          if (yb <= ya) continue;
          const sh = n[0] !== 0 ? 0.88 : 1.0;
          const a0 = isRoof ? 0.85 : ao(ya, base) * sh, a1 = isRoof ? 0.85 : ao(yb, base) * sh;
          quad(m, mk(ya, yb), n, [[u0 * s, ya * s], [u1 * s, ya * s], [u1 * s, yb * s], [u0 * s, yb * s]], [a0, a0, a1, a1]);
        }
      }
      // floor AO near walls: darken corners using vertex colors is skipped for simplicity
    }
    buckets.forEach((b, m) => {
      if (!b.pos.length) return;
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(b.pos, 3));
      g.setAttribute('normal', new THREE.Float32BufferAttribute(b.nrm, 3));
      g.setAttribute('uv', new THREE.Float32BufferAttribute(b.uv, 2));
      g.setAttribute('color', new THREE.Float32BufferAttribute(b.col, 3));
      const mat = new THREE.MeshLambertMaterial({ map: texs[m], vertexColors: true });
      const mesh = new THREE.Mesh(g, mat);
      mesh.castShadow = quality !== 'low';
      mesh.receiveShadow = quality !== 'low';
      mesh.matrixAutoUpdate = false;
      group.add(mesh);
    });

    // floor AO strips along wall bases (cheap darkening quads)
    const aoGeo = [];
    for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
      const i = this.idx(c, r);
      if (!this.walkable(c, r)) continue;
      const f = this.floor[i];
      const x0 = this.ox + c * CELL, z0 = this.oz + r * CELL;
      const nb = [[1, 0], [-1, 0], [0, 1], [0, -1]];
      for (const [dc, dr] of nb) {
        const nc = c + dc, nr = r + dr;
        if (!this.inside(nc, nr)) continue;
        if (this.floor[this.idx(nc, nr)] < f + 30) continue;
        const y = f + 0.4, w = 22;
        let pts;
        if (dc === 1) pts = [[x0 + CELL - w, y, z0], [x0 + CELL, y, z0], [x0 + CELL, y, z0 + CELL], [x0 + CELL - w, y, z0 + CELL]];
        else if (dc === -1) pts = [[x0 + w, y, z0 + CELL], [x0, y, z0 + CELL], [x0, y, z0], [x0 + w, y, z0]];
        else if (dr === 1) pts = [[x0 + CELL, y, z0 + CELL - w], [x0 + CELL, y, z0 + CELL], [x0, y, z0 + CELL], [x0, y, z0 + CELL - w]];
        else pts = [[x0, y, z0 + w], [x0, y, z0], [x0 + CELL, y, z0], [x0 + CELL, y, z0 + w]];
        aoGeo.push(pts);
      }
    }
    if (aoGeo.length) {
      const pos = [], al = [];
      for (const p of aoGeo) for (const k of [0, 1, 2, 0, 2, 3]) { pos.push(...p[k]); al.push(k === 1 || k === 2 ? 0.45 : 0); }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('alpha', new THREE.Float32BufferAttribute(al, 1));
      const m = new THREE.ShaderMaterial({
        transparent: true, depthWrite: false,
        vertexShader: 'attribute float alpha; varying float vA; void main(){ vA=alpha; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
        fragmentShader: 'varying float vA; void main(){ gl_FragColor=vec4(0.12,0.08,0.04,vA); }',
      });
      const mesh = new THREE.Mesh(g, m);
      mesh.renderOrder = 1;
      group.add(mesh);
    }

    // wall decals (site letters, arrows)
    for (const d of this.decals) {
      const [text, c, r, face, h, kind] = d;
      const tex = kind === 'arrow' ? TX.makeArrowTexture(text) : TX.makeTextTexture(text, { color: '#8a2016' });
      const w = kind === 'arrow' ? 110 : 120, hh = kind === 'arrow' ? 55 : 120;
      const pl = new THREE.Mesh(new THREE.PlaneGeometry(w, hh), new THREE.MeshLambertMaterial({ map: tex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }));
      const x = this.cx(c), z = this.cz(r);
      const off = CELL / 2 + 0.6;
      if (face === 'e') { pl.position.set(x + off, h, z); pl.rotation.y = Math.PI / 2; }
      if (face === 'w') { pl.position.set(x - off, h, z); pl.rotation.y = -Math.PI / 2; }
      if (face === 's') { pl.position.set(x, h, z + off); }
      if (face === 'n') { pl.position.set(x, h, z - off); pl.rotation.y = Math.PI; }
      group.add(pl);
    }
    // painted bomb-site markers on the floor
    for (const s of ['A', 'B']) {
      const sp = this.spots['plant' + s][0];
      const tex = TX.makeTextTexture(s, { color: '#b03a1c', alpha: 0.55, size: 180 });
      const pl = new THREE.Mesh(new THREE.PlaneGeometry(130, 130), new THREE.MeshLambertMaterial({ map: tex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }));
      pl.rotation.x = -Math.PI / 2;
      pl.position.set(sp.pos.x, this.floorAt(sp.pos.x, sp.pos.z) + 0.5, sp.pos.z);
      group.add(pl);
    }
    this.addProps(group);
    return group;
  }

  addProps(group) {
    // simple decorative props: palm trees, hanging cloth, lamps (non-colliding, outside walkable space or on walls)
    const trunkMat = new THREE.MeshLambertMaterial({ color: 0x6b5236 });
    const leafMat = new THREE.MeshLambertMaterial({ color: 0x4f6b2a, side: THREE.DoubleSide });
    const palm = (x, z, y, h) => {
      const g = new THREE.Group();
      const tr = new THREE.Mesh(new THREE.CylinderGeometry(5, 8, h, 6), trunkMat);
      tr.position.y = h / 2; g.add(tr);
      for (let k = 0; k < 7; k++) {
        const leaf = new THREE.Mesh(new THREE.PlaneGeometry(90, 18), leafMat);
        leaf.position.set(0, h, 0);
        leaf.rotation.set(-0.5, (k / 7) * Math.PI * 2, 0.3);
        leaf.geometry.translate(45, 0, 0);
        g.add(leaf);
      }
      g.position.set(x, y, z);
      group.add(g);
    };
    // palms placed on top of walls (roof gardens) so they never block play
    const palmCells = [[1, 20], [20, 1], [38, 1], [57, 25], [10, 47], [44, 58], [22, 33], [36, 22], [42, 30], [8, 24]];
    for (const [c, r] of palmCells) {
      if (!this.inside(c, r)) continue;
      const i = this.idx(c, r);
      if (this.floor[i] < 200) continue;
      palm(this.cx(c), this.cz(r), this.floor[i], 140 + (c * 7 % 40));
    }
    // cloth awnings above some doorways
    const clothMat = new THREE.MeshLambertMaterial({ color: 0x9a3b2a, side: THREE.DoubleSide });
    const cloth2 = new THREE.MeshLambertMaterial({ color: 0x2c5a7a, side: THREE.DoubleSide });
    const awn = (x, y, z, w, rot, m) => {
      const p = new THREE.Mesh(new THREE.PlaneGeometry(w, 60), m);
      p.position.set(x, y, z); p.rotation.set(-1.1, rot, 0);
      group.add(p);
    };
    awn(this.cx(9), 175, this.cz(32) + 30, 200, 0, clothMat);
    awn(this.cx(28.5), 165, this.cz(23) + 30, 140, 0, cloth2);
    awn(this.cx(49), 190, this.cz(43) + 30, 300, 0, clothMat);
  }

  // top-down image for radar / menu
  renderRadar(scale = 4) {
    const cv = document.createElement('canvas');
    cv.width = this.W * scale; cv.height = this.H * scale;
    const ctx = cv.getContext('2d');
    ctx.fillStyle = 'rgba(0,0,0,0)'; ctx.clearRect(0, 0, cv.width, cv.height);
    for (let r = 0; r < this.H; r++) for (let c = 0; c < this.W; c++) {
      const i = this.idx(c, r);
      const f = this.floor[i];
      if (f >= 200) continue;
      let col;
      if (this.roof[i] !== Infinity) col = '#5d5a52';
      else if (f > 30) col = `rgb(${150 + f * 0.5},${142 + f * 0.4},${120 + f * 0.3})`;
      else col = '#8b8578';
      if (this.topMat[i] === MAT.TILE) col = '#9a8f78';
      if (f > 0 && (this.mat[i] === MAT.WOOD || this.mat[i] === MAT.METAL || this.mat[i] === MAT.METAL2 || this.mat[i] === MAT.CONCRETE)) col = '#6c6558';
      ctx.fillStyle = col;
      ctx.fillRect(c * scale, r * scale, scale, scale);
    }
    // outlines
    ctx.strokeStyle = 'rgba(230,225,210,0.5)';
    ctx.lineWidth = 1;
    for (let r = 0; r < this.H; r++) for (let c = 0; c < this.W; c++) {
      const i = this.idx(c, r);
      if (this.floor[i] >= 200) continue;
      for (const [dc, dr] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nc = c + dc, nr = r + dr;
        if (this.inside(nc, nr) && this.floor[this.idx(nc, nr)] < 200) continue;
        ctx.beginPath();
        const x = c * scale, y = r * scale;
        if (dc === 1) { ctx.moveTo(x + scale, y); ctx.lineTo(x + scale, y + scale); }
        if (dc === -1) { ctx.moveTo(x, y); ctx.lineTo(x, y + scale); }
        if (dr === 1) { ctx.moveTo(x, y + scale); ctx.lineTo(x + scale, y + scale); }
        if (dr === -1) { ctx.moveTo(x, y); ctx.lineTo(x + scale, y); }
        ctx.stroke();
      }
    }
    // site letters
    ctx.font = `bold ${scale * 6}px "Barlow Condensed", Impact, sans-serif`;
    ctx.fillStyle = 'rgba(220,70,50,0.85)';
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    for (const z of this.zones) if (z.type === 'site') ctx.fillText(z.name, ((z.c1 + z.c2 + 1) / 2) * scale, ((z.r1 + z.r2 + 1) / 2) * scale);
    return cv;
  }
}

export function worldToRadar(map, x, z, scale) {
  return [(x - map.ox) / CELL * scale, (z - map.oz) / CELL * scale];
}

export function getMap() { return world.map; }
