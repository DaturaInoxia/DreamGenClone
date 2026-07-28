# Plan: Continuation Prompt Updates

**Date:** 2026-07-27
**Goal:** Four coordinated updates to the continuation prompt: (1) move Style Guide slot to end, (2) word count markers via phase guidance, (3) enrich interaction history metadata, (4) audit character memory pipeline.

## Phase 1 — Move WritingStyleSlot to End of Prompt

Move only — do NOT merge with FinalInstructionSlot or any other slot.

**Steps**:
1. Change `WritingStyleSlot.Order` from `8` → `18`, Zone from `B` → `C`, `IsTrimEligible` → `false` in `WritingStyleSlot.cs`
2. Update `RolePlayPromptBuilder.GetExpectedOrder`: `PromptSlotId.WritingStyle => 8` → `=> 18`; update `GetExpectedZone` so WritingStyle maps to Zone C
3. Update `PromptSlotId.WritingStyle` XML doc comment in `PromptSlotId.cs`
4. Remove `PromptSlotId.WritingStyle` entry from `PromptBudgetEnforcer` priority map (no longer trim-eligible)
5. Update `SlotContractTests`: `WritingStyleSlot_HasCorrectIdentity` (Order=18, Zone=C), `WritingStyleSlot_IsTrimEligible` (→ true since we set `false` → `Assert.False` stays), fix stale `WritingStyleSlot_OutputsReferenceLine` test

*Parallel with Phase 2.*

## Phase 2 — Word Count Markers

Use `[targetwords:small]` / `[targetwords:medium]` / `[targetwords:large]` pattern consistent with existing `[Pacing:fast]`, `[ClimaxMode:multi-encounter]` markers. Default `[small]`. No Word Target on Narrative variant.

**Marker Ranges**:
- `[small]`: 200–400 words
- `[medium]`: 300–700 words
- `[large]`: 500–1000 words

**Steps**:
1. Add `GetWordTargetMarker(RPTheme?, string phase) → string?` to `RolePlayAssistantPrompts.cs` (follows `GetPacingMode` pattern — scans phase guidance for `[targetwords:small/medium/large]`)
2. Add `WordTargetMarker` field to `ResolvedWritingStyleData` in `PromptBuildContext.cs` (nullable string)
3. Wire into `BuildPromptViaBuilderAsync` in `RolePlayContinuationService.cs`: call `GetWordTargetMarker`, pass result into `ResolveWritingStyleAsync`
4. Update `ResolveWritingStyleAsync`: accept marker parameter; when marker is non-null, override `WordTargetMin`/`WordTargetMax` from the marker mapping; when null, use `SteeringProfile` values as-is (backward compat)
5. Update `WritingStyleSlot.WriteAsync`: replace current Word Target lines with marker-based output for Character variant; skip Word Target entirely for Narrative variant
6. Update tests: `SlotContractTests`, add tests for marker override + Narrative no-word-target

*Parallel with Phase 1.*

## Phase 3 — Enrich Interaction History

Current output: `[ActorName]: content`
Target output: `[ActorName (Role)] Turn N, Interaction M/T: content`

**Steps**:
1. Add `IReadOnlyDictionary<string, string>? ActorRoleMap` to `PromptBuildContext` — populated in `BuildPromptViaBuilderAsync` from scenario characters
2. Add turn grouping computation in `BuildPromptViaBuilderAsync` — group `RecentInteractions` into turns (sequential cycles through all actors), produce turn number + position-in-turn + turn-actor-count per interaction
3. Add turn metadata to `PromptBuildContext` as `IReadOnlyList<RecentInteractionEntry>` where each entry wraps the interaction + `TurnNumber`, `PositionInTurn`, `TurnActorCount`. Or add parallel lists. **Prefer wrapper type for clarity.**
4. Update `InteractionHistorySlot.WriteAsync`: read role from `ActorRoleMap`, read turn metadata, format each line as `[ActorName (Role)] Turn N, Interaction M/T: content`
5. Update `InteractionHistorySlot.Trim`: adjust line-matching patterns if format changes affect trim logic
6. Update tests: `SlotContractTests.InteractionHistorySlot_ThreeTierCompression_FullDetailForRecent`

## Phase 4 — Character Memory Audit (Analysis Only)

**Deliverable**: findings document at `/memories/session/memory-audit-findings.md`. No code changes.

**Audit scope**:
1. **Map the pipeline**: triggers → generation → storage → prompt injection
   - Triggers: phase transitions, arc completion, encounter boundaries
   - Generation: `IEncounterSummaryService` templates → `EncounterSummaryJobHandler` LLM enrichment
   - Storage: `RolePlayV2EncounterSummaries` table
   - Injection: `SessionMemorySlot` 3-tier output
2. **Duplication analysis**: compare `InteractionHistorySlot` (raw recent interactions) vs `SessionMemorySlot` (LLM-summarized memories) — do they describe the same events redundantly?
3. **Accuracy audit**: review `EncounterSummaryJobHandler` prompt templates (`BuildMilestonePrompt`, `BuildArcCompletionPrompt`, `BuildEncounterCompletionPrompt`) — do they provide sufficient context? character-specific filtering?
4. **Trigger coverage**: catalog all `SaveEncounterSummaryAsync` / `GenerateTemplatesAsync` call sites — gaps? redundant writes?
5. **Recommendations**: if issues found, propose fixes as separate follow-up phases

## Relevant Files

- `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/WritingStyleSlot.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SessionMemorySlot.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/RolePlayPromptBuilder.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBudgetEnforcer.cs`
- `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`
- `DreamGenClone.Domain/RolePlay/PromptSlotId.cs`
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`
- `DreamGenClone.Domain/StoryAnalysis/SteeringProfile.cs`
- `DreamGenClone.Infrastructure/StoryAnalysis/SteeringProfileService.cs`
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs`
- `DreamGenClone.Application/RolePlay/IEncounterSummaryService.cs`
- `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs`
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`
- `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs`
- `DreamGenClone.Tests/RolePlay/Prompts/PromptBuilderTests.cs`

## Verification

1. `dotnet build DreamGenClone.Web/DreamGenClone.csproj && dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj`
2. `dotnet test --filter "FullyQualifiedName~SlotContractTests"`
3. `dotnet test --filter "FullyQualifiedName~PromptBuilderTests"`
4. Manual: inspect built prompt via `helpers/dbq-session.ps1` — verify Style Guide at end, word marker, interaction history format
5. Phase 4: review findings document before any code changes

## Decisions

- WritingStyleSlot: move only, no merge with FinalInstructionSlot
- Word markers: `[targetwords:small/medium/large]` pattern, consistent with `[Pacing:*]` / `[ClimaxMode:*]`
- Marker override: when `[targetwords:*]` present → use marker range; absent → use SteeringProfile values (backward compat)
- Narrative: no Word Target line emitted (regardless of markers or profile values)
- Interaction role: resolve from scenario characters at build time (no data migration)
- Phase 4: audit-only, no implementation until findings reviewed
