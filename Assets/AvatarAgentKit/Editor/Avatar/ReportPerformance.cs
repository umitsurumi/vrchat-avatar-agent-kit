// Performance of a baked clone as VRChat rates it on PC: the overall rating and the metrics that set it,
// each metric with its value, rating and the limit of the next better rating, and the biggest offenders
// (renderers by triangles and by material slots, PhysBones by transforms).
//
// From code (execute_code):  return AvatarAgentKit.Editor.ReportPerformance.Run("MyAvatar");
//                            return AvatarAgentKit.Editor.ReportPerformance.Run("MyAvatar", top: 10);
// The target is an avatar (its _bakecheck clone is measured) or the clone itself; it may be left out when
// there is only one clone. Menu: Tools/Avatar Agent Kit/Report Performance; the result goes to the Console.
//
// Only a baked clone is measured. The scene avatar and the SDK build panel show pre-NDMF numbers.
// The limits come from the SDK's own rating tables, so they follow SDK updates.
//
// Verified on: Unity 2022.3.22f1, VRChat SDK 3.10.5; a MANUKA avatar with MA, AAO, Meshia and FaceEmo.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace AvatarAgentKit.Editor
{
    public static class ReportPerformance
    {
        const string Tool = "ReportPerformance";

        class Row
        {
            public AvatarPerformanceCategory Category;
            public string Label;
            public bool Core; // always listed; the others only when they are worse than Excellent
            public Func<AvatarPerformanceStats, string> Value;
            public Func<AvatarPerformanceStatsLevel, string> Limit;
        }

        static string N(int? v) => v.HasValue ? v.Value.ToString("N0", CultureInfo.InvariantCulture) : "-";
        static string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
        static string B(bool? v) => v.HasValue ? (v.Value ? "yes" : "no") : "-";
        static string Size(Bounds b) => b.size.x.ToString("F2", CultureInfo.InvariantCulture) + " x "
            + b.size.y.ToString("F2", CultureInfo.InvariantCulture) + " x " + b.size.z.ToString("F2", CultureInfo.InvariantCulture) + " m";

        static Row R(AvatarPerformanceCategory c, string label, bool core,
            Func<AvatarPerformanceStats, string> value, Func<AvatarPerformanceStatsLevel, string> limit) =>
            new Row { Category = c, Label = label, Core = core, Value = value, Limit = limit };

        static readonly Row[] Rows =
        {
            R(AvatarPerformanceCategory.PolyCount, "triangles", true, s => N(s.polyCount), l => N(l.polyCount)),
            R(AvatarPerformanceCategory.TextureMegabytes, "texture memory", true,
                s => s.textureMegabytes.HasValue ? s.textureMegabytes.Value.ToString("F1", CultureInfo.InvariantCulture) + " MB" : "-",
                l => l.textureMegabytes.ToString("F0", CultureInfo.InvariantCulture) + " MB"),
            R(AvatarPerformanceCategory.SkinnedMeshCount, "skinned meshes", true, s => N(s.skinnedMeshCount), l => N(l.skinnedMeshCount)),
            R(AvatarPerformanceCategory.MeshCount, "meshes", true, s => N(s.meshCount), l => N(l.meshCount)),
            R(AvatarPerformanceCategory.MaterialCount, "material slots", true, s => N(s.materialCount), l => N(l.materialCount)),
            R(AvatarPerformanceCategory.BoneCount, "bones", true, s => N(s.boneCount), l => N(l.boneCount)),
            R(AvatarPerformanceCategory.PhysBoneComponentCount, "PhysBone components", true,
                s => s.physBone.HasValue ? N(s.physBone.Value.componentCount) : "0", l => N(l.physBone.componentCount)),
            R(AvatarPerformanceCategory.PhysBoneTransformCount, "PhysBone transforms", true,
                s => s.physBone.HasValue ? N(s.physBone.Value.transformCount) : "0", l => N(l.physBone.transformCount)),
            R(AvatarPerformanceCategory.PhysBoneColliderCount, "PhysBone colliders", true,
                s => s.physBone.HasValue ? N(s.physBone.Value.colliderCount) : "0", l => N(l.physBone.colliderCount)),
            R(AvatarPerformanceCategory.PhysBoneCollisionCheckCount, "PhysBone collision checks", true,
                s => s.physBone.HasValue ? N(s.physBone.Value.collisionCheckCount) : "0", l => N(l.physBone.collisionCheckCount)),
            R(AvatarPerformanceCategory.ContactCount, "contacts", true, s => N(s.contactCount), l => N(l.contactCount)),
            R(AvatarPerformanceCategory.ConstraintsCount, "constraints", true, s => N(s.constraintsCount), l => N(l.constraintsCount)),
            R(AvatarPerformanceCategory.ConstraintDepth, "constraint depth", false, s => N(s.constraintDepth), l => N(l.constraintDepth)),
            R(AvatarPerformanceCategory.AnimatorCount, "animators", false, s => N(s.animatorCount), l => N(l.animatorCount)),
            R(AvatarPerformanceCategory.LightCount, "lights", false, s => N(s.lightCount), l => N(l.lightCount)),
            R(AvatarPerformanceCategory.ParticleSystemCount, "particle systems", false, s => N(s.particleSystemCount), l => N(l.particleSystemCount)),
            R(AvatarPerformanceCategory.ParticleTotalCount, "particles", false, s => N(s.particleTotalCount), l => N(l.particleTotalCount)),
            R(AvatarPerformanceCategory.ParticleMaxMeshPolyCount, "particle mesh triangles", false, s => N(s.particleMaxMeshPolyCount), l => N(l.particleMaxMeshPolyCount)),
            R(AvatarPerformanceCategory.ParticleTrailsEnabled, "particle trails", false, s => B(s.particleTrailsEnabled), l => l.particleTrailsEnabled ? "yes" : "no"),
            R(AvatarPerformanceCategory.ParticleCollisionEnabled, "particle collision", false, s => B(s.particleCollisionEnabled), l => l.particleCollisionEnabled ? "yes" : "no"),
            R(AvatarPerformanceCategory.TrailRendererCount, "trail renderers", false, s => N(s.trailRendererCount), l => N(l.trailRendererCount)),
            R(AvatarPerformanceCategory.LineRendererCount, "line renderers", false, s => N(s.lineRendererCount), l => N(l.lineRendererCount)),
            R(AvatarPerformanceCategory.ClothCount, "cloths", false, s => N(s.clothCount), l => N(l.clothCount)),
            R(AvatarPerformanceCategory.ClothMaxVertices, "cloth vertices", false, s => N(s.clothMaxVertices), l => N(l.clothMaxVertices)),
            R(AvatarPerformanceCategory.PhysicsColliderCount, "physics colliders", false, s => N(s.physicsColliderCount), l => N(l.physicsColliderCount)),
            R(AvatarPerformanceCategory.PhysicsRigidbodyCount, "rigidbodies", false, s => N(s.physicsRigidbodyCount), l => N(l.physicsRigidbodyCount)),
            R(AvatarPerformanceCategory.AudioSourceCount, "audio sources", false, s => N(s.audioSourceCount), l => N(l.audioSourceCount)),
            R(AvatarPerformanceCategory.RaycastCount, "raycasts", false, s => N(s.raycastCount), l => N(l.raycastCount)),
            R(AvatarPerformanceCategory.AABB, "bounds", false, s => s.aabb.HasValue ? Size(s.aabb.Value) : "-", l => Size(l.aabb)),
        };

        public static string Run(string target = null, int top = 5)
        {
            var err = Targets.FindBaked(target, out var clone);
            if (err != null) return Tool + ": refused: " + err;
            if (top < 1) top = 1;

            var stats = new AvatarPerformanceStats(false);
            AvatarPerformance.CalculatePerformanceStats(clone.name, clone, stats, false);
            stats.CalculateAllPerformanceRatings(false);
            var overall = stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall);

            var sb = new StringBuilder();
            sb.Append(Tool).Append(": ").Append(Targets.Quote(clone.name)).Append(", PC: ").AppendLine(RatingName(overall));
            if (overall > PerformanceRating.Excellent)
            {
                var setBy = Rows.Where(r => stats.GetPerformanceRatingForCategory(r.Category) == overall).Select(r => r.Label).ToList();
                if (setBy.Count > 0) sb.Append("set by: ").AppendLine(string.Join(", ", setBy));
            }

            sb.AppendLine();
            sb.Append(Pad("metric", 27)).Append(Pad("value", 20)).Append(Pad("rating", 11)).AppendLine("next better");
            foreach (var row in Rows)
            {
                var rating = stats.GetPerformanceRatingForCategory(row.Category);
                if (!row.Core && rating <= PerformanceRating.Excellent) continue;
                var next = "";
                if (rating > PerformanceRating.Excellent)
                {
                    var better = rating - 1;
                    next = RatingName(better) + " <= " + row.Limit(AvatarPerformanceStats.GetStatLevelForRating(better, false));
                }
                sb.Append(Pad(row.Label, 27)).Append(Pad(row.Value(stats), 20)).Append(Pad(RatingName(rating), 11)).AppendLine(next);
            }

            AppendRenderers(sb, clone, stats, top);
            AppendPhysBones(sb, clone, top);
            return sb.ToString().TrimEnd();
        }

        static void AppendRenderers(StringBuilder sb, GameObject clone, AvatarPerformanceStats stats, int top)
        {
            var list = new List<(string path, int tris, int slots, bool off)>();
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;
                int tris = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    if (mesh.GetTopology(i) == MeshTopology.Triangles) tris += (int)(mesh.GetIndexCount(i) / 3);
                var off = !r.enabled || !r.gameObject.activeInHierarchy;
                list.Add((AnimationUtility.CalculateTransformPath(r.transform, clone.transform), tris, r.sharedMaterials.Length, off));
            }
            if (list.Count == 0) return;

            sb.AppendLine();
            sb.Append("renderers by triangles (").Append(list.Count).Append(" renderers, ")
                .Append(N(list.Sum(x => x.tris))).AppendLine(" triangles; (off) = inactive or disabled at upload, still counted):");
            foreach (var x in list.OrderByDescending(x => x.tris).Take(top))
                sb.Append("  ").Append(Pad(N(x.tris), 9)).Append(x.slots).Append(x.slots == 1 ? " slot  " : " slots ")
                    .Append(x.path).AppendLine(x.off ? " (off)" : "");

            if (stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.MaterialCount) > PerformanceRating.Excellent)
            {
                sb.AppendLine("renderers by material slots:");
                foreach (var x in list.OrderByDescending(x => x.slots).Take(top))
                    sb.Append("  ").Append(Pad(x.slots.ToString(CultureInfo.InvariantCulture), 4)).Append(x.path).AppendLine(x.off ? " (off)" : "");
            }
        }

        static void AppendPhysBones(StringBuilder sb, GameObject clone, int top)
        {
            var list = new List<(string path, int transforms, int colliders, bool off)>();
            foreach (var pb in clone.GetComponentsInChildren<VRCPhysBone>(true))
            {
                pb.InitTransforms(true);
                var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                var colliders = pb.colliders == null ? 0 : pb.colliders.Count(c => c != null);
                var off = !pb.enabled || !pb.gameObject.activeInHierarchy;
                list.Add((AnimationUtility.CalculateTransformPath(root, clone.transform), pb.bones.Count, colliders, off));
            }
            if (list.Count == 0) return;

            sb.AppendLine();
            sb.Append("PhysBones by transforms (").Append(list.Count).Append(" components, ")
                .Append(N(list.Sum(x => x.transforms))).AppendLine(" transforms; path = root bone; (off) still counted):");
            foreach (var x in list.OrderByDescending(x => x.transforms).Take(top))
                sb.Append("  ").Append(Pad(N(x.transforms), 5)).Append(Pad(x.colliders + (x.colliders == 1 ? " collider" : " colliders"), 13))
                    .Append(x.path).AppendLine(x.off ? " (off)" : "");
        }

        static string RatingName(PerformanceRating r) => r == PerformanceRating.VeryPoor ? "Very Poor" : r.ToString();

        static string Pad(string s, int width) => s.Length >= width ? s + " " : s.PadRight(width);

        [MenuItem("Tools/Avatar Agent Kit/Report Performance", false, 110)]
        static void ReportMenu() => Debug.Log(Run(Targets.SelectedRootName()));
    }
}
