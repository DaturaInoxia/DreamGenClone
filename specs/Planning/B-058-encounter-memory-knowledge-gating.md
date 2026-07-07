# B-058: Per-Encounter Memory + Knowledge Gating

**State**: `designed`
**Priority**: high
**Scope**: large

---

## TL;DR

Add `EncounterCompletion` as a new `SummaryType` written at every encounter boundary detection within Climax (not just at Climax→Reset). Each entry captures encounter number, interaction range (start→end indices), detection evidence, positions, and stats — with async LLM enrichment generating prose suitable for internal dialogue recall. Then bridge B-056's aftermath injector to consume these enriched summaries instead of the raw ephemeral evidence span. Finally, gate the wife's awareness of another man's intimate attributes behind whether an `EncounterCompletion` exists (pre-encounter = attraction without knowledge; post-encounter = full perspective + comparison). Fix two B-041 gaps (per-session arc cap, dedicated enrichment model) along the way.

**Upstream dependencies**: B-041 (session memory), B-056 (wife-husband aftermath, implemented)
**Downstream consumers**: `IntimateBehavioralTextBuilder` knowledge gating, `HusbandAftermathInjector`, `RolePlayContinuationService`

---

## Discovery Summary

### Current State: B-041 (Session Memory Context) — `done`

Full working system with:

| Component | Detail |
|---|---|
| Entity | `EncounterSummaryRecord` + `EncounterSummaryType` enum |
| DB table | `RolePlayV2EncounterSummaries` (17 columns) |
| Summary types | `PhaseMilestone` (every non-Reset phase transition), `ArcCompletion` (Climax→Reset) |
| Generation | Synchronous template + async LLM enrichment (fire-and-forget, retry-once) |
| Prompt injection | `InjectSessionMemoryBlock` at position 7/23 (after Interaction History, before Scene Continuity) |
| Config | `RolePlayMemoryOptions`: MaxMilestonesToInject=5, MaxArcCompletionsToInject=10, EnableLlmSummaryEnhancement=true |
| Hooks | Lifecycle transitions + BuildUp→Committed gate in `RolePlayEngineService` |
| Tests | 5 unit test files |

### Current State: B-056 (Wife-Husband Aftermath) — implemented (52/53 tasks)

| Component | Detail |
|---|---|
| Enum extension | `TimeSkipPhase.AftermathCoupleInteraction = 3` |
| Evidence | `LastEncounterEvidenceSpan` — single raw detection-text string on `AdaptiveState` (ephemeral, cleared after leg) |
| Injector | `HusbandAftermathInjector` (priority 85) — contrast directive from raw evidence |
| Actor filter | Restricts to wife+husband during aftermath leg; Fast Pacing HC suppressed |
| Tests | 21 unit tests passing |
| Known issue | B-057 race fix applied; manual smoke test T053 pending |

### Key Gap Driving This Feature

**Multi-encounter arcs have no per-encounter memory.** When encounter boundary is detected within Climax (multi-encounter mode), the system increments `CurrentEncounterNumber` and enters time-skip, but **never writes a memory entry**. The next encounter has no record that the 1st encounter occurred — every encounter feels like the first time. B-041 only writes ArcCompletion summaries at Climax→Reset (end of entire arc), missing per-encounter granularity.

### Secondary Gap: No Knowledge Gating

`IntimateBehavioralTextBuilder` generates "she knows his body — [attributes]" text for both the husband AND other men from the very first prompt. There is **no progressive discovery**. The wife's knowledge of another man's intimate attributes (size, skill, stamina, performance) should be gated by whether an encounter has actually occurred. Research confirmed: `BuildPartnerPerspectiveText` and `BuildComparisonText` are unconditionally injected for all male partners from session start, with no `awarenessLevel` resolution (returns null due to TODO).

---

## Design Decisions

