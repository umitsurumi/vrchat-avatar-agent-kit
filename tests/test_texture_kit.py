from __future__ import annotations

import hashlib
import json
import math
import subprocess
import sys

import numpy as np
import pytest

from conftest import ROOT
from psd_writer import Group, Layer, write_psd

sys.path.insert(0, str(ROOT / "Tools" / "avatar-kit"))

from texture_kit import color, ops  # noqa: E402
from texture_kit import recipe as R  # noqa: E402
from texture_kit.image import read_rgba, write_mask, write_rgba  # noqa: E402
from texture_kit.masks import MaskError, MaskSet, dilate_disk, rasterize  # noqa: E402
from texture_kit.psd import PSDError, apply_psd  # noqa: E402

KIT = ROOT / "Tools" / "avatar-kit" / "texture-kit.py"


def solid(h, w, rgb, a=255):
    out = np.zeros((h, w, 4), np.uint8)
    out[..., :3] = rgb
    out[..., 3] = a
    return out


def lab_of(rgb):
    return color.rgb8_to_oklab(np.array(rgb, np.uint8))


class Masks:
    """A stand-in MaskSet with fixed arrays."""

    def __init__(self, **masks):
        self.masks = masks

    def get(self, name):
        return self.masks[name]


# ---- colour ----

def test_oklab_reference_values():
    # values published with OKLab (Ottosson 2020)
    assert np.allclose(color.srgb_to_oklab(np.array([1.0, 1.0, 1.0])), [1, 0, 0], atol=1e-4)
    assert np.allclose(color.srgb_to_oklab(np.array([1.0, 0.0, 0.0])), [0.627955, 0.224863, 0.125846], atol=1e-4)


def test_oklab_round_trip_is_exact_in_8_bit():
    rgb = np.random.default_rng(1).integers(0, 256, (1000, 3), dtype=np.uint8)
    assert np.array_equal(color.oklab_to_rgb8(color.rgb8_to_oklab(rgb)), rgb)


def test_ramp_works_both_ways():
    x = np.array([0.0, 0.5, 1.0])
    assert np.allclose(color.ramp(x, 0.0, 1.0), [1, 0.5, 0])
    assert np.allclose(color.ramp(x, 1.0, 0.0), [0, 0.5, 1])


def test_gray_has_no_hue():
    with pytest.raises(ValueError):
        color.hue_of("#808080")
    assert math.isclose(color.hue_of(90), math.pi / 2)


# ---- masks ----

def test_rasterize_takes_pixel_centres_inside():
    mask = rasterize([[0, 0, 1, 0, 0, 1]], 4, 4)  # the lower-left half of UV space
    for y in range(4):
        for x in range(4):
            u, v = (x + 0.5) / 4, 1 - (y + 0.5) / 4
            assert mask[y, x] == (u + v <= 1 + 1e-9), (x, y)


def test_rasterize_non_square():
    mask = rasterize([[0, 0, 1, 0, 1, 1], [0, 0, 1, 1, 0, 1]], 8, 2)
    assert mask.shape == (2, 8) and mask.all()


def test_dilate_disk():
    m = np.zeros((9, 9), bool)
    m[4, 4] = True
    assert dilate_disk(m, 1).sum() == 5
    assert dilate_disk(m, 2).sum() == 13


def masks_for(tmp_path, specs, alpha=None):
    alpha = np.full((16, 16), 255, np.uint8) if alpha is None else alpha
    return MaskSet(specs, tmp_path, alpha)


