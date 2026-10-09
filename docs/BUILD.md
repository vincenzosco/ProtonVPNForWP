# Building and deploying ProtonVPNForWP

Target: **Windows Phone 8.1**, VB.NET, Visual Studio 2013 / MSBuild 12.0.
Everything below is written for Git Bash on Windows (POSIX syntax).

## Prerequisites

| Requirement | Notes |
|---|---|
| Visual Studio 2013 | supplies MSBuild 12.0 and the XAML compiler targets |
| Windows Phone 8.1 SDK | supplies `Windows.winmd` and the reference assemblies |
| Python 3 | only for the `tools/` scripts (building the app does not need it) |
| Pillow (`pip install Pillow`) | only for `tools/make_icons.py` |

## Build the project

```bash
MSB="/c/Program Files (x86)/MSBuild/12.0/Bin/MSBuild.exe"
"$MSB" "Proton VPN WP/Proton VPN WP/Proton VPN WP.vbproj" \
  //p:Configuration=Debug //p:Platform=AnyCPU //nologo //v:minimal
```

**Expected:** exit code 0 and no line containing `error BC`.

Two things trip people up:

- Build the **`.vbproj`**, not the `.sln`. The solution's platform entry is
  named `Any CPU` (with a space), so `//p:Platform=AnyCPU` on the solution fails
  with `MSB4126: the specified solution configuration is invalid`.
- Under Git Bash, MSBuild flags must be written `//p:` — a single leading slash
  gets rewritten into a Windows path by MSYS.

Release and ARM builds use the same shape:

```bash
"$MSB" "Proton VPN WP/Proton VPN WP/Proton VPN WP.vbproj" //p:Configuration=Release //p:Platform=ARM
```

## Expected output

```
Proton VPN WP/Proton VPN WP/bin/Debug/              Proton VPN WP.exe, .xbf, resources.pri
Proton VPN WP/Proton VPN WP/AppPackages/
    Proton VPN WP_1.0.0.0_AnyCPU_Debug.appxupload   installer
    Proton VPN WP_1.0.0.0_AnyCPU_Debug_Test/*.appx  the package itself (a zip)
```

Inspect what actually shipped — a file can compile and still not be deployed:

```bash
python -c "
import zipfile
p = 'Proton VPN WP/Proton VPN WP/AppPackages/Proton VPN WP_1.0.0.0_AnyCPU_Debug_Test/Proton VPN WP_1.0.0.0_AnyCPU_Debug.appx'
print('\n'.join(sorted(zipfile.ZipFile(p).namelist())))"
```

## Deploy

- **Emulator:** open the solution in Visual Studio 2013, choose a Windows Phone
  8.1 emulator, press F5. Emulators need Hyper-V.
- **Device:** register the phone as a developer device in *Settings → Update and
  security → For developers*, then either deploy from Visual Studio or sideload
  the `.appxupload` with the Windows Phone Application Deployment tool. Sideloaded
  unsigned packages require the device in developer mode.

## Verification, in the order to run it

1. **Crypto vectors** (mandatory after any edit to `Sha512.vb`, `Bcrypt.vb`,
   `SrpClient.vb`, `CryptoBytes.vb`):

   ```bash
   python tools/run_srp_harness.py
   ```

   Expected: 9 `[PASS]` lines and `VB harness vs Python reference: OK`.
   It compiles the shipping sources with `vbc` and compares them against
   `tools/srp_reference.py`.

2. **Build** (above). Expected: 0 errors.

3. **XAML keys and bindings:**

   ```bash
   python tools/xaml_check.py
   ```

   Expected: `XAML check OK`. The XAML compilers resolve neither
   `{StaticResource}` keys nor `{Binding}` paths, so a missing key -- which
   throws as soon as the page loads -- or a misspelled binding path is invisible
   to a successful build. The script fails on a missing resource key and warns
   on a binding path that exists on none of the models or view models.

4. **UI strings:**

   ```bash
   python tools/ocr_check.py
   ```

   Reads the shipping strings back with the Windows OCR engine. It needs no
   model download and no external binary; the dependencies are in
   `tools/requirements.txt`. It verifies text content and legibility, **not**
   layout: with no phone or emulator available, it renders the strings in the
   app's colours and font rather than screenshotting a running app.

