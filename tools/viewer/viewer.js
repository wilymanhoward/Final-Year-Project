// Replay viewer for the FYP run logs. Scene meshes come from FYP > Export Web Viewer Scenes (scenes/case_X|Y.obj + .json).
// Everything (meshes, CSV positions, quaternions) stays in Unity's CaseRoot-local frame inside `world`, which is
// mirrored on X once: Unity is left-handed, three.js right-handed (three flips the face winding for a mirrored matrix).
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';

const COLORS = [0xff9a3c, 0x3cd0ff];
const EVENT_COLORS = {
  error_contamination: '#d70015', error_reasoning: '#d70015', partner_contamination: '#d70015', clue_found: '#248a3d',
  clue_tagged: '#c98a00', clue_untagged: '#a07a30', answer_submitted: '#0066cc', board_suspect: '#0066cc',
  board_evidence: '#0066cc', clue_thrown: '#c93400', session_start: '#8e8e93', session_end: '#8e8e93',
};
const HIDDEN_EVENTS = new Set(['hand_tracking_lost', 'hand_tracking_regained']);
const $ = (id) => document.getElementById(id);

// ---------- three.js scene ----------
const view = $('stage');
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
view.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x272729);
const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 100);
camera.position.set(0, 5.5, 5.5);
const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.6, 0);
scene.add(new THREE.HemisphereLight(0xffffff, 0x444450, 1.6));
const sun = new THREE.DirectionalLight(0xffffff, 1.4);
sun.position.set(2, 5, 3);
scene.add(sun);
const grid = new THREE.GridHelper(10, 20, 0x3a3a3c, 0x313133);
grid.position.y = -0.01;
scene.add(grid);

const world = new THREE.Group();   // Unity CaseRoot frame
world.scale.x = -1;
scene.add(world);
let caseGroup = null, zoneMeshes = {}, clueMarkers = {}, cluePivots = {}, loadedCase = null, sceneNote = '';

function resize() {
  const w = view.clientWidth, h = view.clientHeight;
  renderer.setSize(w, h);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
new ResizeObserver(resize).observe(view);

// ---------- case scene ----------
const texLoader = new THREE.TextureLoader();
function tex(file) {
  const t = texLoader.load('scenes/' + file);
  t.colorSpace = THREE.SRGBColorSpace;
  t.magFilter = THREE.NearestFilter;   // palette cells must not bleed into each other
  t.minFilter = THREE.NearestFilter;
  return t;
}
let materials = null;
function propMaterials() {
  if (materials) return materials;
  const palette = tex('Palette.png');
  const lit = (o) => new THREE.MeshLambertMaterial({ map: palette, ...o });
  materials = {
    Props_Palette: lit({}),
    Props_Glass: lit({ transparent: true, opacity: 0.3, depthWrite: false }),
    Props_Floor: lit({ transparent: true, opacity: 0.7, depthWrite: false }),
    Props_ZoneOutline: new THREE.MeshBasicMaterial({ map: palette, transparent: true, opacity: 0.55, depthWrite: false }),
    Props_BlobShadow: new THREE.MeshBasicMaterial({ map: tex('BlobShadow.png'), transparent: true, opacity: 0.55, depthWrite: false }),
  };
  return materials;
}

function clue_pivot(id) {
  if (!cluePivots[id]) {
    const g = new THREE.Group();
    g.matrixAutoUpdate = false;
    caseGroup.add(g);
    cluePivots[id] = g;
  }
  return cluePivots[id];
}

async function loadCase(code) {
  if (loadedCase === code) return;
  if (caseGroup) world.remove(caseGroup);
  caseGroup = new THREE.Group();
  zoneMeshes = {}; clueMarkers = {}; cluePivots = {};
  world.add(caseGroup);
  loadedCase = code;
  sceneNote = '';
  const mats = propMaterials();
  try {
    const obj = await new OBJLoader().loadAsync(`scenes/case_${code}.obj`);
    obj.traverse((m) => {
      if (!m.isMesh) return;
      const pick = (mm) => mats[mm.name] || mats.Props_Palette;
      m.material = Array.isArray(m.material) ? m.material.map(pick) : pick(m.material);
      if (!Array.isArray(m.material) && m.material === mats.Props_Floor) m.renderOrder = -1;
    });
    caseGroup.add(obj);
    // Each clue's meshes go under a pivot that clue_pose rows move (OBJ names: Case_X/X_K4_Printed_email/...).
    for (const m of [...obj.children]) {
      const id = (m.name.match(/^Case_[XY]\/([XY]_[KR]\d)(?:_|\/|$)/) || [])[1];
      if (id) clue_pivot(id).add(m);
    }
  } catch (e) {
    loadedCase = null;   // retry on the next load
    sceneNote = `Scene meshes for case ${code} not found: run FYP > Export Web Viewer Scenes in Unity, then reload.`;
  }
  try {
    const info = await (await fetch(`scenes/case_${code}.json`)).json();
    setCaseNames(info);
    for (const z of info.zones) {
      const box = new THREE.Mesh(new THREE.BoxGeometry(z.sx, 2, z.sz),
        new THREE.MeshBasicMaterial({ color: 0xff3030, transparent: true, opacity: 0.06, depthWrite: false }));
      box.add(new THREE.LineSegments(new THREE.EdgesGeometry(box.geometry),
        new THREE.LineBasicMaterial({ color: 0xff5050, transparent: true, opacity: 0.35 })));
      box.position.set(z.x, 1, z.z);
      box.rotation.y = THREE.MathUtils.degToRad(z.yaw);
      caseGroup.add(box);
      zoneMeshes[z.id] = box;
    }
    for (const c of info.clues) {
      const ring = new THREE.Mesh(new THREE.TorusGeometry(0.09, 0.008, 6, 24),
        new THREE.MeshBasicMaterial({ color: 0x777777, transparent: true, opacity: 0.8 }));
      ring.position.set(c.x, c.y + 0.02, c.z);
      ring.rotation.x = Math.PI / 2;
      clue_pivot(c.id).add(ring);
      clueMarkers[c.id] = ring;
    }
  } catch { /* zones/clues are optional extras */ }
}

// ---------- CSV ----------
function parseCsv(text) {
  const rows = []; let row = [], cell = '', q = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (q) {
      if (ch === '"') { if (text[i + 1] === '"') { cell += '"'; i++; } else q = false; }
      else cell += ch;
    } else if (ch === '"') q = true;
    else if (ch === ',') { row.push(cell); cell = ''; }
    else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = ''; if (row.length > 1) rows.push(row); row = [];
    } else cell += ch;
  }
  if (cell || row.length) { row.push(cell); rows.push(row); }
  return rows;
}
const num = (s) => (s === '' || s === undefined ? null : Number(s));
const detailMap = (d) => Object.fromEntries(d.split(';').filter((p) => p.includes('=')).map((p) => p.split(/=(.*)/s).slice(0, 2)));

