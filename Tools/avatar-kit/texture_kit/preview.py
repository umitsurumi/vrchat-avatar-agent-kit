"""Preview images for checking a result before it goes into Unity."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from .image import fit_within, flatten, write_rgb
from .masks import bbox

GAP = 8
WIRE = (255, 0, 255)


def before_after(original: np.ndarray, output: np.ndarray, path: Path, margin: int = 32,
                 max_side: int = 2048) -> str | None:
    """Original | output side by side, cropped to what changed. None when nothing changed."""
    found = bbox((output != original).any(-1))
    if found is None:
        return None
    h, w = original.shape[:2]
    x0, y0, x1, y1 = found
    x0, y0, x1, y1 = max(0, x0 - margin), max(0, y0 - margin), min(w, x1 + margin), min(h, y1 + margin)
    a, b = flatten(original[y0:y1, x0:x1]), flatten(output[y0:y1, x0:x1])
    gap = np.full((y1 - y0, GAP, 3), 255, np.uint8)
    write_rgb(path, fit_within(np.concatenate([a, gap, b], 1), max_side))
    return str(path)


def uv_overlay(original: np.ndarray, box, tris, path: Path) -> None:
    """The box of the texture, flattened over gray, with UV triangle edges drawn on top."""
    x0, y0, x1, y1 = box
    h, w = original.shape[:2]
    im = Image.fromarray(flatten(original[y0:y1, x0:x1]))
    if tris:
        draw = ImageDraw.Draw(im)
        for t in tris:
            pts = [(t[i] * w - x0, (1 - t[i + 1]) * h - y0) for i in (0, 2, 4)]
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            if max(xs) < 0 or min(xs) > x1 - x0 or max(ys) < 0 or min(ys) > y1 - y0:
                continue
            draw.line(pts + [pts[0]], fill=WIRE, width=1)
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path)
