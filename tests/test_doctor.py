from __future__ import annotations

import json
import shutil
import subprocess
import sys

import pytest

from conftest import ROOT, doctor, git, make_unity_project, needs_git_lfs

DOCTOR = ROOT / "Tools" / "avatar-kit" / "doctor.py"


def by_id(checks):
    return {c.id: c for c in checks}


# ---- Unity version ----

def test_unity_2022_3_passes(tmp_path):
    c = doctor.check_unity(make_unity_project(tmp_path))
    assert (c.status, c.detail) == ("ok", "2022.3.22f1")


def test_other_unity_version_fails(tmp_path):
    c = doctor.check_unity(make_unity_project(tmp_path, unity="2021.3.4f1"))
    assert c.status == "missing" and "2021.3.4f1" in c.detail


def test_not_a_unity_project(tmp_path):
    c = doctor.check_unity(tmp_path)
    assert c.status == "missing" and "--project" in c.fix


def test_package_checks_are_skipped_outside_a_unity_project(tmp_path):
    ids = by_id(doctor.run_checks(tmp_path, "linux"))
    assert "unity" in ids
    assert "nadena.dev.modular-avatar" not in ids and "mcp-for-unity" not in ids


# ---- packages ----

def test_required_packages_present(tmp_path):
    checks = by_id(doctor.run_checks(make_unity_project(tmp_path), "linux"))
    assert checks["com.vrchat.avatars"].status == "ok"
    assert checks["nadena.dev.modular-avatar"].detail == "1.18.7"
    assert checks["nadena.dev.ndmf"].status == "ok"  # only in "locked", pulled in by MA


def test_missing_required_package_names_its_repository(tmp_path):
    project = make_unity_project(tmp_path, vpm={"com.vrchat.avatars": "3.10.5"})
    c = by_id(doctor.run_checks(project, "linux"))["nadena.dev.modular-avatar"]
    assert (c.level, c.status) == ("required", "missing")
    assert "https://vpm.nadena.dev/vpm.json" in c.fix


def test_listed_but_not_restored_is_a_warning(tmp_path):
    c = by_id(doctor.run_checks(make_unity_project(tmp_path, resolved=False), "linux"))["com.vrchat.avatars"]
    assert c.status == "warn" and "ALCOM" in c.fix


def test_optional_package_missing_is_absent_not_failed(tmp_path):
    checks = doctor.run_checks(make_unity_project(tmp_path), "linux")
    face_emo = by_id(checks)["jp.suzuryg.face-emo"]
    assert (face_emo.level, face_emo.status) == ("optional", "missing")
    assert face_emo not in doctor.failed(checks)


def test_liltoon_as_unitypackage_counts(tmp_path):
    project = make_unity_project(tmp_path)
    (project / "Assets" / "lilToon").mkdir()
    assert by_id(doctor.run_checks(project, "linux"))["jp.lilxyzw.liltoon"].status == "ok"


def test_linux_patch_is_only_checked_on_linux(tmp_path):
    project = make_unity_project(tmp_path)
    patch = "befuddledlabs.linuxvrchatsdkpatch-avatars"
    assert patch in by_id(doctor.run_checks(project, "linux"))
    assert patch not in by_id(doctor.run_checks(project, "windows"))


def test_mcp_for_unity(tmp_path):
    assert doctor.check_mcp(make_unity_project(tmp_path)).status == "ok"
    missing = doctor.check_mcp(make_unity_project(tmp_path / "b", upm={}))
    assert missing.status == "missing" and "unity-mcp" in missing.fix


# ---- the project's Git setup ----

@needs_git_lfs
def test_lfs_setup_needs_the_hook(tmp_path):
    project = make_unity_project(tmp_path)
    assert doctor.check_git_repo(project).status == "missing"
    git(project, "init")
    assert doctor.check_git_repo(project).status == "ok"
    assert doctor.check_lfs_setup(project).status == "missing"
    git(project, "lfs", "install", "--local")
    assert doctor.check_lfs_setup(project).status == "ok"


@needs_git_lfs
def test_pre_push_hook_without_lfs(tmp_path):
    project = make_unity_project(tmp_path)
    git(project, "init")
    git(project, "lfs", "install", "--local")
    hook = project / ".git" / "hooks" / "pre-push"
    hook.write_text("#!/bin/sh\necho hello\n", encoding="utf-8")
    c = doctor.check_lfs_setup(project)
    assert c.status == "missing" and "pre-push" in c.detail


# ---- command line ----

def run_doctor(project, *args):
    return subprocess.run([sys.executable, str(DOCTOR), "--project", str(project), *args],
                          capture_output=True, text=True, encoding="utf-8")


@needs_git_lfs
@pytest.mark.skipif(shutil.which("uv") is None, reason="uv is needed")
def test_cli_passes_on_a_complete_project(tmp_path):
    project = make_unity_project(tmp_path)
    git(project, "init")
    git(project, "lfs", "install", "--local")
    p = run_doctor(project)
    assert p.returncode == 0, p.stdout
    assert "All required checks passed." in p.stdout


def test_cli_json_and_exit_code(tmp_path):
    project = make_unity_project(tmp_path, vpm={"com.vrchat.avatars": "3.10.5"})
    (project / ".avatar-kit-version").write_text("9.9.9\n", encoding="utf-8")
    p = run_doctor(project, "--json")
    assert p.returncode == 1
    report = json.loads(p.stdout)
    assert report["ok"] is False and report["kit_version"] == "9.9.9"
    statuses = {c["id"]: c["status"] for c in report["checks"]}
    assert statuses["nadena.dev.modular-avatar"] == "missing"
    assert statuses["unity"] == "ok"