// {base}_hands.csv (HandJointCsv in Unity): session time, utc, then x,y,z of 25 joints per hand (left, right),
// CaseRoot-local in Unity's frame; an untracked hand is empty.
const JOINT_NAMES = ['wrist', 'thumb-metacarpal', 'thumb-phalanx-proximal', 'thumb-phalanx-distal', 'thumb-tip',
  ...['index', 'middle', 'ring', 'pinky'].flatMap((f) => ['metacarpal', 'phalanx-proximal', 'phalanx-intermediate', 'phalanx-distal', 'tip'].map((s) => `${f}-finger-${s}`))];
const JOINT_INDEX = Object.fromEntries(JOINT_NAMES.map((n, i) => [n, i]));
function buildHands(text) {
  const [header, ...body] = parseCsv(text);
  if (header.length !== 2 + 150 || header[2] !== 'l_wrist_x') return null;
  const t = [], l = [], r = [];
  const side = (row, o) => (row[o] === '' ? null : Float32Array.from(row.slice(o, o + 75), Number));
  for (const row of body) {
    if (row.length !== 152) continue;
    t.push(Number(row[0])); l.push(side(row, 2)); r.push(side(row, 77));
  }
  return t.length ? { t, l, r } : null;
}

function buildRun(name, text) {
  const [header, ...body] = parseCsv(text);
  const ix = Object.fromEntries(header.map((h, i) => [h, i]));
  if (ix.head_x === undefined || ix.session_time_s === undefined) throw new Error(`${name}: not a run log`);
  const g = (r, k) => r[ix[k]];
  const first = body[0];
  const run = {
    name, pair: g(first, 'pair_code'), self: g(first, 'participant_id'), cond: g(first, 'condition'), case: g(first, 'case'),
    startUtc: Date.parse(g(first, 'utc_time')) - 1000 * Number(g(first, 'session_time_s')),
    samples: [], events: [], poses: {},
  };
  for (const r of body) {
    const t = Number(g(r, 'session_time_s'));
    const hx = num(g(r, 'head_x'));
    const s = {
      t,
      head: hx === null ? null : [hx, num(g(r, 'head_y')), num(g(r, 'head_z'))],
      q: num(g(r, 'head_qw')) === null ? null : [num(g(r, 'head_qx')), num(g(r, 'head_qy')), num(g(r, 'head_qz')), num(g(r, 'head_qw'))],
      l: num(g(r, 'lhand_x')) === null ? null : [num(g(r, 'lhand_x')), num(g(r, 'lhand_y')), num(g(r, 'lhand_z'))],
      r: num(g(r, 'rhand_x')) === null ? null : [num(g(r, 'rhand_x')), num(g(r, 'rhand_y')), num(g(r, 'rhand_z'))],
    };
    if (g(r, 'row_type') === 'object') {
      if (g(r, 'event') === 'clue_pose') {
        const [id] = g(r, 'detail').split(';');
        const d = detailMap(g(r, 'detail'));
        (run.poses[id] ||= []).push({ t, p: [+d.x, +d.y, +d.z], q: [+d.qx, +d.qy, +d.qz, +d.qw], held: d.held === '1' });
      }
      continue;   // object rows are not body samples
    }
    run.samples.push(s);
    if (g(r, 'row_type') === 'event') {
      const detail = g(r, 'detail');
      const d = detailMap(detail);
      // Team logs repeat the partner's events; each file contributes only its own (by= missing or self).
      if (d.by && d.by !== run.self) continue;
      run.events.push({ t, type: g(r, 'event'), detail, d });
    }
  }
  return run;
}

// ---------- players ----------
// Avatars live in `scene` (not the mirrored `world`) so the face and the left/right hand models aren't mirrored:
// Unity position (x,y,z) -> (-x,y,z), rotation (qx,qy,qz,qw) -> (qx,-qy,-qz,qw). Trails and floor discs stay in `world`.
const HEAD_URL = 'https://cdn.jsdelivr.net/gh/mrdoob/three.js@r160/examples/models/gltf/LeePerrySmith/';
const HAND_URL = 'https://cdn.jsdelivr.net/npm/@webxr-input-profiles/assets@1.0.19/dist/profiles/generic-hand/';
const avatars = new THREE.Group();
scene.add(avatars);
const avatarAssets = (async () => {
  const gl = new GLTFLoader();
  const [h, l, r] = await Promise.all(['LeePerrySmith.glb'].map((f) => gl.loadAsync(HEAD_URL + f))
    .concat(['left.glb', 'right.glb'].map((f) => gl.loadAsync(HAND_URL + f))));
  h.scene.updateMatrixWorld(true);
  let mesh = null;
  h.scene.traverse((o) => { if (o.isMesh && !mesh) mesh = o; });
  const geo = headOnly(mesh.geometry.clone().applyMatrix4(mesh.matrixWorld));   // the face looks along +Z
  const tl = new THREE.TextureLoader();
  const map = tl.load(HEAD_URL + 'Map-COL.jpg'); map.colorSpace = THREE.SRGBColorSpace; map.flipY = false;
  const normalMap = tl.load(HEAD_URL + 'Infinite-Level_02_Tangent_SmoothUV.jpg'); normalMap.flipY = false;
  const headMat = new THREE.MeshStandardMaterial({ map, normalMap, roughness: 0.62, metalness: 0 });
  return { headGeo: geo, headMat, hands: { l: prepareHand(l.scene, 'l'), r: prepareHand(r.scene, 'r') } };
})();
avatarAssets.catch((e) => console.warn('Avatar models not loaded, using simple markers:', e));

// The scan is a bust (shoulders + head). Fractions of its height, measured once on this model: below 0.27 = shoulders
// (cut off), neck from 0.46, eyes at 0.725. Scaled so the head is 20 cm nose-to-back; origin = the headset's centre
// eye (eye height, 3 cm behind the brow), like the partner head in the app.
function headOnly(src) {
  let g = src.index ? src.toNonIndexed() : src;
  g.computeBoundingBox();
  const b = g.boundingBox, h = b.max.y - b.min.y;
  g.translate(-(b.min.x + b.max.x) / 2, -b.min.y, 0).scale(1 / h, 1 / h, 1 / h);
  const pos = g.attributes.position, keep = [];
  for (let t = 0; t < pos.count; t += 3)
    if (Math.min(pos.getY(t), pos.getY(t + 1), pos.getY(t + 2)) > 0.27) keep.push(t, t + 1, t + 2);
  const out = new THREE.BufferGeometry();
  for (const [name, attr] of Object.entries(g.attributes)) {
    const arr = new Float32Array(keep.length * attr.itemSize);
    keep.forEach((v, i) => { for (let c = 0; c < attr.itemSize; c++) arr[i * attr.itemSize + c] = attr.array[v * attr.itemSize + c]; });
    out.setAttribute(name, new THREE.BufferAttribute(arr, attr.itemSize));
  }
  const p = out.attributes.position;
  let zBack = 9, zFront = -9, brow = -9;
  for (let i = 0; i < p.count; i++) {
    const y = p.getY(i), z = p.getZ(i);
    if (y > 0.46) { zBack = Math.min(zBack, z); zFront = Math.max(zFront, z); }
    if (Math.abs(y - 0.725) < 0.03) brow = Math.max(brow, z);
  }
  const s = 0.2 / (zFront - zBack);
  return out.translate(0, -0.725, -(brow - 0.03 / s)).scale(s, s, s);
}

