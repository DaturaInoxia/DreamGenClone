# 071 — Beat Catalogue synopsis length overflow

## Report

- Reported from Scene Image Studio on 2026-10-03.
- Beat Catalogue generation failed with `scene_beat_output_invalid`:
  `Beat Catalogue beat 'b3' beatSynopsis exceeds 400 characters.`
- Session `8bc36efb-b235-485b-8675-b98ad7754e59`, turn `e66462dacf29456f87f0cf2f9f7f98cb`.
- Failed attempts (dev DB `SceneBeatAnalysisAttempts`):
  `082b212a-…`, `1e28a2d1-…`, `e226d5f4-…` on 2026-10-03, plus `0fc48a5f-…` and `61e97796-…` on 2026-09-24.
  All five ran the `RolePlaySceneBeatAnalyzer` on `deepseek-flash` / `DeepSeek`.

## Analysis

### Where the value is resolved

`SceneBeatCatalogueContract.BeatSynopsisMaxLength` (single constant) is the only synopsis bound. It is consumed in
exactly two places, both from that one field:

- `Parse` → `RequiredBoundedString(element, "beatSynopsis", BeatSynopsisMaxLength, …)` — the enforcement path that
  raised the reported error.
- `CreateResponseSchema` → `StringSchema(BeatSynopsisMaxLength)` — the schema `maxLength` sent to the provider.

There is no fallback, no alternate bound, and no second decision path.

### Root cause

The analyzer is configured as DeepSeek direct (`https://api.deepseek.com`, `deepseek-flash`) with
`StructuredOutputMode = JsonObject` (mode 2, dev DB row `f4eeb102-…`, function `RolePlaySceneBeatAnalyzer`). In
that mode `OpenAiStructuredTextCompletionClient.BuildJsonObjectSystemMessage` pastes the JSON Schema into the
system message as **text only** — DeepSeek does not enforce schema keywords such as `maxLength` for
`json_object`. The only real enforcement is the strict parser, so any overshoot fails the whole attempt.

Measured synopsis lengths across all five failed attempts (30 beats, `json_each` over `RawModelResponse`):
**288–520 characters, median ≈ 390.** The model was aiming *at* the single stated number ("synopses under 400
characters") and overshooting it by 5–30 %. Two compounding facts:

1. The instructed budget and the enforced ceiling were the same number, and the model clusters on the number it
   is told, so the hard maximum sat inside the model's natural output distribution.
2. The same raw outputs drifted into enrichment-level detail the B-100 contract excludes from the catalogue
   (verbatim quoted dialogue, clothing-state inventory, lighting/scenery, interior reflection), which is what
   inflated the synopses.

This is the dominant catalogue failure mode: 5 of the 10 all-time `scene_beat_output_invalid` records are this
exact error (the others are 3 unknown-field errors, 1 label overflow, 1 location error).

400 was set when the contract was authored, before the analyzer ran on real turns; nothing else in the pipeline
depends on it. The downstream consumers (`SceneMomentDiscoveryContract`, `SceneMomentEnrichmentContract`) embed
`BeatSynopsis` verbatim in their prompts and impose no length limit of their own.

### Decision

Relax the enforced bound (user direction: the original value was an arbitrary pre-validation guess) and separate
two numbers that were previously conflated:

- **Instructed target** (prompt + schema description): 300–450 characters — kept below the ceiling so the model's
  natural cluster lands inside the accepted range.
- **Enforced ceiling** (parser): 600 characters — headroom over the observed maximum of 520.

No truncation, no retry on validation failure, no default, no alternate decision path: an over-ceiling synopsis
still fails the attempt explicitly with `scene_beat_output_invalid`.

## Plan

- `SceneBeatCatalogueContract`: raise `BeatSynopsisMaxLength` 400 → 600, add
  `BeatSynopsisTargetMinLength`/`BeatSynopsisTargetMaxLength` (300/450), state the target-versus-ceiling budget in
  `BuildSystemPrompt`, attach per-field `description` values to the bounded schema strings (so the budget travels
  with the field definitions in `JsonObject` mode), and bump `ContractVersion` `v3` → `v4` so stale prompt
  snapshots cannot be reused.
- Tests: assert the prompt states the target and the ceiling, assert the schema carries `maxLength` 600 plus a
  description, add a parse test proving 600 accepted / 601 rejected, and guard the invariant target < ceiling.
- Document the concrete budgets in the B-100 contract doc.

## Resolution

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/SceneBeatCatalogueContract.cs` | `BeatSynopsisMaxLength` 400 → 600; new `BeatSynopsisTargetMinLength = 300`, `BeatSynopsisTargetMaxLength = 450`; `ContractVersion` `scene-beat-catalogue-v3` → `-v4`; `StringSchema`/`StringSchemaAllowEmpty` take an optional `description`; `label`/`beatSynopsis`/`primaryLocation` now carry descriptions; `BuildSystemPrompt` trailing limit sentence replaced with a `FIELD BUDGETS` block (target + hard maximum + count-characters instruction + explicit prohibition on quoted dialogue, clothing/appearance/prop inventory, lighting/weather/scenery, and interior reflection) |
| `DreamGenClone.Tests/RolePlay/SceneBeatCatalogueContractTests.cs` | Added prompt-budget and schema-description assertions; new `Parse_AcceptsSynopsisAtEnforcedMaximumAndRejectsOverflow`; new `InstructedSynopsisTargetStaysBelowEnforcedCeiling` |
| `specs/Planning/B-100-progressive-scene-beat-pipeline/contracts/progressive-beat-pipeline-contract.md` | Validation rules now state the concrete label/synopsis/location budgets |

Version-bump consequence (as in debug `040`): a catalogue created under `v3` fails with
`scene_beat_contract_unsupported` when its job next runs and must be regenerated; catalogues already `Complete`
are untouched. No DB mutation, no configuration change, no restart needed.

## Validated

- [ ] Fresh Generate Beats run in Scene Image Studio (live provider) pending user confirmation.
