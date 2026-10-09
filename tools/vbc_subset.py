"""Run the project's real vbc command with a subset of the source files excluded.

vbc 12 has a hard 100-error ceiling and no /errorlimit switch, so when a project
has more than 100 errors the compiler truncates the list and hides the rest.
This replays the exact command line MSBuild used (captured from a /v:diag log)
while dropping files, which lets a caller see the errors of the remaining ones.

Usage:
    python tools/vbc_subset.py --diag <diag.log> --exclude "Views\\" [--exclude ...]
"""
import argparse
import os
import re
import subprocess
import sys

BAT = os.path.join(".superpowers", "sdd", "proton-vpn-wp81", "vbc-subset.bat")
LOG = os.path.join(".superpowers", "sdd", "proton-vpn-wp81", "vbc-subset.log")
PROJECT_DIR = os.path.join("Proton VPN WP", "Proton VPN WP")


def extract_command(diag_path):
    with open(diag_path, "r", encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            if "Vbc.exe" in raw and "/noconfig" in raw and "/out:" in raw:
                line = raw.strip()
                # MSBuild appends the task id to the echoed command line.
                line = re.sub(r"\s*\(ID .*$", "", line)
                # The executable path contains spaces and must be quoted.
                return re.sub(r"^(.*?Vbc\.exe)", r'"\1"', line, count=1)
    return None


TOKEN = re.compile(r'"[^"]*"|\S+')


def drop_sources(command, excludes):
    """Removes each source file whose path ends with any exclude fragment.

    Quoted tokens are kept quoted: several paths contain a space
    ("Proton VPN WP"), so splitting on whitespace would shred them.
    """
    prefix, _, rest = command.partition("/target:appcontainerexe")
    keep, dropped = [], []
    for token in TOKEN.findall(rest):
        stripped = token.strip('"')
        if stripped.endswith(".vb") and any(frag in stripped for frag in excludes):
            dropped.append(stripped)
        else:
            keep.append(token)
    return prefix + "/target:appcontainerexe " + " ".join(keep), dropped


def main():
    # vbc emits localized messages in the console code page; never let an
    # undecodable byte abort the diagnostic dump.
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass

    parser = argparse.ArgumentParser()
    parser.add_argument("--diag", required=True)
    parser.add_argument("--exclude", action="append", default=[])
    args = parser.parse_args()

    command = extract_command(args.diag)
    if not command:
        print("Could not find the vbc command line in the diag log.", file=sys.stderr)
        return 2

    command, dropped = drop_sources(command, args.exclude)
    print("Excluded {} source file(s):".format(len(dropped)))
    for name in dropped:
        print("  " + name)

    os.makedirs(os.path.dirname(BAT), exist_ok=True)
    with open(BAT, "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write("@echo off\n")
        handle.write('cd /d "{}\n'.format(os.path.abspath(PROJECT_DIR)))
        handle.write(command + "\n")

    with open(LOG, "w", encoding="utf-8", errors="replace") as out:
        result = subprocess.run(["cmd", "/c", os.path.abspath(BAT)],
                                stdout=out, stderr=subprocess.STDOUT)

    with open(LOG, "r", encoding="utf-8", errors="replace") as handle:
        lines = [l.rstrip("\n") for l in handle if "error" in l or "warning BC" in l]
    print("vbc exit code: {}".format(result.returncode))
    print("Diagnostic lines: {}".format(len(lines)))
    for line in lines:
        print(line)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