function label(text, color) {
  const c = document.createElement('canvas'); c.width = 256; c.height = 96;
  const g = c.getContext('2d');
  g.fillStyle = '#' + color.toString(16).padStart(6, '0');
  g.beginPath(); g.roundRect(8, 8, 240, 80, 40); g.fill();
  g.fillStyle = '#1d1d1f'; g.font = '600 48px system-ui, -apple-system, Segoe UI, sans-serif';
  g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(text, 128, 50);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true }));
  sp.scale.set(0.2, 0.075, 1); sp.renderOrder = 10;
  return sp;
}

// Measures a hand model from its bones (fingers = wrist -> middle knuckle, back of the hand from the knuckle line) so
// poseHand doesn't depend on how the model is authored, then bends the flat bind pose into relaxed, slightly curled fingers.
function prepareHand(root, side) {
  root.updateMatrixWorld(true);
  const P = (n) => root.getObjectByName(n).getWorldPosition(new THREE.Vector3());
  const wrist = P('wrist'), f = P('middle-finger-phalanx-proximal').sub(wrist).normalize();
  const across = P('index-finger-phalanx-proximal').sub(P('pinky-finger-phalanx-proximal')).normalize();
  const back = side === 'r' ? new THREE.Vector3().crossVectors(f, across) : new THREE.Vector3().crossVectors(across, f);
  back.addScaledVector(f, -back.dot(f)).normalize();
  const z = f.clone().negate(), x = new THREE.Vector3().crossVectors(back, z);
  const qInv = new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(x, back, z)).invert();
  const pq = new THREE.Quaternion(), tip = new THREE.Vector3(), moved = new THREE.Vector3();
  for (const finger of ['index', 'middle', 'ring', 'pinky']) {
    ['proximal', 'intermediate', 'distal'].forEach((seg, i) => {
      const bone = root.getObjectByName(`${finger}-finger-phalanx-${seg}`);
      const child = root.getObjectByName(i < 2 ? `${finger}-finger-phalanx-${['intermediate', 'distal'][i]}` : `${finger}-finger-tip`);
      bone.parent.getWorldQuaternion(pq);
      const axis = across.clone().applyQuaternion(pq.invert());
      const angle = [0.3, 0.38, 0.3][i] + (finger === 'pinky' ? 0.1 : 0);
      child.getWorldPosition(tip);
      bone.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(axis, angle));
      root.updateMatrixWorld(true);
      if (child.getWorldPosition(moved).sub(tip).dot(back) > 0)   // curled towards the back of the hand: bend the other way
        { bone.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(axis, -2 * angle)); root.updateMatrixWorld(true); }
    });
  }
  return { scene: root, qInv, wrist };
}

function makePlayer(i, name, assets) {
  const color = COLORS[i];
  const tint = new THREE.Color(0xe3b49a).lerp(new THREE.Color(color), 0.28);
  const head = new THREE.Group();
  if (assets) {
    head.add(new THREE.Mesh(assets.headGeo, assets.headMat));
    // a thin headset strap in the player's colour (a full visor would hide the face)
    const strap = new THREE.Mesh(new THREE.TorusGeometry(0.1, 0.007, 8, 40), new THREE.MeshStandardMaterial({ color, roughness: 0.45 }));
    strap.rotation.x = Math.PI / 2 - 0.25; strap.scale.set(0.8, 1.0, 1); strap.position.set(0, 0.02, -0.075);
    head.add(strap);
  } else {
    const mat = new THREE.MeshLambertMaterial({ color });
    head.add(new THREE.Mesh(new THREE.SphereGeometry(0.11, 16, 12), mat));
    const nose = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.08, 8), mat);
    nose.rotation.x = Math.PI / 2; nose.position.set(0, 0, 0.14); head.add(nose);
  }
  const gaze = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0.1), new THREE.Vector3(0, 0, 4)]),
    new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.7 }));
  head.add(gaze);
  const hand = (side) => {
    if (!assets) return new THREE.Mesh(new THREE.SphereGeometry(0.04, 12, 8), new THREE.MeshLambertMaterial({ color }));
    const h = cloneSkinned(assets.hands[side].scene);
    const mat = new THREE.MeshStandardMaterial({ color: tint, roughness: 0.55 });
    h.traverse((o) => { if (o.isMesh) { o.material = mat; o.frustumCulled = false; } });
    h.userData.cal = assets.hands[side];
    h.userData.bones = {};
    h.traverse((o) => { if (o.isBone) h.userData.bones[o.name] = o; });
    h.userData.rest = Object.values(h.userData.bones).map((b) => [b, b.quaternion.clone()]);
    return h;
  };
  const left = hand('l'), right = hand('r');
  const tag = label(name, color);
  avatars.add(head, left, right, tag);
  const shadow = new THREE.Mesh(new THREE.CircleGeometry(0.2, 32),
    new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.35, depthWrite: false }));
  shadow.rotation.x = -Math.PI / 2;
  const trail = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.25 }));
  const recent = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color }));
  const group = new THREE.Group();
  group.add(shadow, trail, recent);
  world.add(group);
  return { group, head, left, right, tag, gaze, shadow, trail, recent, real: !!assets };
}
function removePlayer(p) { world.remove(p.group); avatars.remove(p.head, p.left, p.right, p.tag); }

// Hand rotation isn't logged: fingers point from an estimated shoulder to the logged hand, back of the hand up,
// rolled 35 deg outwards (palms a little towards each other); the model's own axes come from prepareHand.
const _hf = new THREE.Vector3(), _hb = new THREE.Vector3(), _ho = new THREE.Vector3(), _hx = new THREE.Vector3(),
  _hr = new THREE.Vector3(), _hs = new THREE.Vector3(), _hm = new THREE.Matrix4(), UP = new THREE.Vector3(0, 1, 0);
