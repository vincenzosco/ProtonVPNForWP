"""Catalogue failure paths.

`ServerCatalog.LoadAsync`'s live branch is the only one that runs when Proton
answers, and it is the only place that method can throw: it parses and caches a
response that came off the network (`ParseLogicals`, `live.Value.Stringify()`,
`_settings.Save`). Both it and the two view models that call it are awaited from
bare `Async Sub` page handlers -- `HomePage.OnNavigatedTo` and
`ServersPage.OnNavigatedTo` -- where an escaping exception ends the process, and
that branch has never run because no sign-in has ever reached Proton from this
project. `App.OnUnhandledException` does not set `Handled`, so the log line is all
the evidence there would be.

The contract these three methods are checked against is the one their own doc
comments already state: the load either returns a usable list or a failure
result, and it never throws. A guard nobody checks gets deleted, so each one is
asserted here in the style of `tools/startup_check.py`.

A fourth rule matches the compiler: VB12 rejects `Await` inside a `Catch` block
(BC36943), so the fallback has to happen after the `End Try`. The first version of
this fix put it in the `Catch` and passed the three substring rules above while
`vbc` refused the line -- which is why the rule exists rather than a comment.

Reverting any of the four must make this script fail.
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CATALOG = ROOT / "Proton VPN WP/Proton VPN WP/Services/ServerCatalog.vb"
HOME = ROOT / "Proton VPN WP/Proton VPN WP/ViewModels/HomeViewModel.vb"
SERVERS = ROOT / "Proton VPN WP/Proton VPN WP/ViewModels/ServersViewModel.vb"

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def method_body(text: str, start_marker: str, end_marker: str = "\n        End Function") -> str:
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    if end < 0:
        return ""
    return text[start:end]


def main() -> int:
    for path in (CATALOG, HOME, SERVERS):
        if not path.is_file():
            print(f"ERROR: missing file: {path}", file=sys.stderr)
            return 2

    load = method_body(CATALOG.read_text(encoding="utf-8"),
                       "Friend Async Function LoadAsync(")
    home = method_body(HOME.read_text(encoding="utf-8"),
                       "Friend Async Function InitializeAsync(")
    servers = method_body(SERVERS.read_text(encoding="utf-8"),
                          "Friend Async Function LoadAsync(")

    check(
        load != ""
        and "Catch ex As Exception" in load
        and "Return Await LoadBundledAsync(" in load
        and "Await " in load
        and load.find("Await ") > load.find("Try"),
        "the catalogue load degrades instead of throwing",
        "Its live branch parses and caches a network response. A throw there "
        "escapes into the page handler that awaited it, where the process ends.",
    )

    check(
        home != ""
        and "Catch ex As Exception" in home
        and "NoticeLine =" in home
        and "ApiResult(Of IList(Of ProtonLogical)).Failure(" in home,
        "the dashboard reports a failed load instead of propagating it",
        "HomeViewModel.InitializeAsync is awaited from HomePage.OnNavigatedTo, "
        "a bare Async Sub with no handler of its own.",
    )

    check(
        servers != ""
        and "Catch ex As Exception" in servers
        and "StatusMessage =" in servers
        and "ApiResult(Of IList(Of ProtonLogical)).Failure(" in servers,
        "the server browser reports a failed load instead of propagating it",
        "ServersViewModel.LoadAsync is awaited from ServersPage.OnNavigatedTo, "
        "a bare Async Sub with no handler of its own.",
    )

    offenders = []
    for name, body in (("ServerCatalog.LoadAsync", load),
                       ("HomeViewModel.InitializeAsync", home),
                       ("ServersViewModel.LoadAsync", servers)):
        start = body.find("Catch ex As Exception")
        end = body.find("End Try", start) if start >= 0 else -1
        if start >= 0 and end >= 0 and "Await " in body[start:end]:
            offenders.append(name)
    check(
        not offenders,
        "no Await sits inside a Catch block",
        "VB12 rejects it (BC36943), so the fallback has to run after the End Try: "
        "found in " + ", ".join(offenders) if offenders else None,
    )

    print("catalogue failure paths: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