def test_mask_sources_and_modifiers(tmp_path):
    (tmp_path / "uv.json").write_text(json.dumps({"groups": {"left": [[0, 0, 0.5, 0, 0, 1]], "x": []}}))
    write_mask(tmp_path / "m.png", np.eye(16))
    alpha = np.full((16, 16), 255, np.uint8)
    alpha[:, 12:] = 0
    m = masks_for(tmp_path, {
        "uv": {"uv": "uv.json", "groups": ["left"]},
        "img": {"image": "m.png"},
        "box": {"box": [0, 0, 8, 8]},
        "ell": {"ellipses": [{"center": [8, 8], "radius": [4, 4], "feather": 0.5}]},
        "both": {"intersect": ["box", "img"]},
        "grown": {"box": [7, 7, 8, 8], "dilate": 2, "minus": ["img"]},
        "shown": {"box": [0, 0, 16, 16], "within_alpha": True},
    }, alpha)
    assert m.get("uv")[15, 0] == 1 and m.get("uv")[0, 15] == 0
    assert m.get("img")[3, 3] == 1 and m.get("img")[3, 4] == 0
    assert m.get("both").sum() == 8
    assert m.get("ell")[8, 8] == 1 and m.get("ell")[8, 14] == 0 and 0 < m.get("ell")[8, 13] < 1
    assert m.get("grown").sum() == 13 - 3  # the disk around (7, 7) minus its diagonal
    assert m.get("shown")[:, 12:].sum() == 0 and m.get("shown")[:, :12].all()


def test_mask_errors(tmp_path):
    write_mask(tmp_path / "small.png", np.ones((4, 4)))
    m = masks_for(tmp_path, {"a": {"union": ["b"]}, "b": {"union": ["a"]}, "two": {"box": [0, 0, 1, 1], "image": "x"},
                             "small": {"image": "small.png"}})
    with pytest.raises(MaskError, match="loop"):
        m.get("a")
    with pytest.raises(MaskError, match="exactly one"):
        m.get("two")
    with pytest.raises(MaskError, match="4x4"):
        m.get("small")
    with pytest.raises(MaskError, match="unknown mask"):
        m.get("nope")


# ---- recolor / retone ----

def test_recolor_sets_hue_and_keeps_lightness():
    img = solid(2, 2, (200, 60, 60))
    out = ops.recolor(img, {"hue": "#4060C0"}, Masks())
    before, after = lab_of(img[0, 0, :3]), lab_of(out[0, 0, :3])
    assert abs(after[0] - before[0]) < 0.01
    assert abs(math.atan2(after[2], after[1]) - color.hue_of("#4060C0")) < 0.05
    assert np.array_equal(out[..., 3], img[..., 3])


def test_recolor_chroma_scale_matches_the_eye_recolor():
    # the formula the original eye script used: keep L, chroma * k, hue := reference hue
    rgb = np.random.default_rng(2).integers(0, 256, (200, 3), dtype=np.uint8)
    img = np.concatenate([rgb[None], np.full((1, 200, 1), 255, np.uint8)], -1)
    out = ops.recolor(img, {"chroma_scale": 0.35, "hue": "#7A8AA0"}, Masks())
    lab = color.rgb8_to_oklab(rgb)
    chroma = np.hypot(lab[:, 1], lab[:, 2]) * 0.35
    h = color.hue_of("#7A8AA0")
    expected = color.oklab_to_rgb8(np.stack([lab[:, 0], chroma * np.cos(h), chroma * np.sin(h)], -1))
    assert np.array_equal(out[0, :, :3], expected)


def test_recolor_where_picks_dark_low_chroma_pixels_and_force_adds():
    img = solid(1, 3, (0, 0, 0))
    img[0, 0, :3] = (70, 60, 55)     # dark brown: a lash
    img[0, 1, :3] = (240, 200, 190)  # light pink skin
    img[0, 2, :3] = (240, 200, 190)  # skin, but forced
    zone = np.ones((1, 3))
    force = np.array([[0, 0, 1.0]])
    step = {"mask": "zone", "force": "core", "lightness_scale": 0.7, "chroma": 0.0,
            "where": [{"lightness": [0.74, 0.86], "chroma": [0.045, 0.075]}]}
    out = ops.recolor(img, step, Masks(zone=zone, core=force))
    assert not np.array_equal(out[0, 0], img[0, 0])
    assert np.array_equal(out[0, 1], img[0, 1])
    assert not np.array_equal(out[0, 2], img[0, 2])
    assert np.hypot(*lab_of(out[0, 0, :3])[1:]) < 0.01  # grayed


