#!/usr/bin/env python3
"""Guard: what the user types must reach the code, and must be readable.

Two rules, both learned the hard way on this project.

1. **wiring** -- an editable ``TextBox``/``PasswordBox`` that its page's
   code-behind never *reads* cannot do anything with what was typed.
   ``LoginPage`` read ``PasswordEntry.Password`` and never read
   ``UsernameBox.Text``, so a fully filled sign-in form was rejected with
   "Enter your Proton username and password.", and the view model's
   ``Username`` stayed empty because it is only ever seeded from a stored
   session. The read has to be a read: an assignment *into* the box
   (``UsernameBox.Text = _viewModel.Username``) is not one, which is exactly
   the line that made the bug look handled.

2. **appearance** -- the WP8.1 ``TextBox`` template keeps the app's
   ``Foreground`` and switches the *field* to the platform's edit colour while
   typing (``PhoneTextBoxEditBackgroundBrush`` = ``#FFFFFFFF``; the template's
   ``Focused`` state animates no foreground at all). A style that pairs a dark
   field with near-white text therefore goes blank the moment the user types
   into it. The platform's own pairing -- ``#FFFFFFFF`` field,
   ``#FF000000`` text -- is the one this rule requires: contrast of at least
   4.5:1 *and* the text on the dark side of the pair.

Usage:
    python tools/input_check.py                  # both rules
    python tools/input_check.py --only wiring    # the read rule only
    python tools/input_check.py --only appearance
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
APP_XAML = APP / "App.xaml"

INPUT_TAG = re.compile(r"<(TextBox|PasswordBox)\b(.*?)/?>", re.DOTALL)
NAME_ATTR = re.compile(r'x:Name="([^"]+)"')
READONLY_ATTR = re.compile(r'\bIsReadOnly="True"')
BINDING_ATTR = re.compile(r'\b(?:Text|Password)="\{Binding')

STYLE_BLOCK = re.compile(
    r'<Style\s+x:Key="(ProtonTextBox|ProtonPasswordBox)".*?</Style>', re.DOTALL)
SETTER = re.compile(
    r'<Setter\s+Property="(Background|Foreground)"\s+Value="\{StaticResource\s+([^}]+?)\s*\}"')
COLOR_DEF = re.compile(r'<Color\s+x:Key="([^"]+)"\s*>\s*(#[0-9A-Fa-f]{6,8})\s*</Color>')
BRUSH_DEF = re.compile(
    r'<SolidColorBrush\s+x:Key="([^"]+)"\s+Color="\{StaticResource\s+([^}]+?)\s*\}"')

# Below this ratio the text is not legibly readable (WCAG 2.1 AA, normal text).
MIN_CONTRAST = 4.5


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def code_lines(path: Path, pattern: re.Pattern[str]):
    """1-based (line number, text) pairs from a code-behind, comments dropped.

    A name mentioned only in a comment is not a read: the comment about the
    line that caused this bug would otherwise count as the fix.
    """
    for number, line in enumerate(read(path).splitlines(), start=1):
        if line.lstrip().startswith("'"):
            continue
        if pattern.search(line):
            yield number, line


def xaml_files() -> list[Path]:
    files = [APP / "MainPage.xaml"]
    files += sorted((APP / "Views").glob("*.xaml"))
    return [path for path in files if path.is_file()]


def editable_controls(path: Path):
    """(name, kind) for every input the user can type into, bindings excluded."""
    found = []
    for kind, attributes in INPUT_TAG.findall(read(path)):
        name = NAME_ATTR.search(attributes)
        if not name:
            continue
        if READONLY_ATTR.search(attributes) or BINDING_ATTR.search(attributes):
            continue
        found.append((name.group(1), kind))
    return found


def wiring_failures() -> tuple[list[str], int]:
    problems, checked = [], 0
    for xaml in xaml_files():
        code = xaml.with_suffix(".xaml.vb")
        controls = editable_controls(xaml)
        if not controls:
            continue
        if not code.is_file():
            problems.append(f"FAIL: {xaml.name} has input controls but no {code.name} "
                            f"-- nothing can read them")
            continue
        for name, kind in controls:
            checked += 1
            prop = "Password" if kind == "PasswordBox" else "Text"
            mention = re.compile(r"\b" + re.escape(name) + r"\." + prop + r"\b")
            # An assignment into the box is not a read of it.
            assignment = re.compile(
                r"^\s*" + re.escape(name) + r"\." + prop + r"\s*=")
            reads = [(number, line) for number, line in code_lines(code, mention)
                     if not assignment.match(line)]
            if not reads:
                written = [number for number, _ in code_lines(code, mention)]
                where = f"{code.name}:{written[0]}" if written else code.name
                problems.append(
                    f"FAIL: {where} {name} is never read -- what the user types "
                    f"into it cannot reach the view model")
    return problems, checked


def to_rgb(value: str) -> tuple[int, int, int]:
    digits = value.lstrip("#")
    if len(digits) == 8:
        digits = digits[2:]          # drop the alpha channel
    return int(digits[0:2], 16), int(digits[2:4], 16), int(digits[4:6], 16)


def luminance(value: str) -> float:
    """WCAG relative luminance, 0.0 (black) to 1.0 (white)."""
    channels = []
    for raw in to_rgb(value):
        channel = raw / 255.0
        channels.append(channel / 12.92 if channel <= 0.03928
                        else ((channel + 0.055) / 1.055) ** 2.4)
    return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2]


def contrast(first: str, second: str) -> float:
    high, low = sorted((luminance(first), luminance(second)), reverse=True)
    return (high + 0.05) / (low + 0.05)


def appearance_failures() -> tuple[list[str], int]:
    text = read(APP_XAML)
    colors = dict(COLOR_DEF.findall(text))
    brushes = dict(BRUSH_DEF.findall(text))

    def resolve(key: str):
        color = brushes.get(key)
        return colors.get(color) if color else None

    problems, styled = [], 0
    for match in STYLE_BLOCK.finditer(text):
        name, block = match.group(1), match.group(0)
        styled += 1
        setters = dict((prop, key) for prop, key in SETTER.findall(block))
        missing = [prop for prop in ("Background", "Foreground") if prop not in setters]
        if missing:
            problems.append(
                f"FAIL: App.xaml: style {name} does not set {' and '.join(missing)} "
                f"-- it inherits a colour the platform may swap under it")
            continue
        field = resolve(setters["Background"])
        typed = resolve(setters["Foreground"])
        if field is None or typed is None:
            problems.append(
                f"FAIL: App.xaml: style {name} points at a brush that is not defined "
                f"in App.xaml (Background {setters['Background']}, "
                f"Foreground {setters['Foreground']})")
            continue
        ratio = contrast(typed, field)
        if luminance(typed) >= luminance(field):
            problems.append(
                f"FAIL: App.xaml: style {name} puts the text on the light side of the "
                f"pair (text {typed} on field {field}); the field turns white while "
                f"typing, so the text disappears")
        elif ratio < MIN_CONTRAST:
            problems.append(
                f"FAIL: App.xaml: style {name} has only {ratio:.1f}:1 contrast "
                f"(text {typed} on field {field}); {MIN_CONTRAST}:1 is the floor")
    return problems, styled


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", choices=("wiring", "appearance"),
                        help="run one rule instead of both")
    args = parser.parse_args()

    if not APP_XAML.is_file():
        print(f"ERROR: missing file: {APP_XAML}", file=sys.stderr)
        return 2

    problems: list[str] = []
    summary: list[str] = []

    if args.only in (None, "wiring"):
        found, checked = wiring_failures()
        problems += found
        summary.append(f"wiring: {'OK' if not found else 'FAILED'} ({checked} editable controls)")
    if args.only in (None, "appearance"):
        found, styled = appearance_failures()
        problems += found
        summary.append(f"appearance: {'OK' if not found else 'FAILED'} ({styled} styles)")

    for problem in problems:
        print(problem)
    for line in summary:
        print(line)

    if problems:
        print(f"input fields: FAILED ({len(problems)} problem(s))")
        return 1

    print("input fields: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
