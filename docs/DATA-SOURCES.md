# PoE Flip Overlay — Data/policy gate

Checked: **2026-11-03**, local system clock MSK (UTC+03); HTTP evidence captured at 19:39–19:41 UTC. This is a research deliverable, not an application or release. The attached user specification is authoritative. No repository, publication, OAuth registration, account access, game interaction, OCR, private endpoint, or production UI was created.

## Verdict: live gate NOT CLEARED

**«Доступна только история; автоматическое ранжирование по актуальным котировкам NPC-биржи пока не подтверждено».** More precisely, the permitted endpoints checked expose historical digests and aggregate analytics, not a verified currently executable NPC order book.[2][4][5]

This is a bounded investigation, not proof that no suitable source exists anywhere. Searches for other live APIs and Wealthy Exile developer permissions did not establish an approved alternative (`evidence/discovery-search.json`). Do not replace the requested live product with Candidates/manual without explicit acceptance.

## Source matrix

| Source | Market | Access | Permission / licensing | Source time | Publication cadence | Bid/ask | Current depth | Gold fee | Limits / result |
|---|---|---|---|---|---|---|---|---|---|
| GGG public Currency Exchange | Historical NPC exchange | Anonymous HTTP 200 | Documented public endpoint; API policy applies [1][2] | Hourly digest, cursor | Hourly; current-hour data unavailable [2] | No live sides | No: historical lowest/highest stock only | Not supplied | End-of-stream waits for next hour; no complete numeric public-endpoint quota verified |
| poe.ninja exchange overview | Aggregate exchange analytics | Anonymous HTTP 200 | Economy endpoints allowed, no SLA; backend caching recommended [4] | No explicit source timestamp found in captured JSON | Docs: PoE1 ~15 min; approximate, not SLA [4] | Not supplied | Not supplied; volume is not stock | Not supplied | Actual cache max-age=1800, SWR=300, stale-if-error=86400; ETag; age=412 at capture |
| poe.watch exchange ratios | Aggregate exchange analytics | Anonymous HTTP 200 | Public OpenAPI; redistribution/volume terms still need confirmation [5] | Captured `timestamp`, `lastTradeAt`, aggregation `window.start/end`; semantics not fully specified in schema | Not verified; cache max-age=1200 is NOT source cadence | Not supplied | Not supplied | Not supplied | `value` documented as mean price; actual canonical method `volumeWeightedMean` [5] |
| Wealthy Exile | Not established for reusable feed | Not configured | No first-party reusable API contract/permission established | Unknown | Unknown | Unverified | Unverified | Unverified | Unsupported; economy page was not scraped; no private actions called |
| PoE Wiki | Metadata / recipes / gold reference, not prices | Documentation HTTP 200 | Copyright page: CC BY-NC-SA 3.0 for material wiki may license [7] | Page revision is not quote time | No market cadence | N/A | N/A | Community reference, not calibrated model [8] | Cargo documentation reviewed [6]; no catalog imported |
| PoE2 Scout | Future PoE2, not PoE1 | Swagger shell HTTP 200 | Schema/terms not fully audited [9] | Unverified | Unverified | Unverified | Unverified | Unverified | Deferred; no PoE2 integration |

The two aggregators are not automatically independent market confirmations; endpoint names and fresh HTTP dates do not establish live provenance. Their own source/aggregation lineage must be retained rather than manufacturing corroboration.

## Real captures (not synthetic fixtures)

All bodies, HTTP response headers, URL, fetch timestamp, byte count, and SHA256 are under `evidence/`. No account credentials were supplied. These files are research evidence, not permission to redistribute third-party datasets in a release.

