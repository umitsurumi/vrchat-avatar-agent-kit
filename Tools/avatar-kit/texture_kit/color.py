"""sRGB <-> OKLab, colour parsing and soft ramps.

OKLab (Björn Ottosson, 2020) is used for every colour change: its lightness and chroma behave
evenly across hues, so "keep the lightness, change the hue" looks right.
"""

from __future__ import annotations

import math

import numpy as np

M1 = np.array([
    [0.4122214708, 0.5363325363, 0.0514459929],
    [0.2119034982, 0.6806995451, 0.1073969566],
    [0.0883024619, 0.2817188376, 0.6299787005],
])
M2 = np.array([
    [0.2104542553, 0.7936177850, -0.0040720468],
    [1.9779984951, -2.4285922050, 0.4505937099],
    [0.0259040371, 0.7827717662, -0.8086757660],
])
M1_INV = np.linalg.inv(M1)
M2_INV = np.linalg.inv(M2)


def srgb_to_linear(srgb):
    return np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(linear):
    # max() avoids invalid powers in the branch np.where doesn't pick
    return np.where(linear <= 0.0031308, 12.92 * linear, 1.055 * np.maximum(linear, 0) ** (1 / 2.4) - 0.055)


def srgb_to_oklab(srgb):
    """srgb: float array (..., 3) in 0..1."""
    return np.cbrt(srgb_to_linear(srgb) @ M1.T) @ M2.T


def oklab_to_srgb(lab):
    """Returns float sRGB (..., 3); may fall outside 0..1 for out-of-gamut colours."""
    return linear_to_srgb(((lab @ M2_INV.T) ** 3) @ M1_INV.T)


def rgb8_to_oklab(rgb):
    return srgb_to_oklab(np.asarray(rgb, np.float64) / 255)


def oklab_to_rgb8(lab):
    return np.rint(np.clip(oklab_to_srgb(lab), 0, 1) * 255).astype(np.uint8)


def parse_hex(color: str) -> np.ndarray:
    """'#RRGGBB' -> float sRGB in 0..1."""
    s = color.strip().removeprefix("#")
    if len(s) != 6:
        raise ValueError(f"colour must be #RRGGBB, got {color!r}")
    try:
        return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], np.float64) / 255
    except ValueError:
        raise ValueError(f"colour must be #RRGGBB, got {color!r}") from None


def hue_of(value) -> float:
    """OKLab hue in radians, from '#RRGGBB' or a number of degrees."""
    if isinstance(value, (int, float)):
        return math.radians(value)
    lab = srgb_to_oklab(parse_hex(value))
    if math.hypot(lab[1], lab[2]) < 1e-6:
        raise ValueError(f"{value} is a gray and has no hue")
    return float(math.atan2(lab[2], lab[1]))


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0, 1)
    return t * t * (3 - 2 * t)


def ramp(x, full, zero):
    """1 at `full`, 0 at `zero`, smooth in between, flat beyond; works in either direction."""
    return smoothstep(zero, full, x)
