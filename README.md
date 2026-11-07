# Skyrim Mod Splitter

A Windows desktop tool that turns classic Skyrim SE/AE plugins into **ESL-safe
(ESPFE) plugins** without losing their FormID references:

- scans a mod folder (plugins, BSA archives, Papyrus scripts, configs, assets)
- renumbers new records into the light-plugin range 0x800-0xFFF
- sets the ESL flag and validates the result
- splits a plugin into multiple ESL parts only when one plugin cannot fit
- rewrites reachable FormID references: records/VMAD, Papyrus literals,
  INI/JSON/TXT configs, voice filenames
- renames FaceGen head meshes/tint textures (loose overrides; original BSA untouched)
- emits coverage warnings whenever it cannot prove a reference was rewritten

It is a WPF GUI + CLI over one shared C# core. No code is downloaded or executed from
mods; only typed plugin data is read and rebuilt.

## Requirements

- Windows 10/11, 64-bit
- .NET 8 Desktop Runtime (for the GUI and CLI)
- Optional tools, discovered via environment variables or known install paths:
  - BSArch.exe (archive listing; read-only) - SMS_BSARCH
  - Champollion.exe (pex decompile) - SMS_CHAMPOLLION
  - PapyrusCompiler.exe + TESV_Papyrus_Flags.flg - SMS_PAPYRUS_COMPILER / SMS_PAPYRUS_FLAGS
  - vanilla script source folders - SMS_VANILLA_SCRIPTS (semicolon separated)
- Mutagen.Bethesda.Skyrim is bundled through the .NET build.

## Quick start (GUI)

1. Run SkyrimModSplitter.Gui.exe.
2. Pick the mod folder (the folder that contains the .esp/.esm, not the MO2 mods root).
3. Optional: enter data dirs (semicolon-separated) so master files can be verified.
4. Press 1. Scan, then either:
   - 2. Plan ESL conversion -> 3. Apply conversion for a plugin that fits one ESL, or
   - 4. Plan split -> 5. Apply split for a plugin that is too large.
5. Output goes to output/<ModName>-ESL or output/<ModName>-split; the original mod
   folder is never modified. Warnings and validation results are shown in the app and
   written next to the output.

## Quick start (CLI)

    sms scan  <modFolder> [--data-dir <dir>]... [--plugin <file.esp>] [--out <dir>]
    sms plan  <modFolder> [--data-dir <dir>]... [--out <outputDir>] [--plan-out <dir>]
    sms apply <modFolder> [--data-dir <dir>]... [--out <outputDir>] [--overwrite]
    sms split <modFolder> [--out <outputDir>] [--budget 2048] [--apply] [--plan-out <dir>]

Dependent-mod safety (optional):

    sms apply <modFolder> --mo2-profile "<MO2>\profiles\<profile>" [--patch-dependents]
    sms split <modFolder> --mo2-profile "<MO2>\profiles\<profile>" [--patch-dependents]

- Without a profile/search root the tool prints that external safety was unchecked.
- --patch-dependents writes rewritten copies of dependent plugins/configs under
  <output>/dependents; install/enable them above the dependent mods in MO2.

## Output layout

    output/<ModName>-ESL/          converted plugin + copied loose assets + reports
    output/<ModName>-split/        original-name dependent part + new master part
                                   + copied loose assets + reports
    conversion-manifest.json       old->new FormID map, asset renames, validation
    CONVERSION-REPORT.md           human-readable conversion report
    SPLIT-REPORT.md / split-manifest.json
    dependents/                    optional rewritten dependent file copies
    conversion warnings are also listed in the app's Warnings tab.

Split load order: the newly named "Part 2" is a light master and loads BEFORE the
part that keeps the original filename. Example:

    CardWeapon_SE - Part 2.esp   (ESM + ESL master)
    CardWeapon_SE.esp            (ESL, masters Part 2)

## Validation

Every output is reopened with Mutagen and checked for: ESL flag, own IDs in range and
unique, record counts, master list preservation, source-correspondence of links for
conversions, self/master link integrity, and per-part split DAG/load-order rules.
The tool never claims runtime success; in-game load testing is a separate user step.

## Known limitations

- No BSA modification: archives are read only; changed assets are emitted loose.
- FaceGen renames are applied for converted loose assets; split archives with FaceGen
  are reported for manual follow-up.
- Localized plugins (STRINGS/DLSTRINGS/ILSTRINGS) are refused in v1.
- Existing save games are not migrated; runtime FormIDs change.
- SEQ files are reported, not regenerated (use the Creation Kit for affected quests).
- Dependent patch output is a set of file copies; no automatic MO2 installation.
- Runtime/load testing is user-side (V9). Structural validation only.
- GUI verify screens/settings/pickers are deferred in v1; validation is shown inline and
  written to reports, and dependent scan/patch is CLI-only.

## Build and test

    dotnet build SkyrimModSplitter.sln -c Release
    dotnet test tests/SkyrimModSplitter.Tests/SkyrimModSplitter.Tests.csproj -c Release

Real-mod acceptance uses copies of two mods under test-corpus/ (gitignored); see
artifacts/acceptance for recorded evidence.

## License

MIT. See LICENSE.