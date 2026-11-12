// Material slots and skinned meshes down to Good (8 slots, 2 skinned meshes: face + one merged mesh).
// Menu: Tools/Avatar Good/Material Slot Merge (select the avatar root, or anything under it). Rerunnable.
//
// AAO Trace and Optimize merges always-on skinned meshes into one, and identical materials into one
// slot. What keeps a renderer out (GOTCHAS.md, auto-merge conditions) is mostly toggles, and the
// accessories with their own small materials. Three moves here:
//
// 1. Accessories (glasses, pins, a brooch). Each one:
//    - becomes a SkinnedMeshRenderer bound to its own transform (or, with Replaces, to the averaged
//      bones of the part it replaces, which is then retired as EditorOnly);
//    - gets a "Hide_*" blend shape that collapses it into a point inside the head;
//    - uses one shared material whose main texture is a tiny point-filtered palette, with every
//      accessory's UVs on its swatch. A second palette masks the matcap per swatch, so metal stays
//      shiny and enamel doesn't;
//    - copies the reference renderer's settings, because AAO only merges renderers that match.
//    Its "X OFF" menu item then drives the blend shape through an MA Shape Changer, in place of an
//    Object Toggle. The accessory stays active, so AAO merges it into the main mesh.
// 2. Material swaps: a renderer takes another's materials (on the Green build the skirt used the
//    top's material: same texture, only the rim shade differed).
// 3. Atlas merges: renderers merged with AAO Merge Skinned Mesh, plus AAO Merge Material packing their
//    textures into one atlas (on the Green build: body + underwear, two 1024 textures -> 2048 x 1024).
//
// lilToon specifics: the palette and mask go to _MainTex and _MatCapBlendMask. MergeMaterial also
// atlases legacy properties lilToon doesn't use (_BaseMap, _BaseColorMap); shrink those if texture
// memory matters.
// Surin's "lilToon light setting" animates every lilToon renderer, with the curves typed by renderer
// type. AAO doesn't merge renderers whose material animations differ, so the curves of an accessory
// converted from MeshRenderer are moved to SkinnedMeshRenderer, and a replacing accessory gets the
// replaced part's curves. Without that tool, LightSettingDir finds nothing and this step is skipped.
//
// On the Green build the palette had 8 rows, because the speech-bubble pin's emission pulse map uses
// the same row grid. This version has one row; add rows if a part needs per-row emission.

