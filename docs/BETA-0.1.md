# Beta 0.1 publication

User-authorized GitHub prerelease: **beta 0.1**, technical version **0.1.0-beta.1**, target **Windows 11 x64**. Public repository requested after sensitive-data checks. This is not a production-ready release.

## Verified locally before publication

- SDK 10.0.403, Release; locked restore, build/tests and formatting verification passed.
- **178 tests passed**: Core 70, Infrastructure 60, App 32, Platform 16. The additional version-output regression checks that prerelease information is preserved instead of silently printing only the numeric assembly version.
- `--version` prints `0.1.0-beta.1`.
- The portable ZIP is self-contained, but cross-built on macOS; native Windows/PoE1 behavior is not certified by that build.
- Packaging has 23 fixture checks, exact payload-hash read-back and immutable dependency-lock checks. No false installer is supplied.

## Publication hygiene

The selected source tree was scanned for GitHub/OpenAI/AWS-style keys, private-key blocks, credential-bearing URLs and game-session cookie values. No matches were found. This is a bounded scan, not an absolute guarantee. No `.env`, real observation database, portable user-data directory, account cookie, token store or SDK cache is part of the source tree or release payload.

Local home paths in logs/docs were replaced with placeholders; HTTP trace headers were reduced to basic provenance/cache metadata. Public publish disables debug symbols/PDB copying so personal build paths are not exposed via CodeView records. These are deliberate privacy redactions, not changes to measured timings/results. Original private raw response bodies are not bundled. The research fetch/test helpers resolve the environment instead of a personal absolute home path.

Only the sanitized initial history is intended for this first push. The unpublished local checkpoint was consolidated before publication; no previously published tag/release was moved.

## Release limitations

- Candidates use aggregate estimates and optional explicitly user-confirmed, patch-scoped recipe catalogs. Without a confirmed catalog they are empty. Source age may be unknown; gold/depth remain unknown rather than zero.
- Observed opportunities come from manual confirmed directed observations, not live NPC orders. No game input, OCR, memory access or private endpoint is included.
- Windows clean-VM installation, actual Inno Setup output, native overlay/hotkeys/DPI, live PoE1 interaction, CPU/RAM/GPU and gaming frametimes remain open acceptance gates. CI results must be read separately; local success does not imply hosted CI success.
- No independent guarantee of current recipe truth, fill probability, profit or ban safety is provided. Wiki-derived material and GGG assets have separate rights; no broad licence grant is implied by making original project source visible.

The first prerelease attaches the real portable archive and checksums. GitHub's source archives provide the tagged sanitized source. See `STATUS.md`, `USER-GUIDE.md`, `USER-RECIPES.md`, `PERFORMANCE.md` and `PACKAGING.md` for operating instructions and the remaining work.
