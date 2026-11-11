// Live page: what each headset shows right now (passthrough room + the app), streamed by tools/viewer_server.py
// (GET /live?device=..., raw H.264 Annex B from `adb exec-out screenrecord`) and decoded here with WebCodecs.
// Shown flat like Meta's cast (flat.js: left eye, lens warp undone, 16:9) or raw (both eyes).
// Nothing is recorded. Streams only run while the Live page is open.
import { createFlattener } from './flat.js';
const $ = (id) => document.getElementById(id);
const esc = (v) => String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const tiles = new Map();   // real serial -> tile
const supported = 'VideoDecoder' in window;

// Annex B -> NAL units. Returns complete NALs; keeps the unfinished tail in `state.buf`.
function splitNals(state, bytes) {
  const b = state.buf.length ? concat(state.buf, bytes) : bytes;
  const starts = [];
  for (let i = 0; i + 2 < b.length; i++) {
    if (b[i] === 0 && b[i + 1] === 0 && b[i + 2] === 1) { starts.push(i + 3); i += 2; }
  }
  const nals = [];
  for (let k = 0; k + 1 < starts.length; k++) {
    let end = starts[k + 1] - 3;
    if (end > starts[k] && b[end - 1] === 0) end--;   // 4-byte start code
    nals.push(b.subarray(starts[k], end));
  }
  state.buf = starts.length ? b.slice(starts[starts.length - 1] - 3) : b;
  return nals;
}
function concat(a, b) { const c = new Uint8Array(a.length + b.length); c.set(a); c.set(b, a.length); return c; }
const START = new Uint8Array([0, 0, 0, 1]);
function annexB(nals) {
  const out = new Uint8Array(nals.reduce((n, x) => n + 4 + x.length, 0));
  let o = 0;
  for (const x of nals) { out.set(START, o); out.set(x, o + 4); o += 4 + x.length; }
  return out;
}

class LiveTile {
  constructor(h) {
    this.serial = h.serial;
    this.wanted = true;      // watch while the page is open
    this.view = 'flat';
    this.size = '1920x1024';   // falls back to 1280x688 if the headset's encoder refuses
    this.el = document.createElement('article');
    this.el.className = 'livetile';
    this.el.innerHTML = `<header><div><b class="who"></b><small class="dev"></small></div>
      <div class="seg eye" role="group" aria-label="View"><button data-view="flat" aria-pressed="true">Headset view</button><button data-view="both" aria-pressed="false">Both eyes</button></div></header>
      <div class="screen"><canvas class="flat"></canvas><canvas class="raw" hidden></canvas><p class="msg">Connecting…</p></div>
      <footer><span class="stat"></span><span class="actions">
        <button class="btn-s wireless" hidden>Make wireless</button>
        <button class="btn-s full">Full screen</button>
        <button class="btn-s toggle">Stop</button></span></footer>`;
    this.canvas = this.el.querySelector('canvas.raw');
    this.ctx = this.canvas.getContext('2d');
    this.flatCanvas = this.el.querySelector('canvas.flat');
    this.flat = createFlattener(this.flatCanvas);
    this.el.querySelector('.eye').onclick = (e) => {
      const b = e.target.closest('button'); if (!b) return;
      this.view = b.dataset.view;
      this.flatCanvas.hidden = this.view !== 'flat';
      this.canvas.hidden = this.view === 'flat';
      this.el.querySelectorAll('.eye button').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    };
    this.el.querySelector('.toggle').onclick = () => { this.wanted = !this.wanted; this.sync(); };
    this.el.querySelector('.full').onclick = () => this.el.querySelector('.screen').requestFullscreen?.();
    this.el.querySelector('.wireless').onclick = () => this.makeWireless();
    this.update(h);
  }

  update(h) {
    this.h = h;
    this.el.querySelector('.who').textContent = h.participant ? `${h.participant}` : 'Headset';
    this.el.querySelector('.dev').textContent = ` · ${h.serial} · ${h.wireless ? 'Wi-Fi' : 'USB cable'}`;
    this.el.querySelector('.wireless').hidden = h.wireless;
    if (this.device && this.device !== h.id) this.stop();   // switched USB -> Wi-Fi: reconnect on the new id
    this.sync();
  }

  sync() {
    const on = this.wanted && livePageOpen() && supported;
    this.el.querySelector('.toggle').textContent = this.wanted ? 'Stop' : 'Watch';
    if (on && !this.running) this.start();
    if (!on && this.running) this.stop();
    if (!on) this.msg(!supported ? 'Live view needs Chrome or Edge (WebCodecs).' : this.wanted ? '' : 'Stopped.');
  }

  msg(text) { const m = this.el.querySelector('.msg'); m.textContent = text; m.hidden = !text; }