using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace AvatarGood.Editor
{
    public static class MaterialSlotMerge
    {
        class Accessory
        {
            public string Renderer;                 // object with the accessory's MeshRenderer or SkinnedMeshRenderer
            public string MenuItem, Shape;          // "X OFF" menu item and its hide shape; null: always shown
            public string SourceAsset, SourceMesh;  // the original mesh (SourceMesh null: first mesh in the asset)
            public int Swatch = -1;                 // palette column for every vertex; -1: the mesh already has palette UVs
            public int SimplifyTo;                  // Meshia target triangles, 0: none
            public string Replaces;                 // skin to this renderer's averaged bones, and retire it
        }

        class AtlasMerge
        {
            public string Holder;                   // new object at the avatar root
            public string[] Renderers;              // first one is the reference material
            public Rect[] Rects;                    // each source's place in the atlas
            public Vector2Int Size;
        }

        // ---- CONFIG: replace with your avatar's -------------------------------------------------
        const string OutputDir = "Assets/_Generated/Accessories";
        const string BaseMaterial = "Assets/_Generated/Hairpin/Pin_Metal.mat";   // copied to Accessories.mat
        const string ReferenceRenderer = "Manuka_body";   // renderer settings to copy (root bone, anchor, ...)
        // What the hair's and outfit's MA Mesh Settings give their renderers at build. AAO groups meshes
        // for merging by these, so the accessories use the same bounds (around the root bone).
        static readonly Bounds MergedBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        const float HideHeight = 0.06f;                    // hide point: this far above the Head bone

        static readonly (Color color, float matcap)[] Swatches =
        {
            (Hex("DBDEE6"), 1.00f),   // 0 metal
            (Hex("8F9EC2"), 0.12f),   // 1 enamel
            (Hex("54575E"), 0.45f),   // 2 gunmetal
            (Hex("B4A5F2"), 0.00f),   // 3 lavender
        };

        static readonly Accessory[] Accessories =
        {
            new Accessory { Renderer = "SpeechBubblePin", MenuItem = "Hairpin OFF", Shape = "Hide_Hairpin",
                SourceAsset = "Assets/_Generated/Hairpin/SpeechBubblePin.fbx", SourceMesh = "SpeechBubblePin" },
            new Accessory { Renderer = "GlassesMesh", MenuItem = "Glasses OFF", Shape = "Hide_Glasses",
                SourceAsset = "Assets/_Generated/Glasses/Glasses.obj", Swatch = 2 },
            new Accessory { Renderer = "Amepin_A", MenuItem = "HairpinSet_R OFF", Shape = "Hide_Pins",
                SourceAsset = "Assets/Hoshigumo/HairpinSet/Model/HairpinSet.fbx", SourceMesh = "Pin_Bar",
                Swatch = 3, SimplifyTo = 400 },
            new Accessory { Renderer = "Amepin_B", MenuItem = "HairpinSet_R OFF", Shape = "Hide_Pins",
                SourceAsset = "Assets/Hoshigumo/HairpinSet/Model/HairpinSet.fbx", SourceMesh = "Pin_Bar",
                Swatch = 3, SimplifyTo = 400 },
        };

        static readonly (string renderer, string materialsFrom)[] MaterialSwaps = { ("Skirt", "Sera") };

        static readonly AtlasMerge[] AtlasMerges =
        {
            new AtlasMerge { Holder = "Body+Costume", Renderers = new[] { "Manuka_body", "Manuka_underwear_panty" },
                Rects = new[] { new Rect(0f, 0f, 0.5f, 1f), new Rect(0.5f, 0f, 0.5f, 1f) }, Size = new Vector2Int(2048, 1024) },
        };

        const string LightSettingDir = "Assets/Surin/Tools/Liltoon light setting/Generated/";
        // --------------------------------------------------------------------------------------------

        [MenuItem("Tools/Avatar Good/Material Slot Merge")]
        static void Run()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected ? selected.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true) : null;
            if (descriptor == null) { Debug.LogError("Select the avatar (anything under its VRC Avatar Descriptor)"); return; }
            Debug.Log(Setup(descriptor.gameObject));
        }

        public static string Setup(GameObject root)
        {
            var log = new System.Text.StringBuilder();
            System.IO.Directory.CreateDirectory(OutputDir);
            var material = AccessoryMaterial();
            var head = root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var hidePoint = head.position + Vector3.up * HideHeight;   // inside the skull
            var transforms = root.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => transforms.Single(t => t.name == name);

            var reference = Find(ReferenceRenderer).GetComponent<SkinnedMeshRenderer>();
            foreach (var accessory in Accessories)
            {
                var go = Find(accessory.Renderer).gameObject;
                var renderer = MakeSkinned(go, material);
                var replaced = accessory.Replaces != null ? Find(accessory.Replaces).GetComponent<SkinnedMeshRenderer>() : null;
                renderer.sharedMesh = SkinnedCopy(accessory, go.transform.InverseTransformPoint(hidePoint), renderer, replaced);
                MatchRendererSettings(renderer, reference);
                if (replaced != null) Retire(replaced.gameObject);
                if (accessory.SimplifyTo > 0)
                {
                    var meshia = go.GetComponent<Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier>()
                                 ?? Undo.AddComponent<Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier>(go);
                    Undo.RecordObject(meshia, "Simplify accessory");
                    meshia.target = new Meshia.MeshSimplification.MeshSimplificationTarget
                    {
                        Kind = Meshia.MeshSimplification.MeshSimplificationTargetKind.AbsoluteTriangleCount,
                        Value = accessory.SimplifyTo,
                    };
                }
                log.Append($"{accessory.Renderer}: skinned, {accessory.Shape ?? "no hide shape"}"
                           + (replaced != null ? $", replaces {replaced.name}" : "") + "\n");
            }

            foreach (var group in Accessories.Where(a => a.MenuItem != null).GroupBy(a => a.MenuItem))
            {
                // the object with the menu item itself; a Menu Install Target elsewhere may share its name
                var item = transforms.Single(t => t.name == group.Key && t.GetComponent<ModularAvatarMenuItem>() != null).gameObject;
                foreach (var toggle in item.GetComponents<ModularAvatarObjectToggle>())
                    Undo.DestroyObjectImmediate(toggle);
                var changer = item.GetComponent<ModularAvatarShapeChanger>() ?? Undo.AddComponent<ModularAvatarShapeChanger>(item);
                var so = new SerializedObject(changer);
                var shapes = so.FindProperty("m_shapes");
                var list = group.ToList();
                shapes.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    var e = shapes.GetArrayElementAtIndex(i);
                    var target = Find(list[i].Renderer);
                    e.FindPropertyRelative("Object.referencePath").stringValue = AnimationUtility.CalculateTransformPath(target, root.transform);
                    e.FindPropertyRelative("Object.targetObject").objectReferenceValue = target.gameObject;
                    e.FindPropertyRelative("ShapeName").stringValue = list[i].Shape;
                    e.FindPropertyRelative("ChangeType").enumValueIndex = 1;   // Set
                    e.FindPropertyRelative("Value").floatValue = 100f;
                }
                so.ApplyModifiedProperties();
                log.Append($"{group.Key}: Shape Changer on {string.Join(", ", list.Select(a => a.Renderer))}\n");
            }

            foreach (var (name, from) in MaterialSwaps)
            {
                var renderer = Find(name).GetComponent<SkinnedMeshRenderer>();
                Undo.RecordObject(renderer, "Material swap");
                renderer.sharedMaterials = Find(from).GetComponent<SkinnedMeshRenderer>().sharedMaterials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                log.Append($"{name} material -> {renderer.sharedMaterial.name}\n");
            }

            foreach (var atlas in AtlasMerges)
                log.Append(SetupAtlasMerge(root, atlas, atlas.Renderers.Select(n => Find(n).GetComponent<SkinnedMeshRenderer>()).ToArray()));

            log.Append(RetargetLightSettingCurves(root));
            foreach (var accessory in Accessories.Where(a => a.Replaces != null))
                log.Append(CopyLightSettingCurves(root, Find(accessory.Replaces), Find(accessory.Renderer)));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            return log.ToString();
        }

        static Material AccessoryMaterial()
        {
            var main = Palette("AccessoryPalette", s => s.color, true);
            var mask = Palette("AccessoryMatCapMask", s => new Color(s.matcap, s.matcap, s.matcap, 1), false);
            var path = $"{OutputDir}/Accessories.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                AssetDatabase.CopyAsset(BaseMaterial, path);
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            material.SetTexture("_MainTex", main);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_MatCapBlendMask", mask);
            material.SetFloat("_MatCapBlend", 1f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Texture2D Palette(string name, System.Func<(Color color, float matcap), Color> pick, bool sRGB)
        {
            var path = $"{OutputDir}/{name}.png";
            var tex = new Texture2D(Swatches.Length, 1, TextureFormat.RGBA32, false);
            for (int x = 0; x < Swatches.Length; x++) tex.SetPixel(x, 0, pick(Swatches[x]));
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = sRGB;   // the matcap mask holds linear strengths
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Replace a MeshRenderer with a SkinnedMeshRenderer bound to the object itself. A Meshia
        // component needs the renderer, so it comes off first; Setup adds it back from SimplifyTo.
        static SkinnedMeshRenderer MakeSkinned(GameObject go, Material material)
        {
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                var meshia = go.GetComponent<Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier>();
                if (meshia != null) Undo.DestroyObjectImmediate(meshia);
                Undo.DestroyObjectImmediate(go.GetComponent<MeshRenderer>());
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null) Undo.DestroyObjectImmediate(filter);
                smr = Undo.AddComponent<SkinnedMeshRenderer>(go);
            }
            Undo.RecordObject(smr, "Skinned accessory");
            smr.bones = new[] { go.transform };
            smr.rootBone = go.transform;
            smr.sharedMaterials = new[] { material };
            return smr;
        }

        static Mesh SkinnedCopy(Accessory accessory, Vector3 hideLocal, SkinnedMeshRenderer renderer, SkinnedMeshRenderer replaced)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(accessory.SourceAsset).OfType<Mesh>()
                .First(m => accessory.SourceMesh == null || m.name == accessory.SourceMesh);
            var mesh = Object.Instantiate(source);
            mesh.name = accessory.Renderer;
            if (mesh.subMeshCount > 1)
            {
                var all = Enumerable.Range(0, mesh.subMeshCount).SelectMany(i => mesh.GetTriangles(i)).ToArray();
                mesh.subMeshCount = 1;
                mesh.SetTriangles(all, 0);
            }
            if (accessory.Swatch >= 0)
                mesh.uv = Enumerable.Repeat(new Vector2((accessory.Swatch + 0.5f) / Swatches.Length, 0.5f), mesh.vertexCount).ToArray();
            if (replaced == null)
            {
                mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, mesh.vertexCount).ToArray();
            }
            else
            {
                var (bones, weight) = AverageWeights(replaced);
                renderer.bones = bones;
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray();
                mesh.boneWeights = Enumerable.Repeat(weight, mesh.vertexCount).ToArray();
            }
            if (accessory.Shape != null)
            {
                var deltas = mesh.vertices.Select(v => hideLocal - v).ToArray();
                mesh.AddBlendShapeFrame(accessory.Shape, 100f, deltas, null, null);
            }
            mesh.RecalculateBounds();

            var path = $"{OutputDir}/{accessory.Renderer}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            CopyMesh(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);   // else SaveAssets skips it and the old mesh comes back
            AssetDatabase.SaveAssets();
            return existing;
        }

        // The (up to) four bones carrying most of a renderer's weight, and their share of it.
        static (Transform[] bones, BoneWeight weight) AverageWeights(SkinnedMeshRenderer source)
        {
            var sums = new Dictionary<int, float>();
            void Add(int bone, float w) { if (w > 0) sums[bone] = (sums.TryGetValue(bone, out var s) ? s : 0) + w; }
            foreach (var bw in source.sharedMesh.boneWeights)
            {
                Add(bw.boneIndex0, bw.weight0); Add(bw.boneIndex1, bw.weight1);
                Add(bw.boneIndex2, bw.weight2); Add(bw.boneIndex3, bw.weight3);
            }
            var top = sums.OrderByDescending(kv => kv.Value).Take(4).ToArray();
            var total = top.Sum(kv => kv.Value);
            float W(int i) => i < top.Length ? top[i].Value / total : 0f;
            var weight = new BoneWeight
            {
                boneIndex0 = 0, weight0 = W(0), boneIndex1 = top.Length > 1 ? 1 : 0, weight1 = W(1),
                boneIndex2 = top.Length > 2 ? 2 : 0, weight2 = W(2), boneIndex3 = top.Length > 3 ? 3 : 0, weight3 = W(3),
            };
            return (top.Select(kv => source.bones[kv.Key]).ToArray(), weight);
        }

        static void Retire(GameObject go)
        {
            Undo.RecordObject(go, "Replaced by accessory");
            go.tag = "EditorOnly";
            go.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        }

        // Overwrite a mesh asset in place (keeps its GUID). EditorUtility.CopySerialized keeps the old
        // vertex buffer when the vertex count changes, which leaves the new triangles pointing at garbage.
        static void CopyMesh(Mesh src, Mesh dst)
        {
            dst.Clear();
            dst.ClearBlendShapes();
            dst.name = src.name;
            dst.indexFormat = src.indexFormat;
            dst.vertices = src.vertices;
            dst.normals = src.normals;
            dst.tangents = src.tangents;
            dst.uv = src.uv;
            dst.colors = src.colors;
            dst.subMeshCount = 1;
            dst.SetTriangles(src.triangles, 0);
            dst.bindposes = src.bindposes;
            dst.boneWeights = src.boneWeights;
            var dv = new Vector3[src.vertexCount];
            var dn = new Vector3[src.vertexCount];
            var dt = new Vector3[src.vertexCount];
            for (int s = 0; s < src.blendShapeCount; s++)
                for (int f = 0; f < src.GetBlendShapeFrameCount(s); f++)
                {
                    src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    dst.AddBlendShapeFrame(src.GetBlendShapeName(s), src.GetBlendShapeFrameWeight(s, f), dv, dn, dt);
                }
            dst.RecalculateBounds();
        }

        static string SetupAtlasMerge(GameObject root, AtlasMerge atlas, SkinnedMeshRenderer[] renderers)
        {
            var holder = root.transform.Find(atlas.Holder);
            if (holder == null)
            {
                holder = new GameObject(atlas.Holder).transform;
                Undo.RegisterCreatedObjectUndo(holder.gameObject, "Atlas merge");
                holder.SetParent(root.transform, false);
                Undo.AddComponent<SkinnedMeshRenderer>(holder.gameObject);
                var merge = Undo.AddComponent<MergeSkinnedMesh>(holder.gameObject);
                merge.Initialize(2);
            }
            var merged = holder.GetComponent<SkinnedMeshRenderer>();
            Undo.RecordObject(merged, "Atlas merge renderer");
            merged.rootBone = renderers[0].rootBone;   // Merge Skinned Mesh doesn't set it
            MatchRendererSettings(merged, renderers[0]);
            var mergeSkinned = holder.GetComponent<MergeSkinnedMesh>();
            mergeSkinned.SourceSkinnedMeshRenderers.Clear();
            mergeSkinned.SourceSkinnedMeshRenderers.UnionWith(renderers);

            // Merge Material is internal to AAO: add by type, fill through serialization
            var mergeMaterialType = System.Type.GetType("Anatawa12.AvatarOptimizer.MergeMaterial, com.anatawa12.avatar-optimizer.runtime", true);
            var mergeMaterial = holder.GetComponent(mergeMaterialType) ?? Undo.AddComponent(holder.gameObject, mergeMaterialType);
            var so = new SerializedObject(mergeMaterial);
            var merges = so.FindProperty("merges");
            merges.arraySize = 1;
            var info = merges.GetArrayElementAtIndex(0);
            info.FindPropertyRelative("referenceMaterial").objectReferenceValue = renderers[0].sharedMaterial;
            info.FindPropertyRelative("textureSize").vector2IntValue = atlas.Size;
            var format = info.FindPropertyRelative("mergedFormat");
            format.enumValueIndex = System.Array.IndexOf(format.enumNames, "BC7");   // default is uncompressed
            var sources = info.FindPropertyRelative("source");
            sources.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
            {
                var e = sources.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("material").objectReferenceValue = renderers[i].sharedMaterial;
                e.FindPropertyRelative("targetRect").rectValue = atlas.Rects[i];
            }
            so.ApplyModifiedProperties();
            return $"{atlas.Holder}: {string.Join(" + ", renderers.Select(r => r.name))} -> {atlas.Size.x}x{atlas.Size.y} atlas\n";
        }

        static void MatchRendererSettings(SkinnedMeshRenderer renderer, SkinnedMeshRenderer reference)
        {
            Undo.RecordObject(renderer, "Renderer settings");
            renderer.rootBone = reference.rootBone;
            renderer.localBounds = MergedBounds;
            renderer.gameObject.layer = reference.gameObject.layer;
            renderer.shadowCastingMode = reference.shadowCastingMode;
            renderer.receiveShadows = reference.receiveShadows;
            renderer.lightProbeUsage = reference.lightProbeUsage;
            renderer.reflectionProbeUsage = reference.reflectionProbeUsage;
            renderer.probeAnchor = reference.probeAnchor;
            renderer.skinnedMotionVectors = reference.skinnedMotionVectors;
            renderer.allowOcclusionWhenDynamic = reference.allowOcclusionWhenDynamic;
            renderer.updateWhenOffscreen = reference.updateWhenOffscreen;
        }

        static string RetargetLightSettingCurves(GameObject root)
        {
            int moved = 0;
            var dir = LightSettingDir + root.name;
            if (!AssetDatabase.IsValidFolder(dir)) return "light setting: none\n";
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(MeshRenderer)) continue;
                    var target = root.transform.Find(binding.path);
                    if (target == null || target.GetComponent<MeshRenderer>() != null || target.GetComponent<SkinnedMeshRenderer>() == null) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    var skinned = binding;
                    skinned.type = typeof(SkinnedMeshRenderer);
                    AnimationUtility.SetEditorCurve(clip, skinned, curve);
                    moved++;
                }
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
            return $"light setting: {moved} curves now on SkinnedMeshRenderer\n";
        }

        static string CopyLightSettingCurves(GameObject root, Transform from, Transform to)
        {
            int copied = 0;
            var dir = LightSettingDir + root.name;
            if (!AssetDatabase.IsValidFolder(dir)) return "light setting: none\n";
            var fromPath = AnimationUtility.CalculateTransformPath(from, root.transform);
            var toPath = AnimationUtility.CalculateTransformPath(to, root.transform);
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.path == fromPath))
                {
                    var copy = binding;
                    copy.path = toPath;
                    copy.type = typeof(SkinnedMeshRenderer);
                    AnimationUtility.SetEditorCurve(clip, copy, AnimationUtility.GetEditorCurve(clip, binding));
                    copied++;
                }
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
            return $"light setting: {copied} curves {from.name} -> {to.name}\n";
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
    }
}
