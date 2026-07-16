# Tasks: Context-Aware Actor Selection

**Input**: Design documents from `/specs/001-context-aware-actor-selection/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

**Tests**: Tests ARE included — the spec mandates test coverage for major call paths (Constitution Principle VII) and the design's Phase 11 lists explicit test cases.

**Organization**: Tasks are grouped by user story (US1–US6) following the spec priorities. Phases are ordered by dependency. Each user story phase includes its independent test criteria.

## Agent Assignment Convention

Per user request, tasks are tagged with an **agent assignment** marker so a UI-focused agent and a backend-focused agent can be assigned disjoint, self-contained slices:

- 🟦 **[BE]** — Backend / engine / persistence / domain task (assign to backend agent)
- 🟨 **[UI]** — Razor / component / frontend task (assign to UI agent; follows `.github/instructions/razor-editing.instructions.md`)

UI tasks are grouped into their own user-story phases (US5 and US6 are pure UI) and the UI-related sub-tasks of earlier stories (US3 editor, US4 workspace) are broken out as separately assignable items. UI tasks depend only on the backend contract (interface or DTO shape) being stable, NOT on the backend implementation being complete — they can be developed in parallel with a stub.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3...)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain enums, AppFunction slots, BackgroundJobTypes constant — additive primitives that every user story depends on. No behavioural code yet.

- [X] T001 🟦 [BE] Add `TimeOfDay` enum in `DreamGenClone.Domain/RolePlay/TimeOfDay.cs` (values: Morning, Afternoon, Evening, Night)
- [X] T002 🟦 [BE] Add `AffinityType` enum in `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs` (values: None, Preferred, Required, Excluded)
- [X] T003 🟦 [BE] Add `PreferredTurnPosition` enum in `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs` (values: Auto, First, Last)
- [X] T004 🟦 [BE] Add `RolePlayLocationDetection` and `RolePlayActorSelection` enum members to `AppFunction` in `DreamGenClone.Domain/ModelManager/AppFunction.cs` (place after `RolePlaySemanticAnalysis`)
- [X] T005 🟦 [BE] Add `LocationDetection` constant to `BackgroundJobTypes` in `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs` (value: `"location-detection"`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain models + DTOs + interfaces + persistence schema. MUST be complete before any user story behaviour can be built.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 🟦 [BE] Add `LocationAffinities` property to `Character` in `DreamGenClone.Web/Domain/Scenarios/Character.cs` (type: `List<CharacterLocationAffinity>`, default `[]`)
- [X] T007 🟦 [BE] [P] Add `DefaultTimeOfDay` property to `Scenario` in `DreamGenClone.Web/Domain/Scenarios/Scenario.cs` (type: `TimeOfDay`, default `TimeOfDay.Afternoon`)
- [X] T008 🟦 [BE] [P] Add `CurrentTimeOfDay` (nullable) and `TimeOfDayManuallySet` (bool) properties to `AdaptiveScenarioState` in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
- [X] T009 🟦 [BE] [P] Add `CharacterTurnOverrides` (case-insensitive dictionary) plus transient `LastActorOrdering` and `LastContextFingerprint` fields (with `[JsonIgnore]`) to `RolePlaySession` in `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`. Note: `SceneContinueBatchSize` already exists (default 3) — no new field needed
- [X] T010 🟦 [BE] [P] Add `ActorName` (nullable string) property to `SemanticEventRecord` in `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`
- [X] T011 🟦 [BE] [P] Create `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs` model (properties: `LocationName` string, `AffinityType`, `TimeOfDay?`)
- [X] T012 🟦 [BE] [P] Create `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs` model (properties: `CharacterName`, `ResponsePriority` int? , `ParticipateInAutoContinue` bool default true, `PreferredPosition`)
- [X] T013 🟦 [BE] [P] Create `DreamGenClone.Web/Application/RolePlay/Models/LocationDetectionModels.cs` with `LocationDetectionRequest` and `LocationDetectionResult` DTOs per [data-model.md](data-model.md)
- [X] T014 🟦 [BE] [P] Create `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs` with `ActorSelectionRequest`, `ActorSelectionResponse`, `ActorCandidateInfo`, and `ActorSelectionSource` enum per [data-model.md](data-model.md)
- [X] T015 🟦 [BE] [P] Create `DreamGenClone.Web/Application/RolePlay/LocationDetectionJobPayload.cs` (single property: `SessionId`)
- [X] T016 🟦 [BE] [P] Create `DreamGenClone.Web/Application/RolePlay/ILocationDetectionService.cs` interface per [contracts/ILocationDetectionService.md](contracts/ILocationDetectionService.md)
- [X] T017 🟦 [BE] [P] Create `DreamGenClone.Web/Application/RolePlay/IActorSelectionService.cs` interface per [contracts/IActorSelectionService.md](contracts/IActorSelectionService.md)
- [X] T018 🟦 [BE] Add three idempotent `ALTER TABLE` migrations inside `RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync` in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`:
  - `RolePlayV2AdaptiveStates.CurrentTimeOfDay TEXT NULL` (use `HasColumnAsync` pattern)
  - `RolePlayV2AdaptiveStates.TimeOfDayManuallySet INTEGER NOT NULL DEFAULT 0`
  - `RolePlayV2SemanticEvents.ActorName TEXT`
  Also update `LoadSemanticEventsAsync`, `SaveSemanticEventsAsync`, `LoadAdaptiveStateAsync`, `SaveAdaptiveStateAsync` to round-trip the new columns
