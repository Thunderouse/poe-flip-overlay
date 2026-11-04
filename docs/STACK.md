# Stack gate — PoE Flip Overlay

Checked **2026-11-03** (UTC collection timestamps in `stack-evidence/version-summary.json`). Research only: no production project, SDK installation, build, publish, or git commit.

## Decision

**Package-availability gate PASS; build/runtime/performance gates NOT PASSED.** Exact research pin: **Avalonia 12.1.5**, not a preview. NuGet lists it, the package downloads, and GitHub marks the release non-prerelease/non-draft. NuGet publication: **2026-11-02T10:27:15.713Z**; GitHub publication: **2026-11-02T10:37:07Z**, both before the check date.[1][11][18]

- Package references for the eventual app: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, all **12.1.5**, MIT. Their manifests include `net10.0` dependency groups.[12][15][16]
- **TableView is in `Avalonia.Controls.dll` inside the `Avalonia` NuGet package**, not a separately required TableView package. Docs call this core `Avalonia.Controls`, available since 12.1; inspection of the downloaded package confirms `lib/net10.0/Avalonia.Controls.dll` and the `Avalonia.Controls.TableView` XML API entry. The attempted standalone `avalonia.controls` package index returned 404; do not add that PackageReference.[7][14]
- TableView is read-only, derives from ListBox, recycles rows/cells, and does **not** virtualize columns. Keep the column count bounded; no editing requirement justifies DataGrid. Sorting remains an application/shared-projection responsibility, not a claimed verified built-in TableView feature.[7]
- **.NET 10 compatibility is verified at manifest/asset level, not by execution.** Official desktop minimum is .NET 8, and this package supplies explicit net10.0 assets.[5][12][14]

## SDK/runtime availability versus this machine

Microsoft release metadata reports **SDK 10.0.403 / runtime 10.0.14**, release **2026-10-25**; the dated entry passes the 2026-11-03 cutoff. .NET 10 is LTS, with metadata EOL **2028-11-14** (a future support deadline, not a past event).[4][10]

Actual `dotnet --info` selects **SDK 6.0.403**, host **6.0.11**, `osx-arm64`, macOS **27.2**. Installed SDKs: 6.0.200, 6.0.202, 6.0.403; no .NET 10 SDK/runtime is listed. Raw output: `stack-evidence/dotnet-info.txt`. Therefore **local net10.0 compilation is blocked** until separately authorized provisioning or a suitable build host is supplied. No SDK was installed and no downgrade was made.

## Licences and supporting packages

These are verified available stable package manifests/publication records, **not a restored or tested dependency lock**. The xunit row is the package ID `xunit`, not a claim that this is the newest xUnit product generation.

| Component | Observed version | Declared licence | Evidence |
|---|---:|---|---|
| Avalonia / Desktop / Fluent | 12.1.5 | MIT | [12][15][16] |
| .NET runtime | 10.0.14 | MIT | [17] |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [19][23] |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | [20][24] |
| xunit | 2.9.3 | Apache-2.0 | [21][25] |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | [22][26] |

Avalonia's MIT licence requires retaining its copyright and permission notice. Preserve applicable third-party notices with eventual distribution; this check does not certify all transitive/native dependencies or installer tooling.[13] No commercial control is selected. The performance guide recommends TreeDataGrid but expressly places it in Avalonia Pro; that recommendation does not override the project's prohibition. Core TableView stays the selected control.[7][8]

Before release: restore on .NET 10, lock the complete dependency graph, audit transitive/native licences and vulnerabilities, and verify installer-tool licensing separately. No installer licence approval is implied here.

## Platform and performance constraints

