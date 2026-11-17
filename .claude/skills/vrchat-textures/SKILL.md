---
name: vrchat-textures
description: Change a VRChat avatar's textures reproducibly with the kit's texture recipes — recolour eyes, hair or clothes, re-tone blush, gray lashes, apply a makeup PSD, or redraw/regenerate part of a texture with an image model (new eye design, a pattern, an emblem) — then hook the result up in lilToon materials. Use when the user wants a texture's colour or design changed (改色, 换瞳, 妆容, AI 重绘), or asks why an edited texture shows artifacts.
---

# Textures

Builds on **`vrchat-avatar-unity`** (renders, bakes). Face makeup rules (what makeup may touch) are in `vrchat-expressions`.

**Every texture is rebuilt from its untouched original by a recipe.** Editing an output again, by hand or by an image model, piles up artifacts, and after a few rounds no one can say how the texture was made. A recipe is the opposite: `TextureSources/<texture>/recipe.json` names the vendor original, the output under `Assets/_USER/`, and the steps; `run` replays them from the original every time. To change the result, change the recipe and run it again.

The tool: `uv run Tools/avatar-kit/texture-kit.py` (`--help` per command, `--json` for machine-readable output). The recipe format is documented at the top of `Tools/avatar-kit/texture_kit/recipe.py`, the masks in `masks.py`.

| Step | For |
|---|---|
| `recolor` | change lightness, chroma and hue inside a mask (OKLab): iris colour, hair tint, a garment's colour; with `where`, only pixels in a colour range (dark low-chroma lashes, not the pink eyelid under them) |
| `retone` | rotate and scale each pixel's colour offset from a reference colour: blush cooler or softer while the skin it sits on stays exactly as it was |
| `psd` | composite a layered PSD (a makeup or decal PSD made for this texture) like Photoshop; `skip` layers, or recolour single layers on the way in |
| `place` | put an image into a region, e.g. an image model's redraw ([REDRAW.md](REDRAW.md)) |

No step changes alpha, so island shapes and transparency always stay the vendor's.

## Steps

1. **Find the original.** From the renderer's material in Unity: the material's texture properties (`Material.GetTexturePropertyNames()`), and `AssetDatabase.GetAssetPath` of the main texture. Note which other textures of that material share its UV layout (emission, matcap and outline masks): a change to the main texture often needs the same change on them, as its own recipe.
2. **Agree on the change** with the user in words and numbers ("irises blue-gray, about half as saturated"), and decide the mask. Mock-ups are cheap: `texture-kit apply` runs one step outside a recipe.
3. **Start the recipe:**

   ```sh
   uv run Tools/avatar-kit/texture-kit.py new TextureSources/<texture>/recipe.json --original <vendor texture> --output Assets/_USER/Textures/<texture>.png
   ```

4. **Make the masks.** Pick the cheapest that is exact enough:
   - **UV triangles from the mesh** for parts that are a mesh piece: everything a material covers, or the pieces that given shape keys move (lashes, brows, eyelids). Export from Unity, e.g. through MCP:

     ```csharp
     var r = GameObject.Find("MyAvatar/Body").GetComponent<SkinnedMeshRenderer>();
     return AvatarAgentKit.Editor.UvTriangleExport.ExportByShapeKeys(r, 0, "TextureSources/face/face_uv.json",
         new AvatarAgentKit.Editor.UvTriangleExport.Group("lash", "^eyelash_", "\\(white\\)"),
         new AvatarAgentKit.Editor.UvTriangleExport.Group("brow", "^brow_", "^brow_shadow_"));
     ```

     The submesh is the face material's index in `sharedMaterials`. The menu `Tools/Avatar Agent Kit/Export UV Triangles` exports one group per submesh.
   - **Ellipses** for soft areas (cheeks); **boxes**; **mask images** the user paints.
   - **Dilate** UV masks a few pixels so the painted padding around an island changes too; **within_alpha** keeps a mask on visible pixels; **minus** keeps zones apart.

   Write a mask to look at it: `texture-kit mask <recipe> <name> --out <scratch>/m.png`.
5. **Add steps, run, look:**

   ```sh
   uv run Tools/avatar-kit/texture-kit.py run TextureSources/<texture>/recipe.json --preview <scratch folder>
   ```

   View the before/after image. Then render it on the avatar (step 7) before the user judges it: a texture looks different on the model, under its lighting, at in-game distance.
6. **Hook it up in Unity:**
   - **Import settings like the original's:** copy them with a preset, so size, compression, sRGB and mipmaps match:

     ```csharp
     var src = (TextureImporter)AssetImporter.GetAtPath(originalPath);
     var dst = (TextureImporter)AssetImporter.GetAtPath(outputPath);
     new UnityEditor.Presets.Preset(src).ApplyTo(dst); dst.SaveAndReimport();
     ```

     Masks and data textures stay non-sRGB. Keep mipmap streaming on (the SDK's validation asks for it).
   - **A material copy:** copy the vendor material into `Assets/_USER/`, point its texture property at the output, and the renderer's slot at the copy. Never assign the output to the vendor material.
   - Material changes made from a script may need lilToon's own setting refresh (`vrchat-avatar-unity` GOTCHAS, lilToon).
7. **Check on the avatar:** render close-ups and a normal-distance view (`vrchat-avatar-unity` RENDER.md), and anything that moves the textured part: eyes looking around, expressions, blinking.
8. **Commit** the recipe, its inputs (masks, PSDs, generated images, UV exports) and the output together. Inputs are data for reproducing the texture, not Unity assets: that is why they live outside `Assets/`.

## Choosing the step

- **Whole-texture hue or saturation** on a lilToon material: the material's own colour adjustment (HSV/gamma on the main texture) needs no new texture at all. Use a recipe when only part of the texture changes, or when the shift must be baked (atlasing, other shaders).
- **A colour change inside a part** (iris, a garment's trim): `recolor` with a UV or image mask. Set `hue` to a reference colour (`"#7A8AA0"`) and scale chroma rather than painting a flat colour, so the shading stays.
- **Darkening or graying fine detail** (lashes, eyeliner, brows) without touching the skin under it: `recolor` with `where` (lightness and chroma ranges) inside a dilated zone, and `force` on the core pieces.
- **Blush, freckles, a tan:** `retone` against a reference box of plain skin.
- **A makeup PSD** sold for the base: `psd`. Check what it paints first: painted lips and lashes don't follow the mouth and eyelid shape keys (`vrchat-expressions`); `skip` those layers.
- **A new design** (eye pattern, emblem, print): an image model plus `crop` and `place` ([REDRAW.md](REDRAW.md)).

## Worked example: a face, October 2026

A MANUKA face texture (4096²) and its emission map, rebuilt in one recipe each:
- **Irises** blue-gray: `recolor`, `chroma_scale` 0.6, `hue` `#7A8AA0`, on the iris islands dilated 8 px. The same step on the emission map, or the glow keeps the old colour.
- **Lashes and eyeline** dark gray: `recolor`, `lightness_scale` 0.68, `chroma` 0.006 toward the same hue, `where` lightness 0.74→0.86 and chroma 0.045→0.075, plus a second condition for tan highlight streaks (hue 42→36°); zone = lash and eyelid pieces from the UV export dilated 40 px, minus the brow zone; `force` = the lash pieces dilated 2 px. **Brows** the same with lightness 0.56.
- **Blush** cooler: `retone`, `rotate` −22°, `chroma_scale` 0.8, `lightness_scale` 0.85, in two soft ellipses, referenced to a box of plain forehead skin.
- A vendor makeup PSD was tried and dropped: its painted lips stayed still while the mouth moved.
