"""Shared helpers. Run the tests with: uv run --with pytest pytest tests"""

from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]


def load(name: str, rel: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / rel)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


doctor = load("doctor", "Tools/avatar-kit/doctor.py")
installer = load("installer", "install.py")

HAS_GIT_LFS = shutil.which("git") is not None and subprocess.run(
    ["git", "lfs", "version"], capture_output=True).returncode == 0
needs_git_lfs = pytest.mark.skipif(not HAS_GIT_LFS, reason="git and git-lfs are needed")


def make_unity_project(path: Path, unity: str = "2022.3.22f1", vpm: dict[str, str] | None = None,
                       upm: dict[str, str] | None = None, resolved: bool = True) -> Path:
    """A folder that looks like an ALCOM avatar project. vpm/upm map package id -> version."""
    vpm = {"com.vrchat.avatars": "3.10.5", "nadena.dev.modular-avatar": "1.18.7"} if vpm is None else vpm
    upm = {"com.coplaydev.unity-mcp": "https://example.invalid/unity-mcp.git"} if upm is None else upm
    (path / "Assets").mkdir(parents=True, exist_ok=True)
    (path / "ProjectSettings").mkdir(exist_ok=True)
    (path / "ProjectSettings" / "ProjectVersion.txt").write_text(f"m_EditorVersion: {unity}\n", encoding="utf-8")
    packages = path / "Packages"
    packages.mkdir(exist_ok=True)
    locked = dict(vpm)
    if "nadena.dev.modular-avatar" in vpm:
        locked["nadena.dev.ndmf"] = "1.14.8"
    (packages / "vpm-manifest.json").write_text(json.dumps({
        "dependencies": {k: {"version": v} for k, v in vpm.items()},
        "locked": {k: {"version": v} for k, v in locked.items()},
    }), encoding="utf-8")
    (packages / "manifest.json").write_text(json.dumps({"dependencies": upm}), encoding="utf-8")
    if resolved:
        for package_id, version in locked.items():
            (packages / package_id).mkdir(exist_ok=True)
            (packages / package_id / "package.json").write_text(
                json.dumps({"name": package_id, "version": version}), encoding="utf-8")
    return path


def git(path: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=path, check=True, capture_output=True)
