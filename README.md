# ProtonVPNForWP

A Proton VPN client for **Windows Phone 8.1**, written in VB.NET with the
Windows Runtime XAML stack and built with Visual Studio 2013 / MSBuild 12.0.

It signs in to your Proton account over Proton's own HTTP API (including the
SRP-6a login exchange, implemented from scratch), loads the live server list and
falls back to a bundled offline copy when there is no network, and then hands you
exactly the values Windows Phone's **built-in VPN client** needs.

<p align="left"><img src="Proton VPN WP/Proton VPN WP/Assets/ProtonLogo.png" width="96" alt="Proton VPN for Windows Phone"></p>

## Read this before installing

**Windows Phone 8.1 exposes no VPN API to third-party apps.** No app on this
platform can install a VPN profile or dial a tunnel. This app therefore:

- **does** authenticate you to Proton, fetch and browse the server catalogue
  (live or offline), and generate the IKEv2 / OpenVPN profile values;
- **does not** connect or disconnect anything. You add the profile once in
  *Settings → VPN → Add* using the values the app shows you, and from then on
  you connect from the system VPN list.

Anything in the app that looks like status is status: the dashboard reports the
network you are on and a TCP reachability probe to the chosen server. Neither is
a claim that a tunnel is up.

## Proton's API and Windows Phone 8.1's TLS