def test_retone_leaves_the_reference_colour_alone():
    img = solid(1, 2, (230, 190, 180))
    img[0, 1, :3] = (240, 150, 150)  # blush
    step = {"reference": {"box": [0, 0, 1, 1]}, "rotate": -22, "chroma_scale": 0.8, "lightness_scale": 0.85}
    out = ops.retone(img, step, Masks())
    assert np.array_equal(out[0, 0], img[0, 0])
    ref, before, after = lab_of(img[0, 0, :3]), lab_of(img[0, 1, :3]), lab_of(out[0, 1, :3])
    d0, d1 = before[1:] - ref[1:], after[1:] - ref[1:]
    angle = math.degrees(math.atan2(d1[1], d1[0]) - math.atan2(d0[1], d0[0]))
    assert abs(angle + 22) < 2 and abs(np.hypot(*d1) / np.hypot(*d0) - 0.8) < 0.05


def test_retone_needs_a_reference():
    with pytest.raises(ops.StepError):
        ops.retone(solid(1, 1, (1, 2, 3)), {}, Masks())


# ---- place ----

def test_place_scales_into_the_box_and_keeps_alpha(tmp_path):
    img = solid(8, 8, (10, 10, 10))
    img[0, 0, 3] = 0
    write_rgba(tmp_path / "gen.png", solid(2, 2, (250, 0, 0)))
    out = ops.place(img, {"image": "gen.png", "box": [0, 0, 4, 4]}, Masks(), tmp_path, {})
    assert (out[:4, :4, :3] == (250, 0, 0)).all() and (out[4:, :, :3] == 10).all()
    assert np.array_equal(out[..., 3], img[..., 3])


def test_place_uses_the_regions_mask_and_bleeds_into_padding(tmp_path):
    img = solid(4, 8, (10, 10, 10))
    img[:, 6:, 3] = 0                       # padding on the right
    mask = np.zeros((4, 8))
    mask[:, 2:6] = 1
    write_rgba(tmp_path / "gen.png", solid(4, 8, (0, 200, 0)))
    regions = {"r": {"box": [0, 0, 8, 4], "mask": "m"}}
    out = ops.place(img, {"image": "gen.png", "region": "r", "bleed": 1}, Masks(m=mask), tmp_path, regions)
    assert (out[:, 2:6, :3] == (0, 200, 0)).all()
    assert (out[:, :2, :3] == 10).all()           # outside the mask, not padding: untouched
    assert (out[:, 6, :3] == (0, 200, 0)).all()   # one pixel of bleed into the padding
    assert (out[:, 7, :3] == 10).all()


def test_place_flip(tmp_path):
    gen = solid(1, 2, (0, 0, 0))
    gen[0, 0, :3] = (255, 0, 0)  # red on the left
    write_rgba(tmp_path / "gen.png", gen)
    out = ops.place(solid(1, 2, (9, 9, 9)), {"image": "gen.png", "box": [0, 0, 2, 1], "flip": "x"}, Masks(), tmp_path, {})
    assert tuple(out[0, 1, :3]) == (255, 0, 0) and tuple(out[0, 0, :3]) == (0, 0, 0)
    with pytest.raises(ops.StepError, match="flip"):
        ops.place(solid(1, 2, (9, 9, 9)), {"image": "gen.png", "box": [0, 0, 2, 1], "flip": "z"}, Masks(), tmp_path, {})


def test_place_errors(tmp_path):
    with pytest.raises(ops.StepError, match="region"):
        ops.place(solid(2, 2, (0, 0, 0)), {"image": "x.png", "region": "eyes"}, Masks(), tmp_path, {})
    write_rgba(tmp_path / "x.png", solid(2, 2, (0, 0, 0)))
    with pytest.raises(ops.StepError, match="outside"):
        ops.place(solid(2, 2, (0, 0, 0)), {"image": "x.png", "box": [0, 0, 3, 3]}, Masks(), tmp_path, {})


# ---- PSD ----

def psd_result(tmp_path, base, layers, **kw):
    h, w = base.shape[:2]
    write_psd(tmp_path / "m.psd", w, h, layers)
    return apply_psd(base, tmp_path / "m.psd", **kw)


