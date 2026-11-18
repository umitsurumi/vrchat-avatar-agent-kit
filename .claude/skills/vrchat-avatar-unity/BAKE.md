# Bake and inspect

What VRChat receives is the avatar after the NDMF build: Modular Avatar, AAO, Meshia, FaceEmo and the rest. Bake a clone and inspect that.

## The tools

The kit's avatar tools (`Assets/AvatarAgentKit/Editor/Avatar/`) do the common steps. One line of `execute_code` each; the result is text to read, and a misuse comes back as `refused:` with the call that fixes it:

```csharp
return AvatarAgentKit.Editor.Bake.Run("MyAvatar");               // clone MyAvatar_bakecheck, run NDMF, hide the source
return AvatarAgentKit.Editor.ReportPerformance.Run("MyAvatar");  // PC rating, limits, biggest renderers and PhysBones
return AvatarAgentKit.Editor.Bake.Cleanup("MyAvatar");           // remove the clone, restore the source, save if it was clean
```

- The name can be left out when the scene holds one avatar. Options and behaviour are in each file's header.
- **After every change, `Bake.Run` again** before measuring or rendering: the clone is a snapshot. Run replaces the old clone.
- **A timed-out `Bake.Run` is not resent.** The editor finishes the bake first; the next call (e.g. `ReportPerformance`) waits for it and finds the clone.
- **NDMF's errors and warnings** come back in Bake's result. A bake with errors is not what VRChat would get; fix them first.
- **`AvatarAgentKit.Editor.Bake` not found:** the tools compile only with NDMF and the VRChat Avatars SDK installed (`doctor.py`); otherwise look for a compile error in the Console.
- Bake, inspection and cleanup are separate calls. Snippets of your own find the clone by its name, `<avatar>_bakecheck`.

## Menu, parameters and FX

The menu tree with each control's parameter, the synced parameter cost, and every FX binding that touches a keyword. Use it to check that a toggle really drives what it should once MA has merged everything. Adapted from tested code.

```csharp
string AVATAR = "MyAvatar", KEY = "Hat";   // KEY: part of an object path, property or parameter name
GameObject c = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR + "_bakecheck" && g.scene.IsValid()) c = g;
var desc = c.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
var sb = new System.Text.StringBuilder();
System.Action<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu, string, int> walk = null;
walk = (menu, indent, depth) => { if (menu == null || depth > 4) return; foreach (var ctl in menu.controls) { sb.AppendLine(indent + ctl.name + " (" + ctl.type + (ctl.parameter != null && ctl.parameter.name != "" ? ", " + ctl.parameter.name : "") + ")"); if (ctl.subMenu) walk(ctl.subMenu, indent + "  ", depth + 1); } };
walk(desc.expressionsMenu, "", 0);
var ep = desc.expressionParameters;
foreach (var p in ep.parameters) if (p.name.Contains(KEY)) sb.AppendLine("param " + p.name + " synced=" + p.networkSynced + " saved=" + p.saved + " default=" + p.defaultValue);
sb.AppendLine("synced bits=" + ep.CalcTotalCost());
UnityEditor.Animations.AnimatorController fx = null;
foreach (var l in desc.baseAnimationLayers) if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX) fx = l.animatorController as UnityEditor.Animations.AnimatorController;
for (int i = 0; i < fx.layers.Length; i++) {
  var layer = fx.layers[i];
  foreach (var cs in layer.stateMachine.states) {
    var motions = new System.Collections.Generic.List<Motion>();
    if (cs.state.motion is UnityEditor.Animations.BlendTree) { foreach (var ch in ((UnityEditor.Animations.BlendTree)cs.state.motion).children) motions.Add(ch.motion); } else if (cs.state.motion) motions.Add(cs.state.motion);
    foreach (var m in motions) { var clip = m as AnimationClip; if (!clip) continue; foreach (var b in AnimationUtility.GetCurveBindings(clip)) if (b.path.Contains(KEY) || b.propertyName.Contains(KEY)) sb.AppendLine(i + " " + layer.name + " / " + cs.state.name + ": [" + b.path + "] " + b.type.Name + "." + b.propertyName + "=" + AnimationUtility.GetEditorCurve(clip, b).keys[0].value); }
  }
}
return sb.ToString();
```

## Cleanup

`Bake.Cleanup` saves the scene only if it had no unsaved changes before the first bake, and says what it did.

**After saving, `git diff` the scene file.** A read-only session should leave no diff; when it does, see GOTCHAS (read-only code can still change the scene).

**To prove a dirty scene holds no real change** without saving over it, save a copy outside `Assets` and diff it against the scene file:

```csharp
var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "copy=" + UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, "/abs/path/outside/Assets/dirty_check.unity", true);
```
