// AI 팀 사무실 — 3D 사무실 (three.js)
// 창가가 있는 따뜻한 사무실, ㄴ자 칸막이 책상 3개, 휴게실, 수면실, 회의 공간.
// 캐릭터는 관절이 있는 사람 비율 모델(앉기·타이핑·걷기·생각하기·딴짓·졸기·눕기)이고, 외부 모델(GLB/GLTF/FBX/VRM)로 바꿀 수 있다.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { FBXLoader } from 'three/addons/loaders/FBXLoader.js';

const host = document.getElementById('officeScene');
try { start(); }
catch (err) {
  console.error(err);
  host.innerHTML = '<p style="padding:16px;color:#aab">이 PC에서는 3D 화면을 열 수 없어요(그래픽 가속 꺼짐). 아래 팀원 카드로 진행 상황을 볼 수 있어요.</p>';
}

function start() {
  // ───────────── 기본 설정 ─────────────
  const AGENTS = [
    { id: 'claude', name: '클로드', color: '#d97757' },
    { id: 'codex', name: '코덱스', color: '#3fb68b' },
    { id: 'gemini', name: '제미나이', color: '#5b8def' },
  ];
  const DEFAULT_PROFILES = {
    claude: { nick: '클로드', hair: 'long', hairColor: '#8a4a2c', skin: '#f1c7a5', outfit: 'shirt_tie', top: '#f4f1ea', accent: '#1f2a44', bottom: '#2b2f3a', shoes: '#2a211d', build: 1, height: 100, glasses: false, headset: false, model: null, modelRot: 180, modelY: 0, modelScale: 100, clip: '' },
    codex: { nick: '코덱스', hair: 'bob', hairColor: '#2c2420', skin: '#e9b892', outfit: 'blazer', top: '#2f6b57', accent: '#f4f1ea', bottom: '#3a3f4b', shoes: '#1c1c1f', build: 1, height: 98, glasses: true, headset: false, model: null, modelRot: 180, modelY: 0, modelScale: 100, clip: '' },
    gemini: { nick: '제미나이', hair: 'curly', hairColor: '#5a3a26', skin: '#d9a47e', outfit: 'knit', top: '#5b7fd1', accent: '#ffffff', bottom: '#4a4038', shoes: '#3b2a20', build: 1, height: 101, glasses: false, headset: true, model: null, modelRot: 180, modelY: 0, modelScale: 100, clip: '' },
  };
  const DEFAULT_ROOM = { light: 'day', floor: 'wood', wall: '#e9dfcf', labels: true, bubbles: true, motion: true, shadows: true };
  const load = (k, d) => { try { return { ...d, ...JSON.parse(localStorage.getItem(k) || '{}') }; } catch { return { ...d }; } };
  const room = load('office-room2', DEFAULT_ROOM);
  const saveRoom = () => localStorage.setItem('office-room2', JSON.stringify(room));
  const rand = (a, b) => a + Math.random() * (b - a);
  const pick = arr => arr[Math.floor(Math.random() * arr.length)];
  const v3 = (x, z) => new THREE.Vector3(x, 0, z);

  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  renderer.shadowMap.enabled = room.shadows;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  host.prepend(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color('#cfe3ec');
  const camera = new THREE.PerspectiveCamera(36, 2, 0.05, 120);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.maxPolarAngle = Math.PI * 0.47;
  controls.minDistance = 2.5;
  controls.maxDistance = 30;

  const hemi = new THREE.HemisphereLight('#fff8ee', '#b9a58c', 1.2);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight('#fff1d6', 2.4);
  sun.position.set(-6, 11, 7);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  Object.assign(sun.shadow.camera, { left: -11, right: 11, top: 9, bottom: -9, near: 1, far: 40 });
  sun.shadow.bias = -0.0004;
  sun.shadow.normalBias = 0.02;
  scene.add(sun);
  const windowLight = new THREE.DirectionalLight('#dff0ff', 0.7);
  windowLight.position.set(0, 4, -12);
  scene.add(windowLight);

  // ───────────── 재질·도형 도우미 (같은 도형·재질은 한 번만 만들어 재사용) ─────────────
  const matCache = new Map();
  function mat(color, o = {}) {
    const cacheable = !o.unique && !o.map;
    const key = cacheable ? color + JSON.stringify(o) : '';
    if (cacheable && matCache.has(key)) return matCache.get(key);
    const m = new THREE.MeshStandardMaterial({ color, roughness: o.rough ?? 0.75, metalness: o.metal ?? 0, ...(o.map ? { map: o.map } : {}),
      ...(o.transparent ? { transparent: true, opacity: o.opacity } : {}), ...(o.emissive ? { emissive: o.emissive, emissiveIntensity: o.ei ?? 1 } : {}), side: o.side ?? THREE.FrontSide });
    if (cacheable) matCache.set(key, m);
    return m;
  }
  const geoCache = new Map();
  function cgeo(key, make) {
    let g = geoCache.get(key);
    if (!g) { g = make(); g.userData.cached = true; geoCache.set(key, g); }
    return g;
  }
  function mesh(geo, material, parent, x = 0, y = 0, z = 0, shadow = true) {
    const m = new THREE.Mesh(geo, material);
    m.position.set(x, y, z);
    m.castShadow = shadow; m.receiveShadow = true;
    if (parent) parent.add(m);
    return m;
  }
  const M = m => typeof m === 'string' ? mat(m) : m;
  const rbox = (w, h, d, r = 0.02) => cgeo(`b${w},${h},${d},${r}`, () => new RoundedBoxGeometry(w, h, d, 3, Math.min(r, w / 2, h / 2, d / 2)));
  const planeGeo = (w, h) => cgeo(`p${w},${h}`, () => new THREE.PlaneGeometry(w, h));
  const box = (parent, w, h, d, x, y, z, m, r = 0.015) => mesh(rbox(w, h, d, r), M(m), parent, x, y, z);
  const cyl = (parent, rt, rb, h, x, y, z, m, seg = 20) => mesh(cgeo(`c${rt},${rb},${h},${seg}`, () => new THREE.CylinderGeometry(rt, rb, h, seg)), M(m), parent, x, y, z);
  const sph = (parent, r, x, y, z, m, seg = 20) => mesh(cgeo(`s${r},${seg}`, () => new THREE.SphereGeometry(r, seg, Math.round(seg * 0.7))), M(m), parent, x, y, z);
  const cap = (parent, r, len, x, y, z, m) => mesh(cgeo(`k${r},${len}`, () => new THREE.CapsuleGeometry(r, len, 6, 16)), M(m), parent, x, y, z);
  const torus = (parent, r, t, arc, x, y, z, m, seg = 24) => mesh(cgeo(`t${r},${t},${arc},${seg}`, () => new THREE.TorusGeometry(r, t, 8, seg, arc)), M(m), parent, x, y, z);

  function canvasTex(w, h, draw, repeat) {
    const c = document.createElement('canvas'); c.width = w; c.height = h;
    draw(c.getContext('2d'), w, h);
    const t = new THREE.CanvasTexture(c);
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = 8;
    if (repeat) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(repeat[0], repeat[1]); }
    return t;
  }
  const woodTex = canvasTex(512, 512, (g, w, h) => {
    const rows = 8;
    for (let r = 0; r < rows; r++) {
      let x = -((r * 137) % 220);
      while (x < w) {
        const len = 180 + ((x * 7 + r * 31) % 140);
        const l = 62 + ((x + r * 53) % 9) - 4;
        g.fillStyle = `hsl(30, 42%, ${l}%)`;
        g.fillRect(x, r * h / rows, len, h / rows);
        g.strokeStyle = 'rgba(90,55,30,.10)'; g.lineWidth = 1;
        for (let k = 0; k < 5; k++) { g.beginPath(); const yy = r * h / rows + 6 + k * 11 + ((x + k) % 5); g.moveTo(x, yy); g.bezierCurveTo(x + len / 3, yy + 3, x + len * 2 / 3, yy - 3, x + len, yy); g.stroke(); }
        g.fillStyle = 'rgba(70,40,20,.35)'; g.fillRect(x, r * h / rows, 2, h / rows);
        x += len;
      }
      g.fillStyle = 'rgba(70,40,20,.30)'; g.fillRect(0, r * h / rows, w, 2);
    }
  }, [5, 5]);
  const carpetTex = canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = '#8a9a8e'; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 6000; i++) { g.fillStyle = `rgba(${Math.random() < .5 ? '255,255,255' : '30,40,35'},${Math.random() * .06})`; g.fillRect(Math.random() * w, Math.random() * h, 2, 2); }
  }, [6, 6]);
  const skyTex = canvasTex(64, 256, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, '#8cc4ea'); gr.addColorStop(0.7, '#d9eef7'); gr.addColorStop(1, '#f4ead8');
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  });

  // ───────────── 방 ─────────────
  const X0 = -7, X1 = 7, Z0 = -5, Z1 = 4.6, H = 3.0;
  const floorMat = mat('#ffffff', { map: room.floor === 'carpet' ? carpetTex : woodTex, rough: 0.6, unique: true });
  const floor = mesh(new THREE.BoxGeometry(X1 - X0, 0.12, Z1 - Z0), floorMat, scene, 0, -0.06, (Z0 + Z1) / 2, false);
  floor.receiveShadow = true;
  box(scene, X1 - X0 + 0.02, 0.5, Z1 - Z0 + 0.02, 0, -0.38, (Z0 + Z1) / 2, '#7a5a40', 0.01); // 바닥 단면

  const wallMat = mat(room.wall, { unique: true, rough: 0.9 });
  const wallParts = [];
  const wall = (w, h, d, x, y, z) => { const m = box(scene, w, h, d, x, y, z, wallMat, 0.005); wallParts.push(m); return m; };
  // 뒷벽 (창문 5개)
  wall(X1 - X0, 0.85, 0.16, 0, 0.425, Z0);
  wall(X1 - X0, 0.45, 0.16, 0, H - 0.225, Z0);
  const winW = 2.0, gap = (X1 - X0 - winW * 5) / 6;
  const glassMat = mat('#bfe3f5', { transparent: true, opacity: 0.25, rough: 0.05, metal: 0.1, unique: true });
  const frameMat = mat('#f3ede3');
  for (let i = 0; i <= 5; i++) wall(gap, H - 1.3, 0.16, X0 + gap / 2 + i * (gap + winW), 0.85 + (H - 1.3) / 2, Z0);
  for (let i = 0; i < 5; i++) {
    const cx = X0 + gap + winW / 2 + i * (gap + winW);
    mesh(new THREE.BoxGeometry(winW, H - 1.3, 0.02), glassMat, scene, cx, 0.85 + (H - 1.3) / 2, Z0, false);
    box(scene, winW, 0.06, 0.2, cx, 0.88, Z0 + 0.03, frameMat);
    box(scene, winW, 0.06, 0.12, cx, H - 0.48, Z0, frameMat);
    box(scene, 0.05, H - 1.3, 0.12, cx, 0.85 + (H - 1.3) / 2, Z0, frameMat);
    box(scene, winW, 0.04, 0.12, cx, 0.85 + (H - 1.3) * 0.62, Z0, frameMat);
    box(scene, winW + 0.1, 0.05, 0.3, cx, 0.86, Z0 + 0.14, '#efe6d8'); // 창턱
  }
  // 바깥 풍경
  const skyMat = new THREE.MeshBasicMaterial({ map: skyTex });
  mesh(new THREE.PlaneGeometry(40, 12), skyMat, scene, 0, 4, -14, false);
  const groundMat = mat('#9cbf8a', { unique: true });
  mesh(new THREE.PlaneGeometry(40, 10), groundMat, scene, 0, -0.4, -9.5, false).rotation.x = -Math.PI / 2;
  for (let i = 0; i < 16; i++) {
    const tx = -15 + i * 2 + ((i * 37) % 10) / 10, tz = -9 - (i % 3) * 1.4;
    cyl(scene, 0.08, 0.12, 1.2, tx, 0.4, tz, '#6b4f3a', 8);
    sph(scene, 0.9 + (i % 3) * 0.25, tx, 1.7 + (i % 2) * 0.3, tz, ['#5f8f55', '#6fa063', '#4f7e4a'][i % 3], 12);
  }
  // 왼쪽 벽
  wall(0.16, H, Z1 - Z0, X0, H / 2, (Z0 + Z1) / 2);
  box(scene, 0.04, 0.12, Z1 - Z0, X0 + 0.1, 0.06, (Z0 + Z1) / 2, '#cbb79c'); // 걸레받이
  box(scene, X1 - X0, 0.12, 0.04, 0, 0.06, Z0 + 0.1, '#cbb79c');
  // 문 (사람이 지나가면 열린다)
  const DOOR = v3(-6.7, 3.5);
  mesh(planeGeo(0.96, 2.1), mat('#3a332d'), scene, X0 + 0.09, 1.05, 3.5, false).rotation.y = Math.PI / 2;
  box(scene, 0.1, 0.08, 1.14, X0 + 0.1, 2.19, 3.5, '#7d5b40');
  for (const s of [-1, 1]) box(scene, 0.1, 2.2, 0.06, X0 + 0.1, 1.1, 3.5 + s * 0.54, '#7d5b40');
  const doorPivot = new THREE.Group(); doorPivot.position.set(X0 + 0.12, 0, 4.0); scene.add(doorPivot);
  box(doorPivot, 0.06, 2.12, 0.98, 0, 1.07, -0.49, '#9c7350');
  sph(doorPivot, 0.035, 0.05, 1.05, -0.85, '#c9b27a');
  // 화이트보드
  box(scene, 0.05, 1.05, 1.9, X0 + 0.11, 1.65, -2.6, '#d9d9d9');
  box(scene, 0.03, 0.95, 1.8, X0 + 0.14, 1.65, -2.6, '#fbfbfb');
  const boardTex = canvasTex(512, 256, (g) => {
    g.fillStyle = '#fbfbfb'; g.fillRect(0, 0, 512, 256);
    g.strokeStyle = '#3f6fd8'; g.lineWidth = 4; g.font = 'bold 34px sans-serif'; g.fillStyle = '#3f6fd8';
    g.fillText('이번 주 목표', 24, 52);
    g.fillStyle = '#d0573c'; g.font = '26px sans-serif';
    ['① 업무 나누기', '② 동시에 만들기', '③ 꼼꼼히 검토'].forEach((t, i) => g.fillText(t, 36, 100 + i * 42));
    g.strokeStyle = '#2e9d6a'; g.beginPath(); g.moveTo(330, 200); g.lineTo(380, 120); g.lineTo(430, 160); g.lineTo(480, 70); g.stroke();
  });
  const boardFace = mesh(new THREE.PlaneGeometry(1.75, 0.9), new THREE.MeshStandardMaterial({ map: boardTex, roughness: 0.4 }), scene, X0 + 0.16, 1.65, -2.6, false);
  boardFace.rotation.y = Math.PI / 2;
  // 시계
  const clock = cyl(scene, 0.22, 0.22, 0.05, 0, 2.45, Z0 + 0.1, '#ffffff', 32); clock.rotation.x = Math.PI / 2;
  const handH = new THREE.Group(), handM = new THREE.Group();
  for (const [g, len, wd, dz] of [[handH, 0.12, 0.02, 0.14], [handM, 0.18, 0.015, 0.145]]) { g.position.set(0, 2.45, Z0 + dz); scene.add(g); box(g, wd, len, 0.01, 0, len / 2, 0, '#222', 0.004); }

  // ───────────── 가구 ─────────────
  function plant(x, z, s = 1, pot = '#c98b62', parent = scene) {
    const g = new THREE.Group(); g.position.set(x, 0, z); g.scale.setScalar(s); parent.add(g);
    cyl(g, 0.2, 0.15, 0.36, 0, 0.18, 0, pot, 20);
    cyl(g, 0.19, 0.19, 0.02, 0, 0.35, 0, '#4a3628', 20);
    for (let i = 0; i < 11; i++) {
      const a = i * 2.4, r = 0.05 + (i % 3) * 0.06;
      const leaf = sph(g, 0.12, Math.cos(a) * r, 0.55 + (i % 4) * 0.14, Math.sin(a) * r, ['#3f7a4d', '#4f8f5a', '#5ea068'][i % 3], 10);
      leaf.scale.set(0.55, 1.5, 0.3); leaf.rotation.set(Math.sin(a) * 0.6, a, Math.cos(a) * 0.6);
    }
    return g;
  }
  function chair(parent, x, z, rot = 0, color = '#39424a') {
    const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rot; parent.add(g);
    for (let i = 0; i < 5; i++) { const a = i * Math.PI * 2 / 5; const leg = box(g, 0.05, 0.03, 0.3, Math.sin(a) * 0.14, 0.07, Math.cos(a) * 0.14, '#2b2f33'); leg.rotation.y = a; sph(g, 0.03, Math.sin(a) * 0.28, 0.03, Math.cos(a) * 0.28, '#1d1f22', 8); }
    cyl(g, 0.03, 0.03, 0.35, 0, 0.25, 0, '#8d9399', 12);
    box(g, 0.5, 0.08, 0.48, 0, 0.46, 0, color, 0.04);
    const back = box(g, 0.48, 0.55, 0.07, 0, 0.82, 0.24, color, 0.04); back.rotation.x = -0.12;
    for (const s of [-1, 1]) { box(g, 0.04, 0.2, 0.04, s * 0.25, 0.58, 0.02, '#2b2f33'); box(g, 0.07, 0.03, 0.28, s * 0.25, 0.68, 0.0, '#2b2f33'); }
    return g;
  }
  // ㄴ자 책상 상판: 모서리를 둥글린 다각형을 베벨(둥근 테두리)로 밀어낸다. 몸이 닿는 앞쪽 테두리가 둥글다.
  function roundedShape(pts, radii) {
    const s = new THREE.Shape(), n = pts.length;
    for (let i = 0; i < n; i++) {
      const p = pts[i], a = pts[(i + n - 1) % n], b = pts[(i + 1) % n], r = radii[i];
      const la = Math.hypot(a[0] - p[0], a[1] - p[1]), lb = Math.hypot(b[0] - p[0], b[1] - p[1]);
      const p1 = [p[0] + (a[0] - p[0]) / la * r, p[1] + (a[1] - p[1]) / la * r], p2 = [p[0] + (b[0] - p[0]) / lb * r, p[1] + (b[1] - p[1]) / lb * r];
      if (i === 0) s.moveTo(p1[0], p1[1]); else s.lineTo(p1[0], p1[1]);
      s.quadraticCurveTo(p[0], p[1], p2[0], p2[1]);
    }
    s.closePath();
    return s;
  }
  // 책상 기준 좌표: 본체 x -0.83~0.83, z -0.40~0.31 / 날개(오른쪽) x 0.47~0.83, z 0.31~1.02 (도형 y = -z)
  const lTopGeo = new THREE.ExtrudeGeometry(roundedShape(
    [[-0.83, 0.40], [0.83, 0.40], [0.83, -1.02], [0.47, -1.02], [0.47, -0.31], [-0.83, -0.31]],
    [0.04, 0.04, 0.09, 0.09, 0.12, 0.09]), { depth: 0.02, bevelEnabled: true, bevelThickness: 0.02, bevelSize: 0.024, bevelSegments: 5, curveSegments: 10 });
  lTopGeo.userData.cached = true;
  const TOP_Y = 0.775;
  const xrayMats = new Set([wallMat]);
  function desk(x, z, rot, accent, withPerson, id) {
    const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rot; scene.add(g);
    const top = mesh(lTopGeo, mat('#e8d3b2', { rough: 0.55 }), g, 0, 0.735, 0); top.rotation.x = -Math.PI / 2;
    box(g, 0.04, 0.7, 0.7, -0.8, 0.36, -0.05, '#d9d4cc', 0.01);          // 왼쪽 다리판
    box(g, 0.04, 0.7, 1.4, 0.81, 0.36, 0.31, '#d9d4cc', 0.01);           // 오른쪽 다리판 (날개까지)
    box(g, 1.58, 0.38, 0.02, 0, 0.52, -0.37, '#d9d4cc', 0.005);          // 앞가림판
    box(g, 0.3, 0.55, 0.5, 0.64, 0.3, 0.72, '#e9e5de');                  // 날개 아래 서랍
    for (let i = 0; i < 3; i++) box(g, 0.2, 0.006, 0.01, 0.64, 0.15 + i * 0.17, 0.47, '#b9b2a6', 0.002).rotation.y = Math.PI / 2;
    const partMat = mat('#a7b7ad', { unique: true, rough: 0.95 });
    xrayMats.add(partMat);
    box(g, 1.75, 1.18, 0.06, 0, 0.59, -0.47, partMat, 0.02);
    box(g, 1.77, 0.04, 0.08, 0, 1.19, -0.47, '#d6d0c6');
    box(g, 0.06, 1.18, 0.92, -0.9, 0.59, -0.02, partMat, 0.02);
    box(g, 0.08, 0.04, 0.94, -0.9, 1.19, -0.02, '#d6d0c6');
    // 모니터 2대
    const screens = [];
    for (const [mx, ry] of [[-0.3, 0.18], [0.32, -0.18]]) {
      const m = new THREE.Group(); m.position.set(mx, 0.765, -0.2); m.rotation.y = ry; g.add(m);
      box(m, 0.22, 0.015, 0.16, 0, 0.008, 0, '#30353a');
      box(m, 0.04, 0.24, 0.03, 0, 0.13, 0.02, '#30353a');
      box(m, 0.56, 0.34, 0.03, 0, 0.36, 0, '#22262a', 0.01);
      if (withPerson) {
        const c = document.createElement('canvas'); c.width = 256; c.height = 160;
        const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace;
        const scr = mesh(planeGeo(0.52, 0.3), new THREE.MeshStandardMaterial({ map: tex, emissive: '#ffffff', emissiveMap: tex, emissiveIntensity: 0.55, roughness: 0.3 }), m, 0, 0.36, 0.017, false);
        screens.push({ canvas: c, tex, mesh: scr });
      } else mesh(planeGeo(0.52, 0.3), mat('#1a1e22', { rough: 0.3 }), m, 0, 0.36, 0.017, false);
    }
    const kb = box(g, 0.44, 0.02, 0.15, -0.05, TOP_Y, 0.12, id === 'codex' ? '#2a2d33' : '#dcdcdc'); // 키보드
    for (let r = 0; r < 4; r++) box(g, 0.4, 0.006, 0.025, -0.05, 0.788, 0.07 + r * 0.033, id === 'codex' ? '#3b3f47' : '#bdbdbd', 0.002);
    box(g, 0.06, 0.02, 0.1, 0.3, TOP_Y, 0.14, '#dcdcdc', 0.01); // 마우스
    box(g, 0.22, 0.012, 0.3, 0.6, TOP_Y, -0.05, '#fafafa', 0.002).rotation.y = 0.2; // 서류
    // 스탠드
    cyl(g, 0.07, 0.08, 0.02, -0.66, TOP_Y, -0.25, '#2f3337');
    const arm = cyl(g, 0.012, 0.012, 0.42, -0.62, 0.98, -0.22, '#2f3337', 8); arm.rotation.z = -0.25;
    const shadeM = cyl(g, 0.03, 0.09, 0.1, -0.55, 1.18, -0.18, '#2f3337', 16); shadeM.rotation.z = 0.5;
    let lamp = null, seat = null, cup = null, sign = null, glow = null;
    if (withPerson) {
      lamp = new THREE.PointLight('#ffd7a0', 0, 2.2, 2); lamp.position.set(-0.5, 1.05, -0.12); g.add(lamp);
      seat = chair(g, 0, 0.62, 0);
      ({ cup, glow } = deskClutter(g, id, accent));
      sign = makeSign(g);
    } else {
      cyl(g, 0.04, 0.035, 0.1, -0.62, 0.825, 0.1, accent, 16); // 머그
      const pc = plant(0.65, 0.7, 0.35, '#c98b62', g); pc.position.y = TOP_Y;
      chair(g, 0.1, 0.72, 0.4);
    }
    return { group: g, screens, lamp, seat, cup, sign, glow, keyboard: kb };
  }

  // 책상 위 소품 — 각자 취향대로
  function deskClutter(g, id, accent) {
    let cup = null, glow = null;
    const cupG = new THREE.Group(); cupG.position.set(-0.6, TOP_Y, 0.12); g.add(cupG);
    if (id === 'claude') {
      // 차곡차곡 쌓인 책
      [[0.25, 0.045, 0.18, '#7a4b3a', 0], [0.23, 0.04, 0.17, '#3d5f7a', 0.16], [0.21, 0.035, 0.16, '#c9a85a', -0.12], [0.2, 0.03, 0.15, '#5c8a5a', 0.3]]
        .reduce((y, [w, h, d, c, r]) => { box(g, w, h, d, 0.65, y + h / 2, 0.84, c, 0.006).rotation.y = r; return y + h; }, TOP_Y);
      // 찻잔과 받침
      cyl(g, 0.065, 0.06, 0.008, -0.6, TOP_Y + 0.004, 0.12, '#f4efe6', 20);
      cup = new THREE.Group(); cupG.add(cup);
      cyl(cup, 0.04, 0.03, 0.06, 0, 0.038, 0, '#f4efe6', 18);
      cyl(cup, 0.036, 0.036, 0.004, 0, 0.064, 0, '#a5662f', 16);
      torus(cup, 0.018, 0.005, Math.PI * 1.4, 0.045, 0.04, 0, '#f4efe6', 10).rotation.z = -Math.PI * 0.7;
      cyl(cup, 0.0015, 0.0015, 0.07, 0.03, 0.07, 0.0, '#e9e0c8', 4).rotation.z = 0.5; // 티백 줄
      // 포스트잇
      [['#ffe680', 0.5, 1.02, 0.05], ['#ffb3c7', 0.6, 0.96, -0.08], ['#b9f0c8', 0.7, 1.05, 0.12], ['#ffe680', 0.62, 1.1, -0.04]]
        .forEach(([c, sx, sy, r]) => { const n = box(g, 0.075, 0.075, 0.004, sx, sy, -0.437, c, 0.002); n.rotation.z = r; n.castShadow = false; });
      // 다육이
      cyl(g, 0.05, 0.038, 0.07, 0.68, TOP_Y + 0.035, 0.5, '#c97f5a', 14);
      cyl(g, 0.046, 0.046, 0.006, 0.68, TOP_Y + 0.07, 0.5, '#4a3628', 14);
      for (let i = 0; i < 7; i++) { const a = i * 0.9; const l = sph(g, 0.022, 0.68 + Math.cos(a) * 0.022, TOP_Y + 0.09 + (i % 2) * 0.01, 0.5 + Math.sin(a) * 0.022, '#7fb08a', 8); l.scale.set(0.8, 1.3, 0.8); l.rotation.set(Math.sin(a) * 0.5, 0, Math.cos(a) * 0.5); }
    } else if (id === 'codex') {
      // 에너지 드링크 캔들
      cyl(g, 0.033, 0.033, 0.12, 0.58, TOP_Y + 0.06, 0.56, '#2fd07f', 14); cyl(g, 0.03, 0.03, 0.006, 0.58, TOP_Y + 0.123, 0.56, '#c8ccd0', 14);
      cyl(g, 0.033, 0.033, 0.12, 0.69, TOP_Y + 0.06, 0.62, '#2b3f99', 14); cyl(g, 0.03, 0.03, 0.006, 0.69, TOP_Y + 0.123, 0.62, '#c8ccd0', 14);
      const lying = cyl(g, 0.033, 0.033, 0.12, 0.66, TOP_Y + 0.033, 0.8, '#2fd07f', 14); lying.rotation.set(0, 0.6, Math.PI / 2);
      cup = new THREE.Group(); cupG.add(cup);
      cyl(cup, 0.033, 0.033, 0.12, 0, 0.06, 0, '#b7f23a', 14); cyl(cup, 0.03, 0.03, 0.006, 0, 0.123, 0, '#c8ccd0', 14);
      box(cup, 0.003, 0.05, 0.03, 0, 0.06, 0.03, '#1b1d22', 0.001);
      // 고무 오리
      const duck = new THREE.Group(); duck.position.set(0.63, TOP_Y, -0.26); duck.rotation.y = -0.5; g.add(duck);
      sph(duck, 0.045, 0, 0.04, 0, '#ffd43b', 14).scale.set(1, 0.8, 1.25);
      sph(duck, 0.03, 0, 0.09, 0.03, '#ffd43b', 12);
      box(duck, 0.03, 0.01, 0.025, 0, 0.085, 0.062, '#ff8a1f', 0.004);
      for (const s of [-1, 1]) sph(duck, 0.005, s * 0.015, 0.098, 0.055, '#111', 6);
      // RGB 키보드 빛
      glow = new THREE.MeshBasicMaterial({ color: '#ff3cac', transparent: true, opacity: 0.8 });
      mesh(rbox(0.47, 0.004, 0.18, 0.002), glow, g, -0.05, TOP_Y - 0.008, 0.12, false);
      // 작은 로봇 피규어
      const fig = new THREE.Group(); fig.position.set(0.68, TOP_Y, 0.95); fig.rotation.y = -0.4; g.add(fig);
      box(fig, 0.05, 0.06, 0.035, 0, 0.05, 0, '#8d99a6', 0.008);
      box(fig, 0.045, 0.04, 0.04, 0, 0.1, 0, '#c9d2db', 0.01);
      for (const s of [-1, 1]) { sph(fig, 0.006, s * 0.011, 0.103, 0.021, mat('#3cf0ff', { emissive: '#3cf0ff', ei: 1.2 }), 6); box(fig, 0.014, 0.04, 0.014, s * 0.014, 0.01, 0, '#6b7680', 0.004); }
      cyl(fig, 0.002, 0.002, 0.03, 0, 0.135, 0, '#6b7680', 4); sph(fig, 0.006, 0, 0.152, 0, '#ff5a5a', 6);
    } else if (id === 'gemini') {
      // 과자 봉지와 쿠키
      const bag = box(g, 0.13, 0.17, 0.045, 0.6, TOP_Y + 0.06, 0.62, '#e8433a', 0.02); bag.rotation.set(-1.15, 0.3, 0);
      const stripe = box(g, 0.135, 0.04, 0.048, 0.6, TOP_Y + 0.064, 0.62, '#f6c945', 0.01); stripe.rotation.set(-1.15, 0.3, 0);
      cyl(g, 0.07, 0.06, 0.012, 0.7, TOP_Y + 0.006, 0.88, '#f4efe6', 20);
      for (let i = 0; i < 3; i++) cyl(g, 0.025, 0.025, 0.008, 0.68 + (i % 2) * 0.03, TOP_Y + 0.016 + i * 0.008, 0.87 + i * 0.012, '#b9783d', 12);
      // 헤드폰 거치대
      cyl(g, 0.05, 0.06, 0.015, 0.68, TOP_Y + 0.008, 0.3, '#2b2b2e', 16);
      cyl(g, 0.008, 0.008, 0.22, 0.68, TOP_Y + 0.12, 0.3, '#2b2b2e', 8);
      const hp = torus(g, 0.07, 0.01, Math.PI, 0.68, TOP_Y + 0.17, 0.3, '#f0f0f2', 20); hp.rotation.y = Math.PI / 2;
      for (const s of [-1, 1]) { const c = cyl(g, 0.035, 0.035, 0.03, 0.68, TOP_Y + 0.17, 0.3 + s * 0.07, '#f0f0f2', 14); c.rotation.x = Math.PI / 2; }
      // 작은 지구본
      const globeTex = canvasTex(128, 64, (c2, w, h) => {
        c2.fillStyle = '#3d7fd1'; c2.fillRect(0, 0, w, h); c2.fillStyle = '#5fae5a';
        [[20, 22, 14, 10], [36, 40, 8, 12], [70, 20, 18, 9], [78, 38, 9, 10], [104, 46, 10, 6]].forEach(([x, y, rx, ry]) => { c2.beginPath(); c2.ellipse(x, y, rx, ry, 0.3, 0, Math.PI * 2); c2.fill(); });
      });
      const gl = new THREE.Group(); gl.position.set(0.66, TOP_Y, -0.27); g.add(gl);
      cyl(gl, 0.035, 0.045, 0.012, 0, 0.006, 0, '#6b4f3a', 14);
      cyl(gl, 0.005, 0.005, 0.05, 0, 0.035, 0, '#6b4f3a', 6);
      mesh(cgeo('globe', () => new THREE.SphereGeometry(0.06, 20, 14)), mat('#ffffff', { map: globeTex, rough: 0.5 }), gl, 0, 0.12, 0).rotation.z = 0.4;
      torus(gl, 0.068, 0.004, Math.PI * 1.2, 0, 0.12, 0, '#c9a85a', 20).rotation.set(0, Math.PI / 2, 0.4 - Math.PI * 0.1);
      // 카메라
      const cam = new THREE.Group(); cam.position.set(0.63, TOP_Y, 0.97); cam.rotation.y = -0.9; g.add(cam);
      box(cam, 0.11, 0.07, 0.055, 0, 0.035, 0, '#26282c', 0.012);
      const lens = cyl(cam, 0.024, 0.026, 0.04, 0, 0.035, 0.045, '#1a1b1e', 16); lens.rotation.x = Math.PI / 2;
      cyl(cam, 0.016, 0.016, 0.004, 0, 0.035, 0.066, mat('#5a7fb0', { rough: 0.1, metal: 0.4 }), 12).rotation.x = Math.PI / 2;
      box(cam, 0.02, 0.012, 0.02, 0.035, 0.074, 0, '#c9c9c9', 0.003);
      // 커피 머그
      cup = new THREE.Group(); cupG.add(cup);
      cyl(cup, 0.04, 0.036, 0.09, 0, 0.045, 0, accent, 16);
      cyl(cup, 0.036, 0.036, 0.004, 0, 0.087, 0, '#4a2e1c', 14);
      torus(cup, 0.022, 0.006, Math.PI * 1.3, 0.045, 0.048, 0, accent, 10).rotation.z = -Math.PI * 0.65;
    }
    return { cup, glow };
  }

  // 자리 비움 팻말 (책상 위 작은 스탠드)
  function makeSign(g) {
    const c = document.createElement('canvas'); c.width = 320; c.height = 200;
    const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace;
    const grp = new THREE.Group(); grp.position.set(-0.05, TOP_Y, 0.25); grp.scale.setScalar(1.45); g.add(grp);
    const face = mesh(planeGeo(0.32, 0.2), new THREE.MeshStandardMaterial({ map: tex, roughness: 0.6, emissive: '#ffffff', emissiveMap: tex, emissiveIntensity: 0.4 }), grp, 0, 0.1, 0, false);
    face.rotation.x = -0.28;
    const back = box(grp, 0.33, 0.21, 0.01, 0, 0.1, -0.035, '#efe8dc', 0.004); back.rotation.x = 0.22; back.castShadow = false;
    box(grp, 0.34, 0.01, 0.1, 0, 0.005, -0.02, '#c9b89c', 0.004);
    grp.visible = false;
    return { group: grp, canvas: c, tex, key: '' };
  }

  // 작업 공간: 사람 책상 3개 + 빈 책상 2개
  const DESKS = [[-4.7, -2.1], [-2.35, -2.1], [0.0, -2.1]];
  const desks = DESKS.map(([x, z], i) => desk(x, z, 0, AGENTS[i].color, true, AGENTS[i].id));
  desk(-4.7, 1.0, Math.PI, '#d6b25e', false);
  desk(-2.35, 1.0, Math.PI, '#8d7bd6', false);
  const rug = mesh(new THREE.BoxGeometry(7.6, 0.012, 2.6), mat('#ffffff', { map: carpetTex, rough: 1 }), scene, -2.35, 0.006, -1.95, false); rug.receiveShadow = true;

  // 회의 공간 (앞쪽 가운데로 옮김)
  const MTX = 0.3, MTZ = 3.0;
  cyl(scene, 0.75, 0.75, 0.05, MTX, 0.74, MTZ, '#e9dcc5', 32);
  cyl(scene, 0.06, 0.06, 0.72, MTX, 0.37, MTZ, '#55595e');
  cyl(scene, 0.32, 0.32, 0.03, MTX, 0.015, MTZ, '#55595e');
  for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2 + 0.4; chair(scene, MTX + Math.sin(a) * 1.05, MTZ + Math.cos(a) * 1.05, a, '#c07a55'); }

  // 휴게실 (오른쪽 뒤)
  const LX = 4.75, LZ = -2.9;
  const lrug = mesh(new THREE.CylinderGeometry(1.7, 1.7, 0.012, 48), mat('#d9c7a8', { rough: 1 }), scene, LX, 0.006, LZ + 0.3, false); lrug.scale.z = 0.8;
  const sofaMat = mat('#c96f4f', { rough: 0.95 });
  box(scene, 2.9, 0.42, 0.85, LX, 0.21, -4.35, sofaMat, 0.08);
  box(scene, 2.9, 0.6, 0.25, LX, 0.6, -4.68, sofaMat, 0.1);
  for (const s of [-1, 1]) box(scene, 0.25, 0.6, 0.85, LX + s * 1.45, 0.3, -4.35, sofaMat, 0.1);
  for (let i = 0; i < 3; i++) box(scene, 0.88, 0.14, 0.75, LX - 0.92 + i * 0.92, 0.48, -4.3, mat('#d98463'), 0.06);
  box(scene, 0.35, 0.3, 0.12, LX - 1.0, 0.68, -4.5, '#f1d9a8', 0.05).rotation.z = 0.15; // 쿠션
  box(scene, 0.35, 0.3, 0.12, LX + 1.0, 0.68, -4.5, '#8fb3a5', 0.05).rotation.z = -0.15;
  box(scene, 1.3, 0.05, 0.6, LX, 0.42, -3.1, '#9c7350', 0.02);
  for (const [dx, dz] of [[-0.58, -0.25], [0.58, -0.25], [-0.58, 0.25], [0.58, 0.25]]) cyl(scene, 0.025, 0.025, 0.4, LX + dx, 0.2, -3.1 + dz, '#3b3b3b', 8);
  cyl(scene, 0.06, 0.05, 0.1, LX - 0.3, 0.5, -3.1, '#ffffff'); cyl(scene, 0.05, 0.05, 0.08, LX + 0.25, 0.49, -3.0, '#4c7a9c');
  box(scene, 0.28, 0.03, 0.2, LX + 0.1, 0.46, -3.25, '#e6dccb'); // 잡지
  // 커피 바
  box(scene, 0.6, 0.95, 2.2, 6.55, 0.475, -0.6, '#efe8dc', 0.02);
  box(scene, 0.66, 0.05, 2.26, 6.55, 0.97, -0.6, '#5c5148', 0.01);
  box(scene, 0.32, 0.38, 0.32, 6.6, 1.19, -1.2, '#2e2e2e', 0.03);
  cyl(scene, 0.04, 0.04, 0.08, 6.5, 1.04, -1.18, '#fff');
  for (let i = 0; i < 4; i++) cyl(scene, 0.035, 0.03, 0.08, 6.5 + (i % 2) * 0.1, 1.04, -0.3 + i * 0.12, ['#d97757', '#3fb68b', '#5b8def', '#f1d9a8'][i]);
  // 정수기
  box(scene, 0.35, 1.0, 0.35, 6.55, 0.5, -4.55, '#e5e8ea', 0.04);
  cyl(scene, 0.16, 0.16, 0.42, 6.55, 1.21, -4.55, mat('#a8d7f0', { transparent: true, opacity: 0.6, unique: true }));
  // 책장
  box(scene, 0.4, 1.9, 1.6, X0 + 0.32, 0.95, 1.3, '#b98a5e', 0.02);
  for (let s = 0; s < 4; s++) {
    box(scene, 0.38, 0.03, 1.56, X0 + 0.34, 0.25 + s * 0.45, 1.3, '#a77a50', 0.005);
    for (let b = 0; b < 9; b++) box(scene, 0.25, 0.3 - (b % 3) * 0.04, 0.12, X0 + 0.36, 0.42 + s * 0.45 - (b % 3) * 0.02, 0.68 + b * 0.15, ['#c45a4a', '#3d6f8f', '#e0b85a', '#5c8a5a', '#7a5ea8'][(b + s) % 5], 0.01);
  }
  // 화분들
  plant(X0 + 0.5, Z0 + 0.5, 1.3); plant(2.75, Z0 + 0.45, 1.1, '#d8d2c8'); plant(6.55, 0.82, 1.0); plant(-3.3, 4.15, 1.1, '#e0d6c8'); plant(2.4, 1.5, 0.9);
  // 파일 캐비닛
  for (let i = 0; i < 2; i++) { box(scene, 0.5, 1.1, 0.6, 1.55 + i * 0.52, 0.55, Z0 + 0.42, '#c7cbcf', 0.02); for (let d = 0; d < 3; d++) box(scene, 0.12, 0.02, 0.02, 1.55 + i * 0.52, 0.3 + d * 0.33, Z0 + 0.73, '#7d8287', 0.005); }

  // ───────────── 수면실 (오른쪽 앞) ─────────────
  const NX0 = 2.8, NZ0 = 1.2, NGAP0 = 3.35, NGAP1 = 4.25;
  const napRug = mesh(new THREE.BoxGeometry(X1 - NX0 - 0.12, 0.012, Z1 - NZ0 - 0.06), mat('#8d96b8', { map: carpetTex, rough: 1, unique: true }), scene, (NX0 + X1) / 2, 0.007, (NZ0 + Z1) / 2, false);
  napRug.receiveShadow = true;
  const napGlass = mat('#dce8f0', { transparent: true, opacity: 0.32, rough: 0.15, unique: true });
  const napWood = '#b48a62';
  function napPanel(x0, z0, x1, z1) {
    const len = Math.hypot(x1 - x0, z1 - z0), cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, ry = Math.atan2(x1 - x0, z1 - z0) - Math.PI / 2;
    const g = new THREE.Group(); g.position.set(cx, 0, cz); g.rotation.y = ry; scene.add(g);
    box(g, len, 0.28, 0.06, 0, 0.14, 0, napWood, 0.01);
    mesh(new THREE.BoxGeometry(len, 1.12, 0.02), napGlass, g, 0, 0.84, 0, false);
    box(g, len + 0.04, 0.05, 0.08, 0, 1.42, 0, napWood, 0.01);
    for (const s of [-1, 1]) box(g, 0.06, 1.44, 0.08, s * len / 2, 0.72, 0, napWood, 0.01);
    return g;
  }
  napPanel(NX0, NZ0, NGAP0, NZ0);
  napPanel(NGAP1, NZ0, X1 - 0.07, NZ0);
  napPanel(NX0, NZ0, NX0, Z1 - 0.05);
  // 표지판
  const napSignTex = canvasTex(512, 192, (g, w, h) => {
    g.fillStyle = '#2f3550'; rr(g, 0, 0, w, h, 34); g.fill();
    g.fillStyle = '#ffe9b0'; g.font = 'bold 74px "Malgun Gothic", sans-serif'; g.textAlign = 'center'; g.fillText('🌙 수면실', w / 2, 88);
    g.fillStyle = '#c9d0ee'; g.font = '40px "Malgun Gothic", sans-serif'; g.fillText('쉿, 자는 중이에요', w / 2, 150);
  });
  const napSign = mesh(planeGeo(0.95, 0.36), new THREE.MeshStandardMaterial({ map: napSignTex, roughness: 0.6, emissive: '#ffffff', emissiveMap: napSignTex, emissiveIntensity: 0.3 }), scene, 5.4, 1.15, NZ0 - 0.045, false);
  napSign.rotation.y = Math.PI;
  const shhTex = canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = '#ffd36b'; g.beginPath(); g.arc(w / 2, h / 2, 120, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#5a3d12'; g.font = 'bold 96px "Malgun Gothic", sans-serif'; g.textAlign = 'center'; g.fillText('쉿!', w / 2, 120);
    g.font = '64px sans-serif'; g.fillText('🤫', w / 2, 205);
  });
  const shh = mesh(planeGeo(0.36, 0.36), new THREE.MeshStandardMaterial({ map: shhTex, transparent: true, roughness: 0.6, emissive: '#ffffff', emissiveMap: shhTex, emissiveIntensity: 0.25 }), scene, NX0 - 0.045, 1.1, 3.4, false);
  shh.rotation.y = -Math.PI / 2;
  // 침대 3개 (머리는 오른쪽 벽)
  const BED_COLORS = ['#f2cdbb', '#c4e8d6', '#cbd8f6'];
  const BEDS = [1.95, 2.98, 4.01].map((bz, i) => {
    const g = new THREE.Group(); g.position.set(5.92, 0, bz); scene.add(g);
    box(g, 1.95, 0.3, 0.9, 0, 0.2, 0, '#9a7556', 0.04);
    for (const [lx, lz] of [[-0.9, -0.38], [0.9, -0.38], [-0.9, 0.38], [0.9, 0.38]]) box(g, 0.07, 0.06, 0.07, lx, 0.03, lz, '#6f5440', 0.01);
    box(g, 1.86, 0.16, 0.84, -0.01, 0.43, 0, '#f6f3ee', 0.06);
    box(g, 0.06, 0.78, 0.9, 0.96, 0.52, 0, '#9a7556', 0.03);
    box(g, 0.34, 0.1, 0.58, 0.66, 0.56, 0, '#ffffff', 0.045).rotation.z = 0.12;
    const blanket = box(g, 1.2, 0.16, 0.88, 0, 0, 0, BED_COLORS[i], 0.06);
    const b = { z: bz, foot: v3(4.98, bz), approach: v3(4.5, bz), blanket, used: false };
    foldBlanket(b);
    return b;
  });
  function foldBlanket(b) { b.used = false; b.blanket.scale.set(0.3, 0.75, 1.0); b.blanket.position.set(-0.68, 0.57, 0); }
  function spreadBlanket(b) { b.used = true; b.blanket.scale.set(1.1, 1, 1); b.blanket.position.set(-0.3, 0.665, 0); }
  // 은은한 스탠드 조명
  cyl(scene, 0.15, 0.17, 0.03, 3.2, 0.015, 4.25, '#3a3530', 18);
  cyl(scene, 0.012, 0.012, 1.4, 3.2, 0.72, 4.25, '#3a3530', 8);
  cyl(scene, 0.13, 0.2, 0.24, 3.2, 1.48, 4.25, mat('#ffe3b8', { emissive: '#ffb36b', ei: 0.6 }), 20);
  const napLamp = new THREE.PointLight('#ffb36b', 1.0, 4.5, 2); napLamp.position.set(3.25, 1.45, 4.15); scene.add(napLamp);
  // 작은 협탁과 시계
  box(scene, 0.4, 0.45, 0.35, 3.25, 0.225, 1.6, '#9a7556', 0.03);
  cyl(scene, 0.05, 0.05, 0.03, 3.25, 0.465, 1.6, '#e9e3d6', 14);
  plant(3.3, 3.0, 0.7, '#d8d2c8');

  // ───────────── 캐릭터 (관절 모델) ─────────────
  function buildPerson(p) {
    const skin = mat(p.skin, { unique: true, rough: 0.7 }), top = mat(p.top, { unique: true, rough: 0.85 }), accent = mat(p.accent, { unique: true });
    const bottom = mat(p.bottom, { unique: true }), shoes = mat(p.shoes, { unique: true, rough: 0.5 }), hairM = mat(p.hairColor, { unique: true, rough: 0.9, side: THREE.DoubleSide });
    const white = mat('#ffffff'), dark = mat('#1d1a1a'), lip = mat('#b5655c');
    const b = Number(p.build) || 1;
    const rig = {};
    const body = new THREE.Group(); rig.body = body;
    const hips = new THREE.Group(); hips.position.y = 0.93; body.add(hips); rig.hips = hips;
    box(hips, 0.3 * b, 0.16, 0.2, 0, 0, 0, bottom, 0.06);
    box(hips, 0.31 * b, 0.035, 0.21, 0, 0.075, 0, '#2a2522', 0.01);
    const spine = new THREE.Group(); spine.position.y = 0.06; hips.add(spine); rig.spine = spine;
    const torso = cap(spine, 0.15, 0.3, 0, 0.27, 0, top); torso.scale.set(1.12 * b, 1, 0.72);
    const chest = cap(spine, 0.14, 0.12, 0, 0.4, -0.005, top); chest.scale.set(1.22 * b, 0.8, 0.74);
    // 옷 디테일
    if (p.outfit === 'shirt_tie' || p.outfit === 'blazer') {
      const tie = box(spine, 0.045, 0.27, 0.012, 0, 0.36, -0.112, accent, 0.005); tie.rotation.x = -0.05;
      box(spine, 0.05, 0.035, 0.02, 0, 0.5, -0.104, accent, 0.008);
      for (const s of [-1, 1]) { const col = box(spine, 0.07, 0.03, 0.06, s * 0.045, 0.53, -0.08, p.outfit === 'blazer' ? white : top, 0.01); col.rotation.z = s * 0.5; }
    }
    if (p.outfit === 'blazer') {
      box(spine, 0.1, 0.24, 0.01, 0, 0.4, -0.108, white, 0.004);
      for (const s of [-1, 1]) { const lap = box(spine, 0.06, 0.24, 0.015, s * 0.07, 0.42, -0.106, top, 0.005); lap.rotation.z = s * 0.25; }
      for (let i = 0; i < 2; i++) sph(spine, 0.01, 0.0, 0.2 + i * 0.07, -0.112, '#d8c9a0', 8);
    }
    if (p.outfit === 'hoodie') {
      const hood = mesh(new THREE.TorusGeometry(0.11, 0.045, 10, 24, Math.PI * 1.2), top, spine, 0, 0.53, 0.04); hood.rotation.set(Math.PI / 2 + 0.3, 0, Math.PI * 0.9);
      box(spine, 0.2, 0.09, 0.02, 0, 0.17, -0.105, mat(shade(p.top, -18)), 0.01);
      for (const s of [-1, 1]) cyl(spine, 0.004, 0.004, 0.12, s * 0.03, 0.44, -0.11, accent, 6);
    }
    if (p.outfit === 'knit') { for (let i = 0; i < 6; i++) box(spine, 0.27 * b, 0.006, 0.01, 0, 0.14 + i * 0.05, -0.108, mat(shade(p.top, -10)), 0.002); box(spine, 0.12, 0.03, 0.02, 0, 0.53, -0.085, white, 0.01); }
    // 목·머리
    cyl(spine, 0.043, 0.05, 0.07, 0, 0.565, 0, skin, 14);
    const head = new THREE.Group(); head.position.y = 0.585; spine.add(head); rig.head = head;
    const skull = sph(head, 0.105, 0, 0.11, 0, skin, 28); skull.scale.set(0.94, 1.1, 1.0);
    const jaw = sph(head, 0.085, 0, 0.055, -0.012, skin, 20); jaw.scale.set(0.95, 0.85, 1.0);
    rig.eyes = [];
    for (const s of [-1, 1]) {
      const eye = sph(head, 0.017, s * 0.037, 0.115, -0.093, white, 12); eye.scale.z = 0.5;
      const pupil = sph(head, 0.0105, s * 0.037, 0.115, -0.1, mat('#3b2a20'), 10);
      const glint = sph(head, 0.004, s * 0.034, 0.119, -0.108, white, 6);
      // 눈 감은 모습(졸 때)
      const lid = box(head, 0.036, 0.004, 0.006, s * 0.037, 0.113, -0.104, mat('#5a3a2a'), 0.002); lid.visible = false;
      rig.eyes.push({ open: [eye, pupil, glint], lid });
      const brow = box(head, 0.036, 0.007, 0.01, s * 0.037, 0.148, -0.098, hairM, 0.003); brow.rotation.z = s * -0.12;
      sph(head, 0.022, s * 0.1, 0.105, 0.005, skin, 12).scale.set(0.5, 1, 0.8);
      sph(head, 0.016, s * 0.055, 0.075, -0.088, mat('#f0a0a0', { transparent: true, opacity: 0.35 }), 10).scale.z = 0.3;
    }
    sph(head, 0.013, 0, 0.09, -0.104, skin, 10).scale.set(0.9, 1.1, 1);
    const mouth = box(head, 0.032, 0.006, 0.008, 0, 0.055, -0.094, lip, 0.003); rig.mouth = mouth;
    // 머리카락
    const hair = new THREE.Group(); head.add(hair);
    const capGeo = (r, t0, t1, p0 = 0, pl = Math.PI * 2) => cgeo(`h${r},${t0},${t1},${p0},${pl}`, () => new THREE.SphereGeometry(r, 32, 18, p0, pl, t0, t1));
    const hcap = mesh(capGeo(0.114, 0, 1.2), hairM, hair, 0, 0.122, 0.004); hcap.scale.set(0.97, 1.08, 1.04);
    const back = (len) => { const m = mesh(capGeo(0.118, 0.45, len, 0, Math.PI), hairM, hair, 0, 0.115, 0.004); m.scale.set(0.97, 1.08, 1.05); return m; };
    if (p.hair === 'short') back(1.55);
    if (p.hair === 'bob' || p.hair === 'long') { const m = mesh(capGeo(0.124, 0.35, 1.95, -0.42, Math.PI + 0.84), hairM, hair, 0, 0.11, 0.006); m.scale.set(0.98, 1.08, 1.05); }
    if (p.hair === 'long') {
      box(hair, 0.23, 0.34, 0.05, 0, -0.09, 0.09, hairM, 0.025).rotation.x = 0.12;
      for (const s of [-1, 1]) box(hair, 0.045, 0.26, 0.05, s * 0.1, -0.05, 0.0, hairM, 0.02);
    }
    if (p.hair === 'ponytail') { back(1.5); const t = cap(hair, 0.035, 0.18, 0, 0.03, 0.16, hairM); t.rotation.x = 0.5; sph(hair, 0.025, 0, 0.12, 0.13, accent, 10); }
    if (p.hair === 'bun') { back(1.5); sph(hair, 0.055, 0, 0.215, 0.07, hairM, 16); }
    if (p.hair === 'curly') {
      back(1.65);
      for (let i = 0; i < 46; i++) {
        const th = 0.2 + (i % 7) * 0.24, ph = i * 2.39996;
        if (Math.sin(ph) < -0.35 && th > 0.9) continue; // 얼굴 앞은 비움
        const r = 0.123;
        sph(hair, 0.034, -r * Math.cos(ph) * Math.sin(th), 0.12 + r * Math.cos(th) * 1.08, r * Math.sin(ph) * Math.sin(th) + 0.005, hairM, 8);
      }
    }
    if (p.glasses) {
      for (const s of [-1, 1]) torus(head, 0.026, 0.0035, Math.PI * 2, s * 0.038, 0.115, -0.108, '#222', 24).castShadow = false;
      box(head, 0.025, 0.004, 0.004, 0, 0.118, -0.11, '#222', 0.001);
      for (const s of [-1, 1]) box(head, 0.004, 0.004, 0.1, s * 0.064, 0.118, -0.06, '#222', 0.001);
    }
    if (p.headset) {
      torus(head, 0.122, 0.009, Math.PI, 0, 0.11, 0.0, '#2b2b2e', 32).castShadow = false;
      for (const s of [-1, 1]) { const cupM = cyl(head, 0.035, 0.035, 0.025, s * 0.115, 0.1, 0, '#2b2b2e', 16); cupM.rotation.z = Math.PI / 2; }
      const mic = cyl(head, 0.005, 0.005, 0.12, -0.1, 0.06, -0.05, '#2b2b2e', 6); mic.rotation.x = 1.1; mic.rotation.z = 0.3;
      sph(head, 0.012, -0.08, 0.05, -0.1, '#2b2b2e', 8);
    }
    // 팔
    const sleeveFore = p.outfit === 'tshirt' ? skin : top;
    rig.arms = [-1, 1].map(s => {
      const sh = new THREE.Group(); sh.position.set(s * 0.185 * b, 0.47, 0); spine.add(sh);
      sph(sh, 0.06, 0, 0, 0, top, 14).scale.set(1, 0.9, 0.9);
      cap(sh, 0.047, 0.2, 0, -0.14, 0, top);
      const el = new THREE.Group(); el.position.y = -0.285; sh.add(el);
      cap(el, 0.041, 0.18, 0, -0.12, 0, sleeveFore);
      if (p.outfit !== 'tshirt') cyl(el, 0.044, 0.044, 0.03, 0, -0.215, 0, mat(shade(p.top, -15)), 12);
      const hand = sph(el, 0.042, 0, -0.27, -0.005, skin, 14); hand.scale.set(0.8, 1.15, 0.55);
      return { sh, el, side: s };
    });
    // 다리
    rig.legs = [-1, 1].map(s => {
      const hip = new THREE.Group(); hip.position.set(s * 0.085 * b, -0.03, 0); hips.add(hip);
      cap(hip, 0.072 * Math.min(b, 1.1), 0.28, 0, -0.21, 0, bottom);
      const knee = new THREE.Group(); knee.position.y = -0.44; hip.add(knee);
      cap(knee, 0.056, 0.3, 0, -0.2, 0, bottom);
      const foot = box(knee, 0.1, 0.075, 0.25, 0, -0.43, -0.05, shoes, 0.035);
      return { hip, knee, foot, side: s };
    });
    body.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
    rig.joints = [spine, head, ...rig.arms.flatMap(a => [a.sh, a.el]), ...rig.legs.flatMap(l => [l.hip, l.knee])];
    return rig;
  }
  function shade(hex, pct) { const c = new THREE.Color(hex); const hsl = {}; c.getHSL(hsl); c.setHSL(hsl.h, hsl.s, Math.max(0, Math.min(1, hsl.l + pct / 100))); return '#' + c.getHexString(); }

  // 손에 드는 소품 (폰·컵·책·과자)
  const phoneScreenMat = new THREE.MeshBasicMaterial({ color: '#8fd3ff' });
  function attachProps(w) {
    const r = w.rig, R = r.arms[1].el, Lf = r.arms[0].el, P = {};
    P.phone = new THREE.Group(); P.phone.position.set(-0.025, -0.3, -0.04); P.phone.rotation.set(0, 0, 0.25); R.add(P.phone);
    box(P.phone, 0.065, 0.13, 0.012, 0, 0, 0, '#1b1d22', 0.006);
    const scr = mesh(planeGeo(0.056, 0.112), phoneScreenMat, P.phone, 0, 0, -0.0065, false); scr.rotation.y = Math.PI;
    P.drink = new THREE.Group(); P.drink.position.set(0, -0.29, 0.055); R.add(P.drink);
    if (w.id === 'codex') { cyl(P.drink, 0.033, 0.033, 0.12, 0, 0, 0, '#b7f23a', 14); cyl(P.drink, 0.03, 0.03, 0.006, 0, 0.063, 0, '#c8ccd0', 14); }
    else if (w.id === 'claude') { cyl(P.drink, 0.04, 0.03, 0.06, 0, 0, 0, '#f4efe6', 18); cyl(P.drink, 0.036, 0.036, 0.004, 0, 0.026, 0, '#a5662f', 16); }
    else { cyl(P.drink, 0.04, 0.036, 0.09, 0, 0, 0, w.color, 16); cyl(P.drink, 0.036, 0.036, 0.004, 0, 0.042, 0, '#4a2e1c', 14); }
    P.book = new THREE.Group(); P.book.position.set(0.1, -0.3, -0.05); P.book.rotation.set(0, 0, -0.1); Lf.add(P.book);
    for (const s of [-1, 1]) {
      const page = box(P.book, 0.11, 0.16, 0.012, s * 0.056, 0, 0, '#fbf6ea', 0.003); page.rotation.y = -s * 0.28;
      const cover = box(P.book, 0.115, 0.165, 0.006, s * 0.058, 0, 0.01, '#7a3b2e', 0.002); cover.rotation.y = -s * 0.28;
    }
    P.chip = sph(R, 0.022, 0, -0.31, -0.035, '#f2c14e', 10); P.chip.scale.set(1, 1, 0.3);
    for (const k in P) { P[k].visible = false; P[k].traverse(o => { if (o.isMesh) o.castShadow = false; }); }
    r.props = P;
  }

  // ───────────── 이름표·말풍선 ─────────────
  function makeSprite(w, h, scaleW) {
    const c = document.createElement('canvas'); c.width = w; c.height = h;
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true, sizeAttenuation: false }));
    s.scale.set(scaleW, scaleW * h / w, 1); s.renderOrder = 10;
    return { canvas: c, tex: t, sprite: s, key: '' };
  }
  function rr(g, x, y, w, h, r) { g.beginPath(); g.roundRect(x, y, w, h, r); }
  function drawLabel(w) {
    const key = [w.profile.nick, w.leader, w.status, w.color].join('|');
    if (w.label.key === key) return; w.label.key = key;
    const g = w.label.canvas.getContext('2d'); const W = w.label.canvas.width;
    g.clearRect(0, 0, W, 128);
    g.font = 'bold 44px "Malgun Gothic", sans-serif';
    const name = w.profile.nick || w.name;
    const tw = g.measureText(name).width;
    const tag = w.leader ? '팀장' : '';
    g.font = 'bold 30px "Malgun Gothic", sans-serif';
    const tagW = tag ? g.measureText(tag).width + 26 : 0;
    const total = 30 + 26 + tw + (tag ? 14 + tagW : 0) + 26;
    const x0 = (W - total) / 2;
    g.fillStyle = 'rgba(255,255,255,.94)'; rr(g, x0, 18, total, 74, 37); g.fill();
    g.strokeStyle = 'rgba(0,0,0,.12)'; g.lineWidth = 2; g.stroke();
    g.fillStyle = w.color; rr(g, x0 + 26, 43, 24, 24, 5); g.fill();
    g.fillStyle = '#222'; g.font = 'bold 44px "Malgun Gothic", sans-serif'; g.fillText(name, x0 + 62, 71);
    if (tag) { g.fillStyle = '#2f9d6e'; rr(g, x0 + 62 + tw + 14, 33, tagW, 44, 22); g.fill(); g.fillStyle = '#fff'; g.font = 'bold 30px "Malgun Gothic", sans-serif'; g.fillText(tag, x0 + 62 + tw + 27, 66); }
    if (w.status) { g.font = '28px "Malgun Gothic", sans-serif'; g.fillStyle = w.busy ? w.color : '#7b7f86'; g.textAlign = 'center'; g.fillText(w.status, W / 2, 122); g.textAlign = 'left'; }
    w.label.tex.needsUpdate = true;
  }
  function wrapText(g, text, maxW, maxLines) {
    const out = []; let line = '';
    for (const ch of text) { if (g.measureText(line + ch).width > maxW) { out.push(line); line = ch; if (out.length === maxLines) break; } else line += ch; }
    if (out.length < maxLines && line) out.push(line);
    if (out.length === maxLines && text.length > out.join('').length) out[maxLines - 1] = out[maxLines - 1].slice(0, -1) + '…';
    return out;
  }
  // 말풍선 종류: work(💭 실제 생각) · concern(⚠ 우려) · say(직접 하는 말) · quip(딴짓 혼잣말) · zzz(졸음)
  function drawBubble(w) {
    const text = w.bubbleText || '', kind = w.bubbleKind || 'work';
    const key = text + '|' + kind;
    if (w.bubble.key === key) return; w.bubble.key = key;
    const g = w.bubble.canvas.getContext('2d'); const W = w.bubble.canvas.width, Hh = w.bubble.canvas.height;
    g.clearRect(0, 0, W, Hh);
    if (!text) { w.bubble.tex.needsUpdate = true; return; }
    const prefix = kind === 'concern' ? '⚠ ' : kind === 'work' ? '💭 ' : '';
    g.font = kind === 'zzz' ? 'bold italic 34px "Malgun Gothic", sans-serif' : '30px "Malgun Gothic", sans-serif';
    const lines = wrapText(g, prefix + text, W - 70, 2);
    const tw = Math.max(...lines.map(l => g.measureText(l).width));
    const bw = Math.min(W - 20, tw + 64), bx = (W - bw) / 2;
    const bh = 30 + lines.length * 40;
    const fill = { concern: '#fff4e0', zzz: '#eef0ff', quip: '#fffdf5', say: '#ffffff', work: '#ffffff' }[kind] || '#ffffff';
    g.fillStyle = fill; rr(g, bx, 10, bw, bh, 26); g.fill();
    g.strokeStyle = kind === 'concern' ? '#e5a23a' : kind === 'zzz' ? 'rgba(90,100,180,.35)' : 'rgba(0,0,0,.15)'; g.lineWidth = 3; g.stroke();
    g.beginPath(); g.moveTo(W / 2 - 18, 10 + bh - 1); g.lineTo(W / 2, 10 + bh + 22); g.lineTo(W / 2 + 18, 10 + bh - 1); g.fill();
    g.fillStyle = kind === 'zzz' ? '#5560a8' : '#2b2b2b';
    lines.forEach((l, i) => g.fillText(l, bx + 32, 52 + i * 40));
    w.bubble.tex.needsUpdate = true;
  }
  function say(w, text, ms = 5000, kind = 'quip', force = false) {
    const now = performance.now();
    if (!text) return false;
    if (!force && now < w.bubbleUntil && w.bubbleText && w.bubbleKind !== 'zzz') return false;
    w.bubbleText = text; w.bubbleKind = kind; w.bubbleUntil = now + ms;
    if (kind === 'quip') w.lastQuip = now;
    return true;
  }
  const canQuip = w => performance.now() - (w.lastQuip || 0) > 18000;

  // ───────────── 성격 (딴짓 가중치·혼잣말) ─────────────
  const HABITS = {
    claude: { read: 4, drink: 3, chin: 2.4, look: 2, stretch: 1, spin: 0.6, phone: 0.5, doze: 0.5 },
    codex: { phone: 4, drink: 3, stretch: 2.2, spin: 2, fist: 1.3, chin: 1, look: 1, doze: 0.4 },
    gemini: { doze: 3.2, snack: 3, phone: 3, jerk: 1.4, drink: 1.2, chin: 1, stretch: 1, look: 0.8, spin: 0.5 },
  };
  const QUIPS = {
    claude: {
      read: ['한 챕터만 더 읽고 할게요', '이 책 생각보다 재밌네요', '(책 읽는 중)'],
      drink: ['따뜻한 차 한 잔이 최고예요', '차 마시면서 머리 좀 식힐게요'],
      chin: ['음… 더 깔끔한 방법이 있을 텐데', '(창밖 보며 멍…)'],
      stretch: ['으으— 어깨가 뻐근하네요'],
      look: ['이 코드 누가 짰지… 아 나네', '오늘 점심 뭐 먹을까요?'],
      phone: ['(폰 보는 중)'], doze: ['살짝 졸리네요…'], spin: ['의자 한 바퀴만…'],
    },
    codex: {
      phone: ['(폰 보는 중)', '한 판만 더…', '이번 판은 이긴다'],
      fist: ['보스만 잡고 할게요', '(게임 중)'],
      drink: ['에너지 드링크 수혈 중', '카페인 충전 완료!'],
      stretch: ['허리야… 스트레칭 좀 할게요', '으아— 기지개 한 번만'],
      spin: ['의자 돌리는 거 은근 재밌어요', '빙글빙글~'],
      chin: ['버그 없는 세상에 살고 싶다…'],
      look: ['이 코드 누가 짰지… 아 나네', '오리야, 내 얘기 좀 들어볼래?'],
      doze: ['눈꺼풀이 무거워요…'],
    },
    gemini: {
      doze: ['5분만 눈 붙일게요…', '졸려요…'],
      jerk: ['(꾸벅…)'],
      snack: ['과자 드실 분?', '이거 하나만 먹고 할게요', '바삭바삭~'],
      phone: ['(폰 보는 중)', '오, 이 사진 예쁘다'],
      drink: ['커피 한 잔만…', '커피 없인 못 살아요'],
      chin: ['오늘따라 시간이 안 가네요'],
      stretch: ['하아암— 기지개 좀 켤게요'],
      look: ['창밖 날씨 좋다~'], spin: ['빙그르르~'],
    },
  };
  const SIP_LINES = { claude: ['차 한 모금 마시고 계속할게요'], codex: ['에너지 드링크 한 모금!', '카페인 보충 좀…'], gemini: ['커피 한 모금만…', '잠깐 목 좀 축이고요'] };
  const FIST_LINES = ['앗싸, 이겼다!', '나이스!! 한 판 이겼어요'];
  const JERK_LINES = ['헉! 안 잤어요!', '앗, 깜짝이야… 안 졸았어요'];
  const RETURN_LINES = ['다녀왔습니다!', '저 왔어요~ 밀린 일 있나요?', '잘 쉬고 왔어요. 다시 달려볼게요!'];
  const WAKE_BUSY = ['앗, 일 들어왔네요! 바로 갈게요', '잠 깼어요, 자리로 갈게요!'];
  const WAKE_REST = ['푹 잤다~ 개운하네요', '잘 쉬었어요. 자리로 갈게요'];
  const NAP_LINES = ['너무 피곤해요… 잠깐 수면실 다녀올게요', '5분만 눈 붙이고 올게요…'];
  const CANCEL_LINES = ['아, 다시 생각해 보니 할 일이 남았네요', '앗, 잠깐만요. 자리로 돌아갈게요'];

  // ───────────── 휴가·반차·휴식 ─────────────
  const GONE = new Set(['vacation', 'halfday', 'early']);
  const LEAVE_KINDS = ['vacation', 'halfday', 'early', 'rest'];
  const LEAVE_TITLE = { vacation: '휴가 중', halfday: '반차', early: '조기 퇴근', rest: '휴식 중' };
  const pad2 = n => String(n).padStart(2, '0');
  function untilParts(until) {
    if (!until) return null;
    const d = new Date(Number(until)); if (isNaN(d)) return null;
    const now = new Date(), same = d.toDateString() === now.toDateString();
    const tom = new Date(now); tom.setDate(now.getDate() + 1);
    const hm = `${pad2(d.getHours())}:${pad2(d.getMinutes())}`, md = `${d.getMonth() + 1}/${d.getDate()}`;
    return { hm, md, same, tomorrow: d.toDateString() === tom.toDateString() };
  }
  function untilText(until) {
    const u = untilParts(until); if (!u) return '곧 돌아올게요';
    return u.same ? `${u.hm} 복귀` : `${u.md} ${u.hm} 복귀`;
  }
  function leaveLine(L) {
    if (L.reason) return L.reason;
    const u = untilParts(L.until);
    if (L.kind === 'vacation') return u ? `${u.md}까지 휴가 다녀올게요!` : '휴가 다녀올게요!';
    if (L.kind === 'halfday') return u ? `반차 쓰고 ${u.same ? '' : u.md + ' '}${u.hm}에 돌아올게요~` : '반차 쓰고 금방 올게요~';
    if (L.kind === 'early') return u && !u.same ? `오늘은 먼저 들어가 볼게요. ${u.tomorrow ? '내일' : u.md} 봬요!` : '오늘은 먼저 들어가 볼게요!';
    return pick(NAP_LINES);
  }
  function setSign(w, L) {
    const s = w.desk.sign; if (!s) return;
    if (!L) { s.group.visible = false; return; }
    s.group.visible = true;
    const sub = L.kind === 'rest' ? '수면실에서 쉬는 중' : untilText(L.until);
    const key = [L.kind, sub, w.profile.nick].join('|');
    if (s.key === key) return; s.key = key;
    const g = s.canvas.getContext('2d'), W = 320, Hh = 200;
    g.clearRect(0, 0, W, Hh);
    g.fillStyle = '#fffaf0'; rr(g, 4, 4, W - 8, Hh - 8, 22); g.fill();
    g.strokeStyle = w.color; g.lineWidth = 6; g.stroke();
    g.fillStyle = w.color; rr(g, 4, 4, W - 8, 50, [22, 22, 0, 0]); g.fill();
    g.textAlign = 'center';
    g.fillStyle = '#fff'; g.font = 'bold 30px "Malgun Gothic", sans-serif'; g.fillText(w.profile.nick || w.name, W / 2, 40);
    g.fillStyle = '#2b2b2b'; g.font = 'bold 50px "Malgun Gothic", sans-serif'; g.fillText((L.kind === 'vacation' ? '🌴 ' : L.kind === 'rest' ? '😴 ' : '') + LEAVE_TITLE[L.kind], W / 2, 118);
    g.fillStyle = '#555'; g.font = '32px "Malgun Gothic", sans-serif'; g.fillText(sub, W / 2, 168);
    s.tex.needsUpdate = true;
  }

  // ───────────── 팀원 배치 ─────────────
  const workers = new Map();
  AGENTS.forEach((a, i) => {
    const d = desks[i];
    const profile = load('office-profile2:' + a.id, DEFAULT_PROFILES[a.id]);
    const root = new THREE.Group(); scene.add(root);
    const pivot = new THREE.Group(); root.add(pivot); // 눕기용 회전축
    const label = makeSprite(512, 132, 0.25); scene.add(label.sprite);
    const bubble = makeSprite(512, 150, 0.34); scene.add(bubble.sprite);
    label.sprite.center.set(0.5, 0); bubble.sprite.center.set(0.5, -0.66); // 말풍선은 이름표 바로 위에 쌓이게
    const ring = mesh(new THREE.RingGeometry(0.42, 0.5, 40), new THREE.MeshBasicMaterial({ color: a.color, transparent: true, opacity: 0.0 }), scene, 0, 0.02, 0, false);
    ring.rotation.x = -Math.PI / 2;
    const seatWorld = new THREE.Vector3(); d.seat.getWorldPosition(seatWorld);
    const w = { ...a, idx: i, desk: d, bed: BEDS[i], profile, root, pivot, label, bubble, ring, rig: null, external: null, mixer: null, action: null,
      home: seatWorld.clone(), homeRot: d.group.rotation.y, pos: seatWorld.clone(), rotY: d.group.rotation.y,
      mode: 'desk', pose: 'sit', path: [], busy: false, leader: false, present: true, status: '대기', bubbleText: '', bubbleUntil: 0, bubbleKind: 'work',
      thinkPhase: Math.random() * 10, lastThought: '', leave: null, fatigue: 0, quotaOut: false, inTeam: true, inited: false,
      idleSince: performance.now(), beh: null, lastBeh: '', spin: 0, spinTarget: 0, sipStart: null, nextSip: 0, walkWait: 0,
      blendUntil: 0, lastPose: 'sit', sleepy: false, drinkTilt: 0, lastQuip: 0, prevJ: new Float32Array(64) };
    workers.set(a.id, w);
    rebuild(w);
  });

  function rebuild(w) {
    if (w.rig) { w.pivot.remove(w.rig.body); disposeTree(w.rig.body); }
    w.rig = buildPerson(w.profile);
    attachProps(w);
    w.pivot.add(w.rig.body);
    const s = (Number(w.profile.height) || 100) / 100;
    w.rig.body.scale.setScalar(s);
    w.rig.body.visible = !w.external;
    w.label.key = ''; drawLabel(w);
  }
  function disposeTree(o) { o.traverse(n => { if (n.isMesh && !n.geometry?.userData?.cached) n.geometry?.dispose(); }); }

  // ───────────── 자세·동작 ─────────────
  const SOFA = [[LX - 0.92, -4.05], [LX, -4.05], [LX + 0.92, -4.05]];
  const sstep = x => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
  // 올렸다(up) → 유지(hold) → 내리기(down) 0~1 곡선
  const cycle = (x, up, hold, down) => x < 0 ? 0 : x < up ? sstep(x / up) : x < up + hold ? 1 : x < up + hold + down ? 1 - sstep((x - up - hold) / down) : 0;
  // 팔 자세 [어깨 앞뒤, 어깨 벌림(+바깥), 팔꿈치 굽힘, 팔꿈치 비틀기]
  const REST = [0.4, -0.12, 0.75, 0], TYPE = [0.55, -0.14, 0.95, 0.05], LAP = [0.12, -0.08, 0.6, 0];
  const SIP = [1.0, -0.32, 2.3, 0.25], THINK = [1.05, -0.35, 2.05, 0.3], PHONE = [0.32, -0.5, 1.75, 0];
  const BOOK = [0.3, -0.34, 1.55, 0], PUSH = [1.15, -0.42, 2.45, 0.35], UP = [2.9, 0.25, 0.25, 0], ARMREST = [0.15, 0.1, 0.5, 0];
  const armSet = (a, p) => { a.sh.rotation.set(p[0], 0, p[1] * a.side); a.el.rotation.set(p[2], 0, p[3] * a.side); };
  const lerpArm = (a, A, B, k) => { a.sh.rotation.set(A[0] + (B[0] - A[0]) * k, 0, (A[1] + (B[1] - A[1]) * k) * a.side); a.el.rotation.set(A[2] + (B[2] - A[2]) * k, 0, (A[3] + (B[3] - A[3]) * k) * a.side); };
  function eyesClosed(r, closed) { for (const e of r.eyes || []) { e.lid.visible = closed; for (const o of e.open) o.visible = !closed; } }

  function setPose(w, t, dt) {
    const r = w.rig; if (!r) return;
    const [L, R] = r.legs, [AL, AR] = r.arms, P = r.props || {};
    for (const k in P) P[k].visible = false;
    if (w.desk.cup) w.desk.cup.visible = true;
    w.spinTarget = 0; w.sleepy = false;
    if (r.mouth) r.mouth.scale.y = 1;
    eyesClosed(r, false);
    const lying = w.pose === 'lie';
    w.pivot.rotation.set(lying ? Math.PI / 2 : 0, 0, 0);
    w.pivot.position.set(0, lying ? 0.61 : 0, 0);
    const sit = w.pose === 'sit' || w.pose === 'sofa';
    const motion = room.motion;
    r.hips.position.y = sit ? (w.pose === 'sofa' ? 0.5 : 0.53) : 0.93;
    r.hips.position.z = sit ? 0.06 : 0;
    const breathe = motion ? Math.sin(t * (lying ? 1.0 : 1.6) + w.thinkPhase) * (lying ? 0.02 : 0.012) : 0;
    r.spine.scale.y = 1 + breathe * 0.4;
    r.head.rotation.set(0, 0, 0); r.spine.rotation.set(0, 0, 0);
    if (sit) {
      for (const leg of [L, R]) { leg.hip.rotation.set(Math.PI / 2 * 0.98, 0, leg.side * 0.04); leg.knee.rotation.x = -Math.PI / 2 * 0.95; }
    }
    if (w.pose === 'walk') {
      const ph = t * 7.5;
      L.hip.rotation.set(Math.sin(ph) * 0.5, 0, 0); R.hip.rotation.set(-Math.sin(ph) * 0.5, 0, 0);
      L.knee.rotation.x = -Math.max(0, Math.sin(ph + 1.2)) * 0.7; R.knee.rotation.x = -Math.max(0, -Math.sin(ph + 1.2)) * 0.7;
      AL.sh.rotation.set(-Math.sin(ph) * 0.4, 0, 0.08); AR.sh.rotation.set(Math.sin(ph) * 0.4, 0, -0.08);
      AL.el.rotation.set(0.25, 0, 0); AR.el.rotation.set(0.25, 0, 0);
      r.hips.position.y = 0.93 + Math.abs(Math.sin(ph)) * 0.025;
      r.spine.rotation.x = -0.04;
      return;
    }
    if (w.pose === 'stand') {
      for (const leg of [L, R]) { leg.hip.rotation.set(0, 0, 0); leg.knee.rotation.x = 0; }
      for (const a of [AL, AR]) armSet(a, [0, -0.08, 0.15, 0]);
      return;
    }
    if (lying) {
      for (const leg of [L, R]) { leg.hip.rotation.set(0.06, 0, leg.side * 0.04); leg.knee.rotation.x = -0.1; }
      for (const a of [AL, AR]) armSet(a, [0.04, 0.05, 0.12, 0]);
      r.head.rotation.set(0.05, 0.35 + (motion ? Math.sin(t * 0.15 + w.thinkPhase) * 0.1 : 0), 0);
      eyesClosed(r, true); w.sleepy = true;
      return;
    }
    if (w.pose === 'sofa') {
      r.spine.rotation.x = 0.14;
      for (const a of [AL, AR]) armSet(a, [0.35, -0.12, 0.9, 0]);
      r.head.rotation.set(0.05, Math.sin(t * 0.4 + w.thinkPhase) * 0.35, 0);
      if (r.mouth) r.mouth.scale.y = motion && Math.sin(t * 9 + w.thinkPhase) > 0.6 ? 2.2 : 1;
      return;
    }
    deskPose(w, t, r);
  }

  // 책상에 앉은 자세 — 일할 땐 타이핑·턱 괴고 생각·가끔 한 모금, 쉴 땐 성격대로 딴짓
  function deskPose(w, t, r) {
    const motion = room.motion, [AL, AR] = r.arms, P = r.props || {}, ph = w.thinkPhase;
    r.spine.rotation.x = 0.02;
    r.head.rotation.set(motion ? Math.sin(t * 0.5 + ph) * 0.05 : 0, motion ? Math.sin(t * 0.37 + ph) * 0.25 : 0, 0);
    let cupInHand = false;
    if (w.busy && motion) {
      const think = ((t + ph) % 11) > 8.6;
      r.spine.rotation.x = -0.1; r.head.rotation.set(0.08, Math.sin(t * 0.37 + ph) * 0.12, 0);
      for (const a of [AL, AR]) { const tap = Math.sin(t * 15 + a.side * 1.7 + ph) * 0.07; armSet(a, [0.55 + tap * 0.3, -0.14, 0.95 + tap, 0.05]); }
      const sx = w.sipStart != null ? t - w.sipStart : 99;
      if (sx < 4.0) {
        const k = cycle(sx, 1.0, 1.8, 1.0);
        lerpArm(AR, TYPE, SIP, k); r.head.rotation.x += 0.1 * k; r.spine.rotation.x += 0.08 * k;
        cupInHand = k > 0.04; w.drinkTilt = k * 0.6;
      } else if (think) { armSet(AR, THINK); r.head.rotation.set(0.12, 0.2, 0.12); }
    } else if (!motion || !w.beh) {
      for (const a of [AL, AR]) armSet(a, REST);
    } else {
      const b = w.beh, lt = t - b.start;
      switch (b.kind) {
        case 'look':
          for (const a of [AL, AR]) armSet(a, REST);
          r.head.rotation.set(0.06, Math.sin(t * 0.33 + ph) * 0.5, 0);
          break;
        case 'chin': // 팔 괴고 딴짓
          r.spine.rotation.x = -0.12;
          armSet(AL, REST); armSet(AR, THINK);
          r.head.rotation.set(0.04 + Math.sin(t * 0.6) * 0.03, 0.3 + Math.sin(t * 0.25 + ph) * 0.18, 0.18);
          if (Math.sin(t * 0.21 + ph) > 0.97) eyesClosed(r, true);
          break;
        case 'doze': { // 꾸벅꾸벅
          const f = (lt * 0.28 + ph) % 1, nod = f < 0.85 ? sstep(f / 0.85) : 1 - sstep((f - 0.85) / 0.15);
          for (const a of [AL, AR]) armSet(a, LAP);
          r.head.rotation.set(-0.08 - 0.5 * nod, 0, 0.06 * nod);
          r.spine.rotation.x = -0.02 - 0.12 * nod;
          eyesClosed(r, true); w.sleepy = true;
          break;
        }
        case 'jerk': { // 고개가 툭 떨어졌다가 화들짝
          const x = lt % 6.5, idx = Math.floor(lt / 6.5);
          for (const a of [AL, AR]) armSet(a, LAP);
          if (x < 4.6) { const n = (x / 4.6) ** 2; r.head.rotation.x = -0.08 - 0.75 * n; r.spine.rotation.x = -0.15 * n; eyesClosed(r, x > 1.2); w.sleepy = x > 1.2; }
          else if (x < 4.9) { r.head.rotation.x = 0.25; r.spine.rotation.x = 0.1; for (const a of [AL, AR]) armSet(a, [0.6, -0.2, 0.9, 0]); if (r.mouth) r.mouth.scale.y = 2.6; }
          else { const k = 1 - (x - 4.9) / 1.6; r.head.rotation.set(0.25 * k, Math.sin(t * 10) * 0.35 * k, 0); }
          if (x >= 4.6 && b.flag !== idx) { b.flag = idx; if (Math.random() < 0.6) say(w, pick(JERK_LINES), 3000, 'quip', true); }
          break;
        }
        case 'phone':
        case 'fist': {
          r.spine.rotation.x = -0.05;
          for (const a of [AL, AR]) armSet(a, PHONE);
          AL.el.rotation.x += Math.sin(t * 13) * 0.06; // 엄지 톡톡
          r.head.rotation.set(-0.32, 0, 0);
          P.phone.visible = true;
          const left = b.end - t;
          if (b.kind === 'fist' && left < 2.6) { // 작은 승리의 주먹
            const pt = 2.6 - left, pump = Math.abs(Math.sin(pt * 8));
            armSet(AL, [0.5 + 0.45 * pump, 0.35, 2.3 - 0.4 * pump, 0]);
            r.head.rotation.set(0.12, 0, 0); if (r.mouth) r.mouth.scale.y = 2.4;
            if (!b.flag) { b.flag = 1; say(w, pick(FIST_LINES), 2800, 'quip', true); }
          }
          break;
        }
        case 'drink': {
          const x = lt % 8.5, k = cycle(x - 0.6, 1.1, 1.8, 1.1);
          armSet(AL, REST); lerpArm(AR, REST, SIP, k);
          r.head.rotation.x += 0.12 * k;
          cupInHand = k > 0.04; w.drinkTilt = k * 0.65;
          break;
        }
        case 'snack': {
          const x = lt % 3.2, k = cycle(x - 0.3, 0.45, 0.35, 0.5);
          armSet(AL, REST); lerpArm(AR, REST, SIP, k);
          P.chip.visible = k > 0.05 && x < 1.1;
          if (x > 1.1 && r.mouth) r.mouth.scale.y = 1 + Math.abs(Math.sin(t * 14)) * 1.2;
          r.head.rotation.x += Math.sin(t * 2) * 0.03;
          break;
        }
        case 'stretch': {
          const x = lt % 10, k = cycle(x - 0.5, 0.9, 2.6, 0.9);
          for (const a of [AL, AR]) lerpArm(a, REST, UP, k);
          r.spine.rotation.x = 0.02 + 0.16 * k; r.head.rotation.set(0.28 * k, 0, 0);
          if (r.mouth) r.mouth.scale.y = 1 + 2.6 * k * k;
          if (k > 0.6) eyesClosed(r, true);
          break;
        }
        case 'spin': {
          for (const a of [AL, AR]) armSet(a, ARMREST);
          w.spinTarget = Math.sin(lt * 0.85) * 0.8 * Math.min(1, lt / 1.2) * Math.max(0, Math.min(1, (b.end - t) / 1.2));
          r.head.rotation.set(0.05, Math.sin(t * 0.6) * 0.3, 0);
          break;
        }
        case 'read': {
          for (const a of [AL, AR]) armSet(a, BOOK);
          P.book.visible = true;
          r.head.rotation.set(-0.38, Math.sin(t * 0.3) * 0.06, 0);
          const x = lt % 7.5;
          if (x > 5.8) { const k = cycle(x - 5.8, 0.35, 0.55, 0.4); lerpArm(AR, BOOK, PUSH, k); r.head.rotation.x = -0.38 + 0.3 * k; } // 안경 밀어 올리기
          break;
        }
        default: for (const a of [AL, AR]) armSet(a, REST);
      }
    }
    if (P.drink) P.drink.visible = cupInHand;
    if (w.desk.cup) w.desk.cup.visible = !cupInHand;
  }

  function pickBehavior(w, t) {
    const habits = HABITS[w.id] || HABITS.claude;
    const entries = Object.entries(habits).map(([k, wt]) => [k, k === w.lastBeh ? wt * 0.3 : wt]);
    let sum = entries.reduce((s, [, wt]) => s + wt, 0), x = Math.random() * sum, kind = entries[0][0];
    for (const [k, wt] of entries) { x -= wt; if (x <= 0) { kind = k; break; } }
    const dur = rand(8, 25);
    w.beh = { kind, start: t, end: t + dur, flag: 0 }; w.lastBeh = kind;
    w.blendUntil = t + 0.7;
    if (room.bubbles && canQuip(w) && Math.random() < 0.42) {
      const lines = QUIPS[w.id]?.[kind];
      if (lines) say(w, pick(lines), 5000, 'quip');
    }
  }

  // ───────────── 길 찾기 (책상 사이 통로 z=-0.55 를 지나간다) ─────────────
  const AISLE_Z = -0.55;
  function exitChain(p) {
    const x = p.x, z = p.z;
    if (x > 2.75 && z > 1.0) return [v3(3.8, z), v3(3.8, AISLE_Z)];                         // 수면실
    if (x < -5.6 && z > 1.4) return [v3(-6.0, z), v3(-6.0, AISLE_Z)];                       // 문 쪽 복도
    if (x > 3.2 && z < -0.7) return z < -3.3 ? [v3(x, -3.62), v3(3.55, -3.62), v3(3.55, AISLE_Z)] : [v3(3.55, z), v3(3.55, AISLE_Z)]; // 휴게실
    return [v3(x, AISLE_Z)];                                                                 // 책상·통로
  }
  function goTo(w, target, rot, endPose, opts = {}) {
    const pts = [...exitChain(w.pos), ...exitChain(target).reverse(), target.clone()];
    const path = [];
    for (const p of pts) { const last = path[path.length - 1] || w.pos; if (Math.hypot(p.x - last.x, p.z - last.z) > 0.04) path.push(p); }
    if (!path.length) path.push(target.clone());
    w.path = path; w.endPose = endPose; w.endRot = rot; w.onArrive = opts.onArrive || null;
    w.beh = null; w.walkWait = opts.delay || 0;
    w.pose = w.walkWait > 0 ? 'stand' : 'walk';
  }
  let lounge = false;
  function setLounge(on) {
    lounge = on;
    let seat = 0;
    for (const w of workers.values()) {
      if (!w.present || w.mode === 'away' || w.mode === 'leaving' || w.mode === 'nap') continue;
      if (on && !w.busy) { const s = SOFA[seat++ % 3]; w.mode = 'lounge'; goTo(w, v3(s[0], s[1]), Math.PI, 'sofa'); }
      else if (!on && w.mode !== 'desk') backToDesk(w);
    }
  }
  function backToDesk(w, delay = 0) { w.mode = 'desk'; goTo(w, w.home.clone(), w.homeRot, 'sit', { delay }); }
  function stepWalk(w, dt) {
    if (w.walkWait > 0) { w.walkWait -= dt; if (w.walkWait <= 0 && w.path.length) w.pose = 'walk'; return; }
    if (w.pose !== 'walk' || !w.path.length) return;
    const target = w.path[0];
    const dx = target.x - w.pos.x, dz = target.z - w.pos.z, dist = Math.hypot(dx, dz);
    const speed = 1.7;
    if (dist < 0.03) {
      w.path.shift();
      if (!w.path.length) { w.pose = w.endPose; w.rotY = w.endRot; const cb = w.onArrive; w.onArrive = null; cb?.(w); }
      return;
    }
    const step = Math.min(dist, speed * dt);
    w.pos.x += dx / dist * step; w.pos.z += dz / dist * step;
    const want = Math.atan2(-dx, -dz);
    let diff = ((want - w.rotY + Math.PI * 3) % (Math.PI * 2)) - Math.PI;
    w.rotY += diff * Math.min(1, dt * 10);
  }

  // 자리 비우기·돌아오기·수면실
  function getUp(w) {
    if (w.pose === 'lie' || w.mode === 'nap') { if (w.bed.used) foldBlanket(w.bed); }
    if (w.pose === 'lie') { w.pos.copy(w.bed.approach); w.pose = 'stand'; w.rotY = -Math.PI / 2; }
  }
  function startLeave(w, L, instant) {
    getUp(w);
    w.mode = 'leaving'; w.beh = null;
    if (instant) { w.mode = 'away'; w.path = []; w.walkWait = 0; w.pos.copy(DOOR); w.pose = 'stand'; setSign(w, L); return; }
    say(w, leaveLine(L), 8000, 'say', true);
    goTo(w, DOOR.clone(), Math.PI / 2, 'stand', { delay: w.pose === 'sit' ? 1.8 : 0.6, onArrive: x => { x.mode = 'away'; setSign(x, x.leave); } });
  }
  function startReturn(w) {
    setSign(w, null);
    w.mode = 'desk'; w.pos.copy(DOOR); w.rotY = -Math.PI / 2;
    goTo(w, w.home.clone(), w.homeRot, 'sit');
    say(w, pick(RETURN_LINES), 6000, 'say', true);
  }
  function lieDown(w) {
    w.pos.copy(w.bed.foot); w.rotY = Math.PI / 2; w.pose = 'lie'; w.path = []; w.walkWait = 0;
    spreadBlanket(w.bed);
  }
  function goNap(w, L, instant) {
    getUp(w);
    w.mode = 'nap'; w.beh = null;
    if (L && L.kind === 'rest') setSign(w, L);
    if (instant) { lieDown(w); return; }
    say(w, (L && L.reason) || pick(NAP_LINES), 6000, 'say', true);
    goTo(w, w.bed.approach.clone(), Math.PI / 2, 'stand', { delay: w.pose === 'sit' ? 1.3 : 0, onArrive: x => { if (x.mode === 'nap') lieDown(x); } });
  }
  function wakeUp(w) {
    const wasLying = w.pose === 'lie';
    getUp(w); setSign(w, null);
    backToDesk(w, wasLying ? 1.0 : 0);
    say(w, pick(w.busy ? WAKE_BUSY : WAKE_REST), 5000, 'say', true);
  }
  // 상태(휴가·휴식·피로·일)를 보고 어디 있을지 정한다
  function decide(w, instant) {
    if (!w.present) return;
    const L = w.leave;
    if (L && GONE.has(L.kind)) {
      if (w.mode !== 'away' && w.mode !== 'leaving') startLeave(w, L, instant);
      else if (w.mode === 'away') setSign(w, L);
      return;
    }
    if (w.mode === 'away') startReturn(w);
    else if (w.mode === 'leaving') { setSign(w, null); backToDesk(w); say(w, pick(CANCEL_LINES), 4000, 'say', true); }
    const tired = w.fatigue >= 80 && performance.now() - w.idleSince > 12000;
    const wantNap = !w.busy && ((L && L.kind === 'rest') || tired);
    if (wantNap) { if (w.mode !== 'nap') goNap(w, L, instant); else if (L) setSign(w, L); return; }
    if (w.mode === 'nap') { wakeUp(w); return; }
    if (w.busy && w.mode !== 'desk') backToDesk(w);
  }
  function statusOf(w, state) {
    const L = w.leave;
    if (L && GONE.has(L.kind)) return LEAVE_TITLE[L.kind] + (L.until ? ' · ' + untilText(L.until) : '');
    if (w.mode === 'nap' && !w.busy) return '수면실에서 쉬는 중';
    if (!w.inTeam) return '쉬는 중';
    if (w.busy) return w.task ? w.task + ' 작업 중' : '작업 중';
    if (state?.phase === 'approval' && w.leader) return '승인 기다림';
    if (state?.phase === 'done') return '완료';
    if (w.quotaOut) return '사용량 바닥';
    return '대기';
  }

  // ───────────── 외부 모델 ─────────────
  const clockT = new THREE.Clock();
  async function parseModel(buf, name) {
    const ext = (name.split('.').pop() || '').toLowerCase();
    const manager = new THREE.LoadingManager();
    manager.setURLModifier(url => /^(blob:|data:)/i.test(url) ? url : 'data:application/octet-stream;base64,');
    if (ext === 'fbx') { const o = new FBXLoader(manager).parse(buf, ''); return { object: o, clips: o.animations || [] }; }
    const gltf = await new GLTFLoader(manager).parseAsync(buf, '');
    return { object: gltf.scene, clips: gltf.animations || [] };
  }
  function attachExternal(w, object, clips) {
    clearExternal(w, true);
    const holder = new THREE.Group();
    const inner = new THREE.Group(); inner.add(object); holder.add(inner);
    object.traverse(n => { if (n.isMesh) { n.castShadow = true; n.receiveShadow = true; } });
    const bounds = new THREE.Box3().setFromObject(object);
    const size = bounds.getSize(new THREE.Vector3());
    if (!(size.y > 0) || !isFinite(size.y)) throw new Error('빈 모델');
    const scale = 1.72 / size.y;
    inner.scale.setScalar(scale);
    const center = bounds.getCenter(new THREE.Vector3());
    inner.position.set(-center.x * scale, -bounds.min.y * scale, -center.z * scale);
    w.external = { holder, inner, clips };
    w.pivot.add(holder);
    if (w.rig) w.rig.body.visible = false;
    if (clips.length) { w.mixer = new THREE.AnimationMixer(object); playClip(w); }
    applyExternalTweaks(w);
  }
  function playClip(w) {
    if (!w.mixer || !w.external) return;
    w.mixer.stopAllAction();
    const clips = w.external.clips;
    const clip = clips.find(c => c.name === w.profile.clip) || clips[0];
    if (clip) { w.action = w.mixer.clipAction(clip); w.action.play(); }
  }
  function applyExternalTweaks(w) {
    if (!w.external) return;
    const p = w.profile;
    w.external.holder.rotation.y = THREE.MathUtils.degToRad(Number(p.modelRot) || 0);
    w.external.holder.position.y = (Number(p.modelY) || 0) / 100;
    w.external.holder.scale.setScalar(((Number(p.modelScale) || 100) / 100) * ((Number(p.height) || 100) / 100));
  }
  function clearExternal(w, keepFlag) {
    if (w.external) { w.mixer?.stopAllAction(); w.pivot.remove(w.external.holder); w.external.holder.traverse(n => { if (n.isMesh) { n.geometry?.dispose(); } }); }
    w.external = null; w.mixer = null; w.action = null;
    if (w.rig) w.rig.body.visible = true;
    if (!keepFlag) { w.profile.model = null; saveProfile(w); }
  }
  function saveProfile(w) { localStorage.setItem('office-profile2:' + w.id, JSON.stringify(w.profile)); }
  // 저장해 둔 모델 불러오기
  fetch('/api/avatars').then(r => r.json()).then(list => {
    for (const [id, file] of Object.entries(list || {})) {
      const w = workers.get(id); if (!w || !file) continue;
      fetch('/api/avatar?agent=' + id).then(r => r.arrayBuffer()).then(buf => parseModel(buf, file)).then(({ object, clips }) => { attachExternal(w, object, clips); w.profile.model = file; })
        .catch(() => {});
    }
  }).catch(() => {});

  // ───────────── 카메라 ─────────────
  const VIEWS = {
    default: { pos: [1.9, 4.1, 3.3], target: [-2.5, 0.75, -2.1] },
    overview: { pos: [9.5, 12.5, 13.5], target: [0, 0, -0.4] },
    lounge: { pos: [7.8, 3.6, 2.6], target: [4.6, 0.7, -3.4] },
    rest: { pos: [1.2, 3.6, 6.9], target: [5.0, 0.45, 2.9] },
  };
  let camAnim = null, firstPerson = null, viewName = 'default';
  function flyTo(pos, target, ms = 900) { camAnim = { p0: camera.position.clone(), t0: controls.target.clone(), p1: new THREE.Vector3(...pos), t1: new THREE.Vector3(...target), s: performance.now(), ms }; }
  function setView(name) {
    viewName = name;
    if (name === 'first') return;
    firstPerson = null; controls.enabled = true;
    const v = VIEWS[name] || VIEWS.default; flyTo(v.pos, v.target);
  }
  camera.position.set(...VIEWS.default.pos); controls.target.set(...VIEWS.default.target); controls.update();
  let selected = 'claude';
  function firstPersonOf(id) {
    const w = workers.get(id); if (!w) return;
    selected = id; firstPerson = id; controls.enabled = false; viewName = 'first';
  }
  function updateFirstPerson() {
    const w = workers.get(firstPerson); if (!w || !w.rig) return;
    const headPos = new THREE.Vector3(); w.rig.head.getWorldPosition(headPos); headPos.y += 0.13;
    const fwd = new THREE.Vector3(-Math.sin(w.rotY), 0, -Math.cos(w.rotY));
    const eye = headPos.clone().addScaledVector(fwd, 0.08);
    const look = eye.clone().addScaledVector(fwd, 1).add(new THREE.Vector3(0, w.mode === 'desk' && w.pose === 'sit' ? -0.12 : 0, 0));
    camera.position.lerp(eye, 0.2); controls.target.lerp(look, 0.2); camera.lookAt(controls.target);
  }

  // 가린 모습 보기 (벽·칸막이 반투명)
  let xray = false;
  function setXray(on) {
    xray = on;
    for (const m of xrayMats) { m.transparent = on; m.opacity = on ? 0.18 : 1; m.depthWrite = !on; m.needsUpdate = true; }
  }

  // ───────────── 조명 프리셋 + 화면 테마 (테마는 저장하지 않고 위에 덧씌운다) ─────────────
  const THEMES = {
    dark: { bg: '#161b26', sky: '#59627c', hemi: '#c9d3ff', hemiK: 0.6, sun: '#b9c6ff', sunK: 0.55, wall: '#4a5266', wallMix: 0.55, floor: '#9a9caa', lamp: 1.6, win: 0.3, nap: 1.6 },
    light: { bg: '#eef4f8', sky: '#ffffff', hemi: '#ffffff', hemiK: 1.1, sun: '#fff8ec', sunK: 1.05, wall: '#ffffff', wallMix: 0.25, floor: '#ffffff', lamp: 0, win: 1, nap: 0.9 },
    warm: { bg: '#f1d6b4', sky: '#ffe0bd', hemi: '#ffe3c4', hemiK: 1.0, sun: '#ffc489', sunK: 0.9, wall: '#f0cfa4', wallMix: 0.35, floor: '#ffe6c8', lamp: 0.8, win: 0.8, nap: 1.2 },
    navy: { bg: '#17233d', sky: '#6a7fb0', hemi: '#c4d4ff', hemiK: 0.7, sun: '#a8bfff', sunK: 0.65, wall: '#51648c', wallMix: 0.5, floor: '#aab4cc', lamp: 1.3, win: 0.4, nap: 1.5 },
    forest: { bg: '#c9dcc3', sky: '#e2f3da', hemi: '#effbe6', hemiK: 1.0, sun: '#fff0c8', sunK: 0.95, wall: '#c9dbbd', wallMix: 0.45, floor: '#ecf2e2', lamp: 0.3, win: 0.9, nap: 1.0 },
  };
  let theme = null, lastShadows = null;
  function applyRoom() {
    const night = room.light === 'night', evening = room.light === 'evening';
    scene.background.set(night ? '#1b2333' : evening ? '#f0c79a' : '#cfe3ec');
    hemi.intensity = night ? 0.35 : evening ? 0.9 : 1.2;
    hemi.color.set(evening ? '#ffd9b0' : '#fff8ee');
    sun.intensity = night ? 0.25 : evening ? 1.6 : 2.4;
    sun.color.set(night ? '#8aa0ff' : evening ? '#ffb070' : '#fff1d6');
    sun.position.set(evening ? -9 : -6, evening ? 5 : 11, 7);
    windowLight.intensity = night ? 0.1 : 0.7;
    let lampI = night ? 2.2 : evening ? 0.9 : 0;
    napLamp.intensity = night ? 1.8 : evening ? 1.3 : 1.0;
    floorMat.map = room.floor === 'carpet' ? carpetTex : woodTex; floorMat.needsUpdate = true;
    wallMat.color.set(room.wall);
    floorMat.color.set('#ffffff'); skyMat.color.set('#ffffff'); groundMat.color.set('#9cbf8a');
    const T = theme && THEMES[theme];
    if (T) {
      scene.background.lerp(new THREE.Color(T.bg), 0.8);
      hemi.color.lerp(new THREE.Color(T.hemi), 0.6); hemi.intensity *= T.hemiK;
      sun.color.lerp(new THREE.Color(T.sun), 0.6); sun.intensity *= T.sunK;
      windowLight.intensity *= T.win;
      wallMat.color.lerp(new THREE.Color(T.wall), T.wallMix);
      floorMat.color.set(T.floor); skyMat.color.set(T.sky); groundMat.color.multiply(new THREE.Color(T.sky));
      lampI = Math.max(lampI, T.lamp); napLamp.intensity = Math.max(napLamp.intensity, T.nap);
    }
    for (const d of desks) d.lamp.intensity = lampI;
    renderer.shadowMap.enabled = room.shadows;
    if (lastShadows !== room.shadows) { lastShadows = room.shadows; scene.traverse(o => { if (o.material) o.material.needsUpdate = true; }); }
  }
  applyRoom();

  // ───────────── 모니터 화면 ─────────────
  function drawScreens(t) {
    for (const w of workers.values()) {
      const away = w.mode === 'away' || (w.mode === 'nap' && !w.busy);
      w.desk.screens.forEach((s, k) => {
        const g = s.canvas.getContext('2d');
        if (away) {
          if (s.drawn === 'away') return; s.drawn = 'away';
          g.fillStyle = '#0d1116'; g.fillRect(0, 0, 256, 160);
          g.fillStyle = 'rgba(255,255,255,.18)'; g.font = 'bold 20px sans-serif'; g.fillText(k ? '화면 잠김' : '자리 비움', 18, 90);
          s.tex.needsUpdate = true; return;
        }
        g.fillStyle = w.busy ? '#14202b' : '#20303b'; g.fillRect(0, 0, 256, 160);
        if (!w.busy) {
          if (s.drawn === 'idle') return; s.drawn = 'idle';
          g.fillStyle = 'rgba(255,255,255,.35)'; g.font = 'bold 22px sans-serif'; g.fillText(k ? '대기 중' : w.name, 18, 90); s.tex.needsUpdate = true; return;
        }
        s.drawn = 'busy';
        const off = Math.floor(t * 6 + k * 3);
        for (let i = 0; i < 11; i++) {
          const n = (i + off) * 7919 % 97;
          g.fillStyle = i % 4 === 0 ? w.color : ['#9fb4c7', '#c9d6df', '#7fa29a'][n % 3];
          g.fillRect(12 + (n % 4) * 14, 10 + i * 13, 50 + (n * 13) % 150, 6);
        }
        s.tex.needsUpdate = true;
      });
    }
  }

  // ───────────── 클릭 → 신분증 ─────────────
  const ray = new THREE.Raycaster(); const mouse = new THREE.Vector2();
  const isShown = w => w.present && w.mode !== 'away';
  function pickWorker(ev) {
    const rect = renderer.domElement.getBoundingClientRect();
    mouse.set((ev.clientX - rect.left) / rect.width * 2 - 1, -(ev.clientY - rect.top) / rect.height * 2 + 1);
    ray.setFromCamera(mouse, camera);
    const targets = []; for (const w of workers.values()) if (isShown(w)) targets.push(w.root, w.label.sprite);
    const hit = ray.intersectObjects(targets, true)[0];
    if (!hit) return null;
    for (const w of workers.values()) { let o = hit.object; while (o) { if (o === w.root || o === w.label.sprite) return w; o = o.parent; } }
    return null;
  }
  let down = null;
  renderer.domElement.addEventListener('pointerdown', e => { down = [e.clientX, e.clientY]; });
  renderer.domElement.addEventListener('pointerup', e => {
    if (!down || Math.hypot(e.clientX - down[0], e.clientY - down[1]) > 6) return;
    const w = pickWorker(e);
    if (w) { selected = w.id; window.openOfficeCard?.(w.id); }
  });
  renderer.domElement.addEventListener('pointermove', e => { renderer.domElement.style.cursor = pickWorker(e) ? 'pointer' : 'grab'; });

  // 얼굴 사진 (신분증용)
  const portraitRenderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true, alpha: false });
  portraitRenderer.setSize(240, 280); portraitRenderer.outputColorSpace = THREE.SRGBColorSpace; portraitRenderer.toneMapping = THREE.ACESFilmicToneMapping;
  const portraitCam = new THREE.PerspectiveCamera(24, 240 / 280, 0.05, 10);
  function portrait(id) {
    const w = workers.get(id); if (!w) return '';
    const head = new THREE.Vector3();
    if (w.external) { const b = new THREE.Box3().setFromObject(w.external.holder); head.set((b.min.x + b.max.x) / 2, b.max.y - 0.14, (b.min.z + b.max.z) / 2); }
    else w.rig.head.getWorldPosition(head), head.y += 0.07;
    const fwd = new THREE.Vector3(-Math.sin(w.rotY), 0, -Math.cos(w.rotY));
    portraitCam.position.copy(head).addScaledVector(fwd, 0.95).add(new THREE.Vector3(0.12, 0.02, 0));
    portraitCam.lookAt(head.clone().add(new THREE.Vector3(0, -0.06, 0)));
    const hidden = []; for (const o of workers.values()) { hidden.push(o.label.sprite, o.bubble.sprite); }
    const was = hidden.map(o => o.visible);
    hidden.forEach(o => o.visible = false);
    const rootVis = w.root.visible; w.root.visible = true;
    const bg = scene.background.clone(); scene.background = new THREE.Color('#e8e2d8');
    portraitRenderer.shadowMap.enabled = false;
    portraitRenderer.render(scene, portraitCam);
    scene.background = bg; w.root.visible = rootVis;
    hidden.forEach((o, i) => o.visible = was[i]);
    return portraitRenderer.domElement.toDataURL('image/png');
  }

  // ───────────── UI에서 오는 상태 ─────────────
  window.updateOfficeScene = (agentState = {}, events = [], state = null, extra = {}) => {
    const now = performance.now();
    for (const w of workers.values()) {
      const s = agentState[w.id] || {};
      const ready = extra.ready ? extra.ready[w.id] !== false : true;
      w.inTeam = state?.runId ? (state.members || []).includes(w.id) : (extra.members ? extra.members.includes(w.id) : true);
      w.present = ready;
      const wasBusy = w.busy;
      w.busy = Boolean(s.busy);
      if (wasBusy && !w.busy) w.idleSince = now;
      if (!wasBusy && w.busy) w.blendUntil = clockT.elapsedTime + 0.6;
      w.task = s.task || '';
      // 보조 에이전트 = 공중을 나는 AI 비서 드론
      const dr = Array.isArray(s.drones) ? s.drones : [];
      if (dr.length > (w.droneTasks?.length || 0) && w.inited) say(w, '🛸 ' + String(dr[dr.length - 1]).slice(0, 40), 6000, 'work', true);
      w.droneTasks = dr;
      w.fatigue = Number(s.fatigue) || 0;
      w.quotaOut = Boolean(s.quotaOut);
      w.leave = s.leave && LEAVE_KINDS.includes(s.leave.kind) ? s.leave : null;
      w.leader = state?.leader === w.id && !!state?.runId;
      if (s.concern && s.concern !== w.lastConcern) { w.lastConcern = s.concern; say(w, s.concern.replace(/^우려\s*:\s*/, ''), 15000, 'concern', true); }
      else if (s.thought && s.thought !== w.lastThought) { w.lastThought = s.thought; say(w, s.thought.split('\n')[0], w.busy ? 22000 : 9000, 'work', true); }
      decide(w, !w.inited); w.inited = true;
      w.status = statusOf(w, state);
      drawLabel(w);
    }
    // 인계 애니메이션
    const ev = [...events].reverse().find(e => e.kind === 'handoff');
    if (ev && ev.seq !== lastHandoff && Date.now() - ev.ts < 6000) {
      lastHandoff = ev.seq;
      const [from, to] = ev.text.split(' · ')[0].split(' → ').map(s => s.trim());
      const a = [...workers.values()].find(w => w.name === from), b = [...workers.values()].find(w => w.name === to);
      if (a && b) flights.push({ from: a.pos.clone(), to: b.pos.clone(), start: performance.now(), color: a.color });
    }
  };
  let lastHandoff = null;
  const flights = [];

  // ───────────── AI 비서 드론 (보조 에이전트가 일할 때 주인 머리 위를 날며 작업) ─────────────
  function makeDrone(color) {
    const g = new THREE.Group();
    const shell = new THREE.Mesh(cgeo('droneBody', () => new THREE.SphereGeometry(0.11, 20, 14)), mat('#eef1f5', { rough: 0.35, metal: 0.2 }));
    shell.scale.set(1.25, 0.72, 1.25); g.add(shell);
    const band = new THREE.Mesh(cgeo('droneBand', () => new THREE.TorusGeometry(0.128, 0.018, 8, 28)), mat(color, { rough: 0.4, emissive: color, ei: 0.35 }));
    band.rotation.x = Math.PI / 2; g.add(band);
    const face = new THREE.Mesh(cgeo('droneFace', () => new THREE.SphereGeometry(0.062, 16, 10, 0, Math.PI * 2, 0, Math.PI / 2)), mat('#141820', { rough: 0.2 }));
    face.rotation.x = Math.PI / 2; face.position.set(0, -0.005, 0.105); g.add(face);
    const eye = new THREE.Mesh(cgeo('droneEye', () => new THREE.SphereGeometry(0.016, 10, 8)), new THREE.MeshBasicMaterial({ color: '#7df9ff' }));
    eye.position.set(0, 0, 0.152); g.add(eye); g.userData.eye = eye;
    g.userData.rotors = [];
    for (let i = 0; i < 4; i++) {
      const a = Math.PI / 4 + i * Math.PI / 2;
      const arm = new THREE.Mesh(cgeo('droneArm', () => new THREE.BoxGeometry(0.15, 0.014, 0.022)), mat('#c9ced6', { rough: 0.5 }));
      arm.position.set(Math.cos(a) * 0.15, 0.03, Math.sin(a) * 0.15); arm.rotation.y = -a; g.add(arm);
      const rotor = new THREE.Mesh(cgeo('droneRotor', () => new THREE.CylinderGeometry(0.07, 0.07, 0.004, 18)), mat('#9fb4c8', { transparent: true, opacity: 0.45 }));
      rotor.position.set(Math.cos(a) * 0.22, 0.045, Math.sin(a) * 0.22); g.add(rotor); g.userData.rotors.push(rotor);
    }
    // 작업 중임을 보여 주는 빛줄기 (책상 쪽으로)
    const beam = new THREE.Mesh(cgeo('droneBeam', () => { const c = new THREE.ConeGeometry(0.16, 1, 20, 1, true); c.translate(0, -0.5, 0); return c; }),
      new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.18, depthWrite: false, side: THREE.DoubleSide }));
    scene.add(beam); g.userData.beam = beam; // 빛줄기는 드론과 따로 (항상 아래로)
    g.scale.setScalar(0.01);
    scene.add(g);
    return g;
  }
  function updateDrones(w, t, dt, shown) {
    if (!w.drones) w.drones = [];
    const want = shown && w.present ? Math.min(3, Math.max((w.droneTasks || []).length, w.testN || 0)) : 0;
    while (w.drones.length < want) w.drones.push({ g: makeDrone(w.color), born: t, leaving: false });
    w.drones.forEach((d, i) => { if (i >= want) d.leaving = true; else d.leaving = false; });
    const n = Math.max(1, want);
    for (let i = w.drones.length - 1; i >= 0; i--) {
      const d = w.drones[i], g = d.g;
      const k = Math.min(1, (t - d.born) / 0.8);
      const target = d.leaving ? 0 : 1;
      d.s = (d.s ?? 0) + (target - (d.s ?? 0)) * Math.min(1, dt * 5);
      if (d.leaving && d.s < 0.03) { scene.remove(g); scene.remove(g.userData.beam); w.drones.splice(i, 1); continue; }
      const ang = t * 0.7 + i * Math.PI * 2 / n;
      const r = 0.75 + Math.sin(t * 0.9 + i) * 0.08;
      const y = 2.15 + Math.sin(t * 2.2 + i * 1.7) * 0.07 + (1 - k) * 0.6;
      g.position.set(w.pos.x + Math.cos(ang) * r, y, w.pos.z + Math.sin(ang) * r);
      // 주인 쪽을 바라보며 살짝 기울어 날기
      g.lookAt(w.pos.x, y - 0.25, w.pos.z);
      g.rotateZ(Math.sin(t * 1.5 + i) * 0.12);
      g.scale.setScalar(Math.max(0.01, d.s) * 1.35);
      for (const rt of g.userData.rotors) rt.rotation.y += dt * 40;
      g.userData.beam.material.opacity = 0.1 + (Math.sin(t * 5 + i) * 0.5 + 0.5) * 0.14;
      const bm = g.userData.beam;
      bm.position.set(g.position.x, y - 0.06, g.position.z);
      bm.scale.set(d.s, Math.max(0.1, y - 0.86), d.s);
      bm.visible = shown && d.s > 0.2;
      g.userData.eye.material.color.setHSL(0.52, 1, 0.6 + Math.sin(t * 6 + i) * 0.15);
      g.visible = shown;
    }
  }
  const paperGeo = new THREE.BoxGeometry(0.22, 0.01, 0.3);

  // ───────────── 바깥에서 쓰는 기능 ─────────────
  window.officeAPI = {
    agents: AGENTS,
    getProfile: id => ({ ...workers.get(id).profile }),
    defaults: id => ({ ...DEFAULT_PROFILES[id] }),
    setProfile(id, patch) {
      const w = workers.get(id); Object.assign(w.profile, patch); saveProfile(w);
      const rigKeys = ['hair', 'hairColor', 'skin', 'outfit', 'top', 'accent', 'bottom', 'shoes', 'build', 'glasses', 'headset', 'height'];
      if (Object.keys(patch).some(k => rigKeys.includes(k))) rebuild(w);
      if (Object.keys(patch).some(k => ['modelRot', 'modelY', 'modelScale', 'height'].includes(k))) applyExternalTweaks(w);
      if ('clip' in patch) playClip(w);
      w.label.key = ''; drawLabel(w);
      if (w.desk.sign) w.desk.sign.key = '';
    },
    resetProfile(id) {
      const w = workers.get(id); const keepModel = w.profile.model;
      w.profile = { ...DEFAULT_PROFILES[id], model: keepModel }; saveProfile(w); rebuild(w); applyExternalTweaks(w);
    },
    async uploadModel(id, file) {
      const w = workers.get(id);
      const buf = await file.arrayBuffer();
      const { object, clips } = await parseModel(buf, file.name);
      attachExternal(w, object, clips);
      w.profile.model = file.name; saveProfile(w);
      const r = await fetch('/api/avatar?agent=' + id + '&name=' + encodeURIComponent(file.name), { method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body: buf });
      const j = await r.json(); if (!j.ok) throw new Error(j.error || '저장 실패');
      return clips.map(c => c.name);
    },
    clips: id => (workers.get(id).external?.clips || []).map(c => c.name),
    hasModel: id => !!workers.get(id).external,
    async clearModel(id) { clearExternal(workers.get(id)); await fetch('/api/avatar-clear?agent=' + id, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }); },
    room: () => ({ ...room }),
    setRoom(patch) { Object.assign(room, patch); saveRoom(); applyRoom(); },
    view(name) { if (name === 'first') firstPersonOf(selected); else setView(name); return viewName; },
    firstPerson: id => firstPersonOf(id),
    lounge(on) { setLounge(on ?? !lounge); if (lounge) setView('lounge'); else setView('default'); return lounge; },
    isLounge: () => lounge,
    xray(on) { setXray(on ?? !xray); return xray; },
    portrait,
    select: id => { selected = id; },
    // 화면 테마(dark|light|warm|navy|forest)에 맞춰 분위기를 바꾼다. 저장된 방 설정은 건드리지 않는다.
    setTheme(name) { theme = THEMES[name] ? name : null; applyRoom(); return theme; },
    theme: () => theme,
    restRoom() { setView('rest'); return viewName; },
    // 시험용: 드론 띄워 보기  officeAPI.testDrones('claude', 2)
    testDrones(id, n = 1) { const w = workers.get(id); if (w) w.testN = n; },
    droneInfo: () => [...workers.values()].map(w => ({ id: w.id, test: w.testN || 0, tasks: (w.droneTasks || []).length, flying: (w.drones || []).length, present: w.present, pos: w.drones?.[0]?.g.position.toArray().map(v => +v.toFixed(2)), scale: w.drones?.[0]?.g.scale.x })),
  };

  // ───────────── 크기·루프 ─────────────
  new ResizeObserver(() => {
    const w = host.clientWidth, h = host.clientHeight; if (!w || !h) return;
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
  }).observe(host);

  const qa = new THREE.Quaternion(), qb = new THREE.Quaternion(), eul = new THREE.Euler();
  function saveJoints(w) {
    const r = w.rig, a = w.prevJ; let i = 0;
    for (const j of r.joints) { a[i++] = j.rotation.x; a[i++] = j.rotation.y; a[i++] = j.rotation.z; }
    a[i++] = r.hips.position.y; a[i++] = r.hips.position.z;
  }
  function blendJoints(w, k) {
    const r = w.rig, a = w.prevJ; let i = 0;
    for (const j of r.joints) { j.rotation.x = a[i] + (j.rotation.x - a[i]) * k; i++; j.rotation.y = a[i] + (j.rotation.y - a[i]) * k; i++; j.rotation.z = a[i] + (j.rotation.z - a[i]) * k; i++; }
    r.hips.position.y = a[i] + (r.hips.position.y - a[i]) * k; i++;
    r.hips.position.z = a[i] + (r.hips.position.z - a[i]) * k;
  }

  let screenTimer = 0, decideTimer = 0;
  renderer.setAnimationLoop(() => {
    const dt = Math.min(0.05, clockT.getDelta()), t = clockT.elapsedTime, nowMs = performance.now();
    decideTimer += dt;
    if (decideTimer > 0.5) { decideTimer = 0; for (const w of workers.values()) if (w.inited) { decide(w, false); const st = statusOf(w, null); if (st !== w.status && (w.leave || w.mode === 'nap' || w.status === '수면실에서 쉬는 중')) { w.status = st; drawLabel(w); } } }
    let nearDoor = false;
    for (const w of workers.values()) {
      stepWalk(w, dt);
      // 딴짓 고르기·일하다 한 모금
      const atDesk = w.mode === 'desk' && w.pose === 'sit' && !w.path.length;
      if (room.motion && atDesk && !w.busy && w.present && !w.external) { if (!w.beh || t > w.beh.end) pickBehavior(w, t); }
      else if (w.beh) { w.beh = null; w.blendUntil = t + 0.6; }
      if (atDesk && w.busy && room.motion) {
        if (!w.nextSip) w.nextSip = t + rand(15, 40);
        if (t > w.nextSip) { w.sipStart = t; w.nextSip = t + rand(30, 70); if (room.bubbles && canQuip(w) && Math.random() < 0.25) say(w, pick(SIP_LINES[w.id] || SIP_LINES.gemini), 3500, 'quip'); }
      }
      if (w.pose !== w.lastPose) { w.lastPose = w.pose; w.blendUntil = t + 0.45; }
      const blending = t < w.blendUntil && w.pose !== 'walk' && w.rig;
      if (blending) saveJoints(w);
      setPose(w, t, dt);
      if (blending) blendJoints(w, Math.min(1, dt * 9));
      w.spin += (w.spinTarget - w.spin) * Math.min(1, dt * 4);
      const spinning = w.mode === 'desk' && w.pose === 'sit' && !w.path.length;
      if (!spinning) w.spin = 0;
      if (w.desk.seat) w.desk.seat.rotation.y = w.spin;
      w.root.position.copy(w.pos); w.root.rotation.y = w.rotY + w.spin;
      // 손에 든 컵은 항상 똑바로 (마실 때만 살짝 기울인다)
      const P = w.rig?.props;
      if (P?.drink?.visible) {
        w.rig.arms[1].el.getWorldQuaternion(qa).invert();
        qb.setFromEuler(eul.set(w.drinkTilt, w.rotY + w.spin, 0, 'YXZ'));
        P.drink.quaternion.copy(qa).multiply(qb);
      }
      if (w.mixer && (room.motion)) w.mixer.update(dt);
      const shown = isShown(w);
      w.root.visible = shown; w.ring.visible = shown;
      w.label.sprite.visible = shown && room.labels;
      if (w.pose === 'walk' && Math.hypot(w.pos.x - DOOR.x, w.pos.z - DOOR.z) < 1.5) nearDoor = true;
      // 졸 때는 Zzz
      if (w.sleepy && shown && room.bubbles && !(nowMs < w.bubbleUntil && w.bubbleText && w.bubbleKind !== 'zzz')) {
        w.bubbleKind = 'zzz'; w.bubbleText = ['z', 'zZ', 'zZz'][Math.floor(t * 1.3) % 3]; w.bubbleUntil = nowMs + 400;
      }
      let hx = w.pos.x, hz = w.pos.z;
      let headY = w.external ? 1.9 : (w.pose === 'walk' || w.pose === 'stand' ? 1.95 : 1.55);
      if (w.pose === 'lie') { hx += Math.sin(w.rotY) * 1.55; hz += Math.cos(w.rotY) * 1.55; headY = 0.95; }
      w.label.sprite.position.set(hx, headY + 0.12, hz);
      const show = room.bubbles && w.bubbleText && nowMs < w.bubbleUntil && shown;
      w.bubble.sprite.visible = !!show; if (show) { drawBubble(w); w.bubble.sprite.position.set(hx, headY + 0.12, hz); }
      updateDrones(w, t, dt, shown);
      w.ring.position.set(w.pos.x, 0.02, w.pos.z);
      w.ring.material.opacity = w.busy ? 0.55 + Math.sin(t * 4) * 0.25 : 0;
      if (w.desk.glow) w.desk.glow.color.setHSL((t * 0.12) % 1, 0.95, 0.55);
    }
    // 문 여닫기
    const doorTarget = nearDoor ? -1.3 : 0;
    doorPivot.rotation.y += (doorTarget - doorPivot.rotation.y) * Math.min(1, dt * 4);
    // 인계 서류 비행
    for (let i = flights.length - 1; i >= 0; i--) {
      const f = flights[i]; const p = (performance.now() - f.start) / 1500;
      if (!f.mesh) { f.mesh = new THREE.Mesh(paperGeo, mat('#fffaf0')); const tag = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.012, 0.05), mat(f.color)); tag.position.z = -0.12; f.mesh.add(tag); scene.add(f.mesh); }
      if (p >= 1) { scene.remove(f.mesh); flights.splice(i, 1); continue; }
      f.mesh.position.lerpVectors(f.from, f.to, p); f.mesh.position.y = 1.2 + Math.sin(Math.PI * p) * 1.1; f.mesh.rotation.y = p * Math.PI * 2;
    }
    screenTimer += dt; if (screenTimer > 0.25) { screenTimer = 0; drawScreens(t); }
    const now = new Date(); handH.rotation.z = -((now.getHours() % 12) + now.getMinutes() / 60) * Math.PI / 6; handM.rotation.z = -now.getMinutes() * Math.PI / 30;
    if (camAnim) {
      const k = Math.min(1, (performance.now() - camAnim.s) / camAnim.ms), e = k < 0.5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;
      camera.position.lerpVectors(camAnim.p0, camAnim.p1, e); controls.target.lerpVectors(camAnim.t0, camAnim.t1, e);
      if (k >= 1) camAnim = null;
    }
    if (firstPerson) updateFirstPerson(); else controls.update();
    renderer.render(scene, camera);
  });
}
