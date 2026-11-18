// Renders avatars into PNGs and one sheet, without touching the user's scene: each target is copied into a
// preview scene with its scripts switched off, posed, and every skinned mesh is baked (BakeMesh) into a static
// mesh, so the picture is exactly the current pose and blendshapes (in edit mode, the scene shows those a
// frame late). Cameras frame the avatar from its bounds and view position; the light follows the camera.
//
// From code (execute_code):  return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar_bakecheck");
//   return AvatarAgentKit.Editor.RenderAvatar.Run("A_bakecheck,B_bakecheck", "front,threequarter,upper");
// targets: root objects, comma-separated; one row each. Left out: the only baked clone.
// shots:   front, back, left, right, threequarter (front right), upper (head to hips), face. One column each.
//   left is seen from the avatar's left side, so the avatar faces left in the picture; right faces right.
// armsDown: rotate the upper arms down. Default: on for a baked clone in edit mode; off in play mode (keeps a
//   pose set up for a test) and for an unbaked avatar (its outfits are not merged yet, so sleeves would stay).
// focus, size: aim every shot at one part: a humanoid bone (Hips, Chest, LeftUpperLeg, ...) or a path under the
//   target (Armature/Hips/Skirt), framed size metres high. The shot names then give only the direction
//   (face and upper look from the front).
//   return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar_bakecheck", "front,left,back", focus: "Hips", size: 0.6f);
// label: put in front of the file names, e.g. "before" / "after", to tell two runs apart on a texture-kit sheet.
// Menu: Tools/Avatar Agent Kit/Render Avatar (front, back, face of the selected avatar or the only clone).
//
// Output: Temp/AvatarAgentKit/renders/<time>/, one PNG per shot plus a sheet; the result gives the paths.
// A render shows the look; it does not prove the absence of a problem. Look at the sheet before judging.
//
// Verified on: Unity 2022.3.22f1, lilToon 2.3.4; a MANUKA avatar, baked and unbaked, edit and play mode.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace AvatarAgentKit.Editor
{
    public static class RenderAvatar
    {
        const string Tool = "RenderAvatar";
        const float FieldOfView = 30f;
        static readonly Color Background = new Color(0.80f, 0.82f, 0.86f);
        static readonly string[] Shots = { "front", "back", "left", "right", "threequarter", "upper", "face" };

        public static string Run(string targets = null, string shots = "front,back,face", int width = 600, int height = 800,
            bool? armsDown = null, string focus = null, float size = 0.5f, string label = null)
        {
            var shotList = (shots ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
            var unknown = shotList.Where(s => !Shots.Contains(s)).ToList();
            if (shotList.Count == 0 || unknown.Count > 0)
                return Tool + ": refused: unknown shot " + string.Join(", ", unknown.Select(Targets.Quote)) + ". Shots: " + string.Join(", ", Shots);
            if (width < 64 || height < 64 || width > 4096 || height > 4096)
                return Tool + ": refused: width and height must be 64 to 4096 pixels.";
            if (!string.IsNullOrEmpty(focus) && (size < 0.05f || size > 5f))
                return Tool + ": refused: size is the frame height in metres, 0.05 to 5.";
            var prefix = string.IsNullOrWhiteSpace(label) ? "" : Safe(label.Trim()) + "-";

            var err = ResolveTargets(targets, out var objects);
            if (err != null) return Tool + ": refused: " + err;

            var playing = EditorApplication.isPlaying;
            var dir = Path.GetFullPath(Path.Combine("Temp", "AvatarAgentKit", "renders", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(dir);

            var notes = new List<string>();
            var rowLabels = new List<string>();
            var files = new List<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            var created = new List<Object>();
            try
            {
                var lightGo = NewIn(scene, "AAK_Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                var fillGo = NewIn(scene, "AAK_Fill");
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.35f;

                var camGo = NewIn(scene, "AAK_Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.enabled = false;
                cam.fieldOfView = FieldOfView;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
                var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
                created.Add(rt);
                cam.targetTexture = rt;
                var tile = new Texture2D(width, height, TextureFormat.RGB24, false);
                created.Add(tile);
                var sheet = new Texture2D(width * shotList.Count, height * objects.Count, TextureFormat.RGB24, false);
                created.Add(sheet);

                for (int row = 0; row < objects.Count; row++)
                {
                    var target = objects[row];
                    var baked = Targets.IsBakedName(target.name);
                    var pose = armsDown ?? (baked && !playing);
                    if (!baked && !playing && Targets.IsAvatar(target))
                        notes.Add(Targets.Quote(target.name) + " is not baked: this is the avatar before NDMF (outfits not merged, nothing optimized).");

                    var holder = NewIn(scene, "AAK_Holder");
                    holder.SetActive(false);
                    var copy = Object.Instantiate(target, holder.transform);
                    copy.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
                    copy.transform.localScale = target.transform.lossyScale;
                    copy.SetActive(true);
                    foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
                    holder.SetActive(true);

                    var rowLabel = Targets.Quote(target.name);
                    if (pose)
                    {
                        var posed = ArmsDown(target, copy);
                        rowLabel += posed ? " (arms down)" : " (arms not posed: no humanoid rig found)";
                    }
                    rowLabels.Add(rowLabel);

                    var bounds = BakeToStatic(copy, holder.transform, created);
                    var frame = new Frame(copy, bounds);
                    if (!string.IsNullOrEmpty(focus))
                    {
                        var part = FocusTransform(target, copy, focus);
                        if (part == null)
                            return Tool + ": refused: no part " + Targets.Quote(focus) + " in " + Targets.Quote(target.name)
                                + ". Use a humanoid bone (Hips, Chest, Head, LeftUpperLeg, ...; the target must be active) or a path under the root, e.g. \"Armature/Hips\".";
                        frame.Focus(part.position, size);
                    }
                    for (int col = 0; col < shotList.Count; col++)
                    {
                        frame.Place(cam, shotList[col], width / (float)height);
                        AimLights(light.transform, fill.transform, cam.transform);
                        cam.Render();
                        RenderTexture.active = rt;
                        tile.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tile.Apply();
                        RenderTexture.active = null;
                        var file = Path.Combine(dir, prefix + (row + 1) + "-" + Safe(target.name) + "-" + shotList[col] + ".png");
                        File.WriteAllBytes(file, tile.EncodeToPNG());
                        files.Add(file);
                        sheet.SetPixels(col * width, (objects.Count - 1 - row) * height, width, height, tile.GetPixels());
                    }
                    Object.DestroyImmediate(holder);
                }

                sheet.Apply();
                var sheetPath = Path.Combine(dir, prefix + "sheet.png");
                File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
                cam.targetTexture = null;

                var sb = new StringBuilder();
                sb.Append(Tool).Append(": ").Append(objects.Count).Append(objects.Count == 1 ? " row x " : " rows x ")
                    .Append(shotList.Count).Append(shotList.Count == 1 ? " shot -> " : " shots -> ").AppendLine(sheetPath);
                sb.Append("rows: ").AppendLine(string.Join("; ", rowLabels));
                sb.Append("columns: ").Append(string.Join(", ", shotList.Select(Facing)));
                if (!string.IsNullOrEmpty(focus))
                    sb.Append(" (aimed at ").Append(Targets.Quote(focus)).Append(", ").Append(size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" m high)");
                sb.AppendLine();
                sb.Append("single shots in ").Append(dir).Append(": ").AppendLine(string.Join(", ", files.Select(Path.GetFileName)));
                foreach (var n in notes) sb.Append("note: ").AppendLine(n);
                return sb.ToString().TrimEnd();
            }
            finally
            {
                RenderTexture.active = null;
                foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static string ResolveTargets(string targets, out List<GameObject> objects)
        {
            objects = new List<GameObject>();
            if (string.IsNullOrWhiteSpace(targets))
            {
                var clones = Targets.Roots().Where(g => Targets.IsBakedName(g.name)).ToList();
                if (clones.Count == 1) { objects.Add(clones[0]); return null; }
                if (clones.Count > 1) return "several baked clones, name the targets: " + Targets.Names(clones);
                return "nothing baked. Bake first (" + Targets.Ns + "Bake.Run(\"<avatar>\")), or name the objects to render.";
            }
            foreach (var name in targets.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                var found = Targets.RootsNamed(name);
                if (found.Count == 0)
                {
                    var hint = Targets.RootsNamed(name + Targets.BakeSuffix).Count == 1 ? "; did you mean " + Targets.Quote(name + Targets.BakeSuffix) + "?" : "";
                    return "no root object called " + Targets.Quote(name) + " in the open scenes" + hint;
                }
                if (found.Count > 1) return "several root objects are called " + Targets.Quote(name);
                objects.Add(found[0]);
            }
            return null;
        }

        static GameObject NewIn(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // Arms down on the copy; bones are found through the target's Animator and mapped to the copy by path.
        static bool ArmsDown(GameObject target, GameObject copy)
        {
            var anim = target.GetComponent<Animator>();
            if (anim == null || !anim.isHuman || !target.activeInHierarchy) return false;
            return Posing.ArmsDown(b => Mapped(anim.GetBoneTransform(b), target, copy), copy.transform);
        }

        // A humanoid bone by name, else a path under the copy's root.
        static Transform FocusTransform(GameObject target, GameObject copy, string focus)
        {
            if (!int.TryParse(focus, out _) && Enum.TryParse(focus, true, out HumanBodyBones bone) && bone != HumanBodyBones.LastBone)
            {
                var anim = target.GetComponent<Animator>();
                return anim != null && anim.isHuman && target.activeInHierarchy ? Mapped(anim.GetBoneTransform(bone), target, copy) : null;
            }
            return copy.transform.Find(focus);
        }

        static Transform Mapped(Transform bone, GameObject target, GameObject copy)
        {
            if (bone == null) return null;
            if (bone == target.transform) return copy.transform;
            return copy.transform.Find(AnimationUtility.CalculateTransformPath(bone, target.transform));
        }

        // Replaces every visible skinned mesh of the copy with a baked static mesh. Returns the bounds of
        // everything visible.
        static Bounds BakeToStatic(GameObject copy, Transform parent, List<Object> created)
        {
            foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled || smr.sharedMesh == null) continue;
                var mesh = new Mesh();
                created.Add(mesh);
                smr.BakeMesh(mesh, true);
                var go = new GameObject(smr.name);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                mr.receiveShadows = smr.receiveShadows;
                smr.enabled = false;
            }
            var visible = parent.GetComponentsInChildren<Renderer>().Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToList();
            if (visible.Count == 0) return new Bounds(copy.transform.position + Vector3.up * 0.7f, Vector3.one * 1.4f);
            var b = visible[0].bounds;
            foreach (var r in visible.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        static void AimLights(Transform key, Transform fill, Transform cam)
        {
            var right = Vector3.Cross(Vector3.up, cam.forward).normalized;
            var down = Quaternion.AngleAxis(35f, right) * cam.forward;
            key.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(25f, Vector3.up) * down);
            fill.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-120f, Vector3.up) * down);
        }

        // Side views are easy to misread: say which way the avatar faces in the picture.
        static string Facing(string shot) =>
            shot == "left" ? "left (faces left in the picture)" :
            shot == "right" ? "right (faces right in the picture)" :
            shot == "threequarter" ? "threequarter (turned to the right in the picture)" : shot;

        static string Safe(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray());
        }

        // Camera placement in the avatar's own frame: forward is the way it faces.
        class Frame
        {
            readonly Vector3 root, forward, right, up, eye;
            readonly Bounds bounds;
            readonly float height;
            Vector3? focusPoint;
            float focusHalfHeight;

            public void Focus(Vector3 point, float frameHeight)
            {
                focusPoint = point;
                focusHalfHeight = frameHeight / 2f;
            }

            public Frame(GameObject avatar, Bounds visible)
            {
                var t = avatar.transform;
                root = t.position;
                forward = t.forward;
                right = t.right;
                up = t.up;
                bounds = visible;
                height = Mathf.Max(0.3f, Vector3.Dot(visible.max - root, up));
                var desc = avatar.GetComponent<VRCAvatarDescriptor>();
                eye = desc != null && desc.ViewPosition != Vector3.zero ? t.TransformPoint(desc.ViewPosition) : root + up * height * 0.93f;
            }

            public void Place(Camera cam, string shot, float aspect)
            {
                Vector3 dir, center;
                float halfHeight;
                switch (shot)
                {
                    case "back": dir = -forward; break;
                    case "left": dir = -right; break;
                    case "right": dir = right; break;
                    case "threequarter": dir = (forward + right).normalized; break;
                    default: dir = forward; break;
                }
                if (focusPoint.HasValue)
                {
                    center = focusPoint.Value;
                    halfHeight = focusHalfHeight;
                }
                else if (shot == "face")
                {
                    center = eye - up * (height * 0.02f);
                    halfHeight = height * 0.1f;
                }
                else if (shot == "upper")
                {
                    var top = Vector3.Dot(eye - root, up) + height * 0.12f;
                    var bottom = height * 0.45f;
                    center = root + up * ((top + bottom) / 2f);
                    halfHeight = (top - bottom) / 2f * 1.05f;
                }
                else
                {
                    var bottom = Mathf.Min(0f, Vector3.Dot(bounds.min - root, up));
                    center = root + up * ((height + bottom) / 2f);
                    halfHeight = (height - bottom) / 2f * 1.06f;
                    // Fit the width too (a T-pose is about as wide as it is tall).
                    var halfWidth = HalfWidth(Vector3.Cross(up, -dir).normalized, center) * 1.06f;
                    halfHeight = Mathf.Max(halfHeight, halfWidth / aspect);
                }
                var tan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.aspect = aspect;
                cam.transform.position = center + dir * (halfHeight / tan);
                cam.transform.rotation = Quaternion.LookRotation(-dir, up);
            }

            float HalfWidth(Vector3 axis, Vector3 center)
            {
                float max = 0f;
                var min = bounds.min;
                var size = bounds.size;
                for (int i = 0; i < 8; i++)
                {
                    var corner = min + Vector3.Scale(size, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1));
                    max = Mathf.Max(max, Mathf.Abs(Vector3.Dot(corner - center, axis)));
                }
                return max;
            }
        }

        [MenuItem("Tools/Avatar Agent Kit/Render Avatar", false, 120)]
        static void RenderMenu()
        {
            var name = Targets.SelectedRootName();
            if (name != null && !Targets.IsBakedName(name) && Targets.RootsNamed(name + Targets.BakeSuffix).Count == 1) name += Targets.BakeSuffix;
            Debug.Log(Run(name));
        }
    }
}
