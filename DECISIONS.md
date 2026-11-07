# Skyrim Mod Splitter — Implementation Decisions

Product decisions D1-D16 live in IDEA.md section 15. This file freezes the
implementation decisions made to proceed without human interaction, plus the four
previously open scope boundary items (resolved here).

## Frozen implementation choices

| # | Topic | Decision |
|---|---|---|
| I1 | Runtime/framework | Core+CLI: net8.0; GUI: net8.0-windows WPF. .NET 8 LTS; verified WPF/runtime present. |
| I2 | Plugin library | Mutagen.Bethesda.Skyrim 0.50.2 (matches verified esp-explorer build; NuGet restore proven). |
| I3 | Solution layout | src/SkyrimModSplitter.Core, Cli, Gui; tests/SkyrimModSplitter.Tests; one Core project with namespaces Scan/Renumber/Split/Formats/Assets/Validation/Reports (fewer projects; GUI and CLI reference Core only). |
| I4 | GUI architecture | WPF, MVVM-lite (view models implement INotifyPropertyChanged manually; no extra UI framework). Background work via Task + IProgress + CancellationToken; no long I/O on the UI thread. |
| I5 | CLI parsing | Small in-repo argument parser (no System.CommandLine/Spectre dependencies) to keep release deterministic. |
| I6 | Serialization/logging | System.Text.Json (indented, deterministic ordering); logs to file + console/GUI; report files UTF-8 no BOM. |
| I7 | Tests | xUnit; synthetic plugins built with Mutagen for edge cases + real-mod acceptance tests gated by env var SMS_TEST_CORPUS; acceptance scripts under tests/acceptance/. |
| I8 | Test corpus location | test-corpus/ (gitignored); copies of the two provided mods; ground-truth summaries under test-corpus/ground-truth/ (committed). Sources remain read-only. |
| I9 | Target plugin choice | One mod folder per run; if multiple plugins, default = largest plugin by record count; GUI lets the user pick; CLI --plugin. |
| I10 | ESL-impossible fallback | Default: stop with a plan explaining the oversized hard unit. --allow-non-esl writes the affected part as a normal ESP with a loud warning. |
| I11 | Dependent mods | Default: detect + warn (when MO2 profile given), never modify another mod. --patch-dependents emits rewritten copies of dependent files (same filenames) for MO2 override install. No profile => report external safety unchecked. |
| I12 | ESM input | Allowed only when dependents/critical-master checks pass; preserve ESM flag and add ESL flag (light master) only if validation confirms the rule; otherwise refuse with reason. |
| I13 | Localized plugins | Refuse in v1 with a clear warning (STRINGS/DLSTRINGS/ILSTRINGS present). |
| I14 | BSA policy | Read-only: list/dump/unpack via BSArch; never pack or modify the original archive. Changed assets emitted as loose overrides; report every extracted entry. |
| I15 | Output layout | Default output/<ModName>-ESL/ (plugins + loose overrides preserving virtual paths + reports/ + manifest + README). In-place mode only into a verified copy with backup. Original folder hash recorded in the report. |
| I16 | Determinism | Stable record ordering, stable ID assignment (skip occupied, ascending), no timestamps in plugin writes (reuse original where possible; record chosen values in report), sorted archive listings, stable JSON key order, repeated runs byte-identical except documented timestamps. |
| I17 | Validation gates | V1 parse, V2 reference integrity (self+masters), V3 ESL rules, V4 load order/master DAG, V5 script compile, V6 data preservation (semantic dump diff), V7 stale-reference second pass, V8 assets; V9 runtime explicitly user-side/unclaimed. |
| I18 | GUI must-haves | Home/folder picker; scan dashboard; plan preview; apply progress+cancel; result with output open; WARNINGS/coverage viewer; verify; settings. GUI required v1; CLI parity for tests/automation. |
| I19 | Corpus/masters | Scan works without masters; full validation uses --data-dir (Stock Game Data) plus optional extra master dirs when located. Missing masters => warnings, never silent skip. |
| I20 | Autonomy rule | Do not ask the user for product decisions; infer from IDEA.md/DECISIONS.md. Stop only for genuinely missing external context/tool, per user instruction. |

## Resolved scope boundary items (from IDEA.md open list)

- Multiple plugins per folder: one target per run (largest by default; GUI/CLI selection).
- ESL impossible after split: stop with plan; --allow-non-esl opt-in for classic ESP part.
- Dependent patches: default warn-only; --patch-dependents emits file copies (requires
  MO2 profile). Never edit third-party folders.
- ESM input: allowed under I12 conditions, otherwise refused.

## Known evidence gaps (resolve during slices, not blockers)

- FaceGen path convention exact casing/padding — verify from a real mod with FaceGen;
  if none in corpus, build synthetic fixture and check against known convention.
- Voice/FUZ/LIP naming and SEQ regeneration feasibility — research + fixtures;
  CK fallback documented.
- Whether an ESM-flagged plugin can safely take the ESL flag as a light master — verify
  with docs/xEdit behavior; refuse if unproven.
- MO2 file-override precedence for loose files/plugins — verify with the profile/VFS
  docs; acceptance tests use structural checks.

## Slice-4 as-built decisions

| # | Topic | Decision |
|---|---|---|
| I21 | Split partition model | New dependency-closed master part ("Part 2", ESM+ESL) loads first; the part keeping the original filename is the dependent part (ESL) and may reference the master. Cells and their placed references are forced into the original-name part; the new master contains only flat records. |
| I22 | Split scope | Two-part split only in v1; more parts / ESM splitting / localized plugins are refused. |
| I23 | ESL NextFormID | ESL outputs set HEDR NextFormID to max own ID + 1 (12-bit-safe); validator fails values > 0x1000. HEDR stated record counts and master lists are otherwise preserved for conversions. |
| I24 | Dependents | Default: detect via MO2 profile or explicit search root and warn; no profile => explicit "external safety unchecked". --patch-dependents emits rewritten file copies under output/dependents; the tool never installs them. |
| I25 | Runtime gate | Structural validation only; in-game load (V9) is a user-side gate and is never claimed by the tool or by release notes. |

## Final-audit deferrals (Audit-4 PASS with MINORs)

| # | Topic | Decision |
|---|---|---|
| I26 | GUI verify/settings | A separate GUI Verify screen, MO2-profile picker, target-plugin picker and full Settings UI are explicitly deferred in v1. Verification is delivered inline (post-apply validation shown in the Conversion tab and written into reports); dependent scan/patch is CLI-only. Recorded in README/LIMITATIONS. |
| I27 | Known v1 minors (accepted) | F2 release-manifest hash is a sidecar written after the zip (the shipped zip intentionally has no self-hash); F3 split always replaces an existing --out and ignores the GUI Overwrite checkbox; F4 dependent patching re-parses dependent plugins per remap entry; F5 FaceGen inside BSAs is copied unreported (loose FaceGen warns); F6 dependent-patch errors and unreadable editable configs do not change exit codes/produce per-file warnings. These are non-blocking and documented in docs/LIMITATIONS.md. |