  async start() {
    this.running = true;
    this.device = this.h.id;
    this.abort = new AbortController();
    this.msg('Connecting…');
    const state = { buf: new Uint8Array(0) };
    let frame = [], pending = [], configured = false, gotKey = false, ts = 0, frames = 0, last = performance.now();
    const decoder = new VideoDecoder({
      output: (vf) => { this.draw(vf); vf.close(); frames++; },
      error: (e) => { this.msg('Decoder error: ' + e.message); },
    });
    this.decoder = decoder;
    const flush = () => {
      if (!frame.length) return;
      const key = frame.some((n) => (n[0] & 0x1f) === 5);
      if (key) gotKey = true;
      if (configured && gotKey && decoder.state === 'configured')
        decoder.decode(new EncodedVideoChunk({ type: key ? 'key' : 'delta', timestamp: (ts += 33333), data: annexB(pending.concat(frame)) }));
      if (key || configured) pending = [];
      frame = [];
    };
    this.statTimer = setInterval(() => {
      const now = performance.now();
      this.el.querySelector('.stat').textContent = this.running ? `${Math.round((frames * 1000) / (now - last))} fps · live` : '';
      frames = 0; last = now;
    }, 1000);
    try {
      const res = await fetch(`/live?device=${encodeURIComponent(this.device)}&size=${this.size}`, { signal: this.abort.signal, cache: 'no-store' });
      if (!res.ok) throw new Error(`server said ${res.status}`);
      const reader = res.body.getReader();
      let first = true;
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        if (first && value[0] === 0x45 && value[1] === 0x52) {   // "ERROR: ..." from screenrecord instead of video
          const text = new TextDecoder().decode(value);
          if (/LAYER_STACK|display state/i.test(text)) throw new Error("the headset's display is off (asleep or not worn); the picture comes back when it wakes");
          if (this.size !== '1280x688') { this.size = '1280x688'; throw new Error('trying a smaller picture'); }
          throw new Error(text.trim().slice(0, 120));
        }
        first = false;
        for (const nal of splitNals(state, value)) {
          const type = nal[0] & 0x1f;
          if (type === 7) {        // SPS: codec string from profile / constraints / level
            flush();
            if (!configured) {
              const hex = (x) => x.toString(16).padStart(2, '0');
              decoder.configure({ codec: `avc1.${hex(nal[1])}${hex(nal[2])}${hex(nal[3])}`, optimizeForLatency: true });
              configured = true;
            }
            pending.push(nal);
          } else if (type === 8 || type === 6) pending.push(nal);   // PPS, SEI: sent with the next frame
          else if (type === 1 || type === 5) {
            if (nal[1] & 0x80) flush();   // first_mb_in_slice == 0: a new picture starts
            frame.push(nal);
          }
        }
      }
      this.msg('The headset stopped sending (screen off or app closed?). Retrying…');
    } catch (e) {
      if (e.name !== 'AbortError') this.msg('No live picture: ' + e.message + '. Retrying…');
    }
    const retry = this.running && !this.abort.signal.aborted;
    this.cleanup();
    if (retry) setTimeout(() => this.sync(), 2000);
  }

  draw(vf) {
    const w = vf.displayWidth, h = vf.displayHeight;
    if (this.view === 'flat' && this.flat) { this.flat.draw(vf, w, h); this.msg(''); return; }
    const [sx, sy, sw, sh] = [0, 0, w, h];   // raw: both lens-warped eyes
    const cw = Math.round(sw), ch = Math.round(sh);
    if (this.canvas.width !== cw || this.canvas.height !== ch) { this.canvas.width = cw; this.canvas.height = ch; }
    this.ctx.drawImage(vf, sx, sy, sw, sh, 0, 0, cw, ch);
    this.msg('');
  }

  makeWireless() {
    const b = this.el.querySelector('.wireless');
    b.disabled = true; b.textContent = 'Switching…';
    fetch(`/api/wireless?device=${encodeURIComponent(this.h.id)}`, { method: 'POST' }).then((r) => r.json())
      .then((j) => alert(j.message)).catch((e) => alert('Failed: ' + e.message))
      .finally(() => { b.disabled = false; b.textContent = 'Make wireless'; });
  }

  stop() { this.running = false; this.abort?.abort(); this.cleanup(); }
  cleanup() {
    this.running = false;
    clearInterval(this.statTimer);
    if (this.decoder && this.decoder.state !== 'closed') this.decoder.close();
    this.decoder = null;
    this.el.querySelector('.stat').textContent = '';
  }
  remove() { this.stop(); this.el.remove(); }
}

function livePageOpen() { return !$('live').classList.contains('hidden') && !document.hidden; }

async function poll() {
  try {
    const st = await fetch('/api/status', { cache: 'no-store' }).then((r) => { if (!r.ok) throw 0; return r.json(); });
    $('liveBtn').hidden = false;
    // one tile per real headset; prefer its Wi-Fi connection so unplugging the cable keeps the picture
    const bySerial = new Map();
    for (const h of st.headsets || []) if (!bySerial.has(h.serial) || h.wireless) bySerial.set(h.serial, h);
    for (const [serial, t] of tiles) if (!bySerial.has(serial)) { t.remove(); tiles.delete(serial); }
    for (const [serial, h] of bySerial) {
      if (tiles.has(serial)) tiles.get(serial).update(h);
      else { const t = new LiveTile(h); tiles.set(serial, t); $('liveGrid').append(t.el); }
    }
    $('liveEmpty').hidden = tiles.size > 0;
    setTimeout(poll, 3000);
  } catch { /* no viewer_server.py: no live view */ }
}
new MutationObserver(() => tiles.forEach((t) => t.sync())).observe($('live'), { attributes: true, attributeFilter: ['class'] });
document.addEventListener('visibilitychange', () => tiles.forEach((t) => t.sync()));
poll();
