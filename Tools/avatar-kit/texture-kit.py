#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy>=1.26", "pillow>=10.1", "psd-tools>=1.9"]
# [tool.uv]
# exclude-newer = "2026-11-01T00:00:00Z"
# ///
"""Texture kit: rebuild a texture from its untouched original by a recipe.

    uv run Tools/avatar-kit/texture-kit.py <command> ...    (--help for the commands)

uv installs the libraries on first use. `exclude-newer` pins them to what was released before that
date, so a recipe gives the same pixels on every machine. The recipe format is documented in
texture_kit/recipe.py and in the skill vrchat-textures.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from texture_kit.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