- [X] T019 🟦 [BE] Implement one-time idempotent `ActorName` backfill: inside the same `HasColumnAsync` guard as the `ALTER TABLE RolePlayV2SemanticEvents ADD COLUMN ActorName` block in `RolePlayStateRepository.cs`, run `UPDATE RolePlayV2SemanticEvents SET ActorName = (SELECT i.ActorName FROM RolePlayInteractions i WHERE i.Id = RolePlayV2SemanticEvents.InteractionId) WHERE ActorName IS NULL AND EXISTS (SELECT 1 FROM RolePlayInteractions i WHERE i.Id = RolePlayV2SemanticEvents.InteractionId)`
- [X] T020 🟦 [BE] Wire `ActorName` population at the new-event creation sites: `RolePlayAdaptiveStateService.cs:1036` (the `new SemanticEventRecord { ... }` literal) reads `ActorName` from the inference request; `RolePlayEngineService.cs:8080` reads from the corresponding `RolePlayInteraction` row during V1→V2 migration

**Checkpoint**: Foundation ready — domain models build, schema migrations are idempotent, contracts in place. User story behaviour can begin.

---

## Phase 3: User Story 1 — Location Detects Where Characters Are (Priority: P1) 🎯 MVP foundation

**Goal**: Replace synchronous regex-based `DetectSceneLocationSignalAsync` with a background-enqueued LLM-driven `LocationDetectionService`. On LLM failure or no model configured, `Success = false`, `CurrentSceneLocation` unchanged. No regex fallback path remains.

**Independent Test**: Start a session with a scenario that has Home + Beach locations. Narrate moving from Home to Beach. Within one subsequent turn, `CurrentSceneLocation` reflects "Beach". Verify the location change is logged as a debug event; verify the old regex helpers no longer exist in the codebase.

### Tests for User Story 1

> Write these tests FIRST, ensure they FAIL before implementation.

- [X] T021 🟦 [BE] [P] [US1] Create `DreamGenClone.Tests/RolePlay/LocationDetectionServiceTests.cs`: LLM success path (mock `ICompletionClient` returns valid JSON → `Success=true`, `DetectedLocation` populated); LLM returns unknown location → `Success=false`; `ModelResolutionException` → `Success=false`, `ErrorMessage` populated; parse failure → `Success=false`; `Success=false` (any of the above) → caller MUST NOT mutate `CurrentSceneLocation`
- [X] T022 🟦 [BE] [P] [US1] Create `DreamGenClone.Tests/RolePlay/LocationDetectionJobHandlerTests.cs`: job dedupe key `$"location:{sessionId}"` prevents duplicate concurrent jobs; handler skips work when `EnableLocationServices=false` with Information log; `Success=true` persists `CurrentSceneLocation` to DB; `Success=false` leaves DB value unchanged; re-enqueue is safe (no double-write)

### Implementation for User Story 1

