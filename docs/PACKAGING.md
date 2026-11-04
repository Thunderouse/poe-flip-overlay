# Local packaging and verification

## Contract

- `Directory.Build.props` is the sole package/application version source. `global.json` pins the SDK (currently 10.0.403, roll-forward disabled). Scripts require PowerShell 7; Windows PowerShell 5.1 is not supported.
- Target: Windows 11 x64, self-contained .NET folder deployment. Single-file, trimming, AOT and ReadyToRun are explicitly disabled.
- Inno Setup consumes the **same unchanged publish directory** as the portable ZIP. The ZIP adds only an empty `portable.flag`; the publish directory and installed payload have no marker or bundled user data. Archive read-back compares every payload file's SHA-256 with the installer input directory before and after installer compilation.
- The installer uses `PrivilegesRequired=lowest`, `{localappdata}\Programs\PoE Flip Overlay`, per-user shortcuts and a stable AppId. Settings/database/logs belong under `{localappdata}\PoEFlipOverlay` in installed mode. Uninstall retains data unless the user explicitly agrees to deletion; silent uninstall does not request or delete it. Portable storage behavior is implemented by the application, not by the ZIP script.
- No script pushes, tags, uploads, deploys or creates releases. `dotnet publish` means **local compilation**, not remote publication. CI contains no upload/release actions and has read-only contents permission.

## Build on Windows

Install the SDK selected by `global.json`, PowerShell 7 and Inno Setup 6 (supporting `x64compatible`). Inno Setup is an explicit external prerequisite; scripts do not download or silently install it. Record its exact version in Windows build evidence. If `ISCC.exe` is not on PATH, the script tries the standard Inno Setup 6 directory, or accepts an explicit path:

```powershell
pwsh -NoProfile -File tools/build.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Use `-Dotnet` to select a particular SDK executable. SDK selection is checked before building. All output targets must be absent: the script refuses to overwrite an existing version's publish directory, ZIP, setup EXE or `SHA256SUMS`. Archive/move old outputs explicitly; do not delete unrelated evidence to make a run pass.

The script checks existing lock files, runs the safety and packaging-fixture checks, locked solution restore, Release build/tests, offline application selftest and formatting verification. It then performs a separate locked `win-x64`/self-contained restore and publishes with `--no-restore`. It restores the ordinary solution graph again afterward and checks that all dependency lock hashes remain unchanged. A missing lock is an error, not permission to generate one. RID declarations and committed lock graphs must already support these restores; repair them as a deliberate source change, never by disabling locked mode.

Outputs under `artifacts/`:

```text
publish-<version>-win-x64/
PoE-Flip-Overlay-<version>-portable.zip
PoE-Flip-Overlay-<version>-setup.exe
SHA256SUMS
```

The payload contains the user guide as `README.md`, third-party notices and `build-info.json` (version, full commit or `uncommitted`, dirty-tree flag, SDK, local-validation channel, liveScanner=false). A commit hash on a dirty tree is **not** a complete source provenance claim. No timestamp is embedded in that metadata.

## Partial cross-build only

On the macOS development host:

```powershell
pwsh -NoProfile -File tools/build.ps1 -Dotnet <dotnet-sdk-directory>/dotnet -SkipInstaller
```

`-SkipInstaller` is explicit partial output. It never creates a placeholder `setup.exe`, and checksums cover only assets actually built. Without that switch a non-Windows installer build is rejected. The script still runs all build/test/format gates; it does not bypass a failing source integration gate. Final packaging should run only once the source integrator has completed those gates.

## Archive guarantees and limits

`package-common.ps1` writes slash-separated ZIP paths with a single versioned root, ordinal file ordering and fixed 2000-01-01 UTC entry timestamps. It rejects links/reparse points, path traversal, absolute/drive/ADS paths, Windows reserved names, trailing dots/spaces, illegal characters, empty path segments, case-insensitive duplicates, `.git`, `user-data` and pre-existing portable markers. Hidden files are inspected too. Read-back validates root, entry types, uniqueness, required files and exact payload hashes/count when a publish directory is supplied.

These checks make identical fixture inputs produce identical ZIP bytes on the tested runtime. Locked packages, the pinned SDK, deterministic compilation and `ContinuousIntegrationBuild=true` constrain application build inputs. They do **not** prove bit-for-bit reproduction of complete production builds on different machines: runner images, Inno/compiler versions, signing, .NET compression/runtime differences, source paths and native toolchains still require recording and a separate two-clean-build comparison. Setup bytes have not been shown reproducible. `SHA256SUMS` detects accidental corruption, not authenticity; no code signing is configured or claimed.

## Windows portable smoke

Keep `SHA256SUMS` beside the archive. From the checkout matching its version:

```powershell
[xml]$props = Get-Content Directory.Build.props -Raw
$version = [string]$props.Project.PropertyGroup.Version
./tools/verify.ps1 -Archive "artifacts/PoE-Flip-Overlay-$version-portable.zip"
```

The verifier refuses non-Windows execution, mismatching explicit versions, unsafe archives and missing/ambiguous/incorrect checksum entries before extraction or application execution. It extracts to a fresh path containing spaces and Unicode, checks the marker and absence of user data, runs the real executable's `--version` and offline `--selftest` with a 60-second timeout, and writes `verification.json` beside captured stdout/stderr. It retains that directory as evidence. It does not run the installer or certify GUI portability/AppData isolation.

## CI pin provenance

On 2026-11-04, the following exact official Git refs were queried successfully with `git ls-remote` (no remote added to this repository):

```text
git ls-remote https://github.com/actions/checkout.git refs/tags/v4.3.1
34e114876b0b11c390a56381ad16ebd13914f8d5  refs/tags/v4.3.1

