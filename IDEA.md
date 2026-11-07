# Skyrim Mod Splitter — Idea, Scope & Decision Log

> **Status:** idea stage with frozen product decisions (see section 15). No
> implementation or code yet.
> This file records the problem, intended outcomes, and the decided scope so the
> context-collection and technical-design phases start from one shared baseline.
> Anything marked `[VERIFY]` must be confirmed against real plugin bytes, a real
> mod folder, a tool's own documentation, or runtime behavior before it becomes
> a design constraint. Claims in this file are hypotheses, not evidence.

| | |
|---|---|
| Working name | **Skyrim Mod Splitter** (desktop app; CLI working name `sms`) |
| Repo | `D:\Works\source\skyrim-moddings\skyrim-mod-splitter` — git repo, remote `github.com/letuhao/skyrim-mod-splitter`, branch `main`, MIT, initial commit only |
| Direction | .NET tool built on the **Synthesis/Mutagen idea**, shipped as a **Windows desktop app (WPF GUI)** plus a CLI over one shared core |
| Platform target | Windows, Skyrim Special Edition / AE (SSE 1.5.x–1.6.x), 64-bit plugins |
| Non-support | Native SKSE DLL rewriting (declared out of scope in the README) |
| Primary use case | One downloaded mod folder whose classic ESP/ESM should become ESL-safe; assumed no other mod depends on it (MO2 profile optional for checking) |
| Delivery | **WPF desktop GUI is required in v1** — normal use must not require a CLI or an AI agent; the CLI exists for automation/tests and shares the same core |
| Revision | r0.3 — 2026-11-08 — GUI moved into required v1 scope (WPF desktop app); scope, UX, architecture and milestones updated |

---

## 1. One-line pitch

Point the tool at one unpacked Skyrim mod folder and it turns the mod's classic
ESP/ESM into an **ESL-safe (ESPFE) plugin**: it renumbers new records, sets the ESL
flag, and rewrites every FormID reference it can reach (records, VMAD/Papyrus,
INI/JSON/TXT configs, FaceGen assets). If the content cannot fit one ESL, it splits
it into multiple ESL parts — the split exists only to make ESLification possible.
The original BSA is never modified (changed assets become loose overrides), and the
tool **tells the user exactly what it could not rewrite** instead of silently
producing a broken mod. A desktop GUI drives the whole flow — everyday runs need
no command line and no AI agent.

---

## 2. Why this tool should exist

Skyrim SE has three hard load-order facts:

1. **255 full plugins** — a heavily modded list runs out long before the player runs
   out of mods.
2. **Light plugins (ESL/ESPFE)** live in the `0xFE` space and do not consume one of
   those 255 slots; the space holds thousands of them `[VERIFY: 4096]`.
3. A light plugin may only **add** new records whose FormID object index is in
   `0x800–0xFFF`; that is **2,048 new records per plugin** `[VERIFY]`. Overrides of
   master records are a separate case and should not consume that budget `[VERIFY]`.

Consequences for real mods:

- A large overhaul ships one plugin with 2,000–20,000+ records. It either eats a
  full load-order slot or cannot be ESL-ified at all.
- Existing ESL-ifying tools handle the *record table* well but ignore the rest of the
  mod folder: scripts, config files, FaceGen assets, archives, external patches.
  That is exactly where silent breakage lives.
- Splitting and renumbering are **not reversible by the user** after release unless
  the tool produces a complete old→new mapping.

The tool's job is to make this surgery boring, deterministic, and auditable.

---

## 3. User-visible outcome

### Input

One mod folder, as downloaded/unpacked, may contain any mix of:

