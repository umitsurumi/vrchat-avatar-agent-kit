# Renders

Render a baked clone ([BAKE.md](BAKE.md)), then **look at the image** before saying anything about looks. `RenderAvatar` copies each target into a preview scene, so the user's scene is never touched, and bakes the skinned meshes, so the picture shows the current pose and blendshapes:

```csharp
return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar_bakecheck");                                   // front, back, face
return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar_bakecheck", "threequarter,left,upper", 400, 560); // width, height
return AvatarAgentKit.Editor.RenderAvatar.Run("MyAvatar_bakecheck", "front,left,back", focus: "Hips", size: 0.6f);
```

The result gives the path of one sheet (a row per target, a column per shot) and of each single shot. Open the sheet with the image viewer. Shots, options and defaults are in the header of `RenderAvatar.cs`.

- **Never use the scene avatar as "before".** Its outfits aren't merged and nothing is optimized; the tool notes it. NDMF preview in the Scene view may look processed, but these renders are not.
- **Before and after one change:** render with `label: "before"`, make the change, `Bake.Run` again, render the same shots with `label: "after"`, then tile the two sheets and look at that:

  ```sh
  uv run Tools/avatar-kit/texture-kit.py sheet <before-sheet.png> <after-sheet.png> --columns 1 --out <scratch>/compare.png
  ```

- **Two avatars side by side:** one call, the targets comma-separated; each gets a row.
- **A close-up of one part** (a hem, an armpit, the eyes): `focus` takes a humanoid bone or a path under the root, `size` the height of the frame in metres. The shot names then give only the direction.
- **Arms:** a baked clone is rendered with the arms down, as in game. In play mode the current pose is kept, for pose tests ([TEST.md](TEST.md)).
- **An angle the shots don't cover** (from below, a free camera): write it in `execute_code`, on the same pattern: a copy in a preview scene, `BakeMesh`, a camera with `cam.scene` set to the preview scene. Edit-mode changes show a frame late (GOTCHAS).

## Thumbnails

A new avatar's upload needs a 4:3 thumbnail, e.g. 1200 × 900: `RenderAvatar.Run(clone, "threequarter", 1200, 900)` and use the single shot. Let the user pick the angle.
