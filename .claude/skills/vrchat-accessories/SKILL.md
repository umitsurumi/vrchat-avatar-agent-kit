---
name: vrchat-accessories
description: Add accessories to a VRChat avatar and give them small gimmicks — hairpins, hair ties, glasses, earrings, brooches, ribbons; cutting a bought pack down, attaching with MA Bone Proxy, recolouring through the material, OFF toggles, generated accessories, and reactions to voice, mute, AFK or head pats. Use when the user wants an accessory added, swapped, recoloured, hidden or made to react (饰品, 配件, 发夹, 眼镜, 摸头反应, 小机关), or when a bought accessory brings its own menu and parameters. Hats and gloves are worn like clothes: `vrchat-outfits`.
---

# Accessories

Builds on **`vrchat-avatar-unity`**: load it first for baking, renders and tests, and read its [MENUS.md](../vrchat-avatar-unity/MENUS.md) before making any toggle. Hats and gloves are worn like clothes: `vrchat-outfits`. Picking an item is `booth-research`; changing a texture is `vrchat-textures`; merging accessories into the main mesh for a performance rank is `vrchat-pc-good`.

An accessory has few triangles, but it reaches into everything: a bone to follow, a menu entry and a parameter, a material slot and usually a mesh of its own, the avatar's per-renderer animations. A bought pack brings more than you keep: every colour and variant, its own menu, its own synced parameters. Most of the work is cutting it down and wiring it in.

## Steps

1. **Settle the plan** with the user:
   - **What and where:** the item, which side, on the head, riding on a lock of hair, or on the clothes; a reference picture if there is one.
   - **Colour:** one the pack ships, or a recolour.
   - **Toggle:** always on, or an OFF toggle; saved or not, and its default. Small parts that belong together share one toggle.
   - **Rank:** an accessory that toggles on and off costs a mesh and a material slot (MENUS.md). With a rank target, plan from the start to hide it by shape key instead (`vrchat-pc-good`).
   - **Which avatar:** the branch it goes on. Assets built on one branch come over with `git checkout <branch> -- <path>`; the scene work is redone there (AGENTS.md).
