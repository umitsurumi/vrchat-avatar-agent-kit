# Renders

Use a temporary camera rendering into a RenderTexture, with several shots per call. Then tile the PNGs into one sheet and **look at it** with the image viewer before saying anything about looks. Render a baked clone, or two clones (feature on and off) for a before/after. Never use the scene avatar as "before": NDMF preview may already show it processed.

## Quick shots of a baked clone

PNGs go under `Temp/shots/`.

```csharp
string AVATAR = "MyAvatar";
GameObject c = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR + "_bakecheck" && g.scene.IsValid()) c = g;
System.IO.Directory.CreateDirectory("Temp/shots");
var camGo = new GameObject("tmpcam"); var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.01f;
var rt = new RenderTexture(700, 700, 24); rt.antiAliasing = 4; cam.targetTexture = rt; var tex = new Texture2D(700, 700, TextureFormat.RGB24, false);
System.Action<string, Vector3, Vector3, float> shot = (name, pos, tgt, fov) => {
  cam.transform.position = pos; cam.transform.LookAt(tgt); cam.fieldOfView = fov;
  cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 700, 700), 0, 0); tex.Apply(); RenderTexture.active = null;
  System.IO.File.WriteAllBytes("Temp/shots/" + name + ".png", tex.EncodeToPNG()); };
var p = c.transform.position;
shot("full", p + new Vector3(0f, 0.75f, 2.1f), p + new Vector3(0f, 0.68f, 0f), 34);
shot("back", p + new Vector3(0f, 0.75f, -2.1f), p + new Vector3(0f, 0.68f, 0f), 34);
shot("face", p + new Vector3(0.05f, 1.17f, 0.42f), p + new Vector3(0f, 1.15f, 0f), 30);
cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(tex);
return "ok";
```

The camera positions suit an avatar about 1.4 m tall facing +Z; scale them for others.

Tile the shots and view the sheet. Keep sheets in a scratch folder, not under `Assets`:

```sh
magick montage Temp/shots/{full,back,face}.png -tile 3x1 -geometry +4+4 -background '#d0d4dc' <scratch>/sheet.png
```

**Timing:** a blendshape or state change made in edit mode shows only a frame later. Change it in one call and render in the next, or `BakeMesh` into a temporary MeshRenderer and render that.

## Without touching the user's scene

Use this while the user is editing. The clones go into a preview scene, and the camera renders only that scene (`cam.scene = ps`). Variants stand side by side, 3 m apart:

```csharp
string AVATAR = "MyAvatar";
var root = Resources.FindObjectsOfTypeAll<GameObject>().First(x => x.name == AVATAR && x.scene.IsValid() && x.transform.parent == null);
var ps = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
AppDomain.CurrentDomain.SetData("preview_scene", ps.handle);
var lightGo = new GameObject("ZZ_light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, ps);
var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f; lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
for (int i = 0; i < 2; i++) {
  var c = UnityEngine.Object.Instantiate(root);
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(c, ps);
  c.name = "ZZ_variant" + i; c.transform.position = new Vector3(3 * i, 0, 0);
  foreach (var pb in c.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>(true)) pb.enabled = false;
  var an = c.GetComponent<Animator>();   // arms down, as in game
  an.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation *= Quaternion.Euler(0, 0, 70);
  an.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation *= Quaternion.Euler(0, 0, -70);
  // variant 1: apply the change being judged here
}
return "preview scene ready";
```

These clones are not baked. For processed results, bake one in the scene (BAKE.md) and move it into the preview scene.

Render in the next call. The camera lives in the preview scene, and the images are tiled into one sheet:

```csharp
var handle = (int)AppDomain.CurrentDomain.GetData("preview_scene");
UnityEngine.SceneManagement.Scene ps = default(UnityEngine.SceneManagement.Scene);
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == "ZZ_variant0" && g.scene.handle == handle) ps = g.scene;
int N = 2, W = 420, H = 520;
var outTex = new Texture2D(W * N, H * 2, TextureFormat.RGB24, false);
var camGo = new GameObject("ZZ_cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, ps);
var cam = camGo.AddComponent<Camera>(); cam.scene = ps; cam.fieldOfView = 30; cam.nearClipPlane = 0.01f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.82f, 0.84f, 0.88f);
var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
for (int i = 0; i < N; i++) {
  for (int v = 0; v < 2; v++) {   // front, three-quarter
    var target = new Vector3(3f * i, 0.86f, 0);
    var dir = v == 0 ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0.35f).normalized;
    cam.transform.position = target + dir * 1.3f; cam.transform.LookAt(target);
    cam.Render();
    RenderTexture.active = rt; var t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
    outTex.SetPixels(i * W, (1 - v) * H, W, H, t.GetPixels()); UnityEngine.Object.DestroyImmediate(t);
  }
}
RenderTexture.active = null; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
var path = "/abs/scratch/variants.png";   // outside Assets
System.IO.File.WriteAllBytes(path, outTex.EncodeToPNG());
return path;
```

Close the preview scene at the end. The leftover count should be 0:

```csharp
var handle = (int)AppDomain.CurrentDomain.GetData("preview_scene");
UnityEngine.SceneManagement.Scene ps = default(UnityEngine.SceneManagement.Scene);
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == "ZZ_variant0" && g.scene.handle == handle) ps = g.scene;
var closed = ps.IsValid() && UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(ps);
return "closed=" + closed + " leftover=" + Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name.StartsWith("ZZ_") && g.scene.IsValid());
```

## Thumbnails

A new avatar's upload needs a thumbnail. VRChat uses 4:3, e.g. 1200 × 900. Render one with either camera, then save it as a PNG outside `Assets`.
