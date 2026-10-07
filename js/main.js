// Bootstrap: renderer, scenes, lighting, sky, main loop.
import * as THREE from './vendor/three.module.min.js';
import { world, runTimers } from './state.js';
import { settings } from './settings.js';
import { GameMap } from './map.js';
import { fx } from './effects.js';
import { hud } from './hud.js';
import { player } from './player.js';
import { ui } from './ui.js';
import { menu } from './menu.js';
import { game } from './game.js';
import { audio } from './audio.js';
import { updateProjectiles } from './grenades.js';
import { updateItems } from './items.js';

const canvas = document.getElementById('gl');
let renderer, sun, mapGroup;

function makeRenderer() {
  const q = settings.graphics;
  if (renderer) { renderer.dispose(); }
  renderer = new THREE.WebGLRenderer({ canvas, antialias: q !== 'low', powerPreference: 'high-performance' });
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  renderer.autoClear = false;
  world.renderer = renderer;
  applyGraphics();
}

function applyGraphics() {
  const q = settings.graphics;
  const dpr = window.devicePixelRatio || 1;
  renderer.setPixelRatio(q === 'low' ? Math.min(dpr, 1) * 0.7 : q === 'medium' ? Math.min(dpr, 1) : Math.min(dpr, 1.5));
  renderer.shadowMap.enabled = q !== 'low';
  renderer.shadowMap.type = q === 'high' ? THREE.PCFSoftShadowMap : THREE.PCFShadowMap;
  if (sun) {
    sun.castShadow = q !== 'low';
    const s = q === 'high' ? 4096 : 2048;
    if (sun.shadow.mapSize.x !== s) { sun.shadow.mapSize.set(s, s); if (sun.shadow.map) { sun.shadow.map.dispose(); sun.shadow.map = null; } }
  }
  if (mapGroup) mapGroup.traverse(o => { if (o.isMesh && o.material && o.material.map) { o.receiveShadow = q !== 'low'; o.castShadow = q !== 'low'; o.material.needsUpdate = true; } });
  world.scene?.traverse(o => { if (o.material && o.material.isMeshLambertMaterial) o.material.needsUpdate = true; });
  resize();
}
world.applyGraphics = applyGraphics;

