#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
"""Environment check for a VRChat avatar project set up with vrchat-avatar-agent-kit.

    uv run Tools/avatar-kit/doctor.py [--project PATH] [--json]

Checks what the kit and its skills need: Git and Git LFS (and this project's LFS setup), uv,
the Unity version, the VPM packages, MCP for Unity, Blender. Prints a table, or JSON with
--json. Exits 0 when every required check passes, 1 otherwise. It never installs anything:
each failed check says how to fix it.

It can't see Unity itself. Whether MCP for Unity is connected, and to this project, is for the
agent to check (AGENTS.md, "Before starting").

The project defaults to the folder this script is installed in (two levels up).
"""

from __future__ import annotations

import argparse
import glob
import json
import re
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

UNITY_SERIES = "2022.3"
MCP_PACKAGE = "com.coplaydev.unity-mcp"
MCP_GIT_URL = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"


@dataclass(frozen=True)
class Package:
    id: str
    name: str
    level: str  # "required" | "optional"
    repo: str | None  # VPM repository to add in ALCOM; None = official
    used_for: str
    os: str | None = None  # only checked on this OS


PACKAGES = [
    Package("com.vrchat.avatars", "VRChat SDK - Avatars", "required", None,
            "everything; ALCOM adds it to new avatar projects"),
    Package("nadena.dev.modular-avatar", "Modular Avatar", "required", "https://vpm.nadena.dev/vpm.json",
            "outfits, menus, toggles"),
    Package("nadena.dev.ndmf", "NDMF", "required", "https://vpm.nadena.dev/vpm.json",
            "the build pipeline; comes with Modular Avatar"),
    Package("com.anatawa12.avatar-optimizer", "AAO: Avatar Optimizer", "optional", "https://vpm.anatawa12.com/vpm.json",
            "optimizing at build time (skill vrchat-pc-good)"),
    Package("jp.lilxyzw.liltoon", "lilToon", "optional", "https://lilxyzw.github.io/vpm-repos/vpm.json",
            "the shader most Booth models use (material notes in the skills)"),
    Package("com.ramtype0.meshia.mesh-simplification", "Meshia Mesh Simplification", "optional",
            "https://RamType0.github.io/VpmRepository/index.json", "polygon reduction (skill vrchat-pc-good)"),
    Package("jp.suzuryg.face-emo", "FaceEmo", "optional", "https://suzuryg.github.io/vpm-repos/vpm.json",
            "expressions (skill vrchat-expressions)"),
    Package("befuddledlabs.linuxvrchatsdkpatch-avatars", "Linux VRChat SDK Patch (Avatars)", "optional",
            "https://befuddledlabs.github.io/LinuxVRChatSDKPatch", "local Build & Test on Linux", os="linux"),
]

TOOL_FIX = {
    "git": {
        "windows": "Install Git for Windows (https://git-scm.com/download/win), or: winget install --id Git.Git -e",
        "linux": "Install git with your package manager, e.g. sudo apt install git",
        "macos": "brew install git",
    },
    "git-lfs": {
        "windows": "Git for Windows includes Git LFS; reinstall it with LFS ticked, or: winget install --id GitHub.GitLFS -e",
        "linux": "Install git-lfs with your package manager, e.g. sudo apt install git-lfs",
        "macos": "brew install git-lfs",
    },
    "uv": {
        "windows": "winget install --id=astral-sh.uv -e",
        "linux": "curl -LsSf https://astral.sh/uv/install.sh | sh",
        "macos": "brew install uv",
    },
    "blender": {
        "windows": "Install Blender from https://www.blender.org/download/",
        "linux": "Install Blender from https://www.blender.org/download/ or your package manager, and put `blender` on PATH",
        "macos": "Install Blender from https://www.blender.org/download/",
    },
}


@dataclass
class Check:
    id: str
    name: str
    level: str  # "required" | "optional"
    status: str  # "ok" | "missing" | "warn"
    detail: str = ""
    fix: str = ""


def current_os() -> str:
    if sys.platform.startswith("win"):
        return "windows"
    if sys.platform == "darwin":
        return "macos"
    return "linux"


def run(cmd: list[str], cwd: Path | None = None) -> tuple[int, str]:
    try:
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=30)
    except (OSError, subprocess.TimeoutExpired) as e:
        return -1, str(e)
    return p.returncode, (p.stdout.strip() or p.stderr.strip())


def first_line(text: str) -> str:
    return text.splitlines()[0] if text else ""


# ---- tools on PATH ----

def check_tool(id: str, name: str, cmd: list[str], level: str, os_name: str, used_for: str = "") -> Check:
    if shutil.which(cmd[0]) is None:
        return Check(id, name, level, "missing", used_for or "not on PATH", TOOL_FIX[id][os_name])
    code, out = run(cmd)
    if code != 0:
        return Check(id, name, level, "missing", f"`{' '.join(cmd)}` failed: {first_line(out)}", TOOL_FIX[id][os_name])
    return Check(id, name, level, "ok", first_line(out))


