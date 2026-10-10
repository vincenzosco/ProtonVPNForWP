#!/usr/bin/env python3
"""Compile the shipping TLS crypto into a desktop exe and check it against Python.

The app's real source files are compiled with `vbc` and driven by
``tools/vb_tls_harness/Program.vb``. Because it is the same code, a green run is
evidence about what ships, not about a copy (the pattern of
``tools/run_srp_harness.py``).

Usage:
    python tools/run_tls_crypto_harness.py
"""
from __future__ import annotations

import base64
import hashlib
import hmac as hmaclib
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_SERVICES = HERE.parent / "Proton VPN WP" / "Proton VPN WP" / "Services"
OUT = HERE / "vb_tls_harness" / "out"

# Grows as the crypto primitives are added.
SOURCE_FILES = ["CryptoBytes.vb", "Sha256.vb", "Hmac.vb", "Hkdf.vb", "X25519.vb", "Aes.vb", "Gcm.vb", "RsaVerify.vb", "TlsKeys.vb"]


def find_vbc() -> str:
    for c in (r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
              r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\vbc.exe"):
        if os.path.exists(c):
            return c
    raise SystemExit("ERROR: vbc.exe (.NET Framework v4.0.30319) not found")


def build() -> str:
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / "tls_crypto_harness.exe"
    sources = [str(APP_SERVICES / f) for f in SOURCE_FILES]
    missing = [s for s in sources if not os.path.exists(s)]
    if missing:
        raise SystemExit("ERROR: missing source files: " + ", ".join(missing))
    sources.append(str(HERE / "vb_tls_harness" / "Program.vb"))
    cmd = [find_vbc(), "/nologo", "/removeintchecks+", "/optionstrict+", "/optionexplicit+",
           "/target:exe", f"/out:{exe}", "/reference:System.Numerics.dll", *sources]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(f"vbc failed with exit code {result.returncode}")
    if result.stdout.strip():
        print(result.stdout.strip())
    return str(exe)


def hkdf_sha256(ikm: bytes, salt: bytes, info: bytes, length: int) -> tuple[bytes, bytes]:
    prk = hmaclib.new(salt, ikm, hashlib.sha256).digest()
    okm, t, counter = b"", b"", 1
    while len(okm) < length:
        t = hmaclib.new(prk, t + info + bytes([counter]), hashlib.sha256).digest()
        okm += t
        counter += 1
    return prk, okm[:length]


def expected() -> dict[str, str]:
    key = b"\xbb" * 20
    prk, okm = hkdf_sha256(b"\x0b" * 22, bytes(range(13)), bytes(range(0xF0, 0xFA)), 42)
    return {
        "sha256_empty": hashlib.sha256(b"").hexdigest(),
        "sha256_abc": hashlib.sha256(b"abc").hexdigest(),
        "hmac_sha256": hmaclib.new(key, b"Hi There", hashlib.sha256).hexdigest(),
        "hkdf_prk": prk.hex(),
        "hkdf_okm": okm.hex(),
        # RFC 7748 section 5.2 and section 6.1, reproduced with the Python oracle
        # in tools/sdd evidence (see the ledger).
        "x25519_vec": "c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552",
        "x25519_pub": "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a",
        "x25519_shared": "4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742",
        # FIPS-197 C.1 and GCM Test Case 2, reproduced with pyca/cryptography.
        "aes_block": "69c4e0d86a7b0430d8cdb78070b4c55a",
        "gcm_sealed": "0388dace60b6a392f328c2b971b2fe78ab6e47d42cec13bdf53a67b21257bddf",
    }


def parse(stdout: str) -> dict[str, str]:
    got = {}
    for line in stdout.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            got[k.strip()] = v.strip()
    return got


def rsa_vectors() -> list[str]:
    """A fresh RSA key and signatures from pyca/cryptography, or skip."""
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.asymmetric import padding, rsa

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    message = b"rsa-test-message"
    modulus = key.public_key().public_numbers().n.to_bytes(256, "big")
    pss = key.sign(message, padding.PSS(mgf=padding.MGF1(hashes.SHA256()), salt_length=32), hashes.SHA256())
    pkcs1 = key.sign(message, padding.PKCS1v15(), hashes.SHA256())
    return ["rsa", modulus.hex(), "010001", message.hex(), pss.hex(), pkcs1.hex()]


def hkdf_expand(prk: bytes, info: bytes, length: int) -> bytes:
    okm, t, counter = b"", b"", 1
    while len(okm) < length:
        t = hmaclib.new(prk, t + info + bytes([counter]), hashlib.sha256).digest()
        okm += t
        counter += 1
    return okm[:length]


def expand_label(secret: bytes, label: bytes, context: bytes, length: int) -> bytes:
    full = b"tls13 " + label
    info = length.to_bytes(2, "big") + bytes([len(full)]) + full + bytes([len(context)]) + context
    return hkdf_expand(secret, info, length)


def tls13_schedule(shared: bytes, th: bytes) -> dict[str, bytes]:
    zero, empty = bytes(32), hashlib.sha256(b"").digest()
    early = hmaclib.new(zero, zero, hashlib.sha256).digest()
    handshake = hmaclib.new(expand_label(early, b"derived", empty, 32), shared, hashlib.sha256).digest()
    master = hmaclib.new(expand_label(handshake, b"derived", empty, 32), zero, hashlib.sha256).digest()
    c_hs = expand_label(handshake, b"c hs traffic", th, 32)
    fin = expand_label(c_hs, b"finished", b"", 32)
    return {
        "early": early, "handshake": handshake, "master": master,
        "c_hs": c_hs,
        "s_hs": expand_label(handshake, b"s hs traffic", th, 32),
        "c_ap": expand_label(master, b"c ap traffic", th, 32),
        "s_ap": expand_label(master, b"s ap traffic", th, 32),
        "key": expand_label(c_hs, b"key", b"", 16),
        "iv": expand_label(c_hs, b"iv", b"", 12),
        "finished": hmaclib.new(fin, th, hashlib.sha256).digest(),
    }


def main() -> int:
    exe = build()
    proc = subprocess.run([exe], capture_output=True, text=True)
    if proc.returncode != 0:
        print(proc.stdout)
        print(proc.stderr)
        raise SystemExit(f"harness exited {proc.returncode}")

    got = parse(proc.stdout)
    ok = True
    for key, want in expected().items():
        have = got.get(key)
        match = have == want
        print(f"  [{'PASS' if match else 'FAIL'}] {key}")
        if not match:
            print(f"         got  {have}")
            print(f"         want {want}")
        ok = ok and match

    try:
        rsa_proc = subprocess.run([exe, *rsa_vectors()], capture_output=True, text=True)
    except ImportError as exc:
        print(f"  [SKIP] RSA verification: pyca/cryptography unavailable ({exc})")
        ok = False
    else:
        if rsa_proc.returncode != 0:
            print(rsa_proc.stdout)
            print(rsa_proc.stderr)
            raise SystemExit(f"rsa harness exited {rsa_proc.returncode}")
        rsa_got = parse(rsa_proc.stdout)
        for key in ("pss_ok", "pkcs1_ok", "pss_reject_flip", "pkcs1_reject_msg"):
            match = rsa_got.get(key) == "1"
            print(f"  [{'PASS' if match else 'FAIL'}] {key}")
            if not match:
                print(f"         got  {rsa_got.get(key)}")
            ok = ok and match

    shared = bytes(range(1, 33))
    th = bytes(range(0x41, 0x61))
    keys_proc = subprocess.run([exe, "keys", shared.hex(), th.hex()], capture_output=True, text=True)
    if keys_proc.returncode != 0:
        print(keys_proc.stdout)
        print(keys_proc.stderr)
        raise SystemExit(f"keys harness exited {keys_proc.returncode}")
    keys_got = parse(keys_proc.stdout)
    for key, want in tls13_schedule(shared, th).items():
        have = keys_got.get(key)
        match = have == want.hex()
        print(f"  [{'PASS' if match else 'FAIL'}] tls13_{key}")
        if not match:
            print(f"         got  {have}")
            print(f"         want {want.hex()}")
        ok = ok and match

    print("TLS crypto harness vs Python reference:", "OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
