# Tests: motion and animator logic

The Unity Editor shows less than the VRChat client. PhysBones and VRC constraints run only in play mode. Gesture Manager doesn't pick up an avatar driven from MCP, and parameter drivers do nothing in a bare Animator. So:
- motion is tested with **posed bones in play mode**;
- logic is tested by **running the baked FX controller by hand**.

## Pose test (PhysBones, constraints, clipping)

Enter play mode (`manage_editor` play), then pose, wait, measure, render, each in its own call. Measure clipping first and render the regions it names: a measurement settles what a picture only suggests.

```csharp
return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "stand");     // rest pose, arms down; Animator off
// wait 1–3 s for the PhysBones to settle
return AvatarAgentKit.Editor.ReportClipping.Run("MyAvatar");    // standing: saves the reference
return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "squat");     // rest pose, arms down, then the pose
// wait 1–3 s
return AvatarAgentKit.Editor.ReportClipping.Run("MyAvatar");    // what came out through which cover, how far, where
return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar", "front,left,back", focus: "LeftUpperLeg", size: 0.5f, label: "squat");
return AvatarAgentKit.Editor.Pose.Reset("MyAvatar");             // rest pose, Animator back on
```

- **Legs** (skirts, garments): `spread`, `kick-back`, `side-kick`, `squat`, `sit`. **Torso** (hair, braids): `lean-back`, `lean-forward`, `lean-left`, `lean-right`. Angles and the full list are in the header of `Pose.cs`. All but `sit` caught real problems.
- **`ReportClipping`** measures skin (the materials named like body or skin, or `inner:` what you name, e.g. underwear) coming out through the clothes over it, against the standing reference: per body part and garment material, how many vertices and how deep, with the next render call. What was already through standing (design seams such as face over neck, hands hanging into a wide skirt) is listed once and then only reported when a pose pushes it deeper. "Exposed" means the garment moved away, not clipping. It doesn't measure hair or cloth sinking into the body; render for those.
- In play mode the scene avatar is the processed one, and `RenderAvatar` keeps its pose.
- **Entering play mode** runs NDMF on the avatar (20–30 s on a big one). The first `execute_code` after it often fails; wait for `mcpforunity://editor/state` to stop being stale. Twice the connection didn't come back at all and the user had to stop play mode or restart Unity: do all poses in one play session.
- **Compare against a baseline:** the same poses with the old settings, rendered with their own `label`.
- **Static poses still miss fast motion:** dance animations caught clipping that every static pose missed. Put it on the in-game checklist, e.g. a dance in an MMD world, watching the skirt and the hair.

**Constraints:** VRC constraints don't evaluate in edit mode either. At runtime, `ApplyConfigurationChanges()` re-bakes the offsets of *unlocked* constraints, so a vendor's unlocked constraint can drift from what it was authored to do.

## Animator logic (FX layers, toggles, gestures)

Run the **baked** clone's FX on its own Animator, in edit mode: MA has merged every layer by then. Drive the parameters by hand, advance time and read the state names. Bake first (`Bake.Run`, [BAKE.md](BAKE.md)).

```csharp
var clone = GameObject.Find("MyAvatar_bakecheck");
var desc = clone.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
RuntimeAnimatorController fx = null;
foreach (var l in desc.baseAnimationLayers) if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX) fx = l.animatorController;
var anim = clone.GetComponent<Animator>();
anim.runtimeAnimatorController = fx;
anim.Rebind();
int LAYER = 16;   // the layer under test: find its index by name in the FX controller
string[] names = { "Normal", "Muted", "Speak", "AFK" };   // the states it can be in
var sb = new System.Text.StringBuilder();
System.Func<string> state = () => { var h = anim.GetCurrentAnimatorStateInfo(LAYER).shortNameHash; foreach (var n in names) if (Animator.StringToHash(n) == h) return n; return "?" + h; };
System.Action<string, float> step = (label, secs) => { for (float t = 0; t < secs; t += 0.05f) anim.Update(0.05f); sb.AppendLine(label.PadRight(20) + state()); };
step("start", 0.5f);
anim.SetBool("MuteSelf", true); step("mute", 0.5f);
anim.SetBool("MuteSelf", false); anim.SetFloat("Voice", 0.2f); step("talking", 0.5f);
anim.SetFloat("Voice", 0f); step("stop +1s", 1f);
anim.SetBool("AFK", true); step("AFK", 0.5f);
return sb.ToString();
```

- **Judge logic by state names, not by blendshape weights.** In edit mode `Animator.Update` sometimes writes weights back to the renderers and sometimes doesn't. To see the look, set the weights yourself and render (`BakeMesh`).
- **The built-in parameters** (`AFK`, `MuteSelf`, `Voice`, `GestureLeft`, …) are just parameters here, so set them yourself. Copy the defaults of your own synced parameters from the expression parameters first.
- **Parameter drivers** don't run in a bare Animator. To test logic that depends on them, set `VRC.SDKBase.VRC_AvatarParameterDriver.OnApplySettings` temporarily to a handler that applies Set/Add/Copy, then restore it.
- **Blendshape names** on merged meshes are renamed by AAO (`AAO_Merged_<n>_<renderer>__<key>_<i>`). Match them exactly: a prefix match on `Speak` also hits `Speak_Loud`.
- **Layer order matters:** in a merged FX, later layers win. An MA Merge Animator layer that shows a face has to come after the expression tool's default-face layer, or it is overwritten. Give it a higher layer priority.
