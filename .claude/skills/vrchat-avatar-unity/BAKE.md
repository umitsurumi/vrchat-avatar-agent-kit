# Bake and inspect

What VRChat receives is the avatar after the NDMF build: Modular Avatar, AAO, Meshia, FaceEmo and the rest. Bake a clone and inspect that. The snippets are for `execute_code` with `compiler: codedom` (C# 6); set `AVATAR` to the root's name. Unless marked otherwise, each one ran as written on a real avatar.

- **One job per call.** A call that does too much fails silently. Bake, inspection and cleanup are separate calls.
- **Don't resend after a timeout.** `ProcessAvatar` can outlive the bridge call, and a call can come back `success:false` after the bake actually finished. Look for the clone with a cheap read-only call before baking again.

## Bake

The source is deactivated so the scene shows only the clone. The scene's dirty flag is stored so cleanup knows whether saving is safe.

```csharp
string AVATAR = "MyAvatar";
if (EditorApplication.isPlaying) return "playing";
GameObject src = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR && g.scene.IsValid() && g.transform.parent == null) src = g;
AppDomain.CurrentDomain.SetData("bake_wasDirty", src.scene.isDirty);
var c = UnityEngine.Object.Instantiate(src); c.name = AVATAR + "_bakecheck";
nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(c);
src.SetActive(false);
return "baked";
```

## Performance stats

```csharp
string AVATAR = "MyAvatar";
GameObject c = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR + "_bakecheck" && g.scene.IsValid()) c = g;
var s = new VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats(false);
VRC.SDKBase.Validation.Performance.AvatarPerformance.CalculatePerformanceStats(c.name, c, s, false);
var sb = new System.Text.StringBuilder();
sb.Append("polys=").Append(s.polyCount).Append(" bones=").Append(s.boneCount).Append(" matSlots=").Append(s.materialCount).Append(" skinned=").Append(s.skinnedMeshCount).Append(" meshes=").Append(s.meshCount).Append(" texMB=").Append(s.textureMegabytes.HasValue ? s.textureMegabytes.Value.ToString("F1") : "-").Append(" constraints=").Append(s.constraintsCount);
if (s.physBone.HasValue) { var p = s.physBone.Value; sb.Append(" pbComp=").Append(p.componentCount).Append(" pbTransforms=").Append(p.transformCount).Append(" pbColliders=").Append(p.colliderCount).Append(" pbChecks=").Append(p.collisionCheckCount); }
sb.Append(" overall=").Append(s.GetPerformanceRatingForCategory(VRC.SDKBase.Validation.Performance.AvatarPerformanceCategory.Overall));
return sb.ToString();
```

`GetPerformanceRatingForCategory` also takes the other `AvatarPerformanceCategory` values (PolyCount, PhysBoneTransformCount, …) to show which metric sets the rank. The limit assets live in the VRChat base package under `Validation/Performance/StatsLevels/`.

## Renderers and PhysBones

*Not yet run as written; the first time, check its totals against the stats.* Per renderer: triangles and materials. Per PhysBone: root, transforms and colliders.

```csharp
string AVATAR = "MyAvatar";
GameObject c = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR + "_bakecheck" && g.scene.IsValid()) c = g;
var sb = new System.Text.StringBuilder();
foreach (var r in c.GetComponentsInChildren<Renderer>(true))
{
    Mesh m = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : (r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null);
    if (m == null) continue;
    long tris = 0; for (int i = 0; i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3;
    sb.Append(r.name).Append(" tris=").Append(tris).Append(" mats=");
    foreach (var mat in r.sharedMaterials) sb.Append(mat ? mat.name : "-").Append("|");
    sb.Append("\n");
}
foreach (var pb in c.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>(true))
{
    pb.InitTransforms(true);
    var root = pb.rootTransform ? pb.rootTransform : pb.transform;
    sb.Append("PB ").Append(root.name).Append(" transforms=").Append(pb.bones.Count).Append(" colliders=").Append(pb.colliders.Count).Append("\n");
}
return sb.ToString();
```

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

Destroy the clone and reactivate the source. Save only if the scene was clean before the bake: a bake leaves the scene flagged dirty with no real change, but a scene that was already dirty holds the user's unsaved work.

```csharp
string AVATAR = "MyAvatar";
GameObject c = null, src = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) { if (!g.scene.IsValid()) continue; if (g.name == AVATAR + "_bakecheck") c = g; if (g.name == AVATAR && g.transform.parent == null) src = g; }
if (c) UnityEngine.Object.DestroyImmediate(c);
src.SetActive(true);
var was = AppDomain.CurrentDomain.GetData("bake_wasDirty");
if (was is bool && !(bool)was) { UnityEditor.SceneManagement.EditorSceneManager.SaveScene(src.scene); return "cleaned, saved"; }
return "cleaned, not saved (scene had unsaved changes before the bake)";
```

**After saving, `git diff` the scene file.** A read-only session should leave no diff; when it does, see GOTCHAS (read-only code can still change the scene).

**To prove a dirty scene holds no real change** without saving over it, save a copy outside `Assets` and diff it against the scene file:

```csharp
var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "copy=" + UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, "/abs/path/outside/Assets/dirty_check.unity", true);
```
