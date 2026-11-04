# Performance — measured development host, Windows gate open

## Scope

These are **local headless development measurements**, not Windows release acceptance and not a game-performance comparison. Host: Apple M1, 8 logical processors, macOS 27.2 build 26B123, arm64; .NET SDK 10.0.403 / runtime 10.0.14; Release, no debugger. Uncommitted working tree, no published build. Exact invocation and host metadata are embedded in `measurements/mac-development-current.json`.

Reproduce:

```text
dotnet run --project tools/Diagnostics/FlipOverlay.Diagnostics.csproj -c Release -- docs/measurements/mac-development-current.json
```

The one-shot diagnostic process uses **explicit offline fixtures only**: 10,000 aggregate valuations, 5,000 independent deterministic recipe edges, 5,000 prepared two-leg manual routes and 5,000 result rows. It does not feed the production GUI, fetch prices, place orders or remain resident. The graph is deliberately sparse; these timings do not establish worst-case arbitrary graph generation. Raw 30-cycle measurements, allocations and current memory counters are saved; p95 is nearest rank (`ceil(.95*n)-1`), no forced GC.

## Current run (2026-11-04 19:11 UTC)

| Operation | p95 | Scope |
|---|---:|---|
| Candidate generation/evaluation | 37.4038 ms | Traversal and estimates from the offline fixture graph |
| Evaluation of 5,000 prepared manual routes | 26.0896 ms | Integer sizing and unknown-gold paths |
| Incremental projection replacement | 13.1302 ms | Unattached to rendered UI |
| Numeric sort/reorder | 40.3705 ms | Alternating directions, 5,000 rows; UI observers absent |
| Combined cycle | 93.364 ms | The four operations above together |

Fixture construction: 10.672 ms. First candidate calculation: 44.1021 ms; first manual route calculation: 29.4438 ms; first projection: 28.0843 ms. These include their relevant first JIT work but not process startup, build or restore.

WorkingSet64 grew from 75,464,704 to 109,740,032 bytes across this run. Managed heap snapshots (without a forced collection) were 38,594,984 and 68,823,720 bytes. **PrivateMemorySize64 returned zero on macOS; it is unavailable here, not zero RAM.** These endpoint snapshots are neither peaks nor proof of stable retained memory. The Windows <=150/250 MB private-byte targets are not assessed. The short sleeping-console CPU sample is not the overlay's 10-minute idle CPU measurement and must not be presented as one.

## Actual headless virtualization test

`VirtualizationTests.Five_thousand_rows_realize_only_viewport_containers_and_share_selection_across_views`, Release, real Avalonia headless layout:

- Source rows: 5,000.
- Table initial realized rows: **5**; after scrolling to the end: **6**.
- List realized/visual containers: **3/3**.
- Viewport bounds: 1056 × 213 logical units for each view in the tested 1080 × 730 window.
- Distant item 4,900 can be realized, Table/List selection is shared, and the final table row is reachable.

This verifies the headless visual tree recycles containers; it is not a GPU, compositor, Windows or in-game measurement. The test runs on the Avalonia test dispatcher, not a second manually initialized global dispatcher.

## Not measured / not passed

- Windows reference PC, 10-minute idle CPU, private-byte peaks and 30-cycle retained-memory behavior.
- Windows cold UI start <=2s, UI thread stalls and real visual Table/List switch p95.
- Hidden GPU rendering/continuous activity, transparency and render-backend behavior on a real GPU.
- Three paired PoE1 frametime runs without/with overlay, median/p95/p99 and measurement noise.
- Linux compositor behavior and real Linux UI smoke.

No ReadyToRun gain is asserted. No stack substitution or aggressive GC setting is justified by these measurements. Windows/PoE1 measurements remain required before calling this production-ready.
