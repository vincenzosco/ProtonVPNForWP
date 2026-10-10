"""The sign-in reads what Proton sends, and refuses what it cannot do.

`/auth/info` carries more than the SRP challenge: `Version` says which password
derivation the server expects, and `Challenge` says a proof of work is required.
Five properties, each a line in the source:

  1. The sign-in does not call the version-3 derivation directly. It is the right
     derivation for version 4 today -- go-srp's `NewAuthForVerifier` labels 4 as
     "hash version is 4" and calls `hashPasswordVersion3` -- but that is a fact
     about today, and a hardcoded call cannot notice the day it stops holding.
  2. `Version` is read from the response. An unread field is an unstated
     assumption.
  3. `Challenge` is read too. Proton asks for a proof of work when it suspects
     automation, and a request that skips it is answered like a wrong password.
  4. Both refusals name what actually happened, so the message is actionable
     rather than "the sign-in failed".
  5. They refuse: a version this app cannot derive, and a challenge it cannot
     solve, must stop the attempt rather than send a proof it cannot justify.

Reverting any of these must make this script fail.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
SERVICES = APP / "Services"
AUTH = SERVICES / "ProtonAuthService.vb"
SRP = SERVICES / "SrpClient.vb"

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def code_only(text: str) -> str:
    """Drops comment lines and trailing comments, so a source that *names* the
    thing it avoids is not read as using it."""
    kept = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("'"):
            continue
        marker = line.find(" '")
        if marker >= 0:
            line = line[:marker]
        kept.append(line)
    return "\n".join(kept)


def main() -> int:
    missing = [p for p in (AUTH, SRP) if not p.is_file()]
    if missing:
        for path in missing:
            print(f"ERROR: missing file: {path}", file=sys.stderr)
        return 2

    auth = AUTH.read_text(encoding="utf-8")
    auth_code = code_only(auth)
    srp = SRP.read_text(encoding="utf-8")

    check(
        "HashPasswordVersion3(" not in auth_code,
        "the sign-in no longer calls the version-3 derivation directly",
        "One derivation for two versions is the current truth, not a promise; the "
        "call has to go through the version so the next reader sees which is used.",
    )

    check(
        "Friend Shared Function HashPassword(version As Integer" in srp,
        "SrpClient offers a version-aware derivation that returns Nothing for an unknown version",
        "A version the app cannot derive must be a value it can test, not an "
        "exception it might swallow.",
    )

    check(
        re.search(r'TryInt\([^)]*"Version"', auth_code) is not None,
        "the sign-in reads the SRP version from /auth/info",
        "An unread field is an assumption nobody wrote down.",
    )

    check(
        re.search(r'TryObject\([^)]*"Challenge"', auth_code) is not None
        or re.search(r'TryString\([^)]*"Challenge"', auth_code) is not None,
        "the sign-in reads the challenge field from /auth/info",
        "Proton answers a proof-of-work requirement and a wrong password the same "
        "way unless the client asks which it was.",
    )

    check(
        "does not know SRP version" in auth_code and "cannot solve" in auth_code,
        "an unknown version and an unsolved challenge each refuse by name",
        "The user has to be told which of the two stopped the sign-in, or the "
        "message is indistinguishable from a rejected password.",
    )

    check(
        "Return ApiResult(Of ProtonSession).Failure" in auth_code,
        "both refusals stop the attempt rather than continuing",
        "A refusal that continues still sends a proof, which is the failure mode "
        "the whole seam exists to prevent.",
    )

    print("authshape: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
