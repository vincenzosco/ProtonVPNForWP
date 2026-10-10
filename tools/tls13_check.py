"""The on-device TLS 1.3 stack is really the one that reaches Proton.

The app ships a hand-written TLS client because the platform's own stack cannot
negotiate a suite Proton's edge accepts. That makes four properties load-bearing,
and each one is a line in the source rather than a behaviour a green build would
show:

  1. The API client uses it. `ProtonApiClient` must send through the app's own
     `TlsHttpClient` and must not go back to `Windows.Web.Http`, whose stack is
     the thing that cannot handshake. A silent revert here would compile, pass
     every other guard, and fail only on a device -- with the same
     "cannot connect" as before.
  2. The protocol parameters stay pinned. The client offers exactly
     `TLS_AES_128_GCM_SHA256` (0x1301), group `x25519` (0x001D) and
     `rsa_pss_rsae_sha256` (0x0804); widening the set is a security decision and
     has to be made deliberately, not by drift.
  3. No secret reaches a log line. The TLS sources handle traffic secrets, the
     ephemeral private key and the derived keys; `Services/Log.vb` accepts no
     credential by construction, and the new code must not reintroduce one.
  4. The socket stays raw. `SocketTlsTransport` must not pass a
     `SocketProtectionLevel`: that would hand the connection back to Schannel,
     which is the stack that cannot do this handshake, and would fail before
     `TlsClient` saw a byte.
  5. The project compiles the arithmetic unchecked. The crypto is written to
     wrap, and VB checks overflow by default; without
     `<RemoveIntegerChecks>true</RemoveIntegerChecks>` the handshake throws
     `OverflowException` on a real device. This guard exists because nothing
     else can see it: the desktop harness passes `/removeintchecks+` itself, so
     the whole suite is green either way. Only the emulator run of 2026-10-10
     caught it.

Reverting any of these must make this script fail.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "Proton VPN WP" / "Proton VPN WP"
SERVICES = APP / "Services"
API = SERVICES / "ProtonApiClient.vb"
TRANSPORT = SERVICES / "SocketTlsTransport.vb"
PROJECT = APP / "Proton VPN WP.vbproj"

TLS_SOURCES = [
    "TlsClient.vb", "TlsRecord.vb", "TlsKeys.vb", "TlsHttpClient.vb",
    "ITlsTransport.vb", "SocketTlsTransport.vb",
    "Hkdf.vb", "Gcm.vb", "X25519.vb", "Aes.vb", "RsaVerify.vb", "X509.vb",
]

SECRET_WORDS = ("secret", "private", "pass", "nonce", "key")

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def body(text: str, start_marker: str, end_marker: str) -> str:
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    return text[start:end] if end >= 0 else text[start:]


def log_lines(text: str) -> list[str]:
    return [line for line in text.splitlines() if "Log.Info(" in line or "Log.Warn(" in line]


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
    missing = [p for p in (API, TRANSPORT, PROJECT) if not p.is_file()]
    missing += [p for p in (SERVICES / n for n in TLS_SOURCES) if not p.is_file()]
    if missing:
        for path in missing:
            print(f"ERROR: missing file: {path}", file=sys.stderr)
        return 2

    api = API.read_text(encoding="utf-8")
    transport = TRANSPORT.read_text(encoding="utf-8")
    sources = {name: (SERVICES / name).read_text(encoding="utf-8") for name in TLS_SOURCES}
    client = sources["TlsClient.vb"]
    record = sources["TlsRecord.vb"]

    api_code = code_only(api)
    send = body(api_code, "Private Async Function SendAsync(", "\n        End Function")
    check(
        send != "" and "Tls.TlsHttpClient." in send,
        "the API client sends through the app's own TLS client",
        "Every Proton request goes through the handshake this app implements; "
        "Windows.Web.Http would be refused by Proton's edge.",
    )
    check(
        "Windows.Web.Http" not in api_code
        and "HttpRequestMessage" not in api_code
        and re.search(r"\bNew HttpClient\b", api_code) is None,
        "the API client no longer borrows the platform's HTTP stack",
        "Leaving the platform stack reachable is how a revert hides: the call "
        "site changes, the fallback stays.",
    )
    check(
        "Tls.SocketTlsTransport.ConnectAsync" in send,
        "the API client connects a raw socket for the handshake",
        "A protected socket would mean Schannel, which cannot negotiate with "
        "Proton at all.",
    )

    for name, value in (
        ("TLS_AES_128_GCM_SHA256 (0x1301)", "&H1301"),
        ("group x25519 (0x001D)", "&H1D"),
        ("rsa_pss_rsae_sha256 (0x0804)", "&H804"),
        ("TLS 1.3 (0x0304)", "&H304"),
    ):
        check(
            value in client,
            f"the client still offers exactly {name}",
            "The set is pinned on purpose: widening it is a security decision, "
            "not something a diff should do quietly.",
        )

    check(
        "http/1.1" in client and "Extension(&H10" in client,
        "the ClientHello still carries ALPN http/1.1",
        "Without ALPN Proton's edge has no protocol to answer with, and the "
        "handshake fails with an alert instead of an HTTP response.",
    )

    # The finished/client-application secrets are derived from the transcript
    # through the server's Finished; deriving them one message earlier still
    # handshakes and then fails every application record.
    after = body(client, "afterServerFinished", "\n        End Function")
    check(
        after != "" and after.count("afterServerFinished") >= 4,
        "the application secrets and the client Finished use the transcript through the server Finished",
        "RFC 8446 derives c ap traffic / s ap traffic and the client verify_data "
        "from ClientHello..server Finished; one message earlier authenticates the "
        "handshake and then fails every record after it.",
    )

    leaked = []
    for name, text in sources.items():
        for line in log_lines(text):
            if any(word in line.lower() for word in SECRET_WORDS):
                leaked.append(f"{name}: {line.strip()}")
    check(
        not leaked,
        "no TLS source logs a secret, a key or a nonce",
        "The stack handles traffic secrets and an ephemeral private key; not one "
        "of them belongs in a log line."
        + ("" if not leaked else " Offending line(s): " + " | ".join(leaked)),
    )

    check(
        re.search(r"ConnectAsync\([^)\n]*SocketProtectionLevel", transport) is None,
        "the socket transport never asks for a platform-protected socket",
        "StreamSocket.ConnectAsync with a protection level hands the connection "
        "to Schannel, which cannot complete this handshake.",
    )
    check(
        "InputStreamOptions.Partial" in transport,
        "the app's socket read returns as soon as bytes arrive",
        "TLS records do not align with read boundaries: waiting for a full "
        "buffer deadlocks against a server that waits for a reply.",
    )

    check(
        re.search(r"VerifyChain\(", client) is not None
        and "VerifyPssSha256(" in client,
        "the handshake verifies the server key and the chain before returning",
        "A handshake that completes without authenticating the server is a "
        "man-in-the-middle with extra steps.",
    )
    check(
        "ISRG" in sources["X509.vb"] or "Isrg" in sources["X509.vb"],
        "the chain is anchored to the pinned ISRG roots",
        "Pinning the leaf would brick the app on Proton's next certificate "
        "rotation; pinning the root does not.",
    )
    check(
        "AlertCloseNotify" in client and "AlertLevelWarning" in client,
        "a close_notify is the end of the stream, not a failure",
        "Proton answers a completed exchange with close_notify; treating it as "
        "an error throws away a response the app has already received.",
    )
    check(
        "Throw" in record and "IOException" in record,
        "the record layer fails closed on a failed authentication tag",
        "A record that does not authenticate is an error, never a record that "
        "is skipped.",
    )

    # Unchecked integer arithmetic. The crypto relies on wrapping; a checked
    # build throws OverflowException on the device during the first handshake.
    # The desktop harness cannot show this, because it passes /removeintchecks+
    # on its own command line whatever the project file says.
    project = PROJECT.read_text(encoding="utf-8-sig")
    check(
        "<RemoveIntegerChecks>true</RemoveIntegerChecks>" in project,
        "the project compiles with integer overflow checking removed",
        "The SHA/GHASH/X25519 arithmetic is meant to wrap; a checked build "
        "throws OverflowException inside the handshake on a real device, which "
        "is invisible to every harness because they pass the same switch "
        "directly to vbc.",
    )
    check(
        "<RemoveIntegerChecks>false</RemoveIntegerChecks>" not in project,
        "no configuration re-enables integer overflow checking",
        "One configuration is enough to ship the overflow: the emulator's "
        "proof build is the x86 one.",
    )

    print("tls13: " + ("OK" if not failures else f"FAILED ({len(failures)} problem(s))"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
