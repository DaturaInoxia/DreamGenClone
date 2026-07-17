# Phase 0 Research: RP Prompt Redesign

**Branch**: `001-rp-prompt-redesign` | **Date**: 2026-07-17

This document resolves every NEEDS CLARIFICATION and open technical question raised during plan generation. Each entry records the Decision, Rationale, and Alternatives considered. All decisions align with the repo Hard Rules (no fallbacks for RP engine behavior, fail-fast on missing config, UI-backed persisted config for every RP behavior control).

---

## R1. Where does the new prompt code live in the layered solution?

**Decision**: New code lives in `DreamGenClone.Web/Application/RolePlay/Prompts/` (Application layer). Pure domain enums (`PromptZone`, `ActorProfileKind`, `PromptSlotId`, `PromptVariant`) live in `DreamGenClone.Domain/RolePlay/`. Config options live in `DreamGenClone.Infrastructure/Configuration/`.

**Rationale**: The existing `RolePlayContinuationService` already lives in `DreamGenClone.Web/Application/RolePlay/` and depends on Application-layer services (`IScenarioService`, `IRPThemeService`, `IIntensityProfileService`, `ISteeringProfileService`, `IScenarioGuidanceContextFactory`). Slots need the same dependencies, so they belong in the same layer. Domain enums have no dependencies and belong in Domain. This preserves the existing dependency direction (Web → Application → Domain ← Infrastructure) per Constitution II without introducing a 5th project.

**Alternatives considered**:
- *New `DreamGenClone.Prompts` project*: Rejected — would require either duplicating Application-layer service interfaces or creating a circular dependency. The constitution mandates "at minimum" Web/Application/Domain/Infrastructure; adding a 5th project for one feature is unjustified complexity.
- *Slots in Infrastructure*: Rejected — Infrastructure depends on Domain and Application abstractions, but slots are orchestration logic, not persistence/IO adapters.

---

## R2. How is `MaxPromptChars` persisted and seeded without a hardcoded default?

**Decision**: Add a nullable `int? MaxPromptChars` column to the `Sessions` table via idempotent `ALTER TABLE` migration (same pattern as existing `MaxMilestonesToInject`, `MaxArcCompletionsToInject`, `MaxEncounterCompletionsToInject` columns at `SqlitePersistence.cs:1227-1242`). Add a matching `int? MaxPromptChars` property to `RolePlaySession`. New sessions are seeded with `35000` by the session-creation path (`CreateRolePlaySessionRequest` / `SessionService`), NOT by a code default on the property. The runtime reads the persisted value and fails fast with an explicit diagnostic if it is missing or invalid (FR-004).

**Rationale**: The spec is explicit (Clarification 2026-07-17, Assumption 3, FR-004): `MaxPromptChars` MUST be UI-backed persisted config with no hardcoded code default. The existing `Max*ToInject` columns demonstrate the exact pattern: nullable column, idempotent migration, session property, seeded at creation. The 35,000 value is a documented recommended initial config value, not a code default — it lives in `RolePlayPromptOptions.RecommendedInitialMaxPromptChars` (a constant used only by the session-creation seeder, never by the runtime prompt builder).

**Alternatives considered**:
- *Non-nullable column with DEFAULT 35000*: Rejected — a SQL DEFAULT is a hidden fallback. The runtime would silently use it when the property is unset, violating the fail-fast contract.
- *`RolePlayMemoryOptions.MaxPromptChars` global config*: Rejected — the spec requires per-session configuration (SC-011: "Users of 128K-context models can configure `MaxPromptChars` to 80000"). A global option cannot vary per session.

---

## R3. How are the three compression-threshold families (FR-012, FR-015, FR-016) persisted?

**Decision**: Add nullable session-scoped properties to `RolePlaySession` and matching nullable columns to `Sessions`:

| Property | Type | Purpose |
|----------|------|---------|
| `ScenarioCompressionTurnThreshold` | `int?` | Turn band after which scenario context compresses to 2-3 line summary (FR-012). Recommended seed: 10. |
| `HistoryFullDetailTurnBand` | `int?` | Number of recent turns with full character+narrative detail (FR-015 Layer 1). Recommended seed: 3. |
| `HistoryNarrativeOnlyTurnBand` | `int?` | Number of middle turns with narrative-only summaries (FR-015 Layer 2). Recommended seed: 3. |
| `SessionMemoryLongTermTurnThreshold` | `int?` | Turn after which long-term memory compresses (FR-016 Tier 1). Recommended seed: 10. |
| `ContextWindowTurns` | `int?` | Turn-based history window (replaces `ContextWindowSize` interaction count for prompt building). Recommended seed: 8. |

