// Checks an avatar for mistakes that broke real avatars. Run it before a commit and before an upload. Each
// finding names the object, the problem and the fix:
//   error    breaks the avatar or blocks the upload;
//   warning  very likely a mistake;
//   note     worth knowing: costs performance, or may drift.
//
// From code (execute_code):  return AvatarAgentKit.Editor.CheckAvatar.Run("MyAvatar");
// Menu: Tools/Avatar Agent Kit/Check Avatar; the result goes to the Console.
// It checks the scene avatar, and its _bakecheck clone when there is one: some mistakes only show after the
// build. The clone is used as it is; bake again first (Bake.Run) if anything changed since.
//
// Scene avatar: missing scripts; renderers without a mesh, empty material slots, missing shaders, more material
// slots than submeshes; with Modular Avatar: Object Toggle targets that are missing, saved in the toggled
// state already, or above the toggle itself, and Bone Proxies under a toggled or hidden object; PhysBones
// that move nothing, and PhysBones and colliders on hidden objects (with a bake: only those that survive it;
// AAO removes many); unlocked VRC constraints; a blueprint ID that another git branch's copy of the scene also has.
// Bake: menu controls whose parameter doesn't exist; more synced bits than VRChat allows; FX bindings to
// objects that don't exist; Write Defaults that differ between FX layers; lilToon renderers that a
// light-setting gimmick (Surin's lilToon light setting) doesn't animate, or animates as another renderer type.
//
// Verified on: Unity 2022.3.22f1, VRChat SDK 3.10.5, MA 1.18; a MANUKA avatar with MA toggles, Bone Proxies,
// FaceEmo and three git branches.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Core;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Debug = UnityEngine.Debug;

namespace AvatarAgentKit.Editor
{
    public static class CheckAvatar
    {
        const string Tool = "CheckAvatar";
        const int MaxListed = 6;

        enum Level { Error, Warning, Note }

        class Findings
        {
            readonly List<(Level level, string where, string text)> items = new List<(Level, string, string)>();
            public void Add(Level level, string where, string text) => items.Add((level, where, text));
            public int Count(Level level) => items.Count(i => i.level == level);

            public void AppendTo(StringBuilder sb)
            {
                foreach (var i in items.OrderBy(i => i.level))
                {
                    sb.Append(i.level.ToString().ToLowerInvariant().PadRight(9));
                    if (!string.IsNullOrEmpty(i.where)) sb.Append(i.where).Append(": ");
                    sb.AppendLine(i.text);
                }
            }
        }

        public static string Run(string avatar = null)
        {
            if (Targets.IsBakedName(avatar)) avatar = Targets.SourceName(avatar);
            var err = Targets.FindSource(avatar, out var source);
            if (err != null) return Tool + ": refused: " + err;

            var f = new Findings();
            var root = source.transform;
            Scripts(root, f);
            Renderers(root, f);
#if AAK_MA
            ModularAvatar(root, f);
#endif
            var clone = Targets.RootsNamed(source.name + Targets.BakeSuffix).FirstOrDefault();
            PhysBones(root, clone, f);
            Constraints(root, f);
            Blueprint(source, f);
            if (clone != null) Baked(clone, f);

            var sb = new StringBuilder();
            sb.Append(Tool).Append(": ").Append(Targets.Quote(source.name)).Append(": ")
                .Append(Plural(f.Count(Level.Error), "error")).Append(", ")
                .Append(Plural(f.Count(Level.Warning), "warning")).Append(", ")
                .Append(Plural(f.Count(Level.Note), "note"))
                .AppendLine(clone != null
                    ? " (scene avatar and its bake " + Targets.Quote(clone.name) + ")"
                    : " (scene avatar only; bake for the build checks: " + Targets.Ns + "Bake.Run(" + Targets.Quote(source.name) + "))");
#if !AAK_MA
            sb.AppendLine("Modular Avatar is not installed: its checks were skipped.");
#endif
            f.AppendTo(sb);
            return sb.ToString().TrimEnd();
        }

