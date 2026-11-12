// Fits the PhysBones into Good (8 components, 64 transforms, 8 colliders, 128 collision checks).
// Menu: Tools/Avatar Good/PhysBone Budget (select the avatar root, or anything under it).
// Rerunnable: every run deletes the MergePB_* objects it made before and builds them again.
//
// Four levers, each a list in CONFIG:
//   - StaticRoots: PhysBones deleted, the part stops moving (collar flaps, ribbons, a butt).
//   - Groups: AAO Merge PhysBone. Chains with similar behaviour share one component; every setting
//     that differs needs an override, so each group gets one compromise value per setting.
//   - MergedBones: AAO Merge Bone, a bone merged into its parent. Every other bone of a long hair
//     chain halves its transforms; the hair moves less and the tips are stiffer.
//   - Tweaks: settings on one PhysBone that stays alone (a braid that needs body colliders).
// Get the user's yes for each part that goes static or moves less before running it.
//
// Merge Physbone rules (AAO 1.9):
//   - MakeParentUnder null: makeParent = false. All roots need the same parent, and AAO adds a
//     PhysBoneRoot under it. Loose groups sit under a "PhysBone Merges" object at the avatar root.
//   - MakeParentUnder set: makeParent = true. The roots move under the merge object, which is created
//     under that transform, so pick one that stays as still as the old parents.
//   - Left/right chains often mirror their limit rotation in z; a merged PhysBone has one value.
//   - Merged chains can be grabbed only one at a time.
// End bones: Trace and Optimize swaps a chain's unweighted leaf bone for an endpoint, but only when all
// leaves of one PhysBone sit at the same local position, which merged groups of different-length
// chains don't (transforms went up 220 -> 249 on the Green build). So for groups whose leaves are all
// unweighted, the leaves are tagged EditorOnly and the merged PhysBone gets the average leaf offset as
// its endpoint, along local +Y. Check the leaf axis on your rig.
//
// Collision checks are about the sum over PhysBones of transforms x colliders, so give each group only
// the colliders it actually touches. Collider names are looked up among active colliders.
//
// CONFIG holds the Green build's values (MANUKA with MARIYURI side-braid hair) as the example: hair in
// three groups, breast and cheek pinch kept, collar, ribbon and butt static, braid alone with body
// colliders, 22 hair-tip bones merged. Result: 7 components, 55 transforms, 7 colliders, 92 checks.

