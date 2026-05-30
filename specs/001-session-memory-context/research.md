# Research: B-041 — Session Memory Context (Intimate Encounter History Injection)

*Phase 0 output — all NEEDS CLARIFICATION items resolved*

---

## R1: Phase Transition Hook Location

**Decision**: Hook after `await _stateRepository.SaveTransitionEventAsync(...)` in `RolePlayEngineService.cs` (~L2892). Write template summaries synchronously (per character, no await on generation) then enqueue the LLM job asynchronously.

**Evidence**: The phase transition block in `RolePlayEngineService` already calls `SaveTransitionEventAsync` as the final persisted action for each transition. This is the correct hook point because:
- `v2State` (full `AdaptiveScenarioState`) is available: character snapshots, theme scores, cycle index, beat cursor, scene location, stats
- `session` (full `RolePlaySession`) is available: session ID, character list
- `lifecycle.TransitionEvent` is available: `FromPhase`, `ToPhase`, `TriggerType`, `OccurredUtc`, `ReasonCode`
- `InteractionCountInPhase` is tracked on `v2State` and is valid at transition time

**Available data at hook point per transition type**:

| Transition | Key data for template |
|---|---|
| Any | SessionId, CharacterSnapshots (Desire/Restraint/Tension/Connection), CycleIndex, OccurredUtc, InteractionCountInPhase, SceneLocation, PrimaryThemeId |
| BuildUp entry | ThemeSelectionRule, initial character stats as arc baseline |
| Approaching/Commitment | Avg stat values — escalation confirmed by stat levels |
| Climax | CurrentBeatCode (beat reached), peak stats |
| Climax→Reset | Full arc data — triggers ArcCompletion LLM job in addition to template |

**Rationale**: This is the only hook point that has all required structured data simultaneously. Hooking earlier (e.g., at `transition.Transitioned` check) would miss the persisted transition record; hooking later would lose in-memory state.

**Alternatives considered**:
- Separate scheduled job that polls for unprocessed transitions: adds latency, loses in-memory state, requires more infrastructure
- Hook in `ScenarioLifecycleService`: the lifecycle service doesn't have session or interaction data; it only manages the state machine

---

## R2: Async Job Handler Pattern Reference

**Decision**: Follow the `SemanticInteractionAnalysisJobHandler` pattern exactly.

**Reference implementation**:
- `SemanticInteractionAnalysisJobHandler` implements `IBackgroundJobHandler` (defined in `DreamGenClone.Web/Application/BackgroundJobs/IBackgroundJobHandler.cs`)
- `IBackgroundJobHandler` has properties `JobType` (string) and method `HandleAsync(string payloadJson, CancellationToken)`
- Registered in `Program.cs` as: `builder.Services.AddScoped<IBackgroundJobHandler, SemanticInteractionAnalysisJobHandler>()`
- Enqueued from `RolePlayEngineService.cs` (~L1550) as: `_backgroundJobQueue!.Enqueue(BackgroundJobTypes.SemanticInteractionAnalysis, payloadJson, dedupeKey: ...)`
- Payload: minimal (`SessionId`, `InteractionId`, `CharacterId`) — handler loads full state from DB

**For `EncounterSummaryJobHandler`**:
- New constant: `BackgroundJobTypes.EncounterSummaryEnhancement = "encounter-summary-enhancement"`
- Payload: `{ SessionId, CycleIndex }` — one job per arc transition, not per character
- Deduplication key: `$"enc-summary:{sessionId}:{cycleIndex}"`
- Handler loads: all interactions for the arc (by `SessionId` + `CycleIndex`), session character list, ArcCompletion summary rows for all characters
- Generates prose for all characters in a single LLM call, writes one `LlmSummary` per character row
- On LLM failure: retries once after ~5 s; if second attempt also fails, logs `Warning` and abandons (template remains as fallback)
- On success: writes `LlmSummary` + `LlmEnhancedUtc` to each character's row

