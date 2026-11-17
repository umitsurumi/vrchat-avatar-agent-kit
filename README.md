# vrchat-avatar-agent-kit

[中文](README.zh-CN.md)

A starter kit for modding VRChat avatars together with an AI coding agent (Claude Code, Codex, …). It drops into a Unity avatar project made with [ALCOM](https://vrc-get.anatawa12.com/alcom/) and gives the agent:

- **Rules** (`AGENTS.md`): check the environment first, never touch vendor files, commit one undoable step at a time, ask before uploading.
- **Skills** (`.claude/skills/`): how to bake and inspect the avatar the way VRChat receives it, put on outfits, change textures, build expressions, reach PC Good, and research items on Booth.
- **Tools**: an environment check, a texture kit that always rebuilds a texture from its original, and a few Unity editor helpers.

Git records every step, so anything the agent does can be undone.

The kit is **agent-first**: you describe what you want, the agent does the work in the open Unity Editor through [MCP for Unity](https://github.com/CoplayDev/unity-mcp) and shows you renders. Every tool also runs from the command line with plain arguments, so you can call it yourself.

## Quick start

1. Install the [prerequisites](#prerequisites).
2. Create an avatar project in ALCOM, import your base model, and install the [packages](#vpm-packages) it needs.
3. Install [MCP for Unity](#mcp-for-unity) in that project.
4. Open your agent in the project folder and tell it:

   > Install vrchat-avatar-agent-kit into this project by following `INSTALL.md` in https://github.com/umitsurumi/vrchat-avatar-agent-kit (or a local clone of it).

   Or run it yourself: `uv run install.py <path to your project>`. The installer never overwrites a file that already exists; it lists them instead.
5. Start working. In the first session the agent runs the environment check, fills in `PROJECT.md` with you, and makes the first commit.

## Prerequisites

Each check command should print a version number.

| Software | For | Check |
| --- | --- | --- |
| [Git](https://git-scm.com/) | recording every step; Unity also needs it for git-URL packages | `git --version` |
| [Git LFS](https://git-lfs.com/) | storing models and textures in Git | `git lfs version` |
| [uv](https://docs.astral.sh/uv/) | running the kit's Python tools and MCP for Unity; it fetches Python and the libraries itself | `uv --version` |
| [ALCOM](https://vrc-get.anatawa12.com/alcom/) | creating VRChat projects, installing VPM packages | it opens |
| Unity 2022.3 | the version VRChat requires; install it when ALCOM asks | ALCOM opens the project |
| An agent | Claude Code, Codex or another agent that can use MCP | |

Installing them:

- **Windows**: [Git for Windows](https://git-scm.com/download/win) (includes Git LFS) or `winget install --id Git.Git -e`; uv with `winget install --id=astral-sh.uv -e`.
- **Linux**: Git and Git LFS from your package manager (`sudo apt install git git-lfs`, `sudo pacman -S git git-lfs`, `sudo dnf install git git-lfs`); uv with `curl -LsSf https://astral.sh/uv/install.sh | sh`.
- **macOS** (best effort, not tested): `brew install git git-lfs uv`.

## VPM packages

Install what your base model and outfits ask for first. These are the ones the skills use. Add the repository in ALCOM (Settings → Packages → Add Repository), then install the package in the project.

| Package | For | VPM repository |
| --- | --- | --- |
| VRChat SDK - Avatars | required; ALCOM adds it to new avatar projects | official |
| Modular Avatar (`nadena.dev.modular-avatar`) | outfits, menus, toggles; brings NDMF with it | `https://vpm.nadena.dev/vpm.json` |
| AAO: Avatar Optimizer (`com.anatawa12.avatar-optimizer`) | optimizing at build time | `https://vpm.anatawa12.com/vpm.json` |
| lilToon (`jp.lilxyzw.liltoon`) | the shader most Booth models use | `https://lilxyzw.github.io/vpm-repos/vpm.json` |
| Meshia Mesh Simplification (`com.ramtype0.meshia.mesh-simplification`) | polygon reduction, for PC Good | `https://RamType0.github.io/VpmRepository/index.json` |
| FaceEmo (`jp.suzuryg.face-emo`) | expressions, optional | `https://suzuryg.github.io/vpm-repos/vpm.json` |
| Linux VRChat SDK Patch (`befuddledlabs.linuxvrchatsdkpatch-avatars`) | Linux only: local Build & Test | listing at `https://befuddledlabs.github.io/LinuxVRChatSDKPatch` |

## MCP for Unity

The agent drives the open Unity Editor through [MCP for Unity](https://github.com/CoplayDev/unity-mcp).

1. Unity → Window → Package Manager → `+` → Add package from git URL:
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`
   To pin a version, replace `#main` with a tag from the [releases page](https://github.com/CoplayDev/unity-mcp/releases), e.g. `#v10.3.0`.
2. Unity → Window → MCP for Unity → Configure All Detected Clients.
3. Restart the agent completely so it picks up the new MCP configuration.

This changes `Packages/manifest.json`; commit it on its own (e.g. `chore: install MCP for Unity`). With several Unity projects open, the agent may connect to the wrong one; `AGENTS.md` tells it to check first.

## What gets installed

| Path | What |
| --- | --- |
| `AGENTS.md`, `CLAUDE.md` | the agent's rules; `CLAUDE.md` is one line, `@AGENTS.md`, so Claude Code reads the same rules |
| `PROJECT.md` | the project's facts (base model, avatars, blueprint IDs, direction); filled in with you in the first session |
| `.claude/skills/`, `.agents/skills` | the skills; `.agents/skills` links to `.claude/skills` for Codex |
| `Tools/avatar-kit/` | `doctor.py` (environment check) and the texture kit |
| `Assets/AvatarAgentKit/Editor/` | Unity menu `Tools/Avatar Agent Kit` |
| `Assets/_USER/` | your own modified assets; vendor files stay untouched |
| `TextureSources/` | texture recipes and their inputs (outside `Assets/`, so Unity doesn't import them) |
| `.gitignore`, `.gitattributes` | Unity and ALCOM ignores; models and textures go to LFS |
| `.avatar-kit-version` | which kit version the project was set up with |

The kit has two tiers:

- **Tools** (`doctor`, `install.py`, the texture kit, the Unity menu items) have a stable command-line interface, documentation and tests. You pass arguments; you don't edit their code.
- **References** (scripts that come with a skill, e.g. the PC Good scripts) are worked examples, written lightly generic: model-specific values sit in a config block at the top, and the header says what they were verified on and where they may not fit. Let your agent read and adapt them to your avatar. They promise no stable interface.

## Skills

| Skill | For |
| --- | --- |
| `vrchat-avatar-unity` | the base: working in the open Unity Editor through MCP for Unity: bake and inspect, render, test, menus and toggles, upload |
| `vrchat-outfits` | putting on an outfit with Modular Avatar and cleaning up after it |
| `vrchat-textures` | texture recipes, makeup, recolouring, redrawing a texture with an image model, lilToon materials |
| `vrchat-expressions` | face shaping, FaceEmo expressions, MMD-world compatibility |
| `vrchat-pc-good` | taking an avatar to PC Good (a performance rank), with reference scripts |
| `booth-research` | researching items on Booth, looking at every image before recommending |

`booth-research` only reads public item pages and images, never downloads products, and waits a second between requests, in line with [BOOTH's scraping guideline](https://booth.pm/announcements/863) (2025-10-10).

## Tools

- **Environment check**: `uv run Tools/avatar-kit/doctor.py` prints a table of what is installed and how to install what is missing (`--json` for agents). It never installs anything itself.
- **Texture kit** (`uv run Tools/avatar-kit/texture-kit.py --help`): a texture is described by a recipe in `TextureSources/<texture>/recipe.json`: the original, the output, and the steps (UV masks, makeup PSDs, re-toning, recolouring, crop and place for image-model redraws). Every run starts from the untouched original, so an output is never edited again and artifacts don't pile up. Details: skill `vrchat-textures`.
- **Unity menu** `Tools/Avatar Agent Kit`: exports UV triangles for texture masks; sets Write Defaults ON on every state of the selected Animator Controller.

## Git in your avatar project

One commit records one change that can be undone on its own. Messages use `type: description`:

| Type | For |
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

Save the scene and assets in Unity first, then stage, check and commit:

```sh
git add .
git status
git diff --cached --stat         # what is staged
git diff --cached --name-status  # added, modified, deleted
git lfs ls-files                 # models and textures should be listed here
git commit -m "tweak: adjust the outfit material"
```

- Commit assets with their `.meta` files, together with project settings and package manifests.
- Unity caches and the packages ALCOM installs are ignored. When rolling back across a package upgrade, restore the matching package versions in ALCOM.
- Close Unity before restoring files from history and reopen it afterwards so it reimports.

## Not covered

- Quest / Android avatars.
- Outfits set up with VRCFury: the kit assumes Modular Avatar.
- Material settings for shaders other than lilToon (e.g. Poiyomi). The texture kit works with any shader, but the material notes in the skills cover lilToon only.

## Development

Tests live in `tests/` and are not installed into projects:

```sh
uv run --with-requirements tests/requirements.txt pytest tests
```

CI runs them on Ubuntu and Windows. The Unity-side C# is tested by hand in Unity; each script's header says what it was verified on.

## License

[MIT](LICENSE)
