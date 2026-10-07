// Visual effects: GPU instanced billboard particles, tracers, bullet-hole decals, lights.
import * as THREE from './vendor/three.module.min.js';
import { world } from './state.js';
import * as TX from './textures.js';

class Particles {
  constructor(capacity, texture, additive) {
    this.cap = capacity;
    this.list = [];
    const base = new THREE.PlaneGeometry(1, 1);
    const g = new THREE.InstancedBufferGeometry();
    g.index = base.index;
    g.setAttribute('position', base.getAttribute('position'));
    g.setAttribute('uv', base.getAttribute('uv'));
    this.aPos = new THREE.InstancedBufferAttribute(new Float32Array(capacity * 3), 3);
    this.aSize = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
    this.aCol = new THREE.InstancedBufferAttribute(new Float32Array(capacity * 4), 4);
    this.aRot = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
    for (const a of [this.aPos, this.aSize, this.aCol, this.aRot]) a.setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('iPos', this.aPos); g.setAttribute('iSize', this.aSize); g.setAttribute('iColor', this.aCol); g.setAttribute('iRot', this.aRot);
    g.instanceCount = 0;
    this.geo = g;
    const mat = new THREE.ShaderMaterial({
      uniforms: { map: { value: texture } },
      vertexShader: `
        attribute vec3 iPos; attribute float iSize; attribute vec4 iColor; attribute float iRot;
        varying vec2 vUv; varying vec4 vColor;
        void main(){
          vUv = uv; vColor = iColor;
          vec4 mv = modelViewMatrix * vec4(iPos, 1.0);
          float c = cos(iRot), s = sin(iRot);
          vec2 p = vec2(c*position.x - s*position.y, s*position.x + c*position.y) * iSize;
          mv.xy += p;
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader: `
        uniform sampler2D map; varying vec2 vUv; varying vec4 vColor;
        void main(){
          vec4 t = texture2D(map, vUv);
          gl_FragColor = vec4(vColor.rgb * t.rgb, vColor.a * t.a);
          if (gl_FragColor.a < 0.004) discard;
        }`,
      transparent: true, depthWrite: false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
    });
    this.mesh = new THREE.Mesh(g, mat);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = additive ? 3 : 2;
  }
  spawn(p) {
    if (this.list.length >= this.cap) {
      // drop oldest non-persistent particle
      const k = this.list.findIndex(q => !q.persist);
      if (k < 0) return null;
      this.list.splice(k, 1);
    }
    const q = Object.assign({ x: 0, y: 0, z: 0, vx: 0, vy: 0, vz: 0, life: 1, age: 0, size: 10, size1: null, r: 1, g: 1, b: 1, a: 1, a1: 0, rot: Math.random() * 6.28, rotV: 0, drag: 0, grav: 0, fn: null, alphaMul: 1 }, p);
    if (q.size1 === null) q.size1 = q.size;
    this.list.push(q);
    return q;
  }
  update(dt) {
    const L = this.list;
    let n = 0;
    const P = this.aPos.array, S = this.aSize.array, C = this.aCol.array, R = this.aRot.array;
    for (let i = 0; i < L.length; i++) {
      const p = L[i];
      p.age += dt;
      if (p.fn) p.fn(p, dt);
      if (p.age >= p.life || p.dead) continue;
      const k = 1 - Math.exp(-p.drag * dt);
      p.vx -= p.vx * k; p.vy -= p.vy * k; p.vz -= p.vz * k;
      p.vy -= p.grav * dt;
      p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
      p.rot += p.rotV * dt;
      const t = p.age / p.life;
      L[n++] = p;
      const j = n - 1;
      P[j * 3] = p.x; P[j * 3 + 1] = p.y; P[j * 3 + 2] = p.z;
      S[j] = p.size + (p.size1 - p.size) * t;
      C[j * 4] = p.r; C[j * 4 + 1] = p.g; C[j * 4 + 2] = p.b; C[j * 4 + 3] = (p.a + (p.a1 - p.a) * t) * p.alphaMul;
      R[j] = p.rot;
    }
    L.length = n;
    this.geo.instanceCount = n;
    this.aPos.needsUpdate = this.aSize.needsUpdate = this.aCol.needsUpdate = this.aRot.needsUpdate = true;
    this.aPos.clearUpdateRanges?.(); this.aSize.clearUpdateRanges?.(); this.aCol.clearUpdateRanges?.(); this.aRot.clearUpdateRanges?.();
    if (n) {
      this.aPos.addUpdateRange?.(0, n * 3); this.aSize.addUpdateRange?.(0, n); this.aCol.addUpdateRange?.(0, n * 4); this.aRot.addUpdateRange?.(0, n);
    }
  }
  clear() { this.list.length = 0; this.geo.instanceCount = 0; }
}

class Tracers {
  constructor(cap = 96) {
    this.cap = cap;
    this.items = [];
    this.pos = new Float32Array(cap * 6);
    this.col = new Float32Array(cap * 6);
    const g = new THREE.BufferGeometry();
    this.aP = new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage);
    this.aC = new THREE.BufferAttribute(this.col, 3).setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('position', this.aP); g.setAttribute('color', this.aC);
    g.setDrawRange(0, 0);
    this.mesh = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
    this.mesh.frustumCulled = false;
    this.geo = g;
  }
  add(a, b, bright = 1) {
    if (this.items.length >= this.cap) this.items.shift();
    const d = a.distanceTo(b);
    if (d < 60) return;
    this.items.push({ ax: a.x, ay: a.y, az: a.z, dx: (b.x - a.x) / d, dy: (b.y - a.y) / d, dz: (b.z - a.z) / d, len: d, t: 0, speed: 7000, bright });
  }
  update(dt) {
    let n = 0;
    for (const it of this.items) {
      it.t += dt;
      const head = Math.min(it.t * it.speed + 40, it.len), tail = Math.max(0, head - 220);
      if (tail >= it.len - 1) { it.done = true; continue; }
      const k = n * 6;
      this.pos[k] = it.ax + it.dx * tail; this.pos[k + 1] = it.ay + it.dy * tail; this.pos[k + 2] = it.az + it.dz * tail;
      this.pos[k + 3] = it.ax + it.dx * head; this.pos[k + 4] = it.ay + it.dy * head; this.pos[k + 5] = it.az + it.dz * head;
      const c = 0.9 * it.bright;
      this.col[k] = 0; this.col[k + 1] = 0; this.col[k + 2] = 0;
      this.col[k + 3] = c; this.col[k + 4] = c * 0.85; this.col[k + 5] = c * 0.5;
      n++;
    }
    this.items = this.items.filter(i => !i.done);
    this.geo.setDrawRange(0, n * 2);
    this.aP.needsUpdate = true; this.aC.needsUpdate = true;
  }
  clear() { this.items.length = 0; this.geo.setDrawRange(0, 0); }
}

class Decals {
  constructor(cap = 300) {
    this.cap = cap; this.i = 0;
    const mat = new THREE.MeshBasicMaterial({ map: TX.makeBulletHole(), transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -4 });
    this.mesh = new THREE.InstancedMesh(new THREE.PlaneGeometry(1, 1), mat, cap);
    this.mesh.count = 0;
    this.mesh.frustumCulled = false;
    this.m = new THREE.Matrix4(); this.q = new THREE.Quaternion(); this.s = new THREE.Vector3(); this.z = new THREE.Vector3(0, 0, 1); this.n = new THREE.Vector3();
  }
  add(x, y, z, nx, ny, nz, size = 4) {
    this.n.set(nx, ny, nz);
    this.q.setFromUnitVectors(this.z, this.n);
    const r = new THREE.Quaternion().setFromAxisAngle(this.z, Math.random() * 6.28);
    this.q.multiply(r);
    this.s.set(size, size, size);
    this.m.compose(new THREE.Vector3(x + nx * 0.2, y + ny * 0.2, z + nz * 0.2), this.q, this.s);
    this.mesh.setMatrixAt(this.i, this.m);
    this.i = (this.i + 1) % this.cap;
    this.mesh.count = Math.min(this.cap, this.mesh.count + 1);
    this.mesh.instanceMatrix.needsUpdate = true;
  }
  clear() { this.mesh.count = 0; this.i = 0; }
}

export const fx = {
  soft: null, smokeTex: null,
  normal: null, additive: null, smoke: null, tracers: null, decals: null,
  light: null, lightT: 0,
  shakeAmt: 0,
  init(scene) {
    this.soft = TX.makeSoftCircle();
    this.smokeTex = TX.makeSmokePuff();
    this.normal = new Particles(1500, this.soft, false);
    this.smoke = new Particles(3000, this.smokeTex, false);
    this.additive = new Particles(1500, this.soft, true);
    this.tracers = new Tracers();
    this.decals = new Decals();
    scene.add(this.decals.mesh, this.normal.mesh, this.smoke.mesh, this.additive.mesh, this.tracers.mesh);
    this.light = new THREE.PointLight(0xffc070, 0, 600, 1.6);
    scene.add(this.light);
  },
  update(dt) {
    this.normal.update(dt); this.smoke.update(dt); this.additive.update(dt); this.tracers.update(dt);
    if (this.lightT > 0) { this.lightT -= dt; this.light.intensity = Math.max(0, this.lightT) * this.lightI; }
    else this.light.intensity = 0;
    this.shakeAmt = Math.max(0, this.shakeAmt - dt * 3);
  },
  clear() { this.normal.clear(); this.smoke.clear(); this.additive.clear(); this.tracers.clear(); this.decals.clear(); },
  flashLight(pos, intensity = 3, dur = 0.05, color = 0xffc070, dist = 600) {
    this.light.position.copy(pos); this.light.color.setHex(color); this.light.distance = dist;
    this.lightI = intensity / dur; this.lightT = dur;
    this.light.intensity = intensity;
  },
  muzzle(pos, dir, big = 1) {
    for (let i = 0; i < 3; i++) {
      const d = 4 + i * 5 * big;
      this.additive.spawn({ x: pos.x + dir.x * d, y: pos.y + dir.y * d, z: pos.z + dir.z * d, life: 0.05, size: (9 - i * 2) * big, size1: (12 - i * 2) * big, r: 1, g: 0.75, b: 0.35, a: 0.9, a1: 0 });
    }
  },
  impact(h, matSound) {
    // dust puff
    const n = matSound === 'metal' ? 0 : 5;
    for (let i = 0; i < n; i++) {
      this.normal.spawn({ x: h.x, y: h.y, z: h.z, vx: (h.nx + (Math.random() - 0.5) * 0.8) * 60, vy: (h.ny + Math.random() * 0.6) * 60, vz: (h.nz + (Math.random() - 0.5) * 0.8) * 60, life: 0.6 + Math.random() * 0.4, size: 3, size1: 14, r: 0.62, g: 0.55, b: 0.45, a: 0.55, a1: 0, drag: 3, grav: -10 });
    }
    if (matSound === 'metal' || matSound === 'stone') {
      for (let i = 0; i < 4; i++) {
        this.additive.spawn({ x: h.x, y: h.y, z: h.z, vx: (h.nx + (Math.random() - 0.5)) * 300, vy: (h.ny + Math.random()) * 250, vz: (h.nz + (Math.random() - 0.5)) * 300, life: 0.18, size: 1.4, size1: 0.6, r: 1, g: 0.8, b: 0.4, a: 1, a1: 0, grav: 600 });
      }
    }
    if (matSound === 'wood') {
      for (let i = 0; i < 4; i++) this.normal.spawn({ x: h.x, y: h.y, z: h.z, vx: (h.nx + (Math.random() - 0.5)) * 120, vy: Math.random() * 120, vz: (h.nz + (Math.random() - 0.5)) * 120, life: 0.6, size: 1.6, r: 0.45, g: 0.32, b: 0.18, a: 1, a1: 1, grav: 500 });
    }
    this.decals.add(h.x, h.y, h.z, h.nx, h.ny, h.nz, 3 + Math.random() * 1.5);
  },
  blood(pos, dir, head) {
    const n = head ? 10 : 6;
    for (let i = 0; i < n; i++) {
      this.normal.spawn({ x: pos.x, y: pos.y, z: pos.z, vx: (dir.x + (Math.random() - 0.5) * 0.8) * 90, vy: (dir.y + Math.random() * 0.5) * 90, vz: (dir.z + (Math.random() - 0.5) * 0.8) * 90, life: 0.45, size: 3, size1: 9, r: 0.45, g: 0.02, b: 0.02, a: 0.85, a1: 0, grav: 200, drag: 2 });
    }
    if (head) this.normal.spawn({ x: pos.x, y: pos.y, z: pos.z, life: 0.3, size: 6, size1: 22, r: 0.5, g: 0.03, b: 0.03, a: 0.7, a1: 0 });
  },
  explosion(pos, scale = 1) {
    for (let i = 0; i < 26 * scale; i++) {
      const a = Math.random() * 6.28, e = Math.random() * 1.2, s = (120 + Math.random() * 260) * scale;
      this.additive.spawn({ x: pos.x, y: pos.y + 10, z: pos.z, vx: Math.cos(a) * Math.cos(e) * s, vy: Math.sin(e) * s, vz: Math.sin(a) * Math.cos(e) * s, life: 0.35 + Math.random() * 0.3, size: 30 * scale, size1: 70 * scale, r: 1, g: 0.55 + Math.random() * 0.2, b: 0.2, a: 1, a1: 0, drag: 4 });
    }
    for (let i = 0; i < 16 * scale; i++) {
      const a = Math.random() * 6.28, s = (60 + Math.random() * 140) * scale;
      this.normal.spawn({ x: pos.x, y: pos.y + 20, z: pos.z, vx: Math.cos(a) * s, vy: 40 + Math.random() * 80, vz: Math.sin(a) * s, life: 2 + Math.random() * 1.5, size: 40 * scale, size1: 110 * scale, r: 0.25, g: 0.23, b: 0.21, a: 0.6, a1: 0, drag: 1.5 });
    }
    for (let i = 0; i < 20; i++) {
      this.additive.spawn({ x: pos.x, y: pos.y + 5, z: pos.z, vx: (Math.random() - 0.5) * 900, vy: Math.random() * 600, vz: (Math.random() - 0.5) * 900, life: 0.6, size: 2, size1: 1, r: 1, g: 0.7, b: 0.3, a: 1, a1: 0, grav: 800 });
    }
    this.flashLight(pos, 6 * scale, 0.35, 0xffa040, 900 * scale);
    this.decals.add(pos.x, world.map.floorAt(pos.x, pos.z) + 0.3, pos.z, 0, 1, 0, 70 * scale);
  },
  flashbang(pos) {
    this.additive.spawn({ x: pos.x, y: pos.y, z: pos.z, life: 0.25, size: 60, size1: 260, r: 1, g: 1, b: 1, a: 1, a1: 0 });
    for (let i = 0; i < 12; i++) this.additive.spawn({ x: pos.x, y: pos.y, z: pos.z, vx: (Math.random() - 0.5) * 500, vy: (Math.random() - 0.5) * 500, vz: (Math.random() - 0.5) * 500, life: 0.4, size: 3, size1: 1, r: 1, g: 1, b: 0.9, a: 1, a1: 0 });
    this.flashLight(pos, 8, 0.2, 0xffffff, 1200);
  },
  spark(pos) {
    for (let i = 0; i < 6; i++) this.additive.spawn({ x: pos.x, y: pos.y, z: pos.z, vx: (Math.random() - 0.5) * 300, vy: Math.random() * 300, vz: (Math.random() - 0.5) * 300, life: 0.25, size: 2, size1: 0.5, r: 0.6, g: 0.8, b: 1, a: 1, a1: 0, grav: 600 });
  },
  shake(a) { this.shakeAmt = Math.min(2.5, this.shakeAmt + a); },
};