1. **New memory type**: `EncounterCompletion` — a new `SummaryType` alongside `PhaseMilestone` and `ArcCompletion`
2. **When written**: At each encounter boundary detection within Climax (not just at Climax→Reset)
3. **Generation**: Synchronous template + async LLM enrichment (same pattern as ArcCompletion)
4. **Interaction-range tracking**: Use interaction list indices (`StartInteractionIndex`, `EndInteractionIndex`) since interactions are stored as `List<RolePlayInteraction>` inline in `Sessions.PayloadJson` with no sequential ID column
5. **LLM enrichment**: Feed the actual interactions in the range (not `TakeLast(30)`) to the LLM with a dedicated prompt covering who, where, acts, positions, orgasms
6. **Recall via existing prompt block**: Inject encounter completions into the Session Memory block alongside arc completions and milestones
7. **B-056 bridge**: Evidence span flows into EncounterCompletion summary; aftermath injector reads summary instead of raw `LastEncounterEvidenceSpan`; then delete the ephemeral field
8. **Knowledge gating**: Wife's awareness of other man's attributes gated by EncounterCompletion existence — pre-encounter = attraction without knowledge, post-encounter = full perspective + comparison

---

## Implementation Plan

### Phase 1: Data Model — EncounterCompletion Type

*Steps 1–4: pure data-model changes, can run in parallel*

#### Step 1.1 — EncounterCompletion enum value + EncounterNumber + DetectionEvidence

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` | Add `EncounterCompletion` to `EncounterSummaryType` enum. Add `int EncounterNumber` (default 0; for EncounterCompletion rows, stores which encounter in the sequence). Add `string? DetectionEvidence` (raw detection text from semantic inference). |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add `EncounterNumber INTEGER NOT NULL DEFAULT 0` and `DetectionEvidence TEXT NULL` to `RolePlayV2EncounterSummaries` CREATE TABLE |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Include both new columns in INSERT/UPDATE/SELECT SQL statements |

#### Step 1.2 — Per-session MaxArcCompletionsToInject + MaxEncounterCompletionsToInject

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` | Add `int? MaxArcCompletionsToInject` and `int? MaxEncounterCompletionsToInject` |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add both columns to Sessions CREATE TABLE |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Include in Sessions SQL |
| `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor` | Add numeric inputs for both overrides (alongside existing milestones override) |

#### Step 1.3 — RolePlayMemoryOptions — new config properties

| File | Change |
|---|---|
| `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs` | Add `int MaxEncounterCompletionsToInject` (default 5) and `string SummaryEnhancementModelSlot` (default `"roleplay-summary-enhancement"`) |
| `appsettings.json` / `appsettings.Development.json` | Add both to `"RolePlayMemory"` section |

#### Step 1.4 — AppFunction enum for dedicated model slot

| File | Change |
|---|---|
| `DreamGenClone.Application/RolePlay/AppFunction.cs` (or equivalent) | Add `RolePlaySummaryEnhancement` app function value |
| `DreamGenClone.Web/Program.cs` | DI registration for the new model slot |

**Verify**: Build 0 errors.

---

### Phase 2: Interaction-Range Tracking + Encounter Start/End Markers

*Steps 5–8: sequential, depends on Phase 1*

#### Key Design: Interaction List Indices

Interactions are stored as `List<RolePlayInteraction>` inline in `Sessions.PayloadJson` — no sequential index column. We track encounter boundaries by **list position** in this in-memory list. A runtime-only field `CurrentEncounterStartInteractionIndex` on AdaptiveState (with `[JsonIgnore]` to avoid DB persistence) tracks where the current encounter began.

**Set when encounter starts:**
- **1st encounter**: When Climax phase is entered (lifecycle transition handler captures `session.Interactions.Count`)
- **2nd+ encounters**: When `AdvanceTime → None` completes (time-skip finishes, next encounter begins)

**Read when encounter ends:**
- At encounter boundary detection: `EndInteractionIndex = session.Interactions.Count - 1`

