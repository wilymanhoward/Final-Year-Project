// Turns the headset's raw screen (two lens-warped eye images side by side) into one flat 16:9 picture like Meta's
// cast: takes the left eye, undoes the lens pre-warp with a radial model, crops the middle. WebGL, one draw per frame.
// The warp numbers were tuned by eye on a Quest 3 screenshot (straight door frames / card edges come out straight).
export const FLAT = { k1: 0.6, k2: 0.2, zoom: 0.6, cx: 0.485, cy: 0.5, aspect: 16 / 9 };

const VS = `attribute vec2 a; varying vec2 uv; void main(){ uv = a * 0.5 + 0.5; gl_Position = vec4(a, 0.0, 1.0); }`;
const FS = `precision mediump float;
varying vec2 uv;
uniform sampler2D tex;
uniform vec2 center;   // lens centre inside the left-eye half (0..1)
uniform float k1, k2, zoom, aspect, eyeAspect;
void main() {
  // output point in "flat" units (picture height = zoom), then where the lens pre-warp put it on the screen
  vec2 p = (vec2(uv.x, 1.0 - uv.y) - 0.5) * vec2(aspect, 1.0) * zoom;
  float r2 = dot(p, p);
  vec2 q = p / (1.0 + k1 * r2 + k2 * r2 * r2);
  vec2 e = center + vec2(q.x / eyeAspect, q.y);
  if (e.x < 0.0 || e.x > 1.0 || e.y < 0.0 || e.y > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }
  gl_FragColor = texture2D(tex, vec2(e.x * 0.5, e.y));
}`;

export function createFlattener(canvas) {
  const gl = canvas.getContext('webgl', { antialias: false, preserveDrawingBuffer: true });
  if (!gl) return null;
  const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); return s; };
  const prog = gl.createProgram();
  gl.attachShader(prog, sh(gl.VERTEX_SHADER, VS));
  gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, FS));
  gl.linkProgram(prog);
  gl.useProgram(prog);
  gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
  const loc = gl.getAttribLocation(prog, 'a');
  gl.enableVertexAttribArray(loc);
  gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
  gl.bindTexture(gl.TEXTURE_2D, gl.createTexture());
  for (const [k, v] of [[gl.TEXTURE_MIN_FILTER, gl.LINEAR], [gl.TEXTURE_MAG_FILTER, gl.LINEAR],
    [gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE], [gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE]]) gl.texParameteri(gl.TEXTURE_2D, k, v);
  const u = (n) => gl.getUniformLocation(prog, n);
  return {
    /** source: VideoFrame / image / canvas holding the two-eye screen of size w x h. */
    draw(source, w, h, opt = FLAT) {
      const outH = Math.min(1080, Math.round(h * 0.9)), outW = Math.round(outH * opt.aspect);
      if (canvas.width !== outW || canvas.height !== outH) { canvas.width = outW; canvas.height = outH; }
      gl.viewport(0, 0, outW, outH);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
      gl.uniform2f(u('center'), opt.cx, opt.cy);
      gl.uniform1f(u('k1'), opt.k1); gl.uniform1f(u('k2'), opt.k2); gl.uniform1f(u('zoom'), opt.zoom);
      gl.uniform1f(u('aspect'), opt.aspect); gl.uniform1f(u('eyeAspect'), (w / 2) / h);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    },
  };
}
