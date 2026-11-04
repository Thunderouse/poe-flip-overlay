# PoE1 fixed NPC currency conversions — bounded research

## Result and confidence

Four directed, fixed-price **NPC shop purchases** are supported by the retrieved PoE Wiki Vendor price table. They are not player-market orders, Faustus exchange quotes, or sell-window recipes.[3][5] Status: **Community-verified; not in-game calibrated**. This is research evidence only: no production catalog or schema mapping is supplied.

`checkedAt`: **2026-11-04T08:40:58.794691+00:00** for the revision-bearing recipe/NPC response. This is retrieval time, not game observation time or proof of validity in a particular patch. All captures have their own exact `checkedAt` in adjacent metadata files.

## Verified directions

The table below reads the source's **Price → Orb**, never the reverse. All output amounts mean one purchased unit. Source column headers are `Orb`, `Price`, and `First Location`.[3][5]

| Player pays (input) | Player receives (output) | First NPC / act stated | Access scope | Recipe patch |
|---|---|---|---|---|
| 2 × Orb of Alteration | 1 × Jeweller's Orb | Yeena, Act 2 | Reach Yeena's shop in The Forest Encampment; additional unlock conditions unknown | Unknown |
| 4 × Jeweller's Orb | 1 × Orb of Fusing | Yeena, Act 2 | Reach Yeena's shop in The Forest Encampment; additional unlock conditions unknown | Unknown |
| 4 × Orb of Chance | 1 × Orb of Scouring | Yeena, Act 2 | Reach Yeena's shop in The Forest Encampment; additional unlock conditions unknown | Unknown |
| 2 × Orb of Scouring | 1 × Orb of Regret | Yeena, Act 2 | Reach Yeena's shop in The Forest Encampment; additional unlock conditions unknown | Unknown |

Every price and first-vendor row above is explicitly present in Vendor; Yeena's page separately identifies The Forest Encampment in Act 2.[3][5] “Reach the shop” describes physical access, not a newly inferred quest unlock. The retrieved pages do **not** establish a conversion-specific prerequisite quest, minimum character level, or absence of extra conditions; these remain unknown. Yeena's quest list is not evidence that those quests unlock these purchases.[5]

Only the Act 2 access path is retained. No extension to all vendors, hideout availability, Ruthless, special events, every realm/league, or PoE2 is asserted. No reverse edges, rates inferred by inversion, or extra chained recipes have been added. The research is suitable for manual candidate review, not an executable-opportunity guarantee.

## Provenance and patch handling

The revision-bearing response contains Vendor revision **1699119**, source revision timestamp **2026-08-05T17:38:15Z**, and Yeena revision **1578913**, timestamp **2025-10-26T07:44:33Z**.[5] The verifier confirms that Vendor's text in this response exactly matches the separately captured parse response.

Orb of Fusing's page independently repeats the four-Jeweller's price and names Yeena; Orb of Scouring and Orb of Regret repeat their four-Chance and two-Scouring purchase prices respectively. These are cross-checks within the same community wiki, not independent in-game observations.[5] The Jeweller's purchase price is supported directly by Vendor; its item page is not represented as a second price source.

**Recipe patch is unknown for all four rows.** The captured version histories do not attach a specific patch to these four purchase prices. Item `release_version`, unrelated item mechanics changes, NPC identification changes, page edit dates, and the retrieval date must not be mapped to `validFromPatch`, `verifiedPatch`, or a game build.[3][5] Current-stock quantity, trade throughput, gold fee, limits, and current player-market profitability were not measured; do not populate them with guessed numbers or zero defaults.

## Documented API path

The Wiki's own data-query documentation points to MediaWiki and Cargo, recommending Cargo for structured item data.[1] Its live API help documents `cargoquery`, `parse`, and `query+revisions`.[6] This research used only anonymous, read-only calls to the documented Wiki API, with a descriptive research User-Agent; no credentials, gameplay access, private endpoints, or browser impersonation.

Cargo `items.name` returned the four requested output-item names, including HTML-escaped `Jeweller&#039;s Orb`.[7] HTML entity decoding is needed for comparison. This query establishes item names only: **it does not establish NPC conversion quantities or access conditions**. Those come from MediaWiki wikitext, whose explicit table headings and full rows are preserved.[3][5] No hypothetical Cargo vendor-recipe schema was invented.

