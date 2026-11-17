"""The steps a recipe runs. Each takes the current image (uint8 RGBA) and returns a new one.

No step changes alpha: island shapes and transparency stay exactly as in the original.
"""

from __future__ import annotations

import math
from pathlib import Path

import numpy as np

from . import color
from .image import read_rgba, resize_rgba


class StepError(ValueError):
    pass


# ---- recolor ----

def selection_weight(lab: np.ndarray, zone: np.ndarray, where, force) -> np.ndarray:
    """How strongly each pixel is changed: the zone, narrowed by colour conditions, plus forced pixels.

    where: a list of conditions; a pixel's weight is its best condition. Each condition multiplies
    ramps [full, zero] on "lightness" (OKLab L, 0..1), "chroma" (OKLab C, about 0..0.33) and
    "hue" (degrees): 1 at `full`, 0 at `zero`.
    """
    if where:
        L = lab[..., 0]
        C = np.hypot(lab[..., 1], lab[..., 2])
        H = np.degrees(np.arctan2(lab[..., 2], lab[..., 1])) % 360
        values = {"lightness": L, "chroma": C, "hue": H}
        w = np.zeros(L.shape)
        for cond in where:
            unknown = set(cond) - set(values)
            if unknown:
                raise StepError(f"unknown condition(s) {sorted(unknown)}; use lightness, chroma, hue")
            cw = np.ones(L.shape)
            for key, (full, zero) in cond.items():
                cw = cw * color.ramp(values[key], full, zero)
            w = np.maximum(w, cw)
        w = np.where(zone > 0, w * zone, 0.0)
    else:
        w = zone.astype(float)
    if force is not None:
        w = np.maximum(w, (force > 0).astype(float))
    return w


def recolor_lab(lab: np.ndarray, weight: np.ndarray, step: dict) -> np.ndarray:
    """Move each pixel toward a new lightness/chroma/hue, by its weight."""
    L, a, b = lab[..., 0], lab[..., 1], lab[..., 2]
    C = np.hypot(a, b)
    H = np.arctan2(b, a)
    new_L = L * float(step.get("lightness_scale", 1.0))
    new_C = np.full_like(C, float(step["chroma"])) if "chroma" in step else C * float(step.get("chroma_scale", 1.0))
    if "hue" in step:
        new_H = np.full_like(H, color.hue_of(step["hue"]))
    else:
        new_H = H + math.radians(float(step.get("hue_rotate", 0.0)))
    new = np.stack([new_L, new_C * np.cos(new_H), new_C * np.sin(new_H)], -1)
    return lab + weight[..., None] * (new - lab)


def recolor(rgba: np.ndarray, step: dict, masks) -> np.ndarray:
    zone = masks.get(step["mask"]) if "mask" in step else np.ones(rgba.shape[:2])
    force = masks.get(step["force"]) if "force" in step else None
    lab = color.rgb8_to_oklab(rgba[..., :3])
    weight = selection_weight(lab, zone, step.get("where"), force)
    changed = weight > 0
    out = rgba.copy()
    out[changed, :3] = color.oklab_to_rgb8(recolor_lab(lab[changed], weight[changed], step))
    return out


# ---- retone ----

def reference_lab(rgba: np.ndarray, ref: dict) -> np.ndarray:
    if "color" in ref:
        return color.srgb_to_oklab(color.parse_hex(ref["color"]))
    if "box" in ref:
        x0, y0, x1, y1 = (int(v) for v in ref["box"])
        pixels = rgba[y0:y1, x0:x1, :3].reshape(-1, 3)
        if not len(pixels):
            raise StepError(f"reference box {ref['box']} is empty")
        return np.median(color.rgb8_to_oklab(pixels), axis=0)
    raise StepError("reference needs \"color\": \"#RRGGBB\" or \"box\": [x0, y0, x1, y1]")


def retone(rgba: np.ndarray, step: dict, masks) -> np.ndarray:
    """Rotate and scale each pixel's OKLab offset from a reference colour (e.g. blush over skin).

    Pixels equal to the reference don't move, so the skin under a soft mask stays as it was.
    """
    if "reference" not in step:
        raise StepError("retone needs a \"reference\" (the colour to measure offsets from)")
    weight = masks.get(step["mask"]) if "mask" in step else np.ones(rgba.shape[:2])
    ref = reference_lab(rgba, step["reference"])
    sel = weight > 0
    lab = color.rgb8_to_oklab(rgba[sel, :3])
    d = lab - ref
    t = math.radians(float(step.get("rotate", 0.0)))
    c, s = math.cos(t), math.sin(t)
    k_c = float(step.get("chroma_scale", 1.0))
    k_l = float(step.get("lightness_scale", 1.0))
    new = np.stack([ref[0] + d[:, 0] * k_l,
                    ref[1] + (d[:, 1] * c - d[:, 2] * s) * k_c,
                    ref[2] + (d[:, 1] * s + d[:, 2] * c) * k_c], -1)
    mixed = lab + weight[sel][:, None] * (new - lab)
    out = rgba.copy()
    out[sel, :3] = color.oklab_to_rgb8(mixed)
    return out


