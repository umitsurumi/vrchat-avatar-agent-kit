// Mask of the body skin hidden under clothes, for AAO "Remove Mesh By Mask" (Remove White).
// Menu: Tools/Avatar Good/Body Occlusion Mask (select the avatar root, or anything under it).
//
// A body vertex is hidden when every ray inside a cone around its normal hits an occluder: clothing
// that moves with the body. A triangle is removed only when all three vertices are hidden and none of
// them touches a visible triangle (a one-ring margin), so the cut edge stays under the cloth.
//
// AAO samples the mask at each vertex UV (nearest pixel) and removes a triangle when all three samples
// are white, so the mask holds one pixel per vertex: white for removable, black for kept, black wins
// where two vertices share a pixel. Consequences:
//   - No mesh simplifier (Meshia) on the same body mesh: it runs before AAO and moves vertex UVs.
//   - Rerun after any change to the outfit, the body mesh or its shape keys.
// Use: put AAO "Remove Mesh By Mask" on the body renderer, mask = this PNG for each of its material
// slots that the mask's UVs cover, mode Remove White (the enum order is Remove Black, Remove White).
//
// Occluders: whole renderers in Occluders. A garment that swings (PhysBones) only covers reliably
// where it doesn't move, so PartialOccluders count just the triangles not weighted to bones whose
// name starts with the given prefix (a skirt's waistband). This is judged on the garment's
// prefab-source mesh, so a garment whose weights were changed afterwards (GarmentFollowsBody) still
// counts the same part. Anything that can be taken off (a toggled coat, gloves) is not an occluder.
//
// Example (Green build): top, collar, socks, shoes and underwear, plus the skirt's waistband; the
// body went 30,106 -> about 17.5k triangles, thighs kept for views up the skirt.
//
// Gotcha: Physics.Raycast overloads without RaycastHit ignore back faces even with
// Physics.queriesHitBackfaces, and rays from the skin hit the clothing from behind.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarGood.Editor
{
    public static class BodyOcclusionMask
    {
        // ---- CONFIG: replace with your avatar's -------------------------------------------------
        const string BodyRenderer = "Manuka_body";
        static readonly string[] Occluders = { "Sera", "Collar", "Socks", "Loafer", "Manuka_underwear_panty" };
        static readonly (string renderer, string movingBonePrefix)[] PartialOccluders = { ("Skirt", "skirt_") };
        const string MaskPath = "Assets/_Generated/Body_hidden.png";
        const float ConeDegrees = 60f;     // larger hides less
        const int MaskSize = 4096;
        const int Layer = 31;              // a layer nothing else in the scene uses
        // --------------------------------------------------------------------------------------------

        [MenuItem("Tools/Avatar Good/Body Occlusion Mask")]
        static void Run()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected ? selected.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true) : null;
            if (descriptor == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            var (removable, removed, total) = Generate(descriptor.gameObject);
            Debug.Log($"Body occlusion mask: {removable} removable vertices, {removed}/{total} triangles removed -> {MaskPath}");
        }

        public static (int removableVerts, int removedTris, int totalTris) Generate(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer Find(string name) => renderers.First(r => r.name == name);

            var temps = new List<GameObject>();
            try
            {
                foreach (var name in Occluders) temps.Add(MakeOccluder(Find(name), null));
                foreach (var (name, prefix) in PartialOccluders) temps.Add(MakeOccluder(Find(name), prefix));
                Physics.SyncTransforms();

                var body = Find(BodyRenderer);
                var escape = EscapeAngles(body);
                return WriteMask(body.sharedMesh, escape);
            }
            finally
            {
                foreach (var go in temps)
                {
                    Object.DestroyImmediate(go.GetComponent<MeshCollider>().sharedMesh);
                    Object.DestroyImmediate(go);
                }
            }
        }

        // movingBonePrefix: null = the whole mesh; else drop triangles weighted to bones with that prefix
        static GameObject MakeOccluder(SkinnedMeshRenderer smr, string movingBonePrefix)
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            if (movingBonePrefix != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(smr)?.sharedMesh ?? smr.sharedMesh;
                var weights = source.GetAllBoneWeights();
                var perVertex = source.GetBonesPerVertex();
                var bones = smr.bones;
                var moving = new bool[mesh.vertexCount];
                for (int v = 0, k = 0; v < perVertex.Length; v++)
                    for (int j = 0; j < perVertex[v]; j++, k++)
                        if (weights[k].weight > 0.001f && bones[weights[k].boneIndex].name.StartsWith(movingBonePrefix))
                            moving[v] = true;
                var tris = mesh.triangles;
                var keep = new List<int>();
                for (int t = 0; t < tris.Length; t += 3)
                    if (!moving[tris[t]] && !moving[tris[t + 1]] && !moving[tris[t + 2]])
                        keep.AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                mesh.subMeshCount = 1;
                mesh.SetTriangles(keep, 0);
            }
            var go = new GameObject("__occluder_" + smr.name) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // Smallest angle from the normal at which a ray escapes (90 = never within 85 degrees).
        static byte[] EscapeAngles(SkinnedMeshRenderer body)
        {
            var baked = new Mesh();
            body.BakeMesh(baked, true);
            var verts = baked.vertices;
            var normals = baked.normals;
            Object.DestroyImmediate(baked);
            var toWorld = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);

            // a Fibonacci sphere, keeping directions within 85 degrees of +z, nearest to the normal first
            var dirs = new List<Vector3>();
            const int samples = 400;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < samples; i++)
            {
                float z = 1f - 2f * (i + 0.5f) / samples, r = Mathf.Sqrt(1 - z * z), th = golden * i;
                var d = new Vector3(r * Mathf.Cos(th), r * Mathf.Sin(th), z);
                if (d.z >= Mathf.Cos(85f * Mathf.Deg2Rad)) dirs.Add(d);
            }
            dirs.Sort((a, b) => b.z.CompareTo(a.z));

            var escape = new byte[verts.Length];
            bool oldBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    var p = toWorld.MultiplyPoint3x4(verts[i]);
                    var n = toWorld.MultiplyVector(normals[i]).normalized;
                    var rot = Quaternion.FromToRotation(Vector3.forward, n);
                    byte angle = 90;
                    foreach (var d in dirs)
                    {
                        if (Physics.Raycast(p + n * 0.0005f, rot * d, out _, 0.6f, 1 << Layer)) continue;
                        angle = (byte)Mathf.RoundToInt(Mathf.Acos(Mathf.Clamp(d.z, -1f, 1f)) * Mathf.Rad2Deg);
                        break;
                    }
                    escape[i] = angle;
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = oldBackfaces;
            }
            return escape;
        }

        static (int, int, int) WriteMask(Mesh mesh, byte[] escape)
        {
            var verts = mesh.vertices;
            var uv = mesh.uv;
            var tris = mesh.triangles;

            // vertices sharing a position (UV seams) are hidden only if all copies are
            var groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < verts.Length; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 1e5f);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
                list.Add(i);
            }
            void Weld(bool[] flags)
            {
                foreach (var list in groups.Values)
                {
                    bool all = list.All(i => flags[i]);
                    foreach (var i in list) flags[i] = all;
                }
            }

            var hidden = escape.Select(e => e >= ConeDegrees).ToArray();
            Weld(hidden);
            var removable = (bool[])hidden.Clone();
            for (int t = 0; t < tris.Length; t += 3)
                if (!(hidden[tris[t]] && hidden[tris[t + 1]] && hidden[tris[t + 2]]))
                    removable[tris[t]] = removable[tris[t + 1]] = removable[tris[t + 2]] = false;
            Weld(removable);

            int Pixel(Vector2 u)
            {
                int x = Mathf.Min(MaskSize - 1, Mathf.FloorToInt((u.x - Mathf.Floor(u.x)) * MaskSize));
                int y = Mathf.Min(MaskSize - 1, Mathf.FloorToInt((u.y - Mathf.Floor(u.y)) * MaskSize));
                return y * MaskSize + x;
            }
            var pixels = Enumerable.Repeat(new Color32(0, 0, 0, 255), MaskSize * MaskSize).ToArray();
            var keepPixels = new HashSet<int>();
            for (int i = 0; i < verts.Length; i++) if (!removable[i]) keepPixels.Add(Pixel(uv[i]));
            for (int i = 0; i < verts.Length; i++)
                if (removable[i] && !keepPixels.Contains(Pixel(uv[i])))
                    pixels[Pixel(uv[i])] = new Color32(255, 255, 255, 255);

            int removed = 0;
            for (int t = 0; t < tris.Length; t += 3)
                if (pixels[Pixel(uv[tris[t]])].r > 127 && pixels[Pixel(uv[tris[t + 1]])].r > 127 && pixels[Pixel(uv[tris[t + 2]])].r > 127)
                    removed++;

            var tex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(MaskPath));
            File.WriteAllBytes(MaskPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(MaskPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = MaskSize;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.sRGBTexture = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            return (removable.Count(x => x), removed, tris.Length / 3);
        }
    }
}
