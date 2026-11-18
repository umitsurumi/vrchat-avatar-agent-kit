# Bake and inspect

What VRChat receives is the avatar after the NDMF build: Modular Avatar, AAO, Meshia, FaceEmo and the rest. Bake a clone and inspect that.

## The tools

The kit's avatar tools (`Assets/AvatarAgentKit/Editor/Avatar/`) do the common steps. One line of `execute_code` each; the result is text to read, and a misuse comes back as `refused:` with the call that fixes it:

```csharp
return AvatarAgentKit.Editor.Bake.Run("MyAvatar");               // clone MyAvatar_bakecheck, run NDMF, hide the source
return AvatarAgentKit.Editor.ReportPerformance.Run("MyAvatar");  // PC rating, limits, biggest renderers and PhysBones
return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar");         // menu, parameters, FX layers (below)
return AvatarAgentKit.Editor.Bake.Cleanup("MyAvatar");           // remove the clone, restore the source, save if it was clean
```

- The name can be left out when the scene holds one avatar. Options and behaviour are in each file's header.
- **After every change, `Bake.Run` again** before measuring or rendering: the clone is a snapshot. Run replaces the old clone.
- **A timed-out `Bake.Run` is not resent.** The editor finishes the bake first; the next call (e.g. `ReportPerformance`) waits for it and finds the clone.
- **NDMF's errors and warnings** come back in Bake's result. A bake with errors is not what VRChat would get; fix them first.
- **`AvatarAgentKit.Editor.Bake` not found:** the tools compile only with NDMF and the VRChat Avatars SDK installed (`doctor.py`); otherwise look for a compile error in the Console.
- Bake, inspection and cleanup are separate calls. Snippets of your own find the clone by its name, `<avatar>_bakecheck`.

## Menu, parameters and FX

`ReportMenu` reads what MA has merged: the menu tree with each control's parameter, the synced cost, and each FX layer with its Write Defaults and the parameters it tests. With `find`, it traces a toggle from the menu to the layers that test its parameter and what they animate, and marks bindings to objects that don't exist in the clone:

```csharp
return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar");               // overview
return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar", find: "Hat");  // one toggle, end to end
```

A toggle works when its control, its parameter, a layer testing that parameter and bindings to existing objects all show up. `layers: "Gesture"` (or another layer type) reads that controller instead of FX.

## Cleanup

`Bake.Cleanup` saves the scene only if it had no unsaved changes before the first bake, and says what it did.

**After saving, `git diff` the scene file.** A read-only session should leave no diff; when it does, see GOTCHAS (read-only code can still change the scene).

**To prove a dirty scene holds no real change** without saving over it, save a copy outside `Assets` and diff it against the scene file:

```csharp
var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "copy=" + UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, "/abs/path/outside/Assets/dirty_check.unity", true);
```
