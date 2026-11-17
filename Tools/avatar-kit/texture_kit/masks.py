"""Masks: float arrays in 0..1, the size of the texture (row 0 = top).

A recipe names its masks; each one is built from exactly one source and then modified:

  {"uv": "face_uv.json", "groups": ["lash"]}   UV triangles exported from Unity (Tools/Avatar Agent Kit)
  {"image": "iris_mask.png"}                   a mask image the size of the texture, white = selected
  {"box": [x0, y0, x1, y1]}                    pixels, top-left origin, upper bounds exclusive
  {"ellipses": [{"center": [x, y], "radius": [rx, ry], "feather": 0.35}]}
                                               full inside, fading to 0 at (1 + feather) x radius
  {"union": ["a", "b"]}  {"intersect": ["a", "b"]}   other masks of the recipe, by name

Modifiers, applied in this order:
  "dilate": n          grow by n pixels (circular); the result is hard-edged
  "minus": ["a"]       remove other masks
  "within_alpha": true keep only pixels the texture shows (alpha > 0)
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np

from .image import read_gray

SOURCES = ("uv", "image", "box", "ellipses", "union", "intersect")


class MaskError(ValueError):
    pass


def rasterize(tris, width: int, height: int) -> np.ndarray:
    """tris: [[u0, v0, u1, v1, u2, v2], ...], UV origin bottom-left -> bool mask.

    A pixel is in when its centre lies inside (or on the edge of) a triangle.
    """
    mask = np.zeros((height, width), bool)
    for uv in tris:
        p = np.array(uv, float).reshape(3, 2)
        x = p[:, 0] * width - 0.5
        y = (1 - p[:, 1]) * height - 0.5
        x0, x1 = max(int(np.floor(x.min())), 0), min(int(np.ceil(x.max())) + 1, width)
        y0, y1 = max(int(np.floor(y.min())), 0), min(int(np.ceil(y.max())) + 1, height)
        if x0 >= x1 or y0 >= y1:
            continue
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-12:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1), np.arange(y0, y1))
        a = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        b = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        mask[y0:y1, x0:x1] |= (a >= -1e-9) & (b >= -1e-9) & (1 - a - b >= -1e-9)
    return mask


def bbox(mask: np.ndarray):
    """[x0, y0, x1, y1) of the non-zero pixels, or None."""
    ys, xs = np.nonzero(mask)
    if not len(xs):
        return None
    return [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]


def dilate_disk(mask: np.ndarray, radius: int) -> np.ndarray:
    """Exact circular dilation on the integer lattice (no SciPy needed)."""
    mask = mask.astype(bool)
    extent = bbox(mask)
    result = np.zeros_like(mask)
    if extent is None or radius <= 0:
        return mask.copy()
    x0, y0, x1, y1 = extent
    x0, y0 = max(0, x0 - radius), max(0, y0 - radius)
    x1, y1 = min(mask.shape[1], x1 + radius), min(mask.shape[0], y1 + radius)
    source = mask[y0:y1, x0:x1]
    target = result[y0:y1, x0:x1]
    h, w = source.shape
    for dy in range(-radius, radius + 1):
        dx_max = math.isqrt(radius * radius - dy * dy)
        for dx in range(-dx_max, dx_max + 1):
            sy0, sy1 = max(0, -dy), min(h, h - dy)
            sx0, sx1 = max(0, -dx), min(w, w - dx)
            target[sy0 + dy:sy1 + dy, sx0 + dx:sx1 + dx] |= source[sy0:sy1, sx0:sx1]
    return result


def ellipses(specs, width: int, height: int) -> np.ndarray:
    out = np.zeros((height, width))
    yy, xx = np.mgrid[0:height, 0:width]
    for e in specs:
        (cx, cy), (rx, ry) = e["center"], e["radius"]
        feather = float(e.get("feather", 0.35))
        r = np.hypot((xx - cx) / rx, (yy - cy) / ry)
        w = np.ones_like(r) * (r <= 1) if feather <= 0 else np.clip((1 + feather - r) / feather, 0, 1)
        out = np.maximum(out, w)
    return out


def box(b, width: int, height: int) -> np.ndarray:
    x0, y0, x1, y1 = (int(v) for v in b)
    out = np.zeros((height, width))
    out[max(0, y0):min(height, y1), max(0, x0):min(width, x1)] = 1
    return out


def load_uv(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    groups = data.get("groups")
    if not isinstance(groups, dict):
        raise MaskError(f"{path}: no \"groups\" object (expected the UV export format)")
    return groups


class MaskSet:
    """Builds a recipe's named masks on demand, for one texture."""

    def __init__(self, specs: dict, base: Path, alpha: np.ndarray):
        self.specs = specs or {}
        self.base = base
        self.alpha = alpha
        self.height, self.width = alpha.shape
        self._built: dict[str, np.ndarray] = {}
        self._building: list[str] = []

    def get(self, name: str) -> np.ndarray:
        if name in self._built:
            return self._built[name]
        if name not in self.specs:
            raise MaskError(f"unknown mask {name!r}; the recipe defines: {', '.join(self.specs) or 'none'}")
        if name in self._building:
            raise MaskError("masks refer to each other in a loop: " + " -> ".join(self._building + [name]))
        self._building.append(name)
        try:
            mask = self.build(self.specs[name], name)
        finally:
            self._building.pop()
        self._built[name] = mask
        return mask

    def build(self, spec: dict, name: str = "<mask>") -> np.ndarray:
        sources = [k for k in SOURCES if k in spec]
        if len(sources) != 1:
            raise MaskError(f"mask {name!r} needs exactly one of {', '.join(SOURCES)}; it has {sources or 'none'}")
        kind = sources[0]
        w, h = self.width, self.height
        if kind == "uv":
            groups = load_uv(self.base / spec["uv"])
            wanted = spec.get("groups") or list(groups)
            missing = [g for g in wanted if g not in groups]
            if missing:
                raise MaskError(f"mask {name!r}: {spec['uv']} has no group(s) {missing}; it has {list(groups)}")
            tris = [t for g in wanted for t in groups[g]]
            mask = rasterize(tris, w, h).astype(float)
        elif kind == "image":
            mask = read_gray(self.base / spec["image"])
            if mask.shape != (h, w):
                raise MaskError(f"mask {name!r}: {spec['image']} is {mask.shape[1]}x{mask.shape[0]}, "
                                f"the texture is {w}x{h}")
        elif kind == "box":
            mask = box(spec["box"], w, h)
        elif kind == "ellipses":
            mask = ellipses(spec["ellipses"], w, h)
        elif kind == "union":
            mask = np.maximum.reduce([self.get(n) for n in spec["union"]])
        else:
            mask = np.minimum.reduce([self.get(n) for n in spec["intersect"]])

        if spec.get("dilate"):
            mask = dilate_disk(mask > 0, int(spec["dilate"])).astype(float)
        for other in spec.get("minus", []):
            mask = mask * (self.get(other) <= 0)
        if spec.get("within_alpha"):
            mask = mask * (self.alpha > 0)
        return mask
