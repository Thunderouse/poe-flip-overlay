# PoE Flip Overlay — current engineering status

Status: **beta 0.1 (`0.1.0-beta.1`) / not production-ready**. The accepted product boundary is Candidates + manually confirmed observations. Automatic live NPC scanning is deferred; its source gate remains open. The user authorized this public GitHub prerelease after a sensitive-data check; future unrelated publication still requires permission.

Beta packaging preserves the full prerelease string in `--version`. Local home paths and irrelevant HTTP trace headers were removed before publication. No credential/session values, real observation databases or personal settings are included. The prior checkpoint measurements below are historical; release-specific checks include the version-output regression.

## Implemented locally

- .NET 10.0.403, Avalonia 12.1.5, English TableView/ListBox UI, shared numeric sorting with unknown-last in both directions, incremental row identities, selection sharing, budgets and freshness settings. Headless layout verifies actual viewport virtualization for 5,000 rows. The theme is compact charcoal/gold; no browser engine, fonts downloaded from CDN or realtime blur.
- Actual permitted `PoeNinjaEconomyProvider`, `PoeWatchProvider` and GGG hourly history reader. They preserve aggregate/history semantics and never manufacture live bid/ask or current stock. Wealthy Exile explicitly Unsupported. **The app currently uses poe.ninja**, not watch/GGG as alternate GUI sources.
- **Candidates:** local, explicit user-confirmed JSON recipe import with exact patch confirmation, directed integer ratios, NPC/act/access conditions and provenance. No bundled Wiki rates are silently approved. After import, the engine screens estimated purchase → up to two NPC conversions → estimated sale (at most four actions). Unknown source time remains **Age unknown**. Gold/depth are Unknown; setting a hard gold maximum excludes these candidates. User patch confirmation is not independently verified by the app. No installed catalog means an honestly empty Candidates set. See `USER-RECIPES.md`.
- **Observed:** independently entered/confirmed directed manual observations, persisted to SQLite; same-league/context route evaluation, per-leg source times, integer lot/depth sizing, capital/gold constraints, leftovers not credited as revenue. User-entered gold per whole lot is not replaced by a universal fee formula. Unknown time/gold/depth are never silently zero.
- One-shot TimeProvider-backed UI scheduling expires manual rows without another HTTP response, including hidden/frozen rows; visible age updates at most once/second, none while hidden. Candidates can refresh in the background with pause setting. Successful ninja automatic refresh uses the longer of an approximately 15-minute cadence floor or the HTTP cache deadline; manual refresh respects cache and source limits and cannot promise newer source data.
- HTTP single-flight, bounded payload/cache/queue/concurrency, conditional ETag requests, Retry-After handling, 401/403 source stop, bounded error backoff, generation-aware stale response protection. SQLite is serialized, parameterized and transactional, uses rollback journal, bounded retained observations/database, single-writer data-dir ownership. Settings are bounded atomic JSON. Portable mode is selected only by portable.flag; read-only failure does not fall back to AppData.
- A Windows own-window adapter and supported Avalonia WndProc bridge are now **wired**, including topmost/tool window/passive click-through/no-activate, hotkey conflict handling and cleanup. On macOS/Linux the adapter is not constructed and the app remains an ordinary companion window. Windows native behavior remains **unverified**. Hotkey customization currently uses documented environment settings, not a full settings editor.
- GitHub stable release checker library has offline tests and safe URL/version validation; repository is not configured, so no update network request is enabled. No auto-download/installation. CSV export is explicit and formula-escaped.

## Real checks on this host

macOS 27.2 arm64, Apple M1, SDK 10.0.403, Release:

- `dotnet restore FlipOverlay.slnx --locked-mode` — PASS after the complete project-reference graph was regenerated and locked.
- `dotnet build FlipOverlay.slnx -c Release --no-restore` — PASS, zero warnings/errors.
- `dotnet test FlipOverlay.slnx -c Release --no-build --no-restore` — **177/177 PASS**, zero skipped: Core 70, Infrastructure 60, App 31, Platform 16. Raw TRX under `artifacts/test-results-checkpoint/`; captured command output in `docs/measurements/checkpoint-verification.json`.
- `dotnet format FlipOverlay.slnx --verify-no-changes --no-restore` — PASS.
- Actual executable `--selftest` — PASS, offline/no GUI; `--version` — **0.1.0**; `--ui-smoke` — exit 0 with the real macOS native window.
- Headless 5,000-row test: Table initially **5** realized rows, **6** after distant scroll; List **3** realized rows, distant index 4,900 reachable. Table and List selection is shared.
- Actual .NET Infrastructure network request on 2026-11-04: documented ninja leagues returned `Allflame`; exchange provider parsed **100 aggregate entries**, zero source observation timestamps, bids, asks or current depths. This is network/contract evidence, not live-price proof.
- Production C# deny-list scan — no forbidden API matches. This supplements review; it is not a ban-safety guarantee.
- One-shot offline benchmark run with 10,000 valuations / 5,000 candidates / 5,000 prepared manual routes, 30 measured cycles: see `PERFORMANCE.md` and raw `measurements/mac-development-current.json`. No Windows performance gate is claimed.

## Gates still open

1. **Windows 11 + PoE1** native overlay interaction, no-activate/click-through/hotkeys, monitor/DPI/negative coordinates and clean-VM setup/upgrade/uninstall. macOS compilation and fake-native tests cannot close these.
2. **Production performance:** 10-minute idle CPU, Windows private-byte peaks/stability, real-GPU hidden activity, cold start, UI stalls and three paired gaming frametime runs. Headless Mac benchmarks are not equivalent.
3. **Packaging:** current scripts/CI are local source only; actual Inno Setup build and Windows ZIP extraction/runtime require Windows. A cross-published folder is not Windows-tested. No fabricated setup.exe or test output is provided.
4. **Catalog truth and rights:** no runtime recipe is automatically verified for the current patch. The user-confirmed import is advanced configuration, not a curated authoritative database. Wiki research remains separate and unapproved for public redistribution. Gold calibration remains manual.
5. **Remaining UX scope:** monitor-specific geometry persistence, richer filters/top-N panel, scroll-anchor validation under all updates and a GUI hotkey editor are not fully accepted. Core supports more routes than the current two-leg Observed composer. Full native licence audit and Windows redistribution notices remain a release task.
6. **Other sources/platforms:** watch/GGG adapters are not selectable in the UI; no mass-distribution provider agreement is claimed. Linux compositor/game tests and PoE2 are separate future stages. Automatic live scanning is intentionally absent.

A passing local build is an engineering checkpoint, not the specification's final acceptance. Read `DATA-SOURCES.md`, `PERFORMANCE.md`, `USER-RECIPES.md` and the packaging report before testing or distributing anything.
