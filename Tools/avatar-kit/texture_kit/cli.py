"""texture-kit command line. See `uv run Tools/avatar-kit/texture-kit.py --help`."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

from . import recipe as R
from .image import read_rgba, write_mask, write_rgba
from .masks import MaskError, MaskSet, dilate_disk, load_uv, rasterize
from .ops import StepError
from .psd import PSDError

EPILOG = """\
Typical use:
  texture-kit new TextureSources/face/recipe.json --original Assets/Vendor/face.png --output Assets/_USER/Textures/face.png
  (add masks and steps to the recipe)
  texture-kit run TextureSources/face/recipe.json --preview Library/texture-kit
For an image-model redraw:
  texture-kit crop TextureSources/face/recipe.json eyes --mask irises
  (generate eyes.generated.png from eyes.crop.png, add a "place" step with "region": "eyes")
"""


def emit(result, as_json: bool, text: str) -> None:
    print(json.dumps(result, indent=2, ensure_ascii=False) if as_json else text)


def cmd_new(a):
    r = R.new(a.recipe, a.original, a.output)
    emit({"recipe": str(r.path)}, a.json, f"Wrote {r.path}. Add masks and steps, then: texture-kit run {a.recipe}")


def cmd_run(a):
    r = R.load(a.recipe)
    result = R.run(r, a.preview, write=not a.check)
    lines = [f"{'Checked' if a.check else 'Wrote'} {result['output']} ({result['changed_pixels']} pixels changed)"]
    lines += [f"  step {s['step']} {s['op']}: {s['changed_pixels']} pixels" for s in result["steps"]]
    if result.get("preview"):
        lines.append(f"Preview: {result['preview']}")
    lines += [f"Warning: {w}" for w in result["warnings"]]
    emit(result, a.json, "\n".join(lines))


def cmd_mask(a):
    if a.recipe is not None:
        if a.name is None:
            raise R.RecipeError("give the mask's name after the recipe")
        r = R.load(a.recipe)
        alpha = read_rgba(r.original)[..., 3]
        mask = MaskSet(r.masks, r.base, alpha).get(a.name)
    else:
        if a.uv is None or a.size is None:
            raise R.RecipeError("without a recipe, give --uv and --size")
        groups = load_uv(a.uv)
        tris = [t for g in (a.group or list(groups)) for t in groups[g]]
        mask = rasterize(tris, *a.size).astype(float)
        if a.dilate:
            mask = dilate_disk(mask > 0, a.dilate).astype(float)
    write_mask(a.out, mask)
    emit({"out": str(a.out), "pixels": int((mask > 0).sum())}, a.json, f"Wrote {a.out} ({int((mask > 0).sum())} pixels)")


def cmd_crop(a):
    r = R.load(a.recipe)
    result = R.crop(r, a.name, mask=a.mask, box=a.box, whole=a.whole, margin=a.margin,
                    square=not a.no_square, uv=a.uv, groups=a.group)
    emit(result, a.json,
         f"Region {a.name!r} = {result['box']} ({result['size'][0]}x{result['size'][1]}), saved in the recipe.\n"
         f"Give the image model {result['crop']} (UV overlay: {result['uv_overlay']}),\n"
         f"save its image next to the recipe, then add a step: "
         f'{{"op": "place", "image": "<file>", "region": "{a.name}", "source": {{...}}}}')


def cmd_apply(a):
    step = json.loads(a.step)
    rgba = read_rgba(a.input)
    named = dict(m.split("=", 1) for m in a.mask)
    specs = {n: {"image": str(Path(p).resolve())} for n, p in named.items()}
    recipe = R.Recipe(Path.cwd() / "recipe.json", {"version": 1, "original": str(a.input), "output": str(a.output),
                                                   "masks": specs, "steps": [step]})
    R.validate(recipe)
    masks = MaskSet(specs, Path.cwd(), rgba[..., 3])
    warnings: list[str] = []
    out = R.apply_step(rgba, step, recipe, masks, warnings)
    write_rgba(a.output, out)
    changed = int((out != rgba).any(-1).sum())
    emit({"output": str(a.output), "changed_pixels": changed, "warnings": warnings}, a.json,
         "\n".join([f"Wrote {a.output} ({changed} pixels changed)"] + [f"Warning: {w}" for w in warnings]))


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="texture-kit", description="Rebuild a texture from its untouched original by a recipe.",
                                epilog=EPILOG, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--json", action="store_true", help="print JSON for agents")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("new", help="write an empty recipe")
    s.add_argument("recipe", type=Path)
    s.add_argument("--original", required=True, help="the untouched texture (never written)")
    s.add_argument("--output", required=True, help="where the result goes, e.g. under Assets/_USER")
    s.set_defaults(fn=cmd_new)

    s = sub.add_parser("run", help="rebuild the output from the original")
    s.add_argument("recipe", type=Path)
    s.add_argument("--preview", type=Path, metavar="DIR", help="also write a before/after image here")
    s.add_argument("--check", action="store_true", help="run every step but write no output")
    s.set_defaults(fn=cmd_run)

    s = sub.add_parser("mask", help="write a mask as an image: a recipe's mask by name, or UV triangles")
    s.add_argument("recipe", type=Path, nargs="?")
    s.add_argument("name", nargs="?")
    s.add_argument("--uv", type=Path, help="UV export (JSON) when not using a recipe")
    s.add_argument("--group", action="append", help="UV group to use (repeatable; default: all)")
    s.add_argument("--size", type=int, nargs=2, metavar=("W", "H"))
    s.add_argument("--dilate", type=int, default=0)
    s.add_argument("--out", type=Path, required=True)
    s.set_defaults(fn=cmd_mask)

    s = sub.add_parser("crop", help="cut a region out of the original for an image model; records it in the recipe")
    s.add_argument("recipe", type=Path)
    s.add_argument("name", help="region name, used by a place step")
    how = s.add_mutually_exclusive_group(required=True)
    how.add_argument("--mask", help="the bounding box of this recipe mask (plus --margin)")
    how.add_argument("--box", type=int, nargs=4, metavar=("X0", "Y0", "X1", "Y1"))
    how.add_argument("--whole", action="store_true", help="the whole texture")
    s.add_argument("--margin", type=int, default=16)
    s.add_argument("--no-square", action="store_true", help="keep the mask's aspect instead of a square")
    s.add_argument("--uv", type=Path, help="UV export to draw over the crop")
    s.add_argument("--group", action="append")
    s.set_defaults(fn=cmd_crop)

    s = sub.add_parser("apply", help="run one step on an image, outside a recipe (for trying things out)")
    s.add_argument("input", type=Path)
    s.add_argument("output", type=Path)
    s.add_argument("--step", required=True, help="the step as JSON, e.g. '{\"op\": \"recolor\", \"hue_rotate\": 30}'")
    s.add_argument("--mask", action="append", default=[], metavar="NAME=PNG", help="a mask image the step can name")
    s.set_defaults(fn=cmd_apply)
    return p


def main(argv=None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")
    a = parser().parse_args(argv)
    try:
        a.fn(a)
    except (R.RecipeError, MaskError, StepError, PSDError, OSError, ValueError, KeyError) as e:
        msg = str(e) if not isinstance(e, KeyError) else f"missing field {e}"
        if a.json:
            print(json.dumps({"error": msg}))
        else:
            print(f"texture-kit: {msg}", file=sys.stderr)
        return 1
    return 0