def find_blender(os_name: str) -> str | None:
    found = shutil.which("blender")
    if found:
        return found
    candidates: list[str] = []
    if os_name == "windows":
        candidates = sorted(glob.glob("C:/Program Files/Blender Foundation/Blender */blender.exe"), reverse=True)
    elif os_name == "macos":
        candidates = glob.glob("/Applications/Blender.app/Contents/MacOS/Blender")
    return candidates[0] if candidates else None


def check_blender(os_name: str) -> Check:
    used_for = "building accessory meshes in Blender"
    path = find_blender(os_name)
    if path is None:
        return Check("blender", "Blender", "optional", "missing", used_for, TOOL_FIX["blender"][os_name])
    code, out = run([path, "--version"])
    detail = first_line(out) if code == 0 else path
    return Check("blender", "Blender", "optional", "ok", detail)


# ---- the project's Git setup ----

def check_git_repo(project: Path) -> Check:
    fix = "Ask the user first, then in the project folder: git init && git lfs install --local (AGENTS.md, \"Before starting\")"
    if shutil.which("git") is None:
        return Check("git-repo", "Project is a Git repository", "required", "missing", "git is not installed", fix)
    code, out = run(["git", "rev-parse", "--show-toplevel"], cwd=project)
    if code != 0:
        return Check("git-repo", "Project is a Git repository", "required", "missing", "not a Git repository", fix)
    top = Path(out).resolve()
    if top != project.resolve():
        return Check("git-repo", "Project is a Git repository", "required", "warn",
                     f"the project is inside the repository at {top}, not its root",
                     "Usually fine; the kit's .gitignore and .gitattributes rules assume the project folder is the repository root")
    return Check("git-repo", "Project is a Git repository", "required", "ok", str(top))


def hooks_dir(project: Path) -> Path | None:
    code, out = run(["git", "config", "--get", "core.hooksPath"], cwd=project)
    if code == 0 and out:
        p = Path(out).expanduser()
        return p if p.is_absolute() else project / p
    code, out = run(["git", "rev-parse", "--git-path", "hooks"], cwd=project)
    if code != 0:
        return None
    p = Path(out)
    return p if p.is_absolute() else project / p


def check_lfs_setup(project: Path) -> Check:
    name = "Git LFS set up in this project"
    fix = "In the project folder: git lfs install --local"
    if shutil.which("git") is None or run(["git", "rev-parse", "--git-dir"], cwd=project)[0] != 0:
        return Check("lfs-setup", name, "required", "missing", "no Git repository yet", fix)
    code, out = run(["git", "config", "--get", "filter.lfs.process"], cwd=project)
    if code != 0 or not out:
        return Check("lfs-setup", name, "required", "missing", "the LFS filter is not configured", fix)
    hooks = hooks_dir(project)
    pre_push = hooks / "pre-push" if hooks else None
    if pre_push is None or not pre_push.is_file():
        return Check("lfs-setup", name, "required", "missing", "no pre-push hook: LFS files would not be uploaded", fix)
    text = pre_push.read_text(encoding="utf-8", errors="replace")
    if "git lfs" not in text and "git-lfs" not in text:
        return Check("lfs-setup", name, "required", "missing",
                     f"{pre_push.name} hook exists but doesn't call Git LFS",
                     "Add `git lfs pre-push \"$@\"` to the existing pre-push hook, or run: git lfs install --local --force")
    return Check("lfs-setup", name, "required", "ok", "filter and pre-push hook present")


# ---- Unity ----

def read_unity_version(project: Path) -> str | None:
    f = project / "ProjectSettings" / "ProjectVersion.txt"
    if not f.is_file():
        return None
    m = re.search(r"^m_EditorVersion:\s*(\S+)", f.read_text(encoding="utf-8", errors="replace"), re.M)
    return m.group(1) if m else None


def check_unity(project: Path) -> Check:
    name = f"Unity {UNITY_SERIES}"
    version = read_unity_version(project)
    if version is None:
        return Check("unity", name, "required", "missing",
                     f"{project} is not a Unity project (no ProjectSettings/ProjectVersion.txt)",
                     "Run doctor from inside the project, or pass --project <Unity project folder>")
    if not version.startswith(UNITY_SERIES + "."):
        return Check("unity", name, "required", "missing", f"the project uses Unity {version}",
                     f"VRChat requires Unity {UNITY_SERIES}; migrate the project to it in ALCOM")
    return Check("unity", name, "required", "ok", version)


def load_json(path: Path) -> dict | None:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None


