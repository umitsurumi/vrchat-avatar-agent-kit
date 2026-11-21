// Reactions for an accessory: it shows what the avatar is doing by switching shape keys.
// Menu: Tools/Avatar Accessories/Accessory Reactions (select the avatar root, or anything under it).
// Rerunnable: controllers and clips are rebuilt in place (same GUIDs), scene objects are reused.
//
// States, highest priority first (a state whose key is "" in CONFIG is left out):
//   AFK (built-in)        AfkKey     e.g. "zzz"
//   head pat (contact)    PatKey     e.g. a big "?" for as long as the hand stays
//   Voice (built-in)      SpeakKey   grown by LoudKey with the voice volume
//   MuteSelf (built-in)   MutedKey   e.g. still, unlit dots
//   otherwise             Normal: every state key at 0
// One layer, one state per reaction. Every clip writes every state key, so a crossfade only ever
// blends two states and a part never collapses twice. Speaking and patting hold for a moment after
// they stop, so the gaps between syllables or patting strokes don't flicker.
//
// Why shape keys: the accessory stays one active renderer whatever it shows, so AAO can still merge it
// into the main mesh (skill vrchat-pc-good). The mesh has to carry the keys: each state's shapes sit
// collapsed inside the body of the accessory at 0 and come out at 100 (a generator script makes them).
// The built-in parameters are synced by VRChat itself, so the reactions cost no synced bits; the head
// pat costs one (its OFF toggle).
//
// Face (optional, FaceKeys): a second controller that sets a few face keys while the head is patted
// (a shy face: blush, eyes away, troubled brows). It is merged after the expression tool's layers
// (FaceLayerPriority), because FaceEmo's default-face layer rewrites every face key each frame and
// would hide it. Use only keys the gesture expressions leave alone, so gestures and lip sync keep
// working underneath.
//
// Layout it builds or reuses:
//   <Accessory>                 the accessory (Renderer under it); MA Merge Animator with the states
//     Face                      MA Merge Animator with the face layer
//   <PatObject>                 beside the accessory: the head-pat receiver, its own MA Bone Proxy to Head
//   <PatMenu>                   beside the accessory: "HeadPat OFF" toggle for the receiver
// The receiver and the toggle sit beside the accessory, not inside it: an Object Toggle only works
// while its own object is active, and head pats should keep working while the accessory is hidden.
// A receiver or toggle an earlier layout left inside the accessory, under these names, is moved out.
// Anything else an earlier version left (a face layer under another name, an old receiver) is not
// found: remove it first, or two face layers end up merged. Copies of a base's own head pat can be
// retired with StripObjects / StripParameters.
//
// Assumes:
//   - Write Defaults ON across the FX (the face layer's Off state writes nothing and relies on WD to
//     hand the face back to the expression tool). On a WD-off avatar, give Off a clip instead.
//   - The accessory's state keys are on one renderer; the face keys on the face renderer.
//   - The expression parameters asset is a copy in the project's folder, if anything is stripped.
// Verified: the project-specific original (MANUKA speech-bubble hairpin, Oct 2026) ran on three
// branches. This generalized version ran on a copy of the MANUKA military avatar (Nov 2026; Unity
// 2022.3, VRChat SDK 3.10, MA 1.18, FaceEmo, AAO): on the bake, the face layer after FaceEmo's, every
// transition and hold right when the FX was run by hand, each state rendered. Still check yours the
// same way: bake, run the FX by hand (vrchat-avatar-unity TEST.md), render each state.