function poseHand(obj, pos, headPos, headQ, side) {
  obj.position.copy(pos);
  if (!headPos) return;
  _hr.set(-1, 0, 0).applyQuaternion(headQ); _hr.y = 0;
  if (_hr.lengthSq() < 1e-6) _hr.set(-1, 0, 0);
  _hr.normalize();                                                       // participant's right, in three's frame
  _hs.copy(headPos).addScaledVector(UP, -0.28).addScaledVector(_hr, side === 'l' ? -0.17 : 0.17);   // shoulder
  _hf.subVectors(pos, _hs);
  if (_hf.lengthSq() < 1e-4) _hf.set(0, 0, 1).applyQuaternion(headQ);
  _hf.normalize();
  _hb.copy(UP).addScaledVector(_hf, -_hf.dot(UP));
  if (_hb.lengthSq() < 1e-4) _hb.set(0, 0, -1);
  _hb.normalize();
  _ho.copy(_hr).multiplyScalar(side === 'l' ? -1 : 1).addScaledVector(_hf, -_hf.dot(_hr)).normalize();
  _hb.multiplyScalar(Math.cos(0.61)).addScaledVector(_ho, Math.sin(0.61)).normalize();
  _hx.crossVectors(_hb, _hf.negate());                                  // x = y × z with z = -fingers
  _hm.makeBasis(_hx, _hb, _hf);
  obj.quaternion.setFromRotationMatrix(_hm);
  const cal = obj.userData.cal;
  if (cal) {   // model axes -> target axes, and put the model's wrist (not its origin) on the logged point
    obj.quaternion.multiply(cal.qInv);
    obj.position.sub(_hs.copy(cal.wrist).applyQuaternion(obj.quaternion));
  }
}

// ---------- hands from the joint log ----------
const _joints = new Float32Array(75);
const FINGER_CHAINS = [['thumb-metacarpal', 'thumb-phalanx-proximal', 'thumb-phalanx-distal', 'thumb-tip'],
  ...['index', 'middle', 'ring', 'pinky'].map((f) => ['metacarpal', 'phalanx-proximal', 'phalanx-intermediate', 'phalanx-distal', 'tip'].map((s) => `${f}-finger-${s}`))];
function resetBones(obj) { if (obj.userData.rest) for (const [b, q] of obj.userData.rest) b.quaternion.copy(q); }

// Joints of one hand at time t (rows every 0.05 s: interpolate inside 0.12 s, else hold); false = not tracked then.
function jointsAt(h, t, side, out) {
  const ts = h.t, rows = h[side];
  if (t < ts[0] - 0.1 || t > ts[ts.length - 1] + 0.2) return false;
  let lo = 0, hi = ts.length - 1;
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (ts[m] <= t) lo = m; else hi = m; }
  if (ts[hi] <= t) lo = hi;
  const nx = Math.min(lo + 1, ts.length - 1), A = rows[lo], B = rows[nx];
  if (!A) return false;
  const dt = ts[nx] - ts[lo];
  if (B && nx !== lo && dt > 0 && dt <= 0.12) {
    const k = Math.min(1, Math.max(0, (t - ts[lo]) / dt));
    for (let i = 0; i < 75; i++) out[i] = A[i] + (B[i] - A[i]) * k;
  } else out.set(A);
  return true;
}

const _jw = new THREE.Vector3(), _ja = new THREE.Vector3(), _jb = new THREE.Vector3(), _jc = new THREE.Vector3(),
  _jd = new THREE.Vector3(), _jt = new THREE.Vector3(), _jr = new THREE.Quaternion(), _jbw = new THREE.Quaternion(), _jpw = new THREE.Quaternion();
const jointPos = (J, name, out) => { const i = JOINT_INDEX[name] * 3; return out.set(-J[i], J[i + 1], J[i + 2]); };   // Unity -> three
// Wrist frame from the wrist and three knuckles (same rule as prepareHand), then every finger bone turned so it points
// at its next logged joint. The model keeps its own finger lengths; only directions come from the log.
function poseFromJoints(obj, J, side) {
  resetBones(obj);
  const cal = obj.userData.cal, bones = obj.userData.bones;
  jointPos(J, 'wrist', _jw);
  _hf.subVectors(jointPos(J, 'middle-finger-phalanx-proximal', _ja), _jw).normalize();
  _jd.subVectors(jointPos(J, 'index-finger-phalanx-proximal', _jb), jointPos(J, 'pinky-finger-phalanx-proximal', _jc)).normalize();
  if (side === 'r') _hb.crossVectors(_hf, _jd); else _hb.crossVectors(_jd, _hf);
  _hb.addScaledVector(_hf, -_hb.dot(_hf)).normalize();
  _hf.negate();
  _hx.crossVectors(_hb, _hf);
  _hm.makeBasis(_hx, _hb, _hf);
  obj.quaternion.setFromRotationMatrix(_hm).multiply(cal.qInv);
  obj.position.copy(_jw).sub(_hs.copy(cal.wrist).applyQuaternion(obj.quaternion));
  obj.updateMatrixWorld(true);
  for (const chain of FINGER_CHAINS) {
    for (let k = 0; k < chain.length - 1; k++) {
      const bone = bones[chain[k]], child = bones[chain[k + 1]];
      if (!bone || !child) break;
      _jd.subVectors(child.getWorldPosition(_ja), bone.getWorldPosition(_jb)).normalize();
      _jt.subVectors(jointPos(J, chain[k + 1], _ja), jointPos(J, chain[k], _jb)).normalize();
      _jr.setFromUnitVectors(_jd, _jt);
      bone.getWorldQuaternion(_jbw).premultiply(_jr);
      bone.parent.getWorldQuaternion(_jpw);
      bone.quaternion.copy(_jpw.invert().multiply(_jbw));
      bone.updateMatrixWorld(true);
    }
  }
}

// ---------- state ----------
let runs = [], players = [], events = [], duration = 0, now = 0, playing = false, offsets = [];

function sampleAt(run, t) {
  const s = run.samples;
  let lo = 0, hi = s.length - 1;
  if (t <= s[0].t) return [s[0], s[0], 0];
  if (t >= s[hi].t) return [s[hi], s[hi], 0];
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (s[m].t <= t) lo = m; else hi = m; }
  const a = s[lo], b = s[hi];
  return [a, b, b.t > a.t ? (t - a.t) / (b.t - a.t) : 0];
}
const lerp3 = (a, b, k) => (a && b && k < 1 ? a.map((v, i) => v + (b[i] - v) * k) : a);
const _qa = new THREE.Quaternion(), _qb = new THREE.Quaternion(), _hp = new THREE.Vector3();