@pytest.mark.parametrize("blend, layer_rgb, opacity, expected", [
    ("norm", 200, 255, 200),
    ("norm", 200, 128, round(128 + (200 - 128) * 128 / 255)),
    ("mul ", 128, 255, round(128 / 255 * 128 / 255 * 255)),
    ("scrn", 128, 255, round((1 - (1 - 128 / 255) ** 2) * 255)),
    ("over", 204, 255, round((1 - 2 * (1 - 128 / 255) * (1 - 204 / 255)) * 255)),  # base just above 0.5: screen half
    ("lddg", 77, 255, 128 + 77),
    ("lddg", 77, 128, round(128 + 77 * 128 / 255)),
])
def test_blend_modes(tmp_path, blend, layer_rgb, opacity, expected):
    base = solid(2, 2, (128, 128, 128))
    out = psd_result(tmp_path, base, [Layer("L", solid(2, 2, (layer_rgb,) * 3), blend=blend, opacity=opacity)])
    assert abs(int(out[0, 0, 0]) - expected) <= 1, (blend, out[0, 0, 0], expected)


def test_layers_go_bottom_to_top_and_respect_offsets(tmp_path):
    base = solid(4, 4, (0, 0, 0))
    out = psd_result(tmp_path, base, [Layer("red", solid(4, 4, (255, 0, 0))),
                                      Layer("blue", solid(2, 2, (0, 0, 255)), top=2, left=2)])
    assert tuple(out[0, 0, :3]) == (255, 0, 0) and tuple(out[3, 3, :3]) == (0, 0, 255)


def test_hidden_skipped_and_adjusted_layers(tmp_path):
    base = solid(2, 2, (0, 0, 0))
    layers = [Layer("a", solid(2, 2, (255, 0, 0))), Layer("b", solid(2, 2, (0, 255, 0)), hidden=True),
              Group("G", [Layer("c", solid(2, 2, (0, 0, 255)))])]
    assert tuple(psd_result(tmp_path, base, layers)[0, 0, :3]) == (0, 0, 255)
    assert tuple(psd_result(tmp_path, base, layers, skip=["G/c"])[0, 0, :3]) == (255, 0, 0)
    assert tuple(psd_result(tmp_path, base, layers, skip=["G"])[0, 0, :3]) == (255, 0, 0)
    gray = psd_result(tmp_path, base, layers, adjust={"G/c": lambda px: np.concatenate(
        [np.full(px.shape[:2] + (3,), 0.5, np.float32), px[..., 3:]], -1)})
    assert tuple(gray[0, 0, :3]) == (128, 128, 128)


def test_isolated_group_vs_pass_through(tmp_path):
    base = solid(1, 1, (128, 128, 128))
    inner = [Layer("white", solid(1, 1, (255, 255, 255))), Layer("mul", solid(1, 1, (128, 128, 128)), blend="mul ")]
    # pass-through: the multiply sees white -> 128
    assert abs(int(psd_result(tmp_path, base, [Group("G", inner, blend="pass")])[0, 0, 0]) - 128) <= 1
    # isolated group in multiply mode: the group is 128, multiplied onto 128 -> 64
    assert abs(int(psd_result(tmp_path, base, [Group("G", inner, blend="mul ")])[0, 0, 0]) - 64) <= 1


def test_layer_mask(tmp_path):
    base = solid(1, 2, (0, 0, 0))
    m = np.array([[255, 0]], np.uint8)
    out = psd_result(tmp_path, base, [Layer("L", solid(1, 2, (255, 255, 255)), mask=(m, 0, 0, 0))])
    assert out[0, 0, 0] == 255 and out[0, 1, 0] == 0


def test_psd_keeps_alpha_and_transparent_pixels(tmp_path):
    base = solid(1, 2, (10, 10, 10))
    base[0, 1, 3] = 0
    out = psd_result(tmp_path, base, [Layer("L", solid(1, 2, (255, 255, 255)))])
    assert out[0, 0, 0] == 255 and tuple(out[0, 1]) == (10, 10, 10, 0)


def test_unsupported_blend_mode_names_the_layer(tmp_path):
    with pytest.raises(PSDError, match="Weird"):
        psd_result(tmp_path, solid(1, 1, (0, 0, 0)), [Layer("Weird", solid(1, 1, (1, 1, 1)), blend="diff")])


def test_clipping_mask_is_refused(tmp_path):
    layers = [Layer("Base", solid(1, 1, (1, 1, 1))), Layer("Clipped", solid(1, 1, (2, 2, 2)), clipping=True)]
    with pytest.raises(PSDError, match="clipping"):
        psd_result(tmp_path, solid(1, 1, (0, 0, 0)), layers)


