#!/usr/bin/env python3
"""Generate `Services/BlowfishTables.vb` from the hexadecimal digits of pi.

Blowfish's P-array (18 words) and four S-boxes (4 x 256 words) are defined as the
fractional hexadecimal expansion of pi.  There are 1042 constants; typing them is
an invitation to a silent typo, so they are derived here and committed.

The generator asserts the well-known first word (0x243F6A88) and a couple of other
published values, so a precision mistake fails loudly instead of shipping bad keys.

Usage:
    python tools/gen_blowfish.py
"""
from __future__ import annotations

import os
from pathlib import Path

from mpmath import mp

WORDS = 18 + 4 * 256  # P-array + S-boxes
HEX_DIGITS = WORDS * 8


MASK = 0xFFFFFFFF


def cyclic_word(data: bytes, index: int) -> int:
    """32-bit big-endian word cycling through `data`, as Blowfish's key schedule does."""
    n = len(data)
    w = 0
    for k in range(4):
        w = (w << 8) | data[(index * 4 + k) % n]
    return w


def _encipher(p: list[int], s: list[list[int]], l: int, r: int):
    def f(x: int) -> int:
        y = (s[0][(x >> 24) & 0xFF] + s[1][(x >> 16) & 0xFF]) & MASK
        y ^= s[2][(x >> 8) & 0xFF]
        return (y + s[3][x & 0xFF]) & MASK

    for i in range(16):
        l ^= p[i]
        r ^= f(l)
        l, r = r, l
    l, r = r, l
    r ^= p[16]
    l ^= p[17]
    return l & MASK, r & MASK


def _stream2word(data: bytes, index: int):
    """OpenBSD `Blowfish_stream2word`: 4 bytes big-endian, index advances and wraps."""
    n = len(data)
    w = 0
    j = index
    for _ in range(4):
        if j >= n:
            j = 0
        w = (w << 8) | data[j]
        j += 1
    return w, j


def _expand_key(p: list[int], s: list[list[int]], key: bytes, data: bytes | None):
    """OpenBSD `Blowfish_expand0state` / `Blowfish_expandstate`.

    The salt stream index is *continuous* across the P-array and all four
    S-boxes -- resetting it per position silently produces a wrong hash.
    """
    for i in range(18):
        p[i] ^= cyclic_word(key, i)
    l = r = 0
    j = 0
    for i in range(0, 18, 2):
        if data is not None:
            w, j = _stream2word(data, j)
            l ^= w
            w, j = _stream2word(data, j)
            r ^= w
        l, r = _encipher(p, s, l, r)
        p[i], p[i + 1] = l, r
    for box in s:
        for i in range(0, 256, 2):
            if data is not None:
                w, j = _stream2word(data, j)
                l ^= w
                w, j = _stream2word(data, j)
                r ^= w
            l, r = _encipher(p, s, l, r)
            box[i], box[i + 1] = l, r


def bcrypt_raw(password: bytes, salt: bytes, cost: int, words: list[int]) -> bytes:
    """Reference bcrypt using `words` as the pi-derived tables. Returns 24 raw bytes."""
    p = list(words[:18])
    s = [list(words[18 + 256 * i : 18 + 256 * (i + 1)]) for i in range(4)]
    key = password + b"\x00"

    # OpenBSD: Blowfish_expandstate(c, salt, key), then expand0state(key/salt).
    _expand_key(p, s, key, salt)
    for _ in range(1 << cost):
        _expand_key(p, s, key, None)
        _expand_key(p, s, salt, None)

    magic = b"OrpheanBeholderScryDoubt"
    cipher = [int.from_bytes(magic[i : i + 4], "big") for i in range(0, 24, 4)]
    for _ in range(64):
        for i in range(0, 6, 2):
            cipher[i], cipher[i + 1] = _encipher(p, s, cipher[i], cipher[i + 1])
    return b"".join(c.to_bytes(4, "big") for c in cipher)