#### Step 2.1 — Add runtime encounter-start tracker to AdaptiveState + EncounterSummaryRecord fields

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Add `[JsonIgnore] public int CurrentEncounterStartInteractionIndex` (runtime-only, not persisted). Add `[JsonIgnore] public int LastEncounterEndInteractionIndex` (runtime-only, used at detection time as staging). |
| `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` | Add `int StartInteractionIndex` and `int EndInteractionIndex` (persisted — the range of interactions in this encounter) |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add `StartInteractionIndex INT NOT NULL DEFAULT 0` and `EndInteractionIndex INT NOT NULL DEFAULT 0` to `RolePlayV2EncounterSummaries` CREATE TABLE |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Include both new columns in INSERT/UPDATE/SELECT SQL |

#### Step 2.2 — Set encounter start at Climax phase entry

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In the lifecycle transition handler (around line ~3648, where `GenerateTemplatesAsync` is called after `SaveTransitionEventAsync`): when `lifecycle.NextState.Phase == NarrativePhase.Climax`, set `v2State.CurrentEncounterStartInteractionIndex = session.Interactions.Count`. This captures the moment Climax begins — interaction list index right before the first Climax interaction. |

#### Step 2.3 — Reset encounter start at AdvanceTime → None

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In the time-skip state machine (where `CurrentTimeSkipPhase` transitions from `AdvanceTime → None`): set `v2State.CurrentEncounterStartInteractionIndex = session.Interactions.Count`. This captures the start of the next encounter. |

#### Step 2.4 — Hook encounter detection → EncounterCompletion generation

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In the detection-success branch of `UpdateStateAndDetectEncounterAsync` (where `state.CurrentEncounterNumber` gets incremented and `state.LastEncounterEvidenceSpan` is set currently): (1) Compute `endIdx = session.Interactions.Count - 1` (2) Call a new method (or extend existing `GenerateTemplatesAsync`) that creates `EncounterCompletion` rows with `StartInteractionIndex = v2State.CurrentEncounterStartInteractionIndex`, `EndInteractionIndex = endIdx`, `EncounterNumber = v2State.CurrentEncounterNumber`, and `DetectionEvidence = detected.EvidenceSpan`. (3) Save records to DB and append to `v2State.EncounterSummaries`. This is a **new hook point** — distinct from the lifecycle-transition hook (Climax→Reset arc completions) and the BuildUp→Committed gate. |

#### Step 2.5 — IEncounterSummaryService changes for EncounterCompletion

| File | Change |
|---|---|
| `DreamGenClone.Application/RolePlay/IEncounterSummaryService.cs` | Add new method or update `GenerateTemplatesAsync` to accept optional `evidenceSpan`, `encounterNumber`, `startInteractionIndex`, `endInteractionIndex` parameters |
| `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` | When `encounterNumber > 0`, create `EncounterCompletion` rows instead of `PhaseMilestone`; populate `DetectionEvidence`, `EncounterNumber`, `StartInteractionIndex`, `EndInteractionIndex`. Template format: `"{CharacterName} — encounter {EncounterNumber} of arc {CycleIndex}. Scene: {SceneLocation}. Character present: {partnerList}. Evidence: {DetectionEvidence}"` — basic synchronous template; rich prose comes from async LLM enrichment (Phase 3). Character name resolved from `session.Characters` lookup (not raw ID). |

**Verify**: Build 0 errors. New `EncounterCompletion` rows appear in DB after encounter boundary detection.

---

### Phase 3: LLM Enrichment — Interaction-Range-Based Summary Generation

*Steps 9–10: depends on Phase 2*

