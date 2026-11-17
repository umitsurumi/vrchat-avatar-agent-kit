---
name: vrchat-avatar-unity
description: Work on a VRChat avatar in the open Unity Editor through MCP for Unity — bake and inspect what VRChat receives, render checks, play-mode and animator tests, menus and toggles, upload. Use whenever editing, checking or uploading a VRChat avatar in Unity, adding a menu toggle, or when another skill needs these.
---

# VRChat avatar in Unity

The editor is **shared**: the user works in the same Unity, often in the same scene, while you drive it through `execute_code`. So every change you make is undoable and visible, and you judge it on what VRChat will actually receive: a **baked clone**, not the scene avatar. The scene avatar is pre-NDMF, and NDMF preview can make it look already processed.

## Before the first call

- **Which project is the bridge talking to?** Read the active scene path (and `mcpforunity://instances` when several editors run). If it is another project, stop and tell the user. Don't run anything there.
- **Editor state:** play mode, compiling, and whether the scene is dirty with changes you didn't make.
- **The avatar root:** the object with a `VRCAvatarDescriptor`, no parent, `scene.IsValid()`. Find objects through `Resources.FindObjectsOfTypeAll<GameObject>()`: `GameObject.Find` misses inactive ones.

## Working rules

- **Vendor assets are read-only.** Copy into the project's folder for modified assets, edit the copy, and point the avatar at it. Prefab-instance overrides in the scene are fine.
- **Textures are regenerated from the original** by a recipe every time, never edited again from a previous output. Repeated passes pile up artifacts. Skill `vrchat-textures`.
- **One job per `execute_code` call.** Bake, inspect, render and clean up are separate calls. The compiler is codedom, C# 6 (GOTCHAS §codedom).
- **Repeatable work goes in a compiled editor script** with an asmdef, rerunnable from a menu. One-off work stays in `execute_code`.
- **Save the scene only when it was clean before your change**, or when every unsaved change is yours. Otherwise ask. Say what you saved.
- **Switching git branches with Unity open:** open an empty scene first, switch, refresh, then reopen the scene. This avoids Unity's "scene changed on disk" prompt overwriting either side.
- **Look before you claim.** A visual result is claimed only after rendering it and looking at the image. Motion, toggles and feel are claimed only as far as they were tested. Say what is left for the in-game test.

## The check loop

1. **Change** it, through `Undo`, recording prefab-instance modifications.
2. **Bake** a clone ([BAKE.md](BAKE.md)): what VRChat receives after MA, NDMF, AAO and the rest.
3. **Inspect** the clone. Check the stats, the renderers and their materials, the menu, the parameters and the FX map, and the blendshape names (AAO renames merged ones).
4. **Render** ([RENDER.md](RENDER.md)) and look at the images. Use a preview scene while the user is editing.
5. **Test motion and logic** ([TEST.md](TEST.md)):
   - play-mode poses for PhysBones and constraints;
   - an animator run for FX layers and toggles.
6. **Clean up:** destroy clones, close preview scenes, reactivate the source. **Done when** no leftover objects remain and the scene is saved or left exactly as the user had it.
7. **Hand over to the in-game test.** List what only the client can show: physics feel, contacts, other players' view.

## After an outfit change

Putting an outfit on, and the cleanup checklist after it, are in skill `vrchat-outfits`.

## Reference

- [BAKE.md](BAKE.md): before baking, measuring or reading the menu and FX of what gets uploaded.
- [RENDER.md](RENDER.md): before any screenshot or before/after comparison.
- [TEST.md](TEST.md): before testing PhysBones, constraints, toggles, FX layers or gestures.
- [MENUS.md](MENUS.md): before adding, moving or checking a menu item, toggle or parameter.
- [UPLOAD.md](UPLOAD.md): before any upload. Ask the user first, every time.
- [GOTCHAS.md](GOTCHAS.md): before writing code against MA, VRC components, lilToon or AAO internals, and whenever something behaves oddly.
