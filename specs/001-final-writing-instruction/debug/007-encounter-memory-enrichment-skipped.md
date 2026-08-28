# Debug 007: Encounter Memory Enrichment Skipped

**Created:** 2026-08-09
**Session:** 7763f8a8-4e5b-4502-8528-a7fb94bc1281

## Report

Encounter-completion memory records for encounters 3–6 were persisted with template summaries but `LlmSummary` and `LlmEnhancedUtc` remained null. The background enhancement jobs ran, but the handler logged `no interactions in range` and skipped each record.

## Analysis

The jobs were queued and executed, and `RolePlaySummaryEnhancement` resolved to `deepseek-v4-flash`. The records stored interaction ranges such as `[195-196]`, `[199-200]`, `[204-204]`, and `[207-208]`. `BuildEncounterCompletionPrompt` first filters `session.Interactions` with `!x.IsExcluded`, then applies `Skip(record.StartInteractionIndex).Take(...)`. The persisted indexes are based on the original interaction list, so filtering first shifts the positions and causes valid encounter ranges to become empty.

Authoritative artifacts consulted:

- `specs/001-final-writing-instruction/spec.md`
- `specs/001-final-writing-instruction/tasks.md`
- `specs/001-final-writing-instruction/research.md`
- `specs/001-final-writing-instruction/plan.md`
- `specs/001-final-writing-instruction/data-model.md`
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `specs/001-final-writing-instruction/contracts/terminology-mapping.md`
- `specs/001-rp-prompt-redesign/spec.md`
- `DreamGenClone.Web/logs/dreamgenclone-20260808.log`

## Plan

Update `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` so persisted interaction indexes are applied to the original interaction list before excluded interactions are removed. Add a focused regression test if the existing test seams support direct coverage. Build the web and test projects.

## Resolution

Updated `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` so persisted interaction indexes are applied before excluded interactions are filtered. Added a regression test in `DreamGenClone.Tests/RolePlay/Prompts/EncounterEnrichmentPromptTests.cs` covering an excluded interaction before a valid persisted range.

## Validated

- [ ] Web build passes (blocked by the running webapp locking its output assembly; compilation reached the changed code and reported only pre-existing warnings)
- [x] Focused encounter enrichment tests pass
- [ ] A fresh role-play session confirms encounter-completion memories are polished