2. **Get it.**
   - **A bought or free pack:** look inside the `.unitypackage` before importing it (vendors reuse GUIDs: `vrchat-avatar-unity` GOTCHAS), read its terms, and ask whether its files go into Git. Purchased files never go to a public remote.
   - **Made from scratch** (glasses, a pin, a badge): a script that writes the mesh, kept in the repo ([§Generated accessories](#generated-accessories)).
3. **Cut a pack down.** Put its prefab in the scene, then keep only what you use. The vendor's files stay untouched:
   - **Variants:** delete the parts and colours you don't use from the instance (unpack it if Unity won't let you).
   - **Its own gimmicks:** a pack's MA Menu Installer, MA Parameters and Merge Animator often bring a whole menu and many synced bits for colours and positions. Remove what you don't keep, then run `ReportMenu` on a bake: the pack's parameters and bits must be gone.
   - **Its PhysBones and contacts:** only those of parts you keep. `CheckAvatar` reports PhysBones that move nothing.
4. **Attach it.**
   - Place it in the scene where it should sit, at the avatar root, and give its root an **MA Bone Proxy** to the bone it follows, attachment mode *As child; keep world pose*. The proxy goes on the object the toggle switches, never on a child (MENUS.md).
   - **The bone:** Head for anything on the head that should stay put: pins, glasses, a clip. A hair bone for something that rides on a swinging lock (a ribbon on a braid): it swings with it.
   - **On the clothes** (a brooch, a badge): the bone the cloth there follows, or, so it bends with the cloth, skin it to the weights of the part it sits on ([§From real accessories](#from-real-accessories)).
   - **The fit:** close-ups with `RenderAvatar` `focus` from the front, the side and three-quarter. Not sunk into the hair, not floating above it. Hair under a pin that follows the Head swings with its PhysBones: pose-test the torso leans in play mode (`Pose`, TEST.md) for hair swinging through the pin or out from under it.
   - **On the face** (glasses): also render the expressions that move the face under it: smiles lift the cheeks, closed eyes move the lids, and either can push the face through the rims or the pads.
5. **Colour and material.**
   - **Read the vendor material first.** Many colour changes need no new texture: lilToon's main colour, or a second or third layer (Main2nd / Main3rd) whose mask already isolates the part. Copy the material into `Assets/_USER/`, change the copy, point the renderer at it.
   - **A pack's colour materials:** render the candidates side by side on the avatar. Names lie.
   - **A texture change:** `vrchat-textures`.
   - **Per-renderer tools** (Surin's lilToon light setting and the like) don't know a new renderer until they are rerun or their animations are updated (`vrchat-avatar-unity` GOTCHAS, lilToon). Without its curves the accessory ignores the avatar's brightness setting, and AAO won't merge it with the rest. A removed accessory's curves have to go too.
6. **Toggle it** (MENUS.md has the rules):
   - One "<Accessory> OFF" per accessory or group, beside the accessory, with the Bone Proxy on the accessory's root.
   - Anything that must keep working while the accessory is hidden (a contact receiver, another part) lives outside it.
   - Parts that can't be on together are one control: a hat and the ahoge it hides.
   - With a rank target: an MA Shape Changer that collapses the accessory by a `Hide_*` shape key, instead of an Object Toggle, so the accessory still merges (`vrchat-pc-good`, `MaterialSlotMerge.cs`).
7. **Reactions** (an accessory that shows when you speak, mute, go AFK or get your head patted): [REACTIONS.md](REACTIONS.md).
8. **Check on a bake:**
   - `CheckAvatar`: toggles, Bone Proxies, dead PhysBones, renderers the light setting misses.
   - `ReportMenu` with `find` for each toggle: it drives the accessory; a cut-down pack left no parameters behind.
   - `ReportPerformance`: what the accessory added (triangles, material slots, meshes), against the plan.
   - Renders: close-ups and a normal-distance view, every toggle state, the expressions for anything on the face.
9. **Record** in `PROJECT.md`: each accessory, its source, its bone, its toggle and default. Hand over to the in-game test as a checklist: how it reads at a distance and in a mirror, each toggle, the hair moving under it.

## Generated accessories

Simple rigid shapes (glasses, a pin, a badge) can be written by a script from measurements of the face. They fit this face better than a model made for another one, and cost exactly the triangles you give them.

- **Measure** the face with the shape keys the avatar keeps (a reshaped face counts): the Head bone, an eye bone, the front of the face at the nose bridge, its half width at eye height. On a baked clone standing at the origin and facing +Z:

  ```csharp
  var clone = GameObject.Find("MyAvatar_bakecheck");
  var anim = clone.GetComponent<Animator>();
  var head = anim.GetBoneTransform(HumanBodyBones.Head).position;
  var eye = anim.GetBoneTransform(HumanBodyBones.RightEye).position;
  var face = clone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
  var mesh = new Mesh(); face.BakeMesh(mesh, true);
  float bridge = float.MinValue, half = 0f;
  foreach (var v in mesh.vertices)
  {
      var p = face.transform.position + face.transform.rotation * v;
      if (Mathf.Abs(p.y - eye.y) < 0.004f) half = Mathf.Max(half, Mathf.Abs(p.x - head.x));
      if (Mathf.Abs(p.x - head.x) < 0.004f && Mathf.Abs(p.y - eye.y) < 0.01f) bridge = Mathf.Max(bridge, p.z);
  }
  UnityEngine.Object.DestroyImmediate(mesh);
  return "head " + head.ToString("F4") + "  eye " + eye.ToString("F4") + "  bridge z " + bridge.ToString("F4") + "  half width " + half.ToString("F4");
  ```

  The half width counts every vertex of the face mesh at that height, so it is usually the side of the head, not the cheek: where glasses' temples run. On MANUKA: head (0, 1.0925, 0.004), eye ±0.0302 at 1.1343, bridge 0.071, half width 0.0785.
- **Write the mesh relative to the Head bone**, in avatar space, so it sits on an object at the Head with no local rotation under a Bone Proxy (keep world pose). One material slot; with a rank target, every vertex's UV on one swatch of a shared palette material (`vrchat-pc-good`).
- **Keep the script in the repo** with its inputs in a config block, and change the numbers, not the mesh: "rounder rims", "a bit wider" is one rerun. Check every rerun on a render.
- **Shapes that need modelling** (bevels, domes, lettering) were done with headless Blender scripts. Before exporting, set shape keys to 0 (Blender writes their current value as the default) and import the FBX as Generic, or it loses its shape keys (`vrchat-avatar-unity` GOTCHAS).

Anime glasses read as glasses only when the front is about as wide as the face: the outer rim near the cheek outline, the top rim just under the brows, the lenses nearly touching at the bridge.

## From real accessories

Worked examples (a MANUKA base, October 2026):

- **A red hair tie without a texture edit.** The braid's tie was already the hair material's Main2nd layer, in multiply mode, under a vendor mask. A copy of the material with that layer's colour at (0.72, 0.15, 0.17) recoloured only the tie.
- **Bobby pins from a free pack.** The pack (pins, a star, a cloud, five colours) came with its own menu and 11 synced bits. The instance was unpacked and cut down to the crossed pins in lavender, its menu and parameters removed, and the Bone Proxy put on the group's root so the OFF toggle hides it. With the proxy on a child at first, the toggle switched an empty shell.
- **Generated glasses**, from face measurements: a thin gunmetal full rim between round and oval, 1,484 triangles, one material, Bone Proxy to Head.
- **A speech-bubble pin that reacts**: [REACTIONS.md](REACTIONS.md).
- **A cat brooch in place of a name tag.** Generated, then skinned with the name tag's averaged weights (chest 0.54, two breast bones 0.29 and 0.17), so it moves like the tag did; the tag became EditorOnly. It took over the tag's light-setting curves, so AAO still merged it.
- **Hat OFF.** The ahoge poked through the front of a hat. "Hat OFF" hides the hat and shows the ahoge; the ahoge is off in the saved state.
- **A toggle inside its accessory.** "Hairpin OFF" inside the pin switched itself off with it, and the pin never hid. It moved beside the pin. On another branch the same layout worked only because the toggle had become a Shape Changer, so the pin was never switched off.
- **Light-setting curves.** Adding or removing an accessory meant regenerating the lilToon light-setting FX each time; the regenerated one had to leave out the curves of removed pins.

## Reference scripts

`scripts/` holds tools from the MANUKA build, lightly generalized, with its values left in as the worked example. Each file opens with what it does, what it assumes, and where it may not fit.

| Script | Does |
|---|---|
| `build_glasses.py` | thin-rim glasses from face measurements: rims, bridge, nose pads, temples; one OBJ relative to the Head bone, one material slot. `uv run` installs numpy |
| `AccessoryReactions.cs` | the reaction FX for an accessory with state shape keys (AFK, head pat, speaking with the volume, muted), an optional face layer after the expression tool, a head-pat receiver with its OFF toggle ([REACTIONS.md](REACTIONS.md)) |
| `AvatarAccessories.Editor.asmdef` | the reference the C# needs: MA core |

How to use them:
- **If the avatar fits the script's assumptions,** copy it (with the asmdef, for the C#) into the project: Python under `Tools/`, C# into an editor folder under `Assets/_USER/`. Fill its config block and run it: the Python with `uv run`, the C# from **Tools/Avatar Accessories** with the avatar selected. Both are rerunnable.
- **If it doesn't fit,** read it for the method and the pitfalls it encodes, then write the project's own version.
- **Either way,** the result is checked by baking and rendering, not by trusting the script.

`build_glasses.py` with its example values writes the MANUKA build's glasses vertex for vertex (only the UV point and the material name are now settings). `AccessoryReactions.cs` ran on a copy of a MANUKA avatar (Unity 2022.3, VRChat SDK 3.10, MA 1.18, FaceEmo, AAO): every state, hold and priority checked on the bake. Both still get checked on your avatar the same way.
