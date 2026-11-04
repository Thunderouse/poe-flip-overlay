# PoE Flip Overlay — local development build

This is an incomplete PoE1 research build. It is **not production-ready**, and no result guarantees a fill or profit. It never clicks or places an order in Path of Exile. Keep the game in borderless/windowed mode if testing an overlay on Windows; actual in-game behavior has not been certified.

## Accepted modes

- **Candidates** are estimates from permitted aggregate economy data. They are not live bids/asks or available stock. Verify both directions, gold and the actual order quantities yourself in the game before acting. When the source provides no observation time, the UI must say **Age unknown**. Fetched is only the time of the HTTP response.
- **Observed** requires two independently entered, confirmed directed observations with known source observation times. Manual observations are not a live API, expire according to the user-selected limit and do not guarantee repeatable trades. If there is no valid route, the view stays empty. Unknown depth/gold is not zero.
- The NPC Exchange live scanner is **not implemented**. Do not substitute player-trade prices. OCR, game input, cookies, account login, browser scraping and automation are absent by design.

A current build may expose only a subset of these features. Consult `docs/STATUS.md` for measured implementation coverage; do not infer functionality from this guide alone.

## Local build

Use the .NET 10.0.403 SDK selected in `global.json` and the locked NuGet package graph. From the repository:

```text
dotnet restore FlipOverlay.slnx --locked-mode
dotnet build FlipOverlay.slnx -c Release --no-restore
dotnet test FlipOverlay.slnx -c Release --no-build --no-restore
dotnet run --project src/FlipOverlay.App -c Release --no-build -- --selftest
```

For a Windows self-contained build, `tools/build.ps1` uses Inno Setup for a per-user setup and the *same* publish folder for a portable ZIP. `tools/verify.ps1` verifies only ZIP paths, version and offline selftest in a Windows path with Unicode/spaces; it does not replace an in-game/installer VM test. Nothing is published by these scripts.

## Windows overlay controls (native behavior not yet verified)

Windows startup attempts to register recovery hotkeys **before** enabling passive click-through mode. Defaults:

- **Ctrl+Shift+F8** — show/hide the application's own panel.
- **Ctrl+Shift+F9** — switch interactive / passive click-through.
- **Ctrl+Shift+F10** — refresh application data, subject to cache and source limits.

Use interactive mode to edit fields; passive mode does not receive ordinary mouse input. If registration fails, the app reports a conflict and retains an interactive companion window rather than enabling an unrecoverable passive window. There is no global keyboard hook and no game action is generated. The operating system may reserve/conflict with combinations; no game-binding detection is claimed.

Advanced configuration uses process environment variables `FLIP_OVERLAY_HOTKEY_VISIBILITY`, `FLIP_OVERLAY_HOTKEY_INTERACTIVE`, and `FLIP_OVERLAY_HOTKEY_REFRESH`, such as `Control+Shift+F8`. Use distinct combinations with F8–F11; F12 is reserved. These variables affect the application only. Linux/macOS remain companion windows, without a claimed overlay/hotkey adapter. `--ui-smoke` deliberately skips Windows overlay activation and is not a hotkey test.

## Portable data

`portable.flag` beside the executable selects `user-data` beside the executable. An installed build must use a user-scoped data location. A read-only portable folder must fail visibly rather than silently writing to AppData. Back up `user-data` before upgrading the portable binary folder; do not put old user data into a new ZIP. Do not treat portable as leaving no OS traces.

No network login, secret, telemetry or game account is required by the offline selftest. GitHub update checking (if configured) may notify about a release, but must never download or install it without an explicit user action.

## Unclosed verification gates

Windows 11 real-GPU performance, PoE1 borderless-window interaction, DPI/multiple monitors, installer upgrade/uninstall on a clean VM, Linux desktop overlays, real fee calibration and data rights are not established by macOS compilation or a CI job. See `docs/STATUS.md` for the exact current state.