- Avalonia documents Windows 11 24H2/build 26100 x64 as Tier 1; Windows 11 22H2 and Windows 10 22H2 x64 as Tier 2. Framework support is not proof of PoE overlay behaviour or Microsoft's OS lifecycle eligibility.[5][10]
- Linux defaults to X11, including XWayland on Wayland desktops. Native Wayland is opt-in from 12.1.0, early-stage, and has no automatic fallback when `UseWayland()` is selected. Do not assume compositor-independent overlay, hotkeys, no-activate, or click-through.[6]
- Read required platform dependencies: `libx11-6 libice6 libsm6 libfontconfig1`; Skia baseline glibc 2.17, with musl requiring a custom Skia build. Runtime/native dependency compatibility still needs a real Linux host.[5]
- Apply compiled bindings, bounded-height virtualized table/list, shallow visual trees, small images, fixed binding errors, and asynchronous data loading. These are design constraints, **not measured CPU/RAM/GPU results**.[7][8]
- Windows topmost/no-activate/click-through/hotkeys/DPI and gaming frame impact are **UNVERIFIED on this macOS host**. Linux overlay is likewise unverified. See `capability-matrix.md`.

## Deployment decision (not executed)

Keep the requested future deployment: `net10.0`, Release, `win-x64`, explicit self-contained folder deployment; `PublishSingleFile=false`, `PublishTrimmed=false`, `PublishAot=false`; ReadyToRun only after A/B measurement. Installer and ZIP must originate from the same publish folder. Self-contained includes .NET but not every native OS dependency, and runtime security updates require republishing the app.[9]

## Evidence and unresolved gates

`stack-evidence/` contains NuGet indexes/registration/manifests, downloaded `avalonia.12.1.5.nupkg`, package listing/API matches, GitHub release JSON, Microsoft release JSON, SDK output, licence text, mandatory-doc browser text, and summaries. SHA-256 of the downloaded Avalonia package: `3dd307917d5141066c712e8eb422127ffeb17b8cfe84ffc150498a3ef303f015`.

All four mandatory Avalonia pages were read in a real browser after direct requests returned 403; direct-fetch failures are preserved. Microsoft deploying and releases/support pages were read directly. The generic extraction tool failed because its configured backend was search-only; primary HTTP/browser retrieval supplied the evidence instead.

Outstanding: approved .NET 10 build environment; restore/build/test validation; Windows overlay and performance measurements; Linux smoke/compositor testing; complete licence audit. The independent **data-source gate remains the parent task's responsibility**; package availability does not authorize production implementation.

## Sources

[1] https://api.nuget.org/v3-flatcontainer/avalonia/index.json
[4] https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
[5] https://docs.avaloniaui.net/docs/supported-platforms
[6] https://docs.avaloniaui.net/docs/platform-specific-guides/linux
[7] https://docs.avaloniaui.net/controls/data-display/structured-data/tableview
[8] https://docs.avaloniaui.net/troubleshooting/app-performance-issues
[9] https://learn.microsoft.com/en-us/dotnet/core/deploying
[10] https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
[11] https://api.nuget.org/v3/registration5-gz-semver2/avalonia/12.1.5.json
[12] https://api.nuget.org/v3-flatcontainer/avalonia/12.1.5/avalonia.nuspec
[13] https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.5/licence.md
[14] https://api.nuget.org/v3-flatcontainer/avalonia/12.1.5/avalonia.12.1.5.nupkg
[15] https://api.nuget.org/v3-flatcontainer/avalonia.desktop/12.1.5/avalonia.desktop.nuspec
[16] https://api.nuget.org/v3-flatcontainer/avalonia.themes.fluent/12.1.5/avalonia.themes.fluent.nuspec
[17] https://raw.githubusercontent.com/dotnet/runtime/v10.0.14/LICENSE.TXT
[18] https://api.github.com/repos/AvaloniaUI/Avalonia/releases/tags/12.1.5
[19] https://api.nuget.org/v3-flatcontainer/communitytoolkit.mvvm/8.4.2/communitytoolkit.mvvm.nuspec
[20] https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlite/10.0.14/microsoft.data.sqlite.nuspec
[21] https://api.nuget.org/v3-flatcontainer/xunit/2.9.3/xunit.nuspec
[22] https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/18.11.0/microsoft.net.test.sdk.nuspec
[23] https://api.nuget.org/v3/registration5-gz-semver2/communitytoolkit.mvvm/8.4.2.json
[24] https://api.nuget.org/v3/registration5-gz-semver2/microsoft.data.sqlite/10.0.14.json
[25] https://api.nuget.org/v3/registration5-gz-semver2/xunit/2.9.3.json
[26] https://api.nuget.org/v3/registration5-gz-semver2/microsoft.net.test.sdk/18.11.0.json