---

## R3: Prompt Injection Pattern Reference

**Decision**: Follow the theme evidence list injection pattern in `RolePlayContinuationService.BuildPromptAsync`.

**Existing block patterns** (all use `StringBuilder` in `BuildPromptAsync`):
- Recent Interaction History: `sb.AppendLine("Recent Interaction History:");` + per-interaction lines
- Adaptive Character Stats: `sb.AppendLine("Character Stats:");` + per-character stat lines
- Active Theme Tracker: labeled section with sub-items
- Location Perceptions: per-perception lines with confidence and LOS

**"Session Memory" block structure**:
```
Session Memory:
[Arc 1 Complete — Sophie]
Sophie remembers Marcus pulling her close for a long kiss before his hands moved over her body...

[Arc 1 Complete — Marcus]
Marcus remembers the tension breaking when Sophie kissed back without hesitation...

[BuildUp → Approaching — Sophie]
Sophie's hesitation was fading. Desire at 67, Restraint dropping.

[BuildUp → Approaching — Marcus]
Marcus was pressing harder; Tension at 74, Connection rising.
```

**Block position**: After Recent Interaction History, before Scene Continuity Anchor. This is the optimal position because:
- The AI has just processed the current-arc window history
- The memory block grounds it in what happened in prior arcs before it reads spatial constraints
- Placing it inside the scenario block would conflate long-term memory with scenario premise

**Injection logic** (in `BuildPromptAsync`):
```
if (v2State.EncounterSummaries.Any())
    InjectSessionMemoryBlock(sb, v2State.EncounterSummaries, effectiveMaxMilestones, effectiveMaxArcCompletions)
```

Where:
- `effectiveMaxMilestones = session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject`
- `effectiveMaxArcCompletions = _memoryOptions.Value.MaxArcCompletionsToInject`

**Filtering logic**:
- Include at most `MaxArcCompletionsToInject` rows where `SummaryType == ArcCompletion`, ordered by `OccurredUtc DESC`, take M, then reverse to chronological
- Include at most N rows where `SummaryType == PhaseMilestone AND CycleIndex == v2State.CycleIndex`, ordered by `OccurredUtc DESC`, take N, then reverse to chronological
- Render arc completions first (grouped by CycleIndex ascending), then current-arc milestones

---

## R4: Template Summary Generation Content

**Decision**: Template generator uses structured data only (no LLM call). Output is intentionally brief and fact-based; the LLM prose is the rich version.

**Template format per SummaryType**:

**PhaseMilestone** (any non-Reset transition):
```
{CharacterName} — phase moved from {FromPhase} to {ToPhase}. Stats: Desire {d}, Restraint {r}, Tension {t}, Connection {c}. Scene: {location}. Arc {cycleIndex+1}, interaction {interactionCountInPhase} in phase.
```

Example: `Sophie — phase moved from BuildUp to Approaching. Stats: Desire 65, Restraint 42, Tension 71, Connection 58. Scene: Living Room. Arc 2, interaction 8 in phase.`

**ArcCompletion** (Climax→Reset template, before LLM job):
```
{CharacterName} completed arc {cycleIndex+1}. Peak phase: Climax. Beat reached: {beatCode}. Final stats: Desire {d}, Restraint {r}, Tension {t}, Connection {c}. Theme: {themeName}. Finishing move: {finishingMoveId ?? "unknown"}.
```

This is a placeholder until the LLM job enriches it with prose. If the job never runs (disabled or failed), this template text is what gets injected.

---

## R5: Arc Interaction Loading for LLM Job

**Decision**: The LLM job loads all interactions for the completed arc via a new repository method `LoadArcInteractionsAsync(sessionId, cycleIndex)`.

**Evidence**: `RolePlayInteractions` table (already exists) stores all interactions for a session. The `CycleIndex` field on `RolePlayV2AdaptiveStates` tracks which arc the session is in. At Climax→Reset, `v2State.CycleIndex` is the arc that just completed.

