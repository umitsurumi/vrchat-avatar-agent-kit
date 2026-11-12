# Gotchas

Each one cost real time on a real avatar, unless it is marked as coming from someone else's notes.

## codedom (`execute_code`, C# 6)

- No `$"…"` interpolation.
- Write `UnityEngine.Object` (plain `Object` is ambiguous).
- Doing too much in one call fails silently, often with no error at all. Split the work.
- To find an internal type, use `Type.GetType("Ns.Type, assembly-name")`, e.g. `Anatawa12.AvatarOptimizer.MergeBone, com.anatawa12.avatar-optimizer.runtime`. If it returns null, scan `AppDomain.CurrentDomain.GetAssemblies()`.
- AAO's `PrefabSafeSet` `Remove()` cleared the whole set in codedom. Write the set's `mainSet` through `SerializedObject` instead.
- State that has to live between calls goes in `AppDomain.CurrentDomain.SetData/GetData`, or in a file.

## Unity editor

- **Compiled editor scripts need an asmdef** to reach AAO, MA or Meshia types: their assemblies are not autoReferenced. List them under `references`.
- **Compile-check without the editor** (Unity busy, or another project open). Use the Roslyn bundled with the editor and the response file Unity wrote for an asmdef the last time it compiled. Drop that file's source, `-out`, `-refout` and analyzer lines and keep its `-r:` references:

  ```sh
  D=<Unity>/Editor/Data        # e.g. ~/Unity/Hub/Editor/2022.3.22f1/Editor/Data
  cd <project>                 # the rsp paths are relative to it
  grep -vE '\.cs"$|^-out:|^-refout:|^-analyzer|^-additionalfile|^-generatedfilesout' Library/Bee/artifacts/*/<Asm>.rsp > /tmp/base.rsp
  "$D/NetCoreRuntime/dotnet" "$D/DotNetSdkRoslyn/csc.dll" /noconfig @/tmp/base.rsp -out:/tmp/check.dll Scripts/*.cs
  ```

  This only proves the code compiles; running it is still the test.
- **After a compile error,** Unity keeps the old assembly loaded and the error stays in the console. Read the console after every compile before trusting a tool run.
- **Linux:** after a script or asmdef change, compilation can stall at "Requested script compilation" until the user clicks into the Unity window. Ask once; don't loop on refresh. `refresh_unity(compile=request, mode=force, scope=all)` often finishes by itself.
- **Edit-mode blendshapes** show on screen only a frame later. Render in the next call, or `BakeMesh`.
- **`Physics.Raycast`** overloads without `out RaycastHit` ignore back faces, even with `Physics.queriesHitBackfaces = true`.
- **An enabled Animator resets bones** you set by hand. Disable it before posing.
- **`Temp/` can be emptied by Unity.** Keep anything you need again (rest poses, logs) somewhere you re-check, or recreate it.
- **The editor can crash,** natively, around play mode or script import. Save confirmed work first. (From agentic-unity's notes.)

## Scenes and git

- **A bake leaves the scene dirty** with no real change. To prove that, save a copy outside `Assets` and diff it (BAKE.md).
- **The user may have unsaved work in the scene.** Check `isDirty` before every save, and ask if the changes aren't yours. A blind `SaveScene` once saved the user's half-finished outfit swap.
- **Switching branches with Unity open:** open an empty scene, switch, refresh, then reopen the scene.
- **Files that builds rewrite** (lilToon's `lilToonSetting.json`, project settings toggled by tools) are not part of a change. Leave them out of commits unless the user wants them.
- **`Packages/` is often gitignored** in VCC/ALCOM projects. Rolling git back then only rolls back `vpm-manifest.json`, and the packages have to be re-resolved.

## Modular Avatar

- **Bone Proxy** must sit on the object a toggle switches. On a child, the child moves under the bone at build time, and the toggle switches an empty shell.
- **An Object Toggle inside its own target** switches itself off. Keep toggles beside what they switch.
- **Toggle targets return to their saved state** when the toggle is off. Save the target in its "off" state, even if the toggle defaults to on. (From agentic-unity's notes.)
- **Menu item vs install target:** the menu-item object (with `ModularAvatarMenuItem`) and a Menu Install Target can share a name. Pick the one with the component.
- **Shape Changer** fields: `m_shapes[].Object.referencePath / targetObject`, `ShapeName`, `ChangeType` (Delete / Set), `Value`. Hiding by a shape key, instead of toggling the object, keeps the renderer mergeable.
- **Merge Animator layer priority:** later layers win, so a layer that sets the face goes after the expression tool's layers.

## VRC components

- **Constraints and PhysBones** don't run in edit mode. Test them in play mode (TEST.md). Unlocked constraints re-bake their offsets on `ApplyConfigurationChanges()`, and `ActivateConstraint()` locks and bakes at the current pose. Vendor constraints may be unlocked and asymmetric.
- **Contacts:** a receiver that should only react to *other* players' hands needs "allow self" off. Test the logic with the parameter (TEST.md), and the feel in game.
- **Gesture Manager** didn't pick up an avatar driven from MCP. Test the FX by hand instead (TEST.md).

## lilToon

- **Feature flags:** material properties set from a script don't turn on lilToon's shader-feature flags (e.g. a second emission map). Run lilToon's own setting refresh by reflection: the internal `lilToonSetting.LoadShaderSetting`, `CheckTextures(ref setting, material)` and `ApplyShaderSetting(setting, null)`.
- **`lilToonSetting.json`** is rewritten at build, turning unused features off. That is build churn, not a change.
- **Surin's "lilToon light setting"** animates every lilToon renderer, with its curves typed by renderer type. Its button recreates its object and FX with a new GUID, and once lost a Menu Install Target. To cover new or renamed renderers, rewrite its generated `Brightness.anim` / `Monochromatic.anim` in place: clear the curves, then key `_LightMinLimit`, `_LightMaxLimit` and `_MonochromeLighting` per renderer.

## Assets and import

- **An FBX with blendshapes must import as Generic** (or Humanoid). With animation type None, the mesh comes in as a MeshRenderer and loses its blendshapes.
- **Blender** writes a shape key's current value as its default weight; zero them before export. Its camera `clip_start` defaults to 0.1 m, so a close-up preview camera cuts into the object; set 0.001.
- **Overwriting a mesh asset:** `EditorUtility.CopySerialized` onto a mesh asset whose vertex count changed keeps the old vertex buffer, and the mesh renders as a tangle. Copy through the Mesh API, then `SetDirty` before `SaveAssets`.
- **Vendor packages reuse GUIDs** for different files. Inspect a `.unitypackage` before importing it over existing assets. (From agentic-unity's notes.)
- **Generated assets keep their GUID** if you overwrite them in place. A new asset each time breaks every reference to the old one.