5. **Startup invariants:**

   ```bash
   python tools/startup_check.py
   ```

   Expected: six `[PASS]` lines and `startup invariants: OK`. Three of its checks
   pin the reasons the app could not get past the splash screen: the shell's wait
   for the stored session is bounded, `RestoreAsync` awaits no API call, and the log
   really reaches a file. Reverting any of them must make this script fail -- if it
   still passes, the guard is broken, not the code.

6. **Log-file policy vectors:**

   ```bash
   python tools/run_log_harness.py
   ```

   Expected: ten `[PASS]` lines and `log policy vectors: OK`. It compiles the
   shipping `Services/LogFilePolicy.vb` with `vbc`, the same way the crypto harness
   does, which is why that file must stay WinRT-free.

7. **View-model strings:**

   ```bash
   python tools/text_check.py
   ```

   Expected: `view-model strings: OK`. WinRT rejects a null `TextBlock.Text` -- it
   raises `ArgumentNullException` inside the HSTRING marshaller, unlike WPF and
   Silverlight -- so a view-model string property must never hand back `Nothing`.
   This is the check that was missing when `LoginPage.UpdateVisualState` assigned a
   null `StatusMessage` during navigation and the app died. The rule lives in the
   getters (`Return If(_field, String.Empty)`), not at the 49 call sites.

## Reading the log

Every session appends to `logs\app.log` inside the app's local folder, and the
first line of the session records its own full path, so the file can be found
without a debugger:

```
[2026-10-09T18:02:11.4820000Z] Info: startup: log file C:\Data\Users\DefApps\...\LocalState\logs\app.log
```

The startup path logs both sides of each step (`startup:`, `restore:`, `vault:`), so
**the last line in the file names the step that stalled**. That is the whole point:
a stall becomes legible without attaching a debugger.

To retrieve it, use the SDK's Isolated Storage Explorer. Its own usage text gives
the arguments: `ts` takes a snapshot of the isolated store to the desktop, `de`
selects a Windows Phone connected to the desktop, `xd` the default emulator, and
the GUID is the `PhoneProductId` in `Package.appxmanifest`.

```bash
ISET="/c/Program Files (x86)/Microsoft SDKs/Windows Phone/v8.1/Tools/IsolatedStorageExplorerTool/ISETool.exe"

"$ISET" EnumerateDevices                                              # what exists right now
"$ISET" ts de 0a59a853-0bc1-4932-a95e-3b293212e9ec .bug-hunter/device-store   # connected phone
"$ISET" ts xd 0a59a853-0bc1-4932-a95e-3b293212e9ec .bug-hunter/device-store   # emulator
```

The snapshot is written into the path you give (the `Local` store by default), so
the file above appears as `.bug-hunter/device-store/logs/app.log`.

The file is capped at 64 KB (`LogFilePolicy.MaxBytes`); once it passes that the
oldest half is dropped, rather than being left to grow. It never holds a
credential: `Log` exposes no API that accepts one, and the startup lines record
presence and lengths, never values.

## Debugging a build with ~100 errors

vbc 12 stops after 100 errors and offers no `/errorlimit`, so the interesting
errors can be hidden behind cascades. Capture the real compiler command and
replay it with files excluded:

```bash
MSB="/c/Program Files (x86)/MSBuild/12.0/Bin/MSBuild.exe"
"$MSB" "Proton VPN WP/Proton VPN WP/Proton VPN WP.vbproj" \
  //p:Configuration=Debug //p:Platform=AnyCPU //v:diag > diag.log

python tools/vbc_subset.py --diag diag.log --exclude "Views\" --exclude "ViewModels\"
```

`vbc_subset.py` finds the `vbc.exe` command line inside the diagnostic log,
drops the matching sources, and prints only the diagnostics that remain.

## Regenerating generated files

```bash
python tools/make_bundle.py                       # Data/servers.json (offline catalogue)
python tools/make_icons.py <source-logo.png>      # all seven Assets/*.png
python tools/gen_blowfish.py                      # Blowfish constants inside Services/Bcrypt.vb
```

None of these outputs should ever be edited by hand.
