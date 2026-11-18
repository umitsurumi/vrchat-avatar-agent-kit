# AGENTS.md

This is a VRChat avatar modding project. ALCOM creates the project and manages its packages; Git records every step, so any change can be undone. These are the rules for agents working here. They come from [vrchat-avatar-agent-kit](https://github.com/umitsurumi/vrchat-avatar-agent-kit); the installed version is in `.avatar-kit-version`.

Talk to the user in their language, and write `PROJECT.md`, `HANDOFF-*.md` and commit messages in it too. These rules and the skills are in English; that does not change the language you use with the user.

## Before starting

1. **Read [PROJECT.md](PROJECT.md).** If it still holds only placeholders, fill it in with the user before any work: base model, scene, avatar root, direction and no-go areas. Look up what the project can tell you; ask for the rest.
2. **Check the environment.**
   - Run `uv --version`. If uv is missing, stop and give the user its install command: Windows `winget install --id=astral-sh.uv -e`, macOS and Linux `curl -LsSf https://astral.sh/uv/install.sh | sh`.
   - Run `uv run Tools/avatar-kit/doctor.py`. It checks Git, Git LFS, the Unity version and the packages, and says how to fix anything missing. Never install system software or edit the package manifests yourself: pass the instructions on to the user (packages are installed in ALCOM).
   - Doctor can't see Unity. Check that the MCP for Unity tools respond and that the open scene belongs to this project. If they are connected to another project, stop and tell the user; run nothing there.
   - If the project has no commits yet, ask the user, then: `git init`, `git lfs install --local`, `git add .`, review `git status`, and commit `checkpoint: <initial state, in the user's language>`.
3. **Read any `HANDOFF-*.md`** in progress.

## Assets

- Vendor assets (purchased models, outfits, hair, tools) are read-only. To change one, copy it into `Assets/_USER/`, change the copy, and point the avatar at the copy. Overrides on prefab instances in the scene are fine.
- Textures are always regenerated from the untouched original by a recipe, never edited on top of an earlier output: repeated edits pile up artifacts. Recipes and their inputs live in `TextureSources/<texture>/`, outputs go to `Assets/_USER/`. An image made with an image model is committed there as a fixed input, with the model, prompt and date in the recipe; never feed an output back into the model. See skill `vrchat-textures`.
- Generated meshes, masks and materials go in `Assets/_USER/`. When overwriting an existing asset, keep its GUID.
- Scripts that generate assets are kept in the repo, never only in a temp folder.

## Commits

- Commit only when the user says so. One commit does one thing that can be undone on its own. The message is `type: description`:

  | type | for |
  | --- | --- |
  | `import` | importing third-party assets, tools, models |
  | `asset` | generating, converting or changing actual assets |
  | `config` | avatar, MA, menu, parameter and animator setup |
  | `tweak` | looks, materials, positions, body shape |
  | `fix` | bugs, clipping, bones, animation problems |
  | `refactor` | reorganising without changing the result |
  | `chore` | Git, Unity project settings, package upgrades |
  | `checkpoint` | a milestone snapshot |
  | `docs` | documents |

- Commit assets together with their `.meta`. Before committing, read `git diff --cached --stat` and `git lfs ls-files`: models and textures must go through LFS.
- Don't commit files that tools or builds rewrite on their own unless the user wants them, e.g. `ProjectSettings/` entries flipped by a plugin, or `ProjectSettings/lilToonSetting.json` rewritten by lilToon at build time. Leave folders the user keeps untracked on purpose (reference images and such) alone.
- Ask before pushing to a remote.

## Branches and tags

- `main` is the base look. Another avatar (a whole new outfit, a different style) gets its own branch named after it, made from a tag, never from work in progress.
- First thing on a new branch: clear the blueprint ID on the root's `PipelineManager`, or the upload overwrites the original avatar.
- When the user accepts an uploaded version, make an annotated tag (e.g. `<branch>-v1`) whose message gives the avatar name, the blueprint ID and the key numbers (rank, triangles).
- Scripts and assets shared between branches are brought over with `git checkout <branch> -- <path>`. Scene files can't be cherry-picked (object IDs won't match): run the tools again on the target branch.
- Switching branches with Unity open: open an empty scene, switch, refresh, then open the scene again. To restore files from history, close Unity first and reopen it afterwards so it reimports.

## Unity

- The user may be editing the same scene. Before saving, check `scene.isDirty`; if there are changes you didn't make, ask first.
- Judge a change on the NDMF-baked copy, not the scene object. How: skill `vrchat-avatar-unity`.
- A look only counts once it has been rendered and seen. What can only be confirmed in game (how physics feels, how others see it) goes to the user to test in VRChat.

## Upload

- Before every upload ask: a new avatar or overwrite which one; the name; keep it private.

## Notes

- Facts that rarely change (model, avatars and blueprint IDs, outfit sources, direction and no-go areas) go in `PROJECT.md`; update it when they change.
- Each topic in progress gets a `HANDOFF-<topic>.md` at the repo root: the numbers, the user's decisions, the pitfalls hit. Update it after every step.

## Tools

- `Tools/avatar-kit/doctor.py`: environment check (`--json` for a machine-readable result).
- `uv run Tools/avatar-kit/texture-kit.py`: texture recipes (masks, makeup PSD, re-tone, recolor, crop and place for image-model redraws). Driven by skill `vrchat-textures`.
- `Assets/AvatarAgentKit/Editor/Avatar/`: the avatar tools, called from `execute_code`: `AvatarAgentKit.Editor.Bake`, `ReportPerformance`, `RenderAvatar`. How and when: skill `vrchat-avatar-unity`.
- `Assets/AvatarAgentKit/Editor/`: Unity menu `Tools/Avatar Agent Kit` (the avatar tools, UV triangle export for texture masks, Set All Write Defaults ON).

## Skills

Skills live in `.claude/skills/` (`.agents/skills` links to it for Codex):

- `vrchat-avatar-unity`: the base for avatar work in Unity: bake and inspect, render, test, menus and toggles, upload.
- `vrchat-outfits`: putting on an outfit with Modular Avatar and cleaning up after it.
- `vrchat-textures`: texture recipes, makeup, recolouring, redrawing with an image model, lilToon materials.
- `vrchat-expressions`: face shaping, FaceEmo expressions, MMD-world compatibility.
- `vrchat-pc-good`: taking an avatar to PC Good (a performance rank).
- `booth-research`: researching items on Booth; look at every image before recommending.

Agents without skill support: when a task matches a skill's `description`, read its `SKILL.md` and follow it.
