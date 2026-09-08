# 049 - Beat production: deterministic assembly (typedReferences + videoCoverage) implemented

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59`
- **Feature**: beat production deterministic assembly (design:
  `specs/Planning/B-100-progressive-scene-beat-pipeline/beat-production-deterministic-assembly-design.md`)
- **Date**: 2026-09-08 · follows 045/047/048

## Report

Beat production was authoring the entire canonical JSON in four LLM passes, with the final
"assembly" pass emitting `startContinuity`/`endContinuity`/`typedReferences`/`videoCoverage` in one
~19k-char call. Three distinct terminal validation failures in one day (045 ordering, 047 verbatim
source text, 048 assembly omitted `startContinuity`), each after ~9–12 min of active generation.

## Analysis

- `typedReferences` and `videoCoverage` are mechanically derivable from the source snapshot +
  structure/spoken/soundscape outputs (participant profile keys, cue-key unions, fixed
  `requiredMomentRoles` rules) — they should not be model-authored.
- `startContinuity`/`endContinuity` state (character posture, lighting, wardrobe) is semantic and
  cannot be folded purely from app inputs — it must remain a model pass, but can be a small
  dedicated pass instead of the large combined assembly.
- Consumer semantics grounded against `SceneBeatProductionParser` DTOs, a completed v3 plan
  (`cb42375c`), and `DeterministicMultimodalMediaCompiler`.

## Plan (implemented Stage 1)

1. Contract v5: remove the assembly pass + its prompt; add a small `continuity` pass
   (`startContinuity`/`endContinuity` only). `ProviderPassCount` stays 4.
2. New `SceneBeatProductionAssembler` (static): deterministically builds `typedReferences`
   (CharacterIdentity/WardrobeContinuity/LocationContinuity per participant + VoiceIdentity per
   speaker) and a single `WholeBeat` video coverage (sourceEventKeys = all events, cue-key unions,
   exact `audioOwnership` union, `requiredMomentRoles` `['start','end']`, `Validated`).
3. Handler: run structure → spoken ∥ soundscape → continuity, then assemble deterministically.
4. Parser + downstream consumers unchanged (assembler emits the same 12-section JSON shape).

## Resolution

- `SceneBeatProductionContract.cs` — `ContractVersion` → `scene-beat-production-v5`; removed
  `BuildAssemblyPass`/`AssemblySystemPrompt`; added `BuildContinuityPass`/`ContinuitySystemPrompt`.
- `SceneBeatProductionAssembler.cs` — new deterministic assembler.
- `SceneBeatProductionPlanJobHandler.cs` — replaced assembly pass with continuity pass + assembler;
  generic catch now surfaces the inner exception message (root cause no longer swallowed).
- Tests: `SceneBeatProductionContractTests` (continuity pass), `SceneBeatPipelineEndToEndTests`
  (coverage kind → `WholeBeat`).

## Validated

- Web + Tests build: 0 errors.
- Beat-production cluster (`SceneBeatProduction*`, `SceneBeatPipeline*`, `SceneBeatAnalyzer*`,
  `SceneMoment*`): **102 passed / 0 failed**.
- Note: 8 pre-existing, unrelated failures in `SceneImageServiceJobTests`
  (`EnqueueRenderAsync_*`) — "Image render dispatch requires the model resolution service"
  (DI fixture gap in that test area, not touched here).
- Live re-run of b1 on the running app: pending.
