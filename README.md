# PoE Flip Overlay — beta 0.2

C#/.NET 10 + Avalonia beta (`0.1.0-beta.2`) for **PoE1 Candidates and user-confirmed manual observations**. Automatic live NPC exchange scanning is deferred. No game input, OCR, memory access, private endpoints or account cookies.

Starts as a normal interactive window with a title bar and standard Windows controls. Click-through is optional; see [beta 0.2 changes](docs/BETA-0.2.md).

**Not production-ready.** Actual Windows game-overlay, installer VM and resource gates are open. There is no verified current recipe database: Candidates require an explicit user-confirmed patch catalog; without it they are empty. All aggregate values remain estimates. No guaranteed fills, profit or ban safety.

- [Current implementation and open gates](docs/STATUS.md)
- [User guide](docs/USER-GUIDE.md)
- [User-confirmed recipe catalogs](docs/USER-RECIPES.md)
- [Data/policy investigation](docs/DATA-SOURCES.md)
- [Development environment](docs/DEVELOPMENT.md)
- [Packaging and actual checks](docs/PACKAGING.md)
- [Measured development-host performance](docs/PERFORMANCE.md)
- [Accepted decisions](docs/DECISIONS.md)

Use the SDK pinned by `global.json` and existing lock files:

```text
dotnet restore FlipOverlay.slnx --locked-mode
dotnet build FlipOverlay.slnx -c Release --no-restore
dotnet test FlipOverlay.slnx -c Release --no-build --no-restore
dotnet format FlipOverlay.slnx --verify-no-changes --no-restore
dotnet run --project src/FlipOverlay.App -c Release --no-build -- --selftest
```

`--selftest` is offline. `--ui-smoke` opens/closes a native window with no market source. Normal startup uses permitted economy HTTP requests. Nothing is uploaded/published by the build scripts; remote publication requires separate authorization. Runtime is .NET/Avalonia only; disposable Python research scripts are not shipped as app dependencies.
