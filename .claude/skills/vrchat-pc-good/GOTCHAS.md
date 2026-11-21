# Gotchas

Optimization-specific gotchas: AAO's merges and cuts, Meshia, and physics tuning. Each cost real time on the Green build (see SKILL.md), unless marked as coming from another project. General editor, codedom, MA, VRC and lilToon gotchas are in `vrchat-avatar-unity`'s GOTCHAS.md.

## Measuring

- The SDK panel and the scene avatar show **pre-NDMF** numbers, and NDMF preview makes the scene avatar *look* processed. Measure and compare baked clones only. (The preview point comes from avatar-preupload-optimization-check.)
- Inactive objects that an animation can switch on count toward the rank. Inactive objects that nothing switches on are dropped by AAO and don't count.
- Collision checks ≈ Σ over PBs of (transforms × colliders). Remove a collider from one PB's list and its checks go too.

## AAO (Avatar Optimizer 1.9)

**Auto-merging skinned meshes (Trace and Optimize)**

A renderer is only merged if all of these hold:
- it has at least one bone and a root bone;
- light and reflection probes are off, or it has an anchor;
- it carries no extra components.

Renderers are then grouped by root bone, bounds, anchor, shadows and material-property animations; only renderers in the same group merge. Give accessories the same values as the merged mesh, i.e. what the outfit's MA Mesh Settings apply (on Green: root Hips, anchor Chest, bounds centre 0, extents 1).

What else stops a merge:
- **Toggles:** renderers with different toggle animations never merge, and MeshRenderers never do. Turning an accessory's toggle into an MA Shape Changer driving a `Hide` shape makes it mergeable.
- **MMD compatibility** keeps the face (`Body`) out of the merge.
- **Material-swap animations:** a renderer with one (e.g. gloves swapping the body material) doesn't merge.
- **Light-setting animations:** a renderer missing from them (Surin lilToon light setting) doesn't merge, and one whose curves are typed as MeshRenderer after conversion to a skinned mesh doesn't either. `CheckAvatar` on a bake names both. Retarget or copy the curves.

After merging, blendshapes are renamed (`vrchat-avatar-unity` TEST.md).

**Removing PBs and bones**
- Trace and Optimize keeps any PB that has a parameter, even with no weights (a base's tail or ears). Delete those by hand; `CheckAvatar` lists them.
- Its automatic PB merge needs chains with the same parent and identical settings, which is rare. Use MergePhysBone instead.

**MergePhysBone**
- `makeParent=false`: all roots need the same parent.
- `makeParent=true`: the component's object must have no children, and the roots move under it. Put it somewhere that stays as still as the old parent did.
- Every setting that differs between chains needs its override on, or the merge fails.
- Don't use the limitRotation `Fix` mode: it inserts extra bones.
- **End bones:** Trace and Optimize swaps unweighted end bones for an endpoint only on single chains. A merged group of different-length chains keeps them, so transforms jump (220 → 249 on Green). Fix it by tagging unweighted leaves EditorOnly and overriding the endpoint to the group's average leaf offset.
- Merged chains can be grabbed only one at a time.

**MergeBone** is internal: add it by reflection (`vrchat-avatar-unity` GOTCHAS, codedom). It merges a bone into its parent. Every other bone of a hair chain halves its PB transforms.

**RemoveMeshByMask**
- It samples the mask at each vertex UV (nearest pixel) and removes a triangle only when all three samples are white.
- The enum order is Remove Black, Remove White.
- A one-pixel-per-vertex mask breaks if Meshia runs on the same mesh, because Meshia runs before AAO and moves the UVs. Keep Meshia off the masked mesh, or paint the mask by triangle.

**Build order:** MA → Meshia → AAO cuts and merges. Meshia's budget therefore includes triangles that AAO deletes later. Raise a cut renderer's Meshia target by the cut fraction. (From agentic-unity.)

**MergeMaterial** also atlases legacy texture properties lilToon doesn't use (`_BaseMap`, `_BaseColorMap`): texture memory went 28.7 → 34.9 MB on Green. Shrink them with `textureConfigOverrides`.

**MergeSkinnedMesh:** `Initialize(2)` doesn't set a root bone; set it yourself.

## Meshia Mesh Simplification

- `MeshiaMeshSimplifier` with AbsoluteTriangleCount, per renderer.
- It destroys its own component during processing.
- It scans inactive components without checking the enabled flag, so remove a Meshia component rather than deactivating it. (From agentic-unity.)
- A renderer can't be converted (e.g. MeshRenderer → SkinnedMeshRenderer) while its Meshia component exists. Delete Meshia, convert, then add it back.

## Physics tuning seen on Green

- A braid sank into the chest when leaning back: gravity 1, no angle limit, a 2 cm radius on a 2.5 cm braid, and one chest collider. Fixed with torso and upper-chest colliders, a 3 cm radius, gravity 0.6 and a 45° limit. At 90° it flipped back over the shoulder.
- Bangs need limitRotation pointing away from the face (−45° X on Green), or they poke into it.
- An ahoge (gravity 0) can't merge with the bangs, or it collapses.
- A swinging skirt with leg-following constraints still clipped in an MMD dance (legs spread, kick back, twist). Follow-body weights fixed it.
