"""Sign-in invariants.

The emulator log of 2026-10-09 19:00 shows thirteen `/auth/info` requests inside
six seconds with a single sign-in form on screen: a press of Sign in left the
button armed for the whole round trip, because `IsBusy` flips inside
`SignInAsync` -- after the page's own pre-await `UpdateVisualState` -- so every
extra press started another attempt. Every attempt shares `_pendingUid`,
`_pendingServerProof`, `_api.UserId` and `_api.AccessToken` with the others, so
an overlap with two-factor enabled can answer the typed code against the wrong
SRP exchange and fail as "Proton did not prove it knows your password".

Three guards stop that. A guard nobody checks is a guard that gets deleted, so
each one is asserted here, in the style of `tools/startup_check.py`:

  1. `LoginViewModel.SignInAsync` refuses to start while an attempt is in flight,
     keyed on its own field and not on `IsBusy`: the page clears `IsBusy` in
     `OnNavigatedTo`, which would re-arm a second attempt over the first one's
     pending state.
  2. `LoginPage.OnSignInClick` ignores a second press, and arms and disarms its
     own flag around the attempt rather than after it.
  3. `LoginPage.UpdateVisualState` keeps the sign-in button and the way out of the
     page disarmed while that flag is up.

Reverting any of the three must make this script fail.
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VIEW_MODEL = ROOT / "Proton VPN WP/Proton VPN WP/ViewModels/LoginViewModel.vb"
PAGE = ROOT / "Proton VPN WP/Proton VPN WP/Views/LoginPage.xaml.vb"

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def method_body(text: str, start_marker: str, end_marker: str) -> str:
    """The text from start_marker to the next end_marker after it, or ""."""
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    if end < 0:
        return ""
    return text[start:end]


def first_await(body: str) -> int:
    return body.find("Await ")


def main() -> int:
    for path in (VIEW_MODEL, PAGE):
        if not path.is_file():
            print(f"ERROR: missing file: {path}", file=sys.stderr)
            return 2

    view_model = VIEW_MODEL.read_text(encoding="utf-8")
    page = PAGE.read_text(encoding="utf-8")

    sign_in = method_body(view_model, "Friend Async Function SignInAsync(", "\n        End Function")
    click = method_body(page, "Private Async Sub OnSignInClick(", "\n        End Sub")
    visual = method_body(page, "Private Sub UpdateVisualState()", "\n        End Sub")

    refusal = "If _attemptInFlight Then Return False"
    raised = "_attemptInFlight = True"
    check(
        sign_in != ""
        and refusal in sign_in
        and sign_in.find(refusal) < sign_in.find(raised) < first_await(sign_in)
        and "_attemptInFlight = False" in sign_in[first_await(sign_in):],
        "the view model refuses a second attempt while one is in flight",
        "Without this, each extra press runs its own /auth/info and its own SRP "
        "exchange, all of them writing the same pending second-factor UID. The "
        "refusal must not key on IsBusy, which the page clears on navigation.",
    )

    armed = "_signingIn = True"
    disarmed = "_signingIn = False"
    check(
        click != ""
        and "If _signingIn Then Return" in click
        and armed in click
        and first_await(click) > click.find(armed)
        and click.find(disarmed) > first_await(click),
        "the page ignores a press while one is in flight, and disarms itself after",
        "The flag has to be raised before the await and lowered after it: a press "
        "arriving while the request is out is the whole defect.",
    )

    lines = visual.splitlines()
    sign_in_line = next((line for line in lines if "SignInButton.IsEnabled" in line), "")
    offline_line = next((line for line in lines if "OfflineButton.IsEnabled" in line), "")
    check(
        "_signingIn" in sign_in_line,
        "the button stays disarmed for the whole attempt",
        "UpdateVisualState runs before SignInAsync sets IsBusy, so a button keyed "
        "on IsBusy alone is re-armed for the entire round trip.",
    )
    check(
        "_signingIn" in offline_line,
        "the way out of the page is disarmed for the whole attempt",
        "Leaving mid-attempt builds a fresh LoginPage whose own flag is clear, "
        "which is the only route back to a second overlapping attempt.",
    )

    print("sign-in guard: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