- [X] T023 🟦 [BE] [P] [US1] Implement `DreamGenClone.Web/Application/RolePlay/LocationDetectionService.cs` mirroring `SemanticEventInferenceService` exactly: constructor `(ICompletionClient, IModelResolutionService, ILogger<LocationDetectionService>?)`; `try ResolveAsync(AppFunction.RolePlayLocationDetection)` → `catch (ModelResolutionException)` returns `Success=false`; build system + user prompt per [contracts/ILocationDetectionService.md](contracts/ILocationDetectionService.md); `JsonSerializerDefaults.Web` with `ExtractJsonObject` helper; throw `InvalidOperationException` on parse failure (caller's worker catches)
- [X] T024 🟦 [BE] [P] [US1] Implement `DreamGenClone.Web/Application/RolePlay/LocationDetectionJobHandler.cs` as `IBackgroundJobHandler` with `JobType => BackgroundJobTypes.LocationDetection`. Check `RolePlayDecisionOptions.EnableLocationServices` (early-return with Information log if false); deserialize payload `{ SessionId }`; load fresh `AdaptiveScenarioState` + `RolePlaySession` from `RolePlayStateRepository`; build `LocationDetectionRequest` (last 3 NPC/Custom interactions, scenario location names, previous `CurrentSceneLocation`, character names); on `Success=true`: call `UpsertTrueLocation` per `PerCharacterLocations`, `UpdatePerceivedLocationsFromTruth`, update `state.CurrentSceneLocation`, persist via `SaveAdaptiveStateAsync`, emit `LocationDetectionCompleted` debug event (source=LLM, confidence, previous+new locations). On `Success=false`: log warning, leave state unchanged, emit `LocationDetectionSkipped` debug event with reason
- [X] T025 🟦 [BE] [US1] Register DI in `DreamGenClone.Web/Program.cs`: `builder.Services.AddScoped<ILocationDetectionService, LocationDetectionService>();` and `builder.Services.AddScoped<IBackgroundJobHandler, LocationDetectionJobHandler>();` near the existing `ISemanticEventInferenceService` and `SemanticInteractionAnalysisJobHandler` registrations (Program.cs:91, 208)
- [ ] T026 🟦 [BE] [US1] Enqueue location job in `RolePlayEngineService.cs`. Replace the synchronous call at line ~L3891 (`var sceneLocationSignal = _enableLocationServices ? await DetectSceneLocationSignalAsync(...) : null;`) with a fire-and-forget call to a new private `EnqueueLocationDetectionJob(session)` helper that calls `_backgroundJobQueue.Enqueue(BackgroundJobTypes.LocationDetection, JsonSerializer.Serialize(new LocationDetectionJobPayload { SessionId = session.Id }, JsonOptions), dedupeKey: $"location:{session.Id}")`. Remove the synchronous downstream reads of `sceneLocationSignal` — downstream now reads `v2State.CurrentSceneLocation` directly (one-turn lag accepted)
- [ ] T027 🟦 [BE] [US1] Delete the regex-based helpers entirely from `RolePlayEngineService.cs`: the `GenericLocationNames` static array and initializer (L46–L91); `DetectSceneLocationSignalAsync` (L7396–L7498); `MatchScenarioLocation` (L7686); `MatchGenericLocation` (L7708); `ContainsWholeWord` (L7726) **after** auditing callers per [research.md R8](research.md) (only delete if no remaining call sites beyond the deleted detection method — confirm via `grep_search`; if other methods at L7648/L7662 still use it for unrelated persona proximity checks, keep it as a private utility on those methods)
- [X] T028 🟦 [BE] [US1] Extract the per-character location helpers from `RolePlayEngineService.cs` into a new internal static helper class `RolePlayCharacterStateMutator` (new file: `DreamGenClone.Web/Application/RolePlay/RolePlayCharacterStateMutator.cs`). Methods to move: `ClearLocationState` (L7507), `EnsureCharacterLocationRows` (L7514), `UpsertTrueLocation` (L7535), `UpdatePerceivedLocationsFromTruth` (L7557) — promote from `private static` to `public static` on the new class. The existing call sites in `RolePlayEngineService.cs` (`ClearLocationState` warm-up at L3106; helpers called from former `DetectSceneLocationSignalAsync` block) MUST be updated to reference `RolePlayCharacterStateMutator.ClearLocationState(...)` etc. The new `LocationDetectionJobHandler` calls `RolePlayCharacterStateMutator.EnsureCharacterLocationRows(state)` etc. directly. Rejected alternative (internal-interface injection) adds DI overhead for pure mutation helpers with no runtime coupling; static-class extraction keeps the public `RolePlayStateRepository` boundary intact. Record the chosen design in the commit message body
- [X] T029 🟦 [BE] [US1] Seed `CurrentSceneLocation` from `Scenario.DefaultStartingLocationId` (currently unused) in `CreateSessionAsync` at `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` around L404–L410 (alongside existing `scenario.Default*` copies). When `scenario.DefaultStartingLocationId` is non-null, resolve `Location.Name` from `scenario.Locations` and set `session.AdaptiveState.CurrentSceneLocation = locationName.Trim()`. Also seed `session.AdaptiveState.CurrentTimeOfDay = scenario.DefaultTimeOfDay;` in the same block
- [ ] T030 🟦 [BE] [US1] Flip `EnableLocationServices` from `false` to `true` in both `DreamGenClone.Web/appsettings.json:97` and `DreamGenClone.Web/appsettings.Development.json:64` (config value already wired via `RolePlayDecisionOptions.EnableLocationServices` at L313 with `?? true` default)
- [ ] T031 🟦 [BE] [US1] Add Information-level Serilog structured logs in `LocationDetectionService` and `LocationDetectionJobHandler` for REQUEST (`SessionId`, `Function`, `Model`, Provider`, `ElapsedMs`, `OutputLen`), RESPONSE, FAILED, and SKIPPED events. Mirror `SemanticEventInferenceService` log-property conventions exactly. Also log the `LocationDetectionCompleted`/`LocationDetectionSkipped` debug events via `RolePlayDebugEventSink` (existing `IRolePlayDebugEventSink`)

**Checkpoint**: User Story 1 fully functional — LLM-driven background location detection, no regex fallback, debug events, seeded initial location from scenario. Testable independently by running T021/T022 against the service/handler.

---

## Phase 4: User Story 2 — Characters Appear Only Where They Belong (Priority: P1) 🎯 MVP

**Goal**: After `CurrentSceneLocation` is reliably detected, gate character availability using per-character `CharacterLocationAffinity` rules. Required/Excluded are hard gates; Preferred is a hint. Aftermath (B-056) phase bypasses this pipeline.

**Independent Test**: Configure a scenario with Home (Wife=Required, Lifeguard=Excluded) and Beach (Wife=Preferred, Lifeguard=Required). Start at Home — verify Lifeguard is not in the available character pool. Move to Beach — verify Lifeguard becomes available, Wife remains available.

### Tests for User Story 2

- [X] T032 🟦 [BE] [P] [US2] Create `DreamGenClone.Tests/RolePlay/CharacterAvailabilityResolverTests.cs`: Required affinity → character always included; Excluded → never included; Preferred → included (no force, hint only); no affinity → fall back to `IsInScene` from `RolePlayScenePresenceHelper`; Aftermath (B-056) phase → bypass pipeline entirely returning couple actors; multi-affinity same-location conflict (e.g., Excluded + Required at Home) → Excluded wins per [research.md R11](research.md) precedence

### Implementation for User Story 2

- [X] T033 🟦 [BE] [US2] Implement private `ResolveAvailableCharacters` method inside `RolePlayEngineService.cs`. Inputs: `RolePlaySession`, `Scenario`, `string? currentSceneLocation`, `AdaptiveScenarioState v2State`. Output: `List<AvailableCharacter>`. Apply per [research.md R11](research.md) precedence: filter `Character.LocationAffinities` to current location (string.Equals `LocationName` OrdinalIgnoreCase); apply time-of-day specific-rule → wildcard fallback; `Excluded > Required > Preferred > None` precedence; hard-gate `ParticipateInAutoContinue = false`; preserve the existing OtherMan opening exclusion (`totalInteractions < 6` + role == "OtherMan"); `GetAllowedActors` filter at the existing line L2317 stays upstream of this method
- [X] T034 🟦 [BE] [US2] Define the internal `AvailableCharacter` record inside `RolePlayEngineService.cs` (per [data-model.md](data-model.md)): `Name`, `Role`, `IsInScene`, `AffinityStatus` (private enum mirroring `AffinityType`+None), `TimeOfDayMatch` (bool?), `IsAvailable` (bool), `AffinityDetails` (string?). Document conflict-precedence code with comments referencing the spec clarification Q1
- [X] T035 🟦 [BE] [US2] Verify B-056 aftermath early-return guard (L2262–L2313 of `RolePlayEngineService.cs`) is preserved unchanged. The `ResolveAvailableCharacters` pipeline runs only when `CurrentTimeSkipPhase != AftermathCoupleInteraction`. Add an explicit comment at the after-guard return documenting that the new pipeline is intentionally bypassed for aftermath

**Checkpoint**: User Stories 1 + 2 together form the open-world MVP — location detection + location-gated availability.

---

## Phase 5: User Story 4 — Smarter Actor Selection for Overflow Continue (Priority: P2)

**Goal**: Replace the recency-only sort in `ResolveSceneContinueActorsAsync` with: (1) a deterministic scoring base path that ALWAYS runs, and (2) an optional LLM reordering step gated by a composite context-change fingerprint. Persona insertion and empty-list fallback preserved unchanged.

**Independent Test**: In a session with 5 characters at a location, click overflow continue 3 times with narrative context shifts. Verify ordering changes (not a fixed rotation) and the debug log shows the selection source (LLM / Cache / Scoring / Fallback).

### Tests for User Story 4

- [X] T036 🟦 [BE] [P] [US4] Create `DreamGenClone.Tests/RolePlay/ActorScoringTests.cs`: each weight term in isolation (LocationMatch ±1000, Required +500, Preferred +200, Excluded −1000, TimeOfDay match +100, mismatch −500, recency bands 0/60/120/180/200, ResponsePriority additive 0–100, PreferredPosition First +50 / Last −50); combined-factor collisions (e.g., InScene + Required + TimeMatch); persona is NOT scored
- [X] T037 🟦 [BE] [P] [US4] Create `DreamGenClone.Tests/RolePlay/ActorSelectionServiceTests.cs`: Source=LLM on valid JSON response; Source=Cache on fingerprint hit (no LLM call invoked); Source=Scoring on `ModelResolutionException` (no model configured); Source=Fallback on timeout / parse failure / unknown names in response — preserving scoring order; cache fingerprint match/mismatch; JSON validation rejects names not in `request.Candidates`; empty `characters` array from LLM is handled (returned as-is, no exception)
- [X] T038 🟦 [BE] [P] [US4] Create `DreamGenClone.Tests/RolePlay/ResolveSceneContinueActorsIntegrationTests.cs`: full open-world flow with seeded scenario + scripted location/time shifts across 5 candidates; persona insertion rules (first 6 = Insert(0), even ObservedTurnCount = Add, odd = skip) preserved exactly; Aftermath (B-056) returns couple list bypassing pipeline

### Implementation for User Story 4

- [X] T039 🟦 [BE] [P] [US4] Implement the scoring helper inside `RolePlayEngineService.cs` as `private double ScoreActorForAutoSelection(AvailableCharacter, AdaptiveScenarioState, List<string?> recentActors, Dictionary<string, CharacterTurnOverride> overrides)`. Apply the [research.md R9](research.md) weight table as `private const double` constants — NOT user-tunable config. Include `ResponsePriority` (0–100 additive boost) and `PreferredPosition` (First=+50, Last=−50) per clarifications Q3/Q4
- [X] T040 🟦 [BE] [P] [US4] Implement `private string BuildNarrativeSummary(RolePlaySession session, int lastN = 3)` inside `RolePlayEngineService.cs` to produce a condợi ≤500-token text snippet of last 3 interactions (2–3 sentences each, actor name prefixed). Used only for LLM-actor-selection prompt context; pure function, no side effects
- [X] T041 🟦 [BE] [P] [US4] Implement `private string BuildFingerprint(AdaptiveScenarioState v2State, List<AvailableCharacter> available)` inside `RolePlayEngineService.cs`: composite `$"{v2State.CurrentPhase}|{v2State.CurrentSceneLocation}|{string.Join(",", sortedAvailableNames)}|{v2State.CurrentTimeOfDay}"` per [research.md R10](research.md). Per-character stat deltas MUST NOT be part of the fingerprint
- [X] T042 🟦 [BE] [US4] Implement `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs` per [contracts/IActorSelectionService.md](contracts/IActorSelectionService.md). Constructor mirrors `SemanticEventInferenceService`: `(ICompletionClient, IModelResolutionService, ILogger<ActorSelectionService>?)`. Decision order: (a) Cache fingerprint+ordering hit → return cached (`Source=Cache`, no LLM call); (b) `ModelResolutionException` → scoring-only return (`Source=Scoring`, `Success=true` — this is the base path, NOT a fallback); (c) LLM call succeeds → save ordering (to `RolePlaySession.LastActorOrdering` + `LastContextFingerprint` via caller, not here), return (`Source=LLM`); (d) LLM timeout/parse/unknown names → return (`Source=Fallback`, `Success=false`, `ErrorMessage` set). 5-second hard `CancellationTokenSource.CancelAfter` timeout around the LLM call
- [X] T043 🟦 [BE] [US4] Register DI in `DreamGenClone.Web/Program.cs` near L91: `builder.Services.AddScoped<IActorSelectionService, ActorSelectionService>();`
- [X] T044 🟦 [BE] [US4] Rewrite the sort block in `ResolveSceneContinueActorsAsync` at `RolePlayEngineService.cs` lines ~L2415–L2424 (the `ordered = eligibleCharacterNames.Select(...).OrderByDescending(x => x.InScene).ThenBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex).ThenBy(x => x.ScenarioOrder).Select(x => x.Name).ToList();` block). Build `ActorSelectionRequest` with `Candidates` from scored `ResolveAvailableCharacters` output (requires both Phase 4 `ResolveAvailableCharacters` and Phase 5 scoring wired), call `_actorSelectionService.SelectActorsAsync`, map `OrderedNames` back. PERSIST `LastActorOrdering` + `LastContextFingerprint` to `RolePlaySession` ONLY when `Source` is `LLM` or `Cache` (Fallback leaves the cache alone; Scoring is no-cache by design). Preserve the existing persona insertion rules below L2435 verbatim (first-6/even/odd); preserve empty-list fallback to `ResolveDefaultContinueActor` at L2461
- [X] T045 🟦 [BE] [US4] Emit the `OverflowActorSelection` debug event via `RolePlayDebugEventSink` on every `ResolveSceneContinueActorsAsync` call with: `source` (LLM/Cache/Scoring/Fallback), `sessionId`, `availableCount`/`totalCharacters`, per-candidate breakdown (score, affinityStatus, inScene), LLM `reasoning` (when available), `cacheKey`
- [X] T046 🟦 [BE] [US4] Add Information-level Serilog structured logs in `ActorSelectionService` and the `ResolveSceneContinueActorsAsync` call site for: LLM REQUEST (SessionId, Function, Model, CandidateCount, NarrLen, BatchSize, CacheKey), LLM RESPONSE (Model, ElapsedMs, Source, Returned, Reasoning), Cache hit (Debug level), Scoring path (Information: "no model configured, Source=Scoring"), Fallback (Warning: ErrorType, ErrorMessage), Parse failure (Warning: ParseError, Source=Fallback, RawOutputLength). Property names mirror `SemanticEventInferenceService` conventions

**Checkpoint**: User Story 4 fully functional — context-aware AI actor selection with cache + scoring base path + explicit fallback handling.

---

## Phase 6: User Story 3 — Time-of-Day Gates Character Availability (Priority: P2)

**Goal**: Auto-detect time-of-day from narrative via keyword matching. Allow manual override that suppresses auto-detection until switched back to "Auto". Time-of-day mismatch on a character affinity entry treats that affinity as not-applicable.

**Independent Test**: Configure a character with Home + Evening affinity. Start session at Home with time set to Afternoon — verify character is excluded. Change time to Evening (via narrative or manual override) — verify character becomes available.

### Tests for User Story 3

- [X] T047 🟦 [BE] [P] [US3] Create `DreamGenClone.Tests/RolePlay/TimeOfDayDetectionTests.cs`: keyword presence (morning/dawn/sunrise/breakfast/woke up/wake up → Morning; afternoon/lunch/midday/noon → Afternoon; evening/dusk/sunset/dinner/nightfall → Evening; night/midnight/moonlight/dark outside/late hour → Night); word-boundary edge cases ("afternoonschool" does NOT match Afternoon); manual override suppresses auto-detect (when `TimeOfDayManuallySet=true`, autodetect MUST NOT mutate `CurrentTimeOfDay`); switching flag back to false resumes auto-detect; multiple time-period matches in last 3 interactions → most recent mention wins

### Implementation for User Story 3

- [X] T048 🟦 [BE] [US3] Implement `private TimeOfDay? DetectTimeOfDayAsync(RolePlaySession session)` inside `RolePlayEngineService.cs` per spec FR-004: word-boundary regex matching against last 3 NPC/Custom interaction `Content` strings; multiple keyword sets per time-of-day; null when no signal. Pure synchronous (no LLM); never throws
- [X] T049 🟦 [BE] [US3] Wire `DetectTimeOfDayAsync` into the adaptive pipeline of `RolePlayEngineService.cs` immediately AFTER the location-detection enqueue (line ~L3891+ where T026 wired the enq). Only update `session.AdaptiveState.CurrentTimeOfDay` when `session.AdaptiveState.TimeOfDayManuallySet == false`. Manual override sets `TimeOfDayManuallySet = true` (via Workspace UI, US6); UI "Auto" selection sets it back to `false`
- [X] T050 🟦 [BE] [US3] Update `ResolveAvailableCharacters` (from Phase 4 / T033) so that `TimeOfDayMatch` is correctly computed per [research.md R11](research.md): filter to entries whose `TimeOfDay == CurrentTimeOfDay` (specific rule) first; if none, fall back to `TimeOfDay == null` (wildcard); exact-match entries win over wildcard. `TimeOfDayMatch=true` on affinity entry's exact time match; `false` when mismatch (affinity treated as not-applicable — `none`); `null` for wildcard

**Checkpoint**: User Story 3 fully functional — open-world with time-gated characters.

---

## Phase 7: User Story 5 — Authors Configure Location Affinities and Time (Priority: P3) 🟨 UI Phase

**Goal**: Scenario-author-facing UI in `ScenarioEditor.razor` for per-character affinity editing and scenario default-time-of-day setting.

**Independent Test**: Open the scenario editor with a scenario that has 3 locations. Set Beach = Required for one character with time = Afternoon. Save. Reopen — verify the setting persisted. Start a new session with this scenario — verify the affinity is active at the next overflow click.

### Tests for User Story 5

> UI tasks are tagged 🟨 **[UI]**. The backend agent's deliverable for this story is to confirm the `Character.LocationAffinities` and `Scenario.DefaultTimeOfDay` properties (.created in Phase 2) serialize and round-trip via the existing `ScenarioService` JSON persistence. No new backend task.

### Implementation for User Story 5

- [X] T051 🟦 [BE] [US5] Verify (build-time smoke) that `Character.LocationAffinities` and `Scenario.DefaultTimeOfDay` save/load through `ScenarioService` JSON serialization without manual persistence-side changes. If an explicit serialization smell is found (e.g., a `[JsonIgnore]` is erroneously present), fix the property attribute accordingly
- [X] T052 🟨 [UI] [P] [US5] Add a "Default Time of Day" `<select>` bound to `CurrentScenario.DefaultTimeOfDay` in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor` within the existing "Scenario Details" card (around L56–L74). Options render from `Enum.GetValues<TimeOfDay>()`. Default binder uses the existing `SaveScenario` flow. Follow `.github/instructions/razor-editing.instructions.md` micro-step rules — read the surrounding card markup fully before editing
- [X] T053 🟨 [UI] [US5] Add a per-character "Location Affinities" editor subsection inside `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`. For each character in `CurrentScenario.Characters`, iterate `CurrentScenario.Locations` and render per-location: a read-only location name label, an `AffinityType` `<select>` (None/Preferred/Required/Excluded bound to `CurrentScenario.Characters[idx].LocationAffinities` matching by `LocationName` lazily-initialised list entry), and a `TimeOfDay?` `<select>` (Auto/Morning/Afternoon/Evening/Night — Auto = null). On save, persist via existing `SaveScenario` flow
- [X] T054 🟨 [UI] [US5] Add "+ add time-specific rule" controls per (character, location): allow the user to add multiple `CharacterLocationAffinity` entries for the same `LocationName` with distinct `TimeOfDay` values (per clarification Q1). Implement client-side conflict warning per the precedence rule (Excluded > Required > Preferred): e.g., when a user adds an Excluded rule for the same time slot as an existing Required rule, show "This Excluded rule will conflict with the existing Required rule — Excluded wins." Authors see the exact affinity-type pairing in the message, not a generic 'Evening' placeholder
- [X] T055 🟨 [UI] [US5] Extract `CharacterLocationAffinityEditor.razor` as a sub-component under `DreamGenClone.Web/Components/Scenarios/` if T053 + T054 exceed ~80 lines of markup combined; otherwise inline in `ScenarioEditor.razor`. The decision is task-team-rerun: defer to size reality, not pre-emptive extraction. Extract requires a Razor-namespace `@using` and a parameter-based interface (one-way bound character + locations + affinities list)

**Checkpoint**: User Story 5 fully functional — authors can configure open-world character placement.

---

## Phase 8: User Story 6 — User Controls in Workspace (Priority: P3) 🟨 UI Phase

**Goal**: Workspace-facing UI in `RolePlayWorkspace.razor` for time-of-day display/override, per-character turn overrides, and batch-size adjustment.

**Independent Test**: Open a workspace during a session. Verify time-of-day is displayed with an "Auto" indicator. Change it manually to "Night" — verify it stays on the next interaction. Switch back to "Auto" — verify auto-detection resumes on the next narrated interaction. Toggle a character's auto-participate setting — verify it takes effect on the next overflow click.

### Tests for User Story 6

> UI tasks are tagged 🟨 **[UI]**. There is one backend verif sub-task for the override persistence flow.

### Implementation for User Story 6

- [X] T056 🟦 [BE] [US6] Implement workspace-side persistence endpoints/handlers: adding/updating/removing a `CharacterTurnOverride` from `session.CharacterTurnOverrides`; toggling `session.AdaptiveState.TimeOfDayManuallySet` and `session.AdaptiveState.CurrentTimeOfDay`. Verify these rows round-trip via the existing `SaveAdaptiveStateAsync` path from T018 (no new repository methods should be required — confirm with grep)
- [X] T057 🟨 [UI] [P] [US6] Add time-of-day display + manual override dropdown in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (settings panel area — search for `rw-panel-toggle-btn` near L9066 to locate the settings panel). Shows `session.AdaptiveState.CurrentTimeOfDay?.ToString() ?? "(unknown)"` with an "Auto" indicator; dropdown lists Auto/Morning/Afternoon/Evening/Night. On change: persist via the endpoint from T056; Auto → flag clears, manual value → flag sets and updates value. Follow `.github/instructions/razor-editing.instructions.md` strictly — `RolePlayWorkspace.razor` is 9000+ lines, surgical micro-step edits only
- [X] T058 🟨 [UI] [P] [US6] Add per-character override panel in `RolePlayWorkspace.razor` per-character sub-section: auto-participate checkbox (bound to `CharacterTurnOverride.ParticipateInAutoContinue`), response-priority slider 0–100 (bound to `ResponsePriority`, clamped on save per [research.md](research.md)), and preferred-position dropdown (Auto/First/Last bound to `PreferredPosition`). Persona ("You") MUST NOT appear in the list. Persist via the endpoint from T056
- [X] T059 🟨 [UI] [P] [US6] Add batch-size slider (range 1–6, default 3) in `RolePlayWorkspace.razor` settings panel bound to `session.SceneContinueBatchSize` — the property already exists on `RolePlaySession` (L46). Persist via the existing session-save flow

**Checkpoint**: All six user stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Cross-cutting test coverage, build verification, manual smoke.

- [X] T060 🟦 [BE] [P] Add `DreamGenClone.Tests/RolePlay/SemanticEventActorNameBackfillTests.cs`: first-run populates null `ActorName` rows; second-run is no-op (idempotent); rows whose `InteractionId` is missing from `RolePlayInteractions` are left null (no creation); the one-time backfill is safe to re-run across app restarts
- [X] T061 🟦 [BE] [P] Audit `ContainsWholeWord` usage across the repo via `grep_search` and confirm no remaining call sites outside of deleted methods. If any remain in unrelated persona proximity logic, verify that logic still functions after T027 deletion (no broken references)
- [X] T062 🟦 [BE] Run full build: `dotnet build DreamGenClone.sln` — 0 errors. Resolve any compilation failures
- [X] T063 🟦 [BE] Run full test suite: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj` — all green. Investigate any regressions
- [X] T064 🟦 [BE] Run the spec's manual smoke test from [quickstart.md Step 15](quickstart.md): create scenario with Home (Wife Required) / Beach (Wife Preferred, Lifeguard Required) / Neighbor (Home + Evening Required); configure models for `RolePlayLocationDetection` and `RolePlayActorSelection` in Model Manager; start session at Home with time=Afternoon; verify Lifeguard absent; narrate move to Beach; verify Lifeguard appears; override time-of-day to Evening and navigate back Home; verify Neighbor appears; toggle auto-participate off for Wife; verify Wife excluded; reset time to Auto; verify auto-detection resumes
- [X] T065 🟦 [BE] Confirm `copilot-instructions.md` no-fallback rules across every new branch: NO regex detection path in `RolePlayEngineService.cs`; NO scoring-weight user-tunable config (`const` only); `ModelResolutionException` → `Success=false` (NOT silent replacement); `Source=Scoring` is base path, NOT fallback; `Source=Fallback` reserved for LLM-attempted-and-failed cases. Record the verification result in commit message
- [X] T066 🟦 [BE] [P] Update `.github/agents/copilot-instructions.md` if any new architectural discovery emerged during implementation that future agents should know (e.g., a workaround for a build quirk). Otherwise skip
- [X] T067 🟨 [UI] [US5] Manual UX timing measurement for SC-007: in the ScenarioEditor.razor session, configure a scenario with 5 characters × 3 locations × per-character affinities (mix of Required/Preferred/Excluded with at least 2 time-of-day restrictions). Time the configuration from editor-open to save-complete. Verify ≤ 2 minutes. Record the measured time + any UI friction notes in the Phase 9 smoke report. Must be performed by someone who has NOT seen the affinity editor design before (fresh-user perspective) OR by the UI agent self-noting measured time honestly — first preference is fresh user

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies. Start immediately. Can run as parallel commits within a single session.
- **Phase 2 (Foundational)**: Depends on Phase 1 enums. BLOCKS all user stories. T018–T020 are sequential (schema → backfill → population wiring); the model/DTO/interface tasks T006–T017 can run in parallel.
- **Phase 3 (US1, P1)**: Depends on Phase 2 (needs DTOs, interfaces, schema). Sequential within: tests first (T021/T022 in parallel) → implementation (T023/T024 parallel) → wiring T025→T026→T027→T028→T029→T030→T031.
- **Phase 4 (US2, P1)**: Depends on Phase 3 (needs `CurrentSceneLocation` reliably detected). T032 in parallel with nothing; T033 → T034 → T035 sequential.
- **Phase 5 (US4, P2)**: Depends on Phase 4 (needs `ResolveAvailableCharacters` from T033). Tests T036/T037/T038 in parallel. Implementation: T039/T040/T041 in parallel → T042 → T043 → T044 → T045 → T046 sequential.
- **Phase 6 (US3, P2)**: Depends on Phase 2 (`CurrentTimeOfDay` field). T047 in parallel; T048 → T049 → T050 sequential. Can start in parallel with Phase 4/5 if the backend agent has capacity (time-of-day resolver doesn't need `CurrentSceneLocation`).
- **Phase 7 (US5, P3 — UI)**: Depends on Phase 2 (needs `Character.LocationAffinities` and `Scenario.DefaultTimeOfDay` properties). UI agent can begin T052/T053/T054/T055 in parallel with backend Phase 3/4/5 work — the UI integrates only with the contract (DTO shapes already in Phase 2), not the backend implementation.
- **Phase 8 (US6, P3 — UI)**: Depends on Phase 2 (`CharacterTurnOverrides`, `TimeOfDayManuallySet`, `CurrentTimeOfDay`, `SceneContinueBatchSize`). T056 backend verification, then T057/T058/T059 UI tasks in parallel. UI can begin once backend contract from Phase 2 is stable.
- **Phase 9 (Polish)**: Depends on all previous phases.

### User Story Dependencies

| Story | Depends on | Can run parallel with |
|---|---|---|
| US1 (P1) | Phase 2 | — |
| US2 (P1) | Phase 3 (US1) | US3 (no shared infra beyond Phase 2) |
| US3 (P2) | Phase 2 | US2 (parallel via separate branches) |
| US4 (P2) | Phase 4 (US2) | US5, US6 (UI work, different files) |
| US5 (P3, UI) | Phase 2 (DTO only) | US1–US4 (different files) |
| US6 (P3, UI) | Phase 2 + T056 | US1–US4 (different files) |

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation, per the spec's Constitution Principle VII (Testability and Verification by Default).
- Models before services; services before call-site wiring; contract tests before implementation tests.
- Story complete before moving to next priority (the hard checkpoint at the bottom of each story phase).

### Parallel Opportunities

| Tasks / group | Why parallel-safe |
|---|---|
| Phase 2 T006–T017 (Models + DTOs + Interfaces) | All new files, no compile-time coupling |
| Phase 2 T007, T008, T009, T010, T011, T012, T013, T014, T015, T016, T017 | Distinct files, all marked [P] |
| Phase 3 T021, T022 | Independent test files (different test classes) |
| Phase 3 T023, T024 | Service impl + handler impl (different files); both depend only on Phase 2 contracts |
| Phase 5 T036, T037, T038 | Independent test files |
| Phase 5 T039, T040, T041 | Three independent helpers: scoring / narrative-summary / fingerprint; pure functions touching different code regions of `RolePlayEngineService.cs` — coordinate via separate commits but no cross-blocking |
| Phase 6 T047 | Independent test file |
| Phase 7 UI T052 | Independent of T053–T055 (different UI sections) |
| Phase 7 T054 (multi-rule add) | After T053 latch established; same file so strictly sequential in practice — marked [P] only ifn merged as part of T053 commit |
| Phase 8 UI T057, T058, T059 | Three independent workspace sections; can be developed against the stable backend contract from T056 |
| Phase 9 T060, T061 | Independent verification tasks |
| Phase 9 T066 | Independent doc update |

### Agent Assignment Slices

Disjoint slices to split work between 🟦 backend agent and 🟨 UI agent:

- **🟦 Backend agent**: All Phase 1 + Phase 2 tasks; Phase 3 (US1) all tasks; Phase 4 (US2) all tasks; Phase 5 (US4) all tasks; Phase 6 (US3) all tasks; Phase 7 only T051 (verification); Phase 8 only T056 (endpoint); Phase 9 all tasks.
- **🟨 UI agent**: Phase 7 (US5) T052, T053, T054, T055; Phase 8 (US6) T057, T058, T059. UI agent does NOT modify `.cs` files (other than the `.razor.cs` partial if one exists for `ScenarioEditor` or `RolePlayWorkspace` — and even then, coordinate with backend agent for state-binding property changes).

The 🟨 UI agent can start the moment Phase 2 completes (T018 checkpoint); the backend agent continues on Phases 3–6 in parallel. The two agents converge at Phase 9 (T064 manual smoke).

---

## Implementation Strategy (MVP First)

1. **MVP scope**: User Story 1 (location detection fix) + User Story 2 (location-gated availability). These two stories together deliver the open-world foundation that makes all downstream work meaningful. Ship MVP behind the released Tasks through Phase 4.
2. **Incr 2**: User Story 4 (AI actor selection) + User Story 3 (time-of-day gating). Delivers the user-facing selection improvement.
3. **Incr 3**: User Stories 5 + 6 (authoring UI + workspace controls). Quality-of-life polish; can ship in parallel with Incr 2 since UI work depends only on Phase 2 contracts.
4. **Polish**: Phase 9 builds + tests + smoke. Update agent context memory after the manual smoke test if any new build quirk surfaced.

---

## Format Validation

All 67 tasks strictly follow the checklist format `- [ ] [TaskID] [P?] [Story?] Description with file path`. Format components verified:

- **Checkbox**: Every task starts with `- [ ]` ✅
- **Task ID**: Sequential T001–T067 ✅
- **[P] marker**: Present only on parallelizable tasks ✅
- **[Story] label**: Present on user-story phase tasks only; absent from Setup/Foundational/Polish tasks ✅
- **File paths**: Every implementation task names exact file paths ✅
- **Agent tag**: Every task carries 🟦 [BE] or 🟨 [UI] for disjoint assignment ✅

**Summary**:

- Total tasks: **67**
- Per user story: US1 (12 tasks incl. tests), US2 (4), US3 (4), US4 (12 with tests), US5 (5), US6 (4 with backend-UI split).
- Setup: 5. Foundational: 15. Polish: 8 (T067 added for SC-007 manual UX measurement).
- Parallel opportunities identified: 6 task-groupings marked [P] covering tests, pure-function helpers, and independent UI sections.
- Independent test criteria: each user story phase carries its own checkpoint describing how to verify that story in isolation.
- Suggested MVP scope: User Stories 1 + 2 (Phase 3 + Phase 4) — the open-world location detection + gated availability MVP. All other stories are independently buildable on top.

---

## Remediation Log (Post-Analysis Fixes Applied 2026-07-14)

Following the post-`/speckit.analyze` review, the following concrete remediation edits were applied to spec.md and tasks.md:

| Issue ID | Severity | Fix Applied |
|---|---|---|
| H1 | HIGH | T028 rewritten as a concrete instruction: extract static helpers to `RolePlayCharacterStateMutator`; rejected-interface alternative recorded in body |
| H2 | HIGH | SC-007 updated to explicitly mark itself as manual UX measurement and reference the Phase 9 verification step; new T067 added to Phase 9 for the UI agent to perform and record the 2-minute timing check |
| M1 | MEDIUM | Renamed `ActorSelectionResult` → `ActorSelectionResponse` in spec.md Key Entities (matches data-model.md and contracts/) |
| M2 | MEDIUM | SC-001 expanded to enumerate the canonical ~10 transition-phrase corpus for "unambiguous location-change scenarios" |
| M3 | MEDIUM | FR-006 inline summary added listing scoring factors and their weight magnitudes (±1000, ±200–500, ±100 to −500, 0–200, 0–100, ±50) |
| M4 | MEDIUM | US2 acceptance scenario 4 clarified: "detected at that location" operationalises as `RolePlayScenePresenceHelper.IsActorInScene` / `TrueLocation` match |
| L1 | LOW | US6 acceptance scenario 2 unified to "on the next overflow continue" phrasing (matches SC-008) |
| L2 | LOW | T054 conflict-warning example rewritten with explicit affinity-type pairings (`Excluded` vs `Existing Required`) instead of the ambiguous generic "Evening rule" placeholder |