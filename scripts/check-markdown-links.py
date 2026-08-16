#!/usr/bin/env python3
"""Fail when a repository-local Markdown link points to a missing path."""

from pathlib import Path
import re
import sys
from urllib.parse import unquote

root = Path(__file__).resolve().parents[1]
failures: list[str] = []
pattern = re.compile(r"(?<!!)\[[^]]*\]\(([^)]+)\)")

for document in root.rglob("*.md"):
    if ".git" in document.parts:
        continue
    for raw_target in pattern.findall(document.read_text(encoding="utf-8")):
        target = unquote(raw_target.split("#", 1)[0].strip())
        if not target or re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I):
            continue
        if not (document.parent / target).resolve().exists():
            failures.append(f"{document.relative_to(root)}: {raw_target}")

if failures:
    print("Broken local Markdown links:\n" + "\n".join(failures), file=sys.stderr)
    raise SystemExit(1)

print("All local Markdown links resolve.")