def verify_bcrypt(words: list[int]) -> bool:
    """Prove the generated tables by matching Python's `bcrypt` module."""
    import bcrypt as _lib

    ok = True
    cases = [
        (b"abc123", bytes(range(16))),
        (b"", bytes(range(16))),
        (b"Password\nabc!!~~", b"\x00" * 16),
        (b"p@ssw0rd with spaces", bytes.fromhex("deadbeefcafebabe0011223344556677")),
    ]
    for password, salt in cases:
        raw = bcrypt_raw(password, salt, 4, words)[:23]
        ref = _lib.hashpw(password, b"$2b$04$" + _bcrypt_encode(salt))
        want = ref[29 : 29 + 31].decode("ascii")
        got = _b64_31(raw)
        match = got == want
        print(f"  [{'PASS' if match else 'FAIL'}] bcrypt tables, pw={password[:12]!r}")
        if not match:
            print(f"         got  {got}\n         want {want}")
        ok = ok and match
    return ok


def _bcrypt_encode(data: bytes) -> bytes:
    alpha = b"./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
    out = bytearray()
    for i in range(0, len(data), 3):
        chunk = data[i : i + 3]
        n = 0
        for j, b in enumerate(chunk):
            n |= b << (16 - 8 * j)
        for k in range({3: 4, 2: 3, 1: 2}[len(chunk)]):
            out.append(alpha[(n >> (18 - 6 * k)) & 0x3F])
    return bytes(out)


def _b64_31(data: bytes) -> str:
    return _bcrypt_encode(data).decode("ascii")


def pi_hex_words(count: int) -> list[int]:
    """First `count` 32-bit words of the fractional hex expansion of pi."""
    # decimal digits needed to represent `count*8` hex digits safely.
    mp.dps = int(count * 8 * 1.20412) + 50
    frac = mp.pi - 3
    scaled = int(mp.floor(frac * mp.mpf(16) ** (count * 8)))
    digits = format(scaled, "x").rjust(count * 8, "0")
    assert len(digits) == count * 8, (len(digits), count * 8)
    return [int(digits[i : i + 8], 16) for i in range(0, len(digits), 8)]


def main() -> int:
    words = pi_hex_words(WORDS)

    # Published Blowfish constants -- fail loudly if the derivation is wrong.
    assert words[0] == 0x243F6A88, hex(words[0])
    assert words[1] == 0x85A308D3, hex(words[1])
    assert words[2] == 0x13198A2E, hex(words[2])
    assert words[17] == 0x8979FB1B, hex(words[17])
    assert words[18] == 0xD1310BA6, hex(words[18])  # first S-box word
    assert words[-1] == 0x3AC372E6, hex(words[-1])  # last word of the last S-box

    print("verifying the generated tables against python-bcrypt...")
    if not verify_bcrypt(words):
        raise SystemExit("ERROR: generated Blowfish tables do not reproduce bcrypt")

    here = Path(__file__).resolve().parent
    out_path = here.parent / "Proton VPN WP" / "Proton VPN WP" / "Services" / "BlowfishTables.vb"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    lines: list[str] = []
    lines.append("' <auto-generated>")
    lines.append("'     Generated by tools/gen_blowfish.py from the hexadecimal digits of pi.")
    lines.append("'     Blowfish initial P-array and S-boxes. Do not edit by hand.")
    lines.append("' </auto-generated>")
    lines.append("")
    lines.append("Namespace Crypto")
    lines.append("    Friend NotInheritable Class BlowfishTables")
    lines.append("        Private Sub New()")
    lines.append("        End Sub")
    lines.append("")

    lines.extend(_emit_array("P0", words[:18]))
    lines.append("")
    for box in range(4):
        chunk = words[18 + box * 256 : 18 + (box + 1) * 256]
        lines.extend(_emit_array(f"S{box}", chunk))
        lines.append("")

    lines.append("    End Class")
    lines.append("End Namespace")
    lines.append("")

    # newline="" stops Python translating our explicit \r\n into \r\r\n.
    with open(out_path, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write("\r\n".join(lines) + "\r\n")
    print(f"wrote {out_path} ({WORDS} words)")
    return 0


def _emit_array(name: str, ws: list[int]) -> list[str]:
    """Emit one array field.

    VB's implicit line continuation needs every row to end with a comma, and the
    closing brace must sit on the same line as the final element -- a trailing
    comma before `}` makes the compiler expect another expression.
    """
    out = [f"        Friend Shared ReadOnly {name} As UInteger() = {{"]
    parts = [f"&H{w:08X}UI" for w in ws]
    rows = [parts[i : i + 6] for i in range(0, len(parts), 6)]
    for index, row in enumerate(rows):
        last = index == len(rows) - 1
        out.append("            " + ", ".join(row) + ("}" if last else ","))
    return out


if __name__ == "__main__":
    raise SystemExit(main())