using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarGood.Editor
{
    public static class PhysBoneBudget
    {
        class Group
        {
            public string Name;
            public string[] Roots;                  // PhysBone root transform names
            public string MakeParentUnder;          // null: makeParent = false
            public float? Pull, Spring, Stiffness, Gravity, Immobile, Radius, MaxAngleX;
            public VRCPhysBoneBase.LimitType? Limit;
            public Vector3? LimitRotation;
            public string[] Colliders;              // null: keep (AAO requires them to match)
        }

        class Tweak
        {
            public string Root;
            public float? Radius, Gravity, MaxAngleX;
            public string[] Colliders;              // null: keep
        }

        // ---- CONFIG: replace with your avatar's -------------------------------------------------
        const string Prefix = "MergePB_";
        const string LooseHolder = "PhysBone Merges";

        static readonly string[] StaticRoots =
        {
            "Collar_1.002.L", "Collar_1.002.R", "Collar_2.002.L", "Collar_2.002.R",   // sailor collar flaps
            "C_Ribbon.001",                                                          // neck ribbon
            "Butt_L", "Butt_R",                                                      // hidden by the skirt
        };

        static readonly Group[] Groups =
        {
            new Group { Name = "Breast", Roots = new[] { "Breast_L", "Breast_R" }, Colliders = new[] { ">AL_C", ">AR_C" } },
            new Group { Name = "Cheek", Roots = new[] { "Cheek_root_L", "Cheek_root_R" } },
            new Group
            {
                // fringe: the limit rotation keeps it out of the face
                Name = "Front", Roots = new[] { "Bang", "Bang_L", "Bang_R" },
                Pull = 0.3f, Spring = 0.15f, Stiffness = 0.3f, Gravity = 0.2f, Immobile = 0.2f, Radius = 0.008f,
                Limit = VRCPhysBoneBase.LimitType.Angle, MaxAngleX = 50f, LimitRotation = new Vector3(-45, 0, 0),
            },
            new Group
            {
                Name = "Back",
                Roots = new[] { "P_L", "P_L.002", "P_L.003", "P_L.005", "P_L.006", "P_L.010",
                                "P_R", "P_R.002", "P_R.003", "P_R.005", "P_R.006", "P_R.010" },
                Pull = 0.15f, Spring = 0.5f, Stiffness = 0.5f, Gravity = 0.2f, Immobile = 0f, Radius = 0.005f,
                Limit = VRCPhysBoneBase.LimitType.Angle, MaxAngleX = 45f, LimitRotation = new Vector3(-15, 0, 0),
            },
            new Group
            {
                Name = "Side",
                Roots = new[] { "Side_L", "Side_L.003", "Side_L.008", "Side_R", "Side_R.003", "Side_R.008" },
                Pull = 0.12f, Spring = 0.45f, Stiffness = 0.25f, Gravity = 0.2f, Immobile = 0.5f, Radius = 0.01f,
                Limit = VRCPhysBoneBase.LimitType.Angle, MaxAngleX = 60f, LimitRotation = new Vector3(25, 0, 0),
                Colliders = new[] { ">Head_C", ">N_C", ">C_C3" },
            },
            // An ahoge (gravity 0) stays alone: merged with the fringe it collapses.
        };

        static readonly Tweak[] Tweaks =
        {
            // The braid hung over the shoulder onto the chest. With gravity 1, no angle limit and a 2 cm
            // radius (the braid is ~2.5 cm thick) it sank into the chest when leaning back. At 90 degrees
            // it flipped back over the shoulder; 45 rests it on the shoulder.
            new Tweak { Root = "Braids", Radius = 0.03f, Gravity = 0.6f, MaxAngleX = 45f,
                        Colliders = new[] { ">Head_C", ">N_C", ">C_C", ">C_C2", ">C_C3", ">AL_C", ">AR_C" } },
        };

        // every other bone of each hair chain
        static readonly string[] MergedBones = new[] { "L", "R" }.SelectMany(side => new[]
        {
            $"P_{side}.001", $"P_{side}.004", $"P_{side}.007", $"P_{side}.009", $"P_{side}.011", $"P_{side}.013",
            $"Side_{side}.001", $"Side_{side}.004", $"Side_{side}.006", $"Side_{side}.009", $"Side_{side}.011",
        }).ToArray();
        // --------------------------------------------------------------------------------------------

        [MenuItem("Tools/Avatar Good/PhysBone Budget")]
        static void Run()
        {
            var root = AvatarRoot();
            if (root == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            Debug.Log(Setup(root));
        }

        // Read-only: PhysBones whose whole chain has no weight on any mesh (inactive meshes included).
        // Trace and Optimize removes most of these at build, but not one that has a parameter set, and
        // those still count. Candidates to delete; also check for PhysBones nothing ever enables.
        [MenuItem("Tools/Avatar Good/Report Dead PhysBones")]
        static void ReportDead()
        {
            var root = AvatarRoot();
            if (root == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            var weighted = WeightedBones(root);
            var log = new System.Text.StringBuilder("PhysBones with no weighted transform:\n");
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var chainRoot = pb.rootTransform ? pb.rootTransform : pb.transform;
                var chain = chainRoot.GetComponentsInChildren<Transform>(true);
                if (chain.Any(weighted.Contains)) continue;
                log.Append($"  {chainRoot.name} (component on {pb.name}, {chain.Length} transforms, {pb.colliders.Count} colliders"
                           + (string.IsNullOrEmpty(pb.parameter) ? "" : $", parameter '{pb.parameter}'") + ")\n");
            }
            Debug.Log(log.ToString());
        }

        static GameObject AvatarRoot()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected ? selected.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true) : null;
            return descriptor ? descriptor.gameObject : null;
        }

        static HashSet<Transform> WeightedBones(GameObject root)
        {
            var weighted = new HashSet<Transform>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var bones = smr.bones;
                foreach (var w in smr.sharedMesh.GetAllBoneWeights())
                    if (w.weight > 0 && w.boneIndex < bones.Length && bones[w.boneIndex] != null)
                        weighted.Add(bones[w.boneIndex]);
            }
            return weighted;
        }

        public static string Setup(GameObject root)
        {
            var log = new System.Text.StringBuilder();
            foreach (var old in root.GetComponentsInChildren<MergePhysBone>(true).Select(m => m.gameObject).ToList())
                if (old.name.StartsWith(Prefix)) Undo.DestroyObjectImmediate(old);

            foreach (var pb in root.GetComponentsInChildren<VRCPhysBone>(true).ToList())
            {
                var chainRoot = pb.rootTransform ? pb.rootTransform : pb.transform;
                if (!StaticRoots.Contains(chainRoot.name)) continue;
                log.Append($"static: removed PhysBone {chainRoot.name}\n");
                Undo.DestroyObjectImmediate(pb);
            }

            var physBones = root.GetComponentsInChildren<VRCPhysBone>(true);
            VRCPhysBone ByRoot(string name) =>
                physBones.Single(pb => (pb.rootTransform ? pb.rootTransform : pb.transform).name == name);
            var activeColliders = root.GetComponentsInChildren<VRCPhysBoneCollider>(true)
                .Where(c => c.gameObject.activeInHierarchy).ToList();
            VRCPhysBoneColliderBase FindCollider(string name) => activeColliders.Single(c => c.name == name);
            var weighted = WeightedBones(root);

            Transform holder = null;
            foreach (var group in Groups)
            {
                var sources = group.Roots.Select(ByRoot).ToList();
                Transform parent;
                if (group.MakeParentUnder != null)
                    parent = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == group.MakeParentUnder);
                else
                {
                    if (holder == null) holder = root.transform.Find(LooseHolder);
                    if (holder == null)
                    {
                        holder = new GameObject(LooseHolder).transform;
                        Undo.RegisterCreatedObjectUndo(holder.gameObject, "PhysBone merges");
                        holder.SetParent(root.transform, false);
                    }
                    parent = holder;
                }
                var go = new GameObject(Prefix + group.Name);
                Undo.RegisterCreatedObjectUndo(go, "PhysBone merge");
                go.transform.SetParent(parent, false);

                var merge = go.AddComponent<MergePhysBone>();
                merge.Initialize(1);
                merge.MakeParent = group.MakeParentUnder != null;
                merge.PhysBones.UnionWith(sources);

                void Float(MergePhysBone.OverrideAndValueAPI api, float? value)
                {
                    if (value == null) return;
                    api.Override = true;
                    api.FloatOverrideValue = value.Value;
                }
                Float(merge.PullConfig, group.Pull);
                Float(merge.SpringConfig, group.Spring);
                Float(merge.StiffnessConfig, group.Stiffness);
                Float(merge.GravityConfig, group.Gravity);
                Float(merge.ImmobileConfig, group.Immobile);
                Float(merge.RadiusConfig, group.Radius);
                Float(merge.MaxAngleXConfig, group.MaxAngleX);
                if (group.Limit != null)
                {
                    var api = merge.LimitTypeConfig;
                    api.Override = true;
                    api.LimitTypeOverrideValue = group.Limit.Value;
                }
                if (group.LimitRotation != null)
                {
                    // don't use the Fix mode: it inserts extra bones
                    var api = merge.LimitRotationConfig;
                    api.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    api.PositionOverrideValue = group.LimitRotation.Value;
                }
                if (group.Colliders != null)
                {
                    // there is no public setter for the collider list
                    var so = new SerializedObject(merge);
                    so.FindProperty("collidersConfig.override").enumValueIndex = 1;   // Override
                    var list = so.FindProperty("collidersConfig.value");
                    list.arraySize = group.Colliders.Length;
                    for (int i = 0; i < group.Colliders.Length; i++)
                        list.GetArrayElementAtIndex(i).objectReferenceValue = FindCollider(group.Colliders[i]);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                // (leaf, the endpoint its own PhysBone already had)
                var leaves = sources
                    .SelectMany(pb => (pb.rootTransform ? pb.rootTransform : pb.transform)
                        .GetComponentsInChildren<Transform>(true)
                        .Where(t => t.childCount == 0)
                        .Select(t => (leaf: t, endpoint: pb.endpointPosition)))
                    .ToList();
                if (leaves.All(l => !weighted.Contains(l.leaf)))
                {
                    // the parent becomes the new leaf: its endpoint reaches the old leaf plus its endpoint
                    var length = leaves.Average(l => (l.leaf.localPosition + l.leaf.localRotation * l.endpoint).magnitude);
                    var endpoint = new Vector3(0, length, 0);
                    foreach (var leaf in leaves.Select(l => l.leaf))
                    {
                        Undo.RecordObject(leaf.gameObject, "End bone");
                        leaf.gameObject.tag = "EditorOnly";
                        PrefabUtility.RecordPrefabInstancePropertyModifications(leaf.gameObject);
                    }
                    var api = merge.EndPointPositionConfig;
                    api.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    api.PositionOverrideValue = endpoint;
                    log.Append($"{go.name}: {leaves.Count} end bones -> endpoint {endpoint.y:F3}\n");
                }
                log.Append($"{go.name}: {sources.Count} PhysBones, makeParent={merge.MakeParent}\n");
            }

            foreach (var tweak in Tweaks)
            {
                var pb = ByRoot(tweak.Root);
                Undo.RecordObject(pb, "PhysBone tweak");
                if (tweak.Colliders != null) pb.colliders = tweak.Colliders.Select(FindCollider).ToList();
                if (tweak.Radius != null) pb.radius = tweak.Radius.Value;
                if (tweak.Gravity != null) pb.gravity = tweak.Gravity.Value;
                if (tweak.MaxAngleX != null) pb.maxAngleX = tweak.MaxAngleX.Value;
                PrefabUtility.RecordPrefabInstancePropertyModifications(pb);
                log.Append($"{tweak.Root}: tweaked, {pb.colliders.Count} colliders\n");
            }

            // Merge Bone is internal to AAO, so add it by type
            var mergeBoneType = System.Type.GetType("Anatawa12.AvatarOptimizer.MergeBone, com.anatawa12.avatar-optimizer.runtime", true);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            int mergedBones = 0;
            foreach (var name in MergedBones)
            {
                var bone = transforms.Single(t => t.name == name);
                if (bone.GetComponent(mergeBoneType) != null) continue;
                Undo.AddComponent(bone.gameObject, mergeBoneType);
                mergedBones++;
            }
            log.Append($"Merge Bone added on {mergedBones} bones\n");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            return log.ToString();
        }
    }
}
