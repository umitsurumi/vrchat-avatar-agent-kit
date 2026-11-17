---
name: vrchat-pc-good
description: Take a VRChat avatar to PC Good (绿模) with measured, user-approved cuts — triangles, PhysBones, material slots, skinned meshes. Use when the user wants an avatar at PC Good / 绿模 / a better performance rank, asks why it is Poor or Very Poor or whether it can reach Good, wants it lighter or optimized, or plans its PhysBone, triangle or material budget.
---

# VRChat PC Good (绿模)

A Good build is a **budget**: every metric is a line item that has to fit under the Good limit, and the user decides what each part gives up to get there. The work is a loop: **bake, measure, change one thing, bake again.** Judge only a baked clone. The scene avatar and the SDK panel both show pre-NDMF numbers, and NDMF preview makes the scene avatar look already optimized.

This skill builds on **`vrchat-avatar-unity`**: load it first. It covers how to drive the editor, bake and measure ([BAKE.md](../vrchat-avatar-unity/BAKE.md)), render, pose-test and upload. Read this skill's [GOTCHAS.md](GOTCHAS.md) before adding any AAO, Merge PhysBone or Meshia component.

The numbers and examples below come from **the Green build**: a MANUKA base with a sailor outfit and side-braid hair, taken from Very Poor (138k triangles, 58 PhysBones) to Good in Oct 2026 with AAO, MA and Meshia. Treat them as reference points, not rules.

## Ground rules

- **Non-destructive.** Changes are NDMF components (AAO, MA, Meshia) or prefab-instance overrides in the scene. Generated meshes, masks and textures go in the project's own folder, never into vendor assets.
- **The handoff is the record.** A `HANDOFF-<topic>.md` in the project, written in the user's language: numbers, decisions, gotchas, updated after every step. If one exists, read it before measuring anything.
- **One step, one commit**, when the user asks for it. Leave out auto-generated churn (project settings, shader-setting files that builds rewrite).
- **The user may be editing the same scene.** Check `scene.isDirty` before any save. Changes you didn't make → ask first.

## Steps

1. **Baseline.** Bake and measure. Write into the handoff:
   - each metric against Good, with the rank it sets;
   - triangles and materials per renderer after the bake;
   - PhysBone transforms per chain, and colliders;
   - what each menu toggle switches.

   **Done when** every metric over Good has its sources named.
