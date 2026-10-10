"""Honesty invariants: the app must not blame the user for a platform limit, and
must not hide the one it bypasses.

Two measurements are on record. The first (2026-10-10): Windows Phone 8.1 offers
no cipher suite Proton's API edge accepts, so *the platform's* stack is refused
before a single request is sent, on a network that works. Every device transcript
since 2026-10-09 ended there, and the app answered all of it with one sentence:

    "Could not reach Proton. Check the connection and try again."

That sentence blamed the user for the platform. The second measurement, same day:
the app can complete that handshake itself, with its own TLS 1.3 client pinned to
the ISRG roots, which is what the sign-in path now uses.

So this guard pins four things:

  1. The failure path builds its message from the one signal that separates the
     two honest cases without waiting: the connection profile. No profile means
     the device really is offline; a profile with internet access means the
     request was refused somewhere between here and Proton.
  2. The accusation is gone: no `Catch` in the API client returns "Check the
     connection".
  3. The stale excuse is gone too: the message must not claim the platform cannot
     reach Proton, because this app no longer depends on the platform's stack for
     that.
  4. The user-facing documents agree with both measurements: README, the About
     page and the sign-in page say the connection to Proton is made by the app's
     own TLS 1.3 client with a pinned chain, rather than either promising a
     sign-in that cannot happen or denying the one that can.

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
        "the offline branch and the reachable branch are both present",
        "The offline branch names being offline; the other branch names the "
        "handshake or the network as what refused, and points at the offline "
        "server list.",
    )
    check(
        "windows phone 8.1 cannot" not in reachability.lower()
        and "cannot complete" not in reachability.lower(),
        "the message no longer claims the platform cannot reach Proton",
        "The app completes that handshake itself, so the claim is stale: it would "
        "tell the user the one thing that is now not the reason.",
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
            f"{title} states that the app makes the handshake with Proton itself",
            "The platform's stack still cannot, and the app must not promise a "
            "sign-in it cannot deliver -- but it now can, so the document has to "
            "say which side of the seam the handshake happens on.",
        )
        check(
            "tls 1.3" in text,
            f"{title} names the TLS 1.3 client that does it",
            "A hand-written TLS stack and a pinned certificate chain are facts a "
            "user of an unofficial VPN client is entitled to read, not buried.",
        )

    check(
        "isrg" in readme,
        "README names the pinned certificate authority",
        "The pin is the reason a Proton certificate rotation does not brick the "
        "app and the reason an untrusted chain is refused; it belongs in the "
        "document that describes the security posture.",
    )

    print("honesty: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
