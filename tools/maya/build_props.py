"""
Builds the low-poly, flat-shaded prop, clue and floor-decal meshes for both cases and exports one FBX per object to
Assets/_Project/Art/Props/, plus the shared Palette.png and BlobShadow.png.

Run with Maya's Python (no GUI needed):
    "C:/Program Files/Autodesk/Maya2027/bin/mayapy.exe" tools/maya/build_props.py

Style ("low-poly game", not primitive): every edge is hard (flat shading) and most parts are chamfered so edges catch
the light; legs are tapered or turned, furniture has mouldings, drawers, handles and set dressing (laptops, mugs, plants,
books, doors ajar); each material has 2-3 tones in the palette so insets and undersides read darker.
Props are a few hundred to ~1,500 triangles each.

Conventions (the Unity side, CaseSceneTool, relies on them):
- Metres, Y-up. One shared 32 x 36 palette texture (8 x 9 cells): every face's UVs point at the centre of one cell, so
  the meshes work with URP Simple Lit / Unlit (which ignore vertex colours).
- Props: pivot on the floor at the centre of the footprint, front faces +Z. Footprints match CaseSpecs props (width
  along local X, depth along local Z; the stand is turned 90 degrees in Unity, so its width is the spec depth). Set
  dressing stays clear of the clue spots (desk centres, screen/board centre, the display's front-right foot).
- Grabbable clues: pivot at the bottom centre, mesh fills exactly the clue's collider box (CaseSceneTool.ClueSize).
- Flat clues (K1 static, R3 grabbable since session 6): centred on the origin, readable side towards -Z (like a Unity Quad), depth behind it (+Z).
- Zone decals: zone-local, centred, at most a few millimetres high, filling the zone rectangle; outline in "_Alpha".
- Child meshes: "<name>_Body" (opaque palette), optional "<name>_Glass" (transparent), "<name>_Alpha" (outline).
Prints every object's size and triangle count; fails if the total is over the 50k budget.

Each object is also saved as a Maya ASCII scene, tools/maya/scenes/<name>.ma (outside Assets/, so Unity ignores it), with
the palette texture and see-through glass hooked up for viewing in Maya. The script stays the source: re-running it
overwrites both the FBX and the .ma, so edits made by hand in a .ma are lost on the next run.
"""
import math
import os
import struct
import sys
import zlib

import maya.standalone

maya.standalone.initialize(name="python")
import maya.api.OpenMaya as om  # noqa: E402
import maya.cmds as cmds  # noqa: E402
import maya.mel as mel  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Assets", "_Project", "Art", "Props")
RESOURCES = os.path.join(REPO, "Assets", "_Project", "Resources")   # meshes loaded at runtime (PartnerMarker)
SCENES = os.path.join(HERE, "scenes")
TRIANGLE_BUDGET = 50000

# ---------------------------------------------------------------------------------------------------------------
# Palette: 8 x 9 cells of 4 x 4 px (32 x 36 texture). Order matters (UVs point at cells by index).
# Tones: <name> base, <name>_lt lighter, <name>_dk darker.
# ---------------------------------------------------------------------------------------------------------------
PALETTE = [
    ("white", (238, 238, 234)), ("white_dk", (212, 213, 212)), ("grey_lt", (196, 199, 204)), ("grey", (150, 154, 160)),
    ("grey_dk", (88, 92, 98)), ("charcoal", (52, 55, 60)), ("black", (26, 27, 30)), ("silver", (176, 181, 188)),
    ("teal", (32, 150, 148)), ("teal_dk", (20, 104, 104)), ("coral", (236, 112, 88)), ("lcd", (186, 208, 170)),
    ("blue", (56, 118, 214)), ("blue_dk", (36, 74, 150)), ("screen", (32, 50, 78)), ("screen_lt", (78, 124, 176)),
    ("wood_lt", (214, 170, 112)), ("wood", (176, 128, 76)), ("wood_dk", (122, 80, 44)), ("walnut", (92, 56, 32)),
    ("walnut_dk", (62, 38, 22)), ("cork", (190, 146, 94)), ("cork_dk", (156, 116, 72)), ("brass", (206, 164, 76)),
    ("indigo", (48, 54, 120)), ("indigo_dk", (30, 34, 82)), ("ochre", (222, 164, 52)), ("ochre_dk", (176, 122, 34)),
    ("maroon", (134, 34, 42)), ("maroon_dk", (92, 22, 30)), ("cream", (242, 230, 204)), ("batik_lt", (236, 214, 170)),
    ("paper", (248, 246, 234)), ("paper_dk", (222, 218, 200)), ("ink", (40, 40, 54)), ("pencil", (110, 110, 118)),
    ("yellow", (250, 214, 60)), ("yellow_dk", (214, 172, 30)), ("orange", (242, 142, 44)), ("red", (206, 40, 34)),
    ("green", (70, 160, 82)), ("green_dk", (40, 112, 56)), ("leaf", (104, 186, 92)), ("terracotta", (196, 104, 70)),
    ("soil", (70, 50, 36)), ("led", (96, 236, 126)), ("pink", (240, 156, 176)), ("glass", (194, 232, 244)),
    ("coffee", (78, 46, 26)), ("coffee_lt", (120, 78, 46)), ("mud", (86, 66, 44)), ("mud_dk", (62, 46, 30)),
    ("water", (118, 170, 220)), ("water_lt", (170, 206, 236)), ("varnish", (206, 142, 52)), ("varnish_dk", (160, 104, 34)),
    ("shard", (220, 244, 255)), ("shard_dk", (170, 210, 228)), ("outline", (242, 242, 242)), ("chrome", (210, 214, 220)),
    ("rubber", (44, 44, 46)), ("lamp", (255, 236, 170)), ("skin_card", (232, 226, 214)), ("key", (166, 206, 236)),
    # Row 9 (session 6): the briefing detective.
    ("skin", (224, 176, 140)), ("skin_dk", (190, 140, 106)), ("coat", (196, 160, 104)), ("coat_dk", (156, 122, 74)),
    ("hat", (78, 62, 50)), ("hat_dk", (50, 40, 32)), ("lip", (150, 84, 70)), ("eye", (30, 30, 36)),
]
assert len(PALETTE) == 72
COLS, ROWS, CELL = 8, 9, 4
C = {name: i for i, (name, _) in enumerate(PALETTE)}


def cell_uv(index):
    col, row = index % COLS, index // COLS
    return (col + 0.5) / COLS, 1.0 - (row + 0.5) / ROWS   # row 0 at the top of the PNG


