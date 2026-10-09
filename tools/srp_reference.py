#!/usr/bin/env python3
"""Python reference implementation of Proton's SRP-6a (their `go-srp`).

This is the correctness oracle for the VB.NET port that ships inside the
Windows Phone 8.1 app.  It reproduces, faithfully:

  * ``expandHash``            -- SHA-512 with a 0..3 suffix byte, concatenated
  * Proton's bcrypt encoding  -- the "./A-Za-z0-9" alphabet, no padding
  * ``hashPasswordVersion3``  -- bcrypt("$2y$10$" + salt || "proton") then
                                 expandHash(ascii_hash || modulus)
  * the SRP-6a client and server, including the little-endian byte convention
    used by ``toInt``/``fromInt`` in go-srp.

Verification strategy (see the plan's Review Focus):

  ``python tools/srp_reference.py self-test``
      Runs Proton's own end-to-end check (``TestE2EFlow``): a client and a server
      exchange proofs over a real 2048-bit modulus and must agree on both the
      client proof and the server proof.  This proves the protocol math.

  ``python tools/srp_reference.py vector``
      Emits a deterministic vector (fixed client secret) as JSON.  The desktop
      VB harness feeds the *same* inputs to the *shipping* VB code and compares.

The ``TestSRPauth`` byte vector from go-srp is deliberately NOT used as the
oracle: its client secret comes from Go's ``math/rand`` seeded with 42 through a
``crypto/rand.Int`` io.Reader shim, which is not reproducible outside Go.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import sys

import bcrypt as _bcrypt

BIT_LENGTH = 2048
BYTE_LENGTH = BIT_LENGTH // 8  # 256

# bcrypt's own base64 alphabet, as used by Proton's `based64DotSlash`.
DOT_SLASH = "./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

# The 2048-bit modulus and matching server ephemeral published in go-srp's test
# suite (`TestSRPauth`).  Used only for the self-consistency round trip.
TEST_MODULUS_B64 = (
    "W2z5HBi8RvsfYzZTS7qBaUxxPhsfHJFZpu3Kd6s1JafNrCCH9rfvPLrfuqocxWPgWDH2R8neK7PkNvjxto9TStu"
    "Y5z7jAzWRvFWN9cQhAKkdWgy0JY6ywVn22+HFpF4cYesHrqFIKUPDMSSIlWjBVmEJZ/MusD44ZT29xcPrOqeZvwt"
    "CffKtGAIjLYPZIEbZKnDM1Dm3q2K/xS5h+xdhjnndhsrkwm9U9oyA2wxzSXFL+pdfj2fOdRwuR5nW0J2NFrq3kJj"
    "kRmpO/Genq1UW+TEknIWAb6VzJJJA244K/H8cnSx2+nSNZO3bbo6Ys228ruV9A8m6DhxmS+bihN3ttQ=="
)
TEST_SERVER_EPHEMERAL_B64 = (
    "l13IQSVFBEV0ZZREuRQ4ZgP6OpGiIfIjbSDYQG3Yp39FkT2B/k3n1ZhwqrAdy+qvPPFq/le0b7UDtayoX4aOTJ"
    "ihoRvifas8Hr3icd9nAHqd0TUBbkZkT6Iy6UpzmirCXQtEhvGQIdOLuwvy+vZWh24G2ahBM75dAqwkP961EJMh6"
    "7/I5PA5hJdQZjdPT5luCyVa7BS1d9ZdmuR0/VCjUOdJbYjgtIH7BQoZs+KacjhUN8gybu+fsycvTK3eC+9mCN2Y"
    "6GdsuCMuR3pFB0RF9eKae7cA6RbJfF1bjm0nNfWLXzgKguKBOeF3GEAsnCgK68q82/pq9etiUDizUlUBcA=="
)


# --------------------------------------------------------------------------- #
# Byte / big-integer conventions (go-srp treats numbers as little-endian)
# --------------------------------------------------------------------------- #
def to_int(data: bytes) -> int:
    """go-srp `toInt`: bytes are little-endian."""
    return int.from_bytes(data, "little")


def from_int(num: int, bit_length: int = BIT_LENGTH) -> bytes:
    """go-srp `fromInt`: produce a little-endian, zero padded, fixed width array."""
    return num.to_bytes(bit_length // 8, "little")


def expand_hash(data: bytes) -> bytes:
    """go-srp `expandHash`: SHA-512(data || i) for i in 0..3, concatenated."""
    return b"".join(hashlib.sha512(data + bytes([i])).digest() for i in range(4))


# --------------------------------------------------------------------------- #
# bcrypt with Proton's alphabet
# --------------------------------------------------------------------------- #
def bcrypt_b64_encode(data: bytes) -> str:
    """base64 with the bcrypt alphabet and no padding."""
    out = []
    for i in range(0, len(data), 3):
        chunk = data[i : i + 3]
        n = 0
        for j, b in enumerate(chunk):
            n |= b << (16 - 8 * j)
        chars = {3: 4, 2: 3, 1: 2}[len(chunk)]
        for k in range(chars):
            out.append(DOT_SLASH[(n >> (18 - 6 * k)) & 0x3F])
    return "".join(out)


def bcrypt_hash_string(password: bytes, encoded_salt: str) -> str:
    """Proton's `bcrypt.HashBytes(password, b"$2y$10$" + salt)`.

    Returns the full 60 character ``$2y$10$<salt><hash>`` string.  The hash bytes
    of ``$2a$``/``$2b$``/``$2y$`` are identical for ASCII passwords; Python's
    bcrypt only accepts ``$2b$``, so we relabel the prefix to match Proton.
    """
    raw = _bcrypt.hashpw(password, ("$2b$10$" + encoded_salt).encode("ascii"))
    text = raw.decode("ascii")
    assert text.startswith("$2b$"), text
    # "$2b$" is four characters; keep the rest ("10$<salt><hash>") intact.
    return "$2y$" + text[4:]


def hash_password_version3(password: bytes, salt: bytes, modulus: bytes) -> bytes:
    """go-srp `hashPasswordVersion3` (auth version 3 and 4)."""
    encoded_salt = bcrypt_b64_encode(salt + b"proton")
    crypted = bcrypt_hash_string(password, encoded_salt)
    return expand_hash(crypted.encode("ascii") + modulus)


# --------------------------------------------------------------------------- #
# SRP-6a
# --------------------------------------------------------------------------- #
def compute_multiplier(modulus: int, modulus_bytes: bytes, bit_length: int = BIT_LENGTH) -> int:
    """go-srp `computeMultiplier`."""
    k = to_int(expand_hash(from_int(2, bit_length) + modulus_bytes)) % modulus
    if k <= 1 or k >= modulus - 1:
        raise ValueError("SRP multiplier out of bounds")
    return k


class SrpClient:
    """go-srp `Auth` (client side)."""

    def __init__(self, modulus: bytes, server_ephemeral: bytes, hashed_password: bytes,
                 bit_length: int = BIT_LENGTH):
        self.modulus_bytes = modulus
        self.modulus = to_int(modulus)
        self.server_ephemeral = server_ephemeral
        self.hashed_password = hashed_password
        self.bit_length = bit_length

    def generate_proofs(self, secret: int):
        """go-srp `GenerateProofs`.  `secret` is the client random `a`."""
        n = self.modulus
        b_bytes = self.server_ephemeral
        b = to_int(b_bytes)

        if not (1 < b < n - 1):
            raise ValueError("SRP server ephemeral out of bounds")

        k = compute_multiplier(n, self.modulus_bytes, self.bit_length)
        x = to_int(self.hashed_password)

        a = secret
        client_ephemeral = from_int(pow(2, a, n), self.bit_length)
        a_int = to_int(client_ephemeral)

        u = to_int(expand_hash(client_ephemeral + b_bytes))
        if u == 0:
            raise ValueError("SRP scramble parameter is zero")

        base = (b - k * pow(2, x, n)) % n
        exponent = (u * x + a) % (n - 1)
        shared = pow(base, exponent, n)
        shared_session = from_int(shared, self.bit_length)

        client_proof = expand_hash(client_ephemeral + b_bytes + shared_session)
        server_proof = expand_hash(client_ephemeral + client_proof + shared_session)
        return {
            "client_ephemeral": client_ephemeral,
            "client_ephemeral_int": a_int,
            "client_proof": client_proof,
            "server_proof": server_proof,
            "shared_session": shared_session,
        }


class SrpServer:
    """The server half, mirroring go-srp's `server.go` + `TestE2EFlow`."""

    def __init__(self, modulus: bytes, verifier: int, bit_length: int = BIT_LENGTH):
        self.modulus_bytes = modulus
        self.modulus = to_int(modulus)
        self.verifier = verifier
        self.bit_length = bit_length
        self.server_secret = None
        self.client_ephemeral = None
        self.shared = None

    def generate_challenge(self, server_secret: int) -> bytes:
        n = self.modulus
        k = compute_multiplier(n, self.modulus_bytes, self.bit_length)
        self.server_secret = server_secret
        b = (k * self.verifier + pow(2, server_secret, n)) % n
        self.challenge = from_int(b, self.bit_length)
        return self.challenge

    def verify_proofs(self, client_ephemeral: bytes, client_proof: bytes):
        n = self.modulus
        a = to_int(client_ephemeral)
        if not (0 < a < n):
            raise ValueError("client ephemeral out of bounds")
        u = to_int(expand_hash(client_ephemeral + self.challenge))
        s = pow(a * pow(self.verifier, u, n), self.server_secret, n)
        self.shared = from_int(s, self.bit_length)
        expected = expand_hash(client_ephemeral + self.challenge + self.shared)
        if expected != client_proof:
            raise ValueError("client proof mismatch")
        self.client_ephemeral = client_ephemeral
        return expand_hash(client_ephemeral + client_proof + self.shared)


