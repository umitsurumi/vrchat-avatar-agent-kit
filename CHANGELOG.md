# Changelog

All notable changes to this kit. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Avatar tools in Unity (`Assets/AvatarAgentKit/Editor/Avatar/`, assembly `AvatarAgentKit.Editor.Avatar`, compiled only when NDMF and the VRChat Avatars SDK are installed). Static methods that return text, for one line of `execute_code`, and menu items under `Tools/Avatar Agent Kit`:
  - `Bake.Run` / `Bake.Cleanup`: the NDMF-baked clone `<avatar>_bakecheck`, with NDMF's errors in the result; cleanup restores the source and saves the scene only if it was clean before the bake.
  - `ReportPerformance.Run`: the PC rating of a baked clone, the metrics that set it, each metric with the limit of the next better rating (read from the SDK), and the biggest renderers and PhysBones.
  - `RenderAvatar.Run`: renders targets into a sheet in a preview scene, without touching the user's scene; preset shots, arms down on baked clones, close-ups of one part (`focus`), labels for before/after. Side shots say which way the avatar faces in the picture.
  - `ReportMenu.Run`: the menu tree, parameters and synced cost, and each FX layer with its Write Defaults and the parameters it tests; `find` traces a toggle from the menu to what it animates and marks bindings to objects missing from the clone.
  - `Pose.Run` / `Pose.Reset`: play-mode poses for motion tests (torso leans, leg poses), from the saved rest pose, arms down.
  - `CheckAvatar.Run`: mistakes that broke real avatars, each with the object and the fix, as errors, warnings and notes. Scene avatar: missing scripts, broken renderers and shaders, MA Object Toggles and Bone Proxies that can't work, PhysBones that move nothing (judged against the bake: AAO removes most, but keeps those with a parameter), PhysBones and colliders on hidden objects, unlocked VRC constraints, a blueprint ID that another git branch also has. Bake: menu controls without a parameter, too many synced bits, FX bindings to missing objects, mixed Write Defaults. The MA checks compile only with MA installed.
- Skill `vrchat-accessories`: accessories (pins, hair ties, glasses, brooches): cutting a bought pack down to what you keep, with its menu and synced bits; attaching with MA Bone Proxy; recolouring through the material; OFF toggles; generated accessories measured from the face. `REACTIONS.md`: an accessory that reacts to voice, mute, AFK and head pats with shape keys only, a face layer after the expression tool, and an in-game checklist. Reference scripts: `build_glasses.py` (glasses from face measurements) and `AccessoryReactions.cs` (the reaction FX, head-pat receiver and toggle).

### Changed

- `AGENTS.md`: a claim is settled by the cheapest check that can settle it (scene and assets, bake, play mode, the user in VRChat), and what only the game can show goes into the handoff as a checklist. New section Scope: fixes outside the ask that change the upload are named and queued, not made on the side; work without the user ends with its open questions under `needs input:`.
- `AGENTS.md` and UPLOAD.md: bake and run `CheckAvatar` before a commit that changes the avatar and before an upload. The three Modular Avatar gotchas it checks left GOTCHAS.md (MENUS.md keeps them as rules).
- `vrchat-avatar-unity`: BAKE.md, RENDER.md and TEST.md use the avatar tools instead of C# snippets to copy (bake, stats, menu map, renders, poses); MENUS.md, `vrchat-outfits`, `vrchat-pc-good` and MMD.md point at them.

## [0.1.0] - 2026-11-18

First public release: the private avatar-modding template becomes a public kit.

### Added

- `TextureSources/` for texture recipes and their inputs, outside `Assets/`.
- `Tools/avatar-kit/doctor.py`: environment check (Git, Git LFS and the project's LFS setup, uv, Unity 2022.3, VPM packages, MCP for Unity, Blender; the Linux VRChat SDK Patch on Linux). Table or `--json`; exits 1 when a required check fails; never installs anything.
- `install.py` and `INSTALL.md`: install the kit into a Unity project without overwriting anything (existing files are listed, `.gitignore` / `.gitattributes` rules are appended), link `.agents/skills`, record the version in `.avatar-kit-version`. `INSTALL.md` also covers upgrading and uninstalling.
- Tests (`tests/`) and CI on Ubuntu and Windows, Python 3.10 and 3.14.
- Texture kit, `uv run Tools/avatar-kit/texture-kit.py`: a recipe (`TextureSources/<texture>/recipe.json`) rebuilds a texture from its untouched original. Steps: `psd` (makeup PSDs composited like Photoshop: groups, pass-through, opacity, fill, layer masks; normal, multiply, screen, overlay, linear dodge), `retone` (rotate/scale colour offsets from a reference, e.g. blush over skin), `recolor` (OKLab lightness/chroma/hue, with colour-range selection), `place` (an image, e.g. from an image model, into a region). Masks from UV triangles, images, boxes, ellipses and their combinations. `crop` cuts a region for an image model and records it for `place`. `new`, `run` (with `--preview` and `--check`), `mask`, `apply`. Alpha never changes; the output can never be fed back in.
- `Assets/AvatarAgentKit/Editor/UvTriangleExport.cs`: exports UV triangles for texture masks, per submesh (menu) or by shape key (from code).
- `texture-kit sheet` tiles images (render shots, crops) into one labelled sheet; `place` can mirror an image (`"flip": "x"`).
- Skill `vrchat-outfits`: putting an outfit on with Modular Avatar, fitting the body, variant menus, physics, materials, and the cleanup checklist (moved here from `vrchat-avatar-unity`).
- Skill `vrchat-textures`: driving the texture kit, masks from the UV export, hooking the output up in lilToon materials; `REDRAW.md` for redrawing part of a texture with an image model.
- `vrchat-avatar-unity/MENUS.md`: MA menus, toggles, parameters, Write Defaults and layer order, with the rules real toggles broke on.

### Changed (from the private template)

- The template becomes a public kit, `vrchat-avatar-agent-kit`, under the MIT license.
- `AGENTS.md` and `PROJECT.md` are in English; agents still talk to the user in the user's language. `AGENTS.md` no longer points at the README (which is not installed into projects): it carries the commit types itself.
- README in English, with a Chinese translation in `README.zh-CN.md`.
- The Write Defaults tool moved to `Assets/AvatarAgentKit/Editor/` (namespace and asmdef `AvatarAgentKit.Editor`); its menu is now `Tools/Avatar Agent Kit/Set All Write Defaults ON`.
- No skill needs ImageMagick any more: render sheets use `texture-kit sheet`, and `booth.py` makes its sheets with Pillow (installed by `uv run`).
- `booth.py` waits a second between requests (was 0.4 s) and says what it fetches, in line with BOOTH's 2025-10-10 scraping guideline.
- Shell snippets note POSIX shells (Git Bash on Windows), and uploads can be polled from PowerShell too.
- Texture makeup moved from `vrchat-expressions` to `vrchat-textures`; `vrchat-expressions` keeps what makeup may touch.
- Skill descriptions name the everyday words people use (换衣服, 绿模, 改色, Very Poor, …) so they trigger without the jargon.

[Unreleased]: https://github.com/umitsurumi/vrchat-avatar-agent-kit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/umitsurumi/vrchat-avatar-agent-kit/releases/tag/v0.1.0
