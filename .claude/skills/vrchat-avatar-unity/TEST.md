# Tests: motion and animator logic

The Unity Editor shows less than the VRChat client. PhysBones and VRC constraints run only in play mode. Gesture Manager doesn't pick up an avatar driven from MCP, and parameter drivers do nothing in a bare Animator. So:
- motion is tested with **posed bones in play mode**;
- logic is tested by **running the baked FX controller by hand**.

## Pose test (PhysBones, constraints, clipping)

Enter play mode, then:

1. **Save the rest pose**, once per play session. `Temp/` can be cleared, so if the file is gone, save it again at rest.

   ```csharp
   if (!EditorApplication.isPlaying) return "not playing";
   GameObject av = null;
   foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.scene.IsValid() && g.transform.parent == null && g.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null) av = g;
   var anim = av.GetComponent<Animator>(); anim.enabled = false;
   var sb = new System.Text.StringBuilder();
   foreach (var b in new HumanBodyBones[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm }) { var q = anim.GetBoneTransform(b).localRotation; sb.Append(q.x.ToString("R")).Append(',').Append(q.y.ToString("R")).Append(',').Append(q.z.ToString("R")).Append(',').Append(q.w.ToString("R")).Append('\n'); }
   System.IO.Directory.CreateDirectory("Temp/shots");
   System.IO.File.WriteAllText("Temp/shots/rest_torso.txt", sb.ToString());
   return "saved";
   ```

   The Animator has to stay disabled, or it resets every bone you set. For leg poses, save `LeftUpperLeg`, `RightUpperLeg`, `LeftLowerLeg` and `RightLowerLeg` the same way.

2. **Pose: restore the rest pose, then rotate**, all in one call. The rotations are around world axes taken from the avatar, so the pose doesn't depend on the bone rolls.

   ```csharp
   string POSE = "back";   // back | right | left | fwd
   if (!EditorApplication.isPlaying) return "not playing";
   GameObject av = null;
   foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.scene.IsValid() && g.transform.parent == null && g.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null) av = g;
   var anim = av.GetComponent<Animator>(); anim.enabled = false;
   var bones = new Transform[] { anim.GetBoneTransform(HumanBodyBones.Spine), anim.GetBoneTransform(HumanBodyBones.Chest), anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), anim.GetBoneTransform(HumanBodyBones.RightUpperArm) };
   var lines = System.IO.File.ReadAllLines("Temp/shots/rest_torso.txt");
   for (int i = 0; i < 4; i++) { var p = lines[i].Split(','); bones[i].localRotation = new Quaternion(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]), float.Parse(p[3])); }
   var right = av.transform.right; var fwd = av.transform.forward;
   System.Action<int, float, Vector3> rot = (i, a, axis) => { bones[i].rotation = Quaternion.AngleAxis(a, axis) * bones[i].rotation; };
   rot(2, 70f, fwd); rot(3, -70f, fwd);                       // arms down
   if (POSE == "back") { rot(0, -15f, right); rot(1, -15f, right); }
   if (POSE == "fwd") { rot(0, 15f, right); rot(1, 15f, right); }
   if (POSE == "right") { rot(0, -15f, fwd); rot(1, -15f, fwd); }
   if (POSE == "left") { rot(0, 15f, fwd); rot(1, 15f, fwd); }
   return POSE;
   ```

3. **Wait 1–3 s** for the PhysBones to settle, then **render in a separate call** (RENDER.md, aimed at the part under test). Compare against a baseline: the same poses with the old settings.

Poses that caught real problems:
- **Legs** (garments, skirts): spread 40°, kick back 45°, squat (thigh 110°, knee 130°), side kick 70°.
- **Torso** (hair, braids): lean back, left, right and forward by 30° (15° on Spine plus 15° on Chest), arms down.

Static poses still miss fast motion: dance animations caught clipping that every static pose missed. Ask the user to check in game, e.g. in an MMD dance world.

**Constraints:** VRC constraints don't evaluate in edit mode either. At runtime, `ApplyConfigurationChanges()` re-bakes the offsets of *unlocked* constraints, so a vendor's unlocked constraint can drift from what it was authored to do.

## Animator logic (FX layers, toggles, gestures)

Run the **baked** clone's FX on its own Animator, in edit mode: MA has merged every layer by then. Drive the parameters by hand, advance time and read the state names. Bake first (BAKE.md); this example renames the clone `ZZ_BakeTest`.

```csharp
var clone = GameObject.Find("ZZ_BakeTest");
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
