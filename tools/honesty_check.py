"""Honesty invariants: the app must not blame the user for a platform limit.

The 2026-10-10 diagnosis proved a permanent platform limit: Windows Phone 8.1
offers no cipher suite Proton's API edge accepts, so the handshake is refused
before a single request is sent -- on a network that works. Every device
transcript since 2026-10-09 ends in the same place, and the app answered it with
one sentence for every transport failure:

    "Could not reach Proton. Check the connection and try again."

That sentence is false here. The connection is fine; the platform cannot handshake
with Proton at all. So this guard pins three things:

  1. The failure path builds its message from the one signal that separates the
     two honest cases without waiting: the connection profile. No profile means
     the device really is offline; a profile with internet access means the
     refusal is the platform's. The offline branch says "offline", the platform
     branch names the handshake and the offline list.
  2. The accusation is gone: no `Catch` in the API client returns "Check the
     connection".
  3. The user-facing documents agree with the measurement: README, the About page
     and the sign-in page all say the platform cannot complete Proton's
     handshake, rather than promising a sign-in that cannot happen.

Reverting any of these must make this script fail.
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
API = APP / "Services" / "ProtonApiClient.vb"
ABOUT = APP / "Views" / "AboutPage.xaml"
LOGIN = APP / "Views" / "LoginPage.xaml"
README = ROOT / "README.md"

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def body(text: str, start_marker: str, end_marker: str = "\n        End Function") -> str:
    """The text from start_marker to the next end_marker after it, or \"\"."""
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    return text[start:end] if end >= 0 else text[start:]


def main() -> int:
    for path in (API, ABOUT, LOGIN, README):
        if not path.is_file():
            print(f"ERROR: missing file: {path}", file=sys.stderr)
            return 2

    api = API.read_text(encoding="utf-8")
    about = ABOUT.read_text(encoding="utf-8").lower()
    login = LOGIN.read_text(encoding="utf-8").lower()
    readme = README.read_text(encoding="utf-8").lower()

    send = body(api, "Private Async Function SendAsync(", "\n        End Function")
    reachability = body(api, "Private Shared Function ReachabilityFailureMessage(")

    check(
        send != "" and "ReachabilityFailureMessage()" in send,
        "a failed request builds its message from the reachability check",
        "A single canned string for every transport failure is the defect: the "
        "one failure this app hits is not the user's network.",
    )
    check(
        reachability != "" and "HasInternetAccess" in reachability,
        "the message separates \"offline\" from \"the platform cannot reach Proton\"",
        "The connection profile is the one signal available without waiting; "
        "without it the two honest answers collapse into one.",
    )
    check(
        reachability != "" and "offline" in reachability.lower()
        and "handshake" in reachability.lower(),
        "the offline branch and the platform branch are both present",
        "The offline branch names being offline; the platform branch names the "
        "handshake it cannot complete and points at the offline server list.",
    )
    accused = [
        line.strip()
        for line in send.splitlines()
        if "Check the connection" in line and "Failure(" in line
    ]
    check(
        not accused,
        "no failure message tells the user to check their connection",
        "That is the sentence the whole diagnosis exists to stop."
        + ("" if not accused else " Offending line(s): " + " | ".join(accused)),
    )

    for title, text in (
        ("README", readme),
        ("the About page", about),
        ("the sign-in page", login),
    ):
        check(
            "handshake" in text and "proton" in text,
            f"{title} states the platform cannot complete Proton's handshake",
            "The app must not promise a sign-in the platform cannot deliver; the "
            "measurement belongs next to the feature it disables.",
        )

    print("honesty: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