        static void Scripts(Transform root, Findings f)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n > 0)
                    f.Add(Level.Error, Path(t, root), Plural(n, "missing script") + ": VRChat refuses the upload. "
                        + "Fix: install the package the component came from, or remove the component.");
            }
        }

        static void Renderers(Transform root, Findings f)
        {
            var brokenShaders = new Dictionary<Material, List<string>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var where = Path(r.transform, root);
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    f.Add(Level.Error, where, "renderer without a mesh: it shows nothing. Fix: assign the mesh, or remove the renderer.");
                    continue;
                }
                var mats = r.sharedMaterials;
                var empty = Enumerable.Range(0, mats.Length).Where(i => mats[i] == null).ToList();
                if (empty.Count > 0)
                    f.Add(Level.Warning, where, "empty material slot " + string.Join(", ", empty) + ": that part shows nothing (or pink). Fix: assign a material.");
                if (mats.Length > mesh.subMeshCount)
                    f.Add(Level.Note, where, mats.Length + " material slots for " + (mesh.subMeshCount == 1 ? "1 submesh" : mesh.subMeshCount + " submeshes")
                        + ": the extra slots draw the last submesh again and count its triangles again. Fix: remove them, unless the extra pass is wanted.");
                foreach (var m in mats)
                    if (m != null && (m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                    {
                        if (!brokenShaders.TryGetValue(m, out var users)) brokenShaders[m] = users = new List<string>();
                        users.Add(where);
                    }
            }
            foreach (var kv in brokenShaders)
                f.Add(Level.Error, AssetDatabase.GetAssetPath(kv.Key), "material with a missing shader, shows pink; used by " + Listed(kv.Value)
                    + ". Fix: install the shader package (often lilToon), or pick the shader again.");
        }

#if AAK_MA
        static void ModularAvatar(Transform root, Findings f)
        {
            var toggled = new HashSet<Transform>();
            foreach (var toggle in root.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarObjectToggle>(true))
            {
                var where = Path(toggle.transform, root);
                foreach (var entry in toggle.Objects)
                {
                    var target = entry.Object?.Get(toggle);
                    if (target == null)
                    {
                        f.Add(Level.Error, where, "MA Object Toggle target " + Targets.Quote(entry.Object?.referencePath ?? "")
                            + " doesn't exist: that entry does nothing. Fix: pick the object again.");
                        continue;
                    }
                    toggled.Add(target.transform);
                    var targetPath = Path(target.transform, root);
                    if (!entry.Active && toggle.transform.IsChildOf(target.transform))
                        f.Add(Level.Error, where, "MA Object Toggle switches off " + Targets.Quote(targetPath)
                            + ", which contains the toggle itself: the menu item turns itself off. Fix: move the toggle beside what it switches.");
                    else if (target.activeSelf == entry.Active)
                        f.Add(Level.Warning, where, "MA Object Toggle sets " + Targets.Quote(targetPath) + " " + (entry.Active ? "on" : "off")
                            + ", but it is saved " + (entry.Active ? "on" : "off") + " already: the toggle changes nothing. "
                            + "Fix: save the target in the other state; the toggle's default decides how the avatar starts.");
                }
            }

            foreach (var proxy in root.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarBoneProxy>(true))
            {
                var where = Path(proxy.transform, root);
                if (proxy.target == null && proxy.boneReference == HumanBodyBones.LastBone)
                    f.Add(Level.Error, where, "MA Bone Proxy without a target bone: it does nothing. Fix: set the bone.");
                if (toggled.Contains(proxy.transform)) continue;
                for (var a = proxy.transform.parent; a != null && a != root; a = a.parent)
                {
                    var isToggled = toggled.Contains(a);
                    if (!isToggled && a.gameObject.activeSelf) continue;
                    f.Add(Level.Warning, where, "MA Bone Proxy under " + Targets.Quote(Path(a, root)) + ", which is "
                        + (isToggled ? "toggled" : "hidden") + ": at build the proxy moves this object under the bone, out of it, so "
                        + (isToggled ? "the toggle no longer switches it" : "hiding it no longer hides this")
                        + ". Fix: put the Bone Proxy on " + Targets.Quote(a.name) + " itself, or toggle this object directly.");
                    break;
                }
            }
        }
