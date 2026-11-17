"""Recipes: one texture, rebuilt from its untouched original by a list of steps.

recipe.json (paths relative to the recipe's folder):

  {
    "version": 1,
    "original": "../../Assets/Vendor/Textures/face.png",     never written
    "output": "../../Assets/_USER/Textures/face.png",        written by `run`, never read
    "masks":   {"<name>": {mask spec, see masks.py}},
    "regions": {"<name>": {"box": [x0, y0, x1, y1], "mask": "<mask>"}},   added by `crop`
    "steps": [
      {"op": "psd", "file": "makeup.psd", "skip": ["Lips"], "layers": {"Lashes": {recolor fields}}},
      {"op": "retone", "mask": "cheeks", "reference": {"box": [...]} | {"color": "#RRGGBB"},
       "rotate": -22, "chroma_scale": 0.8, "lightness_scale": 0.85},
      {"op": "recolor", "mask": "irises", "hue": "#7A8AA0" | degrees, "hue_rotate": deg,
       "chroma": 0.006 | "chroma_scale": 0.35, "lightness_scale": 0.68,
       "where": [{"lightness": [full, zero], "chroma": [full, zero], "hue": [full, zero]}], "force": "<mask>"},
      {"op": "place", "image": "eyes.generated.png", "region": "eyes", "mask": "<mask>", "bleed": 8, "flip": "x",
       "source": {"model": "...", "prompt": "...", "date": "YYYY-MM-DD"}}
    ]
  }

Every run starts from the original, so an output is never processed again.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from . import RECIPE_VERSION, color, ops
from .image import read_rgba, write_rgba
from .masks import MaskSet, bbox, load_uv
from .psd import apply_psd

OPS = ("psd", "retone", "recolor", "place")
RECOLOR_FIELDS = {"hue", "hue_rotate", "chroma", "chroma_scale", "lightness_scale"}


class RecipeError(ValueError):
    pass


@dataclass
class Recipe:
    path: Path
    data: dict = field(default_factory=dict)

    @property
    def base(self) -> Path:
        return self.path.parent

    @property
    def original(self) -> Path:
        return (self.base / self.data["original"]).resolve()

    @property
    def output(self) -> Path:
        return (self.base / self.data["output"]).resolve()

    @property
    def masks(self) -> dict:
        return self.data.get("masks", {})

    @property
    def regions(self) -> dict:
        return self.data.get("regions", {})

    @property
    def steps(self) -> list:
        return self.data.get("steps", [])

    def save(self) -> None:
        self.path.write_text(json.dumps(self.data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def load(path: Path) -> Recipe:
    path = Path(path).resolve()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise RecipeError(f"can't read {path}: {e}") from None
    recipe = Recipe(path, data)
    validate(recipe)
    return recipe


def step_inputs(recipe: Recipe) -> list[Path]:
    return [(recipe.base / s[k]).resolve() for s in recipe.steps for k in ("image", "file") if k in s]


def validate(recipe: Recipe) -> None:
    d = recipe.data
    if d.get("version") != RECIPE_VERSION:
        raise RecipeError(f"{recipe.path}: \"version\" must be {RECIPE_VERSION}")
    for key in ("original", "output"):
        if not isinstance(d.get(key), str):
            raise RecipeError(f"{recipe.path}: \"{key}\" must be a path")
    if recipe.output == recipe.original:
        raise RecipeError("the output would overwrite the original; write it to another file (e.g. under Assets/_USER)")
    if recipe.output in step_inputs(recipe):
        raise RecipeError("the output is also an input of a step; an output must never be processed again")
    for i, step in enumerate(recipe.steps, 1):
        if step.get("op") not in OPS:
            raise RecipeError(f"step {i}: \"op\" must be one of {', '.join(OPS)}")


def psd_step(rgba, step, recipe: Recipe, warnings):
    def adjuster(params):
        unknown = set(params) - RECOLOR_FIELDS
        if unknown:
            raise RecipeError(f"psd layer adjustment: unknown field(s) {sorted(unknown)}")

        def fn(px):
            lab = color.srgb_to_oklab(px[..., :3].astype(np.float64))
            out = px.copy()
            out[..., :3] = np.clip(color.oklab_to_srgb(ops.recolor_lab(lab, np.ones(lab.shape[:2]), params)), 0, 1)
            return out
        return fn

    adjust = {name: adjuster(p) for name, p in step.get("layers", {}).items()}
    return apply_psd(rgba, recipe.base / step["file"], skip=step.get("skip", ()), adjust=adjust, warnings=warnings)


def apply_step(rgba, step: dict, recipe: Recipe, masks: MaskSet, warnings: list):
    op = step["op"]
    if op == "psd":
        return psd_step(rgba, step, recipe, warnings)
    if op == "retone":
        return ops.retone(rgba, step, masks)
    if op == "recolor":
        return ops.recolor(rgba, step, masks)
    if "source" not in step:
        warnings.append(f"place {step.get('image')}: no \"source\" (model, prompt, date) recorded for this image")
    w = ops.aspect_warning(step, recipe.base, recipe.regions)
    if w:
        warnings.append(w)
    return ops.place(rgba, step, masks, recipe.base, recipe.regions)


def build(recipe: Recipe):
    """Run every step from the original. Returns (original, output, per-step report, warnings)."""
    if not recipe.original.is_file():
        raise RecipeError(f"original not found: {recipe.original}")
    original = read_rgba(recipe.original)
    masks = MaskSet(recipe.masks, recipe.base, original[..., 3])
    warnings: list[str] = []
    report = []
    img = original
    for i, step in enumerate(recipe.steps, 1):
        try:
            new = apply_step(img, step, recipe, masks, warnings)
        except (ValueError, OSError) as e:
            raise RecipeError(f"step {i} ({step['op']}): {e}") from None
        report.append({"step": i, "op": step["op"], "changed_pixels": int((new != img).any(-1).sum())})
        img = new
    if not np.array_equal(img[..., 3], original[..., 3]):  # every step promises this
        raise RecipeError("internal error: alpha changed")
    return original, img, report, warnings


def run(recipe: Recipe, preview_dir: Path | None = None, write: bool = True) -> dict:
    from .preview import before_after

    original, output, report, warnings = build(recipe)
    if write:
        write_rgba(recipe.output, output)
    result = {
        "original": str(recipe.original),
        "output": str(recipe.output),
        "written": write,
        "size": [original.shape[1], original.shape[0]],
        "changed_pixels": int((output != original).any(-1).sum()),
        "steps": report,
        "warnings": warnings,
    }
    if preview_dir is not None:
        result["preview"] = before_after(original, output, preview_dir / f"{recipe.path.parent.name}.before-after.png")
    return result


def square_box(b, width, height):
    x0, y0, x1, y1 = b
    w, h = x1 - x0, y1 - y0
    side = min(max(w, h), width, height)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    nx0 = int(round(min(max(cx - side / 2, 0), width - side)))
    ny0 = int(round(min(max(cy - side / 2, 0), height - side)))
    return [nx0, ny0, nx0 + side, ny0 + side]


def crop(recipe: Recipe, name: str, mask: str | None = None, box=None, whole: bool = False,
         margin: int = 16, square: bool = True, uv: Path | None = None, groups=None) -> dict:
    """Cut a region out of the original for an image model, and record it in the recipe.

    Writes <name>.crop.png (the original's pixels, exactly) and <name>.crop-uv.png (flattened, with the
    UV wireframe drawn over it when UV triangles are known) next to the recipe.
    """
    from .preview import uv_overlay

    if sum(x is not None and x is not False for x in (mask, box, whole or None)) != 1:
        raise RecipeError("crop needs exactly one of a mask, a box, or the whole texture")
    original = read_rgba(recipe.original)
    h, w = original.shape[:2]
    if whole:
        b = [0, 0, w, h]
    elif box is not None:
        b = [int(v) for v in box]
    else:
        masks = MaskSet(recipe.masks, recipe.base, original[..., 3])
        found = bbox(masks.get(mask) > 0)
        if found is None:
            raise RecipeError(f"mask {mask!r} is empty")
        x0, y0, x1, y1 = found
        b = [max(0, x0 - margin), max(0, y0 - margin), min(w, x1 + margin), min(h, y1 + margin)]
        if square:
            b = square_box(b, w, h)
    x0, y0, x1, y1 = b
    if not (0 <= x0 < x1 <= w and 0 <= y0 < y1 <= h):
        raise RecipeError(f"box {b} is outside the {w}x{h} texture")

    region = {"box": b}
    if mask:
        region["mask"] = mask
    recipe.data.setdefault("regions", {})[name] = region
    recipe.save()

    crop_path = recipe.base / f"{name}.crop.png"
    write_rgba(crop_path, original[y0:y1, x0:x1])
    tris = None
    if uv is not None:
        g = load_uv(uv)
        tris = [t for k in (groups or list(g)) for t in g[k]]
    elif mask and "uv" in recipe.masks.get(mask, {}):
        spec = recipe.masks[mask]
        g = load_uv(recipe.base / spec["uv"])
        tris = [t for k in (spec.get("groups") or list(g)) for t in g[k]]
    uv_path = recipe.base / f"{name}.crop-uv.png"
    uv_overlay(original, b, tris, uv_path)
    return {"region": name, "box": b, "size": [x1 - x0, y1 - y0], "crop": str(crop_path), "uv_overlay": str(uv_path)}


def new(path: Path, original: str, output: str) -> Recipe:
    path = Path(path).resolve()
    if path.exists():
        raise RecipeError(f"{path} already exists")
    path.parent.mkdir(parents=True, exist_ok=True)

    def rel(p: str) -> str:
        import os
        return Path(os.path.relpath(Path(p).resolve(), path.parent)).as_posix()

    recipe = Recipe(path, {"version": RECIPE_VERSION, "original": rel(original), "output": rel(output),
                           "masks": {}, "steps": []})
    validate(recipe)
    if not recipe.original.is_file():
        raise RecipeError(f"original not found: {recipe.original}")
    recipe.save()
    return recipe
