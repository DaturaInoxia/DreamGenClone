# RP Engine: Context-Aware Actor Selection — Design Specification

**Date:** 2026-07-14
**Status:** Designed — LLM-first with location fix + open-world availability (reviewed)
**Review Date:** 2026-07-14
**Review Result:** Passed with fixes applied (see §11 Architectural Review)
**Related:** `rp-turn-interaction-cohesion-design.md` (turn prompt structure), `RolePlayEngineService.cs` `ResolveSceneContinueActorsAsync()`

---

## TL;DR

Fix the disabled location services first with LLM-based detection, then build on it: **location-gated character availability** enables open-world scenarios where moving to a new location introduces new characters (Beach → Lifeguard, Golf Course → Golf Buddy). The LLM then selects who speaks from the available character pool using narrative context + location affinities + time-of-day + scoring hints. Dedicated LLM slots for both location detection (`AppFunction.RolePlayLocationDetection`) and actor selection (`AppFunction.RolePlayActorSelection`).

---

## Key Decisions

- **Location fix first** — replace regex-based `DetectSceneLocationSignalAsync` with LLM-based detection, running **as a background job** in the adaptive pipeline after each interaction (fire-and-forget via `SemanticBackgroundJobQueue` pattern; location data is eventually consistent with one-turn lag)
- **Location as character gate** — new `ResolveAvailableCharacters()` uses current location + per-character affinities + time-of-day to determine which characters are narratively present
- **Scoring as base, LLM as enhancement** — simplified scoring always runs (base path); LLM reorders candidates when available and context has changed. No model configured → scoring-only (not a fallback — it's the base path). LLM call fails → scoring order preserved with `Source=Fallback`
- **Scenario-level location affinities** — per-character `CharacterLocationAffinity` (Required/Preferred/Excluded + TimeOfDay?) on scenario Characters
- **Time-of-day tracking** — `CurrentTimeOfDay` auto-detected from narrative; manual override in workspace
- **Open-world design** — location transitions can introduce/exclude characters, creating the feeling of a populated world

---

## 1. Problem Statement

The overflow continue ("...") button auto-selects characters using a simple recency + location sort:

```
OrderByDescending(InScene)
→ ThenBy(LastSeenIndex < 0 ? int.MinValue : LastSeenIndex)
→ ThenBy(ScenarioOrder)
```

This ignores the rich context already tracked on the session:
- Narrative phase (Opening → BuildUp → Committed → Approaching → Climax → Reset)
- Per-character stats (Desire, Restraint, Dominance)
- Character roles
- Active themes and their scores
- Semantic events (what just happened thematically)
- Encounter participation (who is actively in a sexual encounter)
- Character-location affinities (not yet modeled)

Additionally, location services are **disabled in production** (`EnableLocationServices: false`) because the regex-based detection is unreliable — it only detects explicitly named locations, can't handle multi-location scenes, and silently falls back to stale previous locations.

The result: every overflow click produces the same character rotation regardless of what's happening in the story. Characters with no narrative reason to speak get equal priority, and the most dramatically relevant character may be buried at position N. Characters are never gated by location — the Lifeguard can appear in the Living Room, the Neighbor at the Beach.

---

## 2. Architecture

```
┌── ADAPTIVE PIPELINE (after each interaction) ──────────────────┐
│                                                                  │
│  1. DetectLocationAsync()  ← LLM-based (NEW, background job)   │
│     Enqueued via SemanticBackgroundJobQueue (fire-and-forget)   │
│     Input: last 3 interactions + scenario location names        │
│     Output: current location + per-character location status    │
│     → Updates: CurrentSceneLocation, CharacterLocations         │
│     ⚠ Eventually consistent — one-turn lag (next turn reads     │
│       the updated value; current turn uses previous detection)  │
│                                                                  │
│  2. DetectTimeOfDayAsync()  ← keyword matching (NEW)            │
│     Input: last 3 interactions                                  │
│     Output: Morning/Afternoon/Evening/Night or null             │
│     → Updates: CurrentTimeOfDay (if not manually overridden)    │
│                                                                  │
│  3. ResolveAvailableCharacters()  ← NEW: location → gate       │
│     For each scenario character:                                │
│     ├── At current location? (from CharacterLocations)          │
│     ├── Affinity: Required at location → MUST include           │
│     ├── Affinity: Excluded at location → MUST exclude           │
│     ├── Affinity: Time-of-Day match?                            │
│     └── Hard filters: ParticipateInAutoContinue=false           │
│     → Returns: available character set                          │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
           │
           ▼
┌── OVERFLOW CONTINUE (on click) ─────────────────────────────────┐
│                                                                  │
│  4. Context check: (phase, location, char-set, time) changed?   │
│     ├── NO → Reuse cached ordering, rotate by recency           │
│     └── YES → Continue                                           │
│                                                                  │
│  5. Build LLM prompt:                                           │
│     ├── Narrative summary (last 3 interactions)                  │
│     ├── Current: phase, location, time-of-day                    │
│     ├── Available characters with:                              │
│     │   ├── Name, role, key stats                               │
│     │   ├── Location status + affinity match status             │
│     │   ├── Time-of-day relevance                               │
│     │   ├── Recency + base score (hint)                         │
│     ├── Active themes + recent semantic events                  │
│     └── "Select {batchSize} characters to speak, ordered"       │
│                                                                  │
│  6. Call LLM (AppFunction.RolePlayActorSelection) [enhancement] │
│     → Parse JSON → validate names → cache ordering              │
│     → On failure/timeout/no model: use scoring order (base)     │
│       with Source=Fallback (not a silent fallback — explicit)   │
│                                                                  │
│  7. Apply persona rules → select top N → log                    │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### How This Enables Open-World

```
Scenario: "Married Life"

Home (Afternoon)           Beach (Afternoon)         Home (Evening)
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│ Becky (Wife)    │       │ Becky (Wife)    │       │ Becky (Wife)    │
│ Persona (You)   │  →    │ Persona (You)   │  →    │ Persona (You)   │
│ Tom (Neighbor)  │       │ Marco (Lifeguard)│       │ Tom (Neighbor)  │
└─────────────────┘       └─────────────────┘       └─────────────────┘
  Neighbor available         Neighbor excluded         Neighbor reappears
                             Lifeguard introduced      (time=Evening ✓)
```

---

## 3. Phases

### Phase 1: LLM-Based Location Detection (Fix Disabled Services)
*No dependencies — foundation for everything else*

**Step 1.1:** Add `AppFunction.RolePlayLocationDetection` enum
- File: `DreamGenClone.Domain/ModelManager/AppFunction.cs`
- New dedicated slot for location detection model
- User configures a cheap/fast model in Model Manager

**Step 1.2:** Create location detection request/response models
- New file: `DreamGenClone.Web/Application/RolePlay/Models/LocationDetectionModels.cs`
- `LocationDetectionRequest`: SessionId, RecentInteractions (last 3, summarized), ScenarioLocationNames (list), CurrentLocation (previous), CharacterNames (list)
- `LocationDetectionResult`: DetectedLocation (string), LocationConfidence (0-1), PerCharacterLocations (dict: name → location), LocationChanged (bool), Reasoning (string?)

**Step 1.3:** Create `ILocationDetectionService` interface + implementation
- New file: `DreamGenClone.Web/Application/RolePlay/LocationDetectionService.cs`
- Method: `Task<LocationDetectionResult> DetectAsync(LocationDetectionRequest, CancellationToken)`
- Resolves model via `_modelResolutionService.ResolveAsync(AppFunction.RolePlayLocationDetection)`
- Builds prompt with known locations, previous location, recent interactions, character names
- Calls LLM with 3-second timeout; parses JSON response
- **No-fallback compliance:** On LLM failure or no model configured, returns `Success=false` with error message (same pattern as `SemanticEventInferenceService`). `CurrentSceneLocation` is left unchanged (previous value preserved). **Do NOT fall back to regex.** The regex code (`MatchScenarioLocation`, `MatchGenericLocation`, `ContainsWholeWord`, `GenericLocationNames`) is **removed entirely** — there is no alternate detection path.

**Step 1.4:** Replace `DetectSceneLocationSignalAsync` with background-enqueued location detection
- File: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- **Critical:** The current `DetectSceneLocationSignalAsync` call at line ~3891 is **synchronous** — it blocks the user's response generation. An LLM call here would add 2-5 seconds per interaction.
- **Fix:** Move location detection to a **background job** via `SemanticBackgroundJobQueue` (same pattern as semantic analysis):
  1. After each interaction, enqueue a `LocationDetectionJob` with dedupe key `session:{sessionId}`
  2. The background worker calls `ILocationDetectionService.DetectAsync()`, updates `CurrentSceneLocation` + `CharacterLocations` in the DB
  3. The next turn reads the updated value from the V2 state snapshot
  4. **Tradeoff:** Location data is eventually consistent — one-turn lag. The current turn's actor selection uses the previous turn's detected location. This is acceptable because location changes are narrative transitions, not split-second events.
- Remove: `MatchScenarioLocation`, `MatchGenericLocation`, `ContainsWholeWord`, `GenericLocationNames` (no regex fallback path)
- Keep: `EnsureCharacterLocationRows`, `UpsertTrueLocation`, `UpdatePerceivedLocationsFromTruth` (perception model still valid)
- The call site at line ~3891 changes from `await DetectSceneLocationSignalAsync(...)` to `EnqueueLocationDetectionJob(session, ...)` (fire-and-forget)

**Step 1.5:** Enable location services in config
- File: `appsettings.json` and `appsettings.Development.json`
- Change `"EnableLocationServices": false` → `"EnableLocationServices": true`
- `EnableLocationServices` is an **infrastructure toggle** (like `EnableSemanticInference`), not an RP behavior config — the no-fallback rules do not apply to the toggle itself
- If no model configured for `RolePlayLocationDetection`, the background job logs a warning and skips detection — `CurrentSceneLocation` remains null/previous. No silent fallback.

**Step 1.6:** Register in DI
- File: `DreamGenClone.Web/Program.cs`
- `builder.Services.AddScoped<ILocationDetectionService, LocationDetectionService>();`
- Inject into `RolePlayEngineService`

### Phase 2: Location Affinity & Time-of-Day Data Model
*Can run parallel with Phase 1*

**Step 2.1:** Create `TimeOfDay` enum
- New file: `DreamGenClone.Domain/RolePlay/TimeOfDay.cs`
- Values: `Morning`, `Afternoon`, `Evening`, `Night`

**Step 2.2:** Create `CharacterLocationAffinity` model
- New file: `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs`
- Fields: `LocationName` (string — matches `Location.Name` which is what `CurrentSceneLocation` stores), `AffinityType` (enum), `TimeOfDay` (TimeOfDay?, null=any)
- **Design note:** `CurrentSceneLocation` stores `Location.Name` (the display name), not `Location.Id`. Using `LocationName` on the affinity model allows direct string comparison in `ResolveAvailableCharacters` without a lookup step. If location names are edited in a scenario, affinities keyed by name will need re-mapping — this is an acceptable tradeoff for V1 simplicity.
- New enum: `AffinityType { Required, Preferred, Excluded }`

**Step 2.3:** Add to scenario Character
- File: `DreamGenClone.Web/Domain/Scenarios/Character.cs`
- Property: `public List<CharacterLocationAffinity> LocationAffinities { get; set; } = [];`

**Step 2.4:** Add `DefaultTimeOfDay` to Scenario
- File: `DreamGenClone.Web/Domain/Scenarios/Scenario.cs`
- Property: `public TimeOfDay DefaultTimeOfDay { get; set; } = TimeOfDay.Afternoon;`

**Step 2.5:** Add time-of-day tracking to AdaptiveScenarioState
- File: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
- `public TimeOfDay? CurrentTimeOfDay { get; set; }`
- `public bool TimeOfDayManuallySet { get; set; }`
- **Wiring in `CreateSessionAsync`** (line ~424, alongside other `scenario.Default*` copies): add `session.AdaptiveState.CurrentTimeOfDay = scenario.DefaultTimeOfDay;`
- Also wire in `SeedFromScenarioAsync` if it handles adaptive state seeding separately
- DB migration: `ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentTimeOfDay TEXT NULL` (idempotent via `HasColumnAsync` guard — see `EnsureAdaptiveStateSchemaAsync` in `RolePlayStateRepository.cs`)
- **Also wire `DefaultStartingLocationId`:** `Scenario.DefaultStartingLocationId` exists but is currently **unused** in `CreateSessionAsync`. Add: if `scenario.DefaultStartingLocationId` is set, resolve the `Location.Name` from `scenario.Locations` and set `session.AdaptiveState.CurrentSceneLocation` as the initial seed value. This gives the first location detection a starting point.

**Step 2.6:** Add `ActorName` to `SemanticEventRecord`
- File: `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`
- Property: `public string? ActorName { get; set; }`
- Migration: `ALTER TABLE RolePlayV2SemanticEvents ADD COLUMN ActorName TEXT` (idempotent via `HasColumnAsync`)
- Populate at creation in `SemanticEventInferenceService` — set `ActorName` from the `SemanticEventInferenceRequest.ActorName` field (already passed by callers)
- **Backfill existing records:** After migration, run a one-time backfill: for each `SemanticEventRecord` with null `ActorName`, resolve `InteractionId` → `RolePlayInteractions.ActorName` via SQL JOIN and update. This can be a SQL `UPDATE ... SET ActorName = (SELECT ...)` or a C# migration script run once at startup.

### Phase 3: Time-of-Day Detection
*Depends on Phase 2*

**Step 3.1:** Implement `DetectTimeOfDayAsync`
- File: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- Keyword/regex matching in last 3 interactions (word-boundary):
  - Morning: "morning", "dawn", "sunrise", "breakfast", "woke up", "wake up"
  - Afternoon: "afternoon", "lunch", "midday", "noon"
  - Evening: "evening", "dusk", "sunset", "dinner", "nightfall"
  - Night: "night", "midnight", "moonlight", "dark outside", "late hour"
- Returns `TimeOfDay?` (null if no signal)

**Step 3.2:** Wire into adaptive pipeline
- Called after location detection
- Only updates if `TimeOfDayManuallySet == false`
- Manual override suppresses; "Auto" resumes

### Phase 4: Character Availability Resolver (Location → Gate)
*Depends on Phases 1–3*

**Step 4.1:** Implement `ResolveAvailableCharacters`
- File: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- **Insertion point:** This method runs **inside** `ResolveSceneContinueActorsAsync`, **after** the B-056 `AftermathCoupleInteraction` early-return guard (line ~2315). The B-056 branch returns a hardcoded `[wife, husband]` list and bypasses all candidate selection — this is preserved unchanged. The new pipeline only runs when the session is NOT in the aftermath phase.
- For each scenario character:
  1. Check `CharacterLocationState.TrueLocation` vs `CurrentSceneLocation` (note: `CurrentSceneLocation` stores `Location.Name`)
  2. Look up `CharacterLocationAffinity` for current location (match by `LocationName` string comparison):
     - `Required` → MUST include (even if not detected at location by LLM)
     - `Excluded` → MUST exclude (even if detected at location)
     - `Preferred` → hint only, passed to LLM, doesn't force inclusion
     - No affinity → use `inScene` from location detection
  3. Check time-of-day on affinity: if affinity has `TimeOfDay` restriction, check match with `CurrentTimeOfDay`. Mismatch → affinity doesn't apply (treated as no affinity).
  4. Apply `CharacterTurnOverride.ParticipateInAutoContinue` filter
  5. Apply OtherMan opening exclusion (existing logic, `totalInteractions < 6`)
  6. Apply `BehaviorModeService.GetAllowedActors` filter (existing — runs after candidate gathering as a final gate)
- Returns: `List<AvailableCharacter>` with status flags

**Step 4.2:** `AvailableCharacter` record
- Fields: `Name`, `Role`, `IsInScene`, `AffinityStatus` (Required/Preferred/Excluded/None), `TimeOfDayMatch` (bool?), `IsAvailable` (bool)

### Phase 5: Character Turn Overrides
*Can run parallel with Phase 4*

**Step 5.1:** Create `CharacterTurnOverride` model
- New file: `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs`
- Fields: `CharacterName`, `ResponsePriority` (int?, nullable), `ParticipateInAutoContinue` (bool, default true), `PreferredPosition` (PreferredTurnPosition?, nullable)
- New enum: `PreferredTurnPosition { First, Last }`

**Step 5.2:** Add session-level properties
- File: `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`
- `Dictionary<string, CharacterTurnOverride> CharacterTurnOverrides` (case-insensitive)
- `int SceneContinueBatchSize` (default 3, range 1–6)
- `List<string>? LastActorOrdering` (transient)
- `string? LastContextFingerprint` (transient)

### Phase 6: Simplified Scoring Engine (Base Path — Always Runs)
*Depends on Phases 1–4*

**Step 6.1:** Implement `ScoreActorForAutoSelection`
- File: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- **Reframing:** Scoring is the **base path** (always runs, always produces an ordering). The LLM is an **optional enhancement** that reorders candidates when available. This is not a "fallback" — it's the primary deterministic path. The LLM enhances it when configured and context has changed.
- Scoring weights are **internal constants** (`private const double`), not user-tunable config. They are not RP behavior values — they are implementation details of the candidate ranking algorithm. The LLM is the user-facing decision-maker.
- Simplified — only objective factors:

| Factor | Range | Logic |
|---|---|---|
| Location Match | 0 or 1000 | `IsInScene` from location detection |
| Affinity: Required + match | +500 | Required affinity for current location |
| Affinity: Preferred + match | +200 | Preferred affinity for current location |
| Affinity: Excluded + at location | -1000 | Shouldn't be here |
| Affinity: Time-of-Day mismatch | -500 | Time restriction doesn't match |
| Affinity: Time-of-Day match | +100 | Time restriction matches |
| Recency | 0–200 | Never=200, >6=180, 4–6=120, 2–3=60, last=0 |

- Apply `CharacterTurnOverride.ResponsePriority` as additive boost
- Used as: (a) structured hints in the LLM prompt, (b) the base ordering when LLM is unavailable or context unchanged

### Phase 7: LLM Actor Selection Service
*Depends on Phases 4–6*

**Step 7.1:** Add `AppFunction.RolePlayActorSelection` enum
- File: `DreamGenClone.Domain/ModelManager/AppFunction.cs`

**Step 7.2:** Create request/response models
- New file: `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs`
- `ActorSelectionRequest`: NarrativeSummary, CurrentPhase, CurrentLocation, CurrentTimeOfDay, Candidates, ActiveThemes, RecentSemanticEvents, BatchSize
- `ActorCandidateInfo`: Name, Role, IsInScene, AffinityStatus, TimeOfDayMatch, KeyStats, LastSpokeTurnsAgo, BaseScore, AffinityDetails
- `ActorSelectionResponse`: OrderedNames, Reasoning, Source (LLM/Cache/Fallback)

**Step 7.3:** Create `IActorSelectionService` + implementation
- New file: `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs`
- Context fingerprint: `{CurrentPhase}|{CurrentSceneLocation}|{sortedCharacterNames}|{CurrentTimeOfDay}`
- Match → return cached ordering with recency rotation (no LLM call)
- Mismatch → build prompt, call LLM, parse JSON, cache result
- Timeout: 5s; on failure → return scoring order with `Source=Fallback` (explicit, logged)
- No model configured → return scoring order with `Source=Scoring` (base path, not a fallback)
- **No-fallback compliance:** `ModelResolutionService.ResolveAsync` throws `ModelResolutionException` when no model is configured. Catch this, log a warning, and return the scoring-ordered list with `Source=Scoring`. Do NOT silently substitute a different model or add hardcoded defaults.

**Step 7.4:** LLM prompt structure
- System: narrative director instructions with affinity/time/recency/score considerations
- User: phase, location, time, narrative summary, active themes, recent events, candidate list with stats/affinities/scores
- Response format: `{"characters": ["Name1", "Name2"], "reasoning": "..."}`

**Step 7.5:** Register in DI
- `builder.Services.AddScoped<IActorSelectionService, ActorSelectionService>();`

### Phase 8: Wire into ResolveSceneContinueActorsAsync
*Depends on Phases 1–7*

**Step 8.1:** Replace current sorting with new pipeline
- **Insertion point:** The new pipeline runs **after** the B-056 `AftermathCoupleInteraction` early-return guard (line ~2315) and **after** `BehaviorModeService.GetAllowedActors` (line ~2317). The B-056 branch is preserved unchanged.
- Call `ResolveAvailableCharacters()` → filtered candidates (location + affinity + time gates)
- Call `ScoreActorForAutoSelection()` for each → base scores
- Build `ActorSelectionRequest` with narrative summary + candidates
- Call `_actorSelectionService.SelectActorsAsync(request, ct)` → ordered list
- Map `OrderedNames` → `OverflowActorCandidate` list
- Preserve: persona insertion rules (first 6 / even / odd), B-056 aftermath filter, OtherMan opening exclusion
- Remove: old `OrderByDescending(InScene).ThenBy(LastSeenIndex).ThenBy(ScenarioOrder)` sort block

**Step 8.2:** Build narrative summary helper
- Method: `BuildNarrativeSummary(session, lastN=3)`
- Condensed: actor name + first 2-3 sentences per interaction (max ~150 tokens each)
- **Token budget:** Total narrative summary ≤ 500 tokens. Candidate info ≤ 50 tokens per character. Total prompt ≤ 1500 tokens for 10 candidates. This keeps the actor selection prompt small for fast LLM response.
- Used for both location detection (background) and actor selection (overflow click) prompts

**Step 8.3:** Debug logging
- Extend `OverflowActorSelection` event: source (LLM/Cache/Scoring/Fallback), locationSource (LLM/Disabled), availableCount/totalCharacters, per-candidate breakdown (score, affinity status, in-scene), LLM reasoning (if available)
- Also log location detection background job results in a `LocationDetectionCompleted` debug event

### Phase 9: UI — Scenario Editor
*Can run parallel with Phases 5–8*

**Step 9.1:** Location affinity editor in character section
- File: `DreamGenClone.Web/Components/Pages/Scenarios/ScenarioEditor.razor`
- Per character: scenario locations with Required/Preferred/Excluded/None + TimeOfDay dropdown
- Locations sourced from `Scenario.Locations`

**Step 9.2:** Scenario default time-of-day
- "Default Time of Day: [Afternoon ▼]" in scenario settings

### Phase 10: UI — Workspace
*Can run parallel with Phases 7–8*

**Step 10.1:** Time-of-day display + override
- Shows current time with dropdown: Morning/Afternoon/Evening/Night + "Auto"

**Step 10.2:** Per-character override controls
- Auto-participate checkbox, priority slider 0–100, position Auto/First/Last

**Step 10.3:** Batch size slider (1–6, default 3)

### Phase 11: Testing & Verification
*Depends on Phases 1–10*

**Step 11.1:** Location detection tests — LLM success, LLM failure (Success=false, CurrentSceneLocation unchanged), no model configured (warning logged, no detection), location change detected, per-character locations applied, background job dedupe key prevents duplicate jobs
**Step 11.2:** Time-of-day tests — keyword matching, word-boundary edge cases, manual override, Auto resume
**Step 11.3:** Character availability tests — Required/Excluded/Preferred affinity enforcement, time-of-day gating, ParticipateInAutoContinue filter
**Step 11.4:** Actor selection tests — LLM success (Source=LLM), LLM fail/timeout (Source=Fallback, scoring order preserved), no model configured (Source=Scoring, base path), cache hit (no LLM call), cache miss (LLM called), context fingerprint match/mismatch
**Step 11.5:** Integration test — full open-world flow with 5 characters across location/time changes
**Step 11.6:** Build verification — 0 errors, no regressions
**Step 11.7:** Manual smoke test — scenario with affinities, location transitions, time-of-day shifts

---

## 4. Persona Handling

The persona (POV character / "You") is handled separately from the main candidate pipeline. The existing rules are preserved:

- **First 6 interactions** → `Insert(0)` (persona leads to establish setup)
- **After 6, even `ObservedTurnCount`** → `Add()` (appended, last before narrative)
- **After 6, odd `ObservedTurnCount`** → skip

The persona is not scored and is not included in the LLM candidate list. These rules apply after the LLM selects NPC ordering.

---

## 5. Relevant Files

### New files:
- `DreamGenClone.Domain/RolePlay/TimeOfDay.cs` — TimeOfDay enum
- `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs` — affinity model + AffinityType enum
- `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs` — override model + PreferredTurnPosition
- `DreamGenClone.Web/Application/RolePlay/LocationDetectionService.cs` — ILocationDetectionService + impl
- `DreamGenClone.Web/Application/RolePlay/Models/LocationDetectionModels.cs` — request/response DTOs
- `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs` — IActorSelectionService + impl
- `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs` — request/response DTOs

### Modified files:
- `DreamGenClone.Domain/ModelManager/AppFunction.cs` — add RolePlayLocationDetection + RolePlayActorSelection
- `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` — add CurrentTimeOfDay, TimeOfDayManuallySet
- `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs` — add ActorName to SemanticEventRecord
- `DreamGenClone.Web/Domain/Scenarios/Scenario.cs` — add DefaultTimeOfDay
- `DreamGenClone.Web/Domain/Scenarios/Character.cs` — add LocationAffinities
- `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` — add CharacterTurnOverrides, SceneContinueBatchSize, cache fields
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — replace DetectSceneLocationSignalAsync; add DetectTimeOfDayAsync, ResolveAvailableCharacters, ScoreActorForAutoSelection, BuildNarrativeSummary; replace sort in ResolveSceneContinueActorsAsync; wire new services
- `DreamGenClone.Web/Application/RolePlay/SemanticEventInferenceService.cs` — populate ActorName
- `DreamGenClone.Web/Program.cs` — register ILocationDetectionService, IActorSelectionService
- `DreamGenClone.Web/Components/Pages/Scenarios/ScenarioEditor.razor` — location affinity editor
- `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — time-of-day + overrides + batch size
- `appsettings.json` + `appsettings.Development.json` — EnableLocationServices: true
- `DreamGenClone.Infrastructure/Persistence/` — migrations
- `DreamGenClone.Tests/RolePlay/` — new test files

### Reference files (patterns):
- `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs` — ResolveAsync for LLM slots
- `DreamGenClone.Web/Application/RolePlay/SemanticEventInferenceService.cs` — dedicated LLM slot usage
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:7507` — ClearLocationState, EnsureCharacterLocationRows, UpsertTrueLocation, UpdatePerceivedLocationsFromTruth (keep these)
- `DreamGenClone.Web/Application/RolePlay/RolePlayScenePresenceHelper.cs` — IsActorInScene

---

## 6. Migration & Backward Compatibility

| Aspect | Strategy |
|---|---|
| **Existing sessions** | No migration needed. `CharacterTurnOverrides` starts empty; `LocationAffinities` read from scenario at session start. |
| **Location services disabled** | If `EnableLocationServices: false`, location detection is skipped entirely. `CurrentSceneLocation` remains null. All characters get `IsInScene=false`. |
| **No location detection model** | If `EnableLocationServices: true` but no model configured for `RolePlayLocationDetection`, background job logs warning and skips. `CurrentSceneLocation` unchanged. No regex fallback. |
| **No actor selection model** | If no `RolePlayActorSelection` model configured, scoring-only base path runs (Source=Scoring). Not a fallback — it's the primary path without enhancement. |
| **Null stats** | Characters without `CharacterStatProfileV2` get neutral scores. |
| **Null role** | Characters without `CharacterRole` get neutral handling. |
| **No semantic events** | All characters get 0 semantic boost; `ActorName` is null for existing records. |
| **No themes** | All characters get 0 theme boost. |
| **Fallback** | If all scoring produces zeros or LLM is unavailable, `ResolveDefaultContinueActor` is used (existing behavior, preserved). |
| **Concurrency** | Background location detection writes `CurrentSceneLocation` while foreground reads it. **Accepted as eventually consistent** — one-turn lag. No session-level lock added. The `SemanticBackgroundJobQueue` dedupe key (`session:{sessionId}`) prevents duplicate jobs. |

---

## 7. Verification

1. **Build**: `dotnet build` — 0 errors
2. **Unit tests**: Location detection (LLM success/fallback/no-model), time-of-day detection, character availability resolver, actor selection (LLM/cache/fallback), overrides
3. **Integration**: Full open-world flow — location changes gate character availability; LLM selects from available pool
4. **Degradation chains**: Location LLM fails → `Success=false`, location unchanged (no regex); Actor LLM fails → scoring base path (Source=Fallback); No models configured → location skipped, scoring-only actor selection (Source=Scoring)
5. **Config**: Location services enabled by default; disabled skips detection entirely (no regex path remains)
6. **UI**: Affinity editor saves/loads; time-of-day manual/auto; overrides take effect

---

## 8. Decisions Record

1. **Location fix first, as part of B-050**: The disabled location services are the foundation for open-world character availability. Fixing them within B-050 ensures the fix is designed to feed character selection.
2. **Dedicated location detection LLM slot**: `AppFunction.RolePlayLocationDetection`. Separate from actor selection because it runs at different times (adaptive pipeline vs overflow click).
3. **Location as character gate**: `ResolveAvailableCharacters()` uses Required/Excluded affinities as hard gates. Preferred is a soft hint. Enables "Beach → Lifeguard appears, Neighbor disappears" open-world behavior.
4. **Two LLM calls, not one**: Location detection + actor selection are separate because they fire at different lifecycle points.
5. **No regex fallback**: Old regex matching is **removed entirely**. Location detection is LLM-only via background job. On failure, `CurrentSceneLocation` is left unchanged. This complies with the repo's no-fallback rules — there is no hidden alternate detection path. Gradual adoption is enabled by the `EnableLocationServices` toggle (off = no detection, on = LLM detection).
6. **Time-of-day auto-detection**: Keyword-based for V1. Simple but sufficient for Morning/Afternoon/Evening/Night discrimination. LLM-based detection could enhance in V2.

---

## 9. Scope Boundaries

**Included:**
- LLM-based location detection via background job (no regex fallback — regex code removed)
- `ResolveAvailableCharacters()` — location-gated character availability
- Scenario-level character-location affinities (Required/Preferred/Excluded + TimeOfDay)
- Time-of-day tracking with auto-detection + manual override
- LLM-driven actor selection with context-change gating + ordering cache
- Simplified scoring as base path (always runs) + LLM hints (when available)
- Per-character overrides (auto-participate, priority, position)
- Batch size control
- Debug logging with source tracking (LLM/Cache/Fallback for both location and actors)
- ActorName on SemanticEventRecord + migration
- UI: scenario affinity editor, workspace time-of-day + overrides

**Excluded (V2+):**
- Location-based arc/scene creation triggers ("arriving at Beach" → new arc)
- LLM-suggested character introduction (LLM proposing new characters not in scenario)
- Multi-location simultaneous scenes
- Location-to-theme mappings
- LLM-based time-of-day detection (keyword-only in V1)
- Per-character talkativeness drift, group dynamics, pacing sliders
- Location templates or reusable location profiles

---

## 10. Open-World Enablement (What This Unlocks)

After B-050, the system can support:
- **Location-triggered character introduction**: Narrative moves to Beach → Lifeguard (Required at Beach) automatically becomes available for actor selection
- **Time-gated characters**: Neighbor only available at Home during Evening; Night Guard only at Warehouse at Night
- **Location-based exclusion**: OtherMan excluded from Home → never appears in domestic scenes unless the narrative moves elsewhere
- **User-driven exploration**: "Go to beach with Wife" changes location → Lifeguard enters → new character dynamics
- **Scenario design**: Authors define which characters belong where, creating a "map" of possible encounters

---

## 11. Architectural Review (2026-07-14)

### Review Summary

The plan was reviewed against the actual codebase. 5 critical gaps, 4 compliance issues, and 3 design oversights were identified. All have been addressed in the updates above.

### Critical Gaps Fixed

| # | Gap | Fix Applied |
|---|---|---|
| 1 | Location detection LLM blocks every interaction (synchronous call at line 3891) | Moved to background job via `SemanticBackgroundJobQueue` pattern; one-turn lag accepted as eventually consistent |
| 2 | B-056 aftermath early-return not addressed | Explicit insertion point documented: new pipeline runs after B-056 guard (line ~2315) and after `GetAllowedActors` (line ~2317) |
| 3 | `DefaultTimeOfDay` not wired in `CreateSessionAsync` | Explicit copy step added at line ~424 alongside existing `scenario.Default*` copies; also wire `DefaultStartingLocationId` → `CurrentSceneLocation` seed |
| 4 | `LocationId` vs `Location.Name` mismatch in affinities | Changed `CharacterLocationAffinity.LocationId` → `LocationName` (string) for direct comparison with `CurrentSceneLocation` which stores `Location.Name` |
| 5 | No concurrency guard for background location writes | Accepted as eventually consistent; documented in migration table; `SemanticBackgroundJobQueue` dedupe key prevents duplicate jobs |

### Compliance Issues Fixed

| # | Issue | Fix Applied |
|---|---|---|
| 1 | "Regex fallback" violates no-fallback rules | Regex code removed entirely; on LLM failure, `Success=false` and location unchanged (same pattern as `SemanticEventInferenceService`) |
| 2 | "Scoring fallback" framing violates no-fallback rules | Reframed: scoring is the **base path** (always runs), LLM is an **optional enhancement**. No model = Source=Scoring (base, not fallback) |
| 3 | Hardcoded scoring weights need justification | Documented as internal `const` values, not user-tunable config. LLM is the user-facing decision-maker. Not RP behavior values. |
| 4 | `TimeOfDayManuallySet` flag needs UI surface | Already has UI surface in Phase 10 (Step 10.1). Confirmed compliant. |

### Design Oversights Fixed

| # | Oversight | Fix Applied |
|---|---|---|
| 1 | `DefaultStartingLocationId` unused — no initial location seed | Added wiring step in Phase 2: resolve `Location.Name` from `scenario.Locations` and set `CurrentSceneLocation` at session creation |
| 2 | Existing semantic events have null `ActorName` | Added backfill step in Phase 2: one-time SQL JOIN or C# migration script to populate `ActorName` from `InteractionId` |
| 3 | No token budget for narrative summary | Added truncation rules: 2-3 sentences per interaction (~150 tokens), total summary ≤ 500 tokens, total prompt ≤ 1500 tokens |

### What the Plan Gets Right (Confirmed)

1. **Two-pipeline architecture** — separating location detection (adaptive) from actor selection (overflow) is correct
2. **Context-change gating** — caching the LLM ordering and only re-querying on context change is the right latency optimization
3. **Location as character gate** — `ResolveAvailableCharacters` with Required/Excluded as hard gates is the right model for open-world
4. **Migration pattern** — the `HasColumnAsync` + `ALTER TABLE` pattern is already in the codebase and idempotent
5. **Scenario caching** — `ScenarioService` is in-memory dictionary, so multiple `GetScenarioAsync` calls per cycle are O(1)
6. **Existing injectors auto-benefit** — `TimeLocationInjector` and `RolePlayContinuationService` read `CurrentSceneLocation` directly; better detection improves them automatically
7. **Persona handling preserved** — the existing first-6/even/odd rules are kept as-is
8. **BehaviorMode filtering** — `GetAllowedActors` runs after candidate gathering as a final gate; the new pipeline fits this pattern

### Verdict

The plan is **ready for implementation handoff** after the above fixes. All critical gaps, compliance issues, and design oversights have been addressed in the updated document.