def check_package(project: Path, pkg: Package, vpm: dict | None) -> Check:
    if vpm is None:
        return Check(pkg.id, pkg.name, pkg.level, "missing", "Packages/vpm-manifest.json not found or unreadable",
                     "Create the project in ALCOM as an Avatars project")
    listed = pkg.id in (vpm.get("dependencies") or {}) or pkg.id in (vpm.get("locked") or {})
    if pkg.repo is None:
        fix = f"Add \"{pkg.name}\" to the project in ALCOM"
    else:
        fix = f"In ALCOM: Settings > Packages > Add Repository {pkg.repo}, then add \"{pkg.name}\" to the project"
    if not listed:
        if pkg.id == "jp.lilxyzw.liltoon" and (project / "Assets" / "lilToon").is_dir():
            return Check(pkg.id, pkg.name, pkg.level, "ok", "installed as a unitypackage in Assets/lilToon")
        return Check(pkg.id, pkg.name, pkg.level, "missing", f"for {pkg.used_for}", fix)
    if not (project / "Packages" / pkg.id / "package.json").is_file():
        return Check(pkg.id, pkg.name, pkg.level, "warn", "listed in vpm-manifest.json but not in Packages/",
                     "Open the project in ALCOM (or Unity) so it restores the package")
    info = load_json(project / "Packages" / pkg.id / "package.json") or {}
    return Check(pkg.id, pkg.name, pkg.level, "ok", str(info.get("version", "installed")))


def check_mcp(project: Path) -> Check:
    name = "MCP for Unity"
    fix = f"Unity > Window > Package Manager > + > Add package from git URL: {MCP_GIT_URL}"
    manifest = load_json(project / "Packages" / "manifest.json")
    if manifest is None:
        return Check("mcp-for-unity", name, "required", "missing", "Packages/manifest.json not found or unreadable", fix)
    source = (manifest.get("dependencies") or {}).get(MCP_PACKAGE)
    if source is None:
        return Check("mcp-for-unity", name, "required", "missing", "lets the agent work in the open Unity Editor", fix)
    return Check("mcp-for-unity", name, "required", "ok", str(source))


# ---- all together ----

def run_checks(project: Path, os_name: str) -> list[Check]:
    checks = [
        check_tool("git", "Git", ["git", "--version"], "required", os_name),
        check_tool("git-lfs", "Git LFS", ["git", "lfs", "version"], "required", os_name),
        check_tool("uv", "uv", ["uv", "--version"], "required", os_name),
        check_git_repo(project),
        check_lfs_setup(project),
        check_unity(project),
    ]
    if read_unity_version(project) is not None:  # package checks mean nothing outside a Unity project
        vpm = load_json(project / "Packages" / "vpm-manifest.json")
        checks += [check_package(project, p, vpm) for p in PACKAGES if p.os in (None, os_name)]
        checks.append(check_mcp(project))
    checks.append(check_blender(os_name))
    return checks


def kit_version(project: Path) -> str | None:
    f = project / ".avatar-kit-version"
    return f.read_text(encoding="utf-8").strip() if f.is_file() else None


def failed(checks: list[Check]) -> list[Check]:
    return [c for c in checks if c.level == "required" and c.status == "missing"]


def label(c: Check) -> str:
    if c.status == "ok":
        return "ok"
    if c.status == "warn":
        return "WARN"
    return "MISSING" if c.level == "required" else "absent"


def print_table(project: Path, os_name: str, checks: list[Check]) -> None:
    version = kit_version(project)
    print(f"vrchat-avatar-agent-kit doctor  (kit {version or 'not installed'}, {os_name})")
    print(f"project: {project}\n")
    width = max(len(c.name) for c in checks)
    for c in checks:
        print(f"  {label(c):<8} {c.name:<{width}}  {c.detail}")
    bad = failed(checks)
    warns = [c for c in checks if c.status == "warn"]
    absent = [c for c in checks if c.level == "optional" and c.status == "missing"]
    if bad:
        print(f"\n{len(bad)} required check(s) failed. To fix:")
        for c in bad:
            print(f"  - {c.name}: {c.fix}")
    if warns:
        print("\nWarnings:")
        for c in warns:
            print(f"  - {c.name}: {c.detail}. {c.fix}")
    if absent:
        print("\nOptional, not found (install when a task needs it):")
        for c in absent:
            print(f"  - {c.name}, {c.detail}: {c.fix}")
    print("\nNot checked here: whether MCP for Unity is connected to this project. The agent checks that itself.")
    if not bad:
        print("\nAll required checks passed.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Check the environment for a vrchat-avatar-agent-kit project.")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2],
                        help="Unity project folder (default: the project this script is installed in)")
    parser.add_argument("--json", action="store_true", help="print JSON for agents")
    args = parser.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")

    project = args.project.resolve()
    os_name = current_os()
    checks = run_checks(project, os_name)
    ok = not failed(checks)
    if args.json:
        print(json.dumps({"kit_version": kit_version(project), "project": str(project), "os": os_name,
                          "ok": ok, "checks": [asdict(c) for c in checks]}, indent=2))
    else:
        print_table(project, os_name, checks)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