async function loadFiles(fileList) {
  const csvs = [...fileList].filter((f) => f.name.endsWith('.csv') && !f.name.includes('_summary'));
  const files = csvs.filter((f) => !f.name.endsWith('_hands.csv')), handFiles = csvs.filter((f) => f.name.endsWith('_hands.csv'));
  if (!files.length) return status('Drop a run log CSV (not the _summary.csv).');
  try {
    const loaded = await Promise.all(files.slice(0, 2).map(async (f) => buildRun(f.name, await f.text())));
    for (const r of loaded) {   // {base}_hands.csv next to the run = every finger joint at 20 Hz
      const hf = handFiles.find((f) => f.name === r.name.replace(/\.csv$/, '_hands.csv'));
      r.hands = hf ? buildHands(await hf.text()) : null;
    }
    if (loaded.length === 2 && loaded[0].case !== loaded[1].case) return status('The two files are from different cases.');
    const assets = await avatarAssets.catch(() => null);
    for (const p of players) removePlayer(p);
    runs = loaded;
    await loadCase(runs[0].case);
    const t0 = Math.min(...runs.map((r) => r.startUtc));
    offsets = runs.map((r) => (r.startUtc - t0) / 1000);
    duration = Math.max(...runs.map((r, i) => offsets[i] + r.samples[r.samples.length - 1].t));
    players = runs.map((r, i) => {
      const p = makePlayer(i, r.self, assets);
      const pts = r.samples.filter((s) => s.head).map((s) => new THREE.Vector3(s.head[0], 0.01, s.head[2]));
      p.trail.geometry.setFromPoints(pts);
      return p;
    });
    events = runs.flatMap((r, i) => r.events.map((e) => ({ ...e, run: i, gt: e.t + offsets[i] }))).sort((a, b) => a.gt - b.gt);
    $('drop').classList.add('hidden');
    $('runTitle').innerHTML = `${esc(runs.map((r) => r.self).join(' + '))} · Case ${esc(runs[0].case)} <span>${esc(caseTitle(runs[0]))}</span>`;
    $('replayBtn').disabled = false;
    document.querySelector('[data-cam="fp1"]').disabled = runs.length < 2;
    setPage('replay');
    followHead = null;
    renderRunList(); renderTicks(); renderEvents();
    $('time').max = duration;
    seek(0);
  } catch (e) { status(e.message); }
}

function status(msg) { setPage('replay'); $('runs').textContent = msg; }
const caseTitle = (r) => (r.events.find((e) => e.type === 'session_start') || { d: {} }).d.case_title || '';
const hex = (c) => '#' + c.toString(16).padStart(6, '0');

function renderRunList() {
  $('runs').innerHTML = runs.map((r, i) =>
    `<div class="runrow"><span class="sw" style="background:${hex(COLORS[i])}"></span>
     <span><b>${esc(r.self)}</b> · pair ${esc(r.pair)} · condition ${esc(r.cond)} (${r.cond === 'A' ? 'solo' : 'team'})<small>${esc(r.name)}</small></span></div>`).join('') +
    (sceneNote ? `<div class="note bad">${esc(sceneNote)}</div>` : '') +
    (runs.some((r) => Object.keys(r.poses).length) ? '' :
      `<div class="note">Recorded before prop logging, so props stay at their start spots.</div>`) +
    `<div class="note">${runs.every((r) => r.hands) ? 'Hands: real finger movement from the joint log.'
      : runs.some((r) => r.hands) ? 'Hands: finger movement for ' + esc(runs.filter((r) => r.hands).map((r) => r.self).join(', ')) + '; estimated pose for the other.'
        : 'Hands: estimated pose (this run has no finger-joint log).'}</div>`;
}
function renderTicks() {
  $('ticks').innerHTML = events.filter((e) => EVENT_COLORS[e.type] && !e.type.startsWith('session'))
    .map((e) => `<i style="left:${(100 * e.gt) / duration}%;background:${EVENT_COLORS[e.type]}" title="${esc(fmt(e.gt) + '  ' + describe(e))}"></i>`).join('');
}
function renderEvents() {
  $('events').innerHTML = events.filter((e) => !HIDDEN_EVENTS.has(e.type)).map((e) =>
    `<div class="ev" data-t="${e.gt}"><span class="t">${fmt(e.gt)}</span>
     <span class="sw" style="background:${EVENT_COLORS[e.type] || '#c7c7cc'};${runs.length > 1 ? `box-shadow:0 0 0 2px #fff,0 0 0 4px ${hex(COLORS[e.run])}` : ''}"></span>
     <span class="d" title="${esc(e.type + ' ' + e.detail)}">${esc(describe(e))}</span></div>`).join('');
}
const fmt = (s) => { const t = Math.round(s * 10) / 10; return `${Math.floor(t / 60)}:${(t % 60).toFixed(1).padStart(4, '0')}`; };

// ---------- Plain-language event descriptions (names from scenes/case_X|Y.json, written by the Unity export) ----------
let caseNames = { clues: {}, suspects: {}, zones: {} };
function setCaseNames(info) {
  caseNames = {
    clues: Object.fromEntries((info.clues || []).map((c) => [c.id, c])),
    suspects: Object.fromEntries((info.suspects || []).map((s) => [s.id, s])),
    zones: Object.fromEntries((info.zones || []).map((z) => [z.id, z])),
  };
}
const PARTS = { Head: 'body (feet)', LeftHand: 'left hand', RightHand: 'right hand' };
const END_REASONS = { Submitted: 'answer submitted', TimeCap: '15 min time cap reached', Aborted: 'aborted by the experimenter',
  Interrupted: 'app closed or crashed' };
const PLACEMENT = { anchor: 'saved spatial anchor', manual: 'manual (no anchor)', auto_anchor: 'automatic room fit',
  shared_anchor: "host's shared anchor", none: 'not placed' };
function clueText(id) {
  const c = caseNames.clues[id];
  if (!c || !c.name) return id;
  const kind = c.key ? 'key evidence' : `red herring${caseNames.suspects[c.pointsAt] ? ' pointing at ' + caseNames.suspects[c.pointsAt].name : ''}`;
  return `"${c.name}" (${id}, ${kind})`;
}
function suspectText(id) {
  const s = caseNames.suspects[id];
  return s ? `${s.name} (${s.role}${s.culprit ? ', the real culprit' : ', innocent'})` : id;
}
const zoneText = (id) => (caseNames.zones[id] && caseNames.zones[id].name ? `"${caseNames.zones[id].name}" (${id})` : id);
// "evidence=Y_K3;Y_R1;correct=0": list values are ';'-separated like the fields, so collect until the next key=value.
function listAfter(detail, key) {
  const parts = detail.split(';'), i = parts.findIndex((p) => p.startsWith(key + '='));
  if (i < 0) return [];
  const out = [parts[i].slice(key.length + 1)];
  for (let j = i + 1; j < parts.length && !parts[j].includes('='); j++) out.push(parts[j]);
  return out.filter(Boolean);
}
const secs = (v) => (v === undefined || v === '' ? '' : fmt(Number(v)).replace(/\.\d$/, ''));

function describe(e) {
  const d = e.d, first = e.detail.split(';')[0];
  switch (e.type) {
    case 'session_start': return `Run started: ${d.case_title || ''}, time cap ${Math.round(d.time_cap_s / 60)} min, ` +
      (d.start_source === 'participant_start' ? 'started by the participant' : d.start_source ? 'started by the experimenter' : '') +
      (d.placement ? `, scene placed by ${PLACEMENT[d.placement] || d.placement}` : '');
    case 'session_end': return `Run ended: ${END_REASONS[d.reason] || d.reason} after ${secs(d.elapsed_s)}` +
      (d.paused_s > 0 ? ` (${Number(d.paused_s).toFixed(1)} s paused)` : '') + `, ${d.contamination} contamination, ${d.reasoning} reasoning error(s)`;
    case 'run_armed': return 'Experimenter armed the run (briefing card shown to the participant)';
    case 'briefing': return d.heard === '1' ? `Heard Inspector Hart's briefing${d.replays > 0 ? `, replayed ${d.replays}x` : ''}` +
      (d.partner_heard === '1' ? ' (partner heard it too)' : '') : 'Briefing skipped (run started with "Start now")';
    case 'board_order': return 'Suspect cards on the board, left to right: ' + (d.left_to_right || '').split('|').map((id) => caseNames.suspects[id]?.name || id).join(', ');
    case 'placement_set': case 'placement_loaded':
      return `Scene ${e.type === 'placement_set' ? 'placed' : 'loaded'} with ${PLACEMENT[d.method] || d.method}` +
        (d.room_fit ? ` - room check: ${d.room_fit === 'ok' ? 'fits the room' : d.room_fit === 'no_scan' ? 'no room scan' : `${d.room_problems} problem(s), ${d.room_warnings} warning(s)`}` : '');
    case 'placement_check': return `Floor height check ${d.ok === '1' ? 'OK' : 'FAILED'} (scene ${(d.height_m * 100).toFixed(0)} cm from the floor` +
      (d.head_m ? `, head ${Number(d.head_m).toFixed(2)} m above it` : '') + `)${d.when === 'after_recenter' ? ' after a recentre' : ''}`;
    case 'depth_occlusion': return `Depth occlusion ${{ active: 'on', off: 'off', waiting: 'starting', no_permission: 'blocked (no spatial data permission)', unsupported: 'not supported' }[d.status] || d.status}`;
    case 'tracking_recentered': return 'Headset tracking was recentred';
    case 'clue_found': return `Found ${clueText(first)} (first time picked up or touched)`;
    case 'clue_tagged': return `Tagged ${clueText(first)} as evidence`;
    case 'clue_untagged': return `Removed the evidence tag from ${clueText(first)}`;
    case 'clue_thrown': return `Threw ${clueText(first)} at ${Number(d.speed).toFixed(1)} m/s${d.capped === '1' ? ' (slowed to the 2 m/s limit)' : ''}`;
    case 'clue_reset': return `${clueText(first)} went back to its start spot (${d.reason === 'in_zone' ? 'it came to rest in an exclusion zone' : 'it left the play area'})`;
    case 'clue_grab_taken': return `Took ${clueText(first)} out of the partner's hand`;
    case 'board_suspect': return d.suspect ? `Chose ${suspectText(d.suspect)} as the prime suspect on the board` : 'Cleared the prime suspect on the board';
    case 'board_evidence': return d.selected === '1' ? `Pinned ${clueText(d.clue)} to the evidence board (selected)`
      : `Took ${clueText(d.clue)} off the evidence board (deselected)`;
    case 'answer_submitted': {
      const ev = listAfter(e.detail, 'evidence');
      return `Submitted the answer: ${suspectText(d.suspect)} with ${ev.length ? ev.map(clueText).join(', ') : 'no evidence'} - ` +
        `${d.correct === '1' ? 'correct culprit' : 'wrong culprit'}${d.perfect === '1' ? ', perfect submission' : ''}`;
    }
    case 'error_reasoning': return d.type === 'wrong_suspect' ? `Reasoning error: accused ${suspectText(d.suspect)}`
      : d.type === 'wrong_evidence' ? `Reasoning error: submitted ${clueText(d.evidence)}, which isn't key evidence` : `Reasoning error: ${e.detail}`;
    case 'error_contamination': return `Contamination: ${d.part === "Head" ? "stepped" : (PARTS[d.part] || d.part) + " reached"} into ${zoneText(d.zone)}`;
    case 'partner_contamination': return `Partner ${d.by}: ${PARTS[d.part] || d.part} into ${zoneText(d.zone)}`;
    case 'app_paused': return 'App paused (headset menu or sleep)';
    case 'app_resumed': return `App resumed (${Number(d.paused_s).toFixed(1)} s paused in total so far)`;
    case 'hmd_unmounted': return 'Headset taken off';
    case 'hmd_mounted': return `Headset put back on (${Number(d.paused_s).toFixed(1)} s paused in total so far)`;
    case 'hand_tracking_lost': return `${PARTS[d.part] || d.part} tracking lost`;
    case 'hand_tracking_regained': return `${PARTS[d.part] || d.part} tracking back`;
    case 'net_peer_connected': return 'Partner headset connected';
    case 'net_peer_disconnected': return 'Partner headset disconnected';
    case 'net_stopped': return 'Networking stopped';
    case 'incident': return d.type === 'partner_disconnected' ? `Partner disconnected during the run (seen on the ${d.side})` : `Incident: ${e.detail}`;
    default: return `${e.type.replace(/_/g, ' ')}: ${e.detail}`;
  }
}


