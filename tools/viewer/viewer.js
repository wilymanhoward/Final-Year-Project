// Replay viewer for the FYP run logs. Scene meshes come from FYP > Export Web Viewer Scenes (scenes/case_X|Y.obj + .json).
// Everything (meshes, CSV positions, quaternions) stays in Unity's CaseRoot-local frame inside `world`, which is
// mirrored on X once: Unity is left-handed, three.js right-handed (three flips the face winding for a mirrored matrix).
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';

const COLORS = [0xff9a3c, 0x3cd0ff];
const EVENT_COLORS = {
  error_contamination: '#ff4d4d', error_reasoning: '#ff4d4d', clue_found: '#5fd17a', clue_tagged: '#e0c040',
  clue_untagged: '#a08a40', answer_submitted: '#ffffff', session_start: '#888', session_end: '#888',
};
const HIDDEN_EVENTS = new Set(['hand_tracking_lost', 'hand_tracking_regained']);
const $ = (id) => document.getElementById(id);

// ---------- three.js scene ----------
const view = $('view');
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
view.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x14161a);
const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 100);
camera.position.set(0, 5.5, 5.5);
const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.6, 0);
scene.add(new THREE.HemisphereLight(0xffffff, 0x444450, 1.6));
const sun = new THREE.DirectionalLight(0xffffff, 1.4);
sun.position.set(2, 5, 3);
scene.add(sun);
const grid = new THREE.GridHelper(10, 20, 0x333842, 0x24272e);
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
function makePlayer(color) {
  const mat = new THREE.MeshLambertMaterial({ color });
  const head = new THREE.Group();
  const skull = new THREE.Mesh(new THREE.SphereGeometry(0.11, 16, 12), mat);
  const visor = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.08, 0.06), new THREE.MeshLambertMaterial({ color: 0x222222 }));
  visor.position.set(0, 0, 0.1);   // Unity forward = +Z
  const nose = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.08, 8), mat);
  nose.rotation.x = Math.PI / 2;
  nose.position.set(0, 0, 0.16);
  head.add(skull, visor, nose);
  const hand = () => new THREE.Mesh(new THREE.SphereGeometry(0.04, 12, 8), mat);
  const left = hand(), right = hand();
  const gaze = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, 4)]),
    new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.6 }));
  head.add(gaze);
  const shadow = new THREE.Mesh(new THREE.CircleGeometry(0.18, 20),
    new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.35, depthWrite: false }));
  shadow.rotation.x = -Math.PI / 2;
  const trail = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.25 }));
  const recent = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color }));
  const group = new THREE.Group();
  group.add(head, left, right, shadow, trail, recent);
  world.add(group);
  return { group, head, left, right, gaze, shadow, trail, recent };
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
const _qa = new THREE.Quaternion(), _qb = new THREE.Quaternion();

async function loadFiles(fileList) {
  const files = [...fileList].filter((f) => f.name.endsWith('.csv') && !f.name.includes('_summary'));
  if (!files.length) return status('Drop a run log CSV (not the _summary.csv).');
  try {
    const loaded = await Promise.all(files.slice(0, 2).map(async (f) => buildRun(f.name, await f.text())));
    if (loaded.length === 2 && loaded[0].case !== loaded[1].case) return status('The two files are from different cases.');
    for (const p of players) world.remove(p.group);
    runs = loaded;
    await loadCase(runs[0].case);
    const t0 = Math.min(...runs.map((r) => r.startUtc));
    offsets = runs.map((r) => (r.startUtc - t0) / 1000);
    duration = Math.max(...runs.map((r, i) => offsets[i] + r.samples[r.samples.length - 1].t));
    players = runs.map((r, i) => {
      const p = makePlayer(COLORS[i]);
      const pts = r.samples.filter((s) => s.head).map((s) => new THREE.Vector3(s.head[0], 0.01, s.head[2]));
      p.trail.geometry.setFromPoints(pts);
      return p;
    });
    events = runs.flatMap((r, i) => r.events.map((e) => ({ ...e, run: i, gt: e.t + offsets[i] }))).sort((a, b) => a.gt - b.gt);
    $('drop').classList.add('hidden');
    renderRunList(); renderTicks(); renderEvents();
    $('time').max = duration;
    seek(0);
  } catch (e) { status(e.message); }
}

function status(msg) { $('runs').textContent = msg; }