All seeded at session creation from `RolePlayPromptOptions` recommended values. Runtime fails fast if any required threshold is missing/invalid (FR-012a).

**Rationale**: The spec (Clarification 2026-07-17, FR-012a) mandates all compression thresholds be UI-backed persisted config with no hardcoded defaults. Session-scoped (not global) because SC-011 requires per-session tunability. The existing `ContextWindowSize` (interaction count) is preserved for backward compatibility in non-prompt paths (semantic inference context windows at `RolePlayEngineService.cs:5092`) but the prompt builder reads `ContextWindowTurns` exclusively.

**Alternatives considered**:
- *Global `RolePlayMemoryOptions` thresholds*: Rejected — cannot vary per session; violates SC-011.
- *Hardcoded constants*: Explicitly forbidden by FR-012a and the repo Hard Rule.

---

## R4. How is phase-specific Rule-of-Thumb text persisted (FR-014)?

**Decision**: Add a new `PhaseRuleOfThumb` config table:

```sql
CREATE TABLE IF NOT EXISTS PhaseRuleOfThumb (
    Id TEXT NOT NULL PRIMARY KEY,
    Phase TEXT NOT NULL,              -- Opening, BuildUp, Committed, Approaching, Climax, Reset
    RuleOfThumbText TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    UNIQUE(Phase)
);
```

Seed the six phase rows on first run with the text from GAP-6 of the design reference. The `WritingStyleSlot` reads the row for the current phase and fails fast with an explicit diagnostic (session ID, phase name) if the row is missing (FR-014). The writing style profile's existing `SteeringProfile.RuleOfThumb` becomes a separate always-present slot element (the profile default), NOT a fallback. If the profile lacks a default, that also fails fast.

**Rationale**: The spec (Clarification 2026-07-17, Assumption 4, FR-014) requires phase Rule-of-Thumb to be UI-backed persisted config, fail-fast on missing, with no fallback path. A dedicated table (rather than a JSON blob on `SteeringProfile`) allows per-phase UI editing and is consistent with how `RPTheme`, `NarrativeGateProfile`, and other RP config entities are stored as first-class tables. The six phases are a fixed enum (`NarrativePhase`), so the table is small and stable.

**Alternatives considered**:
- *JSON blob on `SteeringProfile` (e.g., `PhaseRuleOfThumbJson`)*: Rejected — harder to edit per-phase in UI, harder to fail-fast on a single missing phase, and breaks the pattern of one row per config entity.
- *Hardcoded switch statement (as shown in GAP-6)*: Explicitly forbidden by FR-014 and the repo Hard Rule. The GAP-6 code snippet is illustrative of content, not implementation.

---

## R5. How does the new builder coexist with the existing `SceneDirectionCoordinator` and 13 `IPromptInjector` implementations?

**Decision**: The new `RolePlayPromptBuilder` fully replaces both the inline `BuildPromptAsync` method AND the `SceneDirectionCoordinator` pipeline for RP prompt construction. The 13 existing injectors (`TurnContextInjector`, `BehavioralFrameInjector`, `ThemeContractInjector`, `ThemeAIGuidanceInjector`, `IntensityContractInjector`, `EscalationInjector`, `SceneTimeDirectionInjector`, `ScenePresenceInjector`, `PositionListInjector`, `HusbandAftermathInjector`, `BeatStageInjector`, `FinalDirectiveInjector`, `TimeLocationInjector`) are deleted. Their content is absorbed into the appropriate slots:
- `TurnContextInjector` → `TurnContextSlot` (S-003: duplicate removed)
- `BehavioralFrameInjector` → `BehavioralFramesSlot` (S-018: generic stub removed)
- `ThemeContractInjector` + `ThemeAIGuidanceInjector` → `ThemeContractSlot` (single instance)
- `IntensityContractInjector` + `EscalationInjector` + `SceneTimeDirectionInjector` → `IntensityPacingSlot` (S-019: merged)
- `PositionListInjector` → `IntensityPacingSlot` (available positions)
- `ScenePresenceInjector` → `SceneContinuityAnchorSlot` (S-015: cross-perceptions only)
- `HusbandAftermathInjector` → `BehavioralFramesSlot` (aftermath is a behavioral frame aspect)
- `BeatStageInjector` → `ScenarioGuidanceSlot`
- `FinalDirectiveInjector` → `FinalInstructionSlot` (S-024: single instance)
- `TimeLocationInjector` → `SceneAnchorSlot` + `WorldStateSlot`