def test_fill_opacity_counts_like_opacity(tmp_path):
    out = psd_result(tmp_path, solid(1, 1, (0, 0, 0)), [Layer("L", solid(1, 1, (255, 255, 255)), fill=128)])
    assert abs(int(out[0, 0, 0]) - 128) <= 1


def test_psd_size_must_match(tmp_path):
    write_psd(tmp_path / "m.psd", 3, 3, [Layer("L", solid(3, 3, (1, 1, 1)))])
    with pytest.raises(PSDError, match="3x3"):
        apply_psd(solid(2, 2, (0, 0, 0)), tmp_path / "m.psd")


# ---- recipes ----

@pytest.fixture
def project(tmp_path):
    """A texture with a transparent corner, and an empty recipe folder."""
    img = solid(32, 32, (220, 180, 170))
    img[8:16, 8:16, :3] = (90, 60, 50)   # a dark patch
    img[:4, :4, 3] = 0
    write_rgba(tmp_path / "Vendor" / "face.png", img)
    (tmp_path / "TextureSources" / "face").mkdir(parents=True)
    return tmp_path


def make_recipe(project, **extra):
    r = R.new(project / "TextureSources" / "face" / "recipe.json", str(project / "Vendor" / "face.png"),
              str(project / "Assets" / "_USER" / "face.png"))
    r.data.update(extra)
    r.save()
    return R.load(r.path)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_run_rebuilds_from_the_original_every_time(project):
    original = project / "Vendor" / "face.png"
    before = digest(original)
    r = make_recipe(project, masks={"patch": {"box": [8, 8, 16, 16]}},
                    steps=[{"op": "recolor", "mask": "patch", "hue_rotate": 120}])
    first = R.run(r)
    out = project / "Assets" / "_USER" / "face.png"
    assert digest(original) == before
    assert first["changed_pixels"] == 64 and first["steps"][0]["changed_pixels"] == 64
    once = digest(out)
    R.run(r)
    assert digest(out) == once  # same pixels again: the output is never fed back in
    assert np.array_equal(read_rgba(out)[..., 3], read_rgba(original)[..., 3])


def test_recipe_refuses_to_overwrite_or_reuse(project):
    r = make_recipe(project)
    r.data["output"] = r.data["original"]
    with pytest.raises(R.RecipeError, match="overwrite the original"):
        R.validate(r)
    r.data["output"] = "../../Assets/_USER/face.png"
    r.data["steps"] = [{"op": "place", "image": "../../Assets/_USER/face.png", "box": [0, 0, 1, 1]}]
    with pytest.raises(R.RecipeError, match="never be processed again"):
        R.validate(r)
    r.data["steps"] = [{"op": "paint"}]
    with pytest.raises(R.RecipeError, match="op"):
        R.validate(r)


def test_crop_then_place_round_trip(project):
    r = make_recipe(project, masks={"patch": {"box": [8, 8, 16, 16]}})
    info = R.crop(r, "eyes", mask="patch", margin=2)
    assert info["box"] == [6, 6, 18, 18]
    crop = read_rgba(project / "TextureSources" / "face" / "eyes.crop.png")
    assert np.array_equal(crop, read_rgba(project / "Vendor" / "face.png")[6:18, 6:18])
    assert (project / "TextureSources" / "face" / "eyes.crop-uv.png").is_file()
    r = R.load(r.path)
    assert r.regions["eyes"] == {"box": [6, 6, 18, 18], "mask": "patch"}

    write_rgba(project / "TextureSources" / "face" / "eyes.generated.png", solid(12, 12, (0, 0, 250)))
    r.data["steps"] = [{"op": "place", "image": "eyes.generated.png", "region": "eyes"}]
    r.save()
    result = R.run(R.load(r.path))
    out = read_rgba(project / "Assets" / "_USER" / "face.png")
    assert (out[8:16, 8:16, :3] == (0, 0, 250)).all()     # inside the region's mask
    assert (out[6:8, 6:18, :3] == (220, 180, 170)).all()  # in the box, outside the mask
    assert any("source" in w for w in result["warnings"])


