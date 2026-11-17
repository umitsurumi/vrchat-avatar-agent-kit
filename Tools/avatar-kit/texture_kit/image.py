"""Reading and writing images as uint8 RGBA arrays (row 0 = top)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


def read_rgba(path: Path) -> np.ndarray:
    with Image.open(path) as im:
        return np.array(im.convert("RGBA"))


def read_gray(path: Path) -> np.ndarray:
    """A mask image as float 0..1 (white = 1). Alpha, if any, multiplies in."""
    with Image.open(path) as im:
        rgba = np.array(im.convert("RGBA"), np.float64) / 255
    return rgba[..., :3].mean(-1) * rgba[..., 3]


def write_rgba(path: Path, rgba: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.ascontiguousarray(rgba, dtype=np.uint8)).save(path)


def write_rgb(path: Path, rgb: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.ascontiguousarray(rgb, dtype=np.uint8)).save(path)


def write_mask(path: Path, mask: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.rint(np.clip(mask, 0, 1) * 255).astype(np.uint8)).save(path)


def resize_rgba(rgba: np.ndarray, width: int, height: int) -> np.ndarray:
    """Lanczos resize with premultiplied alpha, so transparent pixels don't darken the edges."""
    a = rgba[..., 3:4].astype(np.float64) / 255
    premul = np.concatenate([rgba[..., :3] * a, a * 255], -1)
    channels = [np.array(Image.fromarray(np.ascontiguousarray(premul[..., i], dtype=np.float32)).resize((width, height), Image.Resampling.LANCZOS))
                for i in range(4)]
    out = np.stack(channels, -1)
    alpha = np.clip(out[..., 3:4] / 255, 0, 1)
    rgb = np.where(alpha > 0, out[..., :3] / np.maximum(alpha, 1e-8), 0)
    return np.rint(np.clip(np.concatenate([rgb, alpha * 255], -1), 0, 255)).astype(np.uint8)


def flatten(rgba: np.ndarray, background: int = 128) -> np.ndarray:
    """RGB over a flat gray, for previews."""
    a = rgba[..., 3:4].astype(np.float64) / 255
    return np.rint(rgba[..., :3] * a + background * (1 - a)).astype(np.uint8)


def fit_within(rgb: np.ndarray, max_side: int) -> np.ndarray:
    h, w = rgb.shape[:2]
    scale = max_side / max(h, w)
    if scale >= 1:
        return rgb
    size = (max(1, round(w * scale)), max(1, round(h * scale)))
    return np.array(Image.fromarray(np.ascontiguousarray(rgb, dtype=np.uint8)).resize(size, Image.Resampling.LANCZOS))