def generate_verifier(hashed_password: bytes, modulus: bytes,
                      bit_length: int = BIT_LENGTH) -> int:
    """go-srp `GenerateVerifier`: v = g^x mod N."""
    return pow(2, to_int(hashed_password), to_int(modulus))


# --------------------------------------------------------------------------- #
# Commands
# --------------------------------------------------------------------------- #
def cmd_self_test() -> int:
    """Proton's TestE2EFlow: client and server must agree on every value."""
    modulus = base64.b64decode(TEST_MODULUS_B64)
    assert len(modulus) == BYTE_LENGTH, len(modulus)

    password = "Password\nabc!!~~ä\r\n".encode("utf-8")
    salt = bytes(range(10))  # deterministic stand-in for 10 random bytes

    hashed = hash_password_version3(password, salt, modulus)
    assert len(hashed) == 256, len(hashed)

    verifier = generate_verifier(hashed, modulus)

    server = SrpServer(modulus, verifier)
    server_secret = 0x1234567890ABCDEF1234567890ABCDEF
    challenge = server.generate_challenge(server_secret)

    client = SrpClient(modulus, challenge, hashed)
    client_secret = 0x0FEDCBA9876543210FEDCBA987654321
    proofs = client.generate_proofs(client_secret)

    server_proof = server.verify_proofs(proofs["client_ephemeral"], proofs["client_proof"])

    checks = [
        ("server proof matches client's expected server proof",
         server_proof == proofs["server_proof"]),
        ("shared session agrees", server.shared == proofs["shared_session"]),
        ("client ephemeral is 256 bytes", len(proofs["client_ephemeral"]) == 256),
        ("client proof is 256 bytes", len(proofs["client_proof"]) == 256),
        ("multiplier k in range",
         1 < compute_multiplier(to_int(modulus), modulus) < to_int(modulus) - 1),
    ]
    ok = True
    for name, passed in checks:
        print(f"  [{'PASS' if passed else 'FAIL'}] {name}")
        ok = ok and passed

    # A negative control: a wrong password must NOT authenticate.
    bad_hash = hash_password_version3(b"wrong-password", salt, modulus)
    bad_client = SrpClient(modulus, challenge, bad_hash)
    bad_proofs = bad_client.generate_proofs(client_secret)
    rejected = False
    try:
        server.verify_proofs(bad_proofs["client_ephemeral"], bad_proofs["client_proof"])
    except ValueError:
        rejected = True
    print(f"  [{'PASS' if rejected else 'FAIL'}] wrong password is rejected by the server")
    ok = ok and rejected

    print("self-test:", "OK" if ok else "FAILED")
    return 0 if ok else 1