The `IPromptInjector` interface, `SceneDirectionCoordinator`, and `PromptInjectionContext` are deleted. The new `IPromptSlot` interface and `PromptBuildContext` record replace them.

**Rationale**: The spec (FR-028, SC-010) mandates full replacement with no residual legacy code path. The design reference (GAP-7 Migration Strategy) explicitly calls for deleting the 900-line method and the coordinator's inline duplicates. Keeping the coordinator as a parallel system would reintroduce the exact duplication the redesign eliminates (S-003, S-018, S-024).

**Alternatives considered**:
- *Keep coordinator as an implementation detail of one slot*: Rejected — the coordinator's 13 injectors produce content that belongs in 8 different slots across 3 zones. Routing them through one slot would recreate the monolithic method under a different name.
- *Adapter that maps injectors to slots*: Rejected — adds indirection without value; the injectors are simple text builders that are easier to inline into their target slots than to adapt.

---

## R6. How is the `ActorProfile` resolved from `ContinueAsActor` + `PromptIntent`?

**Decision**: `ActorProfileResolver` takes `(ContinueAsActor actor, string? customActorName, PromptIntent intent, RolePlaySession session, ScenarioCharacter[] presentCharacters)` and returns an `ActorProfile` record with `Kind` (one of `Player`, `NpcPresent`, `NpcNonPresent`, `Narrative`, `Custom`) plus resolved `ActorName`, `ActorRole`, and `PresentCharacterIds`. Resolution rules:

| `intent` | `actor` | `Kind` |
|----------|---------|--------|
| `Narrative` | (any) | `Narrative` |
| `Message`/`Instruction` | `You` | `Player` |
| `Message`/`Instruction` | `Npc` + actor is in current scene location | `NpcPresent` |
| `Message`/`Instruction` | `Npc` + actor is NOT in current scene location | `NpcNonPresent` |
| `Message`/`Instruction` | `Custom` | `Custom` |

"Present" is determined by `RolePlayScenePresenceHelper` (already exists) using `session.AdaptiveState.CurrentSceneLocation` and the character location truth state. If the requested actor is not found in the session's character roster, fail fast with an explicit diagnostic (Edge Case: Actor profile mismatch).

**Rationale**: The spec (FR-024, FR-025, FR-026, GAP-4) defines exactly 5 profiles. The existing `ContinueAsActor` enum (`You`, `Npc`, `Custom`) plus `PromptIntent` (`Message`, `Narrative`, `Instruction`) provides enough information to resolve the profile. The `NpcPresent` vs `NpcNonPresent` distinction requires scene-presence data that already exists. Fail-fast on unknown actor aligns with the Edge Cases section.

**Alternatives considered**:
- *Store the profile kind on the session*: Rejected — the profile is a build-time resolution, not persisted state. Storing it would create stale-data bugs when scene presence changes.
- *Collapse `NpcPresent`/`NpcNonPresent` into one `Npc` profile*: Rejected — the spec (FR-025) mandates different content filtering for non-present NPCs (full self + comparison-only for present chars, reduced directive scope).

---

## R7. How does tiered history compression interact with the token budget?

**Decision**: The `InteractionHistorySlot` first computes the tiered window using the configured turn bands (R3): Layer 1 (recent, full detail), Layer 2 (middle, narrative-only), Layer 3 (long-term, encounter summaries from `SessionMemorySlot`). It then produces text. The `PromptBudgetEnforcer` runs AFTER all slots have produced text and trims in the FR-029 priority order: Slot 9 (oldest history) → Slot 5 (non-present char data) → Slot 6 (scenario metadata) → Slot 10 (session memory) → remaining Zone B low-priority slots (7, 11, 8). Trimming Slot 9 means dropping the oldest turns from Layer 1 first, then Layer 2, then Layer 3 — never touching Zone A, Slot 12, Slot 15, Slot 16 (when present), or Slot 17.

