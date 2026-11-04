# User-confirmed directed recipe catalogs

Candidates are **aggregate estimates**, never live bid/ask, stock or promised net profit. No built-in vendor recipe is assumed current. The application can use a small **user-confirmed** catalog as a local input for estimated buy → deterministic NPC conversion → estimated sale screening.

## Workflow

1. Manually check every recipe's direction, integer ratio, NPC/act and conditions in the PoE1 patch you are actually playing. Do not infer reverse trades. Do not use random crafting or expected-value recipes.
2. Use **exact IDs returned by poe.ninja exchange overview** for inputs and outputs. Display names and IDs from other providers are not interchangeable. No fuzzy matching is performed. For research, use the documented economy API; do not use private APIs or game extraction.
3. Prepare the JSON below with your own verified facts. In the app, expand **Manual observation · Export · Settings**, paste the JSON, and enter the exact patch string separately. Tick the explicit verification checkbox and choose **Import confirmed catalog**. JSON edits or patch edits clear confirmation.
4. Select Candidates and Refresh. The configured settlement is `chaos`. A hard maximum-gold filter excludes candidates with unknown gold. A catalog's patch is **user asserted**, not independently detected or certified by the app. Re-check and replace it after game patches; saved configuration cannot prove current game validity.

Import checks the schema, game/realm/provider, positive quantities, exact patch confirmation, provenance, known check time, duplicate IDs and bounded size. It does **not** check game truth. Importing a catalog does not promote estimates into Observed and does not grant reuse rights over anyone else's dataset.

## Schema template — placeholders, NOT a valid game recipe

Replace every placeholder with your verified values. The illustrative integer quantities below demonstrate JSON shape only and are **not a PoE recipe**. This template is not loaded by the application.

```json
{
  "version": "your-catalog-version",
  "game": "poe1",
  "realm": "pc",
  "patch": "your-exact-verified-patch",
  "sourceId": "poe.ninja",
  "settlementId": "chaos",
  "recipes": [
    {
      "id": "your-directed-recipe-id",
      "inputId": "exact-provider-input-id",
      "inputUnits": 1,
      "outputId": "exact-provider-output-id",
      "outputUnits": 1,
      "npc": "NPC you checked",
      "act": "Act you checked",
      "condition": "Access requirements you checked",
      "source": "Your manual in-game check or attributed source",
      "checkedAt": "2026-01-01T00:00:00Z"
    }
  ]
}
```

Use the actual check time with timezone, not the template date. Maximum: 100 directed entries, 256 KiB JSON **both before and after safe serialization** (escaping can expand the stored form). Imports that would exceed the persisted bound are rejected before modifying the active catalog. Unknown fields are rejected. Current runtime supports PoE1 PC, poe.ninja aggregate valuations and chaos settlement only. Up to **two NPC conversion edges** are used, so purchase + conversions + sale remains at most four actions. Different economic entry points remain separate. Quantities and leftovers are whole units. Unconverted leftovers are retained in details but never credited as sale revenue. Both legs' exchange gold remains Unknown unless separately observed; no infinity or fabricated zero.

An empty result can mean missing exact ID prices, no positive estimate, the hard gold filter, mismatched settlement or no catalog. Refreshing the source does not make its unknown observation time known. Candidates deliberately retain **Age unknown** and **Estimated — verify in game**.

This is an advanced local configuration path, not an automatic verified recipe database. Wiki research in `RECIPES-RESEARCH.md` has unknown patch coverage; it is not silently imported.