#### Step 3.1 — EncounterSummaryJobHandler handles EncounterCompletion with interaction range

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Add `case "EncounterCompletion":` to the summary-type switch. **Key change**: instead of `TakeLast(30)` on all interactions, load the range: `session.Interactions .Skip(record.StartInteractionIndex) .Take(record.EndInteractionIndex - record.StartInteractionIndex + 1) .Where(x => !x.IsExcluded) .Select(...)` — this gives the complete interaction history of that specific encounter. Use a dedicated LLM prompt: *"Write 2-3 concise sentences from {CharacterName}'s perspective describing what happened during this encounter (encounter {N} of {totalInArc}). Include: who was present, where it happened, what physical acts occurred (flashing, hands, oral, intercourse), positions used, and orgasm details (especially male orgasm details). Focus on sensory and emotional impact so the character can recall this in their internal dialogue and actions. Base your summary on the following interactions:"* followed by the formatted interaction range. Change model resolution to use the new `RolePlaySummaryEnhancement` slot. |

#### Step 3.2 — Enqueue enrichment job for EncounterCompletion

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In the new detection-time hook (Step 2.4): after saving EncounterCompletion summaries, enqueue `EncounterSummaryEnhancement` background jobs for each one (same pattern as Climax→Reset arc completions, with dedup key `$"enc-summary:{session.Id}:{summary.Id}"`) |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Ensure `LoadEncounterSummariesForSessionAsync` (used by job handler) includes the new columns (`StartInteractionIndex`, `EndInteractionIndex`, `EncounterNumber`, `DetectionEvidence`) |

**Verify**: Build 0 errors. Enrichment jobs process and populate `LlmSummary` with prose derived from the actual encounter interactions.

---

### Phase 4: Prompt Injection — Session Memory Block Updates

*Steps 11–12: depends on Phase 2 (needs EncounterCompletion records in memory)*

#### Step 4.1 — Inject EncounterCompletion summaries into prompt block

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | In `InjectSessionMemoryBlock`: after rendering arc completions (prior arcs), insert encounter completions for the **current cycle** in reverse-chronological order (most recent first). Cap count via `session.MaxEncounterCompletionsToInject ?? _memoryOptions.Value.MaxEncounterCompletionsToInject`. Format: `"[Encounter {N}/{total} — {CharacterName}]\nActiveSummary"`. Ensure the numbering makes it clear whether this is the 1st, 2nd, or subsequent encounter in the current arc. |

**Example rendered block:**
```
Session Memory:
[Arc 1 Complete — Sophie]
Sophie's arc summary from LLM enrichment...

[Encounter 2/3 — Sophie]
Sophie and Marcus had their second encounter on the living room sofa. He entered her from behind while she gripped the armrest. Marcus came inside her with a shuddering groan, filling her deeply.

[Encounter 1/3 — Sophie]
Their first encounter in this arc — Sophie flashed Marcus in the kitchen, leading to oral on the countertop. He finished on her thighs.

[Approaching → Climax — Sophie]
Sophie — phase moved from Approaching to Climax...
```

#### Step 4.2 — Resolution order in InjectSessionMemoryBlock

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Refactor the cap-resolution section to be clean and consistent across all three summary types: `effectiveArcCompletions`, `effectiveEncounterCompletions`, `effectiveMilestones` — each following `session.X ?? _memoryOptions.Value.X` pattern. **Order in rendered block**: arc completions → encounter completions → phase milestones. |

**Verify**: Build 0 errors. Session Memory block includes encounter completions between arc completions and milestones with correct numbering.

---

### Phase 5: B-056 Aftermath Integration

*Steps 13–15: depends on Phase 2 (needs EncounterCompletion records existing at detection time)*

#### Step 5.1 — Aftermath injector reads EncounterCompletion summary

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` | Access `v2State.EncounterSummaries` (already in memory). Find the most recent `EncounterCompletion` for wife character where `CycleIndex == currentCycleIndex`. Use `ActiveSummary` (LlmSummary ?? TemplateSummary). Fall back to `DetectionEvidence` if `ActiveSummary` is empty/missing. If no EncounterCompletion record exists (shouldn't happen since Phase 2 writes it), omit the injector gracefully. |

#### Step 5.2 — Update aftermath directive template

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` | Update `BuildText`: replace `{EvidenceSpan}` with `{EncounterSummary}`. Frame the directive like: *"You just experienced: {EncounterSummary}. Now you must return to your husband. Your internal thoughts should contrast this encounter with your relationship with your husband."* (refine exact wording as appropriate during implementation) |