**The platform's TLS stack cannot reach Proton, so the app speaks TLS 1.3
itself.** The measurement is unchanged: Proton's API edge accepts, at TLS 1.2,
only `ECDHE_RSA_WITH_AES_256_GCM_SHA384`, `ECDHE_RSA_WITH_AES_128_GCM_SHA256` and
`ECDHE_RSA_WITH_CHACHA20_POLY1305` (measured on 2026-10-10: every other suite,
including all CBC ones, is refused with a handshake alert). Windows 8.1 and
Windows Phone 8.1 offer no ECDHE+AES-GCM suite for an RSA server certificate --
per [TLS Cipher Suites in Windows
8.1](https://learn.microsoft.com/en-us/windows/win32/secauthn/tls-cipher-suites-in-windows-8-1)
the only ECDHE-GCM suites it has are ECDSA ones -- and Proton presents an RSA
certificate. The intersection is empty, so Schannel is refused before a single
request is sent.

That is why the request is not left to `Windows.Web.Http`. The app carries its
own TLS 1.3 client over a raw socket: X25519, AES-128-GCM, SHA-256 with
HMAC/HKDF, RSA-PSS and PKCS#1 v1.5 verification and X.509 chain checking, all
hand-written because Windows Phone 8.1 also has no
`System.Security.Cryptography`. It offers exactly one suite
(`TLS_AES_128_GCM_SHA256`), one group (`x25519`), one signature scheme
(`rsa_pss_rsae_sha256`) and ALPN `http/1.1`.

The server is authenticated before any application byte is trusted: the
`CertificateVerify` signature is checked with the leaf key and the chain is
verified against the **pinned ISRG roots** (a root pin, not a leaf pin, so
Proton's certificate rotation does not brick the app). A tag mismatch, an
unexpected server choice, a missing proof or a chain error aborts the connection
-- there is no mode that continues anyway.

Measured on 2026-10-10 with `python tools/run_tls_harness.py`, which compiles the
shipping sources and drives the live edge: TLS 1.3 handshake in suite `0x1301`
and `HTTP 200` carrying Proton's SRP modulus from `account.proton.me`, while
`www.microsoft.com` is refused because the pinned anchors do not sign its chain.
The 8.1 emulator's own log shows the same exchange from the shipping sign-in
path: `HTTP 200 POST account.proton.me/api/auth/info` and then `HTTP 422 POST
account.proton.me/api/auth` -- Proton answering an account that does not exist --
while the platform's own stack, in the same diagnosis run, still fails with
`0x80090326`.

The emulator is also where the one bug no desktop check could have found
appeared. VB checks integer overflow by default, and the crypto here is written
to wrap, so the device build threw `OverflowException` inside the first
handshake until the project was compiled with integer checks removed; the
guards in `tools/` pass `/removeintchecks+` to the compiler themselves, so every
one of them was green either way.

Two things this does not free the app from. It still identifies itself with an
`x-pm-appversion` Proton accepts (`windows-vpn@5.0.0.0`; an unknown product is
answered with `400 Code 5002` before authentication is even considered). And it
breaks if Proton changes its API -- this is an unofficial client.

## What it does today

| Screen | Purpose |
|---|---|
| Sign in | Proton email + password, SRP-6a, with 2FA prompt; or continue offline |
| Home | Account state, network state, catalogue source, quick connect |
| Servers | Full catalogue with search, free-tier filter, live load and features |
| Server detail | Per-server status, load, feature flags, physical nodes, latency probe |
| Profile | The exact `Settings → VPN → Add` values, plus OpenVPN config export |
| Settings | Account, OpenVPN/IKEv2 credentials, protocol, catalogue refresh, diagnostics, full wipe |
| About | Version, honest limitations, links |

The bundled catalogue contains **64 logical servers** across free and Plus tiers,
generated by `tools/make_bundle.py`, so a fresh install with no network is still
usable.

## Build

Prerequisites: Visual Studio 2013 (or the MSBuild 12.0 toolchain plus the
Windows Phone 8.1 SDK) on Windows.

```bash
"/c/Program Files (x86)/MSBuild/12.0/Bin/MSBuild.exe" \
  "Proton VPN WP/Proton VPN WP/Proton VPN WP.vbproj" \
  //p:Configuration=Debug //p:Platform=AnyCPU //nologo //v:minimal
```

Build the `.vbproj` directly: the solution's platform is named `Any CPU`, which
the solution file rejects if you pass `AnyCPU`. Expected: exit 0, no `error BC`.
The package lands in `Proton VPN WP/Proton VPN WP/AppPackages/`.

Full instructions, including how to deploy to a device or the emulator, are in
[docs/BUILD.md](docs/BUILD.md).

## Verify

```bash
python tools/run_srp_harness.py     # crypto vectors: expect "VB harness vs Python reference: OK"
python tools/run_tls_crypto_harness.py   # SHA-256/HKDF/X25519/AES-GCM/RSA/X.509 vectors
python tools/run_tls_harness.py     # live TLS 1.3 handshake: expect "live TLS handshake: OK"
python tools/xaml_check.py          # resource keys and bindings: expect "XAML check OK"
python tools/ocr_check.py           # UI strings: expect no mismatches
```

`xaml_check.py` exists because the XAML compilers resolve neither
`{StaticResource}` keys nor `{Binding}` paths: a missing key throws when the page
loads, and a misspelled binding path renders a blank control. Both slip past a
green build.

The first one compiles the *shipping* crypto sources with the desktop compiler
and cross-checks SHA-512, bcrypt and the complete SRP-6a exchange against an
independent Python reference. It is the reason this app's authentication can be
trusted at all: Windows Phone 8.1 has no `System.Security.Cryptography`, so the
cryptography here is hand-written and is verified against a second
implementation on every change.

## Security posture

- Passwords are passed as arguments to the SRP code and are never assigned to a
  field that could reach a binding, a log line or a navigation parameter.
- Access tokens, refresh tokens and the OpenVPN/IKEv2 password are encrypted
  with `DataProtectionProvider` (`LOCAL=user`) before they are written to
  `ApplicationData`, and can be erased from Settings.
- TLS is terminated in-process by the client described above, pinned to the ISRG
  roots. Certificate validation is not delegated to the platform and there is no
  override, no "accept anyway" path and no configured exception.
- No analytics, no third-party endpoints, no telemetry. The only network traffic
  is to Proton's own API.
- Logs record method names, status codes and error types — never secrets,
  passwords, SRP proofs or private ephemerals.

## Repository layout

```
Proton VPN WP/Proton VPN WP/
  Models/       data types mirroring Proton's API and the app's own state
  Services/     crypto (Sha256, Sha512, Hmac, Hkdf, X25519, Aes, Gcm, RsaVerify,
                Asn1, X509), the TLS 1.3 client and its HTTP/1.1 layer, API
                client, SRP auth, catalogue, settings/vault, connectivity,
                VPN profile builder
  ViewModels/   one view model per page, no XAML knowledge
  Views/        the seven pages (XAML + code-behind)
  Data/         generated offline server catalogue
  Assets/       generated tile, store and splash images
tools/          Python: verification harnesses and asset generators
docs/           plan, architecture and build notes
.agents/skills/ maintainer skill for AI agents working on this repo
```

## Credits and legal

Protocol behaviour follows Proton's public client implementations
([github.com/ProtonVPN](https://github.com/ProtonVPN)), the go-srp SRP
implementation, and the SRP-6a specification; the bcrypt tables derive from the
Blowfish initialisation values published with Bruce Schneier's algorithm.

This is an **unofficial, independent client**. "Proton" and "Proton VPN" are
trademarks of Proton AG. It is not affiliated with, endorsed by, or supported by
Proton AG, and it will break if Proton changes its API.