**What the LLM arc completion prompt receives**:
- All interaction text for the arc (AI response text only — the actual narrative prose)
- Character name and role for the target character
- Instruction to write 2–3 sentences from that character's first-person perspective describing the physical intimacy that occurred: initial contact, escalation acts (oral sex if present), intercourse (positions if mentioned), and how the encounter ended

**LLM prompt template** (used by `EncounterSummaryJobHandler`):
```
You are summarizing a roleplay session from one character's perspective.

Character: {characterName} ({characterRole})
Arc interactions (in order):
{interactions}

Write 2-3 sentences from {characterName}'s perspective describing the intimate physical acts that occurred in this arc: what physical contact happened (kissing, touching), any oral sex, intercourse (note positions if mentioned), and how the encounter ended. Be specific. Write in third person past tense.
```

**Token budget consideration**: If the arc has many interactions, truncate to the last 30 (most recent / most likely to contain climax acts). The full arc context is already available in Recent Interaction History for the current session; this summary is for future arcs.

---

## R6: Global Settings Pattern Reference

**Decision**: Follow `RolePlayFeatureFlagsOptions.cs` pattern for the new `RolePlayMemoryOptions` class.

**Reference**:
- `DreamGenClone.Infrastructure/Configuration/RolePlayFeatureFlagsOptions.cs` — `static class` with const section name + properties
- Bound in `Program.cs` via `builder.Services.Configure<RolePlayFeatureFlagsOptions>(builder.Configuration.GetSection(RolePlayFeatureFlagsOptions.SectionName))`
- Injected via `IOptions<RolePlayFeatureFlagsOptions>` in consuming services

**New `RolePlayMemoryOptions`**:
```csharp
public sealed class RolePlayMemoryOptions
{
    public const string SectionName = "RolePlayMemory";
    public int MaxMilestonesToInject { get; init; } = 5;
    public int MaxArcCompletionsToInject { get; init; } = 10;
    public bool EnableLlmSummaryEnhancement { get; init; } = true;
}
```

**`appsettings.Development.json` addition**:
```json
"RolePlayMemory": {
  "MaxMilestonesToInject": 5,
  "MaxArcCompletionsToInject": 10,
  "EnableLlmSummaryEnhancement": true
}
```

**Per-session override**: `RolePlaySession.MaxMilestonesToInject` (int?, null = use global). Resolved at prompt build time: `session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject`.

---

## R7: SQLite Migration Pattern

**Decision**: Add `RolePlayV2EncounterSummaries` table in the `EnsureAdaptiveStateSchemaAsync` block in `SqlitePersistence.cs`, following the `CREATE TABLE IF NOT EXISTS` guard pattern already used for `RolePlayV2ScenarioHistory` (~L952) and `RolePlayV2SemanticEvents` (~L975).

**Sessions column migration**: `ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL` — guarded with `PRAGMA table_info(Sessions)` check, same pattern as `HusbandAwarenessProfileId` migration.

**Rationale**: All V2 adaptive state tables are created in the same startup block. No separate migration runner exists in this codebase — it uses `IF NOT EXISTS` guards and `PRAGMA table_info` checks for additive changes.

**Load site**: New method `RolePlayStateRepository.LoadEncounterSummariesAsync(sessionId, maxMilestones, currentCycleIndex)` queries:
- All rows WHERE `SessionId = ? AND SummaryType = 'ArcCompletion'` (no limit)
- Last N rows WHERE `SessionId = ? AND SummaryType = 'PhaseMilestone' AND CycleIndex = ?` ORDER BY OccurredUtc DESC LIMIT N
- Combined into a single `List<EncounterSummaryRecord>` in chronological order

Called from `RolePlayStateRepository.LoadStateAsync` alongside the existing history/telemetry loads.
