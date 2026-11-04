# Capability matrix — research gate, 2026-11-03

**Documented** means official documentation/package evidence, not an executed application test. **Unverified** means no test result exists. No production app was created or published.

| Capability | Evidence/status | Remaining acceptance test |
|---|---|---|
| Stable Avalonia >=12.1 | **Verified available: 12.1.5**, NuGet listed; release published 2026-11-02, non-prerelease.[11][18] | Restore exact versions after build environment is authorized. |
| Free core TableView | **Verified package/API + docs:** in Avalonia 12.1.5 / Avalonia.Controls.dll; MIT; no separate TableView package needed.[7][12][14] | Compile XAML and open a real window. |
| net10.0 package compatibility | **Verified metadata/assets**, explicit net10.0 group and assembly.[12][14] | Full restore/build/test; this is not binary execution evidence. |
| Local .NET 10 toolchain | **Unavailable in installed SDK list**; selected SDK 6.0.403. See `stack-evidence/dotnet-info.txt`. | Authorized .NET 10 environment; do not silently downgrade/install. |
| Table row/cell recycling | **Documented**; rows/cells recycled, columns not virtualized.[7] | Bound container height; measure realized elements and scroll latency at specified dataset sizes. |
| List mode and common sort/filter | **Unimplemented**; intended ListBox + shared ResultProjection; TableView derives from ListBox.[7] | Same IDs/order/selection, numeric sort, unknown-last rules, anchor preservation, incremental update tests. |
| Windows 11 desktop | **Documented** Tier 1 for 24H2/build 26100 x64/ARM64.[5] | Real win-x64 smoke test and dependency validation. |
| Windows 10 22H2 | **Documented** Avalonia Tier 2 x64, not automatic runtime/OS support approval.[5][10] | Separate supported-OS/runtime eligibility and smoke test. |
| Windows topmost/tool window, taskbar/Alt+Tab exclusion | **UNVERIFIED on macOS**; desired own-window adapter only. | Borderless/windowed PoE test; hide/show, Z-order and activation across monitors. |
| Windows no-activate, click-through, interactive mode | **UNVERIFIED on macOS**. | Verify focus never stolen in passive mode; correct input routing on mode switch. |
| Windows RegisterHotKey/message integration | **UNVERIFIED on macOS**; no low-level hooks permitted by project. | Register/unregister; collision reporting; hide/show and refresh; supported Avalonia integration. |
| Per-monitor DPI/negative coordinates | **UNVERIFIED on macOS**. | Mixed-DPI monitors, reconnect, scaling/position persistence without double scaling. |
| Exclusive-fullscreen overlay | **Out of MVP scope**, no compatibility claim. | None unless scope explicitly changes. |
| Linux desktop X11/XWayland | **Documented backend**, not app tested; native prerequisites and distro tiers apply.[5][6] | Build/test on Linux, native dependency checks, real desktop smoke. |
| Native Linux Wayland | **Documented opt-in from 12.1.0**, early-stage; explicit backend has no fallback.[6] | Separate compositor/backend test matrix; no automatic enablement in this task. |
| Linux topmost/no-activate/click-through/global hotkeys | **UNVERIFIED**, no generic compositor-independent support claim. | Capability flags and explicit Unsupported paths; real X11/Wayland compositor tests. |
| macOS host | Research tools work; host reports 27.2 arm64. This host version is not explicitly listed in the retrieved Avalonia support table.[5] | Do not infer Tier 1 or Windows parity from successful research tooling. |
| Resource budgets and game impact | **UNMEASURED**; documentation recommends compiled bindings, virtualization, shallow trees and async loading.[8] | Windows Release process CPU/private working set/GPU, idle/visible/hidden/refresh/scroll and game frame-time A/B measurements. |
| Self-contained folder deployment | **Documented strategy**, no publish performed. Runtime bundled; OS-native prerequisites remain.[9] | Authorized win-x64 publish; clean-machine installer and portable smoke from same folder. |
| Complete distribution licence compliance | **Partial:** main package manifests/licences inspected; no commercial UI selected.[12][15][16] | Locked transitive/native dependency and installer licence/notice audit. |

## Gate outcome

Package availability **passes**. Local build readiness **fails** (no installed .NET 10). Overlay/performance/deployment validation **not performed**. Data eligibility is a separate gate owned by the parent investigation; nothing here clears it.

## Sources

[5] https://docs.avaloniaui.net/docs/supported-platforms
[6] https://docs.avaloniaui.net/docs/platform-specific-guides/linux
[7] https://docs.avaloniaui.net/controls/data-display/structured-data/tableview
[8] https://docs.avaloniaui.net/troubleshooting/app-performance-issues
[9] https://learn.microsoft.com/en-us/dotnet/core/deploying
[10] https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
[11] https://api.nuget.org/v3/registration5-gz-semver2/avalonia/12.1.5.json
[12] https://api.nuget.org/v3-flatcontainer/avalonia/12.1.5/avalonia.nuspec
[14] https://api.nuget.org/v3-flatcontainer/avalonia/12.1.5/avalonia.12.1.5.nupkg
[15] https://api.nuget.org/v3-flatcontainer/avalonia.desktop/12.1.5/avalonia.desktop.nuspec
[16] https://api.nuget.org/v3-flatcontainer/avalonia.themes.fluent/12.1.5/avalonia.themes.fluent.nuspec
[18] https://api.github.com/repos/AvaloniaUI/Avalonia/releases/tags/12.1.5
