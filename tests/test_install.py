from __future__ import annotations

import json
import os
import subprocess
import sys

from conftest import ROOT, installer, make_unity_project

INSTALL = ROOT / "install.py"


def run_install(project, *args):
    return subprocess.run([sys.executable, str(INSTALL), str(project), *args],
                          capture_output=True, text=True, encoding="utf-8")


def test_fresh_install(tmp_path):
    project = make_unity_project(tmp_path)
    report = installer.install(project, dry_run=False)
    for rel in ["AGENTS.md", "CLAUDE.md", "PROJECT.md", ".gitignore", ".gitattributes",
                "Tools/avatar-kit/doctor.py", "Assets/AvatarAgentKit/Editor/SetAllWriteDefaultsOn.cs",
                ".claude/skills/vrchat-avatar-unity/SKILL.md", "Assets/_USER/.gitkeep", "TextureSources/.gitkeep"]:
        assert (project / rel).is_file(), rel
    assert (project / ".avatar-kit-version").read_text(encoding="utf-8") == \
        (ROOT / ".avatar-kit-version").read_text(encoding="utf-8")
    assert report["skipped"] == []
    # The kit's own files that don't belong in a project stay out.
    for rel in ["install.py", "INSTALL.md", "README.md", "CHANGELOG.md", "LICENSE", "tests", "docs", ".github"]:
        assert not (project / rel).exists(), rel
    link = project / ".agents" / "skills"
    if report["link"] == "created":
        assert link.is_symlink() and (link / "vrchat-avatar-unity" / "SKILL.md").is_file()
    else:  # Windows without Developer Mode
        assert report["link"] == "failed" and any("Developer Mode" in n for n in report["notes"])


def test_existing_files_are_never_overwritten(tmp_path):
    project = make_unity_project(tmp_path)
    (project / "AGENTS.md").write_text("my rules\n", encoding="utf-8")
    report = installer.install(project, dry_run=False)
    assert (project / "AGENTS.md").read_text(encoding="utf-8") == "my rules\n"
    assert report["skipped"] == ["AGENTS.md"]
    assert any("merge" in n for n in report["notes"])


def test_second_run_changes_nothing(tmp_path):
    project = make_unity_project(tmp_path)
    installer.install(project, dry_run=False)
    report = installer.install(project, dry_run=False)
    assert report["copied"] == [] and report["skipped"] == [] and report["merged"] == {}
    assert report["link"] == "exists" or report["link"] == "failed"


def test_dry_run_writes_nothing(tmp_path):
    project = make_unity_project(tmp_path)
    before = sorted(p.relative_to(project) for p in project.rglob("*"))
    report = installer.install(project, dry_run=True)
    assert sorted(p.relative_to(project) for p in project.rglob("*")) == before
    assert "AGENTS.md" in report["copied"] and report["link"] == "would create"


def test_ignore_rules_are_merged_keeping_crlf(tmp_path):
    project = make_unity_project(tmp_path)
    (project / ".gitignore").write_bytes(b"# mine\r\n/[Ll]ibrary/\r\n*.log")  # no final newline
    report = installer.install(project, dry_run=False)
    added = report["merged"][".gitignore"]
    assert "/[Ll]ibrary/" not in added and "/Packages/*" in added
    data = (project / ".gitignore").read_bytes()
    assert data.startswith(b"# mine\r\n/[Ll]ibrary/\r\n*.log\r\n")
    assert b"\n" not in data.replace(b"\r\n", b"")
    assert data.count(installer.MERGE_HEADER.encode()) == 1
    assert installer.install(project, dry_run=False)["merged"] == {}


def test_version_mismatch_points_at_upgrading(tmp_path):
    project = make_unity_project(tmp_path)
    (project / ".avatar-kit-version").write_text("0.0.1\n", encoding="utf-8")
    report = installer.install(project, dry_run=False)
    assert (project / ".avatar-kit-version").read_text(encoding="utf-8") == "0.0.1\n"
    assert any("Upgrading" in n for n in report["notes"])


def test_caches_are_not_installed(tmp_path, monkeypatch):
    kit = tmp_path / "kit"
    (kit / "Tools" / "avatar-kit" / "__pycache__").mkdir(parents=True)
    (kit / "Tools" / "avatar-kit" / "__pycache__" / "doctor.cpython-312.pyc").write_bytes(b"x")
    (kit / "Tools" / "avatar-kit" / "doctor.py").write_text("", encoding="utf-8")
    (kit / "Tools" / "avatar-kit" / ".DS_Store").write_bytes(b"x")
    monkeypatch.setattr(installer, "KIT", kit)
    monkeypatch.setattr(installer, "INSTALL_PATHS", ["Tools/avatar-kit"])
    assert [p.as_posix() for p in installer.kit_files()] == ["Tools/avatar-kit/doctor.py"]


def test_refuses_a_folder_that_is_not_a_unity_project(tmp_path):
    p = run_install(tmp_path)
    assert p.returncode == 2 and "not a Unity project" in p.stderr
    assert not any(tmp_path.iterdir())


def test_cli_json(tmp_path):
    project = make_unity_project(tmp_path)
    p = run_install(project, "--json", "--dry-run")
    assert p.returncode == 0
    report = json.loads(p.stdout)
    assert report["dry_run"] is True and report["project"] == str(project.resolve())
    assert not (project / "AGENTS.md").exists()
    assert os.path.isabs(report["kit"])