- `*.esp`, `*.esm`, `*.esl` (one or more; usually one main plugin)
- `*.bsa` / `*.bsl` archives (assets, scripts, FaceGen, sounds, configs)
- loose `meshes\`, `textures\`, `sound\`, `scripts\`, `SKSE\Plugins\` files
- Papyrus `*.pex` (and sometimes `*.psc` sources) — inside the archive or loose
- FormID-bearing config: SPID `*_DISTR.ini`, KID `*_KID.ini`, SkyPatcher INI,
  BOS INI, CDF JSON, SKSE-plugin INI/JSON/TXT configs, MCM/JSON settings, xEdit
  `*.pas` scripts, generic `*.txt`/`*.json`/`*.ini`
- mod-manager metadata (`meta.ini`, `fomod\`), readmes, translations

### Output

A **new output folder** (input is never modified), containing:

- 1..N rebuilt plugins. Each output is ideally a **light plugin** with the ESL flag
  set (extension usually kept as `.esp`, i.e. ESPFE; `.esl`/ESM variants TBD).
- master/load-order DAG between the outputs when one output must reference another
  `[VERIFY master-flag rules]`
- changed assets emitted as loose files (the original BSA is never modified or repacked)
- Papyrus: updated VMAD property targets; rewritten + recompiled scripts where
  literal FormIDs exist; otherwise verified untouched
- rewritten FormID occurrences in supported INI/JSON/TXT formats
- renamed/moved FaceGen head meshes and tint textures for renumbered/moved NPCs
- `split-manifest.json` — complete old→new FormID and plugin mapping
- `SPLIT-REPORT.md` — record counts, ESL math, what changed, load order
- `WARNINGS.md` + `coverage.json` — everything the tool could not safely cover,
  with file/line/offset, so the user can manually check
- checksums and a one-command verify report
- **desktop GUI** (required) that runs scan → plan → apply → verify and displays the
  coverage/warnings; the CLI is optional automation

### User story

> "I downloaded a 4,800-record overhaul that needs one of my 255 full slots. Run
> it through the splitter: it becomes one core light master + two ESPFE parts, the
> 34 SPID/JSON configs still point at the right records, the 108 custom NPC faces
> still render, the scripts still compile, and the report tells me the two things
> it could not rewrite before I install it."

---

## 4. Scope

### 4.1 In scope (target v1)

1. **Scan** a whole mod folder: plugins, archives, scripts, configs, assets, metadata.
2. **Extract** SSE BSA archives; map every entry to its virtual path; detect which
   plugin/archive owns which asset.
3. **Analyse plugins** with Mutagen: record counts (new vs override), FormID ranges,
   masters, record-type histogram, reference graph, VMAD inventory, header flags.
4. **Plan a split**: hard units (quest+dialogue, cell+refs, worldspace+cells), record
   budgets, naming, load-order DAG; deterministic plan file the user can inspect.
5. **ESL-ify without splitting (primary goal)**: if the plugin fits the new-record
   budget (default 2,048), set the ESL flag — as-is when IDs are already in range,
   otherwise **renumber** into `0x800–0xFFF`. This is the main product: classic
   ESP/ESM → ESPFE.
6. **Split only as an ESL enabler**: when one ESL cannot hold the content (budget or
   an indivisible hard unit), split into multiple ESL parts so ESLification still
   succeeds. Split is not a general modularisation feature.
7. **Rewrite every reachable FormID reference** (see matrix, section 8): plugin
   record links, VMAD, Papyrus source/compiled scripts, INI/JSON/TXT configs,
   FaceGen asset filenames/paths.
8. **Rebuild Papyrus scripts** where needed: patch VMAD in the plugins; detect and
   rewrite literal FormIDs in `.psc`; when only `.pex` exists, decompile → rewrite →
   recompile; if that cannot be proven safe, keep the original and warn.
9. **Handle dialogue/voice fully**: renumber DIAL/INFO, rename voice `.fuz`/`.lip`
   and handle SEQ files / fragment checks; if programmatic SEQ regeneration is not
   proven, require a user-side Creation Kit step for affected quests.
10. **Archives: read-only extraction, loose output**: the original BSA/BSL is never
    modified or repacked. Extract only affected archive entries, rename/renumber
    them, and emit them as loose files in the output; loose FormID-derived asset
    names are renumbered the same way.
11. **Dependent patches via file copies**: if the optional MO2 profile scan finds
    other plugins/configs referencing the target, emit rewritten copies (same
    filenames) for the user to install above the originals; never edit another
    mod's folder. Without a profile, state that external safety was not checked.
12. **Coverage scan + warnings**: classify every FormID-bearing surface as
    auto-rewritten, verified-external, ambiguous, or unhandled; produce a
    manual-review list; never silently miss one.
13. **Validate**: reparse, reference-integrity check, ESL rule check, master/load
    order check, script compile check, asset-pair check, second-pass stale-reference
    scan, and a human-readable report.
14. **Mapping manifest** so a user (or a future tool) can update external patches.
15. **Output modes**: new output folder by default (`output/<ModName>-ESL/`);
    optional in-place mode writes into a copy of the source mod with a backup.
16. **Desktop GUI (required, not optional)**: WPF Windows app that drives the whole
    flow without an AI agent — folder/profile pickers, plugin dashboard, plan
    preview, apply with progress/cancel, warnings/report viewer, verify, settings.
17. **CLI parity**: the same core is also exposed as a CLI for automation and CI;
    the GUI must never contain logic the CLI cannot run.

### 4.2 Explicitly out of scope for v1 (some are roadmap items)

- Rewriting **native SKSE DLLs** (`*.dll`) — native code cannot be safely recompiled
  from a downloaded mod; scan and warn instead.
- **Editing other mods on disk.** Dependent mods are handled only by generating
  rewritten *copies* (same filenames) in the output that the user installs above the
  originals in MO2; never edit a third-party mod folder (decision D4).
- **Save-game migration**: renumbering/ESL-flagging changes runtime FormIDs; assume
  new game or accept broken references (must be stated loudly).
- Localised plugins with `.STRINGS`/`.DLSTRINGS`/`.ILSTRINGS` — research first; v1 may
  treat localized plugins as "unsupported, report and refuse".
- FO4/Starfield BA2 and plugin formats; Skyrim LE (32-bit) — SSE first.
- Merging plugins, conflict resolution, load-order sorting, mod-manager integration.
- Generating new gameplay/assets; the tool only reorganises existing data.
- BSA repacking — the original archives stay untouched; changed assets are emitted
  loose (decision D7).
- Web, mobile, or remote/headless UI — v1 is a Windows desktop GUI + CLI (decision D16).
- FOMOD regeneration / Nexus packaging (preserve installer files, regenerate later).

---

## 5. Domain background (assumptions to verify in the next phase)

### 5.1 FormID basics

- A runtime FormID is 32 bits: load-order index (8 bits) + object ID (24 bits).
- Inside a plugin file, a record's FormID uses the plugin's **local master index**
  (byte 0 = the plugin itself) + object ID. The game remaps indices at load time.
- **New record** = FormID whose master byte is 0. **Override** = FormID whose master
  byte points at an entry in the plugin's master list.
- Renumbering = assigning new object IDs to new records and rewriting every link that
  targets the old IDs; overrides must keep their master-relative FormIDs.

### 5.2 Light plugin (ESL/ESPFE) constraints `[VERIFY all]`

- ESL flag is a bit in the `TES4` record header flags (`0x200` believed).
- New records must have object IDs in `0x800–0xFFF`; records below `0x800` are
  reserved and must not be new records.
- Maximum **2,048 new records** per light plugin (the `0x800–0xFFF` window).
- Overrides of master records are allowed and do not consume the 2,048 budget.
- Max light plugins is in the thousands (believed 4,096).
- Plugin extension does not have to be `.esl`; an `.esp` with the ESL flag is an
  ESPFE. Whether it can be a **master of another plugin** depends on the ESM flag as
  well — critical for split outputs that reference each other `[VERIFY]`.
- An ESL-flagged `.esm` is a "light master" — the likely shape for the core output of
  a split `[VERIFY]`.

### 5.3 Where FormIDs actually live (the reference surface map)

This is the core of the "never silently break" requirement. Every surface must be
classified as **AUTO** (tool rewrites and verifies), **REPORT** (tool lists it for
manual action), or **N/A** (provably unrelated).

| # | Surface | Examples | v1 handling |
|---|---|---|---|
| 1 | Plugin record FormLinks | conditions, packages, aliases, factions, outfits, keywords, leveled lists, cells/refs, worldspace links | AUTO — typed Mutagen traversal `[VERIFY coverage]` |
| 2 | VMAD in records | quest/fragment scripts, object properties, alias properties, struct properties | AUTO — typed rewrite; REPORT if link type unsupported |
| 3 | Papyrus `.psc` literals | `Game.GetForm(0x...)`, `GetFormFromFile(0x..., "Mod.esp")`, numeric literals in fragments | AUTO when source exists → recompile |
| 4 | Papyrus `.pex` only | same literals after decompile (Champollion) → rewrite → recompile | AUTO best-effort; REPORT if decompile/recompile fails |
| 5 | Config files — known grammars | SPID, KID, SkyPatcher, BOS, CDF, framework INIs | AUTO via per-format adapters |
| 6 | Config files — generic | JSON/INI/TXT with FormID-looking values, plugin-name strings | REPORT by default (or AUTO with format-specific rules); never blind-regex |
| 7 | FaceGen assets | `facegeom\<plugin>\<NPC FormID>.nif`, `facetint\<plugin>\<NPC FormID>.dds` `[VERIFY]` | AUTO — rename/move when NPC renumbered/moved |
| 8 | Other FormID-derived asset names | voice `.fuz`/`.lip` (FormID-named?), `SEQ` files `[VERIFY]` | REPORT until researched; dialogue-heavy mods flagged |
| 9 | External plugins / patches | other ESP/ESL/configs referencing our records | REPORT — external-dependency analysis if load order supplied |
| 10 | Save games | runtime FormIDs | N/A for tool; loud warning that saves are not migrated |
| 11 | Native DLLs | hardcoded FormIDs/plugin strings in `.dll` | REPORT (unsupported, as declared) |
| 12 | Unknown/opaque plugin fields | record types/fields Mutagen does not model | REPORT — round-trip + link-coverage audit; block affected renumber ops if unproven |

### 5.4 FaceGen / chargen assets for NPCs

NPC head geometry and tint are stored as separate files whose **names are derived from
the owning plugin filename and the NPC FormID**, not from a path stored in the NPC
record `[VERIFY exact case/extension]`:

- mesh: `meshes\actors\character\facegendata\facegeom\<PluginName.esp>\<8-hex FormID>.nif`
- tint: `textures\actors\character\facegendata\facetint\<PluginName.esp>\<8-hex FormID>.dds`
  (older/LE variants may use other extensions)

Consequences the tool must handle:

- **Renumber** an NPC → rename both files to the new FormID.
- **Split** NPCs into a different plugin → move both files into the folder of the new
  owning plugin (folder name = new plugin filename).
- Assets may be **inside a BSA** → extract those entries, rename them, emit as loose
  overrides; the BSA itself stays untouched.
- Also handle the reverse: every custom-face NPC should have a matching pair after the
  operation; missing/extra pairs are warnings (not all NPCs ship FaceGen).
- If two plugins override the same NPC, which plugin's FaceGen folder wins is a
  load-order/override question `[VERIFY]`; splitting NPC overrides may change the
  winning face. Report any NPC that is an override.
- **Decision D6:** "replace chargen meshes/textures" means rename/move the FaceGen
  head NIF + face tint DDS to the new FormID/owning plugin. FaceGen regeneration is
  NOT part of this tool.

### 5.5 Archives

- SSE uses BSA v105; the verified local tool is `BSArch.exe` — used **read-only**
  for list/dump/unpack. BA2 is out of scope. No BSA writer is needed.
- **Original BSA/BSL files are never modified or repacked** (decision D7). Virtual
  paths inside the archive are what records/NIFs reference; the tool never rewrites
  the internal layout. Only FormID/plugin-derived **file names** change.
- Assets whose names embed a FormID/plugin name (FaceGen first; voice/SEQ as
  researched) are extracted from the archive, renamed, and emitted as **loose
  files** in the output. Loose files with the same virtual path override the BSA in
  Skyrim/MO2 `[VERIFY precedence with a test profile]`.
- Archive-to-plugin ownership is scanned/reported, not rewritten; a shared BSA stays
  as-is because changed assets are loose overrides.

### 5.6 Config frameworks known to carry FormIDs

Priority adapters (verify each grammar from its own docs, never invent tokens):

1. SPID `*_DISTR.ini` — `FormID|EditorID`, optional `~Plugin.esp` qualifier.
2. KID `*_KID.ini` — keyword/form distribution rules.
3. SkyPatcher INI — filters by form IDs / editor IDs / plugin.
4. BOS — sectioned pipe syntax with `0x...~plugin`-style tokens.
5. CDF — JSON container/vendor rules.
6. MCM Helper / SKSE plugin JSON/INI/TXT — generic but high-value.
7. xEdit `.pas` scripts — contain hardcoded FormIDs; generic report (do not edit).

Also relevant: configs may reference a plugin **by filename** (`~SomeMod.esp`) even
when no FormID is present; if an output plugin is renamed, those strings must be
updated too, and vice versa: `GetFormFromFile(..., "OldName.esp")` in scripts.

---

## 6. Proposed pipeline (8 stages)

    [0] Intake & safety      copy input, hash, inventory, detect profile/data dir
    [1] Archive extraction   BSArch unpack, virtual-path catalog, source map
    [2] Analysis             Mutagen parse, counts, ranges, graph, VMAD, configs, assets
    [3] ESL eligibility      eligible / needs renumber / cannot / already light
    [4] Split & renumber     plan: hard units, budgets, output DAG, naming
    [5] Apply                rebuild plugins, rewrite refs, scripts, configs, FaceGen
    [6] Coverage & warnings  unresolved/ambiguous/manual list; stale-reference second pass
    [7] Validation           reparse, refs, ESL rules, masters, scripts, assets, round-trip
    [8] Package & report     output layout, manifest, load order, checksums, README

Each stage writes a machine-readable artifact and the next stage only consumes that
artifact. `plan` and `apply` are separate commands so the user can review before any
write.

---

## 7. Capability ideas in detail

### 7.1 Scan & inventory

- Walk the mod folder; classify files: plugins, archives, scripts, configs, assets,
  metadata, unknown binaries.
- Detect plugin set and which plugin is the "main" one (largest? named like folder?
  user override).
- Optional `--mo2-profile <MO2 profile>` / `--data-dir <Data>` to resolve masters for
  validation and to run external-dependency analysis. Without it: still transform, but
  validation is limited and reported as such.
- Never trust one source of truth: verify the plugin really parses before planning.

### 7.2 Analysis & ESL eligibility

Per plugin report:

- total records; new records; overrides; per-record-type histogram
- new FormID min/max; count in `0x800–0xFFF`; count below `0x800` (must be overrides
  or invalid); duplicate FormIDs
- masters and whether each is present/resolvable
- header flags (ESM/ESL/localized), form version, header version
- VMAD scripts/properties inventory; scripts on disk vs. missing
- FormID-bearing config inventory; plugin-name string inventory
- FaceGen pairs and NPC correspondence; other FormID-derived assets
- external reference candidates (when load order available)

Eligibility verdicts:

| Verdict | Meaning | Action |
|---|---|---|
| `LIGHT_ALREADY` | already ESL-flagged, still valid | verify/refresh only, report |
| `ESL_READY` | <=2,048 new records, all new IDs already 0x800–0xFFF, no external blockers | set flag |
| `ESL_RENUMBER` | <=2,048 new records but IDs scattered/below 0x800 | renumber then flag |
| `SPLIT_REQUIRED` | >2,048 new records | plan split into N light outputs |
| `SPLIT_WITH_CORE` | >2,048 but a shared master core exists | core master + parts |
| `BLOCKED` | external refs, unsupported record/field coverage, localized, indivisible unit too big, unproven dependency on external tools/records | refuse or `--force` with warnings |

Decision D3: ">2,048" means the **new-record budget** (ESL slots) by default;
overrides do not count. A configurable total-record cap is available but off by
default (`--max-new-records` / `--max-total-records`). Report both numbers.

### 7.3 Split planner

1. Build a record graph: nodes = major records + their owned children; edges =
   FormLinks and implicit containment (quest→aliases/dialogue, cell→refs, worldspace→
   cells, scene→actors, etc.).
2. Condense strongly connected components (SCCs) — a cycle must not be split.
3. Define **hard units** that must never be separated:
   - QUST + its aliases + DIAL/INFO + SCEN + PACK + fragment VMAD (+ voice/SEQ assets)
   - CELL + all REFR/ACHR/ACRE/etc. + navmesh
   - WRLD + all its exterior cells (worldspace splitting is a v2 problem)
   - records referenced by a single script/VMAD cluster
4. Pack components into outputs respecting the new-record budget (2,048 for ESL) and
   optional total-record cap; deterministic ordering (sorted by stable keys).
5. Build the output dependency DAG; choose master flags (ESM/ESL/ESPFE) so every
   cross-output reference is legal and loaded in order `[VERIFY]`.
6. Naming: deterministic, readable, source-derived, e.g. `<OriginalName> - Core`,
   `<OriginalName> - Part 2 - Quest <EditorID>`; never depend on file timestamps.
7. Emit `plan.json`: output list, record→output assignment, count math, DAG, warnings
   for anything indivisible/over budget.

If the user prefers pure mechanical chunking (`--strategy chunk`) it must still
respect hard units; default strategy is hard-unit aware.

### 7.4 Renumbering engine

- Build `old objectID -> new objectID` map for every new record, starting at `0x800`,
  skipping IDs already occupied, deterministic order.
- Straightforward "compact sequentially" is fine for the idea; do NOT preserve gaps.
- Overrides are never renumbered; they keep master-relative IDs.
- Rewrite links through the plugin's typed API, then re-verify by dumping the plugin
  again and resolving every FormKey.
- Handle `LinkCache`-style resolution carefully: references to records that stay in the
  same output are simple; references to records moved to another output must be
  rendered as `MasterKey + new ObjectID` with the new master list.
- Also rewrite FormIDs inside VMAD; then rebuild scripts; then configs; then assets.
- Keep the mapping manifest as the single source of truth for all later steps.

### 7.5 Papyrus "rebuild"

Define precisely (this is what "rebuilds Papyrus scripts automatically" means here):

- **Mandatory:** VMAD script property values in the plugins are FormLinks — they must be
  retargeted after renumber/split (Mutagen typed rewrite), otherwise every scripted
  record breaks even if the `.pex` is untouched.
- **Literal FormIDs in source (`.psc`):** rewrite numbers via the mapping; recompile
  affected scripts with `PapyrusCompiler.exe` using the correct flags/source dirs;
  ship new `.pex`; verify exit code and output file.
- **Only `.pex` available:** decompile with `Champollion.exe` → scan for
  `GetForm`/`GetFormFromFile` numeric-literal arguments → rewrite → recompile. If
  decompile/compile fails or the script did not round-trip, keep the original `.pex`,
  mark it `REPORT`, and fail strict mode. Never claim a script was rebuilt on an
  unverified compile.
- **Plugin-name strings:** `GetFormFromFile(0x..., "OriginalName.esp")` must be
  updated when the record moves to a differently named output plugin. Build a
  per-record "owning output plugin" map and rewrite these strings where the source is
  proven.
- Compiler discovery: no hardcoded paths; accept `--compiler` or find from game/
  manager config (verified local toolchain includes the MO2 Stock Game compiler).
- **DLLs:** not supported; scan `.dll`/PDB-adjacent config files for plugin names and
  FormID-looking strings, report, never patch native binaries.

### 7.6 Config / FormID text rewrite

- Per-format adapters with real grammar, preserving comments, whitespace, ordering,
  encoding (UTF-8/BOM/CRLF), and quoting.
- Token resolution needs plugin context: a value may be local-to-plugin, qualified with
  `~Plugin.esp`, or an EditorID (unchanged by renumber if EditorIDs are preserved —
  say so in the report, but verify).
- Only rewrite occurrences that resolve to records **this tool moved/renumbered**;
  leave references to other mods/vanilla untouched (and report them as verified
  external).
- `JSON`: order/format-preserving editor; classify keys by name heuristics + value
  shape; rewrite only confident matches, report the rest.
- `INI`: preserve section/key order and comments; format adapters first, generic
  scanner second.
- `TXT`: generic scanner.
- **Generic JSON/INI/TXT best-effort rewriting is ON by default** (decision D12):
  every generic edit is logged with file/line/before/after and confidence; backups
  are kept; `--no-generic` disables it. Framework adapters always run regardless.
- Second pass after writing: rescan outputs for stale old IDs / old plugin filenames;
  any hit = verification failure (unless allowlisted as external).

### 7.7 FaceGen / derived-asset rewrite

- Build NPC→(old plugin, old FormID)→(new output plugin, new FormID) map.
- Compute expected old/new virtual paths; find assets in loose files and inside BSAs.
- Rename/copy in the output as loose files; assets found inside a BSA are extracted,
  renamed, emitted loose; never lose a pair silently.
- Detect collisions (two source NPCs mapping to the same target name should be
  impossible if IDs are unique — assert it).
- Report NPCs whose FaceGen is missing before/after; report overridden NPCs (winner
  may change); report non-standard asset locations it could not classify.
- Voice files: decision D8 puts them in v1 scope — research the `.fuz`/`.lip` naming
  convention, rename affected files, and verify with a real dialogue mod.
- `SEQ` files: research whether they embed FormIDs; treat as REPORT until proven.

### 7.8 Archive extraction (read-only) and loose output

- List (`-list`/`-dump`) then unpack to a staging directory; keep a map of
  archive-entry → staging path → intended output (unchanged / renamed loose).
- Original BSA/BSL files are never modified or repacked (decision D7).
- Never change virtual paths except FormID/plugin-derived file names where the
  convention is proven (FaceGen first, voice/SEQ as researched). Extract only the
  affected entries, rename them, write them loose in the output; original BSA
  entries stay unused and harmless.
- Report every archive entry that was extracted/renamed and why.
- `.bsl` and other archive metadata are scanned/reported only `[VERIFY]`.

### 7.9 Coverage scanner and warnings (the safety contract)

> **Rule: an apply is only "clean" if every reference surface is AUTO-verified or
> provably external. Anything else makes the result `NEEDS-MANUAL-REVIEW`, never
> "done".**

- Pre-apply coverage report by surface, with percentages and counts.
- Every FormID-looking token in text files is classified:
  `RESOLVED` (will rewrite), `EXTERNAL` (deliberately untouched),
  `UNRESOLVED` (matches our plugin but not in map → error), `AMBIGUOUS` (no plugin
  context), `UNSUPPORTED_FORMAT`, `BINARY_SUSPECT`.
- Every plugin record type is checked against a "known FormLink coverage" allowlist
  derived from Mutagen + xEdit definitions; unknown link-carrying fields are reported.
- Every asset class with FormID-derived naming is either AUTO-handled or REPORT.
- Every generic (non-adapter) text edit is listed with before/after and confidence
  (decision D12); a false-positive edit is a blocking warning, not silent.
- Post-apply second scan proves no stale references remain.
- Output: `coverage.json` (machine) + `WARNINGS.md` (human, grouped by severity,
  each item with file, line/offset, raw token, guessed target, why it was not
  rewritten, suggested manual action).
- Exit codes: `0` clean; `2` applied but manual review required; `3` refused/blocked;
  `1` tool error. `--strict` upgrades any manual item to `3`.

### 7.10 Reports & manifests

- `split-manifest.json`: per output plugin — name, masters, record counts, ESL math;
  record mapping `(oldPlugin, oldID) -> (newPlugin, newID)`; plugin rename map;
  config token map; asset map; tool version; input hash; determinism seed.
- `SPLIT-REPORT.md`: human-readable plan/apply/validation summary, load order,
  warnings, "how to test" instructions.
- `checksums.sha256`.
- Optional `explain <formid>` helper to trace a mapping for debugging.

---

## 8. Rewrite matrix (what must be changed, and what can go wrong)

| Location | Rewrite trigger | Method | Primary risk |
|---|---|---|---|
| Record FormLinks | renumber / move | Mutagen typed | unmodelled fields missed |
| VMAD object/alias/struct properties | renumber / move | Mutagen typed | property type not traversed |
| Quest aliases / forced refs | renumber / move | Mutagen typed | quest+dialogue split |
| CELL/REFR links | renumber / move | Mutagen typed | navmesh/Persistent refs |
| Papyrus `.psc` literals | renumber / move | text + recompile | decompile round-trip faults |
| Papyrus `.pex` literals | renumber / move | decompile/recompile | compiler flags/source dirs |
| `GetFormFromFile` plugin string | rename / move | text + recompile | record owns multiple scripts |
| SPID/KID/SkyPatcher/BOS/CDF | renumber / move | format adapters | grammar variants |
| Generic JSON/INI/TXT | renumber / move | report (default) | false positives |
| FaceGen `.nif`/`.dds` names & folders | renumber / move | rename + extract-if-archived, loose output | archive-internal paths |
| Voice `.fuz`/`.lip`, `SEQ` | renumber (dialogue) | TBD [VERIFY] | naming convention unknown |
| Plugin-name strings in any config | rename / split | adapters/scanner | mod references by name |
| External plugins/saves/DLLs | any | REPORT | user must patch/migrate |

---

## 9. Safety & compatibility model

1. **Never write the input by default.** Output goes to a new folder; optional
   in-place mode writes into a copy of the source mod with a backup and is never the
   default (decision D10). Keep source hashes in the report.
2. **Plan before apply.** `scan` and `plan` are read-only; `apply` requires a plan and
   prints exactly what will change.
3. **External dependencies: detect, then patch via copies.** If the user supplies an
   MO2 profile, scan other plugins/configs for references to the target plugin's
   FormIDs or filename. Instead of editing those mods, emit rewritten copies (same
   filenames) in the output; the user enables the output mod above the dependent
   mods (MO2 file override). Without a profile, the report must state that external
   safety was not checked. Never edit another mod's folder (decision D4).
4. **Save games:** state plainly that runtime FormIDs change; saves are not migrated;
   recommend a new game / test save.
5. **Unknown data:** if a record contains unparsed fields that might hide FormLinks,
   do not silently renumber it — report/refuse. Round-trip dump comparison is the
   gate, not optimism.
6. **Determinism:** sorted traversal, fixed naming, no timestamps in logical output,
   stable archive packing order, hashes in the report so two runs can be compared.
7. **Rollback:** output is a new folder; keep the plan/manifest; never delete the
   source; document how to revert (delete output, reinstall original).
8. **Honest status:** tool-validated ≠ runtime-confirmed. In-game load test is a
   user-side step (copied MO2 profile), not something the tool claims.

---

## 10. Validation strategy (gates before "done")

| Gate | Check | Failure behavior |
|---|---|---|
| V1 Parse | every output reopens with Mutagen; record counts match plan | abort |
| V2 References | every FormKey resolves in self+masters; no dangling masters; no duplicate FormIDs | abort |
| V3 ESL rules | new-record count <=2,048; all new IDs 0x800–0xFFF; flag/extension/header consistent; overrides untouched | abort |
| V4 Load order | output DAG legal; masters listed before dependents; plugin names unique | abort |
| V5 Scripts | all rewritten `.psc` compile; changed set matches plan; untouched scripts hashed | abort or REPORT |
| V6 Data preservation | semantic dump diff original→output for non-FormID fields; unknown subrecords preserved | abort for affected records |
| V7 Coverage second pass | no stale old IDs / old plugin names / orphan FaceGen in outputs | abort or REVIEW |
| V8 Assets | FaceGen pair counts match expected NPC sets; every extracted/renamed loose asset listed and present | REVIEW |
| V9 User runtime | copied MO2 profile loads, main menu, test cell, spot-check NPC faces | user-confirmed, reported separately |

The tool must **never** claim runtime success. Reports use the workspace evidence
ladder (`tool-validated`, user-confirmed, etc.).

---

## 11. GUI and CLI (UX sketch)

### 11.1 GUI — required v1 (WPF desktop app)

Normal flow, usable without a CLI or agent:

1. **Home**: pick the mod folder, optional MO2 profile, output folder; choose the
   target plugin when the folder contains more than one.
2. **Scan dashboard**: plugin card(s) with total/new/override counts, ESL verdict,
   master list, scripts/configs/assets found, warning count; expandable details.
3. **Plan preview**: proposed output part(s) and the reason (fits one ESL vs must
   split), per-part record budget, hard units kept together, rewrite counts per
   surface (records/VMAD/scripts/configs/FaceGen/voice), dependent copies, coverage
   %, and the WARNINGS list; buttons: Export plan, Back, Apply.
4. **Apply**: stage-by-stage progress (extract → analyse → build → rewrite →
   validate → package), live log pane, cancel that leaves no half-published output.
5. **Result**: summary, open output folder, open `SPLIT-REPORT.md` / `WARNINGS.md`
   viewer, one-click Verify.
6. **Settings**: new-record threshold, total-record cap, generic rewriting on/off,
   patch-dependents, data dir, compiler/BSArch paths, logging level.

GUI rules: thin shell over the core; background execution with progress +
cancellation; never blocks on long I/O; no logic that the CLI cannot run; every
destructive action (in-place mode) requires explicit confirmation.

### 11.2 CLI (same core, for automation and tests)

    sms scan  <modFolder> [--mo2-profile <path>] [--data-dir <Data>] [--json] -o reports/
    sms plan  <modFolder|scan.json> [--max-new-records 2048] [--max-total-records N]
              [--esl auto|force|off] [--patch-dependents] -o plan/
    sms apply <plan.json> [--out <folder>|--in-place] [--backup] [--strict] [--dry-run]
    sms verify <outFolder>
    sms explain <manifest.json> <plugin>:<formid>

- Default is dry-run/preview; writing needs an explicit `apply`.
- Default output is a new folder; `--in-place` writes into a copy with backup (D10).
- `--mo2-profile` enables load-order/dependent checks; `--patch-dependents` emits
  rewritten dependent file copies (requires the profile).
- `--no-generic` disables generic JSON/INI/TXT rewriting (on by default, D12).
- Rich console summary + machine JSON; every warning has an ID for the report.
- Configuration file for data dirs/compiler path so users don't repeat long args.

---

## 12. Architecture sketch (idea level)

Build on the **Synthesis/Mutagen idea**, not the Synthesis application:

- **.NET (C#)** solution in this repo (the `.gitignore` already targets .NET).
- **Mutagen.Bethesda.Skyrim** for typed plugin read/write, FormKeys, VMAD,
  link resolution. Pin a version verified against the actual SSE record set; the
  workspace already has a Mutagen 0.50.2-based dumper to compare behavior with.
- Synthesis lessons to reuse: deterministic patcher-style pipeline stages, typed
  settings, no hidden load-order assumptions, one record-mapping model, LinkCache,
  strict output validation. Synthesis itself is GUI-only here (no `Synthesis.CLI.exe`
  on disk), so we build our own GUI and CLI over one shared core.
- External tools (verified locally, invoke by absolute path, configurable):
  - `BSArch.exe` — BSA list/dump/unpack only (read-only; no pack step)
  - `Champollion.exe` — `.pex` → `.psc`
  - `PapyrusCompiler.exe` — compile `.psc` (from MO2 Stock Game)
  - xEdit/SSEDump — optional user-side cross-check, not a runtime dependency
- Components: `Core` (scan/analyse/plan/renumber), `Formats` (plugin-config-script
  adapters), `Assets` (BSA + FaceGen), `Validation`, `Reports`, `Cli`, `Gui` (WPF).
- GUI rule: the WPF `Gui` project references `Core` only; it renders progress/reports
  and calls the same operations as `Cli` (no duplicated business logic).
- Extensibility: adapter registries (config formats, asset-name handlers, split
  strategies) so support grows without touching the core.
- Windows-only desktop app is acceptable (WPF); keep core paths abstracted for future
  cross-platform/Avalonia migration.

---

## 13. Milestones (idea level, acceptance criteria later)

| M | Scope | Why this order |
|---|---|---|
| **M0** | `scan` only: parse plugins, count records/new/overrides, ESL math, inventory archives/scripts/configs/assets, coverage dry report | read-only, proves parser + counters + scanner safely |
| **M1** | Minimal GUI + engine: folder/profile pick + scan dashboard + warnings; ESL-flag already-eligible plugins (no renumber); loose output | first usable app; no ID churn |
| **M2** | Renumber + ESL (primary product), with GUI plan preview and apply progress: plugin links + VMAD + `.psc`/`.pex` + config adapters + FaceGen rename; loose output; mapping manifest | the core "ESL-ify safely" capability |
| **M3** | Split >2,048 as an ESL enabler: hard units, planner, multi-output DAG, per-part ESL, Part 1 keeps the name; GUI shows the split plan | hardest; needs the M2 reference engine |
| **M4** | Dialogue/voice/SEQ full handling, dependent file-copy patches, generic best-effort polish, GUI report/warnings viewer + verify + settings polish | remaining v1 scope |
| **M5** | Optional: Synthesis patcher wrapper, web/mobile UI, FO4/BA2, LE | only if demanded |

Each milestone must ship with a sample corpus run + validation report and a working
GUI path for the features it adds, not just code.

---

## 14. Risk register (hard problems)

| # | Risk | Impact | Mitigation idea |
|---|---|---|---|
| R1 | External plugins/patches reference the original FormIDs | broken patches, CTDs | external-dependency scan + refuse; mapping manifest; future dependent-rewrite mode |
| R2 | Save games reference old FormIDs | broken saves | loud warning; new-game expectation; no save migration v1 |
| R3 | Unmodelled record fields hide FormLinks | silent broken refs | link-coverage allowlist + round-trip + report/refuse |
| R4 | `.pex` decompile/recompile is lossy | broken scripts | source-first; round-trip verify; keep original + REPORT on failure |
| R5 | FaceGen/voice/SEQ naming conventions | dark faces, silent voice loss | FaceGen auto + verification; voice/SEQ report until proven |
| R6 | Dialogue/quest splitting breaks fragments/SEQ/voice | quests stuck | hard units; avoid splitting dialogue in v1 if unsure |
| R7 | Cell/worldspace splitting (navmesh, refs, LOD) | broken world | hard units; keep WRLD whole; split only non-world content v1 |
| R8 | Cross-output masters / ESM-flag rules | load order refuses to load | verify master rules early; core-master design |
| R9 | Loose-vs-BSA precedence or archive extraction misses | missing/wrong assets | verify loose override precedence in a test profile; asset diff report; original BSA untouched |
| R10 | Localized string files | missing names/dialogue | refuse localized plugins in v1 |
| R11 | Large plugin performance/memory | tool too slow | Mutagen streaming/full load benchmark; cap and report |
| R12 | Tool over-promises "automatic" | user trusts broken output | coverage gate + explicit manual list + honest reports |
| R13 | Licensing: splitting someone else's mod | distribution issues | tool is local-use; docs remind user about permissions |
| R14 | Renumber changes conflict/winners among overrides | different gameplay | preserve override records/order; report risky clusters |
| R15 | Dependent file copies incomplete/stale, or user installs them below the dependent mod | broken patches | full load-order scan, explicit install order in report, verify step, mapping manifest |
| R16 | Loose override does not win over BSA, or extract/rename misses an entry | wrong/missing assets | verify loose-vs-BSA precedence in a test profile; re-list affected archive entries; asset diff report |
| R17 | Generic best-effort text edits hit false positives (D12 default ON) | broken configs | per-edit before/after log, backups, confidence scores, `--no-generic`, strict mode |
| R18 | GUI scope creep or UI bugs blocking normal use | tool unusable for the user | GUI is a thin shell over the tested core; ship GUI from M1; smoke-test the main flow; CLI drives automated tests |
| R19 | Long operations freeze/crash the GUI | data loss or confusion | background tasks, progress + cancellation, write to temp then publish atomically, explicit confirmation for in-place |

---

## 15. Decision log (frozen 2026-11-08) and remaining questions

### 15.1 Resolved decisions

| # | Topic | Decision |
|---|---|---|
| D1 | Tool form | Standalone .NET **WPF desktop app + CLI** over one reusable core (Mutagen); Synthesis is only an architectural reference. Optional Synthesis wrapper later, not v1. |
| D2 | Primary goal | Convert a classic ESP/ESM into an ESL-safe plugin (ESPFE): renumber first, then set the ESL flag. Splitting exists only to make ESLification possible when one plugin cannot hold the content — not for general modularisation. |
| D3 | Split threshold | Configurable: the new-record budget (default 2,048) drives ESL; an optional total-record cap exists but is off by default. Overrides do not consume the new-record budget. |
| D4 | Dependent mods | Detect via an optional MO2 profile scan; never edit other mods. Emit rewritten *copies* of dependent plugins/configs (same filenames) for the user to install above the originals (MO2 file override). Without a profile, report that external safety was unchecked. Primary use case remains mods with no dependents. |
| D5 | Save compatibility | Warn loudly; no save migration in v1; new game / test save expected. |
| D6 | FaceGen / chargen | Rename/move the head NIF + face tint DDS to the new FormID/owning plugin. No FaceGen regeneration. Files inside BSAs are extracted, renamed and emitted loose. |
| D7 | BSA policy | The original BSA/BSL is never modified or repacked; extraction is read-only and only for scanning/asset rename. Changed assets are emitted as loose overrides; loose FormID-derived names are renumbered too. |
| D8 | Dialogue / voice | In v1 scope: renumber DIAL/INFO and handle voice `.fuz`/`.lip` + SEQ + fragment checks. Research programmatic SEQ/voice regeneration; if unproven, require a user-side Creation Kit step for affected quests. |
| D9 | Split naming | Keep the original filename for Part 1; extra parts `OldMod - Part 2.esp`, `OldMod - Part 3.esp`, ... |
| D10 | Output location | New output folder by default (`output/<ModName>-ESL/`); optional in-place mode writes into a copy of the source mod with backup. |
| D11 | Config formats | v1 rewrites SPID / KID / SkyPatcher / BOS framework INIs, CDF JSON, MCM Helper / SKSE plugin JSON+INI, and generic JSON/INI/TXT best-effort; xEdit `.pas` is report-only. |
| D12 | Generic text rewriting | Best-effort ON by default for FormID-looking tokens (user decision); every generic edit is reported with before/after; `--no-generic` opt-out; backups kept. |
| D13 | MO2 support | Optional profile pick for load-order/dependent checks; single-mod-folder mode works without it (limited validation, stated in the report). |
| D14 | Test corpus | Copies of rpg-mechanism mods plus one big plugin copy from the Mantras MO2 list; originals untouched. |
| D15 | Scope boundaries | SSE 1.6.1170 primary (parse 1.5.x where possible); no LE/FO4/BA2; localized plugins refused in v1; WRLD+cells and quest+dialogue are hard units; no DLL rewriting; no save migration; FOMOD generation out of scope; deterministic, CI-testable outputs. |
| D16 | GUI | **Desktop WPF GUI is required in v1**, not optional; normal use must not need a CLI or an AI agent. It is a thin shell over the same core as the CLI: scan dashboard → plan preview → apply with progress/cancel → warnings/report viewer → verify → settings. WPF is the recommended framework (Windows-native, no extra runtime); Avalonia is the fallback only if cross-platform becomes a goal. UI text English in v1; localization later. |

### 15.2 Remaining questions (research/verification, not product decisions)

These no longer need a user decision; they need evidence in the context phase:

- Exact ESL flag bit, valid new-record range/count, and master/extension rules (verify from `R88_ESLify.pas`, xEdit behavior, and docs).
- Exact FaceGen path convention (folder name, case, FormID padding, extension) from a real BSA.
- Voice `.fuz`/`.lip` naming and SEQ content/regeneration feasibility for dialogue.
- MO2 file-override precedence (loose file and same-name plugin copies) in a test profile.
- Mutagen FormLink coverage per record type, VMAD struct/property rewriting, unknown-field round-trip.
- Champollion → PapyrusCompiler round-trip success rate on real mods.
- Config grammars/versions actually used by the target mods; adapter validation.
- Whether an ESM-flagged plugin can receive the ESL flag safely as a light master when it has no dependents.
- Load-order semantics for `0xFE` light plugins when their forms are referenced from other plugins.
- WPF/.NET desktop workload availability on the build machine; minimal GUI smoke-test approach; packaging (portable folder vs installer) and UI language still open.

---

## 16. Context-collection plan (draft — to be guided by the user next)

The next phase should collect evidence before any technical document. Proposed
checklist (the user will steer priority):

1. **Pick a test corpus** (copies only):
   - small ESL-eligible mod: Fortunate Foes (3 ESL ESPs, 108 FaceGen NPC triples in
     BSA, SPID/SkyPatcher INIs) — excellent for FaceGen + config coverage;
   - already-light plugin: Worlds Dawn (742 records, 46 `.pex`, JSON config);
   - config-heavy mod: DiabloRim (INI/CSV), Summermyst CSV;
   - a >2,048-record plugin: copy one from the Mantras MO2 list (read-only, copy to
     scratch) — needed to design the split planner;
   - synthetic plugins built with Mutagen for edge cases (IDs below 0x800, overrides,
     VMAD, FaceGen, weird records).
2. **Ground-truth dumps**: record counts/FormID histograms from the existing
   `esp-explorer` dumper + raw TES4 walker; BSA listings (BSArch `-list`).
3. **Verify the ESL rules** against the local `R88_ESLify.pas` + xEdit behavior +
   docs; record exact constraints (flag bit, ranges, master rules, extension rules).
4. **Verify FaceGen naming** by listing a real mod BSA that contains FaceGen
   (Fortunate Foes) and matching names to NPC FormIDs.
5. **Verify loose-file vs. BSA precedence** in Skyrim/MO2 from docs and a copied MO2
   test profile (changed assets are loose overrides; the original BSA is untouched).
6. **Verify config grammars** from each framework's own docs (SPID/KID/SkyPatcher/
   BOS/CDF), including `~plugin` semantics and bare FormID interpretation.
7. **Verify Mutagen capabilities**: FormLink coverage across SSE record types,
   VMAD property rewriting, unknown-field round-trip, master handling, binary write
   determinism; compare a parse→write round-trip against the original.
8. **Verify Papyrus toolchain**: Champollion decompile of a real mod `.pex` set,
   PapyrusCompiler flags/import dirs, compile success rate on decompiled scripts.
9. **Verify the local toolchain versions** (BSArch, Champollion, .NET, compiler) and
   record commands + evidence; confirm the WPF/.NET desktop workload builds on this
   machine and prototype the thin GUI shell against a dummy scan result.
10. **Inspect prior art code** (Synthesis source, ESLify script, any ESLifier source)
    for proven algorithms; cite what it proves and what it does not.

Deliverable of that phase: an evidence-backed context file (e.g. `docs/CONTEXT.md`)
that feeds the technical document.

---

## 17. Prior art / references to study (no claims yet)

- **Synthesis 0.36.6** source in the local `_tools` tree — patcher/pipeline architecture,
  Mutagen usage patterns, settings model; its WPF GUI is also a UX reference for
  mod-folder/plugin dashboards (we still ship our own standalone app).
- **Mutagen.Bethesda** source/docs — typed records, `IFormLinkContainer`, VMAD, master
  handling, binary writers, archive reader.
- **xEdit `R88_ESLify.pas`** (local SSEEdit `Edit Scripts`) — the de-facto ESL
  eligibility rules; also the ESLify/ESLifier tools' algorithms.
- **UESP / xEdit / Mutagen docs** on TES4/SSE plugin format, ESL flag, light masters.
- **FaceGen path conventions** (modding wikis + real BSA listings).
- **SPID, KID, SkyPatcher, BOS, CDF docs** — exact grammar for FormID tokens.
- **Papyrus compiler + Champollion** docs for flags/exit codes.
- **MO2 documentation** — BSA loading via VFS, plugin/ESL ordering.
- **Existing splitters/mergers** (merge tools, SWIFT?, zMerge?) for record-grouping
  lessons and known failure modes.

---

## 18. Glossary (working terms)

- **Plugin** — `.esp`/`.esm`/`.esl` file; "mod plugin".
- **New record** — record created by this plugin (master index 0 in local FormID).
- **Override** — record replacing a master's record.
- **Light plugin / ESL / ESPFE** — plugin with the ESL flag, loaded in `0xFE` space.
- **Renumber** — reassign object IDs of new records to make a plugin light.
- **Split** — distribute one plugin's records into multiple output plugins.
- **Hard unit** — record cluster that must stay together (quest+dialogue, cell+refs).
- **Coverage** — fraction of FormID-bearing surfaces the tool can rewrite/verify.
- **Manifest** — machine-readable old→new mapping and plan record.

---

## 19. Revision history

| Rev | Date | Change |
|---|---|---|
| r0.1 | 2026-11-07 | Initial idea draft: full-folder pipeline, ESL/renumber/split, Papyrus, BSA, plus the newly captured requirements — FaceGen/chargen mesh+tint asset rewrite for NPCs and FormID rewriting in INI/JSON/TXT configs with a coverage/warning system for uncovered references. |
| r0.2 | 2026-11-08 | Product decisions frozen (section 15). Primary goal clarified: ESLify a classic ESP/ESM; split only when needed to make ESL possible. Original BSA read-only, changed assets emitted loose. Dependents fixed via rewritten file copies (MO2 override). Dialogue/voice/SEQ in v1 scope. New output folder default, in-place optional. |
| r0.3 | 2026-11-08 | GUI moved into required v1 scope: WPF desktop app (scan dashboard, plan preview, apply progress/cancel, warnings/report viewer, verify, settings) sharing the core with the CLI; scope, architecture, milestones and risks updated. |

---

## 20. What this document is not

- Not a technical design: no schemas, algorithms frozen, no API.
- Not an implementation plan with estimates.
- Not evidence: every technical claim marked `[VERIFY]` must be checked before it is
  used in code or a technical document.
- The next artifact after context collection is a separate technical design document
  (architecture, algorithms, data model, test plan); this file stays as the scope
  baseline and decision log for the idea stage.
