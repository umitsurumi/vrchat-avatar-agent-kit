"""A minimal PSD writer for test files: 8-bit RGB, raw channels, groups, hidden layers, layer masks.

Layers are given bottom to top, like the PSD file stores them:
  Layer(name, rgba HxWx4 uint8, top, left, blend="norm", opacity=255, hidden=False, mask=None)
  Group(name, [children], blend="pass" | "norm" | ..., opacity=255, hidden=False)
mask: (uint8 HxW, top, left, default_color) for a layer mask.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np


@dataclass
class Layer:
    name: str
    rgba: np.ndarray
    top: int = 0
    left: int = 0
    blend: str = "norm"
    opacity: int = 255
    hidden: bool = False
    mask: tuple | None = None
    clipping: bool = False
    fill: int = 255


@dataclass
class Group:
    name: str
    children: list = field(default_factory=list)
    blend: str = "pass"
    opacity: int = 255
    hidden: bool = False


def pascal(name: str) -> bytes:
    raw = name.encode("latin1")[:255]
    data = bytes([len(raw)]) + raw
    return data + b"\0" * (-len(data) % 4)


def block(key: str, data: bytes) -> bytes:
    return b"8BIM" + key.encode("latin1") + struct.pack(">I", len(data)) + data + b"\0" * (len(data) % 2)


def record(name, rect, channels, blend, opacity, hidden, extra_blocks=b"", mask=None, clipping=False):
    """channels: [(id, bytes incl. 2-byte compression)]."""
    t, l, b, r = rect
    out = struct.pack(">iiiiH", t, l, b, r, len(channels))
    for cid, data in channels:
        out += struct.pack(">hI", cid, len(data))
    out += b"8BIM" + blend.encode("latin1") + bytes([opacity, 1 if clipping else 0, 2 if hidden else 0, 0])
    if mask is None:
        mask_data = struct.pack(">I", 0)
    else:
        m, mt, ml, default = mask
        mask_data = struct.pack(">IiiiiBBH", 20, mt, ml, mt + m.shape[0], ml + m.shape[1], default, 0, 0)
    extra = mask_data + struct.pack(">I", 0) + pascal(name) + extra_blocks
    return out + struct.pack(">I", len(extra)) + extra


def raw(plane: np.ndarray) -> bytes:
    return struct.pack(">H", 0) + np.ascontiguousarray(plane, np.uint8).tobytes()


def flatten(items, records, data):
    for item in items:
        if isinstance(item, Group):
            empty = [(cid, struct.pack(">H", 0)) for cid in (-1, 0, 1, 2)]
            records.append(record("</Layer group>", (0, 0, 0, 0), empty, "norm", 255, False,
                                  block("lsct", struct.pack(">I", 3))))
            data.extend(d for _, d in empty)
            flatten(item.children, records, data)
            records.append(record(item.name, (0, 0, 0, 0), empty, item.blend, item.opacity, item.hidden,
                                  block("lsct", struct.pack(">I", 1) + b"8BIM" + item.blend.encode("latin1"))))
            data.extend(d for _, d in empty)
        else:
            h, w = item.rgba.shape[:2]
            chans = [(-1, raw(item.rgba[..., 3])), (0, raw(item.rgba[..., 0])),
                     (1, raw(item.rgba[..., 1])), (2, raw(item.rgba[..., 2]))]
            if item.mask is not None:
                chans.append((-2, raw(item.mask[0])))
            records.append(record(item.name, (item.top, item.left, item.top + h, item.left + w), chans,
                                  item.blend, item.opacity, item.hidden,
                                  block("iOpa", bytes([item.fill, 0, 0, 0])) if item.fill != 255 else b"",
                                  mask=item.mask, clipping=item.clipping))
            data.extend(d for _, d in chans)


def write_psd(path: Path, width: int, height: int, layers) -> None:
    records, data = [], []
    flatten(layers, records, data)
    info = struct.pack(">h", len(records)) + b"".join(records) + b"".join(data)
    info += b"\0" * (len(info) % 2)
    layer_info = struct.pack(">I", len(info)) + info
    layer_and_mask = layer_info + struct.pack(">I", 0)
    header = b"8BPS" + struct.pack(">H6sHIIHH", 1, b"\0" * 6, 3, height, width, 8, 3)
    merged = struct.pack(">H", 0) + bytes(3 * width * height)
    Path(path).write_bytes(header + struct.pack(">I", 0) + struct.pack(">I", 0)
                           + struct.pack(">I", len(layer_and_mask)) + layer_and_mask + merged)
