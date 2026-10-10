"""Network-failure diagnosis invariants.

The one thing the device transcripts of 2026-10-09 and 2026-10-10 never said is
*which layer* refused: `Network request failed: Exception Exception from HRESULT:
0x80072EFD` is the same line whether the name did not resolve, the profile has no
internet, the TCP connect was refused, or Proton's own host is the only one
unreachable. The error code alone cannot tell those apart, and the difference is
the whole diagnosis -- one of them is a bug in this app, three are not.

So a failed request now explains itself, once, in layers, into the log. Each
guard below is a property a diagnosis has to keep to be worth reading:

  1. All four layers are probed: the connection profile, DNS, TCP, and HTTP to
     two hosts -- Proton and a control that is not Proton. Without the control,
     "no internet on this device" and "only Proton is blocked from here" read as
     the same failure.
  2. The socket layer reports the specific `SocketErrorStatus`, not just a
     message: `HostNameNotResolved` and `NetworkDown` are different answers.
  3. It runs at most once at a time and at most once per interval, because it is
     reached from the failure path and the failure path can be a retry loop.
  4. It cannot throw: it runs while something has already failed, so a second
     failure out of the diagnosis would replace the answer with noise.
  5. The failed request calls it, and no secret can reach a `netdiag:` line.
  6. It is not on the startup path: the first frame must not wait for a probe
     (`tools/startup_check.py` pins the same rule for the stored session).

Reverting any of these must make this script fail.
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
CONNECTIVITY = APP / "Services" / "ConnectivityService.vb"
API = APP / "Services" / "ProtonApiClient.vb"
AUTH = APP / "Services" / "ProtonAuthService.vb"
SHELL = APP / "MainPage.xaml.vb"
APP_SHELL = APP / "App.xaml.vb"

SECRET_WORDS = ("token", "password", "proof", "ephemeral", "secret")

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def body(text: str, start_marker: str, end_marker: str = "\n        End Function") -> str:
    """The text from start_marker to the next end_marker after it, or ""."""
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    return text[start:end] if end >= 0 else text[start:]


def log_lines(text: str) -> list[str]:
    return [line for line in text.splitlines() if "Log.Info(" in line or "Log.Warn(" in line]


def main() -> int:
    for path in (CONNECTIVITY, API, AUTH, SHELL, APP_SHELL):
        if not path.is_file():
            print(f"ERROR: missing file: {path}", file=sys.stderr)
            return 2

    connectivity = CONNECTIVITY.read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")

    diagnose = body(connectivity, "Friend Shared Async Function DiagnoseAsync(")
    send = body(api, "Private Async Function SendAsync(", "\n        End Function")

    layers = {
        "the connection profile": ("Summarise()", "the profile is the one layer the app can read without sending anything"),
        "DNS": ("GetEndpointPairsAsync", "resolution on its own, so a name that will not resolve is not reported as a refused connection"),
        "TCP": ("ConnectAsync", "the connect on its own, so a refused or unreachable port is named as such"),
        "the TLS handshake": (
            "ProbeTlsAsync",
            "WP8.1's HTTP stack offers the OS default protocol set, and an edge that "
            "accepts none of them closes the connection -- which arrives as exactly "
            "the same \"cannot connect\" as a dead network",
        ),
        "a control HTTP request": ("ControlUrl", "without a second, non-Proton host there is no way to tell \"no internet here\" from \"only Proton is blocked\""),
        "a control TLS handshake": (
            "ControlTlsHost",
            "a handshake that succeeds elsewhere is what tells a refusal of this "
            "client's protocol set apart from a transport that cannot handshake at all",
        ),
        "the Proton HTTPS request": (
            "protonUrl",
            "the endpoint the app actually uses, handed in by the caller so the probe "
            "targets the host that failed rather than a copy of it",
        ),
    }
    for title, (marker, why) in layers.items():
        check(
            diagnose != "" and marker in connectivity,
            f"the diagnosis probes {title}",
            why,
        )
    for probe in ("ProbeDnsAsync", "ProbeTcpAsync", "ProbeTlsAsync", "ProbeHttpAsync"):
        check(
            probe in diagnose,
            f"DiagnoseAsync runs {probe}",
            "A probe that nothing calls is not a diagnosis; the layers above have "
            "to be the ones the failure path actually walks.",
        )
    check(
        diagnose != "" and 'Const ControlUrl As String = "http://' in connectivity,
        "the control host is reached over plain HTTP",
        "Over HTTPS the control would fail for TLS reasons too, and would stop "
        "isolating the transport from the internet itself.",
    )

    check(
        "SocketError.GetStatus" in connectivity,
        "the socket layer reports the specific SocketErrorStatus",
        "HostNameNotResolved, NetworkDown and ConnectionRefused are three "
        "different answers to \"why\", and the HRESULT alone hides all three.",
    )
    check(
        "_diagnoseBusy" in diagnose and "DiagnoseMinIntervalSeconds" in diagnose,
        "the diagnosis is bounded to one run at a time and one per interval",
        "It is reached from the failure path; a retry loop would otherwise "
        "re-run it per attempt and bury the first answer.",
    )
    check(
        "Catch ex As Exception" in diagnose and "Finally" in diagnose,
        "the diagnosis cannot throw out of itself",
        "It runs while something has already failed: a second failure from the "
        "diagnosis would replace the answer with noise.",
    )

    call = next((line for line in send.splitlines() if "ConnectivityService.DiagnoseAsync" in line), "")
    check(
        send != "" and call != "" and "url" in call,
        "the failed request explains itself in layers",
        "This is the line that turns 0x80072EFD from a dead end into an answer, "
        "and it has to hand the diagnosis the URL that just failed.",
    )

    leaked = [
        line.strip()
        for line in log_lines(diagnose)
        if any(word in line.lower() for word in SECRET_WORDS)
    ]
    check(
        not leaked,
        "no netdiag line can carry a secret",
        "Services/Log.vb exposes no API that accepts a credential; the "
        "diagnosis must not reintroduce one through a URL or a parameter."
        + ("" if not leaked else " Offending line(s): " + " | ".join(leaked)),
    )

    startup_paths = {
        "App.OnLaunched": body(APP_SHELL.read_text(encoding="utf-8"), "Protected Overrides Sub OnLaunched(", "\n    End Sub"),
        "MainPage.WaitForRestoreAsync": body(
            SHELL.read_text(encoding="utf-8"), "Private Shared Async Function WaitForRestoreAsync(", "\n    End Function"
        ),
        "ProtonAuthService.RestoreAsync": body(
            AUTH.read_text(encoding="utf-8"), "Friend Async Function RestoreAsync(", "\n        End Function"
        ),
    }
    on_startup = [name for name, text in startup_paths.items() if "DiagnoseAsync" in text]
    check(
        not on_startup,
        "the diagnosis is not on the startup path",
        "The first frame must not wait for a probe; a failure there is what "
        "tools/startup_check.py exists to prevent."
        + ("" if not on_startup else " Found in: " + ", ".join(on_startup)),
    )

    print("network diagnosis: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