def test_crop_whole_and_square(project):
    r = make_recipe(project, masks={"strip": {"box": [0, 10, 32, 12]}})
    assert R.crop(r, "all", whole=True)["box"] == [0, 0, 32, 32]
    box = R.crop(r, "strip", mask="strip", margin=0)["box"]
    assert box[2] - box[0] == box[3] - box[1] == 32


def test_aspect_warning(project):
    write_rgba(project / "TextureSources" / "face" / "wide.png", solid(4, 8, (0, 0, 0)))
    r = make_recipe(project, steps=[{"op": "place", "image": "wide.png", "box": [0, 0, 8, 8], "source": {}}])
    assert any("stretched" in w for w in R.run(r)["warnings"])


def test_psd_step_in_a_recipe(project):
    write_psd(project / "TextureSources" / "face" / "makeup.psd", 32, 32,
              [Group("Lashes", [Layer("Brown", solid(4, 4, (120, 80, 40)), top=20, left=20)])])
    r = make_recipe(project, steps=[{"op": "psd", "file": "makeup.psd",
                                     "layers": {"Lashes/Brown": {"chroma": 0.0}}}])
    R.run(r)
    px = read_rgba(project / "Assets" / "_USER" / "face.png")[21, 21, :3]
    assert np.hypot(*lab_of(px)[1:]) < 0.005  # the brown layer went in grayed


# ---- command line ----

def kit(*args, cwd=None):
    return subprocess.run([sys.executable, str(KIT), *args], capture_output=True, text=True, encoding="utf-8", cwd=cwd)


def test_cli_new_run_crop(project):
    recipe = project / "TextureSources" / "face" / "recipe.json"
    p = kit("new", str(recipe), "--original", str(project / "Vendor" / "face.png"),
            "--output", str(project / "Assets" / "_USER" / "face.png"))
    assert p.returncode == 0, p.stderr
    data = json.loads(recipe.read_text())
    assert data["original"] == "../../Vendor/face.png"
    data["masks"] = {"patch": {"box": [8, 8, 16, 16]}}
    data["steps"] = [{"op": "recolor", "mask": "patch", "chroma_scale": 0}]
    recipe.write_text(json.dumps(data))
    p = kit("--json", "run", str(recipe), "--preview", str(project / "preview"))
    assert p.returncode == 0, p.stderr
    result = json.loads(p.stdout)
    assert result["changed_pixels"] == 64 and (project / "preview" / "face.before-after.png").is_file()
    p = kit("--json", "crop", str(recipe), "eyes", "--box", "0", "0", "8", "8")
    assert json.loads(p.stdout)["box"] == [0, 0, 8, 8]


def test_cli_mask_and_apply(project, tmp_path):
    uv = tmp_path / "uv.json"
    uv.write_text(json.dumps({"groups": {"all": [[0, 0, 1, 0, 0, 1], [1, 0, 1, 1, 0, 1]]}}))
    p = kit("mask", "--uv", str(uv), "--size", "32", "32", "--out", str(tmp_path / "m.png"))
    assert p.returncode == 0, p.stderr
    out = tmp_path / "o.png"
    p = kit("apply", str(project / "Vendor" / "face.png"), str(out), "--step",
            '{"op": "recolor", "mask": "m", "hue_rotate": 90}', "--mask", f"m={tmp_path / 'm.png'}")
    assert p.returncode == 0, p.stderr
    assert (read_rgba(out) != read_rgba(project / "Vendor" / "face.png")).any()


def test_cli_reports_errors(project):
    p = kit("run", str(project / "missing.json"))
    assert p.returncode == 1 and "texture-kit:" in p.stderr


def test_cli_sheet(project, tmp_path):
    a, b = tmp_path / "a.png", tmp_path / "b.png"
    write_rgba(a, solid(100, 50, (255, 0, 0)))
    write_rgba(b, solid(50, 100, (0, 0, 255)))
    p = kit("sheet", str(a), str(b), "--out", str(tmp_path / "s.png"), "--columns", "2", "--cell", "64")
    assert p.returncode == 0, p.stderr
    s = read_rgba(tmp_path / "s.png")
    assert s.shape[1] == 2 * (64 + 12) and (s[..., 0] == 255).any() and (s[..., 2] == 255).any()