2. **Decisions.** For each metric over the limit, lay out the levers ([§Budget](#budget)) with what each costs in looks or motion. Then ask the user the calls that are theirs:
   - which parts may go static, follow the body, or disappear;
   - what must not change (often the face);
   - which toggles stay;
   - new blueprint or overwrite.

   Record the answers in the handoff. **Done when** every lever you plan to pull has the user's yes.
3. **Cut, in the user's order.** The order used for the Green build:
   1. dead PhysBones;
   2. triangles;
   3. PhysBones;
   4. material slots and skinned meshes.

   After each sub-step: bake and measure. Render before/after views where looks can change (several angles, plus close-ups of reduced parts). Update the handoff. **Done when** the step's metric is under its target and the user has seen the renders.
4. **Motion test.** Anything whose motion changed gets a play-mode pose test ([TEST.md](../vrchat-avatar-unity/TEST.md)):
   - follow-body garments;
   - colliders, merged chains and static parts.

   Legs: spread, kick back, squat, side kick. Torso: lean back, left, right and forward, arms down. **Done when** the renders show no crossing, or the user has accepted what remains.
5. **Verify.** Bake: overall Good, and every category Good. Then check:
   - each menu toggle still reaches its target (shape keys exist in the merged mesh, objects exist);
   - expression and MMD blendshapes survive;
   - expression parameters are present;
   - no new console errors.

   **Done when** all of them pass and the final numbers are in the handoff.
6. **Upload and tag.** Ask before every upload: which blueprint, new or overwrite. Upload through the builder API ([UPLOAD.md](../vrchat-avatar-unity/UPLOAD.md)). Static poses miss dance motion, so the user's in-game test (an MMD dance room) is the real check. Tag the accepted commit with an annotated tag giving the numbers and the blueprint.

## Budget

PC limits checked in Oct 2026; re-check on <https://creators.vrchat.com/avatars/avatar-performance-ranking-system/>.

| Metric | Good | Levers, cheapest first |
|---|---|---|
| Triangles | 70,000 (Medium and Poor are the same 70k) | hidden body (mask); parts that are always covered → EditorOnly; Meshia per renderer |
| Bones | 150 | follow-body garments drop their chains; Merge Bone on hair tips; bones with no weights go with their PB |
| Material slots | 8 | identical materials; AAO Merge Material atlas; toggles → Shape Changer so accessories merge into the main mesh with one palette material |
| Skinned meshes | 2 | face + one merged mesh; meet AAO's auto-merge conditions (GOTCHAS) |
| Meshes (non-skinned) | 8 | convert accessories to single-bone skinned meshes so they merge too |
| PB components | 8 | delete dead PBs; Merge PhysBone groups; static parts |
| PB transforms | 64 | follow-body garments; Merge Bone every other hair-tip bone; static parts |
| PB colliders | 8 | share one set; drop duplicates and colliders only dead PBs used |
| PB collision checks | 128 | ≈ Σ over PBs of (transforms × colliders) |
| Texture memory | 75 MB | rarely the problem; Merge Material also atlases unused legacy properties (shrink them) |

Inactive objects that an animation can switch on still count. Inactive objects nothing switches on are dropped by AAO and don't count.

**Triangle cuts on the Green build** (9-view comparison, accepted by the user):

| Part | Cut | Result |
|---|---|---|
| Top | 22.2k → 7.5k | folds slightly smoother |
| Braid | 10.7k → 5k | no visible change |
| Side hair | 8.5k → 4k | no visible change |
| Skirt | 9.4k → 4.5k | no visible change |
| Socks | 5k → 1.8k | no visible change |
| Shoes | 16.4k → 3k | fine (2k lost the sole tread) |
| Face | not cut | |

[avatar-preupload-optimization-check](https://github.com/Megapotatosan/avatar-preupload-optimization-check)'s in-game caps are more cautious: hands about 30% (only after splitting them from the body), tight clothing about 25%, hair cards about 33%. Past those, look closer.

### Physics: the crux

Good's PhysBone budget (8 / 64 / 8 / 128) rarely fits hair plus a swinging garment. Levers, in the order they were pulled on the Green build:

1. **Dead PBs.** A PB whose chain has no weight on any mesh (inactive meshes included), or that nothing ever enables. AAO removes most of these itself. It keeps any PB that has a parameter (a base's tail or ears, say), and those count.
2. **Static parts** (the user's call per part): delete the PB. Collars, a neck ribbon and the butt went static on Green. The face pinch stayed.
3. **Merge PhysBone groups** of similar chains, overriding the settings that differ: hair front, back and side. Leave alone any chain whose behaviour is unique (an ahoge with gravity 0, a braid).
4. **Merge Bone every other bone** of long hair chains: the hair moves less and the tips are stiffer.
5. **Follow-body garments.** Copy the body's bone weights onto a skirt or coat. Its PBs and leg constraints go away. It no longer flutters, but it never clips. On Green, a swinging skirt with leg-following constraints clipped badly in an MMD dance, and follow-body fixed it.
6. **Spend what's left** where clipping shows: Green gave the braid torso colliders and a 45° limit, then the leftover transforms could go back to hair tips.

## Reference scripts

`scripts/` holds the Green build's editor tools: lightly generalized, with the Green build's values left in as a worked example. Each file opens with what it does, what it assumes, and where it may not fit.

| Script | Does |
|---|---|
| `GarmentFollowsBody.cs` | copies body weights onto a garment (nearest body vertices, inverse-square, smoothed) and removes the garment's PBs and constraints |
| `BodyOcclusionMask.cs` | ray-cone test of which body vertices clothing hides; writes the mask for AAO Remove Mesh By Mask |
| `PhysBoneBudget.cs` | static parts, Merge PhysBone groups with overrides and the end-bone fix, Merge Bone on hair tips, per-PB tweaks; a read-only dead-PhysBone report |
| `MaterialSlotMerge.cs` | accessories → single-bone skinned meshes with `Hide_*` shapes and a palette material, OFF toggles → Shape Changer, material swaps, body + underwear atlas |
| `AvatarGood.Editor.asmdef` | the references they need: AAO runtime, AAO prefab-safe-set, MA core, Meshia runtime + NDMF runtime |

How to use them:
- **If the avatar fits the script's assumptions,** copy it with the asmdef into the project's editor folder. Fill its CONFIG block, select the avatar, and run it from **Tools/Avatar Good**. Each is rerunnable.
- **If it doesn't fit** (another hair rig, a long coat, a different shader), read it for the method and the pitfalls it encodes, then write the project's own version.
- **Either way,** the result is checked by baking, not by trusting the script.

They compile against Unity 2022.3, VRChat SDK 3, AAO 1.9, MA and Meshia 3.2 (checked outside Unity: `vrchat-avatar-unity` GOTCHAS, compile-check). The project-specific originals ran on the Green build. These generalized versions have only been compiled so far: the first real run is their test, so bake and compare before trusting one.

## Other tools

- **No rank target** (the user only wants it lighter): [avatar-preupload-optimization-check](https://github.com/Megapotatosan/avatar-preupload-optimization-check)'s quality-first mode does a lossless cleanup that accepts any rank.
- **[LAC Texture Compressor](https://lac.limitex.dev/)**: texture memory.
- **d4rkAvatarOptimizer**: overlaps AAO and refuses lilToon shader rewriting, so it isn't used here.