git ls-remote https://github.com/actions/setup-dotnet.git refs/tags/v4.3.1
67a3573c9a986a3f9c594539f4ab511d57bb3ce9  refs/tags/v4.3.1
```

`validate.yml` uses those immutable commit IDs and disables persisted checkout credentials. Windows and Ubuntu build/test/format/selftest jobs also parse the PowerShell scripts and run archive fixture checks; native UI smokes are not game-overlay tests. Windows then builds setup/portable and runs the archive smoke. The Windows runner must actually provide Inno Setup or the job fails clearly; no unverified installer download is embedded. Hosted image labels are mutable, so this is not an immutable build-environment claim. The workflow has not been dispatched from this local session.

## Verification actually performed on 2026-11-04

- Live SDK executable: `<dotnet-sdk-directory>/dotnet --version` returned `10.0.403`.
- No `pwsh` was on PATH. Official PowerShell 7.6.9 macOS ARM64 tarball was downloaded over verified TLS from the `PowerShell/PowerShell` GitHub release into user scratch only, checked against the official release API asset SHA-256 `a1c7e98ac403da4be5ba75c1c5855af272de7787a91d478c4267466fe58fc1af`, safely extracted there and executed. No system-wide install, sudo, quarantine bypass or TLS bypass was used. Executable: `<local-research-directory>/powershell-7.6.9/pwsh` (scratch may expire).
- All five `tools/*.ps1` files parsed successfully using the actual PowerShell parser. YAML parsed with Ruby's YAML parser; all three inline `pwsh` blocks also parsed with PowerShell.
- `tools/test-packaging.ps1`: **23 fixture checks passed**, including exact payload hashes, identical-input ZIP bytes, malicious paths, exact/case-insensitive duplicate entries, Unix symlink entries, tampered payload, overwrite and bundled user-data rejection. Fixtures are inert bytes, cleaned afterward, and never presented as an application/installer.
- `tools/check-safety.ps1` passed its production-source deny-list scan. This is not a security audit or a guarantee of GGG acceptance.
- Existing-output refusal was exercised without modifying the existing `artifacts/publish-0.1.0-win-x64` output. The verifier's non-Windows refusal was exercised successfully.
- All discovered project files had adjacent `packages.lock.json` files. This existence check is not a claim that the updated complete build script ran end-to-end.

## Source integrator follow-up

The full `build.ps1 -SkipInstaller` path was executed locally after source integration. The initial run reproduced a real NU1004 failure: `dotnet restore -r win-x64` overrides the declared RuntimeIdentifiers list. The script now selects the single publish RID with `-p:RuntimeIdentifier=win-x64`, preserving the existing multi-RID lock graph. The corrected end-to-end cross-build passed: packaging fixtures, safety scan, locked restore, Release build/tests, selftest, formatting, RID restore, self-contained publish, ordinary locked restore and ZIP payload-hash read-back. This remains a partial cross-build, not installer or Windows execution evidence. Any later source revision requires regenerating the archive; `build-info.json` records the exact commit/dirty state of its own run.

## Exact remaining blockers / required evidence

1. **No Windows execution environment in this session.** Windows executable startup, native UI smoke, actual archive extraction/runtime behavior on Windows and per-user installed storage behavior remain unverified here.
2. **No Inno Setup on this macOS host.** `installer.iss` has not been compiled here; no genuine setup EXE was generated by this packaging work. Compiler syntax/compatibility and clean-VM install/uninstall/upgrade remain Windows gates. Never rename another file to `setup.exe` or claim an installer test from a macOS cross-build.
3. **Full updated build script not executed end-to-end by the packaging worker.** Existing integration output was preserved, and source/build/test/format integration and final production artifact generation belong to the source integrator. No successful source gate is inferred from parser/fixture tests.
4. **No two-clean-production-build comparison or signing.** Dependency-lock and deterministic-ZIP checks are narrower evidence, as described above.
5. **Manual acceptance still required:** standard (non-admin) Windows 11 clean VM; install/start/update using the stable AppId; uninstall with both keep/delete-data choices; silent uninstall retention; portable extraction to spaces/Unicode and writable/read-only locations; no installed-mode marker; settings/SQLite/log persistence, absence of unexpected AppData writes, second-instance UI behavior; multi-monitor/DPI/hotkeys/click-through/focus; PoE1 borderless/windowed interaction; measured CPU/RAM/GPU/frame times. Linux compositor overlay capability and Windows 10 compatibility remain separate gates. Neither CI UI smoke nor macOS syntax validation closes these.
