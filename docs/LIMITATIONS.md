# Limitations and non-goals (v1)

- Original BSA/BSL files are never modified or repacked. Changed FaceGen assets are
  emitted loose; split FaceGen archives are reported for manual follow-up.
- Native SKSE DLLs are not rewritten; only config/script text references are handled.
- Localized plugins are refused.
- Save games are not migrated; renumbering changes runtime FormIDs.
- SEQ regeneration is user-side (Creation Kit).
- Dependent patches are rewritten file copies; the tool does not install them.
- The 2,048 budget is the new-record/ESL budget; overrides do not consume it.
- Split output is two parts (master + original-name dependent). More-than-two-part
  splits are not implemented.
- Runtime correctness is never claimed: validation is structural
  (Mutagen reparse + reference/master/ESL rules). In-game load is a user-side gate.
- ESM splitting, localized plugins, and BSA repacking are explicitly out of scope.
- GUI: no separate Verify screen, no MO2-profile picker, no target-plugin picker and no
  full Settings UI in v1. Verification is shown inline after Apply and in reports;
  dependent scan/patch is CLI-only.
- Split output replaces an existing --out folder without an --overwrite gate; the GUI
  split ignores the Overwrite checkbox.
- Dependent patching is correct but re-parses dependent plugins per remap entry
  (performance, not correctness).
- FaceGen assets inside BSA archives are copied unchanged and not itemised (loose
  FaceGenData files are checked/warned).
- Dependent-patch errors and unreadable editable configs are reported in logs/reports
  but do not change exit codes.
