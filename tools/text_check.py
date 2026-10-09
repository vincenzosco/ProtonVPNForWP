#!/usr/bin/env python3
"""Guard: a view model must never hand a view a null string.

WinRT rejects a null ``TextBlock.Text`` -- it raises ArgumentNullException inside
the HSTRING marshaller, which is *not* how WPF or Silverlight behave, so the usual
habit of letting Nothing flow into a Text property kills the page. The views assign
view-model properties straight to those properties, so the rule lives in the
getters: every string property coalesces to String.Empty.

This is the check that was missing on 2026-10-09, when
``LoginPage.UpdateVisualState`` assigned a null ``StatusMessage`` during navigation
and the app died with an unhandled Frame.NavigationFailed.

Only the bare shape is flagged -- ``Return _field`` -- because that is the one that
can hand back Nothing. ``Return If(_field, String.Empty)`` is the fix, and a getter
with real logic is judged by its own guarded returns.

Usage:
    python tools/text_check.py      # exit 0 when every string getter coalesces
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VIEWMODELS = ROOT / "Proton VPN WP" / "Proton VPN WP" / "ViewModels"

DECL = re.compile(r"^\s*(?:Friend|Public|Private|Protected)?\s*(?:ReadOnly\s+)?Property\s+(\w+)\s+As\s+String\s*$")
RETURN = re.compile(r"^\s*Return\s+(.+?)\s*$")
BARE_FIELD = re.compile(r"^_[A-Za-z_]\w*$")
END_PROPERTY = re.compile(r"^\s*End Property\s*$")


def scan(path: Path) -> list[str]:
    """Violations in one file: properties whose first return is a bare field."""
    problems: list[str] = []
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()

    index = 0
    while index < len(lines):
        decl = DECL.match(lines[index])
        if not decl:
            index += 1
            continue

        name = decl.group(1)
        for offset in range(index + 1, min(index + 30, len(lines))):
            line = lines[offset]
            if END_PROPERTY.match(line):
                break
            returned = RETURN.match(line)
            if not returned:
                continue
            expression = returned.group(1)
            if BARE_FIELD.match(expression):
                problems.append(
                    f"FAIL: {path.name}:{offset + 1} {name} returns '{expression}' uncoalesced"
                    f" -- a null TextBlock.Text throws on WinRT"
                )
            break
        index += 1

    return problems


def main() -> int:
    if not VIEWMODELS.is_dir():
        raise SystemExit(f"ERROR: missing directory: {VIEWMODELS}")

    files = sorted(VIEWMODELS.glob("*.vb"))
    if not files:
        raise SystemExit(f"ERROR: no view models found in {VIEWMODELS}")

    problems: list[str] = []
    for path in files:
        problems.extend(scan(path))

    for problem in problems:
        print(problem)

    if problems:
        print(f"view-model strings: FAILED ({len(problems)} uncoalesced)")
        return 1

    print(f"view-model strings: OK ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
