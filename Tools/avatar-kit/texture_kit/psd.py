"""Composite a layered PSD (e.g. a makeup PSD made for the texture) onto a texture, like Photoshop.

Supported: pixel layers, groups (isolated, or pass-through), visibility, layer and group opacity,
fill opacity, layer masks, and the blend modes normal, multiply, screen, overlay and linear dodge
(add). Anything else (other blend modes, clipping masks, adjustment layers) stops with an error
naming the layer, so a wrong result never goes out silently; hide or skip that layer, or merge it
in an image editor first. Layer effects (shadows, strokes) are ignored with a warning.

The texture keeps its alpha: makeup only changes the colour of pixels that already show.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

BLEND = {
    "NORMAL": "norm",
    "MULTIPLY": "mul ",
    "SCREEN": "scrn",
    "OVERLAY": "over",
    "LINEAR_DODGE": "lddg",
    "PASS_THROUGH": "pass",
}


class PSDError(ValueError):
    pass


def blend_fn(key: str):
    if key == "norm":
        return lambda cb, cs: cs
    if key == "over":
        return lambda cb, cs: np.where(cb <= 0.5, 2 * cb * cs, 1 - 2 * (1 - cb) * (1 - cs))
    if key == "lddg":
        return lambda cb, cs: np.minimum(1, cb + cs)
    if key == "mul ":
        return lambda cb, cs: cb * cs
    if key == "scrn":
        return lambda cb, cs: 1 - (1 - cb) * (1 - cs)
    raise PSDError(f"blend mode {key!r} is not supported")


def composite_onto(canvas: np.ndarray, src: np.ndarray, top: int, left: int, key: str) -> None:
    """W3C separable compositing of src (straight alpha, float 0..1) onto canvas, in place."""
    h, w = src.shape[:2]
    H, W = canvas.shape[:2]
    t0, l0 = max(top, 0), max(left, 0)
    t1, l1 = min(top + h, H), min(left + w, W)
    if t0 >= t1 or l0 >= l1:
        return
    s = src[t0 - top:t1 - top, l0 - left:l1 - left]
    c = canvas[t0:t1, l0:l1]
    cs, a_s = s[..., :3], s[..., 3:]
    cb, a_b = c[..., :3], c[..., 3:]
    mixed = (1 - a_b) * cs + a_b * blend_fn(key)(cb, cs)
    a_o = a_s + a_b * (1 - a_s)
    co = np.where(a_o > 0, (a_s * mixed + a_b * cb * (1 - a_s)) / np.maximum(a_o, 1e-8), 0)
    if key == "lddg":
        # Photoshop adds the alpha-weighted source and clips afterwards
        co = np.where(a_b >= 1, np.minimum(1, cb + a_s * cs), co)
    c[..., :3] = co
    c[..., 3:] = a_o


def blend_key(layer) -> str:
    name = getattr(layer.blend_mode, "name", str(layer.blend_mode))
    if name not in BLEND:
        raise PSDError(f"layer {layer.name!r}: blend mode {name} is not supported "
                       f"(supported: {', '.join(k.lower() for k in BLEND)})")
    return BLEND[name]


def fill_opacity(layer) -> float:
    value = getattr(layer, "fill_opacity", None)
    return 1.0 if value is None else float(value) / 255


def has_visible_clip_layers(layer) -> bool:
    if not hasattr(layer, "has_clip_layers"):
        return False
    try:
        return layer.has_clip_layers(visible=True)
    except TypeError:  # older psd-tools
        return layer.has_clip_layers()


def layer_pixels(layer):
    """(rgba float32 HxWx4 in 0..1 with opacity and mask applied, top, left), or None if empty."""
    left, top, right, bottom = layer.bbox
    if right <= left or bottom <= top:
        return None
    im = layer.topil()
    if im is None:
        return None
    px = np.asarray(im.convert("RGBA"), np.float32) / 255
    # opacity is applied to the 8-bit alpha and rounded back to 8 bits, as image editors store it
    px[..., 3] = np.rint(px[..., 3] * (layer.opacity / 255) * fill_opacity(layer) * 255) / 255
    mask = layer.mask
    if mask is not None and not mask.disabled:
        m = np.full(px.shape[:2], mask.background_color / 255, np.float32)
        ml, mt, mr, mb = mask.bbox
        mim = mask.topil()
        if mim is not None and mr > ml and mb > mt:
            mpx = np.asarray(mim.convert("L"), np.float32) / 255
            # paste the mask's own rectangle into the layer's rectangle
            y0, x0 = max(mt, top), max(ml, left)
            y1, x1 = min(mb, bottom), min(mr, right)
            if y0 < y1 and x0 < x1:
                m[y0 - top:y1 - top, x0 - left:x1 - left] = mpx[y0 - mt:y1 - mt, x0 - ml:x1 - ml]
        px[..., 3] *= m
    return px, top, left


def check_supported(layer, path: str) -> None:
    clipped = layer.clipping if hasattr(layer, "clipping") else getattr(layer, "clipping_layer", False)
    if clipped or has_visible_clip_layers(layer):
        raise PSDError(f"layer {path!r} uses a clipping mask, which is not supported: "
                       "merge it down in an image editor, or skip it")
    if layer.kind == "adjustment" or (layer.kind not in ("pixel", "group", "type", "shape", "smartobject")):
        raise PSDError(f"layer {path!r} is a {layer.kind} layer, which is not supported: "
                       "rasterize or merge it in an image editor, or skip it")


def render(layers, canvas, skip, adjust, warnings, prefix=""):
    for layer in layers:
        path = prefix + layer.name
        if not layer.visible or path in skip or layer.name in skip:
            continue
        check_supported(layer, path)
        if getattr(layer, "has_effects", None) and layer.has_effects() and layer.effects.enabled:
            warnings.append(f"layer {path!r}: layer effects are ignored")
        key = blend_key(layer)
        if layer.is_group():
            if key == "pass":
                if layer.opacity < 255:
                    warnings.append(f"group {path!r}: opacity of a pass-through group is ignored")
                render(layer, canvas, skip, adjust, warnings, path + "/")
            else:
                group = np.zeros_like(canvas)
                render(layer, group, skip, adjust, warnings, path + "/")
                group[..., 3] *= layer.opacity / 255
                composite_onto(canvas, group, 0, 0, key)
            continue
        got = layer_pixels(layer)
        if got is None:
            continue
        px, top, left = got
        fn = adjust.get(path)
        if fn is not None:
            px = fn(px)
        composite_onto(canvas, px, top, left, key)


def apply_psd(rgba: np.ndarray, path: Path, skip=(), adjust=None, warnings=None):
    """rgba: uint8 HxWx4. Returns a new uint8 array; alpha and fully transparent pixels are unchanged.

    skip: layer or group names, or paths ("Group/Layer"), to leave out.
    adjust: {"Group/Layer": f(rgba float 0..1, straight alpha) -> rgba} applied to a layer's pixels.
    """
    from psd_tools import PSDImage
    from psd_tools.constants import ColorMode

    psd = PSDImage.open(path)
    if psd.color_mode != ColorMode.RGB or psd.depth != 8:
        raise PSDError(f"{path}: only 8-bit RGB PSDs are supported (this one is {psd.color_mode.name}, "
                       f"{psd.depth}-bit)")
    if (psd.height, psd.width) != rgba.shape[:2]:
        raise PSDError(f"{path} is {psd.width}x{psd.height}, the texture is {rgba.shape[1]}x{rgba.shape[0]}")
    warnings = [] if warnings is None else warnings
    canvas = rgba.astype(np.float32) / 255
    render(psd, canvas, set(skip), adjust or {}, warnings)
    out = rgba.copy()
    keep = rgba[..., 3] > 0
    out[keep, :3] = np.rint(np.clip(canvas[keep, :3], 0, 1) * 255).astype(np.uint8)
    return out
