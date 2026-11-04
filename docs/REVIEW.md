# Development checkpoint review

## Independent review outcomes

Two read-only reviews inspected economic correctness and the user-confirmed catalog integration. No security concern was reported in those scopes. They did **not** certify Windows, game interaction, dataset truth, a globally optimal batch-size algorithm or production readiness.

1. The economic audit reproduced a high-severity max-budget sizing failure: buying 100 B with 1 B sellable stock hid the valid 10 A → 1 B → 12 A batch. Additional whole-lot and worse-depth cases were reproduced. The route engine now reduces unproductive input spending and evaluates depth boundaries and bounded rounding/alignment candidates. Independent follow-up confirmed the requested cases (capital/revenue/profit 10/12/2, 9/10/1, and the profitable first depth tier). The older batch test expectation was independently checked as 6/7/1 with settlement remainder 5. Further rounding-phase and integration regressions were added. This is bounded sizing, **not a global integer-optimization proof**.
2. Follow-up found a medium-severity persistence defect: a short imported JSON catalog could expand past the parser's limit during safe serialization, be stored successfully and fail on the next refresh. A regression first failed, then passed after the exact canonical serialized form was reparsed and bounded **before** SaveSetting. The final independent targeted check reported `passed=true`, no security concerns or logic errors, and **3 catalog integration tests passed**. Valid import/save/refresh still works; oversized serialized catalogs are rejected without replacing the active catalog.

## Local integration checks

Full Release suite after those fixes: **177 tests**, no failures or skipped tests (Core 70, Infrastructure 60, App 31, Platform 16). The solution restores with existing locks and builds without warnings/errors. Formatting and production forbidden-API scan pass. Real macOS native UI smoke and offline selftest are separate checks; neither invokes a real Windows game overlay.

The TableView columns now use typed compiled bindings; the headless 5,000-row test asserts that actual route text is displayed, not only that a row collection is attached. Windows bridge startup is wired before Show and explicitly skipped by UI smoke/non-Windows operation; native behavior remains a Windows manual test gate.

Packaging parser/fixture checks passed independently (23 archive checks). The source integrator ran the partial macOS→win-x64 build end-to-end, fixing a reproduced multi-RID restore failure without disabling lock verification. There is no Inno Setup compiler/clean Windows VM result and no fabricated installer.

See `STATUS.md`, `PERFORMANCE.md` and `PACKAGING.md` for limitations and exact acceptance gates. This checkpoint does not supersede them.
