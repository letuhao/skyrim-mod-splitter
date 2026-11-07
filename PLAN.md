# Skyrim Mod Splitter — Implementation Plan v1

Status: active. Goal: ship a working WPF GUI + CLI tool that ESLifies real classic
ESPs, splitting only when required, with full FormID reference rewriting and
coverage warnings. No human interaction is expected during implementation.
Stop and report only if a required tool/context is genuinely missing.

Source of product decisions: IDEA.md section 15 (D1-D16). Implementation decisions:
DECISIONS.md. This plan slices the work into 4 large, independently auditable
slices; an audit agent is assigned per slice after it lands.

## Verified environment (2026-11-08)

- .NET SDKs 6/7/8/9/10 installed; WindowsDesktop runtime 8/9/10 present; WPF template builds.
- NuGet reachable (HTTP 200); Mutagen.Bethesda.Skyrim 0.50.2 restored and built in a test project.
- BSArch: D:\Works\source\skyrim-moddings\_tools\BSArch-64745-0-9-1660562025\sse\BSArch.exe
- Champollion: D:\Works\source\skyrim-moddings\_tools\Champollion\Champollion.exe
- PapyrusCompiler: H:\Skyrim\TwistedSkyrim\Stock Game\Papyrus Compiler\PapyrusCompiler.exe
  Flags: H:\Skyrim\TwistedSkyrim\Stock Game\Data\Source\Scripts\TESV_Papyrus_Flags.flg
- Game masters: H:\Skyrim\MantrasOfMara\Stock Game\Data (Skyrim.esm, Update.esm,
  Dawnguard.esm, HearthFires.esm, Dragonborn.esm present).
- MO2 profile/load order: H:\Skyrim\MantrasOfMara\profiles\Mantras of Mara\plugins.txt
  (3815 lines). Live MO2 folders are read-only; all writes go to this repo.
- Existing ground-truth dumper: _context\rpg-mechanism\tools\esp-explorer
  (Mutagen 0.50.2, built binary available).

## Real test corpus (copies only; originals untouched)

Copied to this repo as scratch (gitignored):

| Case | Source (read-only) | Facts from esp-explorer |
|---|---|---|
| A. SprawlingBimbos | H:\Skyrim\MantrasOfMara\mods\[NoDelete]_SLGP_BoS - Sprawling Bimbos | 1 ESP (302 KB), 937 records, 779 own / 158 overrides, 0 own IDs below 0x800, non-ESL/ESM. Files: 51 .pex, 47 .psc, 1 .seq, 2 .ini, 56 .nif, 55 .dds, Interface/Meshes/scripts/Seq/textures, no BSA. Masters include Skyrim/Update/Dawnguard/HearthFires/Dragonborn + SexLab.esm, SexLabAroused.esm, CustomComments.esp. Renumber target. |
| B. CardWeapon | H:\Skyrim\MantrasOfMara\mods\[NoDelete]_WP_Cardweapon SE (Official version by the original creator) | 1 ESP (1.2 MB), 3,373 records, 3,367 own / 6 overrides, 0 own IDs below 0x800, non-ESL/ESM. Plus CardWeapon_SE.bsa (62 MB), CardWeapon_SE - Textures.bsa (322 MB), 1 INI, 1 TXT. Masters: Skyrim.esm, Update.esm. Split target (needs >= 2 ESL parts). |

Ground-truth summaries and the existing esp-explorer output are kept under
test-corpus/ground-truth/ for slice audits. The full plugin/BSA copies stay
gitignored. Master search dirs for analysis: Stock Game Data plus (if found) the MO2
mod folders containing SexLab.esm / SexLabAroused.esm / CustomComments.esp.

## Target project layout

    SkyrimModSplitter.sln
    src/SkyrimModSplitter.Core/      domain, scan, renumber, split, formats, assets, validation, reports
    src/SkyrimModSplitter.Cli/       sms CLI (automation/tests)
    src/SkyrimModSplitter.Gui/       WPF desktop app (required v1)
    tests/SkyrimModSplitter.Tests/   xUnit unit + synthetic fixtures + real-mod acceptance
    test-corpus/                     gitignored copies + ground-truth summaries
    artifacts/                       gitignored outputs, audits, logs, release zips
    PLAN.md, DECISIONS.md, IDEA.md, README.md

