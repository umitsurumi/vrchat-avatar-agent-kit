# Redrawing with an image model

For a change colour math can't make: a new iris design, a pattern, an emblem, a print. The image model draws a region; the recipe places it into the texture. The kit never calls an image model itself. Use whatever the agent can reach (an image-generation tool, MCP for Unity's image generation if it is set up), or the user generates the image on a website and saves it into the recipe folder.

## The rule

**Every generation starts from the original crop.** Never give the model its own earlier output to "fix": each pass adds the model's artifacts (smudged detail, drifting lines, shading that wasn't there), and they pile up. To get a better result, change the prompt and generate again from the same crop. Once the user accepts an image, it becomes a fixed input of the recipe, and the recipe never regenerates it.

## Steps

1. **Decide the region.** A mask that covers the part (the iris islands from a UV export, an ellipse, a mask image), or `--whole` when the texture is only that part (many avatars have a separate eye texture). Smaller regions get more of the model's resolution.
2. **Crop:**

   ```sh
   uv run Tools/avatar-kit/texture-kit.py crop TextureSources/<texture>/recipe.json eyes --mask irises
   ```

   This writes `eyes.crop.png` (the original's pixels, exactly), `eyes.crop-uv.png` (the same with the UV wireframe on top: where the mesh actually samples the texture), and records the region's box in the recipe. The box is square by default, since image models work best with square images; `--no-square` keeps the mask's shape.
3. **Generate.** Give the model `eyes.crop.png` as the reference image, and say:
   - what to change, and what must stay: the same layout, the same size and position of the iris and pupil, the same highlight positions, the original's drawing style and line weight;
   - flat, unlit colours: no lighting, glare or shadows the original doesn't have (the shader adds lighting);
   - no background, border or frame; the same aspect ratio as the crop (square).

   Save the result next to the recipe, numbered (`eyes.gen-01.png`, `eyes.gen-02.png`, …), and keep the prompt.
4. **Check before placing.** Compare the generated image with `eyes.crop-uv.png`: is the iris where the mesh samples it, the same size, centred the same? Drift of a few percent shows on the avatar as a squint or a lazy eye. Regenerate from the crop rather than editing the output.
5. **Place it** with a step that records where the image came from:

   ```json
   {"op": "place", "image": "eyes.gen-03.png", "region": "eyes", "bleed": 8,
    "source": {"model": "<model name>", "prompt": "<the prompt>", "date": "2026-11-18"}}
   ```

   The image is scaled to the region's box. Inside the box, the region's mask (soft or hard) decides what is replaced; the texture's alpha never changes. `bleed` extends the new colours into the transparent padding around the islands, so mipmaps don't pull in the old colours at a distance. `run` warns when a placed image has no `source`, or when it has to be stretched.
6. **Companion maps.** List the material's other textures on the same UV layout (emission, matcap mask, normal map). For each: if the new design keeps the layout (same highlight positions), the original still fits; if not, rebuild it too (its own recipe, often a `place` of a simple derived image), or turn that feature off for the region. Glow from an old emission map on a new iris is the usual giveaway.
7. **Check on the avatar** (`vrchat-avatar-unity` RENDER.md): close-ups and normal distance, and the eyes looking up, down and to the sides (eye bones or eye-look shape keys), and blinking. If moving eyes show the edge of the region, enlarge the region or the bleed and place again.

## Limits

- Image models output about 1024 px. A larger region gets upscaled and looks soft; crop tighter, or accept it.
- Detail that must match the mesh exactly (seams across UV islands, text, symmetric left/right pairs) is hard for a model. For a left/right pair, generate one side and place the same image into the other side's region with `"flip": "x"`; for the rest, redraw by hand.
- A redraw is a derivative of the vendor's texture: it falls under the vendor's licence. Keep it out of public repositories along with the original.
