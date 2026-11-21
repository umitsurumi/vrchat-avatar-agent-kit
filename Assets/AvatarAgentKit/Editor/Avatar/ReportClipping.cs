// Measures clipping in play mode: which skin (or underwear) vertices have come out through the garment that covers
// them, how far, and where. A measurement settles what a render only suggests; render the regions it names to see them.
//
// From code (execute_code, in play mode; one call per pose, PhysBones need time to settle in between):
//   return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "stand");                    // then wait 1 to 3 s
//   return AvatarAgentKit.Editor.ReportClipping.Run("MyAvatar");                  // standing: saves the reference
//   return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "squat");                    // then wait 1 to 3 s
//   return AvatarAgentKit.Editor.ReportClipping.Run("MyAvatar");                  // measures the squat
//   return AvatarAgentKit.Editor.ReportClipping.Run("MyAvatar", inner: "Underwear");   // underwear through outer clothes
// Menu: Tools/Avatar Agent Kit/Report Clipping (play mode, avatar selected); the result goes to the Console.
//
// How: in play mode the avatar is the processed one (NDMF Apply on Play), and AAO has usually merged body and clothes
// into one mesh, so parts are told apart by material. `inner` names the materials measured (default: those named like
// "body" or "skin"); every other submesh of every visible renderer is a cover, grouped by material name.
//   Standing (Pose "stand": rest pose, arms down, as in every other pose) it saves the reference: for each inner
//   vertex, the nearest cover along its normal within 15 cm, or the cover it is already through (none outward, one
//   within 2 cm inward: design seams such as face over neck, or clipping that is there without moving; render to tell).
//   Posed: a reference vertex still inside its cover (outward ray hits it) is fine; one with the cover within 6 cm
//   inward is through it by that distance, and is reported when that is at least minMm deeper than standing; one with
//   neither is exposed (the cover moved away: not clipping).
// Vertices at the same spot (UV seams) count once. Regions are the humanoid bone each vertex follows most (its
// heaviest bone, or that bone's nearest humanoid parent). Every vertex through goes to a CSV under
// Temp/AvatarAgentKit/clipping/.
//
// Limits: it measures inner layers coming out through covers, not hair or cloth sinking into the body; with layered
// clothes the reference is the nearest layer; static poses miss fast motion (dance in game).
//
// Verified on: Unity 2022.3.22f1, VRChat SDK 3.10.5, MA 1.18, AAO; a MANUKA avatar in play mode.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarAgentKit.Editor
{
    [InitializeOnLoad]
    public static class ReportClipping
    {
        const string Tool = "ReportClipping";
        const int Layer = 31;               // the temporary colliders' layer; hits on anything else are ignored
        const float CoverReach = 0.15f;     // how far out from the skin a cover still counts as covering it
        const float RestDepth = 0.02f;      // standing: how deep a vertex may already be through to be found
        const float PosedDepth = 0.06f;     // posed: the deepest poke-through measured
        const float Offset = 0.0005f;       // rays start this far out from the vertex
        const int MaxLines = 8;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static ReportClipping() => EditorApplication.playModeStateChanged += _ => References.Clear();

        class Reference
        {
            public string Inner;
            public readonly Dictionary<long, (string cover, float depth)> Cover = new Dictionary<long, (string, float)>(); // vertex key -> cover, depth through it standing
        }

        // Per avatar (instance ID), for the current play session.
        static readonly Dictionary<int, Reference> References = new Dictionary<int, Reference>();

        struct Sample
        {
            public Renderer Renderer;
            public int Vertex;
            public Vector3 Position, Normal;
            public long Key => ((long)Renderer.GetInstanceID() << 32) | (uint)Vertex;
        }

        struct Finding
        {
            public Sample Sample;
            public string Cover;
            public float Depth;     // metres; 0 for exposed
        }

        public static string Run(string avatar = null, string inner = null, float minMm = 2f)
        {
            if (!EditorApplication.isPlaying)
                return Tool + ": refused: not in play mode. PhysBones only move in play mode: enter it, then measure at rest, pose with Pose.Run, wait, and measure again.";
            if (Targets.IsBakedName(avatar)) avatar = Targets.SourceName(avatar);
            var err = Targets.FindSource(avatar, out var source);
            if (err != null) return Tool + ": refused: " + err;
            if (!source.activeInHierarchy) return Tool + ": refused: " + Targets.Quote(source.name) + " is inactive; activate it, or leave and re-enter play mode.";
            var anim = source.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return Tool + ": refused: " + Targets.Quote(source.name) + " has no humanoid Animator.";
            if (minMm < 0f || minMm > 50f) return Tool + ": refused: minMm is the smallest depth reported, 0 to 50.";

            var renderers = source.GetComponentsInChildren<Renderer>(false)
                .Where(r => r.enabled && (r is SkinnedMeshRenderer || r is MeshRenderer)).ToList();
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().OrderBy(n => n).ToList();
            var guessed = string.IsNullOrEmpty(inner);
            var innerSet = guessed
                ? new HashSet<string>(materials.Where(n => n.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0))
                : new HashSet<string>(inner.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            var unknown = innerSet.Where(n => !materials.Contains(n)).ToList();
            if (innerSet.Count == 0 || unknown.Count > 0)
                return Tool + ": refused: " + (guessed ? "no visible material is named like \"body\" or \"skin\"" : "no visible material " + string.Join(", ", unknown.Select(Targets.Quote)))
                    + ". Pass inner: the materials to measure, e.g. inner: \"Body_Skin\". Visible materials: " + string.Join(", ", materials.Select(Targets.Quote));
            var innerKey = string.Join(",", innerSet.OrderBy(s => s));

            var pose = Pose.CurrentPose(source);
            var standing = pose == "stand";
            References.TryGetValue(source.GetInstanceID(), out var reference);
            var howToReference = "Run " + Targets.Ns + "Pose.Run(" + Targets.Quote(source.name) + ", \"stand\"), wait 1 to 3 s, run " + Targets.Ns + "ReportClipping.Run("
                + Targets.Quote(source.name) + (guessed ? "" : ", inner: " + Targets.Quote(inner)) + ") to save the reference, then pose and measure.";
            if (!standing && (pose == null || pose == "rest"))
                return Tool + ": refused: " + Targets.Quote(source.name) + " is " + (pose == null ? "not posed" : "in its rest pose") + ": the arms are where they were modelled, "
                    + "while every test pose puts them down. " + howToReference;
            if (!standing && (reference == null || reference.Inner != innerKey))
                return Tool + ": refused: no reference for " + Describe(innerSet) + " in this play session, and " + Targets.Quote(source.name) + " is posed (" + pose + "). " + howToReference;

            var temps = new List<Object>();
            var backfaces = Physics.queriesHitBackfaces;
            try
            {
                var notes = new List<string>();
                var samples = Build(renderers, innerSet, temps, notes, out var covers);
                if (samples.Count == 0) return Tool + ": refused: " + Describe(innerSet) + " have no vertices on visible renderers.";
                if (covers.Count == 0) return Tool + ": refused: nothing covers " + Describe(innerSet) + ": every visible material is inner.";
                Physics.SyncTransforms();
                Physics.queriesHitBackfaces = true;   // single-sided cloth is hit from inside too
                var regions = new Regions(anim);
                var head = Tool + ": " + Targets.Quote(source.name) + " (" + (pose ?? "rest") + "): ";
                var innerLine = "inner: " + Describe(innerSet) + (guessed ? " (named like body or skin; pass inner to change)" : "");
                return standing
                    ? SaveReference(source, innerKey, innerLine, samples, covers, regions, notes, head)
                    : Compare(source, reference, innerLine, samples, covers, regions, notes, head, pose, minMm / 1000f);
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
                foreach (var o in temps) if (o != null) Object.DestroyImmediate(o);
            }
        }

        // ------------------------------------------------------------------ measuring

        static string SaveReference(GameObject source, string innerKey, string innerLine, List<Sample> samples, Dictionary<int, string> covers,
            Regions regions, List<string> notes, string head)
        {
            var reference = new Reference { Inner = innerKey };
            var through = new List<Finding>();
            var perCover = new Dictionary<string, int>();
            foreach (var s in samples)
            {
                var o = s.Position + s.Normal * Offset;
                var depth = 0f;
                var d = Cast(o, s.Normal, CoverReach, covers, null, out var cover);
                if (d < 0)
                {
                    d = Cast(o, -s.Normal, RestDepth + Offset, covers, null, out cover);
                    if (d < 0) continue;
                    depth = Mathf.Max(0f, d - Offset);
                    through.Add(new Finding { Sample = s, Cover = cover, Depth = depth });
                }
                reference.Cover[s.Key] = (cover, depth);
                perCover[cover] = perCover.TryGetValue(cover, out var n) ? n + 1 : 1;
            }
            References[source.GetInstanceID()] = reference;

            var sb = new StringBuilder(head);
            sb.Append("reference saved: ").Append(N(reference.Cover.Count)).Append(" of ").Append(N(samples.Count)).Append(" inner vertices are under a cover; ")
                .Append(through.Count == 0 ? "none" : N(through.Count)).AppendLine(" already through standing.");
            sb.AppendLine(innerLine);
            sb.AppendLine("covers (inner vertices each covers): " + string.Join(", ", perCover.OrderByDescending(kv => kv.Value).Take(MaxLines)
                .Select(kv => Targets.Quote(kv.Key) + " " + N(kv.Value))) + (perCover.Count > MaxLines ? " and " + (perCover.Count - MaxLines) + " more" : ""));
            if (through.Count > 0) AppendRegions(sb, "already through standing (design seams such as face over neck, or clipping that is there without moving; render to tell; poses report only what goes deeper)", through, regions);
            foreach (var n in notes) sb.AppendLine("note: " + n);
            if (through.Count > 0) sb.AppendLine("details: " + WriteCsv(source.name, "stand", through, regions));
            sb.Append("Next: ").Append(Targets.Ns).Append("Pose.Run(").Append(Targets.Quote(source.name)).Append(", \"squat\"), wait 1 to 3 s, then ")
                .Append(Targets.Ns).Append("ReportClipping.Run(").Append(Targets.Quote(source.name)).Append(") again; one pose per call.");
            return sb.ToString();
        }

        static string Compare(GameObject source, Reference reference, string innerLine, List<Sample> samples, Dictionary<int, string> covers,
            Regions regions, List<string> notes, string head, string pose, float min)
        {
            var through = new List<Finding>();
            var exposed = new List<Finding>();
            int measured = 0;
            foreach (var s in samples)
            {
                if (!reference.Cover.TryGetValue(s.Key, out var r)) continue;
                measured++;
                var o = s.Position + s.Normal * Offset;
                if (Cast(o, s.Normal, CoverReach, covers, r.cover, out _) >= 0) continue;
                var d = Cast(o, -s.Normal, PosedDepth + Offset, covers, r.cover, out _);
                if (d < 0) exposed.Add(new Finding { Sample = s, Cover = r.cover });
                else if (d - Offset >= r.depth + min) through.Add(new Finding { Sample = s, Cover = r.cover, Depth = d - Offset });
            }

            var sb = new StringBuilder(head);
            var minText = Mm(min) + " mm or more deeper than standing";
            if (through.Count == 0)
                sb.Append("nothing through its cover (").Append(minText).Append("); ").Append(N(measured)).AppendLine(" covered vertices measured.");
            else
            {
                var regionCount = through.Select(f => regions.Of(f.Sample)).Distinct().Count();
                var coverCount = through.Select(f => f.Cover).Distinct().Count();
                sb.Append(N(through.Count)).Append(" vertices through ").Append(coverCount == 1 ? "1 cover" : coverCount + " covers")
                    .Append(" in ").Append(regionCount == 1 ? "1 region" : regionCount + " regions").Append(", deepest ").Append(Mm(through.Max(f => f.Depth)))
                    .Append(" mm (").Append(minText).AppendLine(").");
                AppendRegions(sb, null, through, regions);
            }
            sb.AppendLine(innerLine);
            if (exposed.Count > 0)
                sb.AppendLine("exposed, not clipping: " + N(exposed.Count) + " vertices no longer under their cover ("
                    + string.Join(", ", exposed.GroupBy(f => regions.Of(f.Sample)).OrderByDescending(g => g.Count()).Take(4).Select(g => g.Key + " " + N(g.Count())))
                    + "): the cover moved away. Look only if it shows more than it should.");
            foreach (var n in notes) sb.AppendLine("note: " + n);
            if (through.Count > 0)
            {
                sb.AppendLine("details: " + WriteCsv(source.name, pose, through, regions));
                var worst = through.GroupBy(f => regions.Of(f.Sample)).OrderByDescending(g => g.Max(f => f.Depth)).First().Key;
                sb.Append("Next: see it with ").Append(Targets.Ns).Append("RenderAvatar.Run(").Append(Targets.Quote(source.name))
                    .Append(", \"front,left,back\", focus: ").Append(Targets.Quote(worst)).Append(", size: 0.5f, label: ").Append(Targets.Quote(pose)).Append(").");
            }
            else
                sb.Append("Next: the next pose, or ").Append(Targets.Ns).Append("Pose.Reset(").Append(Targets.Quote(source.name)).Append(") when done.");
            return sb.ToString().TrimEnd();
        }

        // Distance along the ray to the nearest hit on a cover (`want`, or any when null), or -1.
        // RaycastAll gives one hit per collider, the nearest on it; there is one collider per cover.
        static float Cast(Vector3 origin, Vector3 dir, float reach, Dictionary<int, string> covers, string want, out string cover)
        {
            cover = null;
            float best = -1f;
            foreach (var h in Physics.RaycastAll(origin, dir, reach, 1 << Layer, QueryTriggerInteraction.Ignore))
            {
                if (!covers.TryGetValue(h.collider.GetInstanceID(), out var c)) continue;
                if (want != null && c != want) continue;
                if (best < 0f || h.distance < best) { best = h.distance; cover = c; }
            }
            return best;
        }

        // Inner vertices in world space, and one temporary collider per cover material (collider ID -> material name).
        static List<Sample> Build(List<Renderer> renderers, HashSet<string> innerSet, List<Object> temps, List<string> notes, out Dictionary<int, string> covers)
        {
            var samples = new List<Sample>();
            var coverVerts = new Dictionary<string, List<Vector3>>();
            var coverTris = new Dictionary<string, List<int>>();
            foreach (var r in renderers)
            {
                Mesh mesh;
                Matrix4x4 toWorld;
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    mesh = new Mesh();
                    temps.Add(mesh);
                    smr.BakeMesh(mesh, true);   // current pose and shape keys, scale applied
                    toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                }
                else
                {
                    mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    toWorld = r.transform.localToWorldMatrix;
                }
                Vector3[] verts, normals;
                try { verts = mesh.vertices; normals = mesh.normals; }
                catch (Exception) { notes.Add(Targets.Quote(r.name) + " skipped: its mesh can't be read (Read/Write off)."); continue; }
                var world = verts.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
                var mats = r.sharedMaterials;
                var innerVerts = new HashSet<int>();
                var baseIndex = new Dictionary<string, int>();
                for (int sub = 0; sub < mesh.subMeshCount && mats.Length > 0; sub++)
                {
                    var m = mats[Math.Min(sub, mats.Length - 1)];
                    if (m == null || mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var tris = mesh.GetTriangles(sub);
                    if (innerSet.Contains(m.name)) { innerVerts.UnionWith(tris); continue; }
                    if (!coverVerts.ContainsKey(m.name)) { coverVerts[m.name] = new List<Vector3>(); coverTris[m.name] = new List<int>(); }
                    if (!baseIndex.TryGetValue(m.name, out var b))
                    {
                        baseIndex[m.name] = b = coverVerts[m.name].Count;
                        coverVerts[m.name].AddRange(world);
                    }
                    coverTris[m.name].AddRange(tris.Select(t => t + b));
                }
                if (normals == null || normals.Length != verts.Length)
                {
                    if (innerVerts.Count > 0) notes.Add(Targets.Quote(r.name) + ": inner vertices skipped, the mesh has no normals.");
                    continue;
                }
                var seen = new HashSet<Vector3Int>();   // UV seams split a vertex into copies at one spot: count it once
                foreach (var v in innerVerts.OrderBy(i => i))
                {
                    if (!seen.Add(Vector3Int.RoundToInt(world[v] * 10000f))) continue;
                    var n = toWorld.MultiplyVector(normals[v]);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    samples.Add(new Sample { Renderer = r, Vertex = v, Position = world[v], Normal = n.normalized });
                }
            }

            covers = new Dictionary<int, string>();
            foreach (var kv in coverVerts)
            {
                if (coverTris[kv.Key].Count == 0) continue;
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                temps.Add(mesh);
                mesh.SetVertices(kv.Value);
                mesh.SetTriangles(coverTris[kv.Key], 0);
                var go = new GameObject("__clipcheck_" + kv.Key) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                temps.Add(go);
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                covers[collider.GetInstanceID()] = kv.Key;
            }
            return samples;
        }

        // ------------------------------------------------------------------ regions and output

        // The humanoid bone a vertex follows most.
        class Regions
        {
            readonly Animator anim;
            readonly Dictionary<Transform, string> humanoid = new Dictionary<Transform, string>();
            readonly Dictionary<Renderer, string[]> perVertex = new Dictionary<Renderer, string[]>();
            readonly Dictionary<long, string> cache = new Dictionary<long, string>();

            public Regions(Animator anim)
            {
                this.anim = anim;
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    var t = anim.GetBoneTransform(b);
                    if (t != null && !humanoid.ContainsKey(t)) humanoid[t] = Coarse(b).ToString();
                }
            }

            // Fingers count as their hand, toes as their foot, eyes and jaw as the head: one line per body part.
            static HumanBodyBones Coarse(HumanBodyBones b)
            {
                if (b >= HumanBodyBones.LeftThumbProximal && b <= HumanBodyBones.LeftLittleDistal) return HumanBodyBones.LeftHand;
                if (b >= HumanBodyBones.RightThumbProximal && b <= HumanBodyBones.RightLittleDistal) return HumanBodyBones.RightHand;
                if (b == HumanBodyBones.LeftToes) return HumanBodyBones.LeftFoot;
                if (b == HumanBodyBones.RightToes) return HumanBodyBones.RightFoot;
                if (b == HumanBodyBones.LeftEye || b == HumanBodyBones.RightEye || b == HumanBodyBones.Jaw) return HumanBodyBones.Head;
                return b;
            }

            public string Of(Sample s)
            {
                if (cache.TryGetValue(s.Key, out var name)) return name;
                if (!perVertex.TryGetValue(s.Renderer, out var names)) perVertex[s.Renderer] = names = Build(s.Renderer);
                name = names != null && s.Vertex < names.Length && names[s.Vertex] != null ? names[s.Vertex] : Up(s.Renderer.transform);
                cache[s.Key] = name;
                return name;
            }

            string Up(Transform t)
            {
                for (; t != null && t != anim.transform; t = t.parent)
                    if (humanoid.TryGetValue(t, out var name)) return name;
                return "Hips";
            }

            string[] Build(Renderer r)
            {
                var smr = r as SkinnedMeshRenderer;
                if (smr == null || smr.sharedMesh == null) return null;
                try
                {
                    var mesh = smr.sharedMesh;
                    var counts = mesh.GetBonesPerVertex();
                    var weights = mesh.GetAllBoneWeights();
                    var bones = smr.bones;
                    var boneNames = bones.Select(b => b != null ? Up(b) : null).ToArray();
                    var names = new string[mesh.vertexCount];
                    for (int v = 0, k = 0; v < counts.Length && v < names.Length; v++)
                    {
                        float best = -1f;
                        for (int j = 0; j < counts[v]; j++, k++)
                            if (weights[k].weight > best && weights[k].boneIndex < boneNames.Length) { best = weights[k].weight; names[v] = boneNames[weights[k].boneIndex]; }
                    }
                    return names;
                }
                catch (Exception) { return null; }   // unreadable mesh: the renderer's own bone is used
            }
        }

        static void AppendRegions(StringBuilder sb, string title, List<Finding> found, Regions regions)
        {
            if (title != null) sb.AppendLine(title + ":");
            var groups = found.GroupBy(f => (region: regions.Of(f.Sample), cover: f.Cover)).OrderByDescending(g => g.Max(f => f.Depth)).ThenByDescending(g => g.Count()).ToList();
            foreach (var g in groups.Take(MaxLines))
            {
                var depths = g.Select(f => f.Depth).OrderBy(d => d).ToList();
                sb.Append("  ").Append(g.Key.region.PadRight(16)).Append(" through ").Append(Targets.Quote(g.Key.cover)).Append(": ").Append(N(depths.Count))
                    .Append(depths.Count == 1 ? " vertex" : " vertices").Append(", deepest ").Append(Mm(depths[depths.Count - 1])).Append(" mm, median ")
                    .Append(Mm(depths[depths.Count / 2])).AppendLine(" mm");
            }
            if (groups.Count > MaxLines) sb.AppendLine("  and " + (groups.Count - MaxLines) + " more (in the details)");
        }

        static string WriteCsv(string avatar, string label, List<Finding> found, Regions regions)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "AvatarAgentKit", "clipping");
            Directory.CreateDirectory(dir);
            var safe = new string((label ?? "rest").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            var path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + "-" + safe + ".csv");
            var sb = new StringBuilder("avatar,pose,region,cover,renderer,vertex,depth_mm,x,y,z\n");
            foreach (var f in found.OrderByDescending(f => f.Depth))
            {
                var p = f.Sample.Position;
                sb.Append(Csv(avatar)).Append(',').Append(Csv(label)).Append(',').Append(regions.Of(f.Sample)).Append(',').Append(Csv(f.Cover)).Append(',')
                    .Append(Csv(f.Sample.Renderer.name)).Append(',').Append(f.Sample.Vertex).Append(',').Append(Mm(f.Depth)).Append(',')
                    .Append(p.x.ToString("0.####", Inv)).Append(',').Append(p.y.ToString("0.####", Inv)).Append(',').Append(p.z.ToString("0.####", Inv)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path.Replace('\\', '/');
        }

        static string Csv(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        static string Describe(HashSet<string> inner) => (inner.Count == 1 ? "material " : "materials ") + string.Join(", ", inner.OrderBy(s => s).Select(Targets.Quote));
        static string N(int n) => n.ToString("N0", Inv);
        static string Mm(float metres) => (metres * 1000f).ToString("0.#", Inv);

        [MenuItem("Tools/Avatar Agent Kit/Report Clipping", false, 112)]
        static void ReportMenuItem() => Debug.Log(Run(Targets.SelectedRootName()));
    }
}