# ---- place ----

def bleed(rgb: np.ndarray, known: np.ndarray, fill: np.ndarray, steps: int) -> np.ndarray:
    """Spread known colours outward into `fill` pixels, one ring per step (average of known neighbours)."""
    rgb = rgb.astype(np.float64)
    known = known.copy()
    for _ in range(steps):
        total = np.zeros_like(rgb)
        count = np.zeros(known.shape)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            shifted_known = np.zeros_like(known)
            shifted_rgb = np.zeros_like(rgb)
            ys = slice(max(dy, 0), known.shape[0] + min(dy, 0))
            yd = slice(max(-dy, 0), known.shape[0] + min(-dy, 0))
            xs = slice(max(dx, 0), known.shape[1] + min(dx, 0))
            xd = slice(max(-dx, 0), known.shape[1] + min(-dx, 0))
            shifted_known[yd, xd] = known[ys, xs]
            shifted_rgb[yd, xd] = rgb[ys, xs]
            total += shifted_rgb * shifted_known[..., None]
            count += shifted_known
        grow = fill & ~known & (count > 0)
        if not grow.any():
            break
        rgb[grow] = total[grow] / count[grow][:, None]
        known = known | grow
    return rgb


def place(rgba: np.ndarray, step: dict, masks, base: Path, regions: dict) -> np.ndarray:
    """Put an image (e.g. from an image model) into a region of the texture.

    The image is scaled to the region's box. Inside the box, the mask (the region's, unless the step
    names one) and the image's own alpha decide how much of it shows. The texture keeps its alpha.
    "bleed": n spreads the placed colours n pixels into transparent padding next to them, so
    mipmaps don't pull in the old colours. "flip": "x" mirrors the image left-right first (one
    generated eye placed on both sides), "y" turns it upside down.
    """
    if "image" not in step:
        raise StepError("place needs an \"image\"")
    region = {}
    if "region" in step:
        if step["region"] not in regions:
            raise StepError(f"unknown region {step['region']!r}; the recipe has: {', '.join(regions) or 'none'} "
                            "(texture-kit crop adds one)")
        region = regions[step["region"]]
    b = step.get("box", region.get("box"))
    if b is None:
        raise StepError("place needs a \"region\" or a \"box\"")
    x0, y0, x1, y1 = (int(v) for v in b)
    h, w = rgba.shape[:2]
    if not (0 <= x0 < x1 <= w and 0 <= y0 < y1 <= h):
        raise StepError(f"box {b} is outside the {w}x{h} texture")

    src = read_rgba(base / step["image"])
    flip = step.get("flip")
    if flip not in (None, "x", "y"):
        raise StepError(f"flip must be \"x\" (mirror left-right) or \"y\" (upside down), got {flip!r}")
    if flip == "x":
        src = src[:, ::-1]
    elif flip == "y":
        src = src[::-1]
    bw, bh = x1 - x0, y1 - y0
    if src.shape[:2] != (bh, bw):
        src = resize_rgba(src, bw, bh)
    mask_name = step.get("mask", region.get("mask"))
    weight = masks.get(mask_name)[y0:y1, x0:x1] if mask_name else np.ones((bh, bw))
    weight = weight * (src[..., 3] / 255)

    out = rgba.copy()
    patch = out[y0:y1, x0:x1, :3].astype(np.float64)
    patch = patch * (1 - weight[..., None]) + src[..., :3] * weight[..., None]
    n = int(step.get("bleed", 0))
    if n > 0:
        placed = weight > 0.5
        padding = rgba[y0:y1, x0:x1, 3] == 0
        patch = bleed(patch, placed, padding, n)
    out[y0:y1, x0:x1, :3] = np.rint(np.clip(patch, 0, 255)).astype(np.uint8)
    return out


def aspect_warning(step: dict, base: Path, regions: dict) -> str | None:
    region = regions.get(step.get("region"), {})
    b = step.get("box", region.get("box"))
    if b is None or "image" not in step:
        return None
    src_h, src_w = read_rgba(base / step["image"]).shape[:2]
    bw, bh = b[2] - b[0], b[3] - b[1]
    if abs((src_w / src_h) / (bw / bh) - 1) > 0.02:
        return (f"{step['image']} is {src_w}x{src_h} but the box is {bw}x{bh}: it gets stretched. "
                "Ask the image model for the crop's aspect ratio.")
    return None
