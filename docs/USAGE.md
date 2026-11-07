# Usage

## Concepts

- Conversion: one plugin that fits the 2,048-new-record ESL budget is renumbered
  into 0x800-0xFFF and gets the ESL flag (ESPFE).
- Split: a plugin whose new-record count exceeds the budget is split into two ESL
  parts. The new "Part 2" is a light master (ESM + ESL) and must load before the part
  that keeps the original plugin filename.
- Outputs are new folders; the source mod folder is never written.

## GUI

- Mod folder: the folder that directly contains the plugin (Data-style layout).
- Data dir(s): optional master locations (Stock Game/Data, MO2 mod folders with
  external masters), semicolon-separated.
- Scan: read-only inventory and ESL analysis.
- Plan/Apply conversion or Plan/Apply split: a plan is shown first; Apply writes the
  output. Output identity, validation and warnings are shown in the Conversion tab
  and the Warnings tab.
- Cancel interrupts an in-flight conversion/split.

## CLI exit codes

- 0 success
- 1 tool/validation error
- 2 usage error
- 3 blocked (split required/unsafe conversion, scan error warnings, validation fail)

## Environment overrides

- SMS_BSARCH, SMS_CHAMPOLLION, SMS_PAPYRUS_COMPILER, SMS_PAPYRUS_FLAGS,
  SMS_VANILLA_SCRIPTS
- SMS_TEST_CORPUS (accepted for acceptance harnesses; tests locate test-corpus from
  the repository root)

## What to review manually

Read WARNINGS.md/CONVERSION-REPORT.md/SPLIT-REPORT.md and the app's Warnings tab. Any
warning code beginning with PAPYRUS_, PEX_, COVERAGE_ or dependent-related code means
the tool could not fully prove a rewrite. Runtime load testing remains user-side.
