#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy>=1.26"]
# [tool.uv]
# exclude-newer = "2026-11-01T00:00:00Z"
# ///
"""Thin-rim glasses generated from face measurements.

    uv run build_glasses.py [--out Assets/_USER/Glasses]

Writes Glasses.obj: one static mesh, one material slot, every vertex on one UV point (a palette
swatch, or anywhere for a plain-colour material). Every part is a tube swept along a path:
  - two rims, a shape between a circle and an oval, slightly wrapped and tilted like real frames;
  - a bridge arching between the rims;
  - two nose pads on short arms;
  - two temples running back over the ears, with a short hook behind them.

Geometry is authored in avatar space (metres; avatar at the origin facing +Z; +X is the avatar's
right, +Y up) and written relative to the Head bone. In Unity the mesh goes on an object placed at the
Head bone with zero local rotation, under an MA Bone Proxy to Head (As child; keep world pose).
OBJ is right-handed and Unity negates X on import, so X is negated on write and the triangle order is
kept; normals point outward in Unity space.

Measure first, on the face mesh with the shape keys the avatar keeps (skill vrchat-accessories has the
snippet): the Head bone, an eye bone, the front of the face at the nose bridge. Then adjust the design
numbers with the user from renders: front, side, three-quarter and top-down close-ups, plus the
expressions that move the cheeks and eyelids (smiles, closed eyes), which can push the face through
the rims or the pads.

Verified: the CONFIG below is the MANUKA (Oct 2026) face after reshaping, and gives the glasses that
avatar wears (1,484 triangles). Where it may not fit: other head shapes need the temple path redone
(it is absolute, not derived from a measurement); faces much wider or narrower than MANUKA's need the
lens size too; hair over the ears is not taken into account (the temples go into it).
"""

import math
import os
import sys

import numpy as np

# ---- CONFIG: measured on your avatar -------------------------------------------------------------

HEAD = np.array((0.0, 1.0925, 0.004))   # Head bone position
EYE = (0.0302, 1.1343)                  # eye bone |x| and y
BRIDGE_Z = 0.071                        # front of the face at the nose bridge (forehead was 0.073-0.076)

MATERIAL = "Glasses"                    # the OBJ's material name (the slot's name on import)
UV = (0.5, 0.5)                         # UV of every vertex: a palette swatch when the glasses will
                                        # share a palette material (vrchat-pc-good); MANUKA's Good
                                        # build used (0.625, 0.75), the gunmetal swatch

# ---- design: MANUKA's glasses, a starting point to adjust with the user ---------------------------

# Anime glasses read as glasses only when the front is about as wide as the face: outer rim near the
# cheek outline, top rim just under the brows, lenses nearly touching at the bridge.
LENS_CX = EYE[0] + 0.0028   # lens centre, |x|
LENS_CY = EYE[1] + 0.0002   # lens centre, y (pupils sit about at it)
LENS_A = 0.0270             # half width  (a / b = 1.17: between round and oval)
LENS_B = 0.0230             # half height
LENS_N = 2.1                # superellipse exponent (2 = ellipse, higher = squarer)
FRONT_Z = BRIDGE_Z + 0.0115 # frame front at the inner rim edge
WRAP_DEG = 10.0             # outer side of each lens swings back
TILT_DEG = 8.0              # pantoscopic tilt: lens bottom closer to the cheek

RIM_R = (0.0009, 0.0008)        # rim tube: in-plane, depth half sizes
RIM_SEG = (48, 6)               # samples along the rim, around the tube

BRIDGE_Y = 0.0100               # bridge height above lens centre at the rims
BRIDGE_ARCH = 0.0022            # extra rise at the middle
BRIDGE_R = (0.0008, 0.0008)
BRIDGE_SEG = (8, 6)

PAD_CENTRE = (0.0062, EYE[1] - 0.0078, BRIDGE_Z + 0.0030)   # right pad (|x|, y, z), on the nose
PAD_HALF = (0.0011, 0.0022, 0.0007)     # pad ellipsoid half extents
PAD_ARM_R = (0.00045, 0.00045)