using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace AvatarAccessories.Editor
{
    public static class AccessoryReactions
    {
        // ---- CONFIG: replace with your avatar's -------------------------------------------------
        const string Accessory = "Hairpin";                 // object at the avatar root
        const string Renderer = "SpeechBubblePin";          // relative to Accessory: holds the state keys
        const string AssetDir = "Assets/_USER/Hairpin";     // controllers here, clips in <AssetDir>/Clips
        const string UserDir = "Assets/_USER/";             // assets outside it are never changed

        const string AfkKey = "AFK", PatKey = "Pat", SpeakKey = "Speak", LoudKey = "Speak_Loud", MutedKey = "Muted";
        const float VoiceOn = 0.02f;      // Voice above this counts as speaking
        const float VoiceFull = 0.3f;     // ...and at this LoudKey is at 100
        const float SpeakHold = 0.5f, PatHold = 0.8f, Fade = 0.1f;

        // Face keys while patted; empty: no face layer. Keys no gesture expression uses.
        const string FaceRenderer = "Body";                 // relative to the avatar root
        static readonly KeyValuePair<string, float>[] FaceKeys =
        {
            new KeyValuePair<string, float>("option_cheek 3", 100f),
            new KeyValuePair<string, float>("eye_right", 60f),
            new KeyValuePair<string, float>("eye_down", 20f),
            new KeyValuePair<string, float>("brow_trouble", 50f),
        };
        const int FaceLayerPriority = 1;                    // after FaceEmo (0)

        // Head pat; PatKey "" and FaceKeys empty: no receiver at all.
        const string PatParam = "USER/HeadPat";             // prefix with your project
        const string PatObject = "HeadPat";
        static readonly Vector3 PatPosition = new Vector3(0f, 0.15f, 0.03f);   // from the Head bone
        const float PatRadius = 0.07f;
        const string PatMenu = "HeadPat OFF";
        const string AccessoryMenu = "Hairpin OFF";         // the accessory's own toggle: its settings are
                                                            // copied and PatMenu goes right after it
        static readonly string MenuFolder = "Avatar Menu/角色菜单";   // an MA menu folder under the root; "": root menu

        // A base's own head pat to retire (EditorOnly), and its parameters to drop.
        static readonly string[] StripObjects = { "Armature/Hips/Spine/Chest/Neck/Head/NadeNade_Head", "Avatar Menu/原版菜单/NadeNade_Head" };
        static readonly string[] StripParameters = { "NadeNade_Head_toggle" };
        // ------------------------------------------------------------------------------------------

        static string[] StateKeys
        {
            get { return new[] { AfkKey, PatKey, SpeakKey, LoudKey, MutedKey }.Where(k => k != "").ToArray(); }
        }

        static bool UsesPat { get { return PatKey != "" || FaceKeys.Length > 0; } }

        static string ClipDir { get { return AssetDir + "/Clips"; } }

        [MenuItem("Tools/Avatar Accessories/Accessory Reactions")]
        static void Run()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected != null ? selected.GetComponentInParent<VRCAvatarDescriptor>() : null;
            if (descriptor == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            Debug.Log(Setup(descriptor.gameObject));
        }

        public static string Setup(GameObject root)
        {
            var refusal = Validate(root);
            if (refusal != null) return "refused: " + refusal;

            var log = new System.Text.StringBuilder("Accessory reactions:\n");
            EnsureFolder(AssetDir);
            EnsureFolder(ClipDir);
            var accessory = root.transform.Find(Accessory);
            var head = root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);

            var merge = GetOrAdd<ModularAvatarMergeAnimator>(accessory.gameObject);
            Undo.RecordObject(merge, "Accessory FX");
            merge.animator = BuildStateController();
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = true;
            log.Append("  " + Accessory + "_FX: " + string.Join(" > ", StateNames()) + "\n");

            if (FaceKeys.Length > 0)
            {
                var face = GetOrAdd<ModularAvatarMergeAnimator>(Child(accessory, "Face"));
                Undo.RecordObject(face, "Accessory face");
                face.animator = BuildFaceController();
                face.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                face.pathMode = MergeAnimatorPathMode.Absolute;
                face.matchAvatarWriteDefaults = true;
                face.deleteAttachedAnimator = true;
                face.layerPriority = FaceLayerPriority;
                log.Append("  " + Accessory + "_Face: " + FaceKeys.Length + " face keys while patted, layer priority " + FaceLayerPriority + "\n");
            }

            if (UsesPat)
            {
                var pat = Beside(accessory, PatObject);
                Undo.RecordObject(pat, "Head pat");
                pat.SetPositionAndRotation(head.position, head.rotation);
                SetupReceiver(GetOrAdd<VRCContactReceiver>(pat.gameObject));
                SetupHeadProxy(GetOrAdd<ModularAvatarBoneProxy>(pat.gameObject));
                log.Append("  " + PatObject + ": receiver -> " + PatParam + ", others' hands only, bone proxy -> Head\n");

                var template = root.transform.Find(AccessoryMenu);
                var installer = SetupPatMenu(Beside(accessory, PatMenu).gameObject, template, pat.gameObject);
                if (MenuFolder != "") log.Append("  " + PatMenu + ": " + AddInstallTarget(root.transform.Find(MenuFolder), installer) + "\n");
                else log.Append("  " + PatMenu + ": in the root menu\n");
            }

            log.Append(Strip(root));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            log.Append("Next: bake, run the FX by hand, render each state (vrchat-avatar-unity BAKE.md, TEST.md).");
            return log.ToString();
        }

        static string Validate(GameObject root)
        {
            if (!AssetDir.StartsWith(UserDir)) return "AssetDir must be under " + UserDir;
            var accessory = root.transform.Find(Accessory);
            if (accessory == null) return "no '" + Accessory + "' under " + root.name + "; set Accessory to its path from the avatar root";
            var smr = Smr(accessory, Renderer);
            if (smr == null) return "no SkinnedMeshRenderer at " + Accessory + "/" + Renderer;
            var missing = StateKeys.Where(k => smr.sharedMesh.GetBlendShapeIndex(k) < 0).ToArray();
            if (missing.Length > 0)
                return Renderer + " has no shape key " + string.Join(", ", missing) + " (it has: " +
                       string.Join(", ", Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(i => smr.sharedMesh.GetBlendShapeName(i)).ToArray()) +
                       "); set the key to \"\" to leave a state out";
            if (FaceKeys.Length > 0)
            {
                var face = Smr(root.transform, FaceRenderer);
                if (face == null) return "no SkinnedMeshRenderer at " + FaceRenderer;
                var faceMissing = FaceKeys.Where(k => face.sharedMesh.GetBlendShapeIndex(k.Key) < 0).Select(k => k.Key).ToArray();
                if (faceMissing.Length > 0) return FaceRenderer + " has no shape key " + string.Join(", ", faceMissing);
            }
            if (UsesPat && MenuFolder != "" && root.transform.Find(MenuFolder) == null)
                return "no menu folder '" + MenuFolder + "' under " + root.name + "; set MenuFolder, or \"\" for the root menu";
            if (StripParameters.Length > 0)
            {
                var parameters = root.GetComponent<VRCAvatarDescriptor>().expressionParameters;
                if (parameters != null && parameters.parameters.Any(p => StripParameters.Contains(p.name)) &&
                    !AssetDatabase.GetAssetPath(parameters).StartsWith(UserDir))
                    return "the expression parameters (" + AssetDatabase.GetAssetPath(parameters) + ") are not a copy in " + UserDir +
                           "; copy them there and point the descriptor at the copy, or clear StripParameters";
            }
            return null;
        }

        static SkinnedMeshRenderer Smr(Transform parent, string path)
        {
            var t = parent.Find(path);
            return t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
        }

        static IEnumerable<string> StateNames()
        {
            if (AfkKey != "") yield return "AFK";
            if (PatKey != "") yield return "Pat";
            if (SpeakKey != "") yield return "Speak";
            if (MutedKey != "") yield return "Muted";
            yield return "Normal";
        }

        // ------------------------------------------------------------------ the states

        static AnimatorController BuildStateController()
        {
            var keys = StateKeys;
            System.Func<string, float, string[], AnimationClip> clip = (name, length, on) =>
                Clip(Accessory + "_" + name, length, keys.Select(k => Curve(Renderer, k, on.Contains(k) ? 100f : 0f)));

            var ac = FreshController(AssetDir + "/" + Accessory + "_FX.controller");
            if (AfkKey != "") ac.AddParameter("AFK", AnimatorControllerParameterType.Bool);
            if (MutedKey != "") ac.AddParameter("MuteSelf", AnimatorControllerParameterType.Bool);
            if (SpeakKey != "") ac.AddParameter("Voice", AnimatorControllerParameterType.Float);
            if (PatKey != "") ac.AddParameter(PatParam, AnimatorControllerParameterType.Float);
            ac.AddLayer(Accessory + " Reactions");
            var sm = ac.layers[0].stateMachine;

            var idle = State(sm, "Normal", clip("Normal", 0, new string[0]), 0, 0);
            sm.defaultState = idle;

            if (MutedKey != "")
            {
                var muted = State(sm, "Muted", clip("Muted", 0, new[] { MutedKey }), 1, 1);
                Go(idle, muted, Fade, Cond("MuteSelf", AnimatorConditionMode.If, 0));
                Go(muted, idle, Fade, Cond("MuteSelf", AnimatorConditionMode.IfNot, 0));
            }
            if (SpeakKey != "")
            {
                Motion speakMotion = clip("Speak", 0, new[] { SpeakKey });
                if (LoudKey != "")
                {
                    var tree = new BlendTree
                    {
                        name = "Speak (Voice)", blendType = BlendTreeType.Simple1D, blendParameter = "Voice",
                        useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
                    };
                    AssetDatabase.AddObjectToAsset(tree, ac);
                    tree.AddChild(speakMotion, 0f);
                    tree.AddChild(clip("SpeakLoud", 0, new[] { SpeakKey, LoudKey }), VoiceFull);
                    speakMotion = tree;
                }
                var speak = State(sm, "Speak", speakMotion, 1, -1);
                var hold = State(sm, "Speak Hold", clip("SpeakHold", SpeakHold, new[] { SpeakKey }), 2, -1);
                Go(idle, speak, Fade, Cond("Voice", AnimatorConditionMode.Greater, VoiceOn));
                Go(speak, hold, Fade, Cond("Voice", AnimatorConditionMode.Less, VoiceOn));
                Go(hold, speak, Fade, Cond("Voice", AnimatorConditionMode.Greater, VoiceOn));
                After(hold, idle, Fade);
            }
            if (PatKey != "")
            {
                var pat = State(sm, "Pat", clip("Pat", 0, new[] { PatKey }), -1, 2);
                var hold = State(sm, "Pat Hold", clip("PatHold", PatHold, new[] { PatKey }), 0, 2);
                var conds = new List<AnimatorCondition>();
                if (AfkKey != "") conds.Add(Cond("AFK", AnimatorConditionMode.IfNot, 0));
                conds.Add(Cond(PatParam, AnimatorConditionMode.Greater, 0.5f));
                Any(sm, pat, conds.ToArray());
                Go(pat, hold, Fade, Cond(PatParam, AnimatorConditionMode.Less, 0.5f));
                After(hold, idle, Fade);
            }
            if (AfkKey != "")
            {
                var afk = State(sm, "AFK", clip("AFK", 0, new[] { AfkKey }), -1, -2);
                Any(sm, afk, Cond("AFK", AnimatorConditionMode.If, 0));
                Go(afk, idle, Fade, Cond("AFK", AnimatorConditionMode.IfNot, 0));
            }
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ------------------------------------------------------------------ the face

        static AnimatorController BuildFaceController()
        {
            System.Func<string, float, AnimationClip> clip = (name, length) =>
                Clip(name, length, FaceKeys.Select(k => Curve(FaceRenderer, k.Key, k.Value)));

            var ac = FreshController(AssetDir + "/" + Accessory + "_Face.controller");
            ac.AddParameter("AFK", AnimatorControllerParameterType.Bool);
            ac.AddParameter(PatParam, AnimatorControllerParameterType.Float);
            ac.AddLayer(Accessory + " Face");
            var sm = ac.layers[0].stateMachine;
            var off = State(sm, "Off", null, 0, 0);   // no motion: writes nothing, the expression tool shows through
            var on = State(sm, "Face", clip(Accessory + "_Face", 0), 1, 0);
            var hold = State(sm, "Face Hold", clip(Accessory + "_FaceHold", PatHold), 2, 0);
            sm.defaultState = off;
            Go(off, on, 0.25f, Cond("AFK", AnimatorConditionMode.IfNot, 0), Cond(PatParam, AnimatorConditionMode.Greater, 0.5f));
            Go(on, off, 0.2f, Cond("AFK", AnimatorConditionMode.If, 0));
            Go(on, hold, 0f, Cond(PatParam, AnimatorConditionMode.Less, 0.5f));
            Go(hold, on, 0f, Cond(PatParam, AnimatorConditionMode.Greater, 0.5f));
            After(hold, off, 0.4f);
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ------------------------------------------------------------------ animator helpers

        // Reuse the asset (keeps its GUID): drop every sub-asset, layer and parameter.
        static AnimatorController FreshController(string path)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != null && o != ac))
            {
                AssetDatabase.RemoveObjectFromAsset(sub);
                Object.DestroyImmediate(sub, true);
            }
            ac.layers = new AnimatorControllerLayer[0];
            ac.parameters = new AnimatorControllerParameter[0];
            return ac;
        }

        struct Binding { public string Path, Key; public float Value; }

        static Binding Curve(string path, string key, float value) { return new Binding { Path = path, Key = key, Value = value }; }

        // Reuse the clip asset (keeps its GUID); a constant curve per key, `length` seconds long.
        static AnimationClip Clip(string name, float length, IEnumerable<Binding> curves)
        {
            var path = ClipDir + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            var end = length > 0 ? length : 1f / 60f;
            foreach (var c in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(c.Path, typeof(SkinnedMeshRenderer), "blendShape." + c.Key),
                    AnimationCurve.Constant(0f, end, c.Value));
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, int x, int y)
        {
            var s = sm.AddState(name, new Vector3(300 + 250 * x, 100 + 80 * y));
            s.motion = motion;
            s.writeDefaultValues = true;
            return s;
        }

        static void Any(AnimatorStateMachine sm, AnimatorState to, params AnimatorCondition[] conds)
        {
            var t = sm.AddAnyStateTransition(to);
            t.canTransitionToSelf = false;
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = Fade;
            t.conditions = conds;
        }

        static void Go(AnimatorState from, AnimatorState to, float duration, params AnimatorCondition[] conds)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.conditions = conds;
        }

        static void After(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = duration;
        }

        // ------------------------------------------------------------------ scene objects

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // A sibling of the accessory, right after it. One left inside the accessory by an earlier layout
        // is moved out instead of making a second one.
        static Transform Beside(Transform accessory, string name)
        {
            var parent = accessory.parent;
            var t = parent.Find(name);
            if (t != null) return t;
            t = accessory.Find(name);
            if (t != null) Undo.SetTransformParent(t, parent, name);
            else t = Child(parent, name).transform;
            t.SetSiblingIndex(accessory.GetSiblingIndex() + 1);
            return t;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        // Other people's hands only: touching your own head is often an expression lock (FaceEmo).
        static void SetupReceiver(VRCContactReceiver receiver)
        {
            var so = new SerializedObject(receiver);
            so.FindProperty("rootTransform").objectReferenceValue = null;
            SetEnum(so.FindProperty("shapeType"), "Sphere");
            so.FindProperty("radius").floatValue = PatRadius;
            so.FindProperty("position").vector3Value = PatPosition;
            so.FindProperty("localOnly").boolValue = false;
            var tags = so.FindProperty("collisionTags");
            tags.arraySize = 2;
            tags.GetArrayElementAtIndex(0).stringValue = "Hand";
            tags.GetArrayElementAtIndex(1).stringValue = "Finger";
            so.FindProperty("allowSelf").boolValue = false;
            so.FindProperty("allowOthers").boolValue = true;
            SetEnum(so.FindProperty("receiverType"), "Constant");
            so.FindProperty("parameter").stringValue = PatParam;
            so.ApplyModifiedProperties();
        }

        // Parented to the head at build time, keeping the pose set in the scene.
        static void SetupHeadProxy(ModularAvatarBoneProxy proxy)
        {
            Undo.RecordObject(proxy, "Head pat bone proxy");
            proxy.boneReference = HumanBodyBones.Head;
            proxy.subPath = "";
            proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepWorldPose;
        }

        static void SetEnum(SerializedProperty p, string name)
        {
            p.enumValueIndex = System.Array.IndexOf(p.enumNames, name);
        }

        // A toggle that switches the receiver off: same saved / synced / default settings as the
        // accessory's own toggle when there is one, else saved, synced, off by default.
        static ModularAvatarMenuInstaller SetupPatMenu(GameObject go, Transform template, GameObject receiver)
        {
            var item = GetOrAdd<ModularAvatarMenuItem>(go);
            var source = template != null ? template.GetComponent<ModularAvatarMenuItem>() : null;
            if (source != null) EditorUtility.CopySerialized(source, item);
            var so = new SerializedObject(item);
            so.FindProperty("Control.name").stringValue = PatMenu;
            so.FindProperty("Control.parameter.name").stringValue = "";   // MA makes one for the toggle
            if (source == null)
            {
                so.FindProperty("Control.type").intValue = (int)VRCExpressionsMenu.Control.ControlType.Toggle;
                so.FindProperty("Control.value").floatValue = 1f;
                so.FindProperty("isSynced").boolValue = true;
                so.FindProperty("isSaved").boolValue = true;
                so.FindProperty("isDefault").boolValue = false;
            }
            so.ApplyModifiedProperties();
            var installer = GetOrAdd<ModularAvatarMenuInstaller>(go);
            var toggle = GetOrAdd<ModularAvatarObjectToggle>(go);
            Undo.RecordObject(toggle, "HeadPat toggle");
            toggle.Objects = new List<ToggledObject>
                { new ToggledObject { Object = new AvatarObjectReference(receiver), Active = false } };
            return installer;
        }

        // The folder holds Menu Install Targets named after their items; add one after AccessoryMenu's.
        // ModularAvatarMenuInstallTarget is internal to MA, hence the type lookup and SerializedObject.
        static string AddInstallTarget(Transform folder, ModularAvatarMenuInstaller installer)
        {
            var type = System.Type.GetType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstallTarget, nadena.dev.modular-avatar.core", true);
            var go = Child(folder, installer.gameObject.name);
            var target = go.GetComponent(type);
            if (target == null) target = Undo.AddComponent(go, type);
            var so = new SerializedObject(target);
            so.FindProperty("installer").objectReferenceValue = installer;
            so.ApplyModifiedProperties();
            var anchor = folder.Find(AccessoryMenu);
            if (anchor != null) go.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            return "in " + folder.name + (anchor != null ? ", after " + AccessoryMenu : "");
        }

        static string Strip(GameObject root)
        {
            var log = new System.Text.StringBuilder();
            foreach (var path in StripObjects)
            {
                var t = root.transform.Find(path);
                if (t == null) continue;
                Undo.RecordObject(t.gameObject, "Retire old head pat");
                t.gameObject.tag = "EditorOnly";
                t.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                log.Append("  retired (EditorOnly): " + path + "\n");
            }
            var parameters = root.GetComponent<VRCAvatarDescriptor>().expressionParameters;
            if (parameters != null && parameters.parameters.Any(p => StripParameters.Contains(p.name)))
            {
                Undo.RecordObject(parameters, "Retire old head pat");
                parameters.parameters = parameters.parameters.Where(p => !StripParameters.Contains(p.name)).ToArray();
                EditorUtility.SetDirty(parameters);
                log.Append("  parameters removed: " + string.Join(", ", StripParameters) + "\n");
            }
            return log.ToString();
        }
    }
}
