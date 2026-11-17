# Changelog

All notable changes to this kit. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- The template becomes a public kit, `vrchat-avatar-agent-kit`, under the MIT license.
- `AGENTS.md` and `PROJECT.md` are in English; agents still talk to the user in the user's language. `AGENTS.md` no longer points at the README (which is not installed into projects): it carries the commit types itself.
- README in English, with a Chinese translation in `README.zh-CN.md`.
- The Write Defaults tool moved to `Assets/AvatarAgentKit/Editor/` (namespace and asmdef `AvatarAgentKit.Editor`); its menu is now `Tools/Avatar Agent Kit/Set All Write Defaults ON`.

### Added

- `TextureSources/` for texture recipes and their inputs, outside `Assets/`.