The initial Vendor recipe system lookup explicitly directs ordinary shop inventory/costs to Vendor; it is retained as discovery evidence, not as the source of these prices.[2]

## Preserved evidence and reproducibility

Everything is under `docs/recipe-evidence/`:

| Stem | Purpose |
|---|---|
| `api-docs` | Current Wiki data-query documentation |
| `vendor-recipes` | Discovery: distinguishes sell-window recipes from shop prices |
| `vendor` | Complete Vendor wikitext with the price table |
| `copyrights` | Wiki's dedicated licensing statement |
| `recipe-pages` | Six revision-bearing pages: Vendor, Yeena, and four output currencies |
| `api-help` | Live help for the three API modules used |
| `cargo-items` | Four output-item name matches |

Each stem has the **unaltered HTTP response body** (`.body`) and a `.metadata.json` sidecar containing requested URL, final URL, checkedAt, status, response headers, byte length and SHA256. `sources-ledger.json` records citation URLs. `fetch_evidence.py` was read before use and retained unchanged; it refuses to overwrite an existing body. `verify_evidence.py` is an offline research verifier, not C# application runtime or a catalog generator.

The first two Python fetch attempts failed locally with `CERTIFICATE_VERIFY_FAILED`; no response was fabricated. Retrying with `SSL_CERT_FILE=/etc/ssl/cert.pem` used the system trust bundle and succeeded, without disabling certificate checks. For a new capture, supply an unused stem:

```sh
SSL_CERT_FILE=/etc/ssl/cert.pem python3 docs/recipe-evidence/fetch_evidence.py NEW_STEM 'DOCUMENTED_WIKI_API_URL'
python3 docs/recipe-evidence/verify_evidence.py
```

Actual offline result: **PASS** — 7 response-body integrity/status/provenance checks, 4 exact vendor-row matches, 4 unique Cargo output names, six revision-bearing pages, Yeena's Act 2 location, and equality of the Vendor parse/revision texts. This validates the captured evidence structure, not the game's behavior. No production files, commits, or releases are part of this work.

## Attribution and reuse boundary

Source attribution: **Path of Exile Wiki contributors**, page titles and API URLs listed below; exact page revisions are retained in `recipe-pages.body`. This document selects and paraphrases four entries; original response bodies are preserved without modification.

The Wiki's dedicated copyright page licenses content it can lawfully license under **Creative Commons Attribution–NonCommercial–ShareAlike 3.0 Unported (CC BY-NC-SA 3.0)** and reserves underlying content/material rights to their respective owners.[4] Treat this Wiki-derived research dataset accordingly; **GGG intellectual-property rights are separate**. Do not assume commercial reuse permission, apply the Wiki dataset licence automatically to original application code, or publish these captures as an approved release asset. Review attribution, noncommercial, share-alike and underlying-IP obligations before redistribution.

## Sources

[1] https://www.poewiki.net/w/api.php?action=parse&page=Path_of_Exile_Wiki%3AData_query_API&prop=wikitext&format=json — api-docs
[2] https://www.poewiki.net/w/api.php?action=parse&page=Vendor_recipe_system&prop=wikitext&format=json — vendor-recipes
[3] https://www.poewiki.net/w/api.php?action=parse&page=Vendor&prop=wikitext&format=json — vendor
[4] https://www.poewiki.net/w/api.php?action=parse&page=Path_of_Exile_Wiki%3ACopyrights&prop=wikitext&format=json — copyrights
[5] https://www.poewiki.net/w/api.php?action=query&prop=revisions&titles=Yeena%7CJeweller%27s_Orb%7COrb_of_Fusing%7COrb_of_Scouring%7COrb_of_Regret%7CVendor&rvprop=ids%7Ctimestamp%7Ccontent&rvslots=main&format=json — recipe-pages
[6] https://www.poewiki.net/w/api.php?action=help&modules=cargoquery%7Cparse%7Cquery%2Brevisions&format=json — api-help
[7] https://www.poewiki.net/w/api.php?action=cargoquery&tables=items&fields=name&where=name+IN+%28%22Jeweller%27s+Orb%22%2C%22Orb+of+Fusing%22%2C%22Orb+of+Scouring%22%2C%22Orb+of+Regret%22%29&limit=10&format=json — cargo-items