If even the mandatory (never-trim) slots exceed `MaxPromptChars`, the builder logs a critical warning and still produces the prompt (Edge Case: Budget overflow with minimal content).

**Rationale**: The spec (FR-029, FR-030, GAP-3, S-026) defines this exact trim priority. Two-phase execution (build all, then trim) is simpler than interleaving because it avoids re-running slots after trimming. The critical-warning-on-overflow path matches the Edge Case requirement.

**Alternatives considered**:
- *Per-slot budget allocation with live trimming during build*: Rejected — requires each slot to know the remaining budget, creating tight coupling and making independent unit testing (FR-036) harder. Two-phase is cleaner.
- *Trim during build by checking cumulative char count*: Rejected — same coupling problem.

---

## R8. How are encounter detection secondary signals (FR-034) implemented?

**Decision**: Extend `RolePlayEngineService.TryDetectEncounterBoundaryAsync` (currently at `:5190`) to evaluate four secondary signals IN ADDITION to the existing LLM-based `encounter-completed` semantic inference:

1. **Scene change after intimacy** — if `session.AdaptiveState.CurrentSceneLocation` changes within N turns of `WasInSexScene=true` interactions.
2. **Significant time passage** — if the narrative response contains time-skip markers ("later that evening", "the next morning", "after a while") and the previous turn had sexual activity.
3. **Explicit encounter boundary language** — if the narrative response contains phrases like "when it was over", "after they dressed", "once they had separated".
4. **Phase transition Climax → Reset** — always creates an encounter summary (this signal already exists in the phase-transition path; wire it to call the encounter-summary writer).

The existing male-orgasm keyword gate (`ContainsEncounterCompletionKeywords`) remains as a primary signal but is no longer the only signal. Secondary signals fire the same encounter-summary write path as the primary signal.

**Rationale**: The spec (FR-034, GAP-8) mandates secondary signals because the current male-orgasm-only detection misses encounters. The four signals are drawn directly from the design reference. Phase-transition Climax → Reset is already a natural encounter boundary in the existing phase machine.

**Alternatives considered**:
- *Replace the LLM inference entirely with keyword detection*: Rejected — the LLM inference is more accurate for ambiguous cases; secondary signals augment rather than replace.
- *Add a new LLM inference call for secondary signals*: Rejected — adds latency and cost; keyword/phase signals are sufficient for the common cases.

---

## R9. How is the encounter enrichment prompt rewritten (FR-033, FR-035)?

**Decision**: Rewrite the enrichment prompt in `EncounterSummaryJobHandler` to capture six dimensions per encounter: (1) what happened (plot), (2) what the character felt (emotional texture), (3) what they learned (sexual self-knowledge), (4) what changed (relationship dynamic), (5) what risk was taken (near-miss/discovery), (6) what the other character now knows. The input to the enrichment LLM is the Narrative response (primary source, per FR-035/S-027) plus character responses (emotional/POV detail). The output is a 3-5 sentence first-person memory from the character's perspective.

The existing `EncounterSummaryRecord.LlmSummary` field stores the output. No schema migration needed — only the prompt text changes. The `EncounterSummaryRecord.DetectionEvidence` field already preserves the detection span.

**Rationale**: The spec (FR-033, FR-035, GAP-8 Tier 2, S-027) defines the six dimensions and mandates Narrative response as the primary source. The existing `RolePlayV2EncounterSummaries` table and `LlmSummary` field are sufficient — the change is prompt-only. SC-009 requires at least 4 of 6 dimensions captured.

**Alternatives considered**:
- *New table for the six dimensions as separate columns*: Rejected — the dimensions are prose, not structured data. A single `LlmSummary` text field is more flexible and matches the existing pattern.
- *Separate LLM call per dimension*: Rejected — 6x the cost and latency for no quality gain; a single prompt can capture all six.

---

## R10. How is the World State slot (FR-009, Slot 4a) implemented without B-062?