def write_png(path, width, height, rgba_rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rgba_rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def write_palette(path):
    rows = []
    for y in range(ROWS * CELL):
        row = []
        for x in range(COLS * CELL):
            r, g, b = PALETTE[(y // CELL) * COLS + x // CELL][1]
            row += [r, g, b, 255]
        rows.append(row)
    write_png(path, COLS * CELL, ROWS * CELL, rows)


def write_blob(path, size=64):
    rows = []
    for y in range(size):
        row = []
        for x in range(size):
            d = math.hypot((x + 0.5) / size * 2 - 1, (y + 0.5) / size * 2 - 1)
            row += [0, 0, 0, int(round(max(0.0, 1.0 - d) ** 1.6 * 255))]
        rows.append(row)
    write_png(path, size, size, rows)


class Rand:
    """Tiny deterministic LCG so every run produces identical meshes."""
    def __init__(self, seed):
        self.s = seed & 0xFFFFFFFF

    def next(self):
        self.s = (1664525 * self.s + 1013904223) & 0xFFFFFFFF
        return self.s / 4294967296.0

    def range(self, a, b):
        return a + (b - a) * self.next()


# ---------------------------------------------------------------------------------------------------------------
# Geometry toolkit. Every face is oriented automatically (outwards from a given inside point, or upwards).
# ---------------------------------------------------------------------------------------------------------------
def newell(pts):
    nx = ny = nz = 0.0
    for i in range(len(pts)):
        x0, y0, z0 = pts[i]
        x1, y1, z1 = pts[(i + 1) % len(pts)]
        nx += (y0 - y1) * (z0 + z1)
        ny += (z0 - z1) * (x0 + x1)
        nz += (x0 - x1) * (y0 + y1)
    return nx, ny, nz


def upward(outline):
    """Orders a 2D (x, z) outline so a polygon through it faces +Y."""
    area = sum(outline[i][0] * outline[(i + 1) % len(outline)][1] - outline[(i + 1) % len(outline)][0] * outline[i][1]
               for i in range(len(outline)))
    return list(outline) if area < 0 else list(reversed(outline))


class Part:
    def __init__(self):
        self.faces = []   # (list of (x, y, z), colour index)

    def face(self, pts, col):
        self.faces.append(([tuple(p) for p in pts], col))

    def face_out(self, pts, col, inside):
        """Adds the polygon facing away from the point `inside`."""
        n = newell(pts)
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        cz = sum(p[2] for p in pts) / len(pts)
        d = n[0] * (cx - inside[0]) + n[1] * (cy - inside[1]) + n[2] * (cz - inside[2])
        self.face(pts if d >= 0 else list(reversed(pts)), col)

    # ---- boxes ----
    def bbox(self, x0, y0, z0, x1, y1, z1, col, b=0.0, edge_col=None):
        """Box, chamfered by b (bevel faces in edge_col, default col). b = 0: plain box."""
        if x1 < x0: x0, x1 = x1, x0
        if y1 < y0: y0, y1 = y1, y0
        if z1 < z0: z0, z1 = z1, z0
        b = max(0.0, min(b, (x1 - x0) / 2.01, (y1 - y0) / 2.01, (z1 - z0) / 2.01))
        c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
        lo, hi = (x0, y0, z0), (x1, y1, z1)
        ec = col if edge_col is None else edge_col

        def P(a, s, va):
            """Point on axis a at side s (0 lo / 1 hi), other axes from va dict {axis: value}."""
            p = [0.0, 0.0, 0.0]
            p[a] = hi[a] if s else lo[a]
            for k, v in va.items():
                p[k] = v
            return tuple(p)

        def inset(k, s):
            return (hi[k] - b) if s else (lo[k] + b)
        # 6 face quads
        for a in range(3):
            o = [k for k in range(3) if k != a]
            for s in (0, 1):
                pts = [P(a, s, {o[0]: inset(o[0], i), o[1]: inset(o[1], j)}) for i, j in ((0, 0), (1, 0), (1, 1), (0, 1))]
                self.face_out(pts, col, c)
        if b <= 0:
            return
        # 12 edge bevels
        for a in range(3):
            for e in range(a + 1, 3):
                t = [k for k in range(3) if k not in (a, e)][0]
                for sa in (0, 1):
                    for se in (0, 1):
                        pts = []
                        for st in (0, 1):
                            pa = [0.0] * 3; pa[a] = hi[a] if sa else lo[a]; pa[e] = inset(e, se); pa[t] = inset(t, st)
                            pts.append(tuple(pa))
                        for st in (1, 0):
                            pe = [0.0] * 3; pe[e] = hi[e] if se else lo[e]; pe[a] = inset(a, sa); pe[t] = inset(t, st)
                            pts.append(tuple(pe))
                        self.face_out(pts, ec, c)
        # 8 corner triangles
        for sx in (0, 1):
            for sy in (0, 1):
                for sz in (0, 1):
                    s = (sx, sy, sz)
                    pts = []
                    for a in range(3):
                        p = [inset(k, s[k]) for k in range(3)]
                        p[a] = hi[a] if s[a] else lo[a]
                        pts.append(tuple(p))
                    self.face_out(pts, ec, c)

    def cbb(self, cx, y, cz, sx, sy, sz, col, b=0.0, edge_col=None):
        """Box from the bottom centre (x, y, z) and its size."""
        self.bbox(cx - sx / 2, y, cz - sz / 2, cx + sx / 2, y + sy, cz + sz / 2, col, b, edge_col)

    # ---- round things ----
    def frustum(self, cx, cy, cz, r0, r1, h, col, sides=8, axis="y", cap=True, cap_col=None, phase=0.0):
        """Tapered cylinder from radius r0 to r1 over length h along +axis, starting at (cx, cy, cz)."""
        def ring(r, t):
            pts = []
            for i in range(sides):
                a = 2 * math.pi * (i + phase) / sides
                u, v = math.cos(a) * r, math.sin(a) * r
                if axis == "y": pts.append((cx + u, cy + t, cz + v))
                elif axis == "x": pts.append((cx + t, cy + u, cz + v))
                else: pts.append((cx + u, cy + v, cz + t))
            return pts
        r_0, r_1 = ring(r0, 0.0), ring(r1, h)
        mid = {"y": (cx, cy + h / 2, cz), "x": (cx + h / 2, cy, cz), "z": (cx, cy, cz + h / 2)}[axis]
        for i in range(sides):
            j = (i + 1) % sides
            self.face_out([r_0[i], r_0[j], r_1[j], r_1[i]], col, mid)
        if cap:
            cc = col if cap_col is None else cap_col
            if r0 > 1e-5: self.face_out(r_0, cc, mid)
            if r1 > 1e-5: self.face_out(r_1, cc, mid)

    def lathe(self, cx, cz, profile, cols, sides=8, base_y=0.0):
        """Turned shape: profile [(y, r), ...] bottom to top; cols one per segment (or one colour)."""
        if isinstance(cols, int):
            cols = [cols] * (len(profile) - 1)
        for k in range(len(profile) - 1):
            (y0, r0), (y1, r1) = profile[k], profile[k + 1]
            self.frustum(cx, base_y + y0, cz, r0, r1, y1 - y0, cols[k], sides, cap=(k == 0 or k == len(profile) - 2))

    def prism(self, outline, y0, y1, col, top_col=None):
        outline = upward(outline)
        n = len(outline)
        bottom = [(x, y0, z) for x, z in outline]
        top = [(x, y1, z) for x, z in outline]
        self.face(list(reversed(bottom)), col)
        self.face(top, top_col if top_col is not None else col)
        for i in range(n):
            j = (i + 1) % n
            self.face([bottom[i], bottom[j], top[j], top[i]], col)

    def flat(self, outline, y, col):
        self.face([(x, y, z) for x, z in upward(outline)], col)

    def triangles(self):
        return sum(len(f) - 2 for f, _ in self.faces)


def ring_outline(cx, cz, rx, rz, n, phase=0.0):
    return [(cx + math.cos(2 * math.pi * (i + phase) / n) * rx, cz + math.sin(2 * math.pi * (i + phase) / n) * rz)
            for i in range(n)]


def blob_outline(cx, cz, rx, rz, n, seed):
    rnd = Rand(seed)
    return [(cx + math.cos(2 * math.pi * i / n) * rx * (0.72 + 0.5 * rnd.next()),
             cz + math.sin(2 * math.pi * i / n) * rz * (0.72 + 0.5 * rnd.next())) for i in range(n)]


def rot_y(pts, yaw_deg, ox=0.0, oz=0.0):
    a = math.radians(yaw_deg)
    c, s = math.cos(a), math.sin(a)
    return [(ox + (x - ox) * c + (z - oz) * s, y, oz - (x - ox) * s + (z - oz) * c) for x, y, z in pts]


# ---------------------------------------------------------------------------------------------------------------
# Maya mesh + FBX export
# ---------------------------------------------------------------------------------------------------------------
def make_mesh(name, part):
    pts = om.MPointArray()
    counts, connects, uv_ids = [], [], []
    us, vs = om.MFloatArray(), om.MFloatArray()
    for i in range(len(PALETTE)):
        u, v = cell_uv(i)
        us.append(u); vs.append(v)
    index = {}
    for verts, col in part.faces:
        counts.append(len(verts))
        for p in verts:
            key = (round(p[0], 5), round(p[1], 5), round(p[2], 5))
            if key not in index:
                index[key] = len(index)
                pts.append(om.MPoint(*key))
            connects.append(index[key])
            uv_ids.append(col)
    fn = om.MFnMesh()
    obj = fn.create(pts, counts, connects, us, vs)   # returns the new transform
    fn.assignUVs(counts, uv_ids)
    node = cmds.rename(om.MFnDagNode(obj).fullPathName(), name)
    cmds.polySoftEdge(node, angle=0, constructionHistory=False)   # flat shading: every edge hard
    return node


MATERIALS = {}


def material(name):
    if name not in MATERIALS:
        shader = cmds.shadingNode("lambert", asShader=True, name="M_" + name)
        sg = cmds.sets(renderable=True, noSurfaceShader=True, empty=True, name="M_" + name + "SG")
        cmds.connectAttr(shader + ".outColor", sg + ".surfaceShader", force=True)
        MATERIALS[name] = sg
    return MATERIALS[name]


REPORT = []


def save_maya_scene(name):
    """Saves the current object's scene as tools/maya/scenes/<name>.ma, after the FBX export (so the FBX is unchanged):
    the palette material gets the Palette.png file texture (point-filtered look) and glass/outline become see-through."""
    for key, sg in MATERIALS.items():
        shader = cmds.listConnections(sg + ".surfaceShader", source=True, destination=False)[0]
        if key == "Palette":
            tex = cmds.shadingNode("file", asTexture=True, name="Palette_tex")
            cmds.setAttr(tex + ".fileTextureName", os.path.join(OUT, "Palette.png").replace("\\", "/"), type="string")
            cmds.setAttr(tex + ".filterType", 0)   # off: crisp palette cells
            cmds.connectAttr(tex + ".outColor", shader + ".color", force=True)
        elif key == "Glass":
            cmds.setAttr(shader + ".color", 0.76, 0.91, 0.96, type="double3")
            cmds.setAttr(shader + ".transparency", 0.75, 0.75, 0.75, type="double3")
        elif key == "Alpha":
            cmds.setAttr(shader + ".color", 0.95, 0.95, 0.95, type="double3")
            cmds.setAttr(shader + ".transparency", 0.4, 0.4, 0.4, type="double3")
    if not os.path.isdir(SCENES):
        os.makedirs(SCENES)
    cmds.file(rename=os.path.join(SCENES, name + ".ma").replace("\\", "/"))
    cmds.file(save=True, type="mayaAscii", force=True)


def export(name, body, glass=None, alpha=None, kind="prop", out=None):
    """Builds the group <name> with <name>_Body (+ _Glass / _Alpha), exports <name>.fbx, records size + triangles."""
    cmds.file(new=True, force=True)
    MATERIALS.clear()
    cmds.currentUnit(linear="m")
    group = cmds.group(empty=True, name=name)
    tris = 0
    lo, hi = [1e9] * 3, [-1e9] * 3
    for suffix, part, mat in (("_Body", body, "Palette"), ("_Glass", glass, "Glass"), ("_Alpha", alpha, "Alpha")):
        if part is None or not part.faces:
            continue
        node = make_mesh(name + suffix, part)
        cmds.sets(node, edit=True, forceElement=material(mat))
        cmds.parent(node, group)
        tris += part.triangles()
        for verts, _ in part.faces:
            for p in verts:
                for k in range(3):
                    lo[k] = min(lo[k], p[k]); hi[k] = max(hi[k], p[k])
    cmds.select(group, replace=True)
    path = os.path.join(out or OUT, name + ".fbx").replace("\\", "/")
    for cmd in ("FBXResetExport", "FBXExportSmoothingGroups -v true", "FBXExportTriangulate -v true",
                "FBXExportSmoothMesh -v false", "FBXExportCameras -v false", "FBXExportLights -v false",
                "FBXExportAnimationOnly -v false", "FBXExportBakeComplexAnimation -v false",
                "FBXExportUpAxis y", "FBXExportConvertUnitString m", "FBXExportInAscii -v false"):
        mel.eval(cmd)
    mel.eval('FBXExport -f "{}" -s'.format(path))
    save_maya_scene(name)
    REPORT.append((kind, name, tuple(round(hi[k] - lo[k], 3) for k in range(3)), round(hi[1], 3), tris))


# ---------------------------------------------------------------------------------------------------------------
# Set dressing (small objects on furniture). y = the surface they stand on.
# ---------------------------------------------------------------------------------------------------------------
def mug(p, x, y, z, col, col_in):
    p.frustum(x, y, z, 0.038, 0.042, 0.095, col, sides=10, cap_col=col)
    p.frustum(x, y + 0.093, z, 0.034, 0.034, 0.003, col_in, sides=10)              # coffee surface
    p.cbb(x + 0.05, y + 0.025, z, 0.02, 0.05, 0.012, col, b=0.004)                  # handle


def plant(p, x, y, z, seed, pot=C["terracotta"], pot_dk=None, scale=1.0):
    s = scale
    p.lathe(x, z, [(0, 0.045 * s), (0.08 * s, 0.06 * s), (0.095 * s, 0.065 * s), (0.105 * s, 0.065 * s)],
            [pot, pot, pot_dk if pot_dk is not None else pot], sides=8, base_y=y)
    p.frustum(x, y + 0.1 * s, z, 0.055 * s, 0.055 * s, 0.004, C["soil"], sides=8)
    rnd = Rand(seed)
    for i in range(7):                                                              # faceted leaves
        a = 2 * math.pi * i / 7 + rnd.range(-0.2, 0.2)
        length = rnd.range(0.12, 0.2) * s
        lift = rnd.range(0.35, 0.8)
        base = (x, y + 0.1 * s, z)
        tip = (x + math.cos(a) * length * math.cos(lift), y + 0.1 * s + length * math.sin(lift) + 0.04 * s,
               z + math.sin(a) * length * math.cos(lift))
        w = 0.028 * s
        side = (-math.sin(a) * w, 0.0, math.cos(a) * w)
        mid = ((base[0] + tip[0]) / 2, (base[1] + tip[1]) / 2 + 0.02 * s, (base[2] + tip[2]) / 2)
        l1 = (mid[0] + side[0], mid[1], mid[2] + side[2])
        l2 = (mid[0] - side[0], mid[1], mid[2] - side[2])
        col = C["leaf"] if i % 2 else C["green"]
        p.face([base, l1, tip], col); p.face([base, tip, l1], C["green_dk"])
        p.face([base, tip, l2], col); p.face([base, l2, tip], C["green_dk"])


def book_stack(p, x, y, z, cols, seed, yaw=0.0):
    rnd = Rand(seed)
    h = y
    for col in cols:
        t = rnd.range(0.022, 0.035)
        w, d = rnd.range(0.15, 0.2), rnd.range(0.2, 0.24)
        a = yaw + rnd.range(-12, 12)
        part = Part()
        part.cbb(0, h, 0, w, t, d, col, b=0.003)
        part.cbb(0.004, h + 0.003, 0, w - 0.01, t - 0.006, d - 0.004, C["paper"])  # page block
        for verts, c in part.faces:
            p.face([(px + x, py, pz + z) for px, py, pz in rot_y(verts, a)], c)
        h += t


def laptop(p, x, y, z, body_col, screen_col, yaw=0.0):
    part = Part()
    part.cbb(0, 0, 0, 0.30, 0.016, 0.21, body_col, b=0.005)                        # base
    part.cbb(0, 0.016, 0.02, 0.26, 0.002, 0.10, C["grey_dk"])                      # keyboard
    part.cbb(0, 0.016, -0.065, 0.08, 0.002, 0.05, C["grey"])                       # touchpad
    lid = Part()
    lid.cbb(0, 0, 0, 0.30, 0.20, 0.010, body_col, b=0.005)
    lid.cbb(0, 0.012, -0.0055, 0.27, 0.17, 0.002, screen_col)
    lid.cbb(-0.05, 0.10, -0.0068, 0.10, 0.04, 0.001, C["screen_lt"])
    tilt = math.radians(-18)
    for verts, c in lid.faces:                                                     # hinge at the back, tilted back
        rv = []
        for vx, vy, vz in verts:
            ry = vy * math.cos(tilt) - vz * math.sin(tilt)
            rz = vy * math.sin(tilt) + vz * math.cos(tilt)
            rv.append((vx, ry + 0.016, rz + 0.105))
        part.face(rv, c)
    for verts, c in part.faces:
        p.face([(px + x, py + y, pz + z) for px, py, pz in rot_y(verts, yaw)], c)


def pen_cup(p, x, y, z):
    p.frustum(x, y, z, 0.03, 0.032, 0.09, C["charcoal"], sides=8)
    for i, col in enumerate((C["blue"], C["red"], C["yellow"])):
        a = i * 2.1
        p.frustum(x + math.cos(a) * 0.012, y + 0.05, z + math.sin(a) * 0.012, 0.004, 0.003, 0.09, col, sides=5)


def desk_lamp(p, x, y, z, col, yaw=0.0):
    part = Part()
    part.frustum(0, 0, 0, 0.06, 0.055, 0.018, col, sides=10)
    part.frustum(0, 0.018, 0, 0.008, 0.008, 0.22, C["chrome"], sides=6)
    part.frustum(0.0, 0.24, 0.0, 0.008, 0.008, 0.14, C["chrome"], sides=6, axis="z")
    part.frustum(0.0, 0.17, 0.14, 0.06, 0.03, 0.07, col, sides=10, axis="y")       # shade, opening downwards
    part.frustum(0.0, 0.173, 0.14, 0.054, 0.054, 0.002, C["lamp"], sides=10)       # bulb glow under the shade
    for verts, c in part.faces:
        p.face([(px + x, py + y, pz + z) for px, py, pz in rot_y(verts, yaw)], c)


def office_chair(p, x, z, seat_col, frame_col, yaw=0.0):
    part = Part()
    for i in range(5):                                                             # star base on castors
        a = 2 * math.pi * i / 5
        ex, ez = math.cos(a) * 0.27, math.sin(a) * 0.27
        part.face_out([(0, 0.07, 0), (ex, 0.05, ez), (ex, 0.035, ez), (0, 0.045, 0)], frame_col, (0, 0, 0))
        part.cbb(ex * 0.9, 0.0, ez * 0.9, 0.03, 0.04, 0.03, C["rubber"], b=0.006)
        part.bbox(-0.01 + ex * 0.5, 0.045, -0.01 + ez * 0.5, 0.01 + ex * 0.5, 0.065, 0.01 + ez * 0.5, frame_col)
    part.frustum(0, 0.06, 0, 0.022, 0.018, 0.37, C["chrome"], sides=8)            # gas lift
    part.cbb(0, 0.43, 0, 0.46, 0.07, 0.44, seat_col, b=0.025, edge_col=seat_col)   # seat
    part.cbb(0, 0.43, -0.2, 0.06, 0.25, 0.03, frame_col, b=0.006)                 # back post
    part.cbb(0, 0.62, -0.23, 0.42, 0.36, 0.06, seat_col, b=0.025)                 # backrest
    for sx in (-1, 1):                                                             # armrests
        part.cbb(sx * 0.24, 0.48, -0.02, 0.03, 0.16, 0.03, frame_col, b=0.005)
        part.cbb(sx * 0.24, 0.64, 0.0, 0.05, 0.025, 0.24, C["rubber"], b=0.008)
    for verts, c in part.faces:
        p.face([(px + x, py, pz + z) for px, py, pz in rot_y(verts, yaw)], c)


def wooden_chair(p, x, z, wood, wood_dk, cushion, yaw=0.0):
    part = Part()
    for sx in (-1, 1):
        for sz in (-1, 1):
            part.lathe(sx * 0.19, sz * 0.18, [(0, 0.016), (0.2, 0.02), (0.22, 0.026), (0.24, 0.02), (0.45, 0.018)],
                       [wood_dk, wood, wood, wood], sides=6)
    part.cbb(0, 0.43, 0, 0.44, 0.04, 0.42, wood, b=0.008, edge_col=wood_dk)        # seat
    part.cbb(0, 0.47, 0.01, 0.38, 0.03, 0.36, cushion, b=0.012)                   # batik cushion
    for sx in (-1, 1):
        part.cbb(sx * 0.19, 0.45, -0.18, 0.035, 0.5, 0.035, wood_dk, b=0.006)       # back posts
    for k, y in enumerate((0.68, 0.84)):
        part.cbb(0, y, -0.18, 0.38, 0.06, 0.025, wood if k == 0 else wood_dk, b=0.006)
    part.cbb(0, 0.12, 0, 0.36, 0.02, 0.02, wood_dk, b=0.004)                      # stretcher
    for verts, c in part.faces:
        p.face([(px + x, py, pz + z) for px, py, pz in rot_y(verts, yaw)], c)


def batik_band(p, x0, x1, y0, y1, z, base, motif, motif2, depth=0.002, count=7, facing=1):
    """Batik-inspired band on a surface at depth z facing +Z (facing=1) or -Z (facing=-1): base strip with diamond
    and dot motifs."""
    f = 1 if facing >= 0 else -1
    p.bbox(x0, y0, z, x1, y1, z + f * depth, base)
    w = (x1 - x0) / count
    cy = (y0 + y1) / 2
    hh = (y1 - y0) * 0.36
    for i in range(count):
        cx = x0 + w * (i + 0.5)
        zm = z + f * (depth + 0.001)
        p.face_out([(cx - w * 0.36, cy, zm), (cx, cy - hh, zm), (cx + w * 0.36, cy, zm), (cx, cy + hh, zm)],
                   motif, (cx, cy, z))
        p.face_out([(px, py, z + f * (depth + 0.0015)) for px, _, py in
                    [(a, 0, b) for a, b in ring_outline(cx, cy, w * 0.1, hh * 0.32, 6)]], motif2, (cx, cy, z))


# ---------------------------------------------------------------------------------------------------------------
# Props
# ---------------------------------------------------------------------------------------------------------------
def display_case(w, d, studio):
    """Glass display case on a plinth (front +Z pane swung open: the case was found open). Top 1.63 m: the case is
    tall enough for the K1 keypad (eye height 1.5 m) on its lock-side glass (local -X, opposite the hinge)."""
    body, glass = Part(), Part()
    if studio:
        main, main_dk, trim, frame = C["white"], C["white_dk"], C["grey_dk"], C["silver"]
    else:
        main, main_dk, trim, frame = C["walnut"], C["walnut_dk"], C["wood_lt"], C["brass"]
    ph = 0.95
    body.cbb(0, 0, 0, w - 0.03, 0.07, d - 0.03, trim, b=0.01)                      # recessed base
    body.cbb(0, 0.07, 0, w - 0.06, ph - 0.15, d - 0.06, main, b=0.012, edge_col=main_dk)
    if studio:
        body.cbb(0, 0.10, d / 2 - 0.03, w - 0.12, 0.012, 0.004, C["teal"])         # light strip
        body.cbb(0, 0.60, d / 2 - 0.028, 0.16, 0.06, 0.006, C["charcoal"], b=0.004)  # placard
        for i in range(3):
            body.cbb(-0.02 + 0.01 * (i == 0), 0.635 - i * 0.012, d / 2 - 0.022, 0.1 - 0.03 * i, 0.004, 0.002, C["white"])
    else:
        body.cbb(0, 0.07, 0, w, 0.05, d, main_dk, b=0.012, edge_col=trim)          # base moulding
        batik_band(body, -w / 2 + 0.05, w / 2 - 0.05, 0.56, 0.66, d / 2 - 0.03, C["indigo"], C["ochre"], C["batik_lt"])
        body.cbb(0, 0.40, d / 2 - 0.028, 0.14, 0.05, 0.006, C["brass"], b=0.004)   # brass plaque
        body.cbb(0, ph - 0.12, 0, w + 0.01, 0.04, d + 0.01, main_dk, b=0.01, edge_col=trim)  # cornice
    body.cbb(0, ph - 0.08, 0, w, 0.08, d, main if studio else main_dk, b=0.015, edge_col=main_dk if studio else trim)  # top slab
    case_h = 0.66
    y0 = ph
    x0, x1, z0, z1 = -w / 2 + 0.035, w / 2 - 0.035, -d / 2 + 0.035, d / 2 - 0.035
    t = 0.016
    for x in (x0, x1 - t):                                                         # frame posts
        for z in (z0, z1 - t):
            body.bbox(x, y0, z, x + t, y0 + case_h, z + t, frame, b=0.003)
    for zz in (z0, z1 - t):                                                        # top rails
        body.bbox(x0, y0 + case_h - t, zz, x1, y0 + case_h, zz + t, frame, b=0.003)
    for xx in (x0, x1 - t):
        body.bbox(xx, y0 + case_h - t, z0, xx + t, y0 + case_h, z1, frame, b=0.003)
    body.cbb(0, y0 + case_h, 0, x1 - x0 + 0.01, 0.022, z1 - z0 + 0.01, frame, b=0.006)  # lid
    # Empty stand: the award (X) / the keris cradle (Y) is gone.
    if studio:
        body.cbb(0, y0, -0.02, 0.12, 0.03, 0.10, C["charcoal"], b=0.006)
        body.cbb(0, y0 + 0.03, -0.02, 0.05, 0.08, 0.05, C["grey_dk"], b=0.004)
    else:
        body.cbb(0, y0, -0.02, 0.26, 0.02, 0.08, C["maroon"], b=0.004)             # velvet pad
        for sx in (-1, 1):
            body.cbb(sx * 0.08, y0 + 0.02, -0.02, 0.02, 0.06, 0.05, C["wood_dk"], b=0.004)  # cradle forks
    glass.bbox(x0 + t, y0, z0 + 0.004, x1 - t, y0 + case_h - t, z0 + 0.008, C["glass"])          # back pane
    glass.bbox(x0 + 0.004, y0, z0 + t, x0 + 0.008, y0 + case_h - t, z1 - t, C["glass"])          # side panes
    glass.bbox(x1 - 0.008, y0, z0 + t, x1 - 0.004, y0 + case_h - t, z1 - t, C["glass"])
    # Front pane: hinged on the local +X post, swung 35 degrees out (+Z), with its own thin frame.
    a = math.radians(35)
    pw = x1 - x0 - 2 * t
    hx, hz = x1 - t, z1
    dx, dz = -math.cos(a), math.sin(a)
    ex, ez = hx + dx * pw, hz + dz * pw
    pane = [(hx, y0 + 0.01, hz), (ex, y0 + 0.01, ez), (ex, y0 + case_h - t - 0.01, ez), (hx, y0 + case_h - t - 0.01, hz)]
    glass.face(pane, C["glass"]); glass.face(list(reversed(pane)), C["glass"])
    nx, nz = -dz * 0.006, dx * 0.006
    for (ax, az), (bx, bz) in (((hx, hz), (ex, ez)),):
        for yy in (y0 + 0.004, y0 + case_h - t - 0.016):
            body.face_out([(ax, yy, az), (bx, yy, bz), (bx, yy + 0.012, bz), (ax, yy + 0.012, az)], frame,
                          (ax - nx, yy + 0.006, az - nz))
            body.face_out([(ax, yy, az), (bx, yy, bz), (bx, yy + 0.012, bz), (ax, yy + 0.012, az)], frame,
                          (ax + nx, yy + 0.006, az + nz))
    body.cbb(ex, y0 + 0.004, ez, 0.012, case_h - t - 0.008, 0.012, frame, b=0.002)  # free edge post
    # Mirrored so the hinge ends up at Unity local +X (Unity's import negates X): in the room it is on the far side
    # from the K1 keypad (CaseSpecs: K1 on the display's lock side).
    return mirrored(body, 0), mirrored(glass, 0)


def desk(w, d, studio, front, drawers_side=None):
    """Desk / reception table, top 0.75 m. Front = +Z = the room side, where people use it (drawers, laptop
    keyboard and the clues all face that way). The modesty panel is always on the back (wall side) and stops beside the
    drawer pedestal, so it never covers the drawers."""
    p = Part()
    h = 0.75
    ped = None if drawers_side is None else drawers_side * (w / 2 - 0.19)   # pedestal centre x (0.30 wide)

    def panel_span(margin):
        x0, x1 = -w / 2 + margin, w / 2 - margin
        if ped is not None:
            if ped > 0: x1 = min(x1, ped - 0.155)
            else: x0 = max(x0, ped + 0.155)
        return x0, x1, -1
    if studio:
        top, top_dk, leg = C["white"], C["white_dk"], C["charcoal"]
        p.cbb(0, h - 0.035, 0, w, 0.035, d, top, b=0.008, edge_col=top_dk)
        for sx in (-1, 1):                                                        # sled legs
            x = sx * (w / 2 - 0.06)
            p.cbb(x, 0, 0, 0.04, 0.025, d - 0.04, leg, b=0.006)
            p.cbb(x, 0.025, d / 2 - 0.05, 0.035, h - 0.06, 0.035, leg, b=0.006)
            p.cbb(x, 0.025, -d / 2 + 0.05, 0.035, h - 0.06, 0.035, leg, b=0.006)
            p.cbb(x, h - 0.07, 0, 0.035, 0.035, d - 0.08, leg, b=0.006)
        x0, x1, side = panel_span(0.075)
        p.cbb((x0 + x1) / 2, 0.28, side * (d / 2 - 0.035), x1 - x0, h - 0.35, 0.018, front, b=0.006)   # modesty panel
        p.cbb((x0 + x1) / 2, 0.42, side * (d / 2 - 0.025), x1 - x0 - 0.07, 0.02, 0.004, C["white"])   # accent line
    else:
        top, top_dk = C["wood_lt"], C["wood"]
        p.cbb(0, h - 0.04, 0, w, 0.04, d, top, b=0.01, edge_col=top_dk)
        p.cbb(0, h - 0.12, 0, w - 0.08, 0.08, d - 0.08, C["wood_dk"], b=0.006)      # apron
        for sx in (-1, 1):
            if drawers_side is not None and sx == drawers_side: continue   # the pedestal carries that side
            for sz in (-1, 1):
                p.lathe(sx * (w / 2 - 0.06), sz * (d / 2 - 0.06),
                        [(0, 0.02), (0.06, 0.028), (0.09, 0.022), (0.30, 0.03), (0.34, 0.036), (0.38, 0.03),
                         (0.63, 0.026)],
                        [C["walnut"], C["wood_dk"], C["wood_dk"], C["wood"], C["wood"], C["wood_dk"]], sides=8)
        if front is not None:
            x0, x1, side = panel_span(0.08)
            p.cbb((x0 + x1) / 2, 0.30, side * (d / 2 - 0.045), x1 - x0, h - 0.42, 0.015, front, b=0.004)
            batik_band(p, x0 + 0.02, x1 - 0.02, 0.42, 0.50, side * (d / 2 - 0.0375), C["indigo"], C["ochre"], C["batik_lt"],
                       count=7 if ped is None else 4, facing=side)
    if ped is not None:                                                             # drawer pedestal
        x = ped
        body_col = C["white_dk"] if studio else C["walnut"]
        p.cbb(x, 0.03, -0.01, 0.30, h - 0.07, d - 0.08, body_col, b=0.006)
        for i in range(3):
            y = 0.06 + i * 0.21
            p.cbb(x, y, d / 2 - 0.05, 0.27, 0.18, 0.012, C["white"] if studio else C["wood"], b=0.004)
            p.cbb(x, y + 0.13, d / 2 - 0.038, 0.09, 0.015, 0.012, C["chrome"] if studio else C["brass"], b=0.003)
    return p


def front_desk_x():
    p = desk(0.9, 0.5, True, C["teal"])
    top = 0.75
    laptop(p, -0.28, top, -0.02, C["silver"], C["screen"], yaw=180)               # world: right of the sheet
    pen_cup(p, 0.33, top, 0.12)
    plant(p, 0.33, top, -0.12, seed=3, pot=C["white"], pot_dk=C["white_dk"], scale=0.8)
    return p


def manager_desk_x():
    p = desk(0.9, 0.5, True, C["coral"], drawers_side=1)
    top = 0.75
    mug(p, 0.30, top, 0.10, C["coral"], C["coffee"])
    book_stack(p, -0.30, top, -0.05, [C["blue_dk"], C["teal"], C["charcoal"]], seed=5, yaw=8)
    desk_lamp(p, 0.32, top, -0.14, C["charcoal"], yaw=200)
    return p


def reception_table_y():
    p = desk(0.9, 0.5, False, C["walnut"])
    top = 0.75
    p.cbb(0, top, 0, 0.24, 0.004, 0.52, C["indigo"])                              # batik runner
    for i in range(4):
        p.cbb(0, top + 0.004, -0.19 + i * 0.125, 0.12, 0.0015, 0.03, C["ochre"] if i % 2 else C["batik_lt"])
    plant(p, 0.33, top, -0.10, seed=8, pot=C["indigo"], pot_dk=C["indigo_dk"], scale=0.9)
    p.frustum(-0.33, top, 0.12, 0.035, 0.03, 0.012, C["walnut"], sides=8)          # bell base
    p.lathe(-0.33, 0.12, [(0, 0.03), (0.02, 0.028), (0.045, 0.012), (0.06, 0.004)], C["brass"], sides=8, base_y=top + 0.012)
    p.cbb(-0.30, top, -0.12, 0.12, 0.02, 0.18, C["cream"], b=0.003)                # brochures
    p.cbb(-0.30, top + 0.02, -0.12, 0.1, 0.002, 0.12, C["maroon"])
    return p


def tour_desk_y():
    p = desk(0.9, 0.5, False, C["maroon_dk"], drawers_side=1)
    top = 0.75
    book_stack(p, -0.30, top, -0.06, [C["maroon"], C["walnut_dk"], C["ochre_dk"]], seed=9, yaw=-6)
    p.frustum(0.30, top, 0.10, 0.04, 0.045, 0.10, C["batik_lt"], sides=8)          # cup (batik)
    p.frustum(0.30, top + 0.098, 0.10, 0.036, 0.036, 0.003, C["coffee"], sides=8)
    p.frustum(0.30, top, -0.14, 0.008, 0.008, 0.32, C["brass"], sides=6)            # tour flag
    p.face_out([(0.30, top + 0.32, -0.14), (0.42, top + 0.28, -0.14), (0.30, top + 0.24, -0.14)], C["ochre"], (0.33, top + 0.28, -0.15))
    p.face_out([(0.30, top + 0.32, -0.14), (0.42, top + 0.28, -0.14), (0.30, top + 0.24, -0.14)], C["ochre_dk"], (0.33, top + 0.28, -0.13))
    return p


def monitor_stand():
    """Studio monitor, wall-mounted style (no stand, user: legs looked odd), 0.4 m wide (X), screen facing +Z; screen
    front at z 0.045 so R3 sits on it. Hangs at 1.30-1.58 m."""
    p = Part()
    p.cbb(0, 1.38, 0.0025, 0.12, 0.10, 0.015, C["charcoal"], b=0.003)               # wall-mount plate behind the screen
    p.cbb(0, 1.30, 0.0275, 0.40, 0.28, 0.035, C["black"], b=0.008)                 # housing
    p.cbb(0, 1.312, 0.0445, 0.37, 0.25, 0.002, C["screen"])                        # display
    for k, (x, y, w, h, col) in enumerate([(-0.11, 1.48, 0.12, 0.05, "screen_lt"), (0.09, 1.47, 0.15, 0.07, "teal"),
                                            (-0.10, 1.36, 0.14, 0.06, "coral"), (0.11, 1.35, 0.11, 0.05, "screen_lt")]):
        p.cbb(x, y - h / 2, 0.0465, w, h, 0.001, C[col])                          # windows on the screen
    p.cbb(0, 1.58, 0.0275, 0.04, 0.012, 0.02, C["charcoal"], b=0.003)              # webcam
    return p


def noticeboard():
    """Gallery cork noticeboard, wall-hung style (no legs, user), 0.4 m wide (X), facing +Z, 1.10-1.62 m. Centre
    (x ~0, 1.42-1.58 m) kept free for R3."""
    p = Part()
    p.cbb(0, 1.10, -0.005, 0.43, 0.52, 0.035, C["walnut"], b=0.01, edge_col=C["walnut_dk"])  # frame
    p.cbb(0, 1.125, 0.0125, 0.38, 0.47, 0.002, C["cork"])                          # cork
    rnd = Rand(17)
    for k in range(10):                                                            # cork speckles
        x, y = rnd.range(-0.17, 0.17), rnd.range(1.14, 1.58)
        p.cbb(x, y, 0.0145, 0.012, 0.008, 0.0005, C["cork_dk"])
    for x, y, w, h, col in [(-0.12, 1.50, 0.10, 0.12, "paper"), (0.125, 1.49, 0.09, 0.11, "cream"),
                            (-0.11, 1.26, 0.12, 0.14, "batik_lt"), (0.11, 1.25, 0.11, 0.12, "pink"),
                            (0.0, 1.20, 0.08, 0.07, "paper")]:
        p.cbb(x, y - h / 2, 0.0146, w, h, 0.0015, C[col])
        for i in range(3):
            p.cbb(x - w * 0.1, y + h / 2 - 0.03 - i * 0.022, 0.0162, w * 0.6, 0.006, 0.0005, C["ink"])
        p.frustum(x, y + h / 2 - 0.012, 0.016, 0.006, 0.005, 0.008, C["red"], sides=6, axis="z")  # pin
    batik_band(p, -0.19, 0.19, 1.585, 1.615, 0.013, C["maroon"], C["ochre"], C["batik_lt"], count=6)
    p.cbb(0, 1.105, 0.0125, 0.12, 0.02, 0.004, C["brass"], b=0.002)                # brass plaque
    return p


def evidence_board():
    """The evidence board's physical board (shared by both cases, user: "like an actual evidence board"): a wall-hung
    cork board in a wooden frame, 1.34 x 0.96 m, centred on the origin, READABLE SIDE -Z like a uGUI canvas (the board's
    pokeable cards are uGUI on a canvas 1.10 x 0.74 m at z = 0, in front of the cork). Pushpins and clippings only in
    the cork margin around the canvas; a ledge under it with a magnifying glass, markers and a pin box.
    Built in Unity coordinates (mirrored on export)."""
    p = Part()
    fw, fh, bar = 0.67, 0.48, 0.05
    wood, wood_dk = C["walnut"], C["walnut_dk"]
    p.bbox(-fw, fh - bar, -0.012, fw, fh, 0.03, wood, b=0.006, edge_col=wood_dk)            # frame: top
    p.bbox(-fw, -fh, -0.012, fw, -fh + bar, 0.03, wood, b=0.006, edge_col=wood_dk)          # bottom
    p.bbox(-fw, -fh + bar, -0.012, -fw + bar, fh - bar, 0.03, wood, b=0.006, edge_col=wood_dk)  # left
    p.bbox(fw - bar, -fh + bar, -0.012, fw, fh - bar, 0.03, wood, b=0.006, edge_col=wood_dk)    # right
    cx, cy = fw - bar, fh - bar
    p.bbox(-cx, -cy, 0.004, cx, cy, 0.03, C["cork"])                                       # cork (front at z 0.004)
    rnd = Rand(91)
    for k in range(60):                                                                    # cork speckles
        x, y = rnd.range(-cx + 0.01, cx - 0.02), rnd.range(-cy + 0.01, cy - 0.02)
        p.bbox(x, y, 0.0034, x + rnd.range(0.006, 0.014), y + rnd.range(0.004, 0.009), 0.004, C["cork_dk"])
    # Margin decoration (outside the canvas: |x| > 0.55 or |y| > 0.37).
    def pin(x, y, col):
        p.frustum(x, y, -0.012, 0.007, 0.006, 0.010, col, sides=6, axis="z")
        p.frustum(x, y, -0.002, 0.0015, 0.0015, 0.006, C["silver"], sides=4, axis="z")
    def clipping(x0, y0, x1, y1, col, lines, seed):
        p.bbox(x0, y0, 0.0028, x1, y1, 0.0034, col)
        r = Rand(seed)
        for i in range(lines):
            y = y1 - 0.012 - i * 0.012
            if y < y0 + 0.008: break
            p.bbox(x0 + 0.006, y, 0.0024, x0 + 0.006 + (x1 - x0 - 0.012) * r.range(0.5, 1.0), y + 0.004, 0.0028, C["ink"])
    clipping(-0.615, 0.12, -0.56, 0.33, C["paper_dk"], 14, 92)                              # newspaper clipping, left
    pin(-0.588, 0.315, C["red"])
    clipping(0.56, -0.30, 0.615, -0.08, C["paper"], 12, 93)                                 # note, right
    pin(0.588, -0.095, C["blue"])
    p.bbox(0.56, 0.20, 0.0028, 0.615, 0.34, 0.0034, C["skin_card"])                         # photo, right
    p.bbox(0.567, 0.21, 0.0024, 0.608, 0.30, 0.0028, C["grey_dk"])
    pin(0.588, 0.325, C["yellow"])
    for x, y, col in ((-0.585, -0.40, C["green"]), (0.30, 0.405, C["red"]), (-0.25, -0.405, C["yellow"])):
        pin(x, y, col)
    p.bbox(-0.05, 0.385, 0.0028, 0.05, 0.415, 0.0034, C["batik_lt"])                       # torn tape strip
    # Ledge under the board with detective tools.
    ly = -fh
    p.bbox(-0.45, ly - 0.022, -0.075, 0.45, ly, 0.03, wood, b=0.004, edge_col=wood_dk)       # shelf
    p.bbox(-0.45, ly, -0.075, 0.45, ly + 0.012, -0.068, wood_dk)                            # lip
    p.frustum(0.22, ly, -0.035, 0.038, 0.038, 0.007, C["brass"], sides=12, cap_col=C["glass"])   # magnifier ring + lens
    p.bbox(0.258, ly, -0.041, 0.34, ly + 0.012, -0.029, wood_dk, b=0.003)                   # magnifier handle
    p.frustum(-0.34, ly + 0.009, -0.045, 0.009, 0.009, 0.12, C["red"], sides=6, axis="x")   # markers
    p.frustum(-0.34, ly + 0.009, -0.02, 0.009, 0.009, 0.12, C["black"], sides=6, axis="x")
    p.frustum(-0.22, ly + 0.009, -0.045, 0.006, 0.002, 0.012, C["white_dk"], sides=6, axis="x")   # cap tips
    p.cbb(-0.05, ly, -0.035, 0.07, 0.03, 0.045, C["white"], b=0.003)                       # pin box
    for i, col in enumerate((C["red"], C["blue"], C["yellow"], C["green"])):
        p.frustum(-0.07 + i * 0.013, ly + 0.03, -0.035, 0.004, 0.004, 0.004, col, sides=5)
    return to_maya(p)


# ---------------------------------------------------------------------------------------------------------------
# Briefing detective (session 6, user): "Inspector Hart", stylised faceted low-poly (user's reference: slender
# proportions, organic shapes made of visible triangular facets, line eyes, real hands, clothing details).
# Built with lofts: shaped cross-section rings swept along a body part and joined by triangles (with a little seeded
# irregularity so the facets read), flat shaded. Four parts so Unity can animate them without a rig
# (DetectiveBriefing): body (pivot on the floor), head (pivot at the neck, DETECTIVE_HEAD_AT), moustache (pivot at its
# centre, DETECTIVE_MOUSTACHE_AT in head space), right arm with a magnifying glass (pivot at the shoulder,
# DETECTIVE_ARM_AT). Built in Unity coordinates (+Z = front), mirrored on export. Keep the offsets in sync with
# CharacterSetupTool (Unity).
# ---------------------------------------------------------------------------------------------------------------
DETECTIVE_HEAD_AT = (0.0, 1.585, 0.012)
DETECTIVE_MOUSTACHE_AT = (0.0, 0.074, 0.124)
DETECTIVE_ARM_AT = (0.195, 1.425, -0.005)
DETECTIVE_ARM_L_AT = (-0.195, 1.425, -0.005)
DETECTIVE_EYES_AT = (0.0, 0.14, 0.1155)     # head space
DETECTIVE_ELBOW_R_AT = (0.035, -0.27, -0.01)    # right shoulder space: forearm pivot
DETECTIVE_ELBOW_L_AT = (-0.04, -0.275, -0.025)  # left shoulder space: forearm pivot
DETECTIVE_MOUTH_AT = (0.0, 0.053, 0.119)    # head space


def _v_add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def _v_sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _v_mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def _v_cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _v_norm(a):
    l = math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]) or 1.0
    return (a[0] / l, a[1] / l, a[2] / l)


def ring_y(y, rx, rz, n, cx=0.0, cz=0.0, shape=None, phase=0.5, jitter=0.0, rnd=None):
    """Horizontal ring (constant y): ellipse rx x rz, optionally reshaped by shape(angle) -> radius factor.
    Angle 0 = +X, pi/2 = +Z (front)."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * (i + phase) / n
        f = shape(a) if shape else 1.0
        if rnd is not None and jitter > 0: f *= 1.0 + rnd.range(-jitter, jitter)
        pts.append((cx + math.cos(a) * rx * f, y, cz + math.sin(a) * rz * f))
    return pts


def ring_at(center, direction, ra, rb, n, up=(0.0, 0.0, 1.0), phase=0.5, jitter=0.0, rnd=None):
    """Ring around `direction` through `center`: radius ra along the side axis, rb along the `up`-ish axis."""
    d = _v_norm(direction)
    side = _v_cross(d, up)
    if abs(side[0]) + abs(side[1]) + abs(side[2]) < 1e-6: side = _v_cross(d, (1.0, 0.0, 0.0))
    side = _v_norm(side)
    fwd = _v_norm(_v_cross(side, d))
    pts = []
    for i in range(n):
        a = 2 * math.pi * (i + phase) / n
        f = 1.0 + (rnd.range(-jitter, jitter) if rnd is not None and jitter > 0 else 0.0)
        pts.append(_v_add(center, _v_add(_v_mul(side, math.cos(a) * ra * f), _v_mul(fwd, math.sin(a) * rb * f))))
    return pts


def loft(p, rings, cols, cap_start=True, cap_end=True):
    """Joins consecutive rings (same point count) with triangles, faces pointing away from the ring centres.
    cols: one colour or a list per segment."""
    if isinstance(cols, int): cols = [cols] * (len(rings) - 1)
    centres = [tuple(sum(q[k] for q in r) / len(r) for k in range(3)) for r in rings]
    n = len(rings[0])
    for s in range(len(rings) - 1):
        a, b = rings[s], rings[s + 1]
        mid = _v_mul(_v_add(centres[s], centres[s + 1]), 0.5)
        for i in range(n):
            j = (i + 1) % n
            p.face_out([a[i], a[j], b[j]], cols[s], mid)
            p.face_out([a[i], b[j], b[i]], cols[s], mid)
    if cap_start: p.face_out(rings[0], cols[0], centres[1])
    if cap_end: p.face_out(rings[-1], cols[-1], centres[-2])


def limb(p, path, n, col, up=(0.0, 0.0, 1.0), cap_start=True, cap_end=True, jitter=0.03, seed=1):
    """Tube along path [(point, ra, rb), ...] with rings perpendicular to the local direction."""
    rnd = Rand(seed)
    rings = []
    for k, (pt, ra, rb) in enumerate(path):
        prev = path[max(0, k - 1)][0]
        nxt = path[min(len(path) - 1, k + 1)][0]
        rings.append(ring_at(pt, _v_sub(nxt, prev), ra, rb, n, up, jitter=jitter if 0 < k < len(path) - 1 else 0.0, rnd=rnd))
    loft(p, rings, col, cap_start, cap_end)


def hand(p, wrist, direction, palm_normal, size=1.0, curl=0.5, thumb=1.0, fingers=True):
    """Stylised hand: palm block + four fingers + thumb. direction = wrist -> knuckles, palm_normal = the way the palm
    faces (fingers curl towards it), thumb = +1 / -1 picks the side of the thumb (cross(direction, palm_normal))."""
    d = _v_norm(direction)
    nrm = _v_norm(palm_normal)
    side = _v_norm(_v_cross(d, nrm))
    s = size
    palm_end = _v_add(wrist, _v_mul(d, 0.075 * s))
    limb(p, [(wrist, 0.032 * s, 0.016 * s), (_v_add(wrist, _v_mul(d, 0.04 * s)), 0.042 * s, 0.018 * s), (palm_end, 0.04 * s, 0.015 * s)],
         6, C["skin"], up=nrm, jitter=0.0)
    if not fingers:
        return palm_end, side
    for k, off in enumerate((-0.027, -0.009, 0.009, 0.027)):
        base = _v_add(palm_end, _v_mul(side, off * s))
        length = (0.052, 0.06, 0.058, 0.046)[k] * s
        mid = _v_add(_v_add(base, _v_mul(d, length * (0.5 - 0.1 * curl))), _v_mul(nrm, length * 0.3 * curl))
        tip = _v_add(_v_add(base, _v_mul(d, length * (1.0 - 0.75 * curl))), _v_mul(nrm, length * 0.75 * curl))
        limb(p, [(base, 0.0085 * s, 0.0075 * s), (mid, 0.0082 * s, 0.0072 * s), (tip, 0.006 * s, 0.0055 * s)], 5, C["skin"],
             up=nrm, jitter=0.0)
    ts = _v_mul(side, thumb)
    tb = _v_add(_v_add(wrist, _v_mul(d, 0.03 * s)), _v_mul(ts, 0.034 * s))
    tt = _v_add(_v_add(tb, _v_mul(d, 0.03 * s + 0.01 * curl * s)), _v_add(_v_mul(ts, 0.012 * s), _v_mul(nrm, 0.022 * s * (0.4 + curl))))
    limb(p, [(tb, 0.011 * s, 0.009 * s), (tt, 0.008 * s, 0.007 * s)], 5, C["skin_dk"], up=nrm, jitter=0.0)
    return palm_end, side


def fist_around(p, knuckles, centre, axis, palm_side, front, handle_r, finger_r=0.0082, wrap_deg=200.0):
    """Fingers of a fist wrapped around a cylinder (a handle): each finger starts at its knuckle and follows an arc
    around the cylinder axis just outside its surface, towards `front` first, so nothing passes through the handle.
    knuckles: [(knuckle point, offset along the axis)], palm_side: direction from the axis to the palm."""
    a = _v_norm(axis)
    def perp(v):
        d = v[0] * a[0] + v[1] * a[1] + v[2] * a[2]
        return _v_norm(_v_sub(v, _v_mul(a, d)))
    u = perp(palm_side)
    v = _v_norm(_v_cross(a, u))
    if v[0] * front[0] + v[1] * front[1] + v[2] * front[2] < 0: v = _v_mul(v, -1.0)
    radius = handle_r + finger_r + 0.0025
    for knuckle, off in knuckles:
        c = _v_add(centre, _v_mul(a, off))
        w = _v_sub(knuckle, c)
        theta0 = math.atan2(w[0] * v[0] + w[1] * v[1] + w[2] * v[2], w[0] * u[0] + w[1] * u[1] + w[2] * u[2])
        path = [(knuckle, finger_r * 1.05, finger_r * 0.95)]
        steps = 4
        for k in range(1, steps + 1):
            th = theta0 + math.radians(wrap_deg) * k / steps
            r = radius * (1.0 - 0.04 * k / steps)
            pt = _v_add(c, _v_add(_v_mul(u, math.cos(th) * r), _v_mul(v, math.sin(th) * r)))
            fr = finger_r * (1.0 - 0.25 * k / steps)
            path.append((pt, fr, fr * 0.9))
        limb(p, path, 5, C["skin"], up=a, jitter=0.0)


def detective_body():
    p = Part()
    # Legs: long and slim (trousers), knees slightly forward.
    for sx in (-1, 1):
        x = sx * 0.088
        limb(p, [((x, 0.93, 0.0), 0.078, 0.082), ((x * 1.02, 0.72, 0.012), 0.066, 0.07), ((x * 1.03, 0.5, 0.022), 0.054, 0.058),
                 ((x, 0.3, 0.005), 0.054, 0.058), ((x, 0.12, -0.006), 0.045, 0.048), ((x, 0.04, 0.0), 0.04, 0.043)], 8,
             C["charcoal"], seed=3 + sx)
        # Shoe: lofted along z, heel to toe.
        shoe = []
        for z, w, h, y0 in ((-0.055, 0.042, 0.065, 0.0), (0.0, 0.05, 0.075, 0.0), (0.07, 0.052, 0.05, 0.0), (0.135, 0.045, 0.036, 0.0),
                            (0.17, 0.03, 0.024, 0.002)):
            shoe.append([(x + math.cos(a) * w, y0 + h * 0.5 + math.sin(a) * h * 0.5, z - 0.01)
                         for a in [2 * math.pi * (i + 0.5) / 8 for i in range(8)]])
        loft(p, shoe, C["black"])
    # Coat skirt: flared, slightly longer at the back, open at the front below the belt.
    rnd = Rand(21)
    def skirt_shape(a):
        return 1.0 + 0.06 * math.sin(a)   # a touch fuller at the front
    skirt = [ring_y(y, rx, rz, 14, cz=-0.01, shape=skirt_shape, jitter=j, rnd=rnd)
             for y, rx, rz, j in ((0.99, 0.19, 0.13, 0.0), (0.82, 0.215, 0.155, 0.025), (0.62, 0.245, 0.18, 0.03), (0.44, 0.27, 0.2, 0.0))]
    skirt[-1] = [(q[0], q[1] - 0.02 * max(0.0, -math.sin(2 * math.pi * (i + 0.5) / 14)), q[2]) for i, q in enumerate(skirt[-1])]
    loft(p, skirt, C["coat"], cap_start=False, cap_end=True)
    p.face_out([(0.0, 0.99, 0.125), (0.012, 0.44, 0.205), (-0.012, 0.44, 0.205)], C["coat_dk"], (0.0, 0.7, 0.0))   # front opening line
    # Torso: waist -> chest -> shoulders, collapsing into the neck.
    def chest_shape(a):
        return 1.0 + 0.05 * max(0.0, math.sin(a))
    torso = [ring_y(y, rx, rz, 14, cz=cz, shape=chest_shape, jitter=j, rnd=rnd)
             for y, rx, rz, cz, j in ((0.99, 0.19, 0.13, -0.01, 0.0), (1.12, 0.2, 0.135, 0.0, 0.02), (1.3, 0.215, 0.145, 0.005, 0.02),
                                      (1.42, 0.218, 0.13, 0.0, 0.015), (1.475, 0.17, 0.105, -0.005, 0.0), (1.525, 0.075, 0.07, 0.0, 0.0))]
    loft(p, torso, [C["coat"], C["coat"], C["coat"], C["coat"], C["coat_dk"]], cap_start=False, cap_end=False)
    # Belt with buckle.
    loft(p, [ring_y(0.965, 0.196, 0.136, 14, cz=-0.01, shape=skirt_shape), ring_y(1.015, 0.196, 0.136, 14, cz=-0.01, shape=skirt_shape)],
         C["walnut_dk"], cap_start=False, cap_end=False)
    p.cbb(0.0, 0.968, 0.137, 0.045, 0.044, 0.012, C["brass"], b=0.004)
    # Open V at the chest: shirt, red tie, lapels.
    zc = 0.158
    p.face_out([(-0.065, 1.47, zc - 0.03), (0.065, 1.47, zc - 0.03), (0.0, 1.24, zc + 0.004)], C["white"], (0.0, 1.38, 0.0))
    p.face_out([(-0.016, 1.455, zc - 0.022), (0.016, 1.455, zc - 0.022), (0.0, 1.425, zc - 0.016)], C["red"], (0.0, 1.44, 0.0))     # knot
    p.face_out([(0.0, 1.43, zc - 0.014), (0.02, 1.33, zc + 0.002), (0.0, 1.25, zc + 0.008), (-0.02, 1.33, zc + 0.002)], C["red"], (0.0, 1.34, 0.0))
    for sx in (-1, 1):
        p.face_out([(sx * 0.075, 1.48, zc - 0.035), (sx * 0.0, 1.24, zc + 0.012), (sx * 0.12, 1.36, zc + 0.006), (sx * 0.13, 1.45, zc - 0.01)],
                   C["coat_dk"], (sx * 0.05, 1.36, 0.0))                                                            # lapel
        for y in (1.07, 1.17):
            p.cbb(sx * 0.075, y, 0.142, 0.018, 0.018, 0.01, C["walnut_dk"], b=0.003)                                  # buttons
        p.face_out([(sx * 0.11, 1.2, 0.149), (sx * 0.17, 1.2, 0.137), (sx * 0.17, 1.215, 0.137), (sx * 0.11, 1.215, 0.149)],
                   C["coat_dk"], (sx * 0.14, 1.2, 0.0))                                                              # pocket flaps
    # Raised collar around the neck (open at the front).
    col_rings = []
    for y, r in ((1.49, 0.098), (1.545, 0.086), (1.575, 0.092)):
        col_rings.append([(math.cos(a) * r, y, math.sin(a) * r * 0.95 - 0.005) for a in
                          [math.radians(110 + 320 * i / 12.0) for i in range(13)]])
    for s in range(2):
        for i in range(12):
            a, b = col_rings[s], col_rings[s + 1]
            p.face_out([a[i], a[i + 1], b[i + 1]], C["coat_dk"], (0.0, 1.54, 0.0))
            p.face_out([a[i], b[i + 1], b[i]], C["coat_dk"], (0.0, 1.54, 0.0))
            p.face_out([a[i], b[i + 1], a[i + 1]], C["coat_dk"], (0.0, 1.54, 0.4))   # inside of the collar
            p.face_out([a[i], b[i], b[i + 1]], C["coat_dk"], (0.0, 1.54, 0.4))
    limb(p, [((0.0, 1.5, 0.0), 0.052, 0.05), ((0.0, 1.6, 0.012), 0.047, 0.047)], 8, C["skin_dk"], jitter=0.0)      # neck
    return p


def detective_head():
    """Head with the neck pivot at the origin: long faceted head, jaw and chin, wedge nose, line eyes, brows, ears,
    hair at the sides and back, fedora with a curved brim and pinched crown."""
    p = Part()
    rnd = Rand(41)
    def face_shape(y):
        def f(a):
            front = max(0.0, math.sin(a))
            side = abs(math.cos(a))
            return 1.0 - 0.1 * side * (1.0 if y < 0.08 else 0.0) + 0.04 * front
        return f
    rings = []
    for y, rx, rz, cz in ((0.0, 0.03, 0.03, 0.075), (0.02, 0.055, 0.05, 0.06), (0.05, 0.078, 0.078, 0.035), (0.09, 0.088, 0.096, 0.022),
                          (0.135, 0.092, 0.1, 0.02), (0.18, 0.09, 0.098, 0.015), (0.22, 0.078, 0.086, 0.01), (0.25, 0.05, 0.055, 0.008),
                          (0.265, 0.02, 0.022, 0.008)):
        rings.append(ring_y(y, rx, rz, 12, cz=cz, shape=face_shape(y), jitter=0.02 if 0.03 < y < 0.24 else 0.0, rnd=rnd))
    loft(p, rings, C["skin"])
    limb(p, [((0.0, -0.045, -0.012), 0.05, 0.048), ((0.0, 0.03, 0.0), 0.05, 0.05), ((0.0, 0.1, 0.01), 0.045, 0.045)], 8,
         C["skin_dk"], jitter=0.0)                                                                                  # neck into the skull
    # Nose: a wedge down the middle of the face.
    zf = 0.118
    p.face_out([(0.0, 0.16, zf - 0.006), (0.0, 0.105, zf + 0.035), (0.018, 0.098, zf + 0.004)], C["skin_dk"], (0.03, 0.12, 0.0))
    p.face_out([(0.0, 0.16, zf - 0.006), (-0.018, 0.098, zf + 0.004), (0.0, 0.105, zf + 0.035)], C["skin_dk"], (-0.03, 0.12, 0.0))
    p.face_out([(0.0, 0.105, zf + 0.035), (-0.018, 0.098, zf + 0.004), (0.018, 0.098, zf + 0.004)], C["skin_dk"], (0.0, 0.14, 0.0))
    # Line eyes and slanted brows (the reference's style), mouth.
    for sx in (-1, 1):
        p.face_out([(sx * 0.018, 0.168, zf - 0.006), (sx * 0.058, 0.172, zf - 0.012), (sx * 0.058, 0.163, zf - 0.012),
                    (sx * 0.018, 0.16, zf - 0.006)], C["hat_dk"], (sx * 0.03, 0.16, 0.0))
        # Ear grows out of the skull: the first ring sits inside the head, so there is never a gap at the join.
        ear = [ring_at((sx * 0.075, 0.13, 0.026), (1.0, 0.0, 0.0), 0.016, 0.024, 6, up=(0.0, 1.0, 0.0)),
               ring_at((sx * 0.098, 0.13, 0.03), (1.0, 0.0, 0.0), 0.018, 0.028, 6, up=(0.0, 1.0, 0.0)),
               ring_at((sx * 0.107, 0.131, 0.028), (1.0, 0.0, 0.0), 0.012, 0.022, 6, up=(0.0, 1.0, 0.0))]
        loft(p, ear, C["skin_dk"])
    # Hair: dark band at the sides and back under the hat.
    hair = []
    for y, r in ((0.12, 0.094), (0.2, 0.098), (0.235, 0.09)):
        hair.append([(math.cos(a) * r * 1.02, y, math.sin(a) * r * 1.08 + 0.012) for a in
                     [math.radians(200 + 140 * i / 8.0) for i in range(9)]])
    for s in range(2):
        for i in range(8):
            a, b = hair[s], hair[s + 1]
            p.face_out([a[i], a[i + 1], b[i + 1]], C["hat_dk"], (0.0, 0.17, 0.0))
            p.face_out([a[i], b[i + 1], b[i]], C["hat_dk"], (0.0, 0.17, 0.0))
    # Fedora: brim curving down at the front and back, band, pinched crown with a dent on top.
    def brim_y(a):
        return 0.228 - 0.022 * abs(math.sin(a))
    inner = [(math.cos(a) * 0.098, 0.23, math.sin(a) * 0.108 + 0.01) for a in [2 * math.pi * (i + 0.5) / 16 for i in range(16)]]
    outer = [(math.cos(a) * 0.19, brim_y(a), math.sin(a) * 0.2 + 0.012) for a in [2 * math.pi * (i + 0.5) / 16 for i in range(16)]]
    outer_lo = [(q[0], q[1] - 0.008, q[2]) for q in outer]
    inner_lo = [(q[0], q[1] - 0.008, q[2]) for q in inner]
    for i in range(16):
        j = (i + 1) % 16
        p.face_out([inner[i], outer[i], outer[j]], C["hat"], (0.0, 0.1, 0.01))
        p.face_out([inner[i], outer[j], inner[j]], C["hat"], (0.0, 0.1, 0.01))
        p.face_out([inner_lo[i], outer_lo[j], outer_lo[i]], C["hat_dk"], (0.0, 0.4, 0.01))
        p.face_out([inner_lo[i], inner_lo[j], outer_lo[j]], C["hat_dk"], (0.0, 0.4, 0.01))
        p.face_out([outer[i], outer_lo[i], outer_lo[j], outer[j]], C["hat_dk"], (0.0, 0.2, 0.01))
    def pinch(a):
        return 1.0 - 0.12 * max(0.0, math.sin(a)) ** 2
    crown = [ring_y(0.226, 0.1, 0.11, 12, cz=0.01), ring_y(0.258, 0.1, 0.108, 12, cz=0.01),
             ring_y(0.262, 0.098, 0.106, 12, cz=0.01), ring_y(0.33, 0.088, 0.096, 12, cz=0.008, shape=pinch),
             ring_y(0.345, 0.07, 0.078, 12, cz=0.006, shape=pinch), ring_y(0.335, 0.03, 0.05, 12, cz=0.004)]
    loft(p, crown, [C["hat_dk"], C["hat"], C["hat"], C["hat"], C["hat_dk"]], cap_start=True, cap_end=True)
    # Inside of the crown seen from below (the cap above faces down only from outside): a dark lining facing down.
    lining = ring_y(0.229, 0.098, 0.108, 12, cz=0.01)
    p.face_out(lining, C["hat_dk"], (0.0, 0.5, 0.01))
    return p


def translated(part, offset):
    q = Part()
    for verts, col in part.faces:
        q.face([_v_add(v, offset) for v in verts], col)
    return q


def partner_head():
    """Team partner marker (PartnerMarker): a neutral faceted head - skull, jaw, wedge nose, ears; no face details, hair
    or hat. Origin = between the eyes (the headset's centre eye), front +Z, so Unity places it straight at the partner's
    head pose."""
    p = Part()
    rnd = Rand(43)

    def face_shape(y):
        def f(a):
            front = max(0.0, math.sin(a))
            side = abs(math.cos(a))
            return 1.0 - 0.1 * side * (1.0 if y < 0.08 else 0.0) + 0.04 * front
        return f
    rings = []
    for y, rx, rz, cz in ((0.0, 0.03, 0.03, 0.06), (0.02, 0.055, 0.05, 0.05), (0.05, 0.078, 0.078, 0.03), (0.09, 0.088, 0.096, 0.02),
                          (0.135, 0.092, 0.1, 0.018), (0.18, 0.09, 0.098, 0.012), (0.22, 0.078, 0.086, 0.006), (0.25, 0.05, 0.055, 0.004),
                          (0.265, 0.02, 0.022, 0.004)):
        rings.append(ring_y(y, rx, rz, 12, cz=cz, shape=face_shape(y), jitter=0.02 if 0.03 < y < 0.24 else 0.0, rnd=rnd))
    loft(p, rings, C["skin"])
    zf = 0.118
    p.face_out([(0.0, 0.16, zf - 0.006), (0.0, 0.105, zf + 0.035), (0.018, 0.098, zf + 0.004)], C["skin_dk"], (0.03, 0.12, 0.0))
    p.face_out([(0.0, 0.16, zf - 0.006), (-0.018, 0.098, zf + 0.004), (0.0, 0.105, zf + 0.035)], C["skin_dk"], (-0.03, 0.12, 0.0))
    p.face_out([(0.0, 0.105, zf + 0.035), (-0.018, 0.098, zf + 0.004), (0.018, 0.098, zf + 0.004)], C["skin_dk"], (0.0, 0.14, 0.0))
    for sx in (-1, 1):
        ear = [ring_at((sx * 0.075, 0.13, 0.026), (1.0, 0.0, 0.0), 0.016, 0.024, 6, up=(0.0, 1.0, 0.0)),
               ring_at((sx * 0.098, 0.13, 0.03), (1.0, 0.0, 0.0), 0.018, 0.028, 6, up=(0.0, 1.0, 0.0)),
               ring_at((sx * 0.107, 0.131, 0.028), (1.0, 0.0, 0.0), 0.012, 0.022, 6, up=(0.0, 1.0, 0.0))]
        loft(p, ear, C["skin_dk"])
    return translated(p, (0.0, -0.165, -0.095))   # eyes (y 0.165, ~2 cm behind the face) -> origin


def detective_arm_left(piece="all"):
    """Left arm in shoulder space (pivot DETECTIVE_ARM_L_AT): hanging, slightly bent, hand relaxed; it gestures.
    piece: "upper" (shoulder to a rounded elbow), "fore" (elbow to hand, translated so the elbow
    DETECTIVE_ELBOW_L_AT is the origin) or "all"."""
    elbow, wrist = DETECTIVE_ELBOW_L_AT, (-0.035, -0.535, 0.025)
    upper, fore = Part(), Part()
    limb(upper, [((0.012, 0.045, 0.0), 0.03, 0.03), ((0.004, 0.022, 0.0), 0.06, 0.058), ((0.0, 0.0, 0.0), 0.068, 0.066),
                 ((-0.02, -0.14, -0.012), 0.062, 0.06), (elbow, 0.056, 0.055), (_v_add(elbow, (0.0, -0.035, 0.002)), 0.035, 0.035)],
         8, C["coat"], up=(1.0, 0.0, 0.0), seed=31)
    limb(fore, [(_v_add(elbow, (0.0, 0.02, 0.0)), 0.052, 0.051), ((-0.038, -0.405, 0.0), 0.052, 0.05),
                (_v_add(wrist, (0.0, 0.025, 0.0)), 0.05, 0.048)], 8, C["coat"], up=(1.0, 0.0, 0.0), seed=32)
    limb(fore, [(_v_add(wrist, (0.0, 0.03, 0.0)), 0.052, 0.05), (_v_add(wrist, (0.0, 0.005, 0.0)), 0.052, 0.05)], 8, C["coat_dk"],
         up=(1.0, 0.0, 0.0), jitter=0.0)                                                                             # cuff
    hand(fore, _v_add(wrist, (0.0, 0.01, 0.0)), (0.02, -1.0, 0.05), (1.0, 0.0, 0.0), curl=0.4, thumb=1.0)
    if piece == "upper": return upper
    if piece == "fore": return translated(fore, _v_mul(elbow, -1.0))
    upper.faces.extend(fore.faces)
    return upper


def detective_eyes():
    """The two line eyes, pivot between them (DETECTIVE_EYES_AT): Unity squashes this part to blink."""
    p = Part()
    for sx in (-1, 1):
        p.bbox(sx * 0.034 - 0.003, -0.012, -0.0015, sx * 0.034 + 0.003, 0.012, 0.0015, C["eye"])
    return p


def detective_mouth():
    """Mouth opening with lips, pivot at its centre (DETECTIVE_MOUTH_AT): Unity opens it (scale y) with the voice."""
    p = Part()
    p.face_out([(-0.017, 0.0, 0.0), (-0.008, 0.006, 0.0), (0.008, 0.006, 0.0), (0.017, 0.0, 0.0), (0.008, -0.007, 0.0),
                (-0.008, -0.007, 0.0)], C["eye"], (0.0, 0.0, -0.01))                                               # opening
    p.face_out([(-0.019, 0.001, 0.0012), (-0.008, 0.008, 0.0012), (0.008, 0.008, 0.0012), (0.019, 0.001, 0.0012),
                (0.008, 0.004, 0.0012), (-0.008, 0.004, 0.0012)], C["lip"], (0.0, 0.0, -0.01))                    # upper lip
    p.face_out([(-0.019, 0.0, 0.0012), (-0.008, -0.004, 0.0012), (0.008, -0.004, 0.0012), (0.019, 0.0, 0.0012),
                (0.008, -0.010, 0.0012), (-0.008, -0.010, 0.0012)], C["lip"], (0.0, 0.0, -0.01))                  # lower lip
    return p


def detective_moustache():
    """A curled moustache, pivot at its centre (it bobs with the voice)."""
    p = Part()
    for sx in (-1, 1):
        limb(p, [((sx * 0.002, 0.004, 0.0), 0.008, 0.006), ((sx * 0.022, 0.002, -0.004), 0.009, 0.007),
                 ((sx * 0.042, -0.006, -0.012), 0.006, 0.005), ((sx * 0.052, 0.002, -0.016), 0.003, 0.003)],
             6, C["hat_dk"], up=(0.0, 0.0, 1.0), jitter=0.0)
    return p


POINT_FINGER_DOWN = 35.0   # degrees below the forearm direction; = 90 - DetectiveBriefing.PointElbowDegrees (keep in sync)


def detective_arm(piece="all", point=False):
    """Right arm in shoulder space (pivot DETECTIVE_ARM_AT): upper arm down, forearm forward, hand holding a
    magnifying glass up. piece: "upper", "fore" (elbow DETECTIVE_ELBOW_R_AT = origin) or "all". point=True: the index finger
    sticks out (towards the display in the Case X briefing; angled POINT_FINGER_DOWN below the forearm so that it lines up
    with the upper arm when the elbow is at the pointing angle) while the other fingers keep holding the handle."""
    elbow, wrist = DETECTIVE_ELBOW_R_AT, (0.01, -0.29, 0.24)
    upper = Part()
    limb(upper, [((-0.012, 0.045, 0.0), 0.03, 0.03), ((-0.004, 0.022, 0.0), 0.06, 0.058), ((0.0, 0.0, 0.0), 0.068, 0.066),
                 ((0.02, -0.14, -0.006), 0.062, 0.06), (elbow, 0.057, 0.056), (_v_add(elbow, (0.003, -0.03, -0.012)), 0.036, 0.036)],
         8, C["coat"], up=(1.0, 0.0, 0.0), seed=51)
    p = Part()   # forearm, hand, magnifier
    limb(p, [(_v_add(elbow, (0.0, 0.004, -0.018)), 0.054, 0.053), ((0.024, -0.29, 0.11), 0.052, 0.05),
             (_v_add(wrist, (0.0, 0.0, -0.025)), 0.05, 0.048)], 8, C["coat"], up=(1.0, 0.0, 0.0), seed=52)
    limb(p, [(_v_add(wrist, (0.0, 0.0, -0.03)), 0.052, 0.05), (_v_add(wrist, (0.0, 0.0, -0.005)), 0.052, 0.05)], 8, C["coat_dk"],
         up=(1.0, 0.0, 0.0), jitter=0.0)                                                                             # cuff
    hdir = _v_norm((0.0, 0.25, 1.0))
    palm_end, side = hand(p, wrist, hdir, (-1.0, 0.0, 0.0), fingers=False)
    # Magnifying glass: the handle runs through the fist (palm side), the ring sits on the handle's top end.
    up = _v_norm((0.0, 1.0, 0.28))
    grip = _v_add(_v_add(wrist, _v_mul(hdir, 0.062)), (-0.024, 0.0, 0.0))
    # Four fingers wrap around the handle (index on top), the thumb over the front just above them.
    knuckles = []
    for off in (0.027, 0.009, -0.009, -0.027):
        k = _v_add(palm_end, _v_mul(up, off))
        if point and off == 0.027:
            a = math.radians(POINT_FINGER_DOWN)
            fdir = (0.0, -math.sin(a), math.cos(a))
            limb(p, [(k, 0.0085, 0.0085), (_v_add(k, _v_mul(fdir, 0.04)), 0.0082, 0.008), (_v_add(k, _v_mul(fdir, 0.085)), 0.0055, 0.0055)],
                 5, C["skin"], jitter=0.0)
            continue
        knuckles.append((k, off + 0.003))
    fist_around(p, knuckles, grip, up, (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), 0.011)
    thumb_base = _v_add(_v_add(wrist, _v_mul(hdir, 0.035)), _v_mul(up, 0.036))
    fist_around(p, [(thumb_base, 0.04)], grip, up, (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), 0.011, finger_r=0.0095, wrap_deg=120.0)
    bottom = _v_add(grip, _v_mul(up, -0.045))
    top = _v_add(grip, _v_mul(up, 0.11))
    limb(p, [(bottom, 0.0105, 0.0105), (grip, 0.011, 0.011), (top, 0.0085, 0.0085)], 6, C["walnut_dk"], jitter=0.0)
    limb(p, [(_v_add(top, _v_mul(up, -0.012)), 0.011, 0.011), (_v_add(top, _v_mul(up, 0.006)), 0.011, 0.011)], 6, C["brass"],
         jitter=0.0)                                                                                                 # ferrule
    axis = _v_norm((0.0, -0.28, 1.0))          # the lens faces forward, perpendicular to the handle
    radius = 0.064
    ring_c = _v_add(top, _v_mul(up, radius + 0.002))
    outer = ring_at(ring_c, axis, radius, radius, 16, up=(1.0, 0.0, 0.0))
    inner = ring_at(ring_c, axis, radius - 0.012, radius - 0.012, 16, up=(1.0, 0.0, 0.0))
    d = _v_mul(axis, 0.007)
    for i in range(16):
        j = (i + 1) % 16
        for sgn in (1, -1):
            o1, o2, i1, i2 = (_v_add(q, _v_mul(d, sgn)) for q in (outer[i], outer[j], inner[i], inner[j]))
            p.face_out([o1, o2, i2, i1], C["brass"], _v_sub(ring_c, _v_mul(d, sgn * 4)))
        p.face_out([_v_add(outer[i], d), _v_sub(outer[i], d), _v_sub(outer[j], d), _v_add(outer[j], d)], C["brass"], ring_c)
        p.face_out([_v_add(inner[i], d), _v_add(inner[j], d), _v_sub(inner[j], d), _v_sub(inner[i], d)], C["brass"],
                   _v_add(ring_c, _v_mul(_v_sub(inner[i], ring_c), 3.0)))
    p.face_out(inner, C["glass"], _v_sub(ring_c, d))
    p.face_out(inner, C["glass"], _v_add(ring_c, d))
    if piece == "upper": return upper
    if piece == "fore": return translated(p, _v_mul(elbow, -1.0))
    upper.faces.extend(p.faces)
    return upper


def door(w, studio):
    """Door frame (thin, 2.0-2.1 m) with the door leaf ajar towards the outside (-Z)."""
    p = Part()
    t, d, h = 0.07, 0.10, 2.0
    frame = C["grey_lt"] if studio else C["walnut"]
    frame_dk = C["grey"] if studio else C["walnut_dk"]
    for sx in (-1, 1):
        p.cbb(sx * (w / 2 - t / 2), 0, 0, t, h, d, frame, b=0.01, edge_col=frame_dk)
        p.cbb(sx * (w / 2 - t / 2), 0, 0, t + 0.02, 0.12, d + 0.02, frame_dk, b=0.006)  # plinth blocks
    p.cbb(0, h - t, 0, w, t, d, frame, b=0.01, edge_col=frame_dk)
    if studio:
        p.cbb(0, h - t - 0.02, d / 2 + 0.002, w - 2 * t, 0.012, 0.006, C["teal"])
        leaf_col, leaf_dk, handle = C["white"], C["white_dk"], C["chrome"]
    else:
        p.cbb(0, h, 0, w + 0.08, 0.10, d + 0.03, C["walnut_dk"], b=0.012, edge_col=C["wood_dk"])  # carved lintel
        batik_band(p, -w / 2 + 0.02, w / 2 - 0.02, h + 0.02, h + 0.08, d / 2 + 0.015, C["indigo"], C["ochre"], C["batik_lt"], count=6)
        leaf_col, leaf_dk, handle = C["wood"], C["wood_dk"], C["brass"]
    p.cbb(0, 0, 0, w - 2 * t, 0.012, d, C["charcoal"], b=0.003)                    # threshold
    # Door leaf: hinged at the -X jamb, opened 65 degrees outwards (-Z).
    leaf = Part()
    lw, lh = w - 2 * t - 0.01, h - t - 0.02
    leaf.cbb(lw / 2, 0.01, 0, lw, lh, 0.04, leaf_col, b=0.008, edge_col=leaf_dk)
    for yy, hh in ((0.18, 0.7), (1.02, 0.78)):                                     # raised panels (both sides)
        for side in (-1, 1):
            leaf.cbb(lw / 2, yy, side * 0.021, lw - 0.16, hh, 0.006, leaf_dk if not studio else C["white_dk"], b=0.004)
    if studio:
        leaf.cbb(lw / 2, 1.0, 0.024, lw - 0.1, 0.03, 0.004, C["teal"])
    for side in (-1, 1):                                                           # lever handles
        leaf.cbb(lw - 0.07, 0.98, side * 0.032, 0.02, 0.06, 0.02, handle, b=0.004)
        leaf.cbb(lw - 0.11, 1.02, side * 0.045, 0.1, 0.018, 0.018, handle, b=0.004)
    hinge_x = -w / 2 + t
    for verts, c in leaf.faces:
        p.face([(hinge_x + x2, y2, z2) for x2, y2, z2 in rot_y(verts, 65)], c)
    return p


# ---------------------------------------------------------------------------------------------------------------
# Clues. Grabbable: fill the collider box (sx, sy, sz) from the bottom centre. Static: centred, readable towards -Z.
# ---------------------------------------------------------------------------------------------------------------
def text_lines(p, x0, x1, z_top, z_step, y, count, col, seed, thick=0.006, z_sign=-1):
    rnd = Rand(seed)
    for i in range(count):
        length = (x1 - x0) * rnd.range(0.5, 0.95)
        z = z_top + z_sign * i * z_step
        p.bbox(x0, y, z - thick / 2, x0 + length, y + 0.0008, z + thick / 2, col)


def paper_sheet(p, sx, sy, sz, col=C["paper"], col_dk=C["paper_dk"]):
    """Sheet with a folded corner, filling the box. Returns the top height."""
    t = sy * 0.6
    f = min(sx, sz) * 0.16
    outline = [(-sx / 2, -sz / 2), (sx / 2, -sz / 2), (sx / 2, sz / 2 - f), (sx / 2 - f, sz / 2), (-sx / 2, sz / 2)]
    p.prism(outline, 0, t, col)
    p.face_out([(sx / 2, t, sz / 2 - f), (sx / 2 - f, t, sz / 2), (sx / 2 - f, sy, sz / 2 - f)], col_dk, (sx / 2 - f, 0, sz / 2 - f))
    p.face_out([(sx / 2, t, sz / 2 - f), (sx / 2 - f, t, sz / 2), (sx / 2 - f, sy, sz / 2 - f)], col_dk, (sx, sy * 3, sz))
    return t


def printout(sx, sy, sz):
    p = Part()
    t = paper_sheet(p, sx, sy, sz)
    p.bbox(-sx / 2 + 0.015, t, sz / 2 - 0.045, sx / 2 - 0.05, t + 0.0008, sz / 2 - 0.02, C["teal"])   # letterhead
    text_lines(p, -sx / 2 + 0.02, sx / 2 - 0.03, sz / 2 - 0.07, 0.022, t, 9, C["ink"], seed=4)
    p.bbox(-sx / 2 + 0.02, t, -sz / 2 + 0.03, -sx / 2 + 0.09, t + 0.0008, -sz / 2 + 0.05, C["blue"])  # signature
    return p


def chat_printout(sx, sy, sz):
    p = Part()
    t = paper_sheet(p, sx, sy, sz)
    p.bbox(-sx / 2 + 0.015, t, sz / 2 - 0.04, sx / 2 - 0.05, t + 0.0008, sz / 2 - 0.015, C["green_dk"])
    for i, (x, w, col) in enumerate([(-0.035, 0.12, "grey_lt"), (0.035, 0.11, "green"), (-0.04, 0.10, "grey_lt"),
                                     (0.04, 0.12, "green"), (-0.03, 0.08, "grey_lt")]):
        z = sz / 2 - 0.07 - i * 0.045
        p.cbb(x, t, z, w, 0.0008, 0.03, C[col], b=0.0)
        p.bbox(x - w * 0.35, t + 0.0008, z - 0.003, x + w * 0.3, t + 0.0012, z + 0.003, C["ink"])
    return p


def clipboard_sheet(sx, sy, sz):
    p = Part()
    p.cbb(0, 0, 0, sx, sy * 0.35, sz, C["wood_dk"], b=0.004, edge_col=C["walnut"])  # board
    p.cbb(0, sy * 0.35, -0.012, sx - 0.03, sy * 0.15, sz - 0.05, C["paper"])
    y = sy * 0.5
    p.bbox(-sx / 2 + 0.03, y, sz / 2 - 0.085, sx / 2 - 0.03, y + 0.0008, sz / 2 - 0.075, C["ink"])     # table head
    for i in range(7):
        z = sz / 2 - 0.1 - i * 0.03
        p.bbox(-sx / 2 + 0.03, y, z - 0.002, sx / 2 - 0.03, y + 0.0006, z + 0.002, C["pencil"])        # rows
        if i < 6:
            text_lines(p, -sx / 2 + 0.035, -0.01, z - 0.012, 0, y, 1, C["ink"], seed=20 + i, thick=0.005)
    z = sz / 2 - 0.1 - 6 * 0.03 - 0.015
    p.bbox(-sx / 2 + 0.03, y + 0.0006, z - 0.009, sx / 2 - 0.04, y + 0.0016, z + 0.009, C["blue_dk"])  # scribbled-out entry
    p.cbb(0, sy * 0.35, sz / 2 - 0.03, 0.09, sy * 0.4, 0.035, C["chrome"], b=0.003)                   # clip
    p.cbb(0, sy * 0.75, sz / 2 - 0.035, 0.05, sy * 0.25, 0.02, C["silver"], b=0.002)
    return p


def mirrored(part, axis):
    """Mirror image of a part across x = 0 (axis 0) or z = 0 (axis 2); winding reversed so faces still point out."""
    q = Part()
    for verts, col in part.faces:
        pts = [tuple(-c if k == axis else c for k, c in enumerate(v)) for v in verts]
        q.face(list(reversed(pts)), col)
    return q


def readable(part):
    """Paper-like clues are modelled with their top edge at +Z and text starting at -X. The reader stands on the clue's
    +Z side (FacingYaw). Unity's FBX import negates X (right- to left-handed) while keeping the look, so in these Maya
    coordinates the reader's left is -X (text already right) and the top must point away: mirror z only. (A 180 degree
    turn would put the text on the right, i.e. show the sheet mirrored.)"""
    return mirrored(part, 2)


def lanyard_clip(sx, sy, sz):
    p = Part()
    tag_x0 = -sx / 2 + 0.03
    p.bbox(tag_x0, 0, -sz / 2, sx / 2, sy * 0.35, sz / 2, C["white"], b=0.003, edge_col=C["white_dk"])  # staff tag
    p.bbox(tag_x0 + 0.006, sy * 0.35, -sz / 2 + 0.006, tag_x0 + 0.03, sy * 0.4, -sz / 2 + 0.036, C["skin_card"])  # photo
    p.bbox(tag_x0 + 0.036, sy * 0.35, sz / 2 - 0.018, sx / 2 - 0.006, sy * 0.38, sz / 2 - 0.006, C["teal"])     # stripe
    p.bbox(tag_x0 + 0.036, sy * 0.35, -0.012, sx / 2 - 0.012, sy * 0.37, -0.006, C["ink"])                       # name
    p.bbox(tag_x0 + 0.036, sy * 0.35, -0.002, sx / 2 - 0.025, sy * 0.37, 0.003, C["pencil"])                     # number
    p.bbox(-sx / 2, 0, -0.012, tag_x0 + 0.004, sy * 0.25, 0.012, C["teal_dk"], b=0.002)                          # strap stub
    p.frustum(-sx / 2 + 0.014, sy * 0.25, 0, 0.012, 0.011, sy * 0.5, C["chrome"], sides=8)                       # broken clip
    p.bbox(-sx / 2 + 0.008, sy * 0.6, 0.004, -sx / 2 + 0.03, sy, 0.012, C["silver"], b=0.002)
    return p


def glove(sx, sy, sz):
    p = Part()
    palm_l = sx * 0.48
    x0 = -sx / 2
    p.bbox(x0, 0, -sz / 2, x0 + 0.045, sy * 0.85, sz / 2, C["yellow"], b=0.008, edge_col=C["yellow_dk"])   # cuff
    p.bbox(x0 + 0.004, sy * 0.85, -sz / 2 + 0.004, x0 + 0.04, sy, sz / 2 - 0.004, C["orange"], b=0.004)     # rolled rim
    p.bbox(x0 + 0.04, 0, -sz * 0.44, x0 + palm_l, sy * 0.72, sz * 0.4, C["yellow"], b=0.01, edge_col=C["yellow_dk"])  # palm
    fl = sx - palm_l
    r = sy * 0.24
    for i in range(4):
        z = -sz * 0.36 + i * sz * 0.235
        length = fl * (0.82 if i in (0, 3) else 0.98)
        p.frustum(x0 + palm_l - 0.01, r, z, r, r * 0.85, length, C["yellow"] if i % 2 else C["yellow_dk"], sides=6, axis="x")
    # thumb, angled outwards
    th = Part()
    th.frustum(0, r, 0, r * 1.05, r * 0.9, palm_l * 0.55, C["yellow"], sides=6, axis="x")
    for verts, c in th.faces:                                                  # stays inside the collider box
        p.face([(x0 + palm_l * 0.45 + vx, vy, sz * 0.22 + vz) for vx, vy, vz in rot_y(verts, -22)], c)
    return p


def torch(sx, sy, sz):
    p = Part()
    r = sy / 2
    x0 = -sx / 2
    p.frustum(x0, r, 0, r * 0.7, r * 0.72, sx * 0.08, C["black"], sides=10, axis="x")                 # tail cap
    p.frustum(x0 + sx * 0.08, r, 0, r * 0.72, r * 0.74, sx * 0.5, C["yellow"], sides=10, axis="x")    # handle
    for k in range(4):                                                                                 # grip rings
        p.frustum(x0 + sx * (0.14 + 0.09 * k), r, 0, r * 0.77, r * 0.77, sx * 0.03, C["black"], sides=10, axis="x")
    p.frustum(x0 + sx * 0.58, r, 0, r * 0.74, r * 0.98, sx * 0.24, C["yellow_dk"], sides=10, axis="x")  # head flare
    p.frustum(x0 + sx * 0.82, r, 0, r * 0.98, r * 0.98, sx * 0.16, C["black"], sides=10, axis="x")      # bezel
    p.frustum(x0 + sx * 0.995, r, 0, r * 0.8, r * 0.8, sx * 0.005, C["lamp"], sides=10, axis="x")       # lens
    p.bbox(x0 + sx * 0.42, sy * 0.86, -0.006, x0 + sx * 0.52, sy * 0.94, 0.006, C["black"], b=0.002)    # switch
    p.bbox(x0 + sx * 0.18, sy * 0.86, -0.009, x0 + sx * 0.38, sy * 0.88, 0.009, C["ink"])               # SECURITY band
    return p


def torn_sketch(sx, sy, sz):
    p = Part()
    t = sy * 0.6
    outline = [(-sx / 2, -sz / 2), (sx / 2, -sz / 2), (sx / 2, sz * 0.18), (sx * 0.38, sz * 0.32), (sx * 0.2, sz * 0.22),
               (sx * 0.06, sz / 2), (-sx * 0.12, sz * 0.36), (-sx * 0.3, sz / 2), (-sx / 2, sz * 0.38)]
    p.prism(outline, 0, t, C["paper"])
    y = t
    p.bbox(-0.03, y, -0.04, 0.03, y + 0.0008, -0.028, C["pencil"])            # award: base
    p.bbox(-0.006, y, -0.028, 0.006, y + 0.0008, 0.01, C["pencil"])           # stem
    p.prism([(-0.03, 0.01), (0.03, 0.01), (0.022, 0.045), (-0.022, 0.045)], y, y + 0.0009, C["ochre"])  # cup
    for sxn in (-1, 1):
        p.bbox(sxn * 0.03, y, 0.018, sxn * 0.04, y + 0.0008, 0.035, C["pencil"])   # handles
    p.bbox(sx * 0.1, y, -sz * 0.42, sx * 0.42, y + 0.0008, -sz * 0.36, C["blue"])  # "L. Ortiz"
    return p


def business_card(sx, sy, sz):
    p = Part()
    t = sy * 0.6
    p.cbb(0, 0, 0, sx, t, sz, C["white"], b=0.0015, edge_col=C["white_dk"])
    p.bbox(-sx / 2 + 0.008, t, sz / 2 - 0.03, -sx / 2 + 0.03, t + 0.0009, sz / 2 - 0.008, C["red"])   # logo
    text_lines(p, -sx / 2 + 0.04, sx / 2 - 0.01, sz / 2 - 0.015, 0.012, t, 2, C["ink"], seed=31, thick=0.005)
    text_lines(p, -sx / 2 + 0.01, sx / 2 - 0.02, -0.0, 0.01, t, 3, C["pencil"], seed=32, thick=0.004)
    p.bbox(-sx / 2, t, -sz / 2, sx / 2, t + 0.0009, -sz / 2 + 0.008, C["maroon"])                     # card band
    return p


def keypad_panel(w, h, studio):
    """Static K1 keypad alarm panel (user's reference photo): portrait housing, LCD window with the time, 4 x 4 round
    keys (1-9, *, 0, #, A, B, ent, esc), status LED, cover flap hinged on the left and swung open. Centred, readable
    side -Z, 2.8 cm deep behind (+Z) so its back touches the display case's side glass."""
    p = Part()
    if studio:
        body, body_dk, frame = C["white"], C["white_dk"], C["charcoal"]
    else:
        body, body_dk, frame = C["cream"], C["brass"], C["walnut_dk"]
    p.bbox(-w / 2, -h / 2, 0.0, w / 2, h / 2, 0.028, body, b=0.008, edge_col=body_dk)      # housing
    p.bbox(-w / 2 + 0.008, -h / 2 + 0.008, -0.0015, w / 2 - 0.008, h / 2 - 0.008, 0.0, body)   # face plate
    # LCD window near the top, showing "21:42" / "20:17" as digit blocks + colon.
    p.bbox(-0.045, 0.045, -0.003, 0.045, 0.078, -0.0015, frame)
    p.bbox(-0.041, 0.049, -0.0035, 0.041, 0.074, -0.003, C["lcd"])
    for x in (-0.033, -0.019, 0.007, 0.021):
        p.bbox(x, 0.054, -0.004, x + 0.011, 0.069, -0.0035, C["ink"])
    for y in (0.057, 0.064):
        p.bbox(-0.0035, y, -0.004, 0.0005, y + 0.003, -0.0035, C["ink"])
    # 4 x 4 round keys with legends; the right column (A, B, ent, esc) has wider word legends.
    for r, y in enumerate((0.022, 0.0, -0.022, -0.044)):
        for c, x in enumerate((-0.033, -0.011, 0.011, 0.033)):
            p.frustum(x, y, -0.006, 0.0075, 0.0088, 0.0045, C["key"], sides=8, axis="z", phase=0.5)
            lw, lh = (0.0034, 0.0045) if c < 3 else (0.0065, 0.0022)
            p.bbox(x - lw, y - lh, -0.0064, x + lw, y + lh, -0.006, C["blue_dk"])
    p.frustum(0.032, -0.07, -0.004, 0.0028, 0.0032, 0.0025, C["led"], sides=6, axis="z")   # status LED
    p.bbox(-0.042, -0.072, -0.002, -0.012, -0.067, -0.0015, C["grey"])                      # brand strip
    # Cover flap: hinged on the left edge (-X), swung 115 degrees open (out to -Z and -X), instruction card inside.
    hx, hz, lw = -w / 2, -0.008, w - 0.004
    lid = Part()
    lid.bbox(hx, -h / 2 + 0.004, hz - 0.006, hx + lw, h / 2 - 0.004, hz, body, b=0.002, edge_col=body_dk)
    lid.bbox(hx + 0.014, -h / 2 + 0.03, hz, hx + lw - 0.014, h / 2 - 0.03, hz + 0.0008, C["paper"])
    for k in range(5):
        y = h / 2 - 0.045 - k * 0.018
        lid.bbox(hx + 0.022, y, hz + 0.0008, hx + lw - 0.03 + 0.008 * (k % 2), y + 0.004, hz + 0.0012, C["pencil"])
    for verts, col in lid.faces:
        p.face(rot_y(verts, 115.0, hx, hz), col)
    p.frustum(hx, -h / 2 + 0.01, hz, 0.0035, 0.0035, h - 0.02, C["silver"], sides=6)       # hinge barrel
    # Built as if -X were the viewer's left; seen from the readable side (-Z) in Maya's right-handed frame the left is
    # +X (Unity's import negates X but keeps the look), so mirror: flap on the left, A/B/ent/esc on the right.
    return mirrored(p, 0)


def sticky_note(w, h):
    p = Part()
    p.bbox(-w / 2, -h / 2 + 0.012, 0.0, w / 2, h / 2, 0.003, C["yellow"])
    # bottom edge curling off the screen
    p.face_out([(-w / 2, -h / 2 + 0.012, 0.0), (w / 2, -h / 2 + 0.012, 0.0), (w / 2, -h / 2, -0.008), (-w / 2, -h / 2, -0.008)],
               C["yellow_dk"], (0, -h / 2 + 0.006, 0.01))
    p.face_out([(-w / 2, -h / 2 + 0.012, 0.0), (w / 2, -h / 2 + 0.012, 0.0), (w / 2, -h / 2, -0.008), (-w / 2, -h / 2, -0.008)],
               C["yellow_dk"], (0, -h / 2 + 0.006, -0.02))
    for i, frac in enumerate((0.8, 0.7, 0.5)):
        y = h * 0.3 - i * h * 0.2
        p.bbox(-w * 0.38, y - 0.004, -0.0015, -w * 0.38 + w * 0.76 * frac, y + 0.004, 0.0, C["ink"])
    p.bbox(w * 0.15, -h * 0.35, -0.0015, w * 0.35, -h * 0.25, 0.0, C["blue"])   # "- A."
    return p


def pinned_note(w, h):
    p = Part()
    p.bbox(-w / 2, -h / 2, 0.0, w / 2, h / 2, 0.004, C["cream"])
    p.bbox(-w / 2, h / 2 - 0.018, -0.0008, w / 2, h / 2, 0.0, C["maroon"])      # gallery header
    p.frustum(0, h / 2 - 0.03, -0.012, 0.007, 0.006, 0.012, C["red"], sides=6, axis="z")   # pin
    for i, frac in enumerate((0.85, 0.7, 0.8, 0.45)):
        y = h * 0.15 - i * h * 0.16
        p.bbox(-w * 0.4, y - 0.003, -0.0015, -w * 0.4 + w * 0.8 * frac, y + 0.003, 0.0, C["ink"])
    p.bbox(w * 0.18, -h * 0.42, -0.0015, w * 0.38, -h * 0.34, 0.0, C["indigo"])  # "- S."
    return p


# ---------------------------------------------------------------------------------------------------------------
# Zone decals: zone-local, centred, a few mm high. Every zone gets the same subtle outline (in _Alpha).
# ---------------------------------------------------------------------------------------------------------------
def outline_border(w, d, t=0.012, y=0.004):
    a = Part()
    x0, x1, z0, z1 = -w / 2, w / 2, -d / 2, d / 2
    for p0, q0, p1, q1 in ((x0, z0, x1, z0 + t), (x0, z1 - t, x1, z1), (x0, z0 + t, x0 + t, z1 - t), (x1 - t, z0 + t, x1, z1 - t)):
        a.flat([(p0, q0), (p0, q1), (p1, q1), (p1, q0)], y, C["outline"])
    return a


def glass_shards(w, d, seed):
    p = Part()
    rnd = Rand(seed)
    for i in range(30):
        r = rnd.range(0.012, 0.05)
        cx = rnd.range(-1, 1) * (w / 2 - r - 0.02)
        cz = rnd.range(-1, 1) * (d / 2 - r - 0.02)
        a = rnd.next() * 2 * math.pi
        n = 3 if rnd.next() < 0.7 else 4
        pts = [(cx + math.cos(a + k * 2 * math.pi / n + rnd.range(-0.4, 0.4)) * r * rnd.range(0.6, 1.0),
                cz + math.sin(a + k * 2 * math.pi / n + rnd.range(-0.4, 0.4)) * r * rnd.range(0.6, 1.0)) for k in range(n)]
        p.prism(pts, 0.001, 0.003 + 0.003 * rnd.next(), C["shard_dk"], top_col=C["shard"] if i % 3 else C["glass"])
    return p


def coffee_spill(w, d):
    p = Part()
    p.prism(blob_outline(-0.02, -0.03, w * 0.36, d * 0.3, 16, 21), 0.001, 0.003, C["coffee"])
    p.prism(blob_outline(-0.02, -0.03, w * 0.22, d * 0.17, 12, 23), 0.003, 0.0035, C["coffee_lt"])   # sheen
    rnd = Rand(24)
    for i in range(6):
        cx, cz = rnd.range(-0.4, 0.4) * w, rnd.range(-0.4, 0.4) * d
        p.prism(ring_outline(cx, cz, rnd.range(0.008, 0.02), rnd.range(0.008, 0.016), 7), 0.001, 0.003, C["coffee"])
    # paper coffee cup lying on its side, flattened to a decal-like height
    p.prism([(0.10, 0.14), (0.16, 0.12), (0.17, 0.155), (0.11, 0.18)], 0.001, 0.006, C["white"], top_col=C["white_dk"])
    p.prism([(0.155, 0.122), (0.17, 0.118), (0.178, 0.152), (0.166, 0.156)], 0.001, 0.0065, C["teal"])
    return p


def blood_pool(w, d):
    # Case Y zone 2 (murder): flat faceted blood pool, darker rim, a bright facet, drips and a short smear. Palette
    # maroon/red only, no gloss, so it reads as stylised low-poly (user, session 6).
    p = Part()
    p.prism(blob_outline(0.0, 0.02, w * 0.38, d * 0.34, 10, 41), 0.001, 0.0025, C["maroon_dk"])
    p.prism(blob_outline(0.0, 0.02, w * 0.28, d * 0.25, 8, 42), 0.0025, 0.003, C["maroon"])
    p.prism(blob_outline(-0.02, 0.0, w * 0.1, d * 0.08, 5, 43), 0.003, 0.0034, C["red"])
    p.prism([(0.06, 0.06), (w * 0.42, 0.10), (w * 0.45, 0.14), (0.08, 0.10)], 0.001, 0.0025, C["maroon"])   # smear
    rnd = Rand(44)
    for i in range(5):                                                         # drips
        cx, cz = rnd.range(-0.42, 0.42) * w, rnd.range(-0.42, 0.42) * d
        p.prism(ring_outline(cx, cz, 0.014, 0.012, 5), 0.001, 0.0025, C["maroon_dk"])
    return p


def body_tape(width=0.035):
    # Case Y: police-style tape outline of a body lying on its back (head at -x, feet at +x), built in Unity
    # coordinates around the origin (CaseSceneTool puts it beside the blood pool). Decoration only, no zone.
    p = Part()
    h = 0.0055   # above the blood pool decal (0.0034)

    def strip(a, b):
        dx, dz = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dz)
        ux, uz = dx / n, dz / n
        e = width / 2
        ax, az, bx, bz = a[0] - ux * e, a[1] - uz * e, b[0] + ux * e, b[1] + uz * e
        nx, nz = -uz * e, ux * e
        p.prism([(ax + nx, az + nz), (bx + nx, bz + nz), (bx - nx, bz - nz), (ax - nx, az - nz)], 0.001, h, C["yellow"],
                top_col=C["yellow_dk"])

    ring = ring_outline(-0.80, 0.0, 0.11, 0.11, 8, phase=0.5)
    for i in range(8):
        strip(ring[i], ring[(i + 1) % 8])
    left = [(-0.69, 0.07), (-0.60, 0.24), (-0.50, 0.27), (-0.38, 0.40), (-0.20, 0.52), (-0.15, 0.46), (-0.30, 0.34),
            (-0.42, 0.19), (-0.30, 0.16), (-0.10, 0.18), (-0.02, 0.19), (0.10, 0.22), (0.45, 0.24), (0.78, 0.26),
            (0.82, 0.20), (0.78, 0.12), (0.16, 0.07), (0.0, 0.0)]
    right = [(-0.69, -0.07), (-0.60, -0.24), (-0.50, -0.27), (-0.38, -0.38), (-0.18, -0.34), (-0.14, -0.28),
             (-0.32, -0.24), (-0.42, -0.19), (-0.30, -0.16), (-0.10, -0.18), (-0.02, -0.19), (0.10, -0.20),
             (0.45, -0.19), (0.74, -0.18), (0.78, -0.12), (0.72, -0.07), (0.16, -0.05), (0.0, 0.0)]
    for side in (left, right):
        for i in range(len(side) - 1):
            strip(side[i], side[i + 1])
    return p


def logbook(sx, sy, sz):
    p = Part()
    c = sy * 0.18
    p.cbb(0, 0, 0, sx, c, sz, C["maroon"], b=0.003, edge_col=C["maroon_dk"])         # back cover
    p.cbb(0.004, c, 0, sx - 0.014, sy - 2 * c, sz - 0.012, C["paper_dk"])             # page block
    for i in range(3):
        p.cbb(0.004 + sx / 2 - 0.008, c + 0.002 + i * (sy - 2 * c) / 3, 0, 0.0008, 0.001, sz - 0.014, C["paper"])
    p.cbb(0, sy - c, 0, sx, c, sz, C["maroon"], b=0.003, edge_col=C["maroon_dk"])     # front cover
    p.cbb(-sx / 2 + 0.011, 0, 0, 0.022, sy, sz, C["maroon_dk"], b=0.004)             # spine
    p.cbb(0.02, sy, 0.04, sx * 0.5, 0.0008, 0.05, C["batik_lt"])                      # label
    p.cbb(0.02, sy + 0.0008, 0.04, sx * 0.35, 0.0006, 0.006, C["ink"])
    p.cbb(0.04, c, -sz / 2 + 0.005, 0.008, sy - 2 * c, 0.01, C["ochre"])             # ribbon
    return p


def keris(sx, sy, sz):
    """Case Y K3, the murder weapon: a wavy-bladed keris lying on the floor, bloody blade, carved wooden hilt with a
    curved pommel, brass guard and a strand of green tour-guide scarf caught at the hilt. Handle at -x, tip at +x."""
    p = Part()
    x0 = -sx / 2
    p.frustum(x0 + 0.03, 0.017, 0, 0.010, 0.0145, 0.06, C["wood_dk"], sides=8, axis="x")      # hilt
    p.bbox(x0, 0.012, -0.012, x0 + 0.032, 0.034, 0.012, C["walnut"], b=0.003)                  # curved pommel
    p.bbox(x0 + 0.09, 0.004, -0.026, x0 + 0.106, 0.030, 0.026, C["brass"], b=0.002)           # guard
    xb, length = x0 + 0.106, sx / 2 - (x0 + 0.106)

    def blade(t0, grow):
        top, bot = [], []
        for i in range(8):
            t = t0 + (1.0 - t0) * i / 7.0
            wave = 0.010 * math.sin(t * math.pi * 3)
            half = (0.016 * (1.0 - 0.85 * t) + 0.002) * grow
            top.append((xb + length * t, wave + half))
            bot.append((xb + length * t, wave - half))
        return top + list(reversed(bot))
    p.prism(blade(0.0, 1.0), 0.003, 0.011, C["silver"], top_col=C["chrome"])                    # blade
    p.prism(blade(0.3, 0.8), 0.011, 0.0118, C["maroon"])                                        # blood on the blade
    p.prism([(x0 + 0.050, 0.012), (x0 + 0.058, 0.012), (x0 + 0.066, 0.034), (x0 + 0.058, 0.034)], 0.001, 0.0035, C["green"])   # scarf
    p.prism([(x0 + 0.060, 0.012), (x0 + 0.066, 0.012), (x0 + 0.082, 0.030), (x0 + 0.076, 0.032)], 0.001, 0.0035, C["green_dk"])
    return p


def wall_clock(w):
    """Case Y K1: round wall clock smashed in the struggle, hands stopped at 8:17, cracked glass. Static clue, centred,
    readable side -Z, 3 cm deep behind. Built as if -X were the viewer's left, then mirrored like the keypad."""
    p = Part()
    r = w / 2
    p.frustum(0, 0, 0, r, r, 0.03, C["walnut"], sides=12, axis="z")                              # case
    p.frustum(0, 0, -0.002, r * 0.88, r * 0.88, 0.002, C["cream"], sides=12, axis="z")           # face
    for k in range(12):
        a = math.radians(k * 30.0)
        p.frustum(r * 0.74 * math.sin(a), r * 0.74 * math.cos(a), -0.0035, 0.004, 0.004, 0.0015, C["ink"], sides=5, axis="z")

    def hand(deg, length, width, z, col):
        a = math.radians(deg)
        dx, dy = math.sin(a), math.cos(a)
        nx, ny = dy, -dx
        pts = [(-dx * 0.012 + nx * width / 2, -dy * 0.012 + ny * width / 2, z),
               (dx * length * 0.3 + nx * width / 2, dy * length * 0.3 + ny * width / 2, z),
               (dx * length, dy * length, z),
               (dx * length * 0.3 - nx * width / 2, dy * length * 0.3 - ny * width / 2, z),
               (-dx * 0.012 - nx * width / 2, -dy * 0.012 - ny * width / 2, z)]
        p.face_out(pts, col, (0, 0, 0.05))
    hand(8 * 30.0 + 17 * 0.5, r * 0.5, 0.012, -0.0038, C["ink"])    # hour hand, 8:17
    hand(17 * 6.0, r * 0.74, 0.008, -0.0044, C["ink"])               # minute hand
    p.frustum(0, 0, -0.005, 0.006, 0.006, 0.002, C["brass"], sides=6, axis="z")
    for (x0, y0, x1, y1) in ((0.012, 0.018, 0.058, 0.062), (-0.01, -0.012, -0.05, -0.058), (0.03, -0.03, 0.07, -0.02)):   # cracks
        dx, dy = x1 - x0, y1 - y0
        n = math.hypot(dx, dy)
        nx, ny = -dy / n * 0.0015, dx / n * 0.0015
        p.face_out([(x0 + nx, y0 + ny, -0.0047), (x1 + nx, y1 + ny, -0.0047), (x1 - nx, y1 - ny, -0.0047), (x0 - nx, y0 - ny, -0.0047)],
                   C["charcoal"], (0, 0, 0.05))
    return mirrored(p, 0)


def fallen_chair():
    """Case Y: a wooden chair knocked over in the struggle, lying on its back. Decoration."""
    base = Part()
    wooden_chair(base, 0, 0, C["wood"], C["wood_dk"], C["indigo"], 0.0)
    faces = [([(x, z, -y) for x, y, z in verts], col) for verts, col in base.faces]   # roll 90 degrees onto its back
    low = min(v[1] for verts, _ in faces for v in verts)
    q = Part()
    for verts, col in faces:
        q.face([(x, y - low + 0.001, z) for x, y, z in verts], col)
    return q


def bloody_footprints(w, d):
    """Case Y zone 2: bloody shoe prints leading from the pool (+z end) towards the entrance (-z), toes pointing -z,
    fading as the blood runs out, a few drops between them."""
    p = Part()
    steps = 9
    sole0 = [(-0.034, -0.04), (0.032, -0.04), (0.044, 0.0), (0.04, 0.045), (0.012, 0.068), (-0.02, 0.064), (-0.04, 0.035), (-0.044, -0.005)]
    rnd = Rand(91)
    for i in range(steps):
        z = d / 2 - 0.14 - i * (d - 0.28) / (steps - 1)
        side = -1 if i % 2 == 0 else 1
        x = side * w * 0.2 + rnd.range(-0.02, 0.02)
        col = C["maroon"] if i < 5 else C["maroon_dk"]
        sole = [(x + dx * side, z - dz) for dx, dz in sole0]                 # toe towards -z
        p.prism(sole, 0.001, 0.0028, col)
        p.prism(ring_outline(x, z + 0.085, 0.032, 0.03, 8), 0.001, 0.0028, col)   # heel
        if i < steps - 1:
            for k in range(2):
                cx, cz = x + rnd.range(-0.1, 0.1), z - 0.12 + rnd.range(-0.05, 0.05)
                p.prism(ring_outline(cx, cz, 0.009, 0.008, 5), 0.001, 0.0026, C["maroon_dk"])
    return p


def vase_shards(w, d):
    """Case Y zone 3: a batik vase knocked off its stand: sharp indigo/ochre shards and the broken base."""
    p = Part()
    rnd = Rand(81)
    for i in range(14):
        r = rnd.range(0.02, 0.055)
        cx = rnd.range(-1, 1) * (w / 2 - r - 0.02)
        cz = rnd.range(-1, 1) * (d / 2 - r - 0.02)
        a = rnd.next() * 2 * math.pi
        n = 3 if rnd.next() < 0.7 else 4
        pts = [(cx + math.cos(a + k * 2 * math.pi / n + rnd.range(-0.4, 0.4)) * r * rnd.range(0.6, 1.0),
                cz + math.sin(a + k * 2 * math.pi / n + rnd.range(-0.4, 0.4)) * r * rnd.range(0.6, 1.0)) for k in range(n)]
        p.prism(pts, 0.001, 0.003 + 0.003 * rnd.next(), C["indigo"] if i % 3 else C["ochre"], top_col=C["batik_lt"])
    p.prism(ring_outline(0.0, 0.0, 0.06, 0.06, 8), 0.001, 0.014, C["indigo_dk"], top_col=C["ochre"])   # broken base
    return p


def trophy_ghost(k=1.7):
    """Case X: silhouette of the missing award (a gold cup with handles and a star), standing on the display case's empty
    stand (stand top y = 0.95 + 0.03 + 0.08 = 1.06, z = -0.02). Built in the display's frame, flat light colour (unlit
    ghost material, alpha animated in Unity). k = size factor (user: bigger; 1.7 -> about 0.45 m tall, case interior 0.66 m)."""
    p = Part()
    col = C["lamp"]
    cz, y0 = -0.02, 1.06

    def y(v):
        return y0 + (v - y0) * k
    p.frustum(0, y0, cz, 0.045 * k, 0.04 * k, 0.016 * k, col, sides=8)                   # base
    p.frustum(0, y(1.076), cz, 0.014 * k, 0.012 * k, 0.05 * k, col, sides=8)             # stem
    p.frustum(0, y(1.126), cz, 0.02 * k, 0.05 * k, 0.10 * k, col, sides=8)               # cup
    for sx in (-1, 1):
        p.bbox(sx * 0.062 * k - 0.007 * k, y(1.17), cz - 0.006 * k, sx * 0.062 * k + 0.007 * k, y(1.22), cz + 0.006 * k, col)   # handles
        p.bbox(sx * 0.045 * k, y(1.17), cz - 0.006 * k, sx * 0.065 * k, y(1.182), cz + 0.006 * k, col)
    star = []
    for i in range(10):
        r = (0.04 if i % 2 == 0 else 0.017) * k
        a = math.radians(90 + i * 36)
        star.append((r * math.cos(a), y(1.285) + r * math.sin(a), cz))
    p.face_out(star, col, (0, y(1.285), cz + 0.2))
    p.face_out(star, col, (0, y(1.285), cz - 0.2))
    return p


def footprints(w, d):
    p = Part()
    steps = 6
    for i in range(steps):
        z = -d / 2 + 0.13 + i * (d - 0.21) / (steps - 1)
        x = (-1 if i % 2 == 0 else 1) * w * 0.18
        mirror = -1 if i % 2 == 0 else 1
        sole = [(x - 0.034, z - 0.04), (x + 0.032, z - 0.04), (x + 0.044, z + 0.0), (x + 0.04, z + 0.045),
                (x + 0.012, z + 0.068), (x - 0.02, z + 0.064), (x - 0.04, z + 0.035), (x - 0.044, z - 0.005)]
        sole = [(x + (sx - x) * mirror, sz) for sx, sz in sole]
        p.prism(sole, 0.001, 0.003, C["mud"])
        p.prism(ring_outline(x, z - 0.085, 0.032, 0.03, 8), 0.001, 0.003, C["mud"])                 # heel
        for k in range(3):                                                                           # tread bars
            zz = z - 0.025 + k * 0.03
            p.bbox(x - 0.025, 0.003, zz - 0.004, x + 0.025, 0.0036, zz + 0.004, C["mud_dk"])
        p.bbox(x - 0.02, 0.003, z - 0.092, x + 0.02, 0.0036, z - 0.08, C["mud_dk"])
        rnd = Rand(60 + i)
        for k in range(2):                                                                           # mud crumbs
            cx, cz = x + rnd.range(-0.07, 0.07), z + rnd.range(-0.1, 0.1)
            p.prism(ring_outline(cx, cz, 0.008, 0.007, 5), 0.001, 0.004, C["mud_dk"])
    return p


# ---------------------------------------------------------------------------------------------------------------
# Room floors (session 6, user: a visible, semi-transparent floor per case; Unity material Props_Floor).
# Built in Unity's case frame (x right, z towards the front wall) and mirrored to Maya's frame on export
# (Unity's FBX import negates X), so the floorboards and the varnish zone decal line up exactly in Unity.
# ---------------------------------------------------------------------------------------------------------------
FLOOR_HALF = 2.5            # PlayArea.HalfSize (5 x 5 m)
FLOOR_Y = -0.001            # just under the decals (>= 1 mm) and the physics floor (y 0)


def to_maya(part):
    """Part built in Unity coordinates -> Maya coordinates (Unity's FBX import negates X)."""
    return mirrored(part, 0)


def floor_tiles():
    """Case X: light polished-concrete tiles, 0.5 m, with thin open joints (the real floor shows through)."""
    p = Part()
    rnd = Rand(61)
    n, gap = 10, 0.004
    t = 2 * FLOOR_HALF / n
    tones = [C["grey_lt"], C["white_dk"], C["grey_lt"], C["silver"]]
    for i in range(n):
        for j in range(n):
            x0, z0 = -FLOOR_HALF + i * t + gap / 2, -FLOOR_HALF + j * t + gap / 2
            p.flat([(x0, z0), (x0 + t - gap, z0), (x0 + t - gap, z0 + t - gap), (x0, z0 + t - gap)], FLOOR_Y,
                   tones[int(rnd.next() * len(tones))])
    return p


def plank_layout():
    """Case Y floorboards in the case frame: (x0, x1, z0, z1, colour) per board; boards run front-back, staggered."""
    rnd = Rand(71)
    rows = 33
    bw, gap = 2 * FLOOR_HALF / rows, 0.003
    tones = [C["wood"], C["wood"], C["wood_lt"], C["wood_dk"], C["wood"]]
    boards = []
    for i in range(rows):
        x0 = -FLOOR_HALF + i * bw + gap / 2
        z = -FLOOR_HALF - rnd.range(0.0, 1.2)
        while z < FLOOR_HALF:
            length = rnd.range(0.9, 1.8)
            za, zb = max(z, -FLOOR_HALF), min(z + length, FLOOR_HALF)
            if zb - za > 0.05:
                boards.append((x0, x0 + bw - gap, za + (gap / 2 if za > -FLOOR_HALF else 0),
                               zb - (gap / 2 if zb < FLOOR_HALF else 0), tones[int(rnd.next() * len(tones))]))
            z += length
    return boards


def floor_planks():
    p = Part()
    for x0, x1, z0, z1, col in plank_layout():
        p.flat([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], FLOOR_Y, col)
    return p


# ---------------------------------------------------------------------------------------------------------------
# Build everything
# ---------------------------------------------------------------------------------------------------------------
def build():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    write_palette(os.path.join(OUT, "Palette.png"))
    write_blob(os.path.join(OUT, "BlobShadow.png"))

    # Case X - modern design studio.
    b, g = display_case(0.6, 0.5, studio=True)
    export("X_Display", b, g)
    export("X_FrontDesk", front_desk_x())
    export("X_ManagerDesk", manager_desk_x())
    export("X_Monitor", monitor_stand())
    export("X_Door", door(0.8, studio=True))

    # Case Y - heritage gallery.
    b, g = display_case(0.6, 0.5, studio=False)
    export("Y_Display", b, g)
    export("Y_ReceptionTable", reception_table_y())
    export("Y_TourDesk", tour_desk_y())
    export("Y_Noticeboard", noticeboard())
    export("Y_Entrance", door(0.8, studio=False))

    # Evidence board (shared by both cases; under CaseRoot, built by FYP > Set Up Evidence Board).
    export("EvidenceBoard", evidence_board())

    # Briefing detective (parts animated in Unity; offsets DETECTIVE_*_AT = CharacterSetupTool).
    export("Detective_Body", to_maya(detective_body()))
    export("Detective_Head", to_maya(detective_head()))
    export("Detective_Moustache", to_maya(detective_moustache()))
    export("Detective_Arm", to_maya(detective_arm("upper")))
    export("Detective_Forearm", to_maya(detective_arm("fore")))
    export("Detective_ForearmPoint", to_maya(detective_arm("fore", point=True)))
    export("Detective_ArmL", to_maya(detective_arm_left("upper")))
    export("Detective_ForearmL", to_maya(detective_arm_left("fore")))
    export("Detective_Eyes", to_maya(detective_eyes()))
    export("Detective_Mouth", to_maya(detective_mouth()))

    # Team partner marker head (loaded with Resources.Load by PartnerMarker).
    export("PartnerHead", to_maya(partner_head()), out=RESOURCES)

    # Room floors (semi-transparent in Unity).
    export("X_Floor", to_maya(floor_tiles()), kind="floor")
    export("Y_Floor", to_maya(floor_planks()), kind="floor")

    # Clues: sizes = CaseSceneTool.ClueSize (keep in sync). Same size range, contrast and detail per slot in both cases.
    export("X_K1", keypad_panel(0.12, 0.18, studio=True), kind="clue")
    export("Y_K1", wall_clock(0.20), kind="clue")
    export("X_K2", readable(clipboard_sheet(0.23, 0.015, 0.32)), kind="clue")
    export("Y_K2", readable(logbook(0.21, 0.03, 0.29)), kind="clue")
    export("X_K3", lanyard_clip(0.10, 0.02, 0.07), kind="clue")
    export("Y_K3", keris(0.30, 0.035, 0.07), kind="clue")
    export("X_K4", readable(printout(0.21, 0.004, 0.297)), kind="clue")
    export("Y_K4", readable(chat_printout(0.21, 0.004, 0.297)), kind="clue")
    export("X_R1", glove(0.18, 0.04, 0.10), kind="clue")
    export("Y_R1", torch(0.20, 0.045, 0.045), kind="clue")
    export("X_R2", readable(torn_sketch(0.17, 0.006, 0.13)), kind="clue")
    export("Y_R2", readable(business_card(0.13, 0.006, 0.08)), kind="clue")
    export("X_R3", sticky_note(0.10, 0.10), kind="clue")
    export("Y_R3", pinned_note(0.12, 0.12), kind="clue")

    # Zone decals: sizes = CaseSpecs zones (X: Z1 0.36 x 0.4, Z2 0.40 x 0.5, Z3 0.35 x 0.9; Y: pool 0.6 x 0.6,
    # footprints 0.45 x 2.2, vase 0.45 x 0.45).
    zones = {"Z1": (0.36, 0.40), "Z2": (0.40, 0.50), "Z3": (0.35, 0.90)}
    export("X_Z1", glass_shards(*zones["Z1"], seed=11), alpha=outline_border(*zones["Z1"]), kind="zone")
    export("X_Z2", coffee_spill(*zones["Z2"]), alpha=outline_border(*zones["Z2"]), kind="zone")
    export("X_Z3", footprints(*zones["Z3"]), alpha=outline_border(*zones["Z3"]), kind="zone")
    export("Y_Z1", blood_pool(0.6, 0.6), alpha=outline_border(0.6, 0.6), kind="zone")
    export("Y_Z2", bloody_footprints(0.45, 2.2), alpha=outline_border(0.45, 2.2), kind="zone")
    export("Y_Z3", vase_shards(0.45, 0.45), alpha=outline_border(0.45, 0.45), kind="zone")
    export("Y_BodyTape", to_maya(body_tape()), kind="decor")
    export("X_TrophyGhost", trophy_ghost(), kind="decor")
    export("Y_FallenChair", fallen_chair(), kind="decor")

    total = 0
    print("\n{:6s} {:18s} {:>22s} {:>8s} {:>6s}".format("kind", "name", "size x/y/z (m)", "top (m)", "tris"))
    for kind, name, size, top, tris in REPORT:
        total += tris
        print("{:6s} {:18s} {:>22s} {:8.3f} {:6d}".format(kind, name, "{:.3f} / {:.3f} / {:.3f}".format(*size), top, tris))
    print("TOTAL triangles (all objects, both cases): {}".format(total))
    if total > TRIANGLE_BUDGET:
        print("ERROR: over the {} triangle budget".format(TRIANGLE_BUDGET))
        sys.exit(1)


if __name__ == "__main__":
    cmds.loadPlugin("fbxmaya", quiet=True)
    try:
        build()
    finally:
        maya.standalone.uninitialize()
