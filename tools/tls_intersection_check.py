"""Guard the necessity of the on-device TLS client.

Windows Phone 8.1 cannot negotiate any cipher suite Proton's API edge accepts:

  * Schannel on Windows 8.1 (the WP8.1 platform) offers, for an RSA server
    certificate, only ECDHE_RSA+CBC, DHE_RSA+CBC/GCM, and static-RSA+CBC/GCM
    -- and no TLS 1.3 at all (Microsoft, "TLS Cipher Suites in Windows 8.1").
  * Proton's edge, measured live here, accepts at TLS 1.2 only
    ECDHE_RSA+AES-GCM/CHACHA20 and, at TLS 1.3, the AEAD suites; every CBC and
    every non-ECDHE family is refused with a handshake alert.

So the two sets do not intersect and no request can be sent. This script pins
that fact: the device set is copied verbatim from the Microsoft table, the
Proton set is measured with OpenSSL, and the intersection is asserted empty.

If Proton ever accepts one of the device's suites, this script fails and the
whole plan (`2026-10-10-tls13-on-device`) becomes unnecessary -- which is the
point of the guard.

Usage:
    python tools/tls_intersection_check.py
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys

HOSTS = ("account.proton.me", "vpn-api.proton.me")

# The Windows 8.1 Schannel set, restricted to suites a client can use against
# an RSA server certificate (Proton presents one). Copied from
# https://learn.microsoft.com/en-us/windows/win32/secauthn/tls-cipher-suites-in-windows-8-1
# Names are OpenSSL spellings of the Microsoft suite strings.
DEVICE_SUITES_TLS12 = {
    # TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384 / _128_CBC_SHA256 / _256_CBC_SHA / _128_CBC_SHA
    "ECDHE-RSA-AES256-SHA384",
    "ECDHE-RSA-AES128-SHA256",
    "ECDHE-RSA-AES256-SHA",
    "ECDHE-RSA-AES128-SHA",
    # TLS_DHE_RSA_WITH_AES_256_GCM_SHA384 / _128_GCM_SHA256
    "DHE-RSA-AES256-GCM-SHA384",
    "DHE-RSA-AES128-GCM-SHA256",
    # TLS_DHE_RSA_WITH_AES_256_CBC_SHA / _128_CBC_SHA
    "DHE-RSA-AES256-SHA",
    "DHE-RSA-AES128-SHA",
    # TLS_RSA_WITH_AES_256_GCM_SHA384 / _128_GCM_SHA256
    "AES256-GCM-SHA384",
    "AES128-GCM-SHA256",
    # TLS_RSA_WITH_AES_256_CBC_SHA256 / _128_CBC_SHA256 / _256_CBC_SHA / _128_CBC_SHA
    "AES256-SHA256",
    "AES128-SHA256",
    "AES256-SHA",
    "AES128-SHA",
}

# Windows 8.1 has no TLS 1.3, so the device offers none of these.
TLS13_SUITES = (
    "TLS_AES_128_GCM_SHA256",
    "TLS_AES_256_GCM_SHA384",
    "TLS_CHACHA20_POLY1305_SHA256",
)

failures: list[str] = []


def check(condition: bool, title: str, why: str = None) -> bool:
    print(("[PASS] " if condition else "[FAIL] ") + title)
    if not condition:
        failures.append(title)
        if why:
            print("       " + why)
    return condition


def find_openssl() -> str:
    found = shutil.which("openssl")
    if not found:
        raise SystemExit("ERROR: openssl not found on PATH")
    return found


def client_hello(openssl: str, host: str, args: list[str]) -> str:
    """The s_client output (stderr merged) for one probe."""
    proc = subprocess.run(
        [openssl, "s_client", "-connect", f"{host}:443", "-servername", host, *args],
        input="", capture_output=True, text=True, timeout=30,
    )
    return proc.stdout + proc.stderr


def accepted(openssl: str, host: str, suite: str, version: int) -> bool:
    if version == 13:
        out = client_hello(openssl, host, ["-tls1_3", "-ciphersuites", suite, "-groups", "X25519", "-brief"])
    else:
        out = client_hello(openssl, host, ["-tls1_2", "-cipher", suite, "-brief"])
    if re.search(r"Ciphersuite:\s*" + re.escape(suite), out):
        return True
    return False


def main() -> int:
    openssl = find_openssl()
    print(f"openssl: {openssl}")

    for host in HOSTS:
        proton: set[str] = set()

        for suite in TLS13_SUITES:
            ok = accepted(openssl, host, suite, 13)
            print(f"  {host} tls1.3 {suite:32} {'accepts' if ok else 'refuses'}")
            if ok:
                proton.add(suite)

        for suite in sorted(DEVICE_SUITES_TLS12):
            ok = accepted(openssl, host, suite, 12)
            print(f"  {host} tls1.2 {suite:32} {'accepts' if ok else 'refuses'}")
            if ok:
                proton.add(suite)

        device = set(DEVICE_SUITES_TLS12)  # no TLS 1.3 on the platform
        common = device & proton
        check(
            not common,
            f"no device suite is accepted by {host}",
            "The intersection must stay empty, or the on-device client is not needed."
            + ("" if not common else " Accepted by both: " + ", ".join(sorted(common))),
        )
        check(
            bool(proton),
            f"{host} answers with modern suites at all",
            "If Proton accepted nothing, a transport problem would look like a TLS gap.",
        )

    print("intersection: " + ("EMPTY — the on-device TLS client is required" if not failures else "NOT EMPTY"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
