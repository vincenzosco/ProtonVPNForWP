"""A sign-in refusal names Proton's code, and never the account identifier.

Three properties, each one a line in the source rather than a behaviour a green
build would show:

  1. A refusal writes the code Proton answered with into the log. Without it a
     `HTTP 422` is one opaque status, and attributing it takes a debugger: the
     first report this work came from needed a Visual Studio Output window to be
     made at all.
  2. The account identifier stays out. Proton's own `Error` and `Details`
     strings quote the address back -- `"This email address does not exist.
     Please try again with a different email address."` was the answer to a
     probe -- so a log line built from them writes the account into a file the
     user can see and can share.
  3. The code is on the result type, because the caller has to branch on it:
     `Code 1001`/`1002` is a second factor rather than a failure, and a message
     string is not a place to read a protocol value from.

Reverting any of these must make this script fail.
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
SERVICES = APP / "Services"
API_RESULT = APP / "Models" / "ApiResult.vb"
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


def log_lines(text: str) -> list[str]:
    """Only the lines that actually write to the log, so a message built for the
    user is not mistaken for one written to the file."""
    return [line for line in text.splitlines()
            if "Log.Info(" in line or "Log.Warn(" in line]


def main() -> int:
    missing = [p for p in (API_RESULT, API, AUTH) if not p.is_file()]
    if missing:
        for path in missing:
            print(f"ERROR: missing file: {path}", file=sys.stderr)
        return 2

    api_result = API_RESULT.read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")
    auth = AUTH.read_text(encoding="utf-8")

    check(
        "Friend Property Code As Integer" in api_result,
        "ApiResult carries Proton's code for the caller to branch on",
        "A refusal that only carries a sentence cannot be branched on: Code 1001 "
        "is a second factor, and reading it out of the message text is how the "
        "continuation lost its payload.",
    )

    check(
        any("sign-in refused: code " in line and ".Code" in line
            for line in log_lines(auth)),
        "the sign-in refusal names Proton's code in the log",
        "The status alone does not distinguish a limited account from a wrong "
        "password from a refused handshake.",
    )

    for name, text in (("ProtonApiClient.vb", api), ("ProtonAuthService.vb", auth)):
        check(
            not any('"Details"' in line or 'TryString(parsed, "Error")' in line
                    for line in log_lines(text)),
            f"{name} logs no Error/Details text, which quotes the address",
            "Proton's Error string repeats the account identifiers it was sent; a "
            "log line built from it leaks them into a file the user shares.",
        )

    check(
        "Case 2028" in api and "limited this account" in api,
        "a limited account is described as such, not as a generic refusal",
        "2028 is what a real account answers with while Proton has limited it, "
        "and it carries the explanation and the appeal path. A message built only "
        "from the table must not reduce it to \"Proton rejected the request.\"",
    )

    print("refusal: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