# Temple path for the right side, after the hinge point on the rim: (|x|, y above LENS_CY, z).
# Absolute, MANUKA's: it must clear the side of the head and end behind the ear. On MANUKA the face
# mesh's half width at eye height (the side of the head) is 0.0785, and the path's widest points are
# at 0.0775-0.0805, around it. Check it top-down.
TEMPLE_PATH = (
    (0.0690, 0.0065, 0.0640),
    (0.0775, 0.0085, 0.0450),
    (0.0805, 0.0100, 0.0100),
    (0.0795, 0.0085, -0.0120),
    (0.0760, -0.0040, -0.0250),
)
HINGE_ANGLE = 0.10              # rim parameter of the hinge (rad, 0 = outer)
TEMPLE_R = (0.0006, 0.0011)     # horizontal, vertical half sizes (flat bar)
TEMPLE_SEG = (14, 6)


# ---------------------------------------------------------------- frame shape

def lens_point(side, t):
    """Point on the rim centreline. side = +1 right lens, -1 left lens.
    t = 0 is the outer edge, pi/2 the top, pi the inner edge."""
    c, s = math.cos(t), math.sin(t)
    u = LENS_A * math.copysign(abs(c) ** (2 / LENS_N), c)
    v = LENS_B * math.copysign(abs(s) ** (2 / LENS_N), s)
    x = side * (LENS_CX + u)
    y = LENS_CY + v
    return np.array((x, y, front_z(x, y)))


def front_z(x, y):
    inner = LENS_CX - LENS_A
    z = FRONT_Z - max(0.0, abs(x) - inner) * math.tan(math.radians(WRAP_DEG))
    return z + (y - LENS_CY) * math.tan(math.radians(TILT_DEG))


def lens_normal(side):
    """Forward-facing normal of a lens plane (wrap + tilt)."""
    w, t = math.radians(WRAP_DEG), math.radians(TILT_DEG)
    n = np.array((side * math.sin(w), -math.sin(t), math.cos(w)))
    return n / np.linalg.norm(n)


# ---------------------------------------------------------------- mesh building

class Mesh:
    def __init__(self):
        self.v, self.n, self.f = [], [], []

    def add_vertex(self, p, nrm):
        self.v.append(np.asarray(p, float))
        self.n.append(np.asarray(nrm, float) / np.linalg.norm(nrm))
        return len(self.v) - 1

    def add_tri(self, a, b, c):
        """Add a triangle wound so its face normal follows the vertex normals."""
        pa, pb, pc = self.v[a], self.v[b], self.v[c]
        fn = np.cross(pb - pa, pc - pa)
        if np.dot(fn, self.n[a] + self.n[b] + self.n[c]) < 0:
            b, c = c, b
        self.f.append((a, b, c))


def frames(path, closed, ref):
    """Tangent + two perpendicular axes per path point. ref(i) gives the
    vector the second axis should lean toward (lens normal or world up)."""
    count = len(path)
    out = []
    for i in range(count):
        if closed:
            d = path[(i + 1) % count] - path[i - 1]
        else:
            d = path[min(i + 1, count - 1)] - path[max(i - 1, 0)]
        tan = d / np.linalg.norm(d)
        side = np.cross(ref(i), tan)
        side /= np.linalg.norm(side)
        up = np.cross(tan, side)
        out.append((tan, side, up))
    return out


def sweep(mesh, path, closed, ref, radii, ring, cap_end=False):
    """Elliptic tube along path. radii = (half size along side axis,
    half size along up axis)."""
    path = [np.asarray(p, float) for p in path]
    fr = frames(path, closed, ref)
    rows = []
    for p, (_, side, up) in zip(path, fr):
        row = []
        for j in range(ring):
            phi = 2 * math.pi * j / ring
            off = radii[0] * math.cos(phi) * side + radii[1] * math.sin(phi) * up
            # ellipse normal: scale the offset by the inverse squared radii
            nrm = (math.cos(phi) / radii[0]) * side + (math.sin(phi) / radii[1]) * up
            row.append(mesh.add_vertex(p + off, nrm))
        rows.append(row)
    spans = len(rows) if closed else len(rows) - 1
    for i in range(spans):
        r0, r1 = rows[i], rows[(i + 1) % len(rows)]
        for j in range(ring):
            k = (j + 1) % ring
            mesh.add_tri(r0[j], r1[j], r1[k])
            mesh.add_tri(r0[j], r1[k], r0[k])
    if cap_end:
        tan = fr[-1][0]
        tip = mesh.add_vertex(path[-1] + tan * min(radii) * 0.8, tan)
        last = rows[-1]
        for j in range(ring):
            mesh.add_tri(last[j], last[(j + 1) % ring], tip)