## Slice 1 — Foundation & Scan (audit-sized)

Scope: solution scaffold; Core domain model; Mutagen plugin analysis (header flags,
masters, record counts raw+Mutagen, new/override FormID ranges, per-signature counts,
VMAD inventory, link stats, unresolved links); mod-folder inventory (plugins, BSA list
via BSArch, loose files, scripts, configs, assets); coverage dry-run scan; report
JSON+Markdown; CLI scan; WPF shell (Home folder/profile pickers, scan dashboard,
warnings panel); tests with synthetic fixtures and both real mods.

Acceptance:
- Builds clean (warnings-as-errors for new code); all unit tests pass.
- Scan of SprawlingBimbos reports 937 records / 779 own / 158 overrides / 0 own IDs
  below 0x800 / non-ESL; masters list matches ground truth; script count 47 psc/51 pex;
  SEQ/INI detected.
- Scan of CardWeapon reports 3373 / 3367 / 6 / 0 / non-ESL; both BSAs listed with entry
  counts; INI/TXT detected.
- GUI launches, scans a folder and shows the dashboard without freezing; CLI and GUI
  produce the same report data.
- Audit-1 by an independent agent: code review + run tests + compare scans against
  esp-explorer summaries; verdict PASS/FAIL written to artifacts/audits/slice-1-audit.md.

## Slice 2 — ESL Conversion Engine (audit-sized)

Scope: deterministic renumber planner/executor (new IDs 0x800-0xFFF, overrides fixed);
typed FormLink rewrite; VMAD object/alias/struct property rewrite; TES4 ESL flag (and
ESM handling); output writer (new folder; copy non-plugin files; loose changed assets);
FaceGen NIF + tint DDS rename/extract-as-loose; mapping manifest + SPLIT-REPORT; gates
V1-V4/V6; CLI plan/apply; GUI plan preview, apply progress/cancel, result screen;
tests incl. real SprawlingBimbos conversion.

Acceptance:
- SprawlingBimbos converts to ESPFE: all 779 new records in 0x800-0xFFF, overrides
  unchanged, every self-link and VMAD property resolves to the new IDs, no duplicate
  FormIDs, output reparses, original mod folder hash unchanged.
- Mapping manifest is complete (every changed FormID, every renamed asset).
- Validation gates pass and report zero unresolved self-references; coverage warnings
  list anything not auto-rewritten.
- GUI converts the real mod from plan preview to result; CLI produces identical output.
- Audit-2 independent agent: adversarial FormID/reference inspection, count math,
  manifest completeness, idempotence/determinism re-run check; verdict file.

## Slice 3 — Content Rewriting & Coverage (audit-sized)

Scope: config adapters (SPID/KID/SkyPatcher/BOS INIs, CDF JSON, MCM/SKSE JSON+INI,
generic JSON/INI/TXT best-effort with per-edit report, xEdit .pas report-only);
plugin-name string rewrite; Papyrus pipeline (Champollion decompile, literal FormID
detection/rewrite, PapyrusCompiler recompile, failures reported); dialogue/voice/SEQ
handling (rename, SEQ strategy, CK-step fallback); FaceGen inventory checks; coverage
scanner classifications; WARNINGS viewer. GUI verify screen is delivered in Slice 4
(with the split flow). Dependent scan / I11
warning default / --patch-dependents file copies are deferred to Slice 4 (recorded
after Audit-3) and are part of Slice 4 acceptance, not Slice 3.

Acceptance:
- Every FormID-bearing surface in both real mods is classified, rewritten or reported;
  no silent surfaces. Generic edits are logged before/after.
- SprawlingBimbos scripts: decompile/recompile success or explicit REPORT; compile exit
  codes recorded.
- CardWeapon INI/TXT scanned and rewritten/reported; BSA entries extracted only when
  needed for renamed assets; original BSAs unchanged.
- Dependent scan/I11 warning and --patch-dependents copies are delivered in Slice 4
  (explicit deferral after Audit-3); not part of the Slice-3 gate.
