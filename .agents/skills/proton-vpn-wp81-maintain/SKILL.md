---
name: proton-vpn-wp81-maintain
description: Use when changing, building, verifying or releasing the ProtonVPNForWP app (the VB.NET Windows Phone 8.1 client) — adding a page, touching the Proton API client, the SRP/bcrypt crypto, the bundled server catalogue, the tile assets, or the VS2013 build. Covers the mandatory plan-then-execute workflow and every verification command.
---

# Maintaining ProtonVPNForWP

A VB.NET / XAML Proton VPN client for Windows Phone 8.1, built with Visual
Studio 2013 and MSBuild 12.0. The repository is `vincenzosco/ProtonVPNForWP`.

## Non-negotiable workflow: plan, then execute the plan

Before touching any source file — unless the change is a genuine one-liner like
a typo in a comment:

1. Load the **writing-plans** skill and write a plan file under
   `docs/plans/<name>.md`. Per task it must name the files touched, the exact
   interfaces produced and consumed, the verification command, and that
   command's `Expected:` output.
2. Load the **executing-plans** skill and execute that plan here, task by task,
   without pausing between tasks. When the plan is wrong, take a ruling, write
   `Ruling: <decision> — <why> — <cost if wrong>` into the ledger at
   `.superpowers/sdd/<plan-name>/progress.md`, and continue.

Writing a plan and then not executing it is a defect. So is executing without
one. This mirrors `AGENTS.md`; that file is the authority if they disagree.

## Verify, never assume

Run all three, and paste real output. A green build alone proves nothing about
behaviour.

1. **Crypto vectors** — `python tools/run_srp_harness.py`
   Compiles the shipping VB crypto (`Sha512.vb`, `Bcrypt.vb`, `SrpClient.vb`,
   `CryptoBytes.vb`) with the desktop compiler and compares SHA-512, bcrypt and
   the whole SRP-6a exchange against the Python reference. Expect
   `VB harness vs Python reference: OK` with 9 `[PASS]` lines.
   Run this after **any** edit to those four files.
2. **The real build**
   ```bash
   "/c/Program Files (x86)/MSBuild/12.0/Bin/MSBuild.exe" \
     "Proton VPN WP/Proton VPN WP/Proton VPN WP.vbproj" \
     //p:Configuration=Debug //p:Platform=AnyCPU //nologo //v:minimal
   ```
   Expect exit 0 and `0` occurrences of `error BC`. Build the `.vbproj`
   directly — the solution's platform name is `Any CPU`, not `AnyCPU`.
3. **XAML keys and bindings** — `python tools/xaml_check.py`
   Expect `XAML check OK`. The XAML compilers resolve neither `{StaticResource}`
   keys nor `{Binding}` paths, so a missing key (which throws when the page
   loads) or a misspelled binding path passes a green build unnoticed. Run this
   after touching any `.xaml` file or any bindable model/view-model property.
4. **UI strings** — `python tools/ocr_check.py`
   Expect `UI text verification OK`. Reads the shipping strings back with the
   Windows OCR engine; it verifies text content and legibility, never layout.
5. **The package** — confirm the requested artefacts really shipped:
   ```bash
   python -c "import zipfile;print('\n'.join(sorted(zipfile.ZipFile('Proton VPN WP/Proton VPN WP/AppPackages/Proton VPN WP_1.0.0.0_AnyCPU_Debug_Test/Proton VPN WP_1.0.0.0_AnyCPU_Debug.appx').namelist())))"
   ```
   Every new asset, `Data/servers.json` and every page `.xbf` must be listed.
   A file can compile and still never be deployed — `Assets/ProtonLogo.png`
   was referenced by a page but absent from the project once.

### When the error list is truncated

vbc 12 stops at 100 errors and has no `/errorlimit`. When a build shows ~100
errors and the causes are unclear, capture the compiler command and replay it
with files dropped:

```bash
# 1. capture the exact command line MSBuild used
MSBuild.exe <vbproj> //p:Configuration=Debug //p:Platform=AnyCPU //v:diag > diag.log
# 2. replay it without the noisy files
python tools/vbc_subset.py --diag diag.log --exclude "Views\" --exclude "ViewModels\"
```

## Recurring jobs

- **Refresh the offline server catalogue:** `python tools/make_bundle.py`.
  Never hand-edit `Data/servers.json`; it is generated and committed so the app
  works with no network.
- **Refresh the branding:** `python tools/make_icons.py <source-logo.png>`.
  Regenerates all seven images in `Assets/` at the exact scale-240 sizes the
  manifest expects and reports any size mismatch.
- **Regenerate the Blowfish tables:** `python tools/gen_blowfish.py` rewrites
  the constants in `Services/Bcrypt.vb`. Do not edit them by hand.
- **Add a page:** create `Views/X.xaml` + `Views/X.xaml.vb` (both `Partial`,
  code-behind `Inherits Page`), then add BOTH a `<Compile>` (with
  `<DependentUpon>`) and a `<Page>` entry to `Proton VPN WP.vbproj`.

## Traps that have already cost time here

- **`Imports` of a project namespace must be root-qualified.** `Imports Models`
  is silently ignored by vbc 12 (warning BC40056) so every `Models` type reads
  as undefined; write `Imports Proton_VPN_WP.Models`. Same for `.Services`,
  `.ViewModels`, `.Crypto`. Always check that a namespace import actually
  resolves before debugging anything else.
- **VB is case-insensitive.** A local or parameter named `json` shadows the
  `Json` helper class; name it `jsonBody`.
- **VB12 has no read-only auto-properties.** `Friend ReadOnly Property X As New
  List(Of T)()` fails to compile; use a backing field and an explicit getter.
  There is also no null-conditional `?.`, no `nameof`, no interpolation.
- **Windows Phone 8.1 is not Windows 8.1.** Missing from the API surface:
  `System.Security.Cryptography`, `System.Text.Encoding.ASCII`,
  `Windows.ApplicationModel.DataTransfer.Clipboard`, and
  `StorageFolder.TryGetItemAsync`. Grep the reference metadata before reaching
  for a WinRT type:
  `grep -a -c Clipboard "/c/Program Files (x86)/Windows Phone Kits/8.1/References/CommonConfiguration/Neutral/Windows.winmd"`
- **Events on other types need `AddHandler`/`RemoveHandler`**; `+=` does not
  compile.
- **`protected` is a VB keyword** — not a usable local variable name.

## Scope honesty (do not regress this)

Windows Phone 8.1 gives third-party apps **no VPN API**: the app cannot install
a profile or start a tunnel. Its job ends at producing the exact IKEv2/OpenVPN
values and the instructions. The UI must keep saying so — never present
"connected", and never label a latency probe a connection. Passing tests do not
change this constraint.