function seek(t) {
  now = Math.max(0, Math.min(duration, t));
  $('time').value = now;
  update();
}

function update() {
  $('clock').textContent = `${fmt(now)} / ${fmt(duration).replace(/\.\d$/, '')}`;
  const showTrail = $('trail').checked, showGaze = $('gaze').checked;
  runs.forEach((run, i) => {
    const p = players[i];
    const t = now - offsets[i];
    const [a, b, k] = sampleAt(run, t);
    const head = lerp3(a.head, b.head, k);
    p.hasHead = p.head.visible = p.tag.visible = p.shadow.visible = !!head;
    if (head) {
      p.head.position.set(-head[0], head[1], head[2]);
      p.shadow.position.set(head[0], 0.005, head[2]);
      p.tag.position.set(-head[0], head[1] + 0.27, head[2]);
      if (a.q) {
        _qa.set(...a.q);
        if (b.q && k < 1) _qa.slerp(_qb.set(...b.q), k);
        p.head.quaternion.set(_qa.x, -_qa.y, -_qa.z, _qa.w);
      }
    }
    for (const [obj, key] of [[p.left, 'l'], [p.right, 'r']]) {
      if (run.hands && obj.userData.bones && jointsAt(run.hands, t, key, _joints)) {   // real finger joints
        obj.visible = true;
        poseFromJoints(obj, _joints, key);
        continue;
      }
      const v = lerp3(a[key], b[key], k);
      obj.visible = !!v;
      resetBones(obj);
      if (v) poseHand(obj, _hp.set(-v[0], v[1], v[2]), head ? p.head.position : null, p.head.quaternion, key);
    }
    p.gaze.visible = showGaze;
    p.trail.visible = p.recent.visible = showTrail;
    if (showTrail) {
      const pts = run.samples.filter((s) => s.head && s.t <= t && s.t > t - 10).map((s) => new THREE.Vector3(s.head[0], 0.015, s.head[2]));
      p.recent.geometry.setFromPoints(pts.length > 1 ? pts : [new THREE.Vector3(), new THREE.Vector3()]);
      p.recent.visible = pts.length > 1;
    }
  });

  moveClues();

  // Scene state at `now`: zones flash 1 s after an entry, clue rings green when found / gold when tagged.
  const showZones = $('zones').checked;
  for (const [id, box] of Object.entries(zoneMeshes)) {
    const hit = events.some((e) => e.type === 'error_contamination' && e.d.zone === id && now >= e.gt && now - e.gt < 1);
    box.visible = showZones;
    box.material.opacity = hit ? 0.45 : 0.06;
  }
  const found = new Set(), tagged = new Set();
  let contam = [0, 0], cluesBy = [0, 0];
  for (const e of events) {
    if (e.gt > now) break;
    const id = e.detail.split(';')[0];
    if (e.type === 'clue_found') { found.add(id); cluesBy[e.run]++; }
    if (e.type === 'clue_tagged') tagged.add(id);
    if (e.type === 'clue_untagged') tagged.delete(id);
    if (e.type === 'error_contamination') contam[e.run]++;
  }
  for (const [id, ring] of Object.entries(clueMarkers))
    ring.material.color.set(tagged.has(id) ? 0xe0c040 : found.has(id) ? 0x5fd17a : 0x777777);
  $('stats').innerHTML = runs.length ? `<div class="tiles">
    <div class="tile"><b>${found.size}<small style="font-size:15px;color:var(--muted);font-weight:400"> / ${Object.keys(clueMarkers).length || 7}</small></b><span>clues found</span></div>
    <div class="tile"><b>${tagged.size}</b><span>tagged as evidence</span></div>
    ${runs.map((r, i) => `<div class="tile"><b style="color:${contam[i] ? 'var(--red)' : 'inherit'}">${contam[i]}</b><span><i class="sw" style="background:${hex(COLORS[i])}"></i>${esc(r.self)} contamination</span></div>
      <div class="tile"><b>${cluesBy[i]}</b><span><i class="sw" style="background:${hex(COLORS[i])}"></i>${esc(r.self)} finds</span></div>`).join('')}</div>` : '–';
  document.querySelectorAll('#events .ev').forEach((el) => {
    const t = Number(el.dataset.t);
    el.classList.toggle('future', t > now);
    el.classList.toggle('now', t <= now && now - t < 1.5);
  });
}