- Audit-3 independent agent: coverage completeness review, script/config round-trip
  evidence, adversarial unknown-format cases; verdict file.

## Slice 4 — Split Planner & Ship (audit-sized)

Scope: record graph + hard units (quest+dialogue, cell+refs, worldspace+cells), SCC
condensation, budget packing (new-record 2048), output DAG/master flags, multi-plugin
writer, split naming (Part 1 keeps original name), per-part validation, GUI split plan;
MO2 profile dependent scan with I11 default (detect+warn; no profile => unchecked) plus
--patch-dependents rewritten file copies (deferred from Slice 3); GUI WARNINGS viewer fed
by conversion warnings; end-to-end on CardWeapon; packaging/portable release zip;
README/USAGE; final acceptance on both real mods.

Acceptance:
- CardWeapon splits into >=2 ESL parts; each part reparses, all new IDs in range,
  cross-part links resolve via masters, Part 1 keeps CardWeapon_SE.esp, load-order
  DAG is valid, output report explains why each part exists.
- Both real mods pass all validation gates; outputs are in artifacts/acceptance/
  with manifests and coverage reports.
- GUI drives the split preview/apply/verify flow; CLI matches.
- MO2 profile dependent scan warns by default when other plugins/configs reference the
  split/converted mod; --patch-dependents emits rewritten file copies; without a profile
  the report says external safety is unchecked.
- Release zip contains GUI+CLI+docs, built Release, with SHA256.
- Audit-4 independent agent: end-to-end adversarial audit of split semantics,
  master/load order, reference integrity, UI flow; final verdict file.

## Slice 4 as-built notes (2026-11-08)

- Partition model: new dependency-closed master part loaded first; the original
  filename stays as the dependent part. Cells and placed refs are forced into the
  original part; the new master contains only flat records so it can be built as a
  new plugin (group.Add + Duplicate + MasterReference API probed and used).
- Real CardWeapon split: Part 2 = 2,048 own (ESM+ESL master), original = 1,319 own +
  6 overrides; validation PASS; deterministic; source untouched; GUI output equals
  CLI output byte-for-byte.
- Dependents: scanner/patcher implemented for conversion and split; rewritten copies
  are emitted under output/dependents; no automatic MO2 install.
- Release packaging: scripts/release.ps1 produced
  artifacts/release/SkyrimModSplitter-1.0.0-win64.zip with SHA256 recorded.
- Runtime/load testing remains user-side (V9); structural validation is the tool
  gate.

## Audit protocol (each slice)

Each audit is a separate large task assigned to an independent agent after the slice
lands. The auditor must: read the slice scope + acceptance; read the code; build Release;
run the full test suite; run the slice's real-mod acceptance command; compare against
ground truth; look specifically for silent data loss, missed FormID surfaces, non-
determinism, and false PASS claims; write artifacts/audits/slice-N-audit.md with
PASS/FAIL, findings ranked by severity, commands and evidence. Lead fixes FAIL findings
before the next slice is declared complete.

## Blocked-if

Stop and report if: NuGet/Mutagen restore stops working; a required binary (BSArch,
Champollion, PapyrusCompiler) disappears; the live MO2 mods become unavailable; the
workspace drive cannot hold the test corpus/outputs; or a decision needs human product
input that cannot be inferred from IDEA.md/DECISIONS.md.

## Final audit (Audit-4, 2026-11-08) — PASS

Independent end-to-end audit reproduced from fresh folders: build 0w/0e; tests 21/21;
Sprawling conversion SHA 52077488...; CardWeapon split Part2 SHA 5c94e9e7..., original
SHA f205f2c2...; independent raw TES4 checks and separate Mutagen verifier PASS
(zero master->dependent refs, 2,055 original->master links resolve, 0 dangling, no
CELL/placed/WRLD in Part 2, overrides/assignments exact); dependents fixtures 14/14 x3;
release zip SHA matches manifest and contains no forbidden data; GUI UIA flows equal
CLI; source untouched. Verdict PASS with 6 MINOR / 8 NOTE, recorded as I26/I27 and in
docs/LIMITATIONS.md. V9 in-game load remains user-side and unclaimed.