#### Step 5.3 — Remove LastEncounterEvidenceSpan from AdaptiveState

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Remove `LastEncounterEvidenceSpan` property |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Drop (or stop writing) `LastEncounterEvidenceSpan` column from `RolePlayV2AdaptiveStates` |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Remove from INSERT/UPDATE/SELECT SQL |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Remove all references to `state.LastEncounterEvidenceSpan` |

> **Note**: Step 5.3 depends on Steps 5.1–5.2 being complete so no code references the removed property.

**Verify**: Build 0 errors. Aftermath injector renders `ActiveSummary` text. New AdaptiveState rows have no `LastEncounterEvidenceSpan`.

---

### Phase 6: Knowledge Gating — Wife's Awareness of Other Man's Attributes

*Steps 16–19: depends on Phase 2 (needs EncounterCompletion records to exist for gating decisions)*

#### Context

Currently `IntimateBehavioralTextBuilder.cs` generates "she knows his body — [attributes]" text for both the husband AND other men from the very first prompt. There is **no progressive discovery**. The wife's knowledge of another man's intimate attributes (size, skill, stamina, performance) should be gated by whether an encounter has actually occurred.

The `EncounterCompletion` records created in Phase 2 provide exactly this signal: if an `EncounterCompletion` exists for a given male character in the current arc, the wife has first-hand knowledge of his intimate attributes. Otherwise, she only has superficial impressions.

