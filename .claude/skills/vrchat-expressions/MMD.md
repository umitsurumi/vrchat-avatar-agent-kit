# MMD worlds

MMD dance worlds seat the avatar in a station and drive the face mesh's **MMD-named shape keys** (あ い う え お, まばたき, 笑い, ウィンク, …) by animation. Whatever else writes those keys every frame wins, and the dance shows a frozen face. Toggles and other FX state can break too.

## What the worlds and tools do

Researched Nov 2026; behaviour differs between worlds.

- **Many MMD worlds zero the weight of FX layers 1 and 2** (zero-based), the left- and right-hand gesture layers of VRChat's default FX template. Others differ: guides report different layer positions, VRCFury notes that MMD worlds disable the Gesture controller, and MA issue #1922 lists worlds that behaved differently.
- **The face mesh must be named `Body`**, and it needs the MMD shape keys.
- **Modular Avatar** inserts padding (`MMD Dummy`) and relay (`MMD Control`) layers at the top of FX, so that the layers which were *originally* 1 and 2 are the ones disabled. Layers merged with **Merge Animator are not affected**, unless they carry the `MA MMD Layer Control` state behaviour (on the layer itself, never on a state). The VRChat Settings component can turn this handling off.
- **FaceEmo** merges with Merge Animator, so MMD worlds don't disable it. Its **dance gimmick** (in-game menu: FaceEmo → Settings) bypasses its expression layers while the avatar is in a station; one option disables the whole FX instead. The toggle **defaults to off and isn't saved**.
- **Optimizers that rewrite the FX** can shift layer indices and break all of this (d4rk "Optimize FX Layer", VRCFury "Fix Write Defaults").

Sources: [MA: MMD World Workarounds](https://modular-avatar.nadena.dev/docs/general-behavior/mmd), [MA issue #1922](https://github.com/bdunderscore/modular-avatar/issues/1922), [MA issue #2094](https://github.com/bdunderscore/modular-avatar/issues/2094), [FaceEmo settings menu](https://suzuryg.github.io/face-emo/docs/optional-functions/setting-menu/), [FaceEmo issue #157](https://github.com/suzuryg/face-emo/issues/157), [d4rk issue #122](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/122), [VRCFury fixes](https://vrcfury.com/fixes/).

## Check an avatar

Bake (`Bake.Run`, `vrchat-avatar-unity` BAKE.md), then list the first FX layers, and every layer that writes face keys, with how many of them are MMD keys. A Japanese-named key counts as MMD here; check the list by eye. Adapted from code that ran on a real avatar (only the names became parameters):

```csharp
string CLONE = "MyAvatar_bakecheck", FACE = "Body";
GameObject c = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == CLONE && g.scene.IsValid()) c = g;
var desc = c.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
UnityEditor.Animations.AnimatorController fx = null;
foreach (var l in desc.baseAnimationLayers) if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX) fx = l.animatorController as UnityEditor.Animations.AnimatorController;
var body = c.transform.Find(FACE).GetComponent<SkinnedMeshRenderer>();
var mmd = new System.Collections.Generic.HashSet<string>();
for (int i = 0; i < body.sharedMesh.blendShapeCount; i++) { var n = body.sharedMesh.GetBlendShapeName(i); foreach (var ch in n) if (ch > 0x3000) { mmd.Add(n); break; } }
var sb = new System.Text.StringBuilder();
sb.Append("FX layers=").Append(fx.layers.Length).Append(" | face keys=").Append(body.sharedMesh.blendShapeCount).Append(" of which MMD-named=").Append(mmd.Count).Append("\n");
for (int i = 0; i < fx.layers.Length; i++) {
  var layer = fx.layers[i];
  var keys = new System.Collections.Generic.HashSet<string>();
  bool wdOn = false, wdOff = false;
  var stack = new System.Collections.Generic.Stack<UnityEditor.Animations.AnimatorStateMachine>(); stack.Push(layer.stateMachine);
  while (stack.Count > 0) { var sm = stack.Pop(); foreach (var ch in sm.stateMachines) stack.Push(ch.stateMachine);
    foreach (var cs in sm.states) { if (cs.state.writeDefaultValues) wdOn = true; else wdOff = true;
      var motions = new System.Collections.Generic.List<Motion>(); var q = new System.Collections.Generic.Queue<Motion>(); if (cs.state.motion) q.Enqueue(cs.state.motion);
      while (q.Count > 0) { var m = q.Dequeue(); var bt = m as UnityEditor.Animations.BlendTree; if (bt) { foreach (var child in bt.children) if (child.motion) q.Enqueue(child.motion); } else motions.Add(m); }
      foreach (var m in motions) { var clip = m as AnimationClip; if (!clip) continue; foreach (var b in AnimationUtility.GetCurveBindings(clip)) if (b.path == FACE && b.propertyName.StartsWith("blendShape.")) keys.Add(b.propertyName.Substring(11)); } } }
  int mmdHit = 0; var hits = new System.Collections.Generic.List<string>(); foreach (var k in keys) if (mmd.Contains(k)) { mmdHit++; if (hits.Count < 6) hits.Add(k); }
  if (i < 6 || keys.Count > 0)
    sb.Append(i).Append(" ").Append(layer.name).Append(" w=").Append(layer.defaultWeight).Append(" WD=").Append(wdOn && wdOff ? "mixed" : (wdOn ? "on" : "off")).Append(" faceKeys=").Append(keys.Count).Append(" mmdKeys=").Append(mmdHit).Append(hits.Count > 0 ? " (" + string.Join(",", hits.ToArray()) + ")" : "").Append("\n");
}
return sb.ToString();
```

How to read the output:
- Which layers sit at **1 and 2**? With MA they should be its `MMD Dummy` / `MMD Control` layers.
- Which layers **write MMD keys with weight 1**, and can an MMD world switch them off? A Merge Animator layer without MMD Layer Control can't be switched off.

**Example** (MA + FaceEmo, default settings): layers 0–2 were MA's MMD layers. FaceEmo's `DEFAULT FACE` wrote all 42 MMD keys, so dance lip sync and blinking depend on the dance gimmick being on. The expression layer itself wrote none.

## Strategies

Pick one with the user:

| Strategy | How | Cost |
|---|---|---|
| **Dance gimmick** | Leave FaceEmo as is; the user turns the gimmick on before dancing | Manual every time; defaults off and isn't saved |
| **MMD keys excluded** | Add every MMD-named key to FaceEmo's Excluded Blend Shapes and re-apply; keep them out of expression clips | Works without a toggle. Gestures during a dance stack on top of the MMD face, since they use other keys. If expressions need MMD keys, this doesn't fit |
| **Whole FX off while dancing** | FaceEmo dance option "disable entire FX layer" (needs Match Avatar Write Defaults) | Toggles and outfit state reset to default during the dance; still needs the gimmick toggle |
| **Opt layers in** | Put `MA MMD Layer Control` on FaceEmo's layers in the generated controller | Edits generated output, which each re-apply replaces; only helps in worlds that disable layers 1 and 2 |

After any change, the user tests in an MMD world, with the gimmick both on and off:
- mouth vowels;
- blinking;
- a smile;
- a gesture during the dance;
- every toggle (outfit pieces, accessories).