function makeSky(scene) {
  const geo = new THREE.SphereGeometry(20000, 24, 12);
  const mat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { sunDir: { value: new THREE.Vector3(0.45, 0.7, 0.35).normalize() } },
    vertexShader: 'varying vec3 vDir; void main(){ vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: `uniform vec3 sunDir; varying vec3 vDir;
      void main(){
        float h = clamp(vDir.y, -0.2, 1.0);
        vec3 horizon = vec3(0.86, 0.80, 0.70);
        vec3 zenith = vec3(0.34, 0.55, 0.85);
        vec3 col = mix(horizon, zenith, pow(max(h,0.0), 0.55));
        float s = max(dot(normalize(vDir), sunDir), 0.0);
        col += vec3(1.0,0.9,0.7) * pow(s, 600.0) * 2.0 + vec3(1.0,0.8,0.5) * pow(s, 12.0) * 0.25;
        if (h < 0.0) col = mix(horizon, vec3(0.55,0.47,0.36), clamp(-h*6.0,0.0,1.0));
        gl_FragColor = vec4(col, 1.0);
      }`,
  });
  const sky = new THREE.Mesh(geo, mat);
  sky.renderOrder = -10;
  scene.add(sky);
  // distant dunes / town silhouette ring so the horizon isn't empty
  const ring = new THREE.Group();
  const duneMat = new THREE.MeshLambertMaterial({ color: 0xb59a72 });
  for (let i = 0; i < 26; i++) {
    const a = (i / 26) * Math.PI * 2;
    const r = 5200 + (i % 3) * 600;
    const h = 300 + ((i * 37) % 5) * 120;
    const m = new THREE.Mesh(new THREE.ConeGeometry(1600, h, 6), duneMat);
    m.position.set(Math.cos(a) * r, h / 2 - 40, Math.sin(a) * r);
    ring.add(m);
  }
  const bMat = new THREE.MeshLambertMaterial({ color: 0xcdb48c });
  for (let i = 0; i < 40; i++) {
    const a = (i / 40) * Math.PI * 2 + 0.05;
    const r = 3000 + (i % 4) * 220;
    const w = 220 + (i * 53 % 200), h = 300 + (i * 71 % 260);
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, w * 0.8), bMat);
    m.position.set(Math.cos(a) * r, h / 2, Math.sin(a) * r);
    m.rotation.y = a;
    ring.add(m);
  }
  scene.add(ring);
}

function setupScene() {
  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0xd8ccb4, 3500, 11000);
  world.scene = scene;
  const camera = new THREE.PerspectiveCamera(73.74, innerWidth / innerHeight, 2, 30000);
  world.camera = camera;
  scene.add(camera);
  const hemi = new THREE.HemisphereLight(0xdfe9ff, 0x9a7d58, 1.25);
  scene.add(hemi);
  sun = new THREE.DirectionalLight(0xfff0d8, 2.4);
  sun.position.set(1800, 2800, 1400);
  sun.target.position.set(0, 0, 0);
  sun.castShadow = settings.graphics !== 'low';
  const sc = sun.shadow.camera;
  sc.left = -2300; sc.right = 2300; sc.top = 2300; sc.bottom = -2300; sc.near = 100; sc.far = 7000;
  sun.shadow.mapSize.set(settings.graphics === 'high' ? 4096 : 2048, settings.graphics === 'high' ? 4096 : 2048);
  sun.shadow.bias = -0.0006; sun.shadow.normalBias = 1.5;
  scene.add(sun, sun.target);
  makeSky(scene);
  // viewmodel scene
  const vmScene = new THREE.Scene();
  const vmCam = new THREE.PerspectiveCamera(54, innerWidth / innerHeight, 0.5, 200);
  vmScene.add(new THREE.HemisphereLight(0xe8eeff, 0x7a6a50, 1.6));
  const vl = new THREE.DirectionalLight(0xfff0d8, 1.8); vl.position.set(3, 6, 4); vmScene.add(vl);
  world.vmScene = vmScene; world.vmCamera = vmCam;
}

function resize() {
  if (!renderer) return;
  renderer.setSize(innerWidth, innerHeight, false);
  if (world.camera) { world.camera.aspect = innerWidth / innerHeight; world.camera.updateProjectionMatrix(); }
  if (world.vmCamera) { world.vmCamera.aspect = innerWidth / innerHeight; world.vmCamera.updateProjectionMatrix(); }
}

function simStep(dt) {
  world.time += dt;
  runTimers();
  player.applyLook();
  game.update(dt);
  updateProjectiles(dt);
  updateItems(dt);
  for (const c of world.chars) c.updateModel(dt);
  fx.update(dt);
  player.updateCamera(dt);
  hud.update(dt);
}

let last = performance.now();
let menuAngle = 0;
function frame(now) {
  requestAnimationFrame(frame);
  let dt = (now - last) / 1000;
  last = now;
  if (dt > 0.05) dt = 0.05;
  if (dt <= 0) dt = 0.001;
  world.dt = dt;
  try {
    if (world.running && !world.paused) {
      simStep(dt);
    } else if (world.running && world.paused) {
      player.applyLook();
      hud.update(0.0001);
    } else {
      // menu background: slow orbit over the map
      menuAngle += dt * 0.04;
      const cam = world.camera;
      cam.position.set(Math.cos(menuAngle) * 2600, 1300, Math.sin(menuAngle) * 2600);
      cam.lookAt(0, 0, 0);
      fx.update(dt);
    }
  } catch (e) {
    console.error(e);
  }
  renderer.clear();
  renderer.render(world.scene, world.camera);
  if (world.running && player.vm && player.vm.root.visible && (world.local?.alive || world.spectating)) {
    renderer.clearDepth();
    renderer.render(world.vmScene, world.vmCamera);
  }
}

function boot() {
  setupScene();
  makeRenderer();
  const map = new GameMap();
  world.map = map;
  mapGroup = map.buildMesh(settings.graphics);
  world.scene.add(mapGroup);
  fx.init(world.scene);
  hud.init(); hud.setMap(map);
  world.hud = hud;
  world.ui = ui;
  world.menu = menu;
  world.game = game;
  world.player = player;
  player.init(canvas);
  ui.init();
  menu.init();
  menu.show();
  window.addEventListener('resize', resize);
  // audio needs a user gesture
  const unlock = async () => { await audio.init(); audio.resume(); audio.setVolume(settings.volume); };
  document.addEventListener('pointerdown', unlock, { once: false });
  document.addEventListener('keydown', unlock, { once: true });
  resize();
  requestAnimationFrame(frame);
  // debug handle for automated testing
  window.__ws = { world, game, player, hud, settings, simStep };
}

boot();