#### Step 6.1 — Add pre-encounter partner perspective method

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/IntimateBehavioralTextBuilder.cs` | Add new method `BuildPartnerPreEncounterText()` — generates text like: *"{sheName} finds {heName} attractive and senses potential, but has not yet experienced him intimately — his intimate qualities remain unknown to her."* Similar structure to `BuildPartnerPerspectiveText` but omits specific attribute comparisons. Uses the same attribute data but frames it as unknown/anticipated rather than known. |

#### Step 6.2 — Gate partner perspective + comparison by EncounterCompletion

**Gate logic** (replace current unconditional injection):
- **Husband (persona)**: Always inject full `BuildPartnerPerspectiveText` (unchanged — wife knows her husband's body from years of marriage)
- **Other man**:
  - If `HasEncounterCompletion(charId, currentCycleIndex)` → inject full `BuildPartnerPerspectiveText` (she discovered his attributes through experience)
  - Else → inject `BuildPartnerPreEncounterText` (she's attracted but hasn't experienced him yet)
- **Comparison text** (`BuildComparisonText`): Only inject if there is at least one `EncounterCompletion` for the other man — wife can't meaningfully compare what she hasn't experienced

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | In `InjectCharacterBehavioralTexts()` (~line 2076): add helper method `HasEncounterCompletionForCharacter(string charId)` that checks `v2State.EncounterSummaries` for an `EncounterCompletion` record with matching `CharacterId` and `CycleIndex`. Use this to gate which version of partner perspective is injected. Gate `BuildComparisonText` behind the same check. |

#### Step 6.3 — Consistency: aftermath injector + knowledge gating

No code change needed — the flow is naturally consistent:
1. Pre-encounter prompt has `BuildPartnerPreEncounterText` (attraction without knowledge)
2. Encounter occurs → EncounterCompletion written → aftermath injector fires with `ActiveSummary` (the discovered knowledge)
3. Subsequent prompts have `BuildPartnerPerspectiveText` (full knowledge)

#### Step 6.4 — Tests for knowledge gating

| File | Change |
|---|---|
| `DreamGenClone.Tests/RolePlay/RolePlayContinuationServiceTests.cs` (or new `KnowledgeGatingTests.cs`) | Add tests: (1) Pre-encounter — no EncounterCompletion for other man → `BuildPartnerPreEncounterText` injected instead of full perspective. (2) Post-encounter — EncounterCompletion exists → full `BuildPartnerPerspectiveText` injected. (3) Comparison text only injected post-encounter. (4) Husband perspective always injected regardless. |

**Verify**: Tests pass. Knowledge gating works end-to-end in manual smoke test.

---

### Phase 7: B-041 Gap Fixes + Tests

#### Step 7.1 — EncounterSummaryJobHandler uses isolated model slot

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Replace `AppFunction.RolePlaySemanticAnalysis` resolution with `AppFunction.RolePlaySummaryEnhancement`. Verify the model slot is in the model manager UI if applicable. |

#### Step 7.2 — Update existing memory tests

| File | Change |
|---|---|
| `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs` | Update tests for new `GenerateTemplatesAsync` signature. Add tests for EncounterCompletion template generation (encounter number, detection evidence, start/end indices populated correctly). |
| `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs` | Add tests for EncounterCompletion injection: block includes encounter completions, encounter completions ordered after arc completions and before milestones, MaxEncounterCompletionsToInject enforced, per-session override works. |

#### Step 7.3 — Name resolution in template summaries

| File | Change |
|---|---|
| `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` | In `GenerateTemplatesAsync`: resolve `CharacterId` to display name using the session's character list or scenario definition before building template text. Use `session.Characters.FirstOrDefault(c => c.Id == charId)?.Name ?? charId` pattern. |

#### Step 7.4 — Include NPC characters in summary generation

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In the encounter-detection hook (Step 2.4): build `allowedCharacterIds` from all characters in `v2State.CharacterSnapshots` (not just scenario-defined characters). This ensures NPCs detected mid-session get memory entries. |

**Verify**: Build 0 errors. Template summaries show display names. NPCs appear in EncounterCompletion records.

---

### Phase 8: Verification

#### Step 8.1 — Build check

```
dotnet build DreamGenClone.Web
dotnet build DreamGenClone.Tests
dotnet build DreamGenClone.Infrastructure
dotnet build DreamGenClone.Domain
```

Confirm 0 errors, 0 warnings.

#### Step 8.2 — Existing test suites

```
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~AftermathHusbandContrast"
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~EncounterSummary"
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~SessionMemoryInjection"
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~IntimateBehavioralText"
```

Confirm all existing aftermath + memory + behavioral text tests pass without regression.

#### Step 8.3 — Knowledge gating tests

```
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~KnowledgeGating"
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~PartnerPreEncounter"
```

Confirm new knowledge gating tests pass: pre-encounter perspective, post-encounter perspective, comparison gating, husband always-known.

#### Step 8.4 — Full test suite

```
dotnet test DreamGenClone.Tests
```

Confirm all tests pass (606 pass, 15 pre-existing behavioral failures — same baseline as B-038 consolidation).

#### Step 8.5 — Manual smoke test (T053)

Complete the pending end-to-end manual smoke test:

1. Create session with multi-encounter theme + `[Aftermath:husband-contrast]` marker, plus a second male with distinct attributes (large endowment, high skill) vs husband (below-average)
2. **Pre-encounter**: Inspect continuation prompt — wife sees `BuildPartnerPreEncounterText` for other man (attraction, no intimate knowledge) and full `BuildPartnerPerspectiveText` for husband
3. Play through first encounter → hit encounter boundary → verify `EncounterCompletion` rows written to `RolePlayV2EncounterSummaries` with correct `EncounterNumber`, `StartInteractionIndex`, `EndInteractionIndex`, `DetectionEvidence`, `TemplateSummary`
4. Verify aftermath injector renders `ActiveSummary` (LlmSummary ?? TemplateSummary) — not raw evidence — in the directive
5. **Post-encounter**: Inspect continuation prompt — wife now sees full `BuildPartnerPerspectiveText` for other man + `BuildComparisonText`
6. Advance to 2nd encounter → verify Session Memory block shows `[Encounter 1/2 — CharName]` with rich summary
7. Verify `LastEncounterEvidenceSpan` is absent from new `RolePlayV2AdaptiveStates` rows
8. Verify full `CloseScene → AftermathCoupleInteraction → AdvanceTime` cycle renders correctly

#### Step 8.6 — Backlog update

- B-041: Note Phase 7 enhancements (model isolation, per-session arc cap, name resolution, NPC inclusion)
- B-056: Update state to `done done` (after T053 passes)
- B-058: New entry for this feature

---

## Relevant Files — Complete List

### Domain (3 files)

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` | Add `EncounterCompletion` enum value; add `EncounterNumber` int, `DetectionEvidence` string?, `StartInteractionIndex` int, `EndInteractionIndex` int |
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Remove `LastEncounterEvidenceSpan`; add `[JsonIgnore] CurrentEncounterStartInteractionIndex`, `[JsonIgnore] LastEncounterEndInteractionIndex` |
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` | Add `MaxArcCompletionsToInject` int?, `MaxEncounterCompletionsToInject` int? |

### Application (2 files)

| File | Change |
|---|---|
| `DreamGenClone.Application/RolePlay/IEncounterSummaryService.cs` | Update `GenerateTemplatesAsync` signature (add `evidenceSpan`, `encounterNumber`, `startInteractionIndex`, `endInteractionIndex`) |
| `DreamGenClone.Application/RolePlay/AppFunction.cs` | Add `RolePlaySummaryEnhancement` |

### Infrastructure (4 files)

| File | Change |
|---|---|
| `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs` | Add `MaxEncounterCompletionsToInject`, `SummaryEnhancementModelSlot` |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Schema: add columns to `RolePlayV2EncounterSummaries` + `Sessions`; drop/stop-writing `LastEncounterEvidenceSpan` from `RolePlayV2AdaptiveStates` |
| `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` | Handle `EncounterCompletion` type; accept encounter params; generate EncounterCompletion template prose; resolve CharacterId → display name |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Update all SQL for new/removed columns |

### Web (9 files)

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add encounter-detection hook for EncounterCompletion generation; capture interaction index at Climax entry and AdvanceTime→None; remove `LastEncounterEvidenceSpan` references; NPC inclusion in summary generation |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Update `InjectSessionMemoryBlock` to include EncounterCompletion summaries; resolve per-session caps. In `InjectCharacterBehavioralTexts()`: gate partner perspective by EncounterCompletion; gate comparison text |
| `DreamGenClone.Web/Application/RolePlay/IntimateBehavioralTextBuilder.cs` | Add `BuildPartnerPreEncounterText()` method for pre-encounter knowledge state |
| `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` | Read EncounterCompletion `ActiveSummary` instead of raw evidence; update directive wording |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Handle `EncounterCompletion` type with interaction-range prompt; use isolated model slot |
| `DreamGenClone.Web/Program.cs` | DI registration for new model slot |
| `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor` | Add `MaxArcCompletionsToInject` and `MaxEncounterCompletionsToInject` inputs |

### Tests (4 files)

| File | Change |
|---|---|
| `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs` | Update for new signature; add EncounterCompletion template tests; add name resolution tests |
| `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs` | Add EncounterCompletion injection tests |
| `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs` | Update injector tests for summary-based evidence |
| `DreamGenClone.Tests/RolePlay/RolePlayContinuationServiceTests.cs` | (or new `KnowledgeGatingTests.cs`) Add knowledge gating tests |

### Config

| File | Change |
|---|---|
| `appsettings.json` / `appsettings.Development.json` | Add `MaxEncounterCompletionsToInject`, `SummaryEnhancementModelSlot` to `"RolePlayMemory"` section |

---

## Scope Boundaries

### In Scope

- ✅ New `EncounterCompletion` SummaryType + `EncounterNumber` field
- ✅ `DetectionEvidence`, `StartInteractionIndex`, `EndInteractionIndex` fields on EncounterSummaryRecord
- ✅ Runtime encounter start/end tracking (interaction list indices via `[JsonIgnore]` runtime fields)
- ✅ Encounter detection hook → EncounterCompletion generation
- ✅ LLM enrichment uses actual interaction range (not `TakeLast(30)`)
- ✅ Dedicated enrichment prompt (who, where, acts, positions, orgasms, male orgasm details)
- ✅ EncounterCompletion injection in Session Memory block (numbered, e.g. `[Encounter 1/3 — Sophie]`)
- ✅ Per-session caps: `MaxArcCompletionsToInject`, `MaxEncounterCompletionsToInject`
- ✅ LLM enrichment model isolation (dedicated `RolePlaySummaryEnhancement` slot)
- ✅ B-056 bridge: aftermath injector reads EncounterCompletion `ActiveSummary`
- ✅ Delete `LastEncounterEvidenceSpan` from AdaptiveState
- ✅ **Knowledge gating**: Wife's awareness of other man's attributes gated by EncounterCompletion
- ✅ Pre-encounter vs post-encounter partner perspective in `IntimateBehavioralTextBuilder`
- ✅ Comparison text gated behind EncounterCompletion
- ✅ Name resolution (CharacterId → display name in template summaries)
- ✅ NPC characters included in summary generation
- ✅ Manual smoke test (T053)

### Deliberately Excluded (Deferred)

- ❌ `PositionIdsJson` / `FinishingMoveId` population (depends on B-029/B-036)
- ❌ `EncounterSummaryJobHandler` unit tests
- ❌ PhaseMilestone enrichment gap analysis
- ❌ Encounter-specific model manager UI page (add during B-039 style follow-up)

---

## Upstream Prerequisite: B-057 Part B

B-058 Phase 2 requires the following fields from B-057 Part B to already exist:

| B-058 Needs | Provided by B-057 | Status |
|---|---|---|
| Universal encounter counter to stamp `EncounterCompletion.EncounterNumber` | `AdaptiveScenarioState.GlobalEncounterCount` | B-057 Part B must add |
| Active encounter number (any phase, not just Climax) | `AdaptiveScenarioState.CurrentEncounterNumber` repurposed as universal | B-057 Part B must repurpose |
| Per-interaction encounter number for diagnostics | `RolePlayInteraction.EncounterNumberAtCreation` | B-057 Part B must add |

B-058 adds the following fields that B-057 does NOT add:
- `AdaptiveScenarioState.CurrentEncounterStartInteractionIndex` (runtime-only, `[JsonIgnore]`)
- `AdaptiveScenarioState.LastEncounterEndInteractionIndex` (runtime-only, `[JsonIgnore]`)
- `EncounterSummaryRecord.StartInteractionIndex` (persisted)
- `EncounterSummaryRecord.EndInteractionIndex` (persisted)

**No field conflicts exist.** B-057's fields are a strict prerequisite — B-058 does not rename or repurpose any B-057 field.

## Dependency Graph

```
B-057 Part B (universal tracking fields)
  └─► B-058 Phase 1 (Data Model)
        └─► Phase 2 (Interaction-Range Tracking)
              ├─► Phase 3 (LLM Enrichment)
              ├─► Phase 4 (Prompt Injection)
              ├─► Phase 5 (B-056 Bridge)
              └─► Phase 6 (Knowledge Gating)
                    └─► Phase 7 (Gap Fixes + Tests)
                          └─► Phase 8 (Verification)
```

B-057 Part A (sync persist) can ship independently — no B-058 dependency.
Phases 3, 4, 5, 6 can proceed in parallel once Phase 2 is complete. Phase 7 (gap fixes) can start after Phases 3–6 are stable.
