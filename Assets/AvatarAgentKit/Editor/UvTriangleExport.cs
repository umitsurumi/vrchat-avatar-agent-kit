// Exports UV triangles as JSON for the texture kit's masks (Tools/avatar-kit/texture-kit.py, mask source "uv").
//
// Menu: Tools/Avatar Agent Kit/Export UV Triangles (select a renderer): one group per submesh ("submesh0", ...),
// for masks like "everything the face material covers".
//
// From code, e.g. an agent through MCP for Unity, groups by shape key:
//
//   AvatarAgentKit.Editor.UvTriangleExport.ExportByShapeKeys(renderer, 0, "TextureSources/face/face_uv.json",
//       new UvTriangleExport.Group("lash", include: "^eyelash_", exclude: @"\(white\)"),
//       new UvTriangleExport.Group("brow", include: "^brow_", exclude: "^brow_shadow_"));
//
// A group holds the triangles of the given submesh whose vertices a matching shape key moves. With
// connectedPieces (the default) it takes the whole mesh piece (index-connected triangles) around them,
// which is what is painted as one part on the texture: a lash, a brow, an eyelid.
//
// JSON: {"source": "...", "uv_origin": "bottom-left", "uv_channel": 0,
//        "groups": {"lash": [[u0, v0, u1, v1, u2, v2], ...], ...}}
// It reads the renderer's mesh as it is in the scene (before NDMF/AAO build), which has the texture's UV layout.
// Verified on: compiled against Unity 2022.3.22f1; not yet run in Unity.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarAgentKit.Editor
{
    public static class UvTriangleExport
    {
        public sealed class Group
        {
            public readonly string Name;
            public readonly Regex Include;
            public readonly Regex Exclude;
            public readonly bool ConnectedPieces;

            public Group(string name, string include, string exclude = null, bool connectedPieces = true)
            {
                Name = name;
                Include = new Regex(include);
                Exclude = string.IsNullOrEmpty(exclude) ? null : new Regex(exclude);
                ConnectedPieces = connectedPieces;
            }
        }

        const float MovedThreshold = 1e-6f;

        [MenuItem("Tools/Avatar Agent Kit/Export UV Triangles")]
        static void ExportSelected()
        {
            var renderer = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Renderer>() : null;
            if (MeshOf(renderer) == null)
            {
                EditorUtility.DisplayDialog("Export UV Triangles",
                    "Select a GameObject with a Skinned Mesh Renderer or a Mesh Renderer first.", "OK");
                return;
            }
            var path = EditorUtility.SaveFilePanel("Export UV triangles", "TextureSources", renderer.name + "_uv", "json");
            if (string.IsNullOrEmpty(path))
                return;
            Debug.Log("[UV export] " + ExportSubmeshes(renderer, path));
        }

        /// <summary>Every triangle of every submesh, one group per submesh. Returns a summary.</summary>
        public static string ExportSubmeshes(Renderer renderer, string outPath, int uvChannel = 0)
        {
            var mesh = RequireMesh(renderer);
            var groups = new Dictionary<string, int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++)
                groups["submesh" + s] = mesh.GetTriangles(s);
            return Write(outPath, $"{Describe(renderer, mesh)}, every submesh", mesh, uvChannel, groups);
        }

        /// <summary>Triangles of one submesh moved by matching shape keys, one group each. Returns a summary.</summary>
        public static string ExportByShapeKeys(Renderer renderer, int submesh, string outPath, params Group[] groups) =>
            ExportByShapeKeys(renderer, submesh, outPath, 0, groups);

        public static string ExportByShapeKeys(Renderer renderer, int submesh, string outPath, int uvChannel, params Group[] groups)
        {
            var mesh = RequireMesh(renderer);
            if (submesh < 0 || submesh >= mesh.subMeshCount)
                throw new ArgumentException($"submesh {submesh} doesn't exist; {mesh.name} has {mesh.subMeshCount}");
            var triangles = mesh.GetTriangles(submesh);
            var pieces = ConnectedPieces(triangles, mesh.vertexCount);

            var result = new Dictionary<string, int[]>();
            var notes = new List<string>();
            foreach (var group in groups)
            {
                var keys = MatchingShapeKeys(mesh, group);
                if (keys.Count == 0)
                    throw new ArgumentException($"group '{group.Name}': no shape key matches; {mesh.name} has: " +
                                                string.Join(", ", AllShapeKeys(mesh).Take(40)));
                var moved = MovedVertices(mesh, keys);
                var picked = new List<int>();
                var movedPieces = new HashSet<int>();
                for (int t = 0; t < triangles.Length; t += 3)
                    for (int k = 0; k < 3; k++)
                        if (moved[triangles[t + k]])
                            movedPieces.Add(pieces[triangles[t]]);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    bool take = group.ConnectedPieces
                        ? movedPieces.Contains(pieces[triangles[t]])
                        : moved[triangles[t]] || moved[triangles[t + 1]] || moved[triangles[t + 2]];
                    if (take)
                        picked.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }
                result[group.Name] = picked.ToArray();
                notes.Add($"{group.Name}: shape keys {string.Join(", ", keys.Select(k => mesh.GetBlendShapeName(k)))}");
            }
            return Write(outPath, $"{Describe(renderer, mesh)}, submesh {submesh}; " + string.Join("; ", notes),
                mesh, uvChannel, result);
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
                return smr.sharedMesh;
            var filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
            return filter ? filter.sharedMesh : null;
        }

        static Mesh RequireMesh(Renderer renderer)
        {
            var mesh = MeshOf(renderer);
            if (mesh == null)
                throw new ArgumentException("the renderer has no mesh");
            return mesh;
        }

        static string Describe(Renderer renderer, Mesh mesh) =>
            $"{renderer.GetType().Name} '{renderer.name}' (mesh '{mesh.name}')";

        static IEnumerable<string> AllShapeKeys(Mesh mesh) =>
            Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName);

        static List<int> MatchingShapeKeys(Mesh mesh, Group group) =>
            Enumerable.Range(0, mesh.blendShapeCount)
                .Where(i =>
                {
                    var name = mesh.GetBlendShapeName(i);
                    return group.Include.IsMatch(name) && (group.Exclude == null || !group.Exclude.IsMatch(name));
                })
                .ToList();

        static bool[] MovedVertices(Mesh mesh, List<int> shapeKeys)
        {
            int n = mesh.vertexCount;
            var moved = new bool[n];
            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            foreach (var key in shapeKeys)
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(key); f++)
                {
                    mesh.GetBlendShapeFrameVertices(key, f, dv, dn, dt);
                    for (int i = 0; i < n; i++)
                        if (dv[i].sqrMagnitude > MovedThreshold * MovedThreshold)
                            moved[i] = true;
                }
            return moved;
        }

        /// <summary>Piece id per vertex: vertices joined by a triangle share one.</summary>
        static int[] ConnectedPieces(int[] triangles, int vertexCount)
        {
            var parent = Enumerable.Range(0, vertexCount).ToArray();

            int Root(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = Root(triangles[t]);
                for (int k = 1; k < 3; k++)
                {
                    int b = Root(triangles[t + k]);
                    if (a != b)
                        parent[Math.Max(a, b)] = Math.Min(a, b);
                    a = Root(a);
                }
            }
            for (int i = 0; i < vertexCount; i++)
                parent[i] = Root(i);
            return parent;
        }

        static string Write(string outPath, string source, Mesh mesh, int uvChannel, Dictionary<string, int[]> groups)
        {
            var uvs = new List<Vector2>();
            mesh.GetUVs(uvChannel, uvs);
            if (uvs.Count != mesh.vertexCount)
                throw new ArgumentException($"{mesh.name} has no UVs in channel {uvChannel}");

            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\n  \"source\": \"").Append(source.Replace("\\", "\\\\").Replace("\"", "\\\""))
              .Append("\",\n  \"uv_origin\": \"bottom-left\",\n  \"uv_channel\": ").Append(uvChannel)
              .Append(",\n  \"groups\": {");
            bool firstGroup = true;
            foreach (var pair in groups)
            {
                sb.Append(firstGroup ? "\n" : ",\n").Append("    \"").Append(pair.Key).Append("\": [");
                firstGroup = false;
                var tris = pair.Value;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    sb.Append(t == 0 ? "\n      [" : ",\n      [");
                    for (int k = 0; k < 3; k++)
                    {
                        var uv = uvs[tris[t + k]];
                        if (k > 0) sb.Append(", ");
                        sb.Append(uv.x.ToString("R", c)).Append(", ").Append(uv.y.ToString("R", c));
                    }
                    sb.Append(']');
                }
                sb.Append(tris.Length > 0 ? "\n    ]" : "]");
            }
            sb.Append("\n  }\n}\n");

            var full = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString());
            return $"wrote {full}: " + string.Join(", ", groups.Select(g => $"{g.Key} {g.Value.Length / 3} triangles"));
        }
    }
}