**Decision**: `WorldStateSlot` implements `ShouldWrite(context)` to return `true` only when `context.WorldState` is non-null. `PromptBuildContext.WorldState` is a nullable record populated from `session.AdaptiveState` only when B-062 data is available. Until B-062 is implemented, `WorldState` is always null and the slot is silently omitted (FR-009, Assumption 2). The slot's `WriteAsync` produces the format defined in GAP-5:

```
World State:
- Day {N} of {total} — {dayOfWeek}. {timePhase} ({time}).
- Weather: {condition}, {temperature}°C. {humidity/description}.
- World rhythm: {ambient activity appropriate to time/location}.
- Temporal pressure: {any active time constraints}.
```

**Rationale**: The spec (FR-009, Assumption 2, User Story 5) explicitly states the slot is designed for B-062 but silently omitted until then. The conditional `ShouldWrite` pattern is the same pattern used by `UserDirectionSlot` (Slot 16, FR-022) and other conditional slots.

**Alternatives considered**:
- *Stub the slot with placeholder weather*: Rejected — would inject fabricated data into prompts, violating the "no guessed RP values" Hard Rule.
- *Skip implementing the slot until B-062*: Rejected — the spec mandates the slot exist now (FR-009) so the architecture is ready.

---

## R11. How are the 17 slots registered and ordered?

**Decision**: All 17 slot implementations are registered in `Program.cs` as `IPromptSlot` (same pattern as the existing `IPromptInjector` registrations at `:126-137`). `RolePlayPromptBuilder` receives `IEnumerable<IPromptSlot>` and sorts by `Zone` (A → B → C) then `Order` (1 → 17) at construction. The `PromptSlotId` enum encodes the frozen contract (spec: "17 Slots — Frozen") and each slot's `Id` property returns its enum value. A startup validation asserts that exactly 17 distinct slots are registered and their `Zone`/`Order` match the frozen contract — fail fast on mismatch.

**Rationale**: The spec (FR-001, FR-003, "Slot Architecture Contract — Frozen") mandates exactly 17 ordered slots and makes the zone/order/trim-eligibility normative. The DI-registration pattern matches the existing injector pattern, minimizing learning curve. Startup validation prevents accidental contract drift.

**Alternatives considered**:
- *Hardcoded slot list in the builder*: Rejected — defeats the swappable-seam principle (Constitution II) and makes testing harder.
- *Attribute-based ordering*: Rejected — attributes are metadata, not behavior; the enum + startup check is more explicit and testable.

---

## R12. How does the builder handle the Narrative variant's distinct content filtering?

**Decision**: Every slot receives `PromptBuildContext.Variant` (`Character` or `Narrative`) and `PromptBuildContext.ActorProfile` (with `Kind == Narrative` for Narrative prompts). Each slot's `WriteAsync` branches on `Variant`/`ActorProfile.Kind` to produce variant-specific content. Key differences (from spec FR-002, FR-026, GAP-1):

| Slot | Character variant | Narrative variant |
|------|-------------------|-------------------|
| 2 (Actor Assignment) | "Continue as: {name} ({role})" | "Write as omniscient narrator" |
| 5 (Character Data) | Full self + partners + comparison for non-present | All chars, lighter format, no persona, no intimate self-awareness |
| 9 (History) | Last 2-3 turns, all interactions | Same (Narrative needs to see what it synthesizes) |
| 13 (Behavioral Frames) | Self + partners only | All frames |
| 17 (Final Instruction) | 1st person, 100-300 words | 3rd person omniscient, 300-500 words, zero-dialogue constraint, physical detail checklist |

**Rationale**: The spec (FR-002, FR-026, User Story 2) mandates Narrative as a first-class variant with different content throughout, not just a different final instruction. Per-slot branching is the cleanest way to implement this without duplicating the builder.

**Alternatives considered**:
- *Two separate builders*: Rejected — duplicates the 17-slot orchestration and budget logic; the spec (FR-001) mandates "the same 17-slot architecture".
- *Strategy pattern per slot per variant*: Rejected — 17×2 strategies is excessive; a branch in `WriteAsync` is simpler and the slots are small.

---

## Summary

All 12 research items resolved. No NEEDS CLARIFICATION remains. The design is ready for Phase 1 (data model, contracts, quickstart).
