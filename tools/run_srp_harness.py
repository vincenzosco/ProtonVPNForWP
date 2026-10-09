#!/usr/bin/env python3
"""Compile the shipping VB crypto into a desktop exe and check it against Python.

The app's real source files (Sha512, Bcrypt, BlowfishTables, SrpClient,
CryptoBytes) are compiled here with `vbc` and driven with the vector emitted by
``tools/srp_reference.py vector``.  Because it is the same code, a green run is
evidence about what ships, not about a copy.

Usage:
    python tools/run_srp_harness.py
"""
from __future__ import annotations

import glob
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_SERVICES = HERE.parent / "Proton VPN WP" / "Proton VPN WP" / "Services"
OUT = HERE / "vb_srp_harness" / "out"

SOURCE_FILES = ["Sha512.vb", "BlowfishTables.vb", "Bcrypt.vb", "SrpClient.vb", "CryptoBytes.vb"]


def find_vbc() -> str:
    candidates = [
        r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
        r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\vbc.exe",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    raise SystemExit("ERROR: vbc.exe (.NET Framework v4.0.30319) not found")


def build() -> str:
    vbc = find_vbc()
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / "srp_harness.exe"

    sources = [str(APP_SERVICES / f) for f in SOURCE_FILES]
    missing = [s for s in sources if not os.path.exists(s)]
    if missing:
        raise SystemExit("ERROR: missing source files: " + ", ".join(missing))
    sources.append(str(HERE / "vb_srp_harness" / "Program.vb"))

    cmd = [
        vbc,
        "/nologo",
        "/removeintchecks+",  # the crypto code relies on unchecked arithmetic
        "/optionstrict+",
        "/optionexplicit+",
        "/target:exe",
        f"/out:{exe}",
        "/reference:System.Numerics.dll",
        *sources,
    ]
    print("compiling:", " ".join(cmd[:6]), "...")
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(f"vbc failed with exit code {result.returncode}")
    if result.stdout.strip():
        print(result.stdout.strip())
    return str(exe)


def main() -> int:
    vector_path = HERE / "out" / "srp_vector.json"
    if not os.path.exists(vector_path):
        raise SystemExit("ERROR: run `python tools/srp_reference.py vector` first")

    import hashlib

    with open(vector_path, encoding="utf-8") as fh:
        vector = json.load(fh)

    exe = build()

    proc = subprocess.run(
        [
            exe,
            _b64_hex(vector["modulus_b64"]),
            _b64_hex(vector["server_ephemeral_b64"]),
            vector["password"],
            vector["salt_b64"],
            vector["client_secret_le_hex"],
        ],
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        print(proc.stdout)
        print(proc.stderr)
        raise SystemExit(f"harness exited {proc.returncode}")

    got = {}
    for line in proc.stdout.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            got[k.strip()] = v.strip()

    expected = {
        "bcrypt_string": vector["bcrypt_string"],
        "encoded_salt": vector["bcrypt_string"][7:29],
        "hashed_password": vector["hashed_password_hex"],
        "client_ephemeral": vector["client_ephemeral_hex"],
        "client_proof": vector["client_proof_hex"],
        "server_proof": vector["server_proof_hex"],
        "shared_session": vector["shared_session_hex"],
        "sha512_empty": hashlib.sha512(b"").hexdigest(),
        "sha512_abc": hashlib.sha512(b"abc").hexdigest(),
    }

    ok = True
    for key, want in expected.items():
        have = got.get(key)
        match = have == want
        print(f"  [{'PASS' if match else 'FAIL'}] {key}")
        if not match:
            print(f"         got  {have}")
            print(f"         want {want}")
        ok = ok and match

    print("VB harness vs Python reference:", "OK" if ok else "FAILED")
    return 0 if ok else 1


def _b64_hex(b64: str) -> str:
    import base64

    return base64.b64decode(b64).hex()


if __name__ == "__main__":
    raise SystemExit(main())
