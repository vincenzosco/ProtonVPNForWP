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

### Emulator, headless, from Git Bash

The emulator, the deployment and the log retrieval all drive from the command
line, which is how the on-device runs recorded below were made:

```bash
XDE="/c/Program Files (x86)/Microsoft XDE/8.1/XDE.exe"
AD="/c/Program Files (x86)/Microsoft SDKs/Windows Phone/v8.1/Tools/AppDeploy/AppDeployCmd.exe"
ISET="/c/Program Files (x86)/Microsoft SDKs/Windows Phone/v8.1/Tools/IsolatedStorageExplorerTool/ISETool.exe"

powershell -NoProfile -Command "Get-VM | Select-Object -ExpandProperty Name"   # the VM's name
"$XDE" //name "Emulator 8.1 U1 WVGA 4 inch 512MB(IT).vincenzo" //snapshot       # boot it; waits ~1 min

# Deploy the *x86* package, which is the architecture the emulator runs.
"$AD" //installlaunch "<...>\AppPackages\Proton VPN WP_1.0.0.0_x86_Debug_Test\Proton VPN WP_1.0.0.0_x86_Debug.appx" //targetdevice:xd

# Read the log back out (see "Reading the log" for the store and its paths).
"$ISET" ts xd 0a59a853-0bc1-4932-a95e-3b293212e9ec .bug-hunter/emulator-store
```

Three things bite here:

- **Every leading-slash flag needs a second slash.** Git Bash rewrites `/name`
  into `C:/Program Files/Git/name`, and `XDE.exe` answers with its usage text
  instead of booting. The same rewrite is why MSBuild wants `//p:` above.
- **`XDE.exe` takes the VM by name, not by VHD path.** `/vhd` is only read when
  the VM does not exist yet, so passing a path to an existing VM also just
  prints the usage text.
- **Which package to deploy.** The command-line runs below used the `x86`
  package. The F5 path deploys `Debug|AnyCPU` and runs too -- the Visual Studio
  run below is one of those. Both are measured; an `ARM` package is not.

The app has to be *started* for a run to happen, and `//installlaunch` does
that: it installs and launches in one call, and the app then writes its log with
other windows open on the host desktop, which is how the runs below were made.
Nothing else needs the emulator window: the log comes back out through
`ISETool`, not off the screen.

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

   Expected: ten `[PASS]` lines and `startup invariants: OK`. Three of its checks
   pin the reasons the app could not get past the splash screen: the shell's wait
   for the stored session is bounded, `RestoreAsync` awaits no API call, and the log
   really reaches a file. It also pins the two ways a session can end with no error at
   all (a suspension, and the back press the platform turns into a close) and the fact
   that the file sink does not resume on the UI thread -- the thread that is going away
   at exactly that moment. Reverting any of them must make this script fail -- if it
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

8. **Input fields:**

   ```bash
   python tools/input_check.py
   ```

   Expected: `input fields: OK`. Two rules, both of which the device found before
   this script existed. First, every editable `TextBox`/`PasswordBox` must be
   *read* by its code-behind: `LoginPage` read `PasswordEntry.Password` and never
   read `UsernameBox.Text`, so a completely filled sign-in form was rejected with
   "Enter your Proton username and password." -- an assignment into the box is not
   a read, which is what made the bug look handled. Second, the style of every
   input must put black text on a white field. The WP8.1 `TextBox` template keeps
   the app's `Foreground` and switches the *field* to the platform's edit colour
   (white) while typing, so the near-white text this app used vanished exactly when
   it mattered -- the field looked empty while it was being typed into. The check
   wants 4.5:1 contrast with the text on the dark side of the pair, which is the
   platform's own pairing (`#FFFFFFFF` field, `#FF000000` text).

9. **Sign-in guard:**

   ```bash
   python tools/signin_check.py
   ```

   Expected: `sign-in guard: OK`. A press of Sign in used to leave the button --
   and the way out of the page -- armed for the whole round trip, because `IsBusy`
   flips inside `SignInAsync`, after the page's own pre-await `UpdateVisualState`.
   The emulator log of 2026-10-09 19:00 shows what that cost: thirteen
   `/auth/info` requests inside six seconds from one finger, every attempt sharing
   the pending second-factor UID that the typed code has to continue. The check
   fails if the view model's refusal, the page's in-flight flag, or either
   control's disarming is removed.

10. **Catalogue failure paths:**

   ```bash
   python tools/catalogue_check.py
   ```

   Expected: `catalogue failure paths: OK`. `ServerCatalog.LoadAsync`'s live
   branch -- the only one that runs when Proton answers -- parses and caches a
   network response outside any `Catch`, and both view models that call it are
   awaited from bare `Async Sub` page handlers (`HomePage.OnNavigatedTo`,
   `ServersPage.OnNavigatedTo`), where an escaping exception ends the process:
   `App.OnUnhandledException` logs and does not set `Handled`. The check fails if
   the load stops degrading to the bundle, or if either view model stops
   reporting the failure itself.

