# Core integration contract

## Existing callers

`MarketContext`, `ManualObservation`, `RouteRequest`, `RouteStep`, `RouteResult`, `AggregateValuation`, `CandidateRequest`, `EstimatedCandidate`, `IGameProfile`, `IRecipeCatalog`, and `IGoldCostModel` retain existing construction parameters. `RecipeEdge` adds **optional trailing** `Game`, `Realm`, `CatalogVersion`, and `Act` fields; existing constructor calls remain valid. `CandidateEngine.Evaluate` adds an optional fourth `DateTimeOffset? now` argument for deterministic checks; existing three-argument calls still work. Candidate IDs now include the source ID to avoid collisions between independent estimates. Consumers should treat IDs as opaque.

No HTTP, storage, platform, UI, or runtime configuration is implemented in Core. No production recipes, community recipes, current patch claims, or game gold formulas are supplied.

## Manual batches

- Evaluate a directed settlement-to-settlement route of 2–4 actual operations. Internal currencies cannot repeat; a completed trade cannot be fed back into the same route to advertise compounded opportunities.
- The entire `MarketContext` must match, including game, realm, league, patch, catalog version, and market kind. Empty metadata and unknown/unverified/latest patch sentinels fail closed. IDs and source/stock provenance must be present.
- Every observation must be user-confirmed, have known observation time, be strictly younger than `MaximumAge`, and satisfy `observedAt <= fetchedAt <= now`. Download time never renews observation age. Route age uses the oldest observation.
- Rates are positive exact integer input/output batches. Depth must be known, nonnegative, and explicitly shared by stable stock IDs. Contradictory definitions of one stock are rejected. Unknown depth is not zero and is not executable.
- `FeePerBatch` is a **confirmed charge denominated in the settlement currency** on each used batch of each leg, reserved in the initial capital budget. It is not a percentage or a gold fee. Callers must not silently pass a charge in an intermediate currency.
- Integer bisection first finds an affordable purchase, then reduces spending that cannot increase downstream output. Price-depth boundaries and a bounded two-leg rounding-phase/aligned-lot search are considered so a large budget does not automatically hide a small profitable fill. This is **not a proof of global optimal profit across every integer size**. Negative results remain possible and callers may filter them. Arithmetic overflow returns null rather than fabricated numbers.
- `IGoldCostModel` must be pure/deterministic, and confirmed charges must be nonnegative and nondecreasing as input/output amounts grow. This monotonic contract is necessary for bounded sizing; arbitrary/nonmonotonic models are not supported. Both exchange legs are charged. Only Confirmed, nonnegative amounts with source, matching patch, and nonfuture checkedAt enter manual Gold. Estimated/invalid/missing costs remain unknown and cannot satisfy a hard GoldBudget. Negative gold budgets are invalid. Confirmed zero has no profit-per-gold value, never infinity.
- `Remainders` and step remainders report **integer unconverted currency quantities before settlement-currency fee reservation**. They are not a spendable balance: fees are separately reserved in `CapitalRequired`. Decimal fees cannot be faithfully represented in the existing integer remainder dictionary. Do not value these leftovers or display the settlement remainder as net wallet cash.

## Aggregate candidates

Candidates remain estimates, not bid/ask, depth, confirmed gold, or executable observations. Purchase and sale estimates must belong to the same source and exact context; duplicate estimates of one item within that source are excluded rather than averaged or selected arbitrarily. Different sources can produce independent results, not a synthetic cross-provider spread.

Every ordinary candidate uses one purchase + at most **two NPC conversions** + one sale, for the specification's four-operation limit. The earlier test that allowed four NPC conversions counted only graph edges, missing purchase/sale; it was replaced with the full-operation requirement. No reverse edge is inferred. Exact integer recipe batches preserve unvalued leftovers. All acquired input is charged, including unused inputs.

The candidate period is the oldest of the purchase/sale source period ends, or unknown if either is unknown. `FetchedAt` remains a separate download timestamp. There is no candidate age-to-live promotion or aggregate freshness filter; old/unknown periods can be shown only as estimates. Future/inconsistent source/fetch times and invalid metadata are excluded. Overflowing quantities/revenue/ROI are skipped. Enumeration is capped at 5,000 candidates; this is not an exhaustive optimization guarantee for enormous catalogs.

## Versioned verified JSON catalog

`JsonRecipeCatalog.Load(string json, DateTimeOffset now, string? verifiedCurrentPatch = null)` parses a local string using System.Text.Json. It does not read files or access a network. Malformed or invalid documents throw `JsonException`; callers should catch it, show a configuration error, and retain an `EmptyRecipeCatalog` rather than fall back to guessed data.

Parsing is **not verification**. A catalog remains inactive unless the caller explicitly supplies a previously verified current patch exactly matching the document patch. Do not derive this activation argument from the catalog itself, an arbitrary community download, or a league name. With no independently verified patch, use `EmptyRecipeCatalog` (the production default).

GetRecipes requires an exact game/realm/patch/catalogVersion match. League is intentionally not part of recipe scope; league-specific restrictions must be represented by the profile/conditions and checked by the user. A future unknown patch yields no recipes. Loaded edges carry their scope so passing them directly to CandidateEngine cannot bypass an explicit mismatch.

Schema v1 envelope/template (empty and **inactive**, placeholders are not verified data):

```json
{
  "schemaVersion": 1,
  "game": "REPLACE_WITH_PROFILE_GAME_ID",
  "realm": "REPLACE_WITH_REALM_ID",
  "patch": "REPLACE_WITH_INDEPENDENTLY_VERIFIED_PATCH",
  "catalogVersion": "REPLACE_WITH_CATALOG_VERSION",
  "recipes": []
}
```

Each recipe object must contain exactly:

| Field | Contract |
| --- | --- |
| `id` | Nonempty unique stable recipe ID |
| `inputId`, `outputId` | Nonempty distinct controlled item IDs, exact direction |
| `inputUnits`, `outputUnits` | Positive Int64 integers; exact batch ratio, no floating quantity |
| `patch` | Exact envelope patch |
| `source` | Nonempty auditable evidence attribution/reference |
| `checkedAt` | Nondefault ISO-8601 timestamp no later than loader `now` |
| `npc`, `act`, `condition` | Nonempty NPC, act/location, and prerequisites; explicitly say no prerequisite if verified |

Unsupported schema versions, unknown properties, duplicate recipe IDs, missing scope, zero/fractional/negative ratios, same-item edges, missing prerequisites, and future verification times fail closed. Recipes are immutable to callers after loading. No random outcomes, implicit reversibility, or automatic community activation exist.

Legacy hand-built RecipeEdges may omit optional scope fields for source compatibility; the caller then owns profile isolation. The application's user-confirmed import uses the separate `UserRecipeCatalog` envelope documented in `USER-RECIPES.md`, validates the same scoped edges, and requires an explicit patch assertion. That envelope is not interchangeable with the lower-level `JsonRecipeCatalog` schema above. Programmatic fixture catalogs are not production verification.

## Remaining gates

Source attribution and a checkedAt string do not prove correctness, rights to redistribute, NPC access, or patch validity. Human verification and licensing review remain required. NPC prerequisites are carried as metadata, not game-state detection; the application cannot know the character's progression. The candidate engine supports settlement-funded NPC purchases using the exact identity 1 settlement unit = 1 settlement unit; this does not invent an API quote or source observation time. It does not yet model arbitrary multi-input/multi-output recipes, stochastic crafting, or candidate fee estimates. Manual records remain explicit directed observations. There is no game/character balance reader or automation.