// Clue meshes are baked at their start spot; a clue_pose row gives the clue's pose, so the mesh moves by
// pose(t) * inverse(first pose) (the first row is written at session start, with every clue at home).
const _m = new THREE.Matrix4(), _home = new THREE.Matrix4(), _pos = new THREE.Vector3(), _q1 = new THREE.Quaternion(), _one = new THREE.Vector3(1, 1, 1);
function poseMatrix(out, pose) { return out.compose(_pos.set(...pose.p), _q1.set(...pose.q), _one); }
function moveClues() {
  const run = runs.find((r) => Object.keys(r.poses).length);   // team runs: the first file's view
  if (!run) return;
  const t = now - offsets[runs.indexOf(run)];
  for (const [id, list] of Object.entries(run.poses)) {
    const pivot = cluePivots[id];
    if (!pivot) continue;
    let lo = 0, hi = list.length - 1;
    if (t < list[0].t) { pivot.matrix.identity(); continue; }
    while (hi - lo > 1) { const m = (lo + hi) >> 1; if (list[m].t <= t) lo = m; else hi = m; }
    if (list[hi].t <= t) lo = hi;
    const a = list[lo], b = list[Math.min(lo + 1, list.length - 1)];
    poseMatrix(_m, a);
    // rows come every 0.1 s while a clue moves: interpolate inside that, hold the last pose across longer gaps
    if (b !== a && b.t - a.t <= 0.15) {
      const k = (t - a.t) / (b.t - a.t);
      _pos.set(...a.p).lerp(new THREE.Vector3(...b.p), k);
      _q1.set(...a.q).slerp(new THREE.Quaternion(...b.q), k);
      _m.compose(_pos, _q1, _one);
    }
    pivot.matrix.multiplyMatrices(_m, poseMatrix(_home, list[0]).invert());
    pivot.matrixWorldNeedsUpdate = true;
  }
}

// ---------- camera ----------
let camMode = 'orbit', followHead = null;   // follow: player 1's head when last framed (null = frame on the next draw)
const _f = new THREE.Vector3(), _u = new THREE.Vector3(), _p = new THREE.Vector3();
function applyCamera() {
  controls.enabled = camMode === 'orbit' || camMode === 'top' || camMode === 'follow';
  if (camMode === 'follow' && players[0] && players[0].hasHead) {   // keep the camera's offset, move with player 1
    const h = players[0].head;
    if (!followHead) {
      _f.set(0, 0, 1).applyQuaternion(h.quaternion); _f.y = 0; _f.normalize();
      controls.target.copy(h.position).add(new THREE.Vector3(0, -0.3, 0)).addScaledVector(_f, 0.15);   // face + both hands
      camera.position.copy(h.position).addScaledVector(_f, 0.95).add(new THREE.Vector3(0.35, 0.05, 0));
      followHead = h.position.clone();
    }
    _p.copy(h.position).sub(followHead);
    camera.position.add(_p); controls.target.add(_p); followHead.copy(h.position);
  }
  players.forEach((p, i) => { if (p.hasHead) { p.head.visible = p.tag.visible = camMode !== `fp${i}`; } });
  if (camMode.startsWith('fp')) {
    const p = players[Number(camMode[2])];
    if (!p || !p.hasHead) return;
    _p.copy(p.head.position);
    _f.set(0, 0, 1).applyQuaternion(p.head.quaternion);
    _u.set(0, 1, 0).applyQuaternion(p.head.quaternion);
    camera.position.copy(_p).addScaledVector(_f, 0.02);
    camera.up.copy(_u);
    camera.lookAt(_p.add(_f));
  }
}
function setCam(mode) {
  camMode = mode;
  document.querySelectorAll('#camSeg button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.cam === mode)));
  camera.up.set(0, 1, 0);
  if (mode === 'top') { camera.position.set(0, 9, 0.001); controls.target.set(0, 0, 0); }
  if (mode === 'orbit') { camera.position.set(0, 5.5, 5.5); controls.target.set(0, 0.6, 0); }
  followHead = null;
  update();
  controls.update();
}
$('camSeg').onclick = (e) => { const b = e.target.closest('button'); if (b && !b.disabled) setCam(b.dataset.cam); };

// ---------- UI ----------
const ICON_PLAY = '<svg viewBox="0 0 18 18" fill="currentColor"><path d="M5 2.8v12.4L15 9z"/></svg>';
const ICON_PAUSE = '<svg viewBox="0 0 18 18" fill="currentColor"><rect x="3.5" y="2.5" width="4" height="13" rx="1"/><rect x="10.5" y="2.5" width="4" height="13" rx="1"/></svg>';
function setPlaying(on) { playing = on; $('play').innerHTML = on ? ICON_PAUSE : ICON_PLAY; $('play').setAttribute('aria-label', on ? 'Pause' : 'Play'); }
setPlaying(false);
function setPage(name) {
  for (const pg of ['lib', 'replay', 'live']) {
    $(pg).classList.toggle('hidden', name !== pg);
    $(pg + 'Btn').setAttribute('aria-current', name === pg ? 'page' : 'false');
  }
  if (name === 'replay') resize();
}
$('libBtn').onclick = () => setPage('lib');
$('replayBtn').onclick = () => setPage('replay');
$('liveBtn').onclick = () => setPage('live');
$('play').onclick = () => { if (!runs.length) return; if (now >= duration) seek(0); setPlaying(!playing); };
$('time').oninput = (e) => seek(Number(e.target.value));
$('events').onclick = (e) => { const ev = e.target.closest('.ev'); if (ev) seek(Number(ev.dataset.t) - 1); };
for (const id of ['trail', 'gaze', 'zones']) $(id).onchange = update;
$('pick').onclick = () => $('file').click();
$('file').onchange = (e) => loadFiles(e.target.files);
addEventListener('dragover', (e) => { e.preventDefault(); document.body.classList.add('dragging'); });
addEventListener('dragleave', () => document.body.classList.remove('dragging'));
addEventListener('drop', (e) => { e.preventDefault(); document.body.classList.remove('dragging'); loadFiles(e.dataTransfer.files); });
addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT' && e.target.type !== 'range') return;
  if ($('replay').classList.contains('hidden')) return;
  if (e.code === 'Space') { e.preventDefault(); $('play').click(); }
  if (e.code === 'ArrowRight') seek(now + 5);
  if (e.code === 'ArrowLeft') seek(now - 5);
});