def cmd_vector() -> int:
    """Deterministic vector for the VB harness cross-check."""
    modulus = base64.b64decode(TEST_MODULUS_B64)
    password = b"abc123"
    salt = base64.b64decode("yKlc5/CvObfoiw==")
    hashed = hash_password_version3(password, salt, modulus)

    client = SrpClient(modulus, base64.b64decode(TEST_SERVER_EPHEMERAL_B64), hashed)
    proofs = client.generate_proofs(0x00C0FFEE00C0FFEE00C0FFEE00C0FFEE)

    vector = {
        "bit_length": BIT_LENGTH,
        "modulus_b64": TEST_MODULUS_B64,
        "server_ephemeral_b64": TEST_SERVER_EPHEMERAL_B64,
        "password": password.decode(),
        "salt_b64": "yKlc5/CvObfoiw==",
        "client_secret_hex": "00C0FFEE00C0FFEE00C0FFEE00C0FFEE",
        # Little-endian, because go-srp's toInt reads numbers little-endian.
        "client_secret_le_hex": (0x00C0FFEE00C0FFEE00C0FFEE00C0FFEE).to_bytes(32, "little").hex(),
        "hashed_password_hex": hashed.hex(),
        "client_ephemeral_hex": proofs["client_ephemeral"].hex(),
        "client_proof_hex": proofs["client_proof"].hex(),
        "server_proof_hex": proofs["server_proof"].hex(),
        "shared_session_hex": proofs["shared_session"].hex(),
        "bcrypt_string": bcrypt_hash_string(
            password, bcrypt_b64_encode(salt + b"proton")),
    }
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, "srp_vector.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(vector, fh, indent=2)
    print(f"wrote {path}")
    print(f"  hashed_password = {hashed.hex()[:64]}...")
    print(f"  client_proof    = {proofs['client_proof'].hex()[:64]}...")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("command", choices=["self-test", "vector"])
    args = ap.parse_args()
    if args.command == "self-test":
        return cmd_self_test()
    return cmd_vector()


if __name__ == "__main__":
    raise SystemExit(main())
