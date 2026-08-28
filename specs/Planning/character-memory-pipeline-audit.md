# Character Memory Pipeline Audit

**Date:** 2026-07-27
**Scope:** Documentation of how the character memory pipeline works.

## 1. Pipeline Map

### Triggers (3 call sites)

| Trigger | Call Site | Summary Type | When |
|---------|-----------|-------------|------|
| **Phase transition** | `RolePlayEngineService.cs:4075` | `PhaseMilestone` or `ArcCompletion` | Every phase transition (BuildUp→Committed, Committed→Climax, Climax→Reset, etc.) |
| **Theme commit transition** | `RolePlayEngineService.cs:3869` | `PhaseMilestone` or `ArcCompletion` | When a theme is committed (selection finalized) |
| **Encounter boundary** | `RolePlayEngineService.cs:5251` | `EncounterCompletion` | Semantic LLM detects encounter-completed event (any phase, any theme with mapping) |

### Generation (2-phase)

**Phase 1 — Synchronous Template** (`EncounterSummaryService`):
- `GenerateTemplatesAsync`: Creates one `EncounterSummaryRecord` per allowed character. `TemplateSummary` is a simple one-liner like `"charlie — phase moved from BuildUp to Committed. Scene: bedroom. Arc 1, turn 3 in phase."` for `PhaseMilestone`. `ArcCompletion` is slightly richer: `"charlie completed arc 1. Peak phase: Climax. Beat reached: C1. Theme: t1."`
- `GenerateEncounterCompletionTemplatesAsync`: Template includes truncated encounter interactions text (max 800 chars) or detection evidence snippet (240 chars). Per-character interaction texts are provided.
- Summary type: `ArcCompletion` when `ToPhase == Reset`, otherwise `PhaseMilestone`.

**Phase 2 — Async LLM Enrichment** (`EncounterSummaryJobHandler`):
- Each saved record enqueues a background job with dedupe key `enc-summary:{sessionId}:{summaryId}`.
- Three enrichment prompt templates:
  - `BuildMilestonePrompt`: Recent 30 interactions + phase transition info. Requests 1-2 sentences describing what the character did or experienced during the phase.
  - `BuildArcCompletionPrompt`: Phase milestone summaries for the arc. Requests 3-4 sentences from the character's perspective summarizing the complete arc.
  - `BuildEncounterCompletionPrompt`: All interactions in encounter range (omniscient) + character-specific interactions + previous encounter summaries. Requests a 3-5 sentence first-person memory capturing 6 dimensions: what happened, what they felt, what they learned, what changed, what risk was taken, and how it compared to previous encounters.
- Uses dedicated model slot (`AppFunction.RolePlaySummaryEnhancement`) for ArcCompletion + EncounterCompletion.
- Has retry logic (2 attempts, 5s delay).

### Storage

- Table: `RolePlayV2EncounterSummaries` (via `IRolePlayStateRepository.SaveEncounterSummaryAsync`)
- Key fields: `Id`, `SessionId`, `CharacterId`, `SummaryType`, `CycleIndex`, `FromPhase`, `ToPhase`, `EncounterNumber`, `StartInteractionIndex`, `EndInteractionIndex`, `TemplateSummary`, `LlmSummary`, `LlmEnhancedUtc`, `DetectionEvidence`, `SceneLocation`, `ActiveThemeId`
- Also added to in-memory `v2State.EncounterSummaries` list on the `AdaptiveScenarioState` object.

### Prompt Injection (`SessionMemorySlot`)

Three tiers based on age relative to `SessionMemoryLongTermTurnThreshold`:
- **Tier 1 — Character Memories** (long-term): Encounter summaries from before threshold. Shows `[CharacterId]: summary`.
- **Tier 2 — Recent Encounter Memories** (medium-term): EncounterCompletion summaries within threshold. Shows `Encounter N (CharacterId): summary`.
- **Tier 3 — Recent Milestones** (short-term): Last 3 PhaseMilestone/ArcCompletion summaries. Shows `[FromPhase→ToPhase] CharacterId: summary`.

