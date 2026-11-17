# Installing vrchat-avatar-agent-kit

Written for the agent doing the install; a person can follow it too. Talk to the user in their language.

## Install

1. **Find the project.** It is the Unity project folder, the one with `Assets/`, `Packages/` and `ProjectSettings/`. If the user hasn't said which, ask. Never install into the kit itself.
2. **Check uv**: `uv --version`. If it is missing, give the user the install command and stop: Windows `winget install --id=astral-sh.uv -e`, macOS and Linux `curl -LsSf https://astral.sh/uv/install.sh | sh`.
3. **Get the kit.** If the user points at a local copy, use it. Otherwise clone the latest release into a temporary folder outside the project:

   ```sh
   git clone --depth 1 --branch <latest release tag> https://github.com/umitsurumi/vrchat-avatar-agent-kit <temp folder>
   ```

   The tags are on the [releases page](https://github.com/umitsurumi/vrchat-avatar-agent-kit/releases); `main` is the development version.
4. **Keep the install its own commit.** If the project is a Git repository with uncommitted changes, tell the user and suggest committing them first.
5. **Dry run** and show the user the result: how many files get copied, which existing files are left alone, which ignore rules get appended.

   ```sh
   uv run <kit>/install.py <project> --dry-run
   ```

6. **Install**, once the user agrees:

   ```sh
   uv run <kit>/install.py <project>
   ```

   `--json` gives the same report as JSON. The installer never overwrites a file; it copies what is missing, appends the `.gitignore` / `.gitattributes` rules the project lacks (under `# Added by vrchat-avatar-agent-kit`), links `.agents/skills` to `.claude/skills`, and writes `.avatar-kit-version`.
7. **Handle what was left alone.** For each skipped file, show the user the difference and ask:
   - `AGENTS.md`: merge the kit's rules into the project's own.
   - `CLAUDE.md`: add the line `@AGENTS.md`.
   - `PROJECT.md`: keep the project's, add the kit's headings it lacks.
   - Anything else (a skill the project already has, a tool): compare and let the user choose.
8. **The link.** If the report says the link failed (Windows without Developer Mode), pass the note on. Claude Code doesn't need the link; Codex does.
9. **Unity.** If Unity is open, it imports the new editor scripts under `Assets/AvatarAgentKit/`. Check the console for compile errors.
10. **Start.** Delete the temporary clone. Then follow the project's new `AGENTS.md`, "Before starting": run `uv run Tools/avatar-kit/doctor.py`, fill in `PROJECT.md` with the user, and, when the user says so, commit the install as `chore: install vrchat-avatar-agent-kit <version>` (the description in the user's language). If the project isn't a Git repository yet, `AGENTS.md` says how to set one up.

## Upgrading

The installer only adds missing files; an upgrade is done by the agent, file by file, so the user's own changes survive.

1. Read the old version from the project's `.avatar-kit-version` and the new one from the kit's.
2. Clone the kit with full history (no `--depth`), then list what changed between the two versions in the installed paths, and read `CHANGELOG.md` for the same range:

   ```sh
   git -C <kit> diff --stat v<old> v<new> -- AGENTS.md CLAUDE.md PROJECT.md .claude/skills Tools/avatar-kit Assets/AvatarAgentKit .gitignore .gitattributes
   ```

3. For each changed file:
   - The project's copy is identical to the old kit's (`git -C <kit> show v<old>:<path>`): replace it with the new one.
   - The user changed it: three-way merge with `git merge-file <project file> <old kit file> <new kit file>`, and show the user any conflicts.
   - `.gitignore` / `.gitattributes`: run the installer again; it appends new rules.
4. Files added in the new version: run the installer again; it copies what is missing.
5. Files removed in the new version: ask the user before deleting them.
6. Write the new version into `.avatar-kit-version`, and commit as `chore: upgrade vrchat-avatar-agent-kit <old> -> <new>` when the user says so.

## Uninstalling

Delete what the installer added: `AGENTS.md`, `CLAUDE.md` (or the `@AGENTS.md` line), `.avatar-kit-version`, `.claude/skills/` (only the kit's skills), `.agents/skills`, `Tools/avatar-kit/`, `Assets/AvatarAgentKit/`, and the block under `# Added by vrchat-avatar-agent-kit` in `.gitignore` and `.gitattributes`. Keep `PROJECT.md`, `Assets/_USER/` and `TextureSources/`: they hold the user's work.
