---
name: vrchat-expressions
description: Shape a VRChat avatar's face and build its expressions — face shaping with shape keys, gesture expressions with FaceEmo, what makeup may touch, and MMD-world compatibility. Use when the user wants to change the face (捏脸), make or fix expressions or gestures (表情), or asks why the face misbehaves in MMD worlds.
---

# Face and expressions

Builds on **`vrchat-avatar-unity`**: load it first, for baking, renders and animator tests.

One face mesh, three writers:
- **Face shaping** sets shape keys on the face renderer in the scene.
- **The expression system** (FaceEmo here) animates them. Its default face rewrites every key it controls, every frame.
- **MMD worlds** animate the MMD-named keys during a dance.

Every decision below is about **ownership**: which writer owns which key. Keep that ownership table in the project's notes. It is the main artifact of this skill, and most face bugs are two writers on one key.

The order is fixed, because each step feeds the next: **shaping → expressions → makeup → MMD check.** FaceEmo takes its default face from the scene's weights when it generates, and makeup choices change what expressions can touch.

## Steps

1. **Inventory the face.** List the face mesh's shape keys and sort each into one group:
   - **shaping:** jaw or eye morphs, pupil and highlight keys;
   - **expression:** eyes, brows, mouth, cheeks;
   - **visemes:** lip sync;
   - **blink;**
   - **MMD:** Japanese-named keys such as あ, まばたき, 笑い;
   - **the base's option keys:** tears, blush, sweat, and so on.

   Then bake and list which FX layers write which keys ([MMD.md](MMD.md) has the snippet). **Done when** every key you plan to touch has exactly one owner.
2. **Direction.** Turn the user's description of the character into concrete traits: eye shape, how the mouth rests, colours, the mood of each expression. Reference images (often generated) are mood, not spec. Ask before copying one literally. Agree on the traits before touching keys.
3. **Face shaping.** Set only keys that no animation writes, as overrides on the face renderer in the scene. A key the expressions also write (e.g. `eye_tare`) shows on the neutral face but is replaced during gestures. Note it, so step 4 carries its value into every expression.
   - Render close-ups of the neutral face: front, three-quarter and side, at a normal viewing distance too.
   - Iterate with the user, then record the final weights.

   **Done when** the user approves the neutral face and the weights are in the notes.
4. **Expressions.** First write a table: gesture and hand → name → keys and values → what it should say. Then build it ([FACEEMO.md](FACEEMO.md)). Rules:
   - each clip writes only the keys it changes; the default face is the scene's weights;
   - every shaping value that an expression key overrides is carried into that expression;
   - MMD-named keys stay out of expression clips;
   - the base's own gesture layers (left and right hand) are removed, so they don't fight the expression system; record what was removed;
   - AFK face, blink, lip sync and the mouth-morph cancel list are set on purpose, not left at defaults.

   **Done when** every row of the table exists as a clip in the menu.
5. **Review.** Render each expression as a face close-up, all on one labelled sheet. The user judges; change one expression at a time and re-render it beside the old one. **Done when** the user has signed off every expression. See [§Reading faces](#reading-faces).
6. **Makeup.** Texture work goes through a recipe (skill `vrchat-textures`), rebuilt from the original every time. What belongs to this skill is what makeup may touch:
   - **Never paint features that move:** lips and lashes painted on the texture don't follow the mouth and eyelid shape keys. On one avatar the painted lips stayed put while the mouth moved, and painted lower lashes showed through closed eyes. In a makeup PSD, skip those layers.
   - Prefer re-toning the base's own blush and marks (`retone`).
   - Makeup done with shape keys interacts with expressions; makeup on the texture doesn't.
7. **MMD check** ([MMD.md](MMD.md)). **Done when** the strategy is chosen with the user and they know what to test in an MMD world.
8. **Apply and test.**
   - Apply FaceEmo, bake, and run the FX by hand (`vrchat-avatar-unity` TEST.md): every gesture, AFK, the menu modes.
   - Any other gimmick that sets the face goes after FaceEmo's layers (MA Merge Animator layer priority), or FaceEmo's default face overwrites it.
   - Re-apply FaceEmo after makeup or shaping changes, so its menu thumbnails match.
   - Then the user tests gestures in game.

## Reading faces

These came up in review rounds on a real avatar:

- **The base face changes what a key means.** On a half-lidded base, heavier lids read as unimpressed or deadpan, not sleepy. "Sleepy" needs nearly closed eyes and a slack mouth.
- **Mouth shapes at full strength read as cartoon.** A pout at slider maximum was too wide; narrower and lower read better.
- **Subtle expressions are several small cues together.** Shy read right as blush, eyes averted and a small smile together. One strong cue alone read as something else.
- **Morphs leak.** An adult-jaw morph re-opened the half-lidded eyes. After any shaping change, check the eyes and the mouth line again.
- **Judge at in-game distance as well as close up.** Small key values vanish at a few metres.