// ---------- Library (only when served by tools/viewer_server.py) ----------
let libRuns = [], libVersion = -1;
const esc = (v) => String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function renderLibrary() {
  const q = $('libFilter').value.trim().toLowerCase();
  const list = libRuns.filter((r) => !q || [r.name, r.title, r.end, r.folder, 'case ' + r.case].join(' ').toLowerCase().includes(q));
  $('libMsg').textContent = libRuns.length ? `${list.length} of ${libRuns.length} runs` : '';
  if (!list.length) { $('libList').innerHTML = `<p class="note" style="font-size:15px">${libRuns.length ? 'No run matches your search.' : 'No runs yet. Plug in a headset after a test and its runs appear here.'}</p>`; return; }
  const days = new Map();
  for (const r of list) { const d = `${r.stamp.slice(0, 4)}-${r.stamp.slice(4, 6)}-${r.stamp.slice(6, 8)}`; (days.get(d) || days.set(d, []).get(d)).push(r); }
  const dayName = (d) => new Date(d + 'T12:00:00').toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  $('libList').innerHTML = [...days].map(([d, rs]) => `<section class="day"><h2>${dayName(d)} <span>${rs.length} run${rs.length > 1 ? 's' : ''}</span></h2><div class="cards">` +
    rs.map((r) => {
      const t = `${r.stamp.slice(9, 11)}:${r.stamp.slice(11, 13)}`;
      const end = { Submitted: ['ok', 'Submitted'], TimeCap: ['warn', 'Time cap'], Aborted: ['bad', 'Aborted'], Interrupted: ['bad', 'Interrupted'] }[r.end] || ['', r.end];
      const verdict = r.end === 'Submitted' ? (r.correct === '1' ? '<span class="chip ok">Right culprit</span>' : '<span class="chip bad">Wrong culprit</span>') : '';
      const sec = Number(r.elapsed);
      return `<button class="card" data-name="${esc(r.name)}">
        <span class="top"><span class="case">Case ${esc(r.case)} · ${r.condition === 'A' ? 'Solo' : 'Team'}</span><span>${t}</span></span>
        <h3>${esc(r.title || 'Case ' + r.case)}</h3>
        <span class="who">${esc(r.participant)} · pair ${esc(r.pair)}${r.team ? (r.partner ? ' · opens with partner' : ' · partner log not pulled') : ''}</span>
        <span class="chips"><span class="chip ${end[0]}">${esc(end[1])}</span>${verdict}</span>
        <span class="metrics"><div><b>${sec ? fmt(sec).replace(/\.\d$/, '') : '–'}</b>time</div><div><b>${r.found !== '' ? esc(r.found) : '–'}</b>clues found</div>
          <div><b style="color:${Number(r.contamination) > 0 ? 'var(--red)' : 'inherit'}">${r.contamination !== '' ? esc(r.contamination) : '–'}</b>contamination</div></span>
      </button>`;
    }).join('') + '</div></section>').join('');
}
async function openLibraryRun(name) {
  const r = libRuns.find((x) => x.name === name);
  if (!r) return;
  const partner = libRuns.find((x) => x.url === r.partner);
  const urls = [r.url, r.hands, r.partner, partner && partner.hands].filter(Boolean);
  await loadFiles(urls.map((u) => ({ name: u.split('/').pop(), text: () => fetch(u).then((res) => res.text()) })));
}
async function pollServer() {
  try {
    const st = await fetch('/api/status', { cache: 'no-store' }).then((r) => { if (!r.ok) throw 0; return r.json(); });
    const dev = Object.entries(st.devices), on = dev.filter(([, s]) => s === 'device');
    const last = st.messages.length ? ' · ' + st.messages[st.messages.length - 1].replace(/^\S+ /, '') : '';
    $('headset').classList.toggle('on', on.length > 0);
    $('headset').querySelector('span').textContent = on.length ? `Headset connected (${on.map(([s]) => s).join(', ')}) · new runs are saved automatically${last}`
      : dev.length ? `Headset ${dev[0][0]}: ${dev[0][1]} - allow USB debugging in the headset` : `No headset plugged in${last}`;
    if (st.version !== libVersion) { libVersion = st.version; libRuns = await fetch('/api/runs', { cache: 'no-store' }).then((r) => r.json()); renderLibrary(); }
    setTimeout(pollServer, 3000);
  } catch (e) { if (e) console.warn('Library:', e); /* no viewer_server.py (plain static server): drag-and-drop only */ }
}
$('libFilter').oninput = renderLibrary;
$('libList').onclick = (e) => { const c = e.target.closest('.card'); if (c) openLibraryRun(c.dataset.name); };
pollServer();

let last = performance.now();
renderer.setAnimationLoop((ms) => {
  const dt = (ms - last) / 1000; last = ms;
  if (playing && runs.length) {
    seek(now + dt * Number($('speed').value));
    if (now >= duration) setPlaying(false);
  }
  applyCamera();
  if (controls.enabled) controls.update();
  renderer.render(scene, camera);
});
resize();
