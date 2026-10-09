#!/usr/bin/env python3
"""Static guard for the startup invariants.

Each check pins something whose absence produced the "never gets past the splash
screen" report: an unbounded wait before the first navigation, network I/O on the
startup path, or a log that is not actually written to the device.

It is static on purpose. None of this is reachable from the desktop harness -- the
code needs WinRT -- so the fastest honest guard is to assert the shape of the
startup path, and to fail loudly when someone reintroduces the stall.

Usage:
    python tools/startup_check.py      # exit 0 when every invariant holds
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
APPX = APP / "App.xaml.vb"
MAIN = APP / "MainPage.xaml.vb"
AUTH = APP / "Services" / "ProtonAuthService.vb"
LOG = APP / "Services" / "Log.vb"
STORE = APP / "Services" / "LogStore.vb"
PROJ = APP / "Proton VPN WP.vbproj"

SIGNATURES = {
    "OnLaunched": "Protected Overrides Sub OnLaunched(",
    "MainPage": "Protected Overrides Async Sub OnNavigatedTo(",
    "WaitForRestore": "Private Shared Async Function WaitForRestoreAsync()",
    "RestoreAsync": "Friend Async Function RestoreAsync()",
    "LogWrite": "Private Shared Sub Write(",
}

# A body ends at the first line that is only an End Sub/Function, at any indent:
# App's members sit at four spaces, MainPage's at eight.
END_RE = re.compile(r"^\s*End (?:Sub|Function)\s*$", re.MULTILINE)


def read(path: Path) -> str:
    if not path.exists():
        raise SystemExit(f"ERROR: missing file: {path}")
    return path.read_text(encoding="utf-8", errors="replace")


def code_only(text: str) -> str:
    """Drop VB comment lines, so a check cannot trip on its own documentation."""
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("'"))


def body_of(text: str, signature: str) -> str:
    """Text from `signature` up to the next line that only ends a Sub/Function."""
    start = text.find(signature)
    if start < 0:
        return ""
    match = END_RE.search(text, start)
    return text[start: match.start()] if match else text[start:]


def check(condition: bool, label: str, detail: str = "") -> bool:
    if condition:
        print(f"  [PASS] {label}")
        return True
    print(f"  [FAIL] {label}")
    if detail:
        for line in detail.splitlines():
            print(f"         {line}")
    return False


def main() -> int:
    app_src = read(APPX)
    main_src = read(MAIN)
    auth_src = read(AUTH)
    log_src = read(LOG)
    store_src = read(STORE)
    proj_src = read(PROJ)

    launched = code_only(body_of(app_src, SIGNATURES["OnLaunched"]))
    shell = code_only(body_of(main_src, SIGNATURES["MainPage"]))
    wait = code_only(body_of(main_src, SIGNATURES["WaitForRestore"]))
    restore = code_only(body_of(auth_src, SIGNATURES["RestoreAsync"]))
    write = code_only(body_of(log_src, SIGNATURES["LogWrite"]))
    store = code_only(store_src)

    ok = True

    ok &= check(
        "New Frame()" in launched
        and "Navigate(GetType(MainPage))" in launched
        and "Window.Current.Content = New MainPage" not in launched,
        "the shell page is navigated to, not assigned as the root visual",
        "Page.OnNavigatedTo is raised by the Frame that navigates. The device log of "
        "2026-10-09 showed no line from MainPage.OnNavigatedTo at all: the handler "
        "that fills RootFrame never ran, so the app never showed a page.",
    )

    ok &= check(
        "Await WaitForRestoreAsync()" in shell
        and "Task.WhenAny" in wait
        and "Task.Delay" in wait,
        "the shell's wait for the stored session is bounded",
        "MainPage.OnNavigatedTo must wait through WaitForRestoreAsync, which races the "
        "restore against Task.Delay(StartupBudget). An unbounded wait is what kept the "
        "app on the splash screen.",
    )

    ok &= check(
        "Await restore" not in shell,
        "no unbounded await of the restore remains in the shell",
    )

    ok &= check(
        "Await _api." not in restore,
        "RestoreAsync performs no network I/O",
        "The startup path must not await an API call: a slow or dead network there "
        "delays the first navigation by the HTTP timeout. Assigning the stored "
        "token onto the client (_api.UserId = ...) is fine; awaiting a call is not. "
        "Renewal belongs in RefreshInBackgroundAsync, called once the UI is up.",
    )

    ok &= check(
        "LogStore.Enqueue" in write,
        "Log.Write reaches the file sink",
        "Without this the log only exists in memory and cannot be read off the device.",
    )

    ok &= check(
        "ApplicationData.Current.LocalFolder" in store and "SharedLocalFolder" not in store,
        "the log lands in the app's local folder",
        "SharedLocalFolder does not exist on WP8.1 (0 matches in the platform winmd), "
        "so the file must live under LocalFolder, where ISETool can retrieve it.",
    )

    ok &= check(
        all(f'<Compile Include="Services\\{name}" />' in proj_src for name in ("LogFilePolicy.vb", "LogStore.vb")),
        "both new sources are registered in the project",
        "A source file that is not a Compile item is dead code the build never sees.",
    )

    print("startup invariants: " + ("OK" if ok else "FAILED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
