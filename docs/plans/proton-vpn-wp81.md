# Proton VPN for Windows Phone 8.1 — Implementation Plan

Plan file: `docs/plans/proton-vpn-wp81.md`
Branch: `main` (the implementation landed on `feature/proton-vpn-wp81`, which is now `main`)
Repo root: `C:\Users\Vincenzo\Documents\ProtonVPNForWP81`

## Goal

Deliver a complete, buildable, installable Proton VPN application for Windows
Phone 8.1 (VB.NET, WinRT XAML, VS2013 toolchain), plus:

- an **agent skill** to maintain and update the app,
- **OCR-based interface evaluation**,
- a **green MSBuild 12.0 build** producing an `.appx`,
- the Proton **logo** asset taken from `~/Downloads/proton.png.png`.

## Hard platform constraints (verified, not assumed)

These are facts measured on this machine, not opinions:

1. **WP8.1 has no public VPN API.** `grep -a "Vpn"` over every `*.winmd` in
   `Microsoft SDKs/Windows Phone/v8.1` returns **0 matches**. `Windows.Networking.Vpn`
   exists only in Windows Kits 10 (UWP). Therefore **no third-party WP8.1 app can
   open a VPN tunnel**. The tunnel is opened by the OS's **built-in VPN**
   (IKEv2 / L2TP / PPTP), configured from Settings or enterprise MDM.
2. **Toolchain present:** MSBuild 12.0 at
   `C:\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe`; Windows Phone SDK v8.1.
   The blank skeleton already builds to `.appx` (exit 0).
