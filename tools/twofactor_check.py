"""A second-factor answer keeps the payload the continuation needs.

`Code 1001`/`1002` means "the sign-in is accepted so far, now send the code", and
the continuation is identified by the `UID` that same response carries. Four
properties, each a line in the source:

  1. The branch that recognises it reads the numeric code. It used to test
     `authResult.Message.Contains("1001")`, which is a protocol value read out of
     a sentence the app itself formats -- and which is what made the next point
     invisible.
  2. It does not read that sentence any more. A message is for the user.
  3. The parsed body travels with the result even when the code is not 1000.
     `ApiResult(Of T).Failure` used to drop it, so the payload the branch then
     reads was always `Nothing`.
  4. The `UID` is therefore read from a payload that is present on that path:
     without it the attempt cannot be continued at all, and the typed code is
     answered against no exchange.

Reverting any of these must make this script fail.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
SERVICES = APP / "Services"
API = SERVICES / "ProtonApiClient.vb"
AUTH = SERVICES / "ProtonAuthService.vb"

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def main() -> int:
    missing = [p for p in (API, AUTH) if not p.is_file()]
    if missing:
        for path in missing:
            print(f"ERROR: missing file: {path}", file=sys.stderr)
        return 2

    api = API.read_text(encoding="utf-8")
    auth = AUTH.read_text(encoding="utf-8")

    check(
        'Contains("1001")' not in auth and 'Contains("1002")' not in auth,
        "the second-factor branch reads the code, not a substring of a message",
        "The message is prose the app formats for the user; the code is the "
        "protocol value. Branching on the prose is what let the payload below "
        "be dropped without anything noticing.",
    )

    check(
        re.search(r"\.Code\s*=\s*1001|\.Code\s*=\s*1002", auth) is not None,
        "the branch compares the code against 1001 and 1002",
        "Both mean a second factor is required; the exchange already in flight "
        "is the one the typed code continues.",
    )

    check(
        re.search(r"Failure\([^\n]*parsed[^\n]*code\)", api) is not None,
        "a parsed body is attached to the result even when the code is not 1000",
        "The refusal and the payload are not alternatives: 1001 carries a UID "
        "and a proof this client still has to verify.",
    )

    check(
        "Failure(message As String, value As T, code As Integer)" in
        (APP / "Models" / "ApiResult.vb").read_text(encoding="utf-8"),
        "ApiResult(Of T) has a factory that refuses and still carries a value",
        "Without it the only way to return a refusal is to hand back Nothing, "
        "which is how the UID was lost.",
    )

    check(
        "BeginTwoFactor(authResult.Value" in auth,
        "the UID is read from a payload that is present on the 1001 path",
        "It is the only thing that identifies the exchange the code continues.",
    )

    print("twofactor: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
