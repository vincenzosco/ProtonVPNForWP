#!/usr/bin/env python3
"""Compile the shipping log-file policy into a desktop exe and check its vectors.

Same shape as ``tools/run_srp_harness.py``: the real app source file
(``Services/LogFilePolicy.vb``) is compiled with ``vbc`` and driven with fixed
inputs, so a green run is evidence about what ships rather than about a copy.

The policy is deliberately WinRT-free precisely so this can run on the desktop;
``Services/LogStore.vb`` (the actual file I/O) cannot be, and is covered by the
build instead.

Usage:
    python tools/run_log_harness.py
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_SERVICES = HERE.parent / "Proton VPN WP" / "Proton VPN WP" / "Services"
OUT = HERE / "vb_log_harness" / "out"

SOURCE_FILES = ["LogFilePolicy.vb"]

# 2026-10-09T15:28:28.7215520Z, written with the invariant round-trip format.
EXPECTED = {
    "format": "[2026-10-09T15:28:28.7215520Z] Info: startup: shell navigated\\r\\n",
    # The same instant handed over as local time must still print as UTC.
    "format_local": "[2026-10-09T15:28:28.7215520Z] Error: boom\\r\\n",
    "trim65536": "False",
    "trim65537": "True",
    "keep1000": "500",
    "keep3": "1",
    "keep1": "1",
    "newest": "c|d",
    "newest_all": "a|b",
    "newest_zero": "",
}


def find_vbc() -> str:
    candidates = [
        r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
        r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\vbc.exe",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    raise SystemExit("ERROR: vbc.exe (.NET Framework v4.0.30319) not found")


def build() -> str:
    vbc = find_vbc()
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / "log_harness.exe"

    sources = [str(APP_SERVICES / f) for f in SOURCE_FILES]
    missing = [s for s in sources if not os.path.exists(s)]
    if missing:
        raise SystemExit("ERROR: missing source files: " + ", ".join(missing))
    sources.append(str(HERE / "vb_log_harness" / "Program.vb"))

    cmd = [
        vbc,
        "/nologo",
        "/optionstrict+",
        "/optionexplicit+",
        "/target:exe",
        f"/out:{exe}",
        *sources,
    ]
    print("compiling: " + " ".join(cmd[:6]) + " ...")
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(f"vbc failed with exit code {result.returncode}")
    if result.stdout.strip():
        print(result.stdout.strip())
    return str(exe)


def main() -> int:
    exe = build()
    run = subprocess.run([exe], capture_output=True, text=True)
    if run.returncode != 0:
        print(run.stdout)
        print(run.stderr)
        raise SystemExit(f"harness failed with exit code {run.returncode}")

    actual: dict[str, str] = {}
    for line in run.stdout.splitlines():
        if "=" in line:
            key, _, value = line.partition("=")
            actual[key] = value

    failures = 0
    for key, expected in EXPECTED.items():
        got = actual.get(key)
        if got == expected:
            print(f"  [PASS] {key}")
        else:
            failures += 1
            print(f"  [FAIL] {key}\n         expected: {expected!r}\n         actual:   {got!r}")

    if failures:
        print(f"log policy vectors: {failures} FAILED")
        return 1
    print("log policy vectors: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