def ellipsoid(mesh, centre, half, rings=4, segs=6):
    centre = np.asarray(centre, float)
    half = np.asarray(half, float)
    top = mesh.add_vertex(centre + (0, half[1], 0), (0, 1, 0))
    bottom = mesh.add_vertex(centre - (0, half[1], 0), (0, -1, 0))
    rows = []
    for i in range(1, rings):
        th = math.pi * i / rings
        row = []
        for j in range(segs):
            ph = 2 * math.pi * j / segs
            unit = np.array((math.sin(th) * math.cos(ph), math.cos(th), math.sin(th) * math.sin(ph)))
            row.append(mesh.add_vertex(centre + unit * half, unit / half))
        rows.append(row)
    for j in range(segs):
        k = (j + 1) % segs
        mesh.add_tri(top, rows[0][j], rows[0][k])
        mesh.add_tri(bottom, rows[-1][k], rows[-1][j])
    for r0, r1 in zip(rows, rows[1:]):
        for j in range(segs):
            k = (j + 1) % segs
            mesh.add_tri(r0[j], r1[j], r1[k])
            mesh.add_tri(r0[j], r1[k], r0[k])


def build():
    mesh = Mesh()
    up = np.array((0.0, 1.0, 0.0))
    for side in (-1, 1):
        nrm = lens_normal(side)
        count = RIM_SEG[0]
        rim = [lens_point(side, 2 * math.pi * i / count) for i in range(count)]
        sweep(mesh, rim, True, lambda i, n=nrm: n, RIM_R, RIM_SEG[1])

        # temple: hinge on the outer rim, then back over the ear
        hinge = lens_point(side, HINGE_ANGLE)
        temple = [hinge] + [np.array((side * x, LENS_CY + dy, z)) for x, dy, z in TEMPLE_PATH]
        sweep(mesh, temple, False, lambda i: up, TEMPLE_R, TEMPLE_SEG[1], cap_end=True)

        # nose pad on a short arm from the lower inner rim
        pad = np.array((side * PAD_CENTRE[0], PAD_CENTRE[1], PAD_CENTRE[2]))
        root = lens_point(side, math.pi * 1.10)
        mid = (root + pad) / 2 + np.array((0, 0, 0.0015))
        sweep(mesh, [root, mid, pad], False, lambda i: up, PAD_ARM_R, 4)
        ellipsoid(mesh, pad, PAD_HALF)

    # bridge between the inner rims
    count = BRIDGE_SEG[0]
    inner = LENS_CX - LENS_A + RIM_R[0] * 0.5
    bridge = []
    for i in range(count + 1):
        s = i / count
        x = -inner + 2 * inner * s
        y = LENS_CY + BRIDGE_Y + BRIDGE_ARCH * math.sin(math.pi * s)
        bridge.append(np.array((x, y, front_z(inner, LENS_CY + BRIDGE_Y) + 0.0002)))
    sweep(mesh, bridge, False, lambda i: np.array((0.0, 0.0, 1.0)), BRIDGE_R, BRIDGE_SEG[1])
    return mesh



# ---------------------------------------------------------------- output

def write_obj(mesh, path):
    with open(path, "w") as fh:
        fh.write("# generated by build_glasses.py\n")
        fh.write("o Glasses\n")
        for p in mesh.v:
            q = p - HEAD
            fh.write(f"v {-q[0]:.6f} {q[1]:.6f} {q[2]:.6f}\n")
        for n in mesh.n:
            fh.write(f"vn {-n[0]:.5f} {n[1]:.5f} {n[2]:.5f}\n")
        fh.write(f"vt {UV[0]} {UV[1]}\n")
        fh.write(f"usemtl {MATERIAL}\n")
        for a, b, c in mesh.f:
            fh.write(f"f {a + 1}/1/{a + 1} {b + 1}/1/{b + 1} {c + 1}/1/{c + 1}\n")


def main():
    argv = sys.argv[1:]
    out_dir = "Assets/_USER/Glasses"
    for flag, val in zip(argv[::2], argv[1::2]):
        if flag == "--out":
            out_dir = val
    os.makedirs(out_dir, exist_ok=True)
    mesh = build()
    path = os.path.join(out_dir, "Glasses.obj")
    write_obj(mesh, path)
    print(f"Glasses: {len(mesh.v)} verts, {len(mesh.f)} tris -> {path}")


main()