function renderRunList() {
  $('runs').innerHTML = runs.map((r, i) =>
    `<div class="run"><span class="sw" style="background:#${COLORS[i].toString(16)}"></span>
     <span><b>${r.self}</b> · pair ${r.pair} · condition ${r.cond} · case ${r.case}<br>
     <span class="hint">${r.name}</span></span></div>`).join('') +
    (sceneNote ? `<div style="color:var(--bad);margin-top:6px">${sceneNote}</div>` : '') +
    (runs.some((r) => Object.keys(r.poses).length) ? '' :
      `<div class="hint" style="margin-top:6px">No clue_pose rows in this log (recorded before clue logging): props stay at their start spots.</div>`) +
    `<div class="hint" style="margin-top:6px">Drop other files to replace${$('libBtn').hidden ? '' : ', or open the Library'}.</div>`;
}
function renderTicks() {
  $('ticks').innerHTML = events.filter((e) => EVENT_COLORS[e.type] && !e.type.startsWith('session'))
    .map((e) => `<i style="left:${(100 * e.gt) / duration}%;background:${EVENT_COLORS[e.type]}" title="${e.type}"></i>`).join('');
}
function renderEvents() {
  $('events').innerHTML = events.filter((e) => !HIDDEN_EVENTS.has(e.type)).map((e) =>
    `<div class="ev" data-t="${e.gt}"><span>${fmt(e.gt)}</span>
     <span class="sw" style="background:#${COLORS[e.run].toString(16)}"></span>
     <span class="d" title="${esc(e.type + ' ' + e.detail)}" style="color:${EVENT_COLORS[e.type] || 'inherit'}">${esc(describe(e))}</span></div>`).join('');
}
const fmt = (s) => `${Math.floor(s / 60)}:${(s % 60).toFixed(1).padStart(4, '0')}`;

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
    p.hasHead = p.head.visible = p.shadow.visible = !!head;
    if (head) {
      p.head.position.set(...head);
      p.shadow.position.set(head[0], 0.005, head[2]);
      if (a.q) {
        _qa.set(...a.q);
        if (b.q && k < 1) _qa.slerp(_qb.set(...b.q), k);
        p.head.quaternion.copy(_qa);
      }
    }
    for (const [mesh, key] of [[p.left, 'l'], [p.right, 'r']]) {
      const v = lerp3(a[key], b[key], k);
      mesh.visible = !!v;
      if (v) mesh.position.set(...v);
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
  $('stats').innerHTML = runs.length ? `
    <div><span>Clues found (team)</span><b>${found.size}</b></div>
    <div><span>Clues tagged</span><b>${tagged.size}</b></div>
    ${runs.map((r, i) => `<div><span><span class="sw" style="background:#${COLORS[i].toString(16)}"></span> ${r.self} contamination / finds</span><b>${contam[i]} / ${cluesBy[i]}</b></div>`).join('')}` : '–';

  document.querySelectorAll('#events .ev').forEach((el) => {
    const t = Number(el.dataset.t);
    el.classList.toggle('past', t <= now);
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
const _f = new THREE.Vector3(), _u = new THREE.Vector3(), _p = new THREE.Vector3();
function applyCamera() {
  const mode = $('cam').value;
  controls.enabled = mode === 'orbit' || mode === 'top';
  if (mode.startsWith('fp')) {
    const p = players[Number(mode[2])];
    if (!p || !p.hasHead) return;
    p.head.getWorldPosition(_p);
    _f.set(0, 0, 1).applyQuaternion(p.head.quaternion); _f.x = -_f.x;   // Unity -> world mirror
    _u.set(0, 1, 0).applyQuaternion(p.head.quaternion); _u.x = -_u.x;
    camera.position.copy(_p);
    camera.up.copy(_u);
    camera.lookAt(_p.add(_f));
    p.head.visible = false;   // don't look out from inside the marker; update() shows it again
  }
}
$('cam').addEventListener('change', () => {
  camera.up.set(0, 1, 0);
  if ($('cam').value === 'top') { camera.position.set(0, 9, 0.001); controls.target.set(0, 0, 0); }
  if ($('cam').value === 'orbit') { camera.position.set(0, 5.5, 5.5); controls.target.set(0, 0.6, 0); }
  update();
  controls.update();
});

// ---------- UI ----------
$('play').onclick = () => { if (now >= duration) seek(0); playing = !playing; $('play').textContent = playing ? '❚❚' : '▶'; };
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
  if (e.code === 'Space') { e.preventDefault(); $('play').click(); }
  if (e.code === 'ArrowRight') seek(now + 5);
  if (e.code === 'ArrowLeft') seek(now - 5);
});

// ---------- Library (only when served by tools/viewer_server.py) ----------
let libRuns = [], libVersion = -1;
const esc = (v) => String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function showLibrary(on) { $('lib').classList.toggle('show', on); if (on) $('drop').classList.add('hidden'); }
function renderLibrary() {
  const q = $('libFilter').value.trim().toLowerCase();
  const list = libRuns.filter((r) => !q || [r.name, r.title, r.end, r.folder].join(' ').toLowerCase().includes(q));
  if (!list.length) { $('libList').textContent = libRuns.length ? 'No run matches the filter.' : 'No runs in StudyData yet - plug in a headset.'; return; }
  const days = new Map();
  for (const r of list) { const d = `${r.stamp.slice(0, 4)}-${r.stamp.slice(4, 6)}-${r.stamp.slice(6, 8)}`; (days.get(d) || days.set(d, []).get(d)).push(r); }
  $('libList').innerHTML = [...days].map(([d, rs], i) => `<details${i === 0 ? ' open' : ''}><summary>${d} <span>· ${rs.length} run${rs.length > 1 ? 's' : ''}</span></summary>` +
    rs.map((r) => {
      const t = `${r.stamp.slice(9, 11)}:${r.stamp.slice(11, 13)}`;
      const end = r.end === 'Submitted' ? 'ok' : r.end === 'TimeCap' ? 'warn' : 'bad';
      const sec = Number(r.elapsed);
      const res = r.end === 'Submitted' ? (r.correct === '1' ? '<span class="tag ok">correct</span>' : '<span class="tag bad">wrong suspect</span>') : '';
      return `<div class="lrow" data-name="${esc(r.name)}"><span><b>${t}</b> · ${esc(r.participant)} · pair ${esc(r.pair)}</span>
        <span>Case ${esc(r.case)} <span class="meta">${esc(r.title)}</span> · condition ${esc(r.condition)}${r.team ? '<span class="tag">team' + (r.partner ? ' + partner' : '') + '</span>' : ''}</span>
        <span class="meta"><span class="tag ${end}">${esc(r.end)}</span>${res}${sec ? ' ' + fmt(sec).replace(/\.\d$/, '') : ''}
          ${r.found !== '' ? ` · found ${esc(r.found)} · contam ${esc(r.contamination)}` : ''}</span></div>`;
    }).join('') + '</details>').join('');
}
async function openLibraryRun(name) {
  const r = libRuns.find((x) => x.name === name);
  if (!r) return;
  const urls = [r.url, r.partner].filter(Boolean);
  showLibrary(false);
  await loadFiles(urls.map((u) => ({ name: u.split('/').pop(), text: () => fetch(u).then((res) => res.text()) })));
}
async function pollServer() {
  try {
    const st = await fetch('/api/status', { cache: 'no-store' }).then((r) => { if (!r.ok) throw 0; return r.json(); });
    if ($('libBtn').hidden) { $('libBtn').hidden = false; if (!runs.length) showLibrary(true); }
    const dev = Object.entries(st.devices);
    $('libStatus').innerHTML = (dev.length
      ? dev.map(([s, state]) => state === 'device' ? `<span class="on">● headset ${esc(s)} connected - new runs are pulled automatically</span>`
        : `● headset ${esc(s)}: ${esc(state)} (allow USB debugging in the headset)`).join('<br>')
      : '○ no headset plugged in') + (st.messages.length ? `<br>${esc(st.messages[st.messages.length - 1])}` : '');
    if (st.version !== libVersion) { libVersion = st.version; libRuns = await fetch('/api/runs', { cache: 'no-store' }).then((r) => r.json()); renderLibrary(); }
    setTimeout(pollServer, 3000);
  } catch { /* plain static server: no library, drag-and-drop only */ }
}
$('libBtn').onclick = () => showLibrary(!$('lib').classList.contains('show'));
$('libFilter').oninput = renderLibrary;
$('libList').onclick = (e) => { const row = e.target.closest('.lrow'); if (row) openLibraryRun(row.dataset.name); };
pollServer();

let last = performance.now();
renderer.setAnimationLoop((ms) => {
  const dt = (ms - last) / 1000; last = ms;
  if (playing && runs.length) {
    seek(now + dt * Number($('speed').value));
    if (now >= duration) { playing = false; $('play').textContent = '▶'; }
  }
  applyCamera();
  if (controls.enabled) controls.update();
  renderer.render(scene, camera);
});
resize();
