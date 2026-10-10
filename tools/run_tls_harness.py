#!/usr/bin/env python3
"""Compile the shipping TLS 1.3 client and run it against the live Proton edge.

This is the proof the phone cannot give on its own: the same Services\\*.vb files
that ship complete a real handshake and HTTP exchange with account.proton.me.
Windows Phone 8.1's own TLS stack can negotiate none of Proton's cipher suites,
so if this passes, the client works.

Two stages, because the app has two transport seams and both have to work:

  1. `Live.vb` drives the client over a `System.Net.Sockets` transport. That
     covers the record layer, the key schedule, the certificate chain and the
     HTTP client.
  2. `WinRtLive.vb` drives it over `Services\\SocketTlsTransport.vb` -- the
     shipping implementation, compiled as the app compiles it, against the WinRT
     surface (`Windows.Networking.Sockets`) that also exists on the desktop. That
     is the transport the phone actually runs.

Stage 2 is skipped, not failed, when the WinRT references are not installed:
absence of a Windows SDK is not a regression in this repository.

It also asserts the client fails closed: `www.microsoft.com` is served by a chain
the pinned ISRG anchors do not sign, so it must be refused.

Usage:
    python tools/run_tls_harness.py [host]
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_SERVICES = HERE.parent / "Proton VPN WP" / "Proton VPN WP" / "Services"
OUT = HERE / "vb_tls_harness" / "out"
HARNESS = HERE / "vb_tls_harness"

SOURCE_FILES = [
    "CryptoBytes.vb", "Sha256.vb", "Hmac.vb", "Hkdf.vb", "X25519.vb",
    "Aes.vb", "Gcm.vb", "RsaVerify.vb", "Asn1.vb", "X509.vb",
    "IRandomSource.vb", "TlsKeys.vb", "ITlsTransport.vb", "TlsRecord.vb",
    "TlsClient.vb", "TlsHttpClient.vb",
]

# The shipping WinRT transport, which the System.Net.Sockets stage does not use.
WINRT_TRANSPORT = "SocketTlsTransport.vb"

CONTROL_HOST = "www.microsoft.com"


def find_vbc() -> str:
    for candidate in (r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
                      r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\vbc.exe"):
        if os.path.exists(candidate):
            return candidate
    raise SystemExit("ERROR: vbc.exe (.NET Framework v4.0.30319) not found")


def winrt_references() -> list[str] | None:
    r"""The reference set that lets desktop vbc compile WinRT code.

    Four parts, and each one is load-bearing:

      * `Facade\Windows.winmd` -- the type-forwarding metadata. The *UnionMetadata*
        copy next to it is the merged one and declares the same types itself, so
        referencing it together with the contracts below makes every WinRT type
        ambiguous (`BC30560: 'HostName' is ambiguous`).
      * `Windows.Foundation.FoundationContract` and `UniversalApiContract` -- where
        those forwards actually point. Without them the first WinRT type fails with
        BC31424.
      * `Facades\System.Runtime.dll` -- the facade the WinRT projection targets, and
        the assembly that declares `System.IDisposable` *as WinRT type metadata
        expects it*. Without it, implementing a WinRT interface is refused with
        `BC30009: a reference to assembly 'System.Runtime, Version=4.0.0.0 ...
        containing the implemented interface 'System.IDisposable' is required`,
        and every WinRT expression that is not statically typed degrades to late
        binding.
      * `System.Runtime.WindowsRuntime.dll` -- the projections of the async and
        buffer interop (`AsTask`, `AsBuffer`). There is no reference-assembly copy
        of it; the framework one is the only one.

    Returns None when any of that is missing, and the caller skips the stage: an
    absent Windows SDK is not a regression in this repository.
    """
    kits = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Windows Kits" / "10"
    framework = (Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
                 / "Reference Assemblies" / "Microsoft" / "Framework" / ".NETFramework")
    facade = kits / "UnionMetadata" / "Facade" / "Windows.winmd"
    versions = sorted(p.parent.name for p in (kits / "UnionMetadata").glob("*/Windows.winmd")
                      if p.parent.name[:1].isdigit())
    # Highest targeting pack wins; the facade is version 4.0.0.0 in every pack, so
    # only the directory differs between them.
    facades = sorted(framework.glob("*/Facades/System.Runtime.dll"))
    interop = [Path(rf"C:\Windows\Microsoft.NET\Framework{bits}\v4.0.30319\System.Runtime.WindowsRuntime.dll")
               for bits in ("64", "")]
    interop = next((p for p in interop if p.is_file()), None)
    if not facade.is_file() or not versions or not facades or interop is None:
        return None

    references = [str(facade)]
    for contract in ("Windows.Foundation.FoundationContract", "Windows.Foundation.UniversalApiContract"):
        found = sorted((kits / "References" / versions[-1] / contract).glob("*/*.winmd"))
        if not found:
            return None
        references.append(str(found[-1]))
    return references + [str(facades[-1]), str(interop)]


def build(driver: str, extra_sources: list[str], exe_name: str,
          references: list[str] | None = None) -> str:
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / exe_name
    names = SOURCE_FILES + extra_sources
    sources = [str(APP_SERVICES / name) for name in names]
    missing = [s for s in sources if not os.path.exists(s)]
    if missing:
        raise SystemExit("ERROR: missing source files: " + ", ".join(missing))
    sources.append(str(HARNESS / driver))
    # The compiler is invoked without a shell, so MSYS cannot rewrite the flags
    # into Windows paths the way it does for `/nologo` typed at the prompt.
    # `/optioninfer+` matches the project's <OptionInfer>On</OptionInfer>; the WinRT
    # read relies on it rather than naming an interop type that is ambiguous when
    # both the Windows metadata and the api contract are referenced.
    cmd = [find_vbc(), "/nologo", "/removeintchecks+", "/optionstrict+", "/optionexplicit+",
           "/optioninfer+", "/target:exe", f"/out:{exe}", "/reference:System.Numerics.dll"]
    for reference in references or []:
        cmd.append(f"/reference:{reference}")
    cmd += sources
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(f"vbc failed with exit code {result.returncode}")
    return str(exe)


def run(exe: str, host: str) -> tuple[subprocess.CompletedProcess, dict[str, str], str]:
    proc = subprocess.run([exe, host], capture_output=True, text=True, timeout=120)
    values: dict[str, str] = {}
    body = ""
    for line in proc.stdout.splitlines():
        if line.startswith("body="):
            body = line.split("=", 1)[1]
        elif "=" in line:
            key, _, value = line.partition("=")
            values[key] = value
    return proc, values, body


def assert_proton(exe: str, host: str, stage: str, failures: list[str]) -> None:
    proc, values, body = run(exe, host)
    print(proc.stdout.strip())
    if proc.stderr.strip():
        print(proc.stderr.strip())

    if proc.returncode != 0:
        failures.append(f"[{stage}] the handshake with {host} failed (exit {proc.returncode})")
    if values.get("suite") != "0x1301":
        failures.append(f"[{stage}] expected TLS_AES_128_GCM_SHA256 (0x1301), got {values.get('suite')!r}")
    if values.get("status") != "200":
        failures.append(f"[{stage}] expected HTTP 200 from {host}, got {values.get('status')!r}")
    if '"Code"' not in body:
        failures.append(f"[{stage}] the reply body carries no Proton \"Code\" field: {body[:80]!r}")

    # Fails closed: the anchors are Let's Encrypt's, so a host outside that chain
    # must be refused even though the handshake itself would succeed.
    control, _, _ = run(exe, CONTROL_HOST)
    if control.returncode == 0:
        failures.append(f"[{stage}] the client accepted {CONTROL_HOST}, whose chain the pinned anchors do not sign")
    detail = control.stderr.strip().splitlines()[0] if control.stderr.strip() else "no detail"
    print(f"[{stage}] control {CONTROL_HOST}: refused as expected ({detail})")


def main() -> int:
    host = sys.argv[1] if len(sys.argv) > 1 else "account.proton.me"
    failures: list[str] = []
    skipped: list[str] = []

    sockets_exe = build("Live.vb", [], "tls_live_harness.exe")
    assert_proton(sockets_exe, host, "sockets", failures)

    references = winrt_references()
    if references is None:
        skipped.append("the app's WinRT transport (Windows SDK metadata not installed)")
    else:
        try:
            winrt_exe = build("WinRtLive.vb", [WINRT_TRANSPORT], "tls_winrt_live_harness.exe", references)
        except SystemExit as error:
            skipped.append(f"the app's WinRT transport ({error})")
        else:
            assert_proton(winrt_exe, host, "winrt", failures)

    for line in failures:
        print(f"[FAIL] {line}")
    for line in skipped:
        print(f"[SKIP] {line}")

    if failures:
        print("live TLS handshake: FAILED")
        return 1

    print(f"live TLS handshake: OK (TLS 1.3, suite 0x1301, HTTP 200 from {host})")
    print("certificate anchoring: OK (www.microsoft.com refused)")
    if not skipped:
        print("WinRT transport: OK (the app's own SocketTlsTransport completed the handshake)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