#endif

        static void PhysBones(Transform root, GameObject clone, Findings f)
        {
            // What can make a moving bone visible: skin weights, a mesh hanging under it, a constraint reading it.
            var weighted = new HashSet<Transform>();
            var meshes = new List<Transform>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendIndices))
                {
                    var bones = smr.bones;
                    foreach (var w in smr.sharedMesh.GetAllBoneWeights())
                        if (w.weight > 0f && w.boneIndex < bones.Length && bones[w.boneIndex] != null) weighted.Add(bones[w.boneIndex]);
                }
                else meshes.Add(r.transform);
            }
            var sources = new HashSet<Transform>();
            foreach (var c in root.GetComponentsInChildren<VRCConstraintBase>(true))
                foreach (var s in c.Sources) if (s.SourceTransform != null) sources.Add(s.SourceTransform);
            foreach (var c in root.GetComponentsInChildren<IConstraint>(true))
                for (int i = 0; i < c.sourceCount; i++) { var s = c.GetSource(i).sourceTransform; if (s != null) sources.Add(s); }

            var dead = new List<Transform>();
            var deadWithParameter = new List<Transform>();
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                var start = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                var ignored = pb.ignoreTransforms?.Where(t => t != null).ToList() ?? new List<Transform>();
                var chain = start.GetComponentsInChildren<Transform>(true).Where(t => !ignored.Any(t.IsChildOf)).ToList();
                var moves = chain.Any(t => weighted.Contains(t) || sources.Contains(t))
                    || meshes.Any(m => chain.Any(m.IsChildOf));
                if (!moves) (string.IsNullOrEmpty(pb.parameter) ? dead : deadWithParameter).Add(pb.transform);
            }
            DeadPhysBones(root, clone, dead, f);
            DeadWithParameter(root, clone, deadWithParameter, f);
            HiddenDynamics(root, clone, f);
        }

        // PhysBones and colliders on objects hidden in the avatar. VRChat counts them, but AAO removes the ones that
        // are never switched on, so with a bake only those that survive it are reported.
        static void HiddenDynamics(Transform root, GameObject clone, Findings f)
        {
            var bones = root.GetComponentsInChildren<VRCPhysBoneBase>(true).Where(c => Hidden(c.transform, root)).Select(c => c.transform).ToList();
            var colliders = root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true).Where(c => Hidden(c.transform, root)).Select(c => c.transform).ToList();
            if (clone != null)
            {
                var bakedBones = new HashSet<string>(clone.GetComponentsInChildren<VRCPhysBoneBase>(true).Select(c => c.name));
                var bakedColliders = new HashSet<string>(clone.GetComponentsInChildren<VRCPhysBoneColliderBase>(true).Select(c => c.name));
                bones = bones.Where(t => bakedBones.Contains(t.name)).ToList();
                colliders = colliders.Where(t => bakedColliders.Contains(t.name)).ToList();
            }
            if (bones.Count + colliders.Count == 0) return;
            var what = string.Join(" and ", new[] { Count(bones.Count, "PhysBone"), Count(colliders.Count, "collider") }.Where(x => x != null));
            var paths = Listed(bones.Concat(colliders).Select(t => Path(t, root)).ToList());
            f.Add(Level.Note, "", clone != null
                ? what + " on objects hidden in the avatar are still in the bake, so they count toward performance even while hidden: " + paths
                    + ". That is the price of a toggle; for parts that never show, remove them."
                : what + " on objects hidden in the avatar: " + paths
                    + ". They count toward performance if the build keeps them (AAO removes those never switched on); bake to see.");
        }

        // AAO keeps a PhysBone that has a parameter even when it moves nothing (a removed tail or ears), so it counts.
        static void DeadWithParameter(Transform root, GameObject clone, List<Transform> dead, Findings f)
        {
            if (clone != null)
            {
                var baked = new HashSet<string>(clone.GetComponentsInChildren<VRCPhysBoneBase>(true).Select(pb => pb.name));
                dead = dead.Where(t => baked.Contains(t.name)).ToList();
            }
            if (dead.Count == 0) return;
            f.Add(Level.Warning, "", Plural(dead.Count, "PhysBone") + " move nothing but have a parameter, so AAO keeps them and they count toward performance: "
                + Listed(dead.Select(t => Path(t, root)).ToList()) + ". Fix: if their part is gone, remove them; keep them only if the parameter drives something.");
        }

        // Hidden within the avatar: the object or a parent below the root is inactive. (Bake.Run hides the root.)
        static bool Hidden(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return true;
            return false;
        }

        // PhysBones whose chain moves nothing. AAO's Trace and Optimize removes such PhysBones at build, so what
        // matters is whether they survive into the bake. A PhysBone counts as surviving when the bake has one on an
        // object of the same name (Modular Avatar can move and rename the path).
        static void DeadPhysBones(Transform root, GameObject clone, List<Transform> dead, Findings f)
        {
            if (dead.Count == 0) return;
            const string what = " move nothing: no skin weights, mesh, constraint or parameter on their chains";
            var paths = dead.Select(t => Path(t, root)).ToList();
            if (clone != null)
            {
                var baked = new HashSet<string>(clone.GetComponentsInChildren<VRCPhysBoneBase>(true).Select(pb => pb.name));
                var kept = dead.Where(t => baked.Contains(t.name)).Select(t => Path(t, root)).ToList();
                var removed = paths.Except(kept).ToList();
                if (kept.Count > 0)
                    f.Add(Level.Warning, "", Plural(kept.Count, "PhysBone") + what + ", and they are still in the bake, where they count toward performance: "
                        + Listed(kept) + ". Fix: remove them if their parts were deleted or merged away.");
                if (removed.Count > 0)
                    f.Add(Level.Note, "", Plural(removed.Count, "PhysBone") + what + "; the build removes them (AAO), so they cost nothing: " + Listed(removed) + ".");
                return;
            }
            var aao = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Anatawa12.AvatarOptimizer.TraceAndOptimize")).FirstOrDefault(t => t != null);
            if (aao != null && root.GetComponentsInChildren(aao, true).Length > 0)
                f.Add(Level.Note, "", Plural(dead.Count, "PhysBone") + what + ": " + Listed(paths)
                    + ". AAO Trace and Optimize should remove them at build; bake to confirm.");
            else
                f.Add(Level.Warning, "", Plural(dead.Count, "PhysBone") + what + "; they count toward performance: " + Listed(paths)
                    + ". Fix: remove them if their parts were deleted or merged away, or let AAO Trace and Optimize remove them.");
        }

        static void Constraints(Transform root, Findings f)
        {
            var unlocked = root.GetComponentsInChildren<VRCConstraintBase>(true).Where(c => !c.Locked).Select(c => Path(c.transform, root)).ToList();
            if (unlocked.Count > 0)
                f.Add(Level.Note, "", Plural(unlocked.Count, "VRC constraint") + " unlocked: VRChat re-bakes their offsets at runtime, so they can drift from how they were set up: "
                    + Listed(unlocked) + ". Fix: activate and lock each at its intended pose.");
        }

        // The blueprint ID decides which avatar an upload overwrites. A branch made for another avatar must clear it.
        static void Blueprint(GameObject source, Findings f)
        {
            var pm = source.GetComponent<PipelineManager>();
            var id = pm != null ? pm.blueprintId : null;
            if (string.IsNullOrEmpty(id))
            {
                f.Add(Level.Note, "", "no blueprint ID: the next upload creates a new avatar.");
                return;
            }
            var scene = source.scene.path;
            var branch = Git("rev-parse --abbrev-ref HEAD")?.Trim();
            var branches = Git("for-each-ref --format=%(refname:short) refs/heads");
            if (string.IsNullOrEmpty(branch) || branches == null)
            {
                f.Add(Level.Note, "", "blueprint " + id + ": git not available, so it was not compared with other branches.");
                return;
            }
            var shared = new List<string>();
            foreach (var b in branches.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0 && s != branch))
            {
                var text = Git("show \"" + b + ":" + scene + "\"");
                if (text != null && text.Contains(id)) shared.Add(b);
            }
            if (shared.Count > 0)
                f.Add(Level.Warning, "", "blueprint " + id + " is also in " + scene + " on branch " + string.Join(", ", shared)
                    + ": uploading from either overwrites the same avatar. Fix: if this branch is another avatar, clear the ID on the root's Pipeline Manager before its first upload.");
        }

        static void Baked(GameObject clone, Findings f)
        {
            var desc = clone.GetComponent<VRCAvatarDescriptor>();
            var where = "bake";
            var ep = desc.expressionParameters;
            var known = new HashSet<string>(ep != null ? ep.parameters.Where(p => p != null).Select(p => p.name) : Enumerable.Empty<string>());
            if (ep != null && ep.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
                f.Add(Level.Error, where, "synced parameters cost " + ep.CalcTotalCost() + " bits, more than " + VRCExpressionParameters.MAX_PARAMETER_COST
                    + ": the upload fails. Fix: make parameters local (not synced), or use one int for variants instead of several bools.");
            foreach (var (path, parameter) in MenuParameters(desc.expressionsMenu, "", new HashSet<VRCExpressionsMenu>()))
                if (!known.Contains(parameter))
                    f.Add(Level.Error, where, "menu control " + Targets.Quote(path) + " uses parameter " + Targets.Quote(parameter)
                        + ", which doesn't exist: the control does nothing. Fix: add the parameter, or point the control at an existing one.");

            var fx = ReportMenu.Controller(desc, VRCAvatarDescriptor.AnimLayerType.FX);
            if (fx == null) return;
            var missing = new Dictionary<string, HashSet<string>>(); // path -> layers
            int on = 0, off = 0;
            var offLayers = new List<string>();
            foreach (var layer in fx.layers)
            {
                var states = ReportMenu.States(layer.stateMachine).ToList();
                if (states.Count > 0)
                {
                    if (states.All(s => s.writeDefaultValues)) on++;
                    else { off++; offLayers.Add(Targets.Quote(layer.name)); }
                }
                foreach (var clip in states.SelectMany(s => ReportMenu.Clips(s.motion)).Distinct())
                    foreach (var b in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                        if (!string.IsNullOrEmpty(b.path) && !ReportMenu.IsPlaceholder(b.path) && clone.transform.Find(b.path) == null)
                        {
                            if (!missing.TryGetValue(b.path, out var layers)) missing[b.path] = layers = new HashSet<string>();
                            layers.Add(Targets.Quote(layer.name));
                        }
            }
            foreach (var kv in missing)
                f.Add(Level.Warning, where, "FX layer " + string.Join(", ", kv.Value) + " animates " + Targets.Quote(kv.Key)
                    + ", which doesn't exist in the bake: that animation does nothing. Fix: the object was renamed or moved; repath the clip, or remove the binding.");
            if (on > 0 && off > 0)
                f.Add(Level.Warning, where, "Write Defaults differ between FX layers: " + on + " on, " + off + " off or mixed (" + Listed(offLayers)
                    + "). Mixed Write Defaults are a classic cause of stuck toggles and faces. Fix: keep one convention.");
            LightSetting(clone, fx, f);
        }

        // lilToon lighting properties that brightness gimmicks (Surin's lilToon light setting and the like) key on
        // every lilToon renderer.
        static readonly string[] LightProperties = { "material._LightMinLimit", "material._LightMaxLimit", "material._MonochromeLighting" };

        // A renderer such a gimmick doesn't animate ignores the brightness setting, and AAO won't merge it with the
        // renderers it does animate. That happens to parts added after the gimmick's tool ran, and to curves keyed
        // for another renderer type (a MeshRenderer since turned into a SkinnedMeshRenderer): those animate nothing,
        // and the build drops them, so they count as missing here.
        static void LightSetting(GameObject clone, UnityEditor.Animations.AnimatorController fx, Findings f)
        {
            var animated = new Dictionary<string, HashSet<Type>>(); // renderer path -> binding types
            var layers = new HashSet<string>();
            foreach (var layer in fx.layers)
                foreach (var clip in ReportMenu.States(layer.stateMachine).SelectMany(s => ReportMenu.Clips(s.motion)).Distinct())
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        if (LightProperties.Contains(b.propertyName))
                        {
                            if (!animated.TryGetValue(b.path, out var types)) animated[b.path] = types = new HashSet<Type>();
                            types.Add(b.type);
                            layers.Add(Targets.Quote(layer.name));
                        }
            if (animated.Count < 2) return; // not a gimmick that covers every renderer

            var root = clone.transform;
            var missing = new List<string>();
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if (!r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.Contains("lilToon"))) continue;
                var path = Path(r.transform, root);
                if (!animated.TryGetValue(path, out var types) || !types.Any(t => t.IsAssignableFrom(r.GetType()))) missing.Add(path);
            }
            if (missing.Count > 0)
                f.Add(Level.Warning, "bake", Plural(missing.Count, "lilToon renderer") + " not animated by the light setting in FX layer "
                    + string.Join(", ", layers.OrderBy(s => s)) + ": " + Listed(missing)
                    + ". They ignore the brightness setting, and AAO won't merge them with the rest. Usually parts added after the light-setting tool ran,"
                    + " or curves keyed for another renderer type (a MeshRenderer turned into a SkinnedMeshRenderer). Fix: rerun the light-setting tool,"
                    + " or add or retype their curves (vrchat-avatar-unity GOTCHAS, lilToon); leave a part out only if it should stay unaffected (a glowing part).");
        }

        static IEnumerable<(string path, string parameter)> MenuParameters(VRCExpressionsMenu menu, string prefix, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) yield break;
            foreach (var c in menu.controls)
            {
                var path = prefix + c.name;
                if (c.parameter != null && !string.IsNullOrEmpty(c.parameter.name)) yield return (path, c.parameter.name);
                if (c.subParameters != null)
                    foreach (var sp in c.subParameters)
                        if (sp != null && !string.IsNullOrEmpty(sp.name)) yield return (path, sp.name);
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    foreach (var x in MenuParameters(c.subMenu, path + "/", seen)) yield return x;
            }
        }

        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(), StandardOutputEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var errTask = p.StandardError.ReadToEndAsync();
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    errTask.Wait(1000);
                    return p.ExitCode == 0 ? output : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string Path(Transform t, Transform root) => t == root ? "(root)" : AnimationUtility.CalculateTransformPath(t, root);

        static string Listed(List<string> items) =>
            string.Join(", ", items.Take(MaxListed).Select(Targets.Quote)) + (items.Count > MaxListed ? " and " + (items.Count - MaxListed) + " more" : "");

        static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");
        static string Count(int n, string word) => n == 0 ? null : Plural(n, word);

        [MenuItem("Tools/Avatar Agent Kit/Check Avatar", false, 90)]
        static void CheckMenu() => Debug.Log(Run(Targets.SelectedRootName()));
    }
}
