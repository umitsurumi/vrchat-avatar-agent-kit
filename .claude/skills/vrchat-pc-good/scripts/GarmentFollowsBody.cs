// Garment follows the body: a skirt (or coat, apron, ...) gets its bone weights copied from the body
// surface under it, and loses the PhysBones and constraints that made it swing.
// Menu: Tools/Avatar Good/Garment Follows Body (select the avatar root, or anything under it).
//
// Why: a swinging skirt clips as soon as the legs move more than walking does (MMD dances: legs
// spread, kick back, twist). Skinned like the body under it, the garment bends exactly as the thighs
// and hips do, so it stays outside them in any pose. The cost: no flutter, and the part between the
// legs stretches when they spread. In exchange it frees the garment's whole PhysBone budget, usually
// the biggest item in a Good build.
//
// How: for each garment vertex (rest pose), the nearest body vertex plus every body vertex up to Slack
// farther, weighted by inverse squared distance, so the front centre between the legs takes both
// thighs evenly instead of jumping to one. Then a few rounds of smoothing over the garment mesh, top 4
// weights kept. Weights map from body bones to the garment's bones by name, or by the nearest ancestor
// whose name the garment has.
//
// Assumes:
//   - The garment's bone list has bones named like the body's (true for outfits merged with an
//     MA Merge Armature: Hips, Spine, UpperLeg_L ... before the merge). A body bone with no counterpart
//     falls back to its nearest named ancestor; with none at all, the tool stops and names the bone.
//   - Body and garment are in the same rest pose in the scene (the scene's current pose is used).
//   - It reads the garment's prefab-source mesh when there is one, so a rerun starts from the
//     original weights, not from its own previous output.
// Where it may not fit:
//   - Long garments (a coat to mid-calf, a long skirt): the hem between the legs takes both legs and
//     will stretch like a trouser leg when they spread. Check in play mode (vrchat-avatar-unity TEST.md, pose test).
//   - Open fronts: each front panel follows the nearest thigh, which is usually what you want.
//   - Garments far from the body (crinoline, a wide cape): the nearest body vertex isn't meaningful.
//     Use colliders and PhysBones instead, or rig it by hand.
// Example (Green build): a 6,477-vertex pleated skirt; weights landed on Spine, Chest, Hips, both
// UpperLegs and both Butt bones; 126 PhysBone transforms freed.

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarGood.Editor
{
    public static class GarmentFollowsBody
    {
        // ---- CONFIG: replace with your avatar's -------------------------------------------------
        const string GarmentRenderer = "Skirt";          // SkinnedMeshRenderer that should follow
        const string BodyRenderer = "Manuka_body";       // the body mesh under it
        const string OutputMesh = "Assets/_Generated/Skirt_FollowBody.asset";
        // PhysBones whose root name starts with one of these are deleted, and so are VRC constraints
        // on objects whose name does; objects named in RemoveObjects too (e.g. an old merge group).
        static readonly string[] RemovePrefixes = { "skirt_" };
        static readonly string[] RemoveObjects = { "MergePB_Skirt" };
        const float Slack = 0.02f;                       // metres beyond the nearest body vertex
        const int SmoothIterations = 10;
        // --------------------------------------------------------------------------------------------

        [MenuItem("Tools/Avatar Good/Garment Follows Body")]
        static void Run()
        {
            var root = AvatarRoot();
            if (root == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            Debug.Log(Apply(root));
        }

        static GameObject AvatarRoot()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected ? selected.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true) : null;
            return descriptor ? descriptor.gameObject : null;
        }

        public static string Apply(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var garment = renderers.Single(r => r.name == GarmentRenderer);
            var body = renderers.Single(r => r.name == BodyRenderer);
            var original = PrefabUtility.GetCorrespondingObjectFromSource(garment)?.sharedMesh ?? garment.sharedMesh;

            var garmentPos = WorldPositions(garment);   // rest pose; any copy of the mesh has the same vertices
            var bodyPos = WorldPositions(body);

            var bodyBones = body.bones;
            var bodyPerVertex = body.sharedMesh.GetBonesPerVertex();
            var bodyWeights = body.sharedMesh.GetAllBoneWeights();
            var bodyStart = new int[bodyPos.Length];
            for (int v = 1; v < bodyPos.Length; v++) bodyStart[v] = bodyStart[v - 1] + bodyPerVertex[v - 1];

            var bounds = new Bounds(garmentPos[0], Vector3.zero);
            foreach (var p in garmentPos) bounds.Encapsulate(p);
            bounds.Expand(0.2f);
            var candidates = Enumerable.Range(0, bodyPos.Length).Where(i => bounds.Contains(bodyPos[i])).ToArray();

            var garmentIndex = new Dictionary<string, int>();
            for (int i = 0; i < garment.bones.Length; i++)
                if (garment.bones[i] != null && !garmentIndex.ContainsKey(garment.bones[i].name)) garmentIndex[garment.bones[i].name] = i;
            var used = new List<int>();          // garment bone indices that receive weight
            var column = new Dictionary<int, int>();
            int Column(int garmentBone)
            {
                if (!column.TryGetValue(garmentBone, out var c)) { c = used.Count; used.Add(garmentBone); column[garmentBone] = c; }
                return c;
            }

            var transfer = new List<Dictionary<int, float>>(garmentPos.Length);
            foreach (var p in garmentPos)
            {
                float nearest = float.MaxValue;
                foreach (var i in candidates) nearest = Mathf.Min(nearest, (bodyPos[i] - p).sqrMagnitude);
                var limit = Mathf.Sqrt(nearest) + Slack;
                limit *= limit;
                var acc = new Dictionary<int, float>();
                foreach (var i in candidates)
                {
                    var d2 = (bodyPos[i] - p).sqrMagnitude;
                    if (d2 > limit) continue;
                    var share = 1f / (d2 + 1e-6f);
                    for (int j = 0; j < bodyPerVertex[i]; j++)
                    {
                        var w = bodyWeights[bodyStart[i] + j];
                        if (w.weight <= 0) continue;
                        var c = Column(MapBone(bodyBones[w.boneIndex], garmentIndex));
                        acc[c] = (acc.TryGetValue(c, out var cur) ? cur : 0f) + share * w.weight;
                    }
                }
                transfer.Add(acc);
            }

            // dense weights, normalised, then smoothed over the garment's welded neighbours
            var weights = new float[garmentPos.Length, used.Count];
            for (int v = 0; v < garmentPos.Length; v++)
            {
                var sum = transfer[v].Values.Sum();
                foreach (var kv in transfer[v]) weights[v, kv.Key] = kv.Value / sum;
            }
            var neighbours = Neighbours(original, garmentPos);
            for (int it = 0; it < SmoothIterations; it++)
            {
                var next = new float[garmentPos.Length, used.Count];
                for (int v = 0; v < garmentPos.Length; v++)
                {
                    var n = neighbours[v];
                    for (int c = 0; c < used.Count; c++)
                    {
                        float avg = 0;
                        foreach (var u in n) avg += weights[u, c];
                        next[v, c] = n.Count == 0 ? weights[v, c] : 0.5f * weights[v, c] + 0.5f * avg / n.Count;
                    }
                }
                weights = next;
            }

            var perVertex = new NativeArray<byte>(garmentPos.Length, Allocator.Temp);
            var list = new List<BoneWeight1>();
            for (int v = 0; v < garmentPos.Length; v++)
            {
                var top = Enumerable.Range(0, used.Count).Select(c => (c, w: weights[v, c]))
                    .Where(x => x.w > 1e-4f).OrderByDescending(x => x.w).Take(4).ToList();
                var sum = top.Sum(x => x.w);
                foreach (var (c, w) in top) list.Add(new BoneWeight1 { boneIndex = used[c], weight = w / sum });
                perVertex[v] = (byte)top.Count;
            }
            var mesh = Object.Instantiate(original);
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(OutputMesh);
            var nativeWeights = new NativeArray<BoneWeight1>(list.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(perVertex, nativeWeights);
            perVertex.Dispose();
            nativeWeights.Dispose();

            // Overwrites the previous output in place, so the GUID (and the scene's reference) stays.
            // Fine here because the vertex count never changes; for meshes whose vertex count can
            // change, copy through the Mesh API instead (GOTCHAS.md).
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputMesh));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMesh);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, OutputMesh);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(garment, "Garment follows body");
            garment.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(garment);

            // what the swinging garment needed
            bool Matches(string name) => RemovePrefixes.Any(name.StartsWith);
            int physBones = 0, constraints = 0, objects = 0;
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBone>(true).ToList())
                if (Matches((pb.rootTransform ? pb.rootTransform : pb.transform).name)) { Undo.DestroyObjectImmediate(pb); physBones++; }
            foreach (var c in root.GetComponentsInChildren<VRC.Dynamics.VRCConstraintBase>(true).ToList())
                if (Matches(c.name)) { Undo.DestroyObjectImmediate(c); constraints++; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => RemoveObjects.Contains(t.name)).ToList())
            { Undo.DestroyObjectImmediate(t.gameObject); objects++; }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            var bones = string.Join(", ", used.Select(i => garment.bones[i].name));
            return $"{GarmentRenderer} weights from {BodyRenderer} ({bones}) -> {OutputMesh}; removed {physBones} PhysBones, {constraints} constraints, {objects} objects\n";
        }

        // the garment bone with the body bone's name, or with its nearest ancestor's
        static int MapBone(Transform bodyBone, Dictionary<string, int> garmentIndex)
        {
            for (var t = bodyBone; t != null; t = t.parent)
                if (garmentIndex.TryGetValue(t.name, out var i)) return i;
            throw new System.InvalidOperationException($"the garment has no bone named like {bodyBone.name} or any of its parents; add one to its bone list");
        }

        static Vector3[] WorldPositions(SkinnedMeshRenderer smr)
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            var result = baked.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
            Object.DestroyImmediate(baked);
            return result;
        }

        // triangle neighbours, with copies of a vertex at the same position (UV seams) joined
        static List<List<int>> Neighbours(Mesh mesh, Vector3[] positions)
        {
            var weld = new Dictionary<Vector3Int, int>();
            var rep = new int[positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                var key = Vector3Int.RoundToInt(positions[v] * 1e5f);
                if (!weld.TryGetValue(key, out rep[v])) { rep[v] = v; weld[key] = v; }
            }
            var sets = Enumerable.Range(0, positions.Length).Select(_ => new HashSet<int>()).ToArray();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                    for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                        if (a != b) sets[rep[tris[t + a]]].Add(rep[tris[t + b]]);
            }
            return Enumerable.Range(0, positions.Length).Select(v => sets[rep[v]].ToList()).ToList();
        }
    }
}