Key filtering:
- `LlmSummary` preferred; falls back to `TemplateSummary` if not yet enhanced
- Character-filtered: only shows actor's own memories unless Narrative variant (shows all)
- Milestones capped at 3 most recent
- Fails fast if `SessionMemoryLongTermTurnThreshold` is missing (FR-012a)

## 2. InteractionHistory vs SessionMemory

`InteractionHistorySlot` and `SessionMemorySlot` serve different purposes in the same prompt:

- **InteractionHistorySlot**: Raw full-detail text of the last N interactions. The model sees exactly what was said and done.
- **SessionMemorySlot**: LLM-summarized memories organized into 3 tiers (long-term character memories, recent encounter memories, recent milestones). These provide higher-level narrative continuity — what encounters meant, how the character felt, what changed.

Both may reference the same events at different granularities. InteractionHistory provides the immediate conversational context; SessionMemory provides the narrative arc and character self-knowledge.

## 3. Enrichment Prompt Templates

### BuildMilestonePrompt
- **Context**: Recent 30 interactions + phase transition info
- **Output**: 1-2 sentences about what the character did during the exiting phase

### BuildArcCompletionPrompt
- **Context**: Phase milestone summaries from the arc, or recent 30 interactions if no milestones exist
- **Output**: 3-4 sentence arc summary from the character's perspective

### BuildEncounterCompletionPrompt
- **Context**: All interactions in encounter range (omniscient) + character-specific responses + previous encounter summaries for comparison
- **Output**: 3-5 sentence first-person memory covering what happened, felt, learned, changed, risk taken, and comparison to prior encounters

## 4. Trigger Coverage

### All Trigger Sites

| # | Site | Trigger | Summary Type | Character Filter |
|---|------|---------|-------------|------------------|
| 1 | `:4075` | Phase transition | PhaseMilestone / ArcCompletion | `GetActiveCharacterNames()` |
| 2 | `:3869` | Theme commit transition | PhaseMilestone / ArcCompletion | `GetActiveCharacterNames()` |
| 3 | `:5251` | Encounter boundary detected | EncounterCompletion | CharacterSnapshots + persona + scenario chars |

Sites 1 and 2 may both fire during a theme-commit-driven phase transition, using different transition events. The background job dedup key prevents double-enrichment.

EncounterCompletion (site 3) fires on every encounter boundary detection, in any phase. `WasEncounterStart` is tracked on interactions for the encounter-start marker but no memory is generated for beginnings — only completions.

## 5. Memory Content

### TemplateSummary vs LlmSummary
- `SessionMemorySlot` prefers `LlmSummary` when non-null, falls back to `TemplateSummary`
- `TemplateSummary` is written synchronously at the trigger moment. `LlmSummary` is populated asynchronously by the background job.
- At prompt-build time, recent summaries may not yet have `LlmSummary` — the `TemplateSummary` fallback ensures the slot always has content.

### SessionMemorySlot Filtering
- **Threshold-based**: `SessionMemoryLongTermTurnThreshold` splits long-term from medium-term
- **Character-filtered**: Non-Narrative variant shows only the actor's own memories. Narrative shows all.
- **Type-filtered**: Milestones capped at 3 most recent. All ArcCompletion + EncounterCompletion included.
- **Source-filtered**: Only records with non-null `LlmSummary` are considered (but uses `TemplateSummary` text as fallback when `LlmSummary` is null).

### Character-Specific Filtering
- `SessionMemorySlot` filters by `CharacterId` matching `ActorName` (case-insensitive) for non-Narrative.
- `EncounterSummaryService` uses `allowedCharacterIds` to filter which characters get memory records.
- `GenerateEncounterCompletionTemplatesAsync` builds per-character interaction texts so each character's memory reflects their own perspective.
