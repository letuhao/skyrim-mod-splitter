# Skyrim Mod Splitter

Windows desktop tool that converts classic Skyrim SE/AE plugins into **ESL-safe
(ESPFE) plugins** without losing FormID references, and splits a plugin into multiple
ESL parts only when one plugin cannot fit the 2,048-new-record light-plugin budget.

One C# core, two front ends: a WPF GUI and a CLI.

## Status

- v1 implemented and structurally validated.
- Real-mod acceptance performed on two mods (read-only copies):
  - SprawlingBimbos.esp -> one ESPFE (779 new records renumbered into 0x800-0xB0A,
    158 overrides kept, 98 FaceGen files renamed; 937 records in/out).
  - CardWeapon_SE.esp -> two ESL plugins (Part 2 master: 2,048 own records; original
    filename: 1,319 own + 6 overrides; 3,373 records in/out; load order Part 2 first).
- Build: 0 warnings / 0 errors. Tests: 21/21.
- Four independent slice audits passed (scan, conversion, content rewriting, final
  end-to-end). Audit evidence is produced locally under artifacts/ (gitignored).
- **In-game load is not tested by the tool or by the audits.** Validation is structural
  (Mutagen reparse, FormID/master/link rules). Runtime testing is the user's gate.

## Features

- Scan a mod folder: plugins (raw TES4 + Mutagen analysis), BSA listing, Papyrus
  scripts, FormID-bearing configs, assets, FaceGen pairs, link/master statistics.
- ESL conversion: deterministic renumber into 0x800-0xFFF, ESL flag, master-list and
  HEDR preservation, validation of source correspondence and all self/master links.
- Split: cell-aware SCC planner; new dependency-closed light master loads first; the
  original filename stays on the dependent part. Two-part split in v1.
- Reference rewriting: record FormLinks, VMAD, Papyrus .psc/.pex (Champollion +
  PapyrusCompiler with fallbacks), INI/JSON/TXT configs with encoding preservation,
  voice filenames, SEQ reported for Creation Kit follow-up.
- FaceGen: NPC head NIF + tint DDS renamed for renumbered records; changed assets are
  emitted loose; original BSA/BSL files are never modified or repacked.
- Coverage warnings: shared token rules between rewriter and coverage scanner, stale
  config-token gate, report-only token reporting; warnings shown in the GUI tab and
  written to reports.
- Dependents (optional): scan an MO2 profile or an explicit search root, warn when
  other plugins/configs reference the target, and with --patch-dependents emit
  rewritten file copies under output/dependents (never edited in place).
- Deterministic output and manifests (old->new FormID map, asset renames, validation).

## Requirements

- Windows 10/11 x64
- .NET 8 Desktop Runtime
- Optional (auto-discovered, env-overridable):
  - BSArch.exe - SMS_BSARCH (archive listing only; read-only)
  - Champollion.exe - SMS_CHAMPOLLION (pex decompile)
  - PapyrusCompiler.exe + TESV_Papyrus_Flags.flg - SMS_PAPYRUS_COMPILER / SMS_PAPYRUS_FLAGS
  - vanilla script source folders - SMS_VANILLA_SCRIPTS (semicolon separated)

## Quick start - GUI

1. Run gui/SkyrimModSplitter.Gui.exe (or the build output).
2. Choose the mod folder that directly contains the plugin.
3. Optional: enter data dir(s), semicolon separated, for master verification.
4. Use 1. Scan, then:
   - 2. Plan ESL conversion -> 3. Apply conversion, or
   - 4. Plan split -> 5. Apply split.
5. Outputs go to output/<ModName>-ESL or output/<ModName>-split. The source folder is
   never modified. Cancel stops an in-flight apply; Open output folder reveals results.

## Quick start - CLI

    sms scan  <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <dir>]
    sms plan  <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <outputDir>]
    sms apply <modFolder> [--plugin <file.esp>] [--data-dir <dir>]... [--out <outputDir>] [--overwrite]
    sms split <modFolder> [--plugin <file.esp>] [--out <outputDir>] [--budget 2048] [--apply]

Dependent safety (optional):

    sms apply <modFolder> --mo2-profile "<MO2>\profiles\<profile>" [--patch-dependents]
    sms split <modFolder> --mo2-profile "<MO2>\profiles\<profile>" [--patch-dependents]

Without a profile or search root the tool prints that external safety was unchecked.

Exit codes: 0 success, 1 error/validation failure, 2 usage, 3 blocked/unsafe.

## Output and load order

- output/<ModName>-ESL/: converted plugin + copied loose assets + conversion-manifest.json
  + CONVERSION-REPORT.md.
- output/<ModName>-split/: two plugins + copied assets + split-manifest.json +
  SPLIT-REPORT.md.
- For a split, the new master loads before the original filename:

      CardWeapon_SE - Part 2.esp   (ESM + ESL light master)
      CardWeapon_SE.esp            (ESL dependent)

- Install outputs as new mods in a copied MO2 profile; do not overwrite the source mod.
- Existing saves are not migrated (runtime FormIDs change).

## Validation performed by the tool

- Outputs reparse with Mutagen and the raw TES4 reader.
- Own FormIDs in 0x800-0xFFF, unique, within ESL budget.
- ESL flag/ESM rules; exact master lists; no dangling self-links or links to
  non-master plugins; conversion source-correspondence (type/EditorID/link sets).
- Split rules: no master->dependent references/cycles, original->master links resolve,
  load-order direction, record-count and override preservation.
- Coverage gate: stale old FormID tokens in editable configs block publish.

The tool does not claim runtime success; in-game testing is separate.

## Known limitations (v1)

- Two-part split only; ESM splitting and localized plugins are refused.
- Original BSA/BSL files are never modified or repacked; changed assets are loose.
- FaceGen assets inside BSAs are copied unchanged and not itemised (loose FaceGen is
  renamed and checked).
- No separate GUI Verify screen, MO2-profile picker, target-plugin picker or settings UI;
  verification is shown inline after Apply and written to reports, and dependent
  scan/patch is CLI-only.
- Split output replaces an existing --out folder without an --overwrite gate; the GUI
  split ignores the Overwrite checkbox.
- Dependent patch errors and unreadable editable configs are reported but do not change
  exit codes.
- Native SKSE DLLs are not rewritten; SEQ regeneration is user-side (Creation Kit).
- Save-game migration is out of scope.

These are recorded in DECISIONS.md (I26/I27) and docs/LIMITATIONS.md.

## Build, test, release

    dotnet build SkyrimModSplitter.sln -c Release
    dotnet test tests/SkyrimModSplitter.Tests/SkyrimModSplitter.Tests.csproj -c Release
    pwsh scripts/release.ps1 -Version 1.0.0

Real-mod acceptance tests skip when test-corpus/ (gitignored local copies) is absent;
synthetic tests always run.

## Repository layout

    src/SkyrimModSplitter.Core/   scanning, conversion, rewriting, split, dependents, validation
    src/SkyrimModSplitter.Cli/    sms CLI
    src/SkyrimModSplitter.Gui/    WPF desktop app
    tests/SkyrimModSplitter.Tests/
    docs/                         usage, limitations, evidence summaries
    scripts/release.ps1           portable release zip builder
    PLAN.md, DECISIONS.md, IDEA.md

## License

MIT. See LICENSE.
