#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
"""Install vrchat-avatar-agent-kit into a Unity avatar project.

    uv run install.py <project> [--dry-run] [--json]

Copies the kit's files into the project, links .agents/skills to .claude/skills (for Codex),
adds the .gitignore and .gitattributes rules the project lacks, and records the kit version
in .avatar-kit-version. It never overwrites an existing file: a file that differs from the
kit's is listed as skipped, for the agent or the user to merge by hand (INSTALL.md).

Exit codes: 0 installed (skipped files are not an error), 2 the target is not a Unity project.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from pathlib import Path

KIT = Path(__file__).resolve().parent

# Installed as they are, relative to the kit root. Folders are copied recursively.
INSTALL_PATHS = [
    "AGENTS.md",
    "CLAUDE.md",
    "PROJECT.md",
    ".avatar-kit-version",
    ".claude/skills",
    "Tools/avatar-kit",
    "Assets/AvatarAgentKit",
    "Assets/_USER/.gitkeep",
    "TextureSources/.gitkeep",
]
# Merged: rules the project lacks are appended; nothing is removed or changed.
MERGED_PATHS = [".gitignore", ".gitattributes"]
LINK_PATH = ".agents/skills"
LINK_TARGET = os.path.join("..", ".claude", "skills")  # Windows only resolves links written with backslashes

EXCLUDED_DIRS = {"__pycache__", ".pytest_cache"}
EXCLUDED_NAMES = {".DS_Store", "Thumbs.db", "desktop.ini"}
EXCLUDED_SUFFIXES = {".pyc"}
MERGE_HEADER = "# Added by vrchat-avatar-agent-kit"

# What to tell the user when one of these already exists.
SKIP_ADVICE = {
    "AGENTS.md": "The project already has its own AGENTS.md: merge the kit's rules into it.",
    "CLAUDE.md": "The project already has a CLAUDE.md: add the line `@AGENTS.md` to it.",
    "PROJECT.md": "The project already has a PROJECT.md: keep it, and add the kit's headings it lacks.",
}


def kit_files() -> list[Path]:
    """Every file to install, relative to the kit root."""
    files: list[Path] = []
    for rel in INSTALL_PATHS:
        src = KIT / rel
        if src.is_file():
            files.append(Path(rel))
            continue
        for root, dirs, names in os.walk(src):
            dirs[:] = sorted(d for d in dirs if d not in EXCLUDED_DIRS)
            for name in sorted(names):
                if name in EXCLUDED_NAMES or Path(name).suffix in EXCLUDED_SUFFIXES:
                    continue
                files.append((Path(root) / name).relative_to(KIT))
    return files


def rule_lines(text: str) -> list[str]:
    return [line.strip() for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")]


def missing_rules(kit_text: str, project_text: str) -> list[str]:
    have = set(rule_lines(project_text))
    return [line for line in rule_lines(kit_text) if line not in have]


def append_rules(path: Path, rules: list[str]) -> None:
    data = path.read_bytes()
    eol = b"\r\n" if b"\r\n" in data else b"\n"
    block = eol.join([MERGE_HEADER.encode()] + [r.encode("utf-8") for r in rules]) + eol
    prefix = b"" if not data or data.endswith(b"\n") else eol
    path.write_bytes(data + prefix + eol + block)


def make_link(link: Path) -> str | None:
    """Create the link; return None on success, or why it failed."""
    link.parent.mkdir(parents=True, exist_ok=True)
    try:
        os.symlink(LINK_TARGET, link, target_is_directory=True)
    except OSError as e:
        return str(e)
    return None


def install(project: Path, dry_run: bool) -> dict:
    report: dict = {
        "kit": str(KIT),
        "kit_version": (KIT / ".avatar-kit-version").read_text(encoding="utf-8").strip(),
        "project": str(project),
        "dry_run": dry_run,
        "copied": [], "unchanged": [], "skipped": [], "merged": {}, "link": "", "notes": [],
    }

    version_file = project / ".avatar-kit-version"
    if version_file.is_file():
        old = version_file.read_text(encoding="utf-8").strip()
        if old != report["kit_version"]:
            report["notes"].append(
                f"The project was set up with kit {old}; this is {report['kit_version']}. "
                "The installer only adds missing files. To upgrade, follow INSTALL.md, \"Upgrading\".")

    for rel in kit_files():
        src, dst = KIT / rel, project / rel
        key = rel.as_posix()
        if dst.exists():
            if dst.is_file() and dst.read_bytes() == src.read_bytes():
                report["unchanged"].append(key)
            else:
                report["skipped"].append(key)
                if key in SKIP_ADVICE:
                    report["notes"].append(SKIP_ADVICE[key])
            continue
        report["copied"].append(key)
        if not dry_run:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)

    for rel in MERGED_PATHS:
        src, dst = KIT / rel, project / rel
        if not dst.exists():
            report["copied"].append(rel)
            if not dry_run:
                shutil.copyfile(src, dst)
            continue
        rules = missing_rules(src.read_text(encoding="utf-8"),
                              dst.read_text(encoding="utf-8", errors="surrogateescape"))
        if not rules:
            report["unchanged"].append(rel)
            continue
        report["merged"][rel] = rules
        if not dry_run:
            append_rules(dst, rules)

    link = project / LINK_PATH
    if link.is_symlink() or link.exists():
        report["link"] = "exists"
    elif dry_run:
        report["link"] = "would create"
    else:
        error = make_link(link)
        report["link"] = "created" if error is None else "failed"
        if error is not None:
            report["notes"].append(
                f"Could not create the link {LINK_PATH} -> {LINK_TARGET} ({error}). On Windows this needs "
                "Developer Mode (Settings > System > For developers) or an administrator. Claude Code doesn't "
                "need the link; for Codex, turn on Developer Mode and run the installer again.")
    return report


def print_report(r: dict) -> None:
    would = "would be " if r["dry_run"] else ""
    print(f"vrchat-avatar-agent-kit {r['kit_version']} -> {r['project']}" + ("  (dry run)" if r["dry_run"] else ""))
    print(f"\n{len(r['copied'])} file(s) {would}copied, {len(r['unchanged'])} already up to date.")
    if r["skipped"]:
        print(f"\n{len(r['skipped'])} file(s) already exist with different content and were left alone:")
        for f in r["skipped"]:
            print(f"  - {f}")
    for f, rules in r["merged"].items():
        print(f"\n{f}: {len(rules)} rule(s) {would}appended:")
        for line in rules:
            print(f"  + {line}")
    print(f"\n{LINK_PATH}: {r['link']}")
    for note in r["notes"]:
        print(f"\nNote: {note}")
    if not r["dry_run"]:
        print("\nNext: open your agent in the project. It follows AGENTS.md: runs the environment check "
              "(uv run Tools/avatar-kit/doctor.py), fills in PROJECT.md with you, and commits the install as "
              f"\"chore: install vrchat-avatar-agent-kit {r['kit_version']}\".")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Install vrchat-avatar-agent-kit into a Unity avatar project.")
    parser.add_argument("project", type=Path, help="the Unity project folder (with Assets/ and ProjectSettings/)")
    parser.add_argument("--dry-run", action="store_true", help="show what would happen, change nothing")
    parser.add_argument("--json", action="store_true", help="print JSON for agents")
    args = parser.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")

    project = args.project.resolve()
    if project == KIT:
        print("The target is the kit itself; give the path of a Unity project.", file=sys.stderr)
        return 2
    if not (project / "Assets").is_dir() or not (project / "ProjectSettings" / "ProjectVersion.txt").is_file():
        print(f"{project} is not a Unity project folder (expected Assets/ and ProjectSettings/ProjectVersion.txt).",
              file=sys.stderr)
        return 2

    report = install(project, args.dry_run)
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print_report(report)
    return 0


if __name__ == "__main__":
    sys.exit(main())