3. **SRP is feasible in VB.NET:** `System.Numerics.dll` and `System.Runtime.Numerics.dll`
   are in `Reference Assemblies\Microsoft\Framework\WindowsPhoneApp\v8.1\`,
   so `BigInteger` and HMAC/SHA-512 are available.
4. **Proton API is auth-gated:** `api.protonvpn.ch/vpn/logicals` public access was
   removed in 2025; logicals now live behind `vpn-api.proton.me` with auth headers.
   A bundled offline catalogue is therefore mandatory.

## Consequence for "functional"

The app is a **Proton VPN manager + profile generator**. It does all the work the
platform allows: authenticate to Proton (real SRP-6a), fetch/cache the server
catalogue, choose a server, obtain/enter IKEv2+OpenVPN credentials, generate a
ready-to-use IKEv2 profile and `.ovpn` text, measure reachability/latency, and
drive the user into the OS VPN settings where the tunnel is actually established.
The app clearly states this in the UI (no silent pretend-connect).

## Deliverables

### A. Application (`Proton VPN WP/Proton VPN WP/`, ns `Proton_VPN_WP`)

| Area | Files |
|---|---|
| Shell | `App.xaml(.vb)`, `MainPage.xaml(.vb)` |
| Views | `Views/LoginPage`, `HomePage`, `ServersPage`, `ServerDetailPage`, `ProfilePage`, `SettingsPage`, `AboutPage` (`.xaml` + `.vb`) |
| ViewModels | `ViewModels/ViewModelBase`, `LoginViewModel`, `HomeViewModel`, `ServersViewModel`, `ServerDetailViewModel`, `ProfileViewModel`, `SettingsViewModel` |
| Models | `Models/ProtonLogical`, `ProtonServer`, `ProtonSession`, `VpnProfile`, `AppSettings`, `ActionResult` |
| Services | `Services/ProtonApiClient`, `ProtonAuthService`, `SrpClient`, `Bcrypt`, `ServerCatalog`, `VpnProfileBuilder`, `ConnectivityService`, `SettingsStore`, `CredentialVault`, `Log` |
| Data | `Data/servers.json` (offline catalogue fallback) |
| Assets | `Assets/*.png` regenerated from the Proton logo |

### B. Verification harness (`tools/`)

- `tools/proton_api_probe.py` — exercises Proton endpoints and reports status.
- `tools/srp_reference.py` — Python port of Proton SRP-6a with the **client+server
  self-consistency check from Proton's own `TestE2EFlow`**; proves the math.
- `tools/vb_srp_harness/` — desktop .NET console that runs the **same VB source**
  and prints proofs, so the shipping VB code is cross-checked against the reference.
- `tools/render_ui.py` + `tools/ocr_check.py` — render each page layout to PNG and
  OCR it to verify the visible text/labels.
- `tools/make_assets.py` — generate store/tile/splash assets from the logo.

### C. Agent skill (`.agents/skills/proton-vpn-wp-maintainer/`)

`SKILL.md` + helper scripts so a future agent can rebuild, refresh the server
catalogue, re-run OCR checks, and regenerate assets.

### D. Docs

`README.md`, `docs/BUILD.md`, `docs/ARCHITECTURE.md`.

## Interfaces (cross-task contracts)

- `SrpClient.GenerateProofs(bitLength, modulus, serverEphemeral, hashedPassword)`
  → `SrpProofs(ClientEphemeral, ClientProof, ExpectedServerProof, SharedSession)`
  all byte arrays; byte order = **little-endian** (Proton convention).
- `Bcrypt.HashRaw(password, encodedSaltDotSlash)` → 23 raw bytes of the `$2y$10$` hash.
- `ProtonAuthService.SignInAsync(username, password, twoFactorCode)` → `ProtonSession`.
- `ServerCatalog.LoadAsync()` → `IReadOnlyList(Of ProtonLogical)` (API first, bundle fallback).
- `VpnProfileBuilder.BuildIkeV2(logical, credentials)` → `VpnProfile`.
- `ConnectivityService.MeasureAsync(host, port)` → latency ms / -1 on failure.

## Task list

1. **Assets** — build `tools/make_assets.py`, generate all `Assets/*.png` from
   `~/Downloads/proton.png.png`; register any new files in `.vbproj`.
2. **Foundation** — models, `ViewModelBase`, `SettingsStore`, `Log`, `Json` helpers,
   `ActionResult`. Verify: build green.
3. **Crypto** — port `SrpClient` + `Bcrypt` to VB. Verify: `tools/srp_reference.py`
   self-consistency passes AND desktop VB harness matches the reference.
4. **API client + catalogue** — `ProtonApiClient`, `ServerCatalog`, `Data/servers.json`.
   Verify: build green + offline parse test.
5. **Profile builder + connectivity** — `VpnProfileBuilder`, `ConnectivityService`.
   Verify: unit-style checks in the harness.
6. **Shell + theme** — `App.xaml` resources (Proton dark theme), `MainPage` frame.
7. **Views + ViewModels** — all seven pages, wiring, navigation, error states.
8. **Project wiring** — add every page/compile item to `.vbproj`.
9. **Compile & fix** — `msbuild 12.0` until exit 0 and `.appx` produced.
10. **OCR evaluation** — render pages, OCR, fix mismatches.
11. **Agent skill + docs** — `SKILL.md`, scripts, README, ARCHITECTURE, BUILD.
12. **Final review** — whole-branch review, fix Critical/Important.

## Review Focus

Inputs and failure modes the tests do not exercise, to be checked deliberately:

- Root/Custom-Host TLS certificate validation in `ProtonApiClient` (must NOT
  disable validation; must not pin incorrectly).
- Password/credential material leaking into logs, navigation parameters, or
  `ApplicationData` in cleartext.
- SRP byte-order correctness (little-endian) — an off-by-one silently breaks auth
  only against the live server.
- Catalogue parsing under malformed/empty JSON (cache and bundle).
- Socket latency test not blocking the UI thread and always disposing sockets.
- Base64 / "dot-slash" bcrypt alphabet: using standard base64 breaks hashing.

## Definition of done

`MSBuild 12.0 Debug|ARM` and `Release|ARM` exit 0, an `.appx` is produced, the SRP
harness is green against the Python reference, OCR confirms every page's visible
labels, the skill and docs exist, and all rulings are recorded in the ledger.