- `ggg-history.body`: 109 markets, `next_change_id=1722027600` → **2024-07-26 21:00:00 UTC**. This is an old initial cursor, not a latest-market timestamp. The no-ID endpoint explicitly starts at the first historical hour.[2] No attempt was made to replay years of history or fabricate an undocumented latest-cursor shortcut.
- `ninja-exchange.body`: 100 lines and 100 items; top-level `core/lines/items`. Captured fields include `primaryValue`, `volumePrimaryValue`, `maxVolumeCurrency`, `maxVolumeRate`, `sparkline`. Structural checks found no explicit source timestamp, bid, ask, or depth keys. Model this snapshot as **Age unknown**, with Fetched separately, never Live.
- `watch-ratios.body`: 1,035 items (not all currency). The first real item's canonical `price` uses `volumeWeightedMean`, a 168-hour window ending **2026-11-03 19:05:29 UTC**, and `lowConfidence=true`. Its `lastTradeAt` is a separate value. Another currency uses a 4-hour window. Do not equate the window end, last trade, canonical mean, side mean, or HTTP Last-Modified.
- The actual watch response has canonical `price`, `method`, `window`, and `lastTradeAt` fields not fully described by the captured OpenAPI. Preserve these as raw evidence; do not promote them to executable quote semantics without provider clarification.
- `ninja-leagues.body`: IDs returned by API include `Allflame`, `Hardcore Allflame`, `Standard`, `Hardcore`. The first returned ID was used for this bounded probe, not hardcoded into an application. The document says the first entry is the current temporary challenge league.[4] It does not provide the complete category/rules/start/end/realm taxonomy required for robust product league selection.
- `watch-leagues.body` includes a Solo Self-Found entry and sentinel end dates. A future implementation must not trust names alone, offer SSF for trading, or treat sentinel dates as valid season-end timestamps.

## Cache findings that change implementation assumptions

The ninja documentation describes roughly five-minute HTTP caching, but the actual response permits **30 minutes** of freshness and stale serving on error. Respect the response headers rather than implementing the illustrative five-minute value.[4] HTTP cache age and stale allowance never fill missing source observation time.

Watch's 20-minute HTTP TTL is a cache instruction, not proof of 20-minute publication. One capture cannot establish volatility, reliable cadence, or an optimal refresh interval. No production scheduler interval or freshness TTL has been approved from these captures.

## Policy / authorization

GGG's checked policy says new application registrations cannot currently be processed. It also prohibits reverse-engineering undocumented endpoints.[1] Existing public-client OAuth uses authorization code with PKCE and cannot use `service:*` scopes.[3] The public history endpoint succeeded without cookies; do not ask for POESESSID to remove the live-data blocker.[2]

GGG's general User-Agent guidelines specify an OAuth-prefixed identity for registered API clients.[1] This research used an explicit unregistered research identity/contact, not browser impersonation; a production API identity and any applicable public-feed identity requirements still need to be settled. OAuth scopes cannot transform historical data into a live order book.

No support email was sent. No policy guarantee or ban-safety guarantee is claimed.

## Wiki/gold caveats

The gold reference describes cost per received item and distinguishes manual order closure from automatic fraction-related cancellation.[8] It is not safe to apply one universal refund assumption. Patch-specific fees, partial-fill rounding, cancellation behavior, and both market legs still require in-game calibration. Unknown gold remains Unknown. No numeric fee model or vendor recipe was accepted here.

The dedicated copyright page names BY-NC-SA 3.0, while its site footer uses a shorter NonCommercial label. Preserve the dedicated page evidence and clarify redistribution before a release; do not extend a wiki dataset licence to the application's original code or assume rights over GGG assets.[7]

## Verification and limitations

Run `python3 verify_evidence.py` from the research directory. Actual run: **21/21 checks passed**, exit 0: 14 body-integrity checks and 7 structural observations. These are not xUnit tests, app correctness checks, performance benchmarks, or a live-data approval. Python is used only for disposable research capture/verification, not application runtime.

No executable, setup, portable app, profit engine, recipes, manual observation UI, Windows interaction, GPU measurements, Linux CI, or game benchmark has been implemented or tested. Production readiness is explicitly **NO**.

Required decisions before implementation:
1. Accept Candidates + manual confirmed observations, accept historical Candidates only, or keep the automatic live requirement blocked pending a documented approved feed.
2. Agree on a repository directory; proposed `<repository>`. That directory was absent during discovery; research remains outside it.
3. Resolve any stack availability gate documented separately. Do not silently replace TableView, Avalonia version, or the prescribed stack.

## Evidence handling

A research archive should exclude response Set-Cookie headers (if present), caches and credentials; its data bodies retain their SHA256 checks. No publish/commit/tag/release authorization is implied. Research raw documents are for private verification; review licensing before public redistribution.

## Sources

[1] https://www.pathofexile.com/developer/docs
[2] https://www.pathofexile.com/developer/docs/reference
[3] https://www.pathofexile.com/developer/docs/authorization
[4] https://poe.ninja/docs/api
[5] https://poe.watch/docs.yaml
[6] https://www.poewiki.net/wiki/Path_of_Exile_Wiki:Data_query_API
[7] https://www.poewiki.net/wiki/Path_of_Exile_Wiki:Copyrights
[8] https://www.poewiki.net/wiki/Currency_exchange_market
[9] https://api.poe2scout.com/swagger