11. **Network-failure diagnosis:**

    ```bash
    python tools/netdiag_check.py
    ```

    Expected: `network diagnosis: OK`. `ConnectivityService.DiagnoseAsync` gives a
    failed request a layered answer in the log -- connection profile, DNS, TCP,
    the app's own TLS 1.3 client, then the platform's Schannel *and* a control
    host, then HTTP -- because `0x80072EFD` reads identically whether the name did
    not resolve, the device has no internet, Proton alone is unreachable, or the
    handshake was refused. The layer that fails is the answer. The 2026-10-10
    emulator run, taken with the bug below still in the tree:

    ```
    netdiag: profile Internet: yes
    netdiag: profile Network: CoreSystem_NIC_01
    netdiag: dns account.proton.me -> 185.70.42.36
    netdiag: tcp account.proton.me:443 -> connected in 118 ms
    netdiag: tls tls13 (managed) account.proton.me failed: Arithmetic operation resulted in an overflow.
    netdiag: tls tls12 platform account.proton.me failed: ... 0x80090326
    netdiag: tls tls12 control www.microsoft.com -> handshake in 2035 ms
    netdiag: http control www.msftconnecttest.com -> 200
    netdiag: http proton failed: ... 0x80072EFD
    ```

    The first four lines are the app's own client, and they name the layer the
    platform's stack could not: DNS resolves, TCP connects, the control host
    completes the same handshake and answers HTTP, and Proton's handshake is
    refused (`0x80090326`). The `(managed)` line is the one that mattered -- it is
    the app's TLS client, and on that run it threw `OverflowException`.

    **That was a real, device-only bug.** The crypto is written to wrap (SHA-256's
    round additions, GHASH over `GF(2^128)`, the X25519 ladder), and VB checks
    integer overflow unless the project says otherwise. The `.vbproj` did not, so
    the check fired on the device in the first handshake. No `tools/` script could
    have shown it: they pass `/removeintchecks+` to `vbc` themselves. The fix is
    `<RemoveIntegerChecks>true</RemoveIntegerChecks>` in the project file, and
    `tools/tls13_check.py` now fails if it is removed or a configuration re-enables
    the check. After it, the same emulator run signs in:

    ```
    Info: nettest: attempting a sign-in with a throwaway identity
    Info: HTTP 200 POST account.proton.me/api/auth/info
    Info: HTTP 422 POST account.proton.me/api/auth
    ```

    `200` from `/api/auth/info` is Proton answering over the app's TLS 1.3
    handshake, and `422` is Proton rejecting the throwaway account at the API
    level -- a request that reached the service, which is the whole point.

    **And then the shipped binary, from Visual Studio.** The run above pressed
    Sign in from a temporary hook. This one has none: the scaffold-free tree,
    with the overflow fix in it, built as `Debug|AnyCPU` and deployed and started
    by Visual Studio 2013 on the same emulator. `Log.Write` sends a short
    local-time line to `System.Diagnostics.Debug`, so the Output window shows the
    same session as `logs\app.log` does, in the other rendering:

    ```
    'Proton VPN WP.exe' (CoreCLR: .): Loaded '...\VS.Debug_AnyCPU.Vincenzo\Proton VPN WP.exe'. Symbols loaded.
    [19:02:17] Info: startup: app constructed
    [19:02:17] Info: restore: no stored session
    [19:02:17] Info: restore: end
    [19:03:10] Info: HTTP 200 POST account.proton.me/api/auth/info
    [19:03:11] Info: HTTP 422 POST account.proton.me/api/auth
    ```

    Two things to read in it: no `nettest:` line, which is how the scaffolding's
    absence is visible; and `SYSTEM.RUNTIME.NUMERICS.NI.DLL` among the loaded
    modules, which is the new client's `BigInteger` arriving. This is the
    strongest of the runs -- the shipped binary, the architecture the F5 path
    picks, driven from the editor the maintainer already has open.

    Proton accepts only ECDHE+AES-GCM/ChaCha20 suites at TLS 1.2, and Windows
    8.1/Windows Phone 8.1's Schannel offers none of them for an RSA server
    certificate, so the intersection is empty and the platform cannot complete
    that handshake at all. `README.md` states that consequence, and names the
    client that does complete it instead.

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

A line beginning `lifecycle:` names a step the platform caused rather than a failure:
`suspending` when Start or the app switcher sends the app away, and `back pressed with
an empty page stack` when the shell lets a back press through and the platform closes
the app. Those are the two ways a session ends with no error at all, so their
**absence** stays evidence that the process died for some other reason -- which is what
the two device transcripts of 2026-10-09 ended in ("exited with code 0", no handler
line, nothing naming the step).

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
