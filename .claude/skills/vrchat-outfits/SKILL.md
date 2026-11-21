---
name: vrchat-outfits
description: Put an outfit on a VRChat avatar with Modular Avatar and clean up after it — clothes, shoes, hats, gloves; body shrink keys and hidden skin, outfit variants and toggles, PhysBones and colliders, colours and materials. Use when the user wants to wear, swap, mix or toggle clothes (换衣服, 衣装), or when skin, underwear or hair shows through or clips after an outfit change.
---

# Outfits

Builds on **`vrchat-avatar-unity`**: load it first for baking, renders and tests, and read its [MENUS.md](../vrchat-avatar-unity/MENUS.md) before making any toggle. Picking the outfit is `booth-research`; recolouring it is `vrchat-textures`; fitting it into a performance rank is `vrchat-pc-good`. Accessories (pins, glasses, a brooch) and their gimmicks are `vrchat-accessories`; hats and gloves stay here.

Putting an outfit on with Modular Avatar takes minutes. The time goes into what comes after: skin and underwear showing through, layers that only work in one combination, PhysBones of parts that are hidden, per-renderer tools that don't know the new meshes. Plan for that part.

## Steps

1. **Settle the plan** with the user:
   - **Which avatar:** a new look is usually a new avatar: its own branch from a tag, its blueprint ID cleared (AGENTS.md). Changing the main look in place is the exception.
   - **What stays:** hair, face, accessories, earlier gimmicks. Anything built for the old outfit (a mask of the hidden body, follow-body weights, an accessory skinned to the old clothes) has to be redone or moved.
   - **The outfit's version for this base.** Booth outfits ship per base avatar. One made for another base needs refitting, which is a different job; say so before buying.
   - **Variants and toggles:** colours, worn/draped/off states, optional parts (hat, gloves, coat). Each toggle costs parameters, and often a mesh merge.
   - **A performance target:** ask once. If the user doesn't care about the rank, don't bring triangle counts up again unless something breaks.
2. **Import.** The vendor package is read-only (AGENTS.md). Look inside a `.unitypackage` before importing it over existing assets: vendors reuse GUIDs (`vrchat-avatar-unity` GOTCHAS). Ask whether the outfit's files go into Git; purchased files never go to a public remote.
3. **Wear it.** Use the vendor's prefab for this base, which normally comes set up for MA (Merge Armature). For a bare outfit, MA's Setup Outfit on the outfit root does the same. Remove the old outfit, together with its toggles, menu items and install targets.
4. **Fit the body** (render after each change, arms down, front, back and side):
   - **Shrink keys:** the base's own `Shrink_*` / hide keys under tight or long clothes, and the outfit's own keys for layering (an inner shirt's key for a coat on top, a hoodie's key under a coat). The outfit's notes or images usually list them.
   - **Body-shape keys the outfit was made for:** e.g. a breast-size variant of the outfit needs the matching key on the body.
   - **Underwear** the outfit always covers: remove it from the prefab instance or tag it EditorOnly. Underwear that shows when a layer is toggled off stays, or gets its own handling in that toggle.
   - **Skin that still pokes through** at joints in poses: more shrink, or remove the hidden body triangles (AAO Remove Mesh By Mask; `vrchat-pc-good` has a mask generator).
5. **Variants and toggles** ([MENUS.md](../vrchat-avatar-unity/MENUS.md)):
   - **One prefab, several states.** Vendor "A/B" prefabs often differ only in which mesh is on and a few shape keys. Keep one, and switch the states with one int parameter: per state an Object Toggle plus a Shape Changer (Set) for the keys.
   - **Layers:** when an outer layer can come off, check what is under it in every state: the inner layer's shrink keys, underwear, the body's shrink keys.
   - **Parts that fight:** an ahoge through a hat, a braid through a hood. Toggle them together (hat on ⇒ ahoge off).
   - **Hidden parts' PhysBones** must be off while hidden: keep their PhysBone objects under the toggled part, or toggle them too.
6. **Physics.** The outfit's PhysBones and colliders: no duplicates of the body's or the hair's colliders; long coats and skirts get leg colliders; hair now resting on a collar or hood may need colliders on that. Pose-test legs and torso (`vrchat-avatar-unity` TEST.md).
7. **Materials and colour.**
   - Outfits often ship one material per part and colour. Parts can be mixed; render the choices side by side, because names lie (a "black B" can render pale gray-blue).
   - lilToon outlines of the body can show through or between new parts (a pink body outline between glove fingers). An outline mask on a copy of the body material fixes it; if only some states need it, swap the material in that toggle (MA Material Setter). A material swap stops AAO from merging that mesh.
   - Tools that write per-renderer animations (light-setting gimmicks, material curves) have to be rerun or updated for the new renderers; removed renderers must leave no curves behind.
   - Recolouring a texture: `vrchat-textures`.
8. **Check** on a bake before calling it done:
   - **Body under the outfit:** shrink keys set, nothing poking through in pose tests, covered underwear gone, toggled underwear still toggled.
   - **Toggles:** each one beside what it switches, Bone Proxies on the toggled roots; `ReportMenu` with `find` on the bake (`vrchat-avatar-unity` BAKE.md) shows every toggle driving what it should; every state rendered.
   - **The outfit's own components:** its Shape Changers and Material Setters target paths that exist on this avatar (outfits made for another base can point at nothing); its PhysBones and colliders don't duplicate the hair's or the body's.
   - **Renderers:** new ones are covered by the avatar's per-renderer animations; their MA Mesh Settings match the rest, so meshes still merge; removed or renamed ones leave no dangling curves.
   - **Derived assets** regenerated: a hidden-body mask, follow-body weights.
   - **Looks:** front, back, side and seam close-ups, arms down; sitting and leg poses for anything below the waist.
9. **Record** in `PROJECT.md`: the outfit, its source link, the branch, the toggles. Hand over to the in-game test: physics, dancing (an MMD world), how the toggles feel.

## From real outfits

Worked examples (a MANUKA base, October 2026):

- **Coat worn / draped / off.** The vendor shipped two prefabs (coat worn, coat over the shoulders). They differed only in the active coat mesh and three shirt shape keys, and each coat's PhysBones were separate objects. One prefab plus an int parameter (0/1/2, saved) with an Object Toggle and a Shape Changer per state replaced both, and the hidden coat's PhysBones were switched off with it.
- **Underwear through the trousers.** With the coat off, the base's panty showed through the suit. It was removed from the prefab instance, and the body's hip-to-calf shrink keys set to 100.
- **Gloves.** The body's pale outline showed between the glove fingers. A copy of the body material with the outfit's glove outline mask fixed it, swapped in only while the gloves are on, because bare hands lost their finger lines with the mask. The swap cost the body its mesh merge.
- **Hat and ahoge.** The hair's ahoge poked through the hat. A "Hat OFF" toggle turns the hat off and the ahoge on; the ahoge is off in the saved state.
- **Toggles inside their target.** A toggle placed inside the object it hides switched itself off and never hid it. It moved beside the object (MENUS.md).
