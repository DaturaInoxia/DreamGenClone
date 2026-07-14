# Research: Context-Aware Actor Selection

*Phase 0 output — all NEEDS CLARIFICATION items resolved*

---

## R1: LLM Service Pattern Reference (`SemanticEventInferenceService`)

**Decision**: New LLM-backed services (`LocationDetectionService`, `ActorSelectionService`) mirror `SemanticEventInferenceService` exactly.

**Evidence**: `DreamGenClone.Web/Application/RolePlay/SemanticEventInferenceService.cs`

- **Constructor deps**: `ICompletionClient completionClient`, `IModelResolutionService modelResolutionService`, `ILogger<T>? logger = null`
- **No-failure fallback contract**: `try { resolved = await _modelResolutionService.ResolveAsync(AppFunction.X, cancellationToken: ct); } catch (ModelResolutionException ex) { _logger?.LogWarning(ex, "..."); return new Result { Success = false, ErrorMessage = ex.Message, ... }; }` (lines 31–47)
- **JSON parsing**: `JsonSerializerOptions` instance: `new JsonSerializerDefaults.Web` with `PropertyNameCaseInsensitive = true`; `ExtractJsonObject(modelOutput)` strips chain-of-thought preamble by returning the outermost `{ ... }` substring when the model prefaces JSON with prose
- **LLM call**: `output = await _completionClient.GenerateAsync(systemMessage, userMessage, resolved, cancellationToken);` — `resolved` carries `ProviderBaseUrl`, timeout, API key, model identifier; the completion client handles its own timeout (`ProviderTimeoutSeconds` on `ResolvedModel`)
- **Logging**: `LogInformation` for REQUEST (system + user prompt) and RESPONSE (raw output) with `SessionId`, `InteractionId`, `Model`, `ElapsedMs`; `LogError` for FAILED/PARSE-FAILED with raw output
- **Throw on parse failure**: parse issues throw `InvalidOperationException`, surfaced by the caller (the background worker catches and marks the job failed)

**Alternatives considered**:
- Returning `null` on parse failure → rejected because it doesn't distinguish "no event" from "bad model output"; fails the repo's fail-fast contract
- Wrapping in a "best effort" retry loop at this layer → rejected; retries belong in the background job handler, not the inference service

---

## R2: Background Job Queue Pattern (`SemanticBackgroundJobQueue` + `IBackgroundJobHandler`)

**Decision**: Location detection enqueues a new job type via the existing `SemanticBackgroundJobQueue` and is processed by the existing `SemanticBackgroundJobWorker` (which dispatches by `JobType` to the right `IBackgroundJobHandler`).

**Evidence**:

- `DreamGenClone.Web/Application/BackgroundJobs/SemanticBackgroundJobQueue.cs`
  - `Enqueue(string jobType, string payloadJson, string? dedupeKey = null)` returns `bool` (false if dedupe key already active for this job type — caller does NOT need to retry)
  - Dedupe key is normalized as `{jobType}:{dedupeKey}` — multiple job types with the same `dedupeKey` don't collide
  - `ReleaseDedupeKey` is invoked by the worker's `finally` block when the job completes or fails
- `DreamGenClone.Web/Application/BackgroundJobs/SemanticBackgroundJobWorker.cs`
  - Concurrency cap is read from `FunctionDefault.MaxConcurrentJobs` for `RolePlaySemanticAnalysis` (default 2). New job types share this cap unless they implement their own rate control (acceptable for V1 — location detection volumes are low, one job per interaction per session)
  - The worker resolves `IEnumerable<IBackgroundJobHandler>` from the DI scope and matches by `JobType` (`string.Equals(x.JobType, capturedJob.JobType, StringComparison.OrdinalIgnoreCase)`)
- `DreamGenClone.Web/Application/BackgroundJobs/IBackgroundJobHandler.cs`
  - `string JobType { get; }` and `Task HandleAsync(BackgroundJobEnvelope job, CancellationToken ct)`
- `DreamGenClone.Web/Application/RolePlay/SemanticInteractionAnalysisJobHandler.cs` (reference implementation)
  - Resolves `RolePlayFeatureFlagsOptions.EnableSemanticInference`; **returns early with Information log if disabled** (lines 36–42) — same pattern applies to location detection gating via `RolePlayDecisionOptions.EnableLocationServices`
  - Deserializes payload `{ SessionId, InteractionId, CharacterId }` (handler loads full state from DB — keep new location job payload minimal too: `{ SessionId }` so handler loads fresh V2 state after the user's response is persisted)
  - Idempotent: skips processing if an existing analysis row is already `Complete` — new location job should similarly skip if `CurrentSceneLocation` was updated after the triggering interaction
- `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` — second reference: dedupe key pattern `$"enc-summary:{sessionId}:{cycleIndex}"`; new location job uses `$"location:{sessionId}"` (one in-flight location job per session — newer interactions supplant older ones via dedupe, next turn reads fresh state)

**Dedupe key design**: `$"location:{sessionId}"`. If a second interaction arrives before the first job finishes, `Enqueue` returns `false` (skips duplicate), the second interaction's location state carries over to the next turn. Acceptable: location changes are slow and the one-turn lag is already part of the contract.

**Alternatives considered**:
- Per-interaction dedupe key `$"location:{sessionId}:{interactionId}"` → rejected — would cause duplicate concurrent detection jobs for the same session and over-write `CurrentSceneLocation` with stale values when older jobs finish after newer ones
- Dedicated worker for location jobs → rejected — the existing semantic worker already handles multi-type dispatch and the location job has the same lifecycle shape (DB write, no streaming progress)

---

## R3: LLM Service Registration Shape

**Decision**: Register services in `Program.cs` alongside `SemanticEventInferenceService`; register the new job handler alongside `SemanticInteractionAnalysisJobHandler` + `EncounterSummaryJobHandler`.

**Evidence**:
- `DreamGenClone.Web/Program.cs:87-91`:
  ```
  builder.Services.AddScoped<IRolePlayEngineService, RolePlayEngineService>();
  ...
  builder.Services.AddScoped<ISemanticEventInferenceService, SemanticEventInferenceService>();
  ```
- `DreamGenClone.Web/Program.cs:208-209`:
  ```
  builder.Services.AddScoped<IBackgroundJobHandler, SemanticInteractionAnalysisJobHandler>();
  builder.Services.AddScoped<IBackgroundJobHandler, EncounterSummaryJobHandler>();
  ```
- Worker dispatch resolution (`SemanticBackgroundJobWorker.cs:52-54`):
  ```
  var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IBackgroundJobHandler>>();
  var handler = handlers.FirstOrDefault(x => string.Equals(x.JobType, capturedJob.JobType, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException($"No handler registered for semantic job type '{capturedJob.JobType}'.");
  ```

**New registrations**:
- `builder.Services.AddScoped<ILocationDetectionService, LocationDetectionService>();`
- `builder.Services.AddScoped<IActorSelectionService, ActorSelectionService>();`
- `builder.Services.AddScoped<IBackgroundJobHandler, LocationDetectionJobHandler>();`

**Alternatives considered**:
- Singleton lifetime for `LocationDetectionService` → rejected because it depends on `ICompletionClient` (potentially scoped) and `IModelResolutionService` (depends on DB-backed repos that must be scoped)
- New background queue type → rejected — the existing queue handles multi-type dispatch cleanly

---

## R4: Replacement Strategy for `DetectSceneLocationSignalAsync` and Regex Helpers

**Decision**: Replace the synchronous `await DetectSceneLocationSignalAsync(...)` call in the adaptive pipeline (~L3891 of `RolePlayEngineService.cs`) with a fire-and-forget `EnqueueLocationDetectionJob(session)`; delete the regex-based helpers entirely. Keep `EnsureCharacterLocationRows`, `UpsertTrueLocation`, `UpdatePerceivedLocationsFromTruth` because they're used to mutate the in-memory state graph that `IsActorInScene` and `RolePlayContinuationService` consume.

**Evidence** (verified line numbers in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`):

- **Warm-up path** (L3104–3108): `if (!_enableLocationServices) { ClearLocationState(v2State); }` — this guard stays untouched; with `EnableLocationServices = true` in appsettings, the LLM path becomes the only detection route
- **Call site** (L3891): `var sceneLocationSignal = _enableLocationServices ? await DetectSceneLocationSignalAsync(session, v2State, cancellationToken) : null;` — replace this single line with `EnqueueLocationDetectionJob(session);` (returns void; the existing pipeline path that reads `sceneLocationSignal` afterwards must tolerate a one-turn lag for `v2State.CurrentSceneLocation`)
- **Detection method** (L7396–7498): `DetectSceneLocationSignalAsync` — DELETE entirely
- **Regex helpers**:
  - L46–91: `GenericLocationNames` static array and its initializer — DELETE
  - L7686: `MatchScenarioLocation` — DELETE
  - L7708: `MatchGenericLocation` — DELETE
  - L7726: `ContainsWholeWord` — DELETE (only the location-detection call sites use it; verify with a usage grep before deleting)
  - Before deleting `ContainsWholeWord`: L7648 and L7662 also reference `ContainsWholeWord`. Need to confirm whether those call sites (a persona-in-proximity check at L7648 and L7662) belong to other methods that should retain equivalent behavior — see R8 below.
- **Kept helpers**:
  - L7507 `ClearLocationState` — preserves the warm-up "no detection" path
  - L7514 `EnsureCharacterLocationRows` — called from the new job handler after loading V2 state
  - L7535 `UpsertTrueLocation` — called from the job handler after LLM detection
  - L7557 `UpdatePerceivedLocationsFromTruth` — called by job handler to refresh perceptions

**Alternatives considered**:
- Keep regex as a "low-quality" fallback when the LLM fails → **explicitly forbidden by the no-fallback rules**. LLM failure means `Success = false`, `CurrentSceneLocation` unchanged, explicit warning logged.
- Inline the LLM call synchronously at L3891 → rejected — would add 2–5 s of latency per interaction. The B-050 architectural review (§11 fix #1 in the design spec) flagged this exact issue.

---

## R5: Insertion Point for `ResolveAvailableCharacters` and Actor Selection Reorder

**Decision**: Insert the new pipeline AFTER the B-056 aftermath early-return guard (L2262–2313) and AFTER the `GetAllowedActors` call (L2317). Replace the `OrderByDescending(x => x.InScene).ThenBy(x => x.LastSeenIndex).ThenBy(x => x.ScenarioOrder)` block at L2396–2404 (the `ordered = eligibleCharacterNames.Select(...).OrderByDescending(...).ThenBy(...).ThenBy(...).Select(x => x.Name).ToList();` block) with the new candidate resolver + scorer + LLM reorder.

**Evidence** (verified in `RolePlayEngineService.cs`):

- L2262–2313: B-056 aftermath early return — **preserved unchanged**. This guard returns `[wife, persona]` and bypasses the candidate pipeline; the new pipeline only runs when `CurrentTimeSkipPhase != AftermathCoupleInteraction`.
- L2316: `var actors = new List<OverflowActorCandidate>();`
- L2317: `var autoAllowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode, explicitSelection: false).ToHashSet();`
- L2360–2371: builds `sceneCharacterNames`, excludes persona, dedupes
- L2383–2391: builds `recentActors` (last 6 NPC/Custom interactions)
- L2393–2395: `currentSceneLocation = _enableLocationServices ? session.AdaptiveState.CurrentSceneLocation : null;`
- L2403–2413: `eligibleCharacterNames = sceneCharacterNames.Where(...).(OtherMan opening exclusion via totalInteractions < 6 + role == "OtherMan").ToList();`
- L2415–L2424: **`ordered = eligibleCharacterNames.Select(...).OrderByDescending(x => x.InScene).ThenBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex).ThenBy(x => x.ScenarioOrder).Select(x => x.Name).ToList();`** — this is the exact block being replaced
- L2428–2436: `if (autoAllowedActors.Contains(ContinueAsActor.Npc))` — adds `OverflowActorCandidate(ContinueAsActor.Npc, name, ...)` per ordered name
- L2438–2470: persona insertion rules (first 6 / even / odd) — **preserved unchanged**
- L2472–2485: empty-list fallback to `ResolveDefaultContinueActor` — **preserved unchanged**

**New pipeline shape (replacing L2415–2424)**:

```
1. ResolveAvailableCharacters(session, scenarioCharacters, currentSceneLocation, v2State)
     → List<AvailableCharacter>  (location + affinity + time-of-day gates)
2. For each AvailableCharacter:
     score = ScoreActorForAutoSelection(availableCharacter, v2State, recentActors, session.CharacterTurnOverrides)
3. Build ActorSelectionRequest { narrative summary, candidates with scores, themes, events, phase, location, time }
4. result = await _actorSelectionService.SelectActorsAsync(request, ct)
     → OrderedNames (LLM) | cached ordering | scoring order (Source = Scoring | Fallback | Cache | LLM)
5. ordered = MapOrderedNamesToCandidates(result.OrderedNames, availableCharacters)
```

The `GetAllowedActors` filter at L2317 stays put — it filters `ContinueAsActor` flag coverage (whether `Npc` and/or `You` are eligible at all). The new pipeline refines which NPCs speak within that broader gate.

**Alternatives considered**:
- Move B-056 check downstream of `ResolveAvailableCharacters` → rejected because it would re-introduce [wife, husband] candidates into the resolver; the B-056 branch is by-design a hard override
- Run `GetAllowedActors` AFTER the candidate resolver → rejected because it changes existing behavior (currently `Npc` candidates wouldn't even be considered if the mode disallows Npc), and matches the design spec's explicit preservation requirement

---

## R6: V2 Adaptive State Schema Migration Pattern (`EnsureAdaptiveStateSchemaAsync`)

**Decision**: Add three new idempotent migrations inside `RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync` and one optional startup-time one-time `ActorName` backfill.

**Evidence** (`DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs:891–1010+`):

- The pattern for each column:
  ```csharp
  if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "<ColumnName>", cancellationToken))
  {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN <ColumnName> <SQLiteType>";
      await cmd.ExecuteNonQueryAsync(cancellationToken);
  }
  ```
  (Existing examples: `CurrentSceneLocation TEXT NULL` L903, `PhaseOverrideFloor TEXT NULL` L910, `TurnsInCurrentBeat INTEGER NOT NULL DEFAULT 0`.)

**New migrations** (insert after the `CurrentBeatCode` / `TurnsInCurrentBeat` block per the established alphabetical-ish ordering, or near the location columns):

- `RolePlayV2AdaptiveStates.CurrentTimeOfDay TEXT NULL` (stores `TimeOfDay` enum name; null = undetected)
- `RolePlayV2AdaptiveStates.TimeOfDayManuallySet INTEGER NOT NULL DEFAULT 0` (boolean flag)
- `RolePlayV2SemanticEvents.ActorName TEXT` (nullable, backfilled separately)

**Load and save wiring** (inside `RolePlayStateRepository.cs`):

- `LoadSemanticEventsAsync` SELECT must include `ActorName`; column index added to the reader mapping; null is preserved (existing records have null until backfill)
- `SaveSemanticEventsAsync` INSERT (L496–512) must add the `ActorName` parameter; `RolePlayInteractions.ActorName` is already the source for new events — wire it via the caller or a backfill step (see R7)
- `LoadAdaptiveStateAsync` and `SaveAdaptiveStateAsync` must include `CurrentTimeOfDay` and `TimeOfDayManuallySet` in their INSERT/SELECT along with the other adaptive-state columns

**Alternatives considered**:
- Dedicated migration runner project → rejected — the repo's established pattern is in-place idempotent ALTERs in `EnsureAdaptiveStateSchemaAsync`
- EF Core migrations → not used in this repo

---

## R7: `ActorName` Backfill Strategy

**Decision**: Add a one-time idempotent C# startup migration that runs after `EnsureAdaptiveStateSchemaAsync` adds the `ActorName` column.

**Evidence**:

- `RolePlayInteractions` table: every `RolePlayInteraction` row already has `ActorName` (see `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs:11` — `public string ActorName { get; set; } = string.Empty;`). The relationship is `RolePlayV2SemanticEvents.InteractionId = RolePlayInteractions.Id`.
- `EnsureAdaptiveStateSchemaAsync` runs on every V2 load/save (L218, L517, L891). The natural hook point for the backfill is **right after the column-add step, gated by a `HasColumnAsync`-style check on a marker column like `ActorNameBackfilled`**, or alternately gated by "any row exists with `ActorName IS NULL`".
- Backfill uses a single `UPDATE ... WHERE ActorName IS NULL` joined to `RolePlayInteractions`:
  ```sql
  UPDATE RolePlayV2SemanticEvents
  SET ActorName = (
      SELECT i.ActorName
      FROM RolePlayInteractions i
      WHERE i.Id = RolePlayV2SemanticEvents.InteractionId
  )
  WHERE ActorName IS NULL
    AND EXISTS (
      SELECT 1 FROM RolePlayInteractions i WHERE i.Id = RolePlayV2SemanticEvents.InteractionId
    );
  ```
- Run at app startup (in `Program.cs` after the DI build, or via a hosted `IHostedService` that runs once). Idempotency comes from the `WHERE ActorName IS NULL` guard — re-runs are no-ops.

**New-event population path**:

1. `RolePlayAdaptiveStateService.CreateSemanticEventRecord` (L1036, the `new SemanticEventRecord { ... }` literal) — add `ActorName = <the actor name>` from the inference request. The caller already threads `ActorName` through `SemanticEventInferenceRequest.ActorName` (`SemanticEventInferenceModels.cs:11`).
2. `RolePlayStateRepository.SaveSemanticEventsAsync` — add `ActorName` parameter to the INSERT.
3. `RolePlayEngineService.cs:8080` (the second `new SemanticEventRecord { ... }` in the V1→V2 migration path) — also wire `ActorName` from the corresponding interaction row where the mapping is built.

**Alternatives considered**:
- Lazy backfill on first read → rejected — would mix disk and computation work in the read path; harder to reason about test determinism; no clear "one-time" boundary
- Manual ops PowerShell script → rejected by spec (FR says application is local-first with zero manual ops); leaves existing rows stuck if the script isn't run
- SQL-only trigger → `Microsoft.Data.Sqlite` has limited trigger support and the migration path doesn't use them elsewhere; adding a trigger violates "the repo's established pattern"

---

## R8: `ContainsWholeWord` Usage Outside Location Detection

**Decision**: Audit callers of `ContainsWholeWord` before deleting it; only delete it if location detection is the sole caller. If other call sites exist (e.g., L7648 / L7662 around a persona proximity check), either move `ContainsWholeWord` into the new `ActorSelectionService` (if it's used there) or leave it as a private utility inside the host service.

**Evidence**: Confirmed callers in the same file:
- L7426, L7429: `DetectSceneLocationSignalAsync` — deleted
- L7648: persona proximity check in a separate method (need to verify which method)
- L7662: same method as L7648
- L7699, L7715, L7717: MatchScenarioLocation / MatchGenericLocation — deleted

**Audit step** in T-phase tasks:
1. Read the surrounding method at L7630–L7700 to identify what it does
2. Decide inline based on whether the method relates to actor selection (move to `ActorSelectionService`) or to be deleted (delete alongside the rest)
3. Cross-check with file_search and usages of `ContainsWholeWord` across the rest of the repo

**Alternatives considered**:
- Blindly delete and let the build break the dead code → rejected; the build will throw red, but the surrounding method may have unrelated persona logic that would lose functionality
- Move `ContainsWholeWord` to a shared helper → only if needed by `ActorSelectionService`; otherwise leave it inline with the rest

---

## R9: Scoring Weights — Per-design, Internal Constants

**Decision**: Implement scoring with `private const double` values exactly as in the design's spec table. They are NOT user-tunable config (no `IOptions<T>`, no UI), because they are implementation details of the candidate-ranking algorithm — the LLM is the user-facing decision-maker.

**Evidence**: The repo's no-fallback rules (`copilot-instructions.md`) forbids hardcoded RP behavior defaults that bypass user-tunable config — BUT scoring weights aren't RP behavior values (they're internal weighting of deterministic criteria to feed LLM rankings). The `RolePlayDecisionOptions` config files in `DreamGenClone.Web/appsettings.json` already follow the convention: only user-facing decisions appear there (e.g., `SuppressNarrativeAfterDecision`, `EnableLocationServices`, `EnableDecisionPrompts`). Adding configurable scoring weights would be over-engineering and would NOT satisfy any user requirement.

**Weights (from the design's spec, §3 Phase 6)**:

| Factor | Weight | Range |
|---|---|---|
| LocationMatch (in-scene) | +1000 | 0 or 1000 |
| Affinity Required + match | +500 | 0 or 500 |
| Affinity Preferred + match | +200 | 0 or 200 |
| Affinity Excluded + at location | −1000 | 0 or −1000 (hard penalty) |
| Affinity TimeOfDay mismatch | −500 | 0 or −500 |
| Affinity TimeOfDay match | +100 | 0 or 100 |
| Recency | 0–200 | Never = 200, >6 = 180, 4–6 = 120, 2–3 = 60, last = 0 |
| `CharacterTurnOverride.ResponsePriority` | +0 to +100 | additive |
| `CharacterTurnOverride.PreferredPosition` | +50 (First) / −50 (Last) | scoring hint, not hard sort |

**Alternatives considered**:
- Make weights configurable → rejected — would surface implementation details as user-facing config; the LLM is the user-facing decision-maker
- Make weights `static readonly` instead of `const` → no functional difference; `const` is the established convention elsewhere in `RolePlayEngineService`

---

## R10: Cache Fingerprint & Invalidation

**Decision**: Use the composite fingerprint `{CurrentPhase}|{CurrentSceneLocation}|{sortedCharacterNames}|{CurrentTimeOfDay}` (string join) as the cache key for the LLM-ordered actor selection.

**Evidence** (clarification from the spec session Q2): "Composite fingerprint of narrative phase + current location + current time-of-day + sorted set of available characters. Excludes per-character stat deltas to avoid over-invalidation."

- `CurrentPhase` — changes infrequently, but transitions invalidate (e.g., `BuildUp` → `Approaching`)
- `CurrentSceneLocation` — changes when LLM background job detects a new location; one-turn lag accepted
- `sortedCharacterNames` — derived from `ResolveAvailableCharacters` output; changes only when a character enters/exits via affinity or location, NOT when a stat increments. This combines with the affinity / time-of-day gates already computed in `ResolveAvailableCharacters`, so it implicitly invalidates on affinity changes too.
- `CurrentTimeOfDay` — keyword-detected or manually set

**Stale check**: `LastContextFingerprint` lives on `RolePlaySession` (transient, not persisted — set to null after the engine reloads the session from DB). On `ResolveSceneContinueActorsAsync`:
1. Build the current fingerprint
2. If equals `LastContextFingerprint` and `LastActorOrdering != null`: rotate `LastActorOrdering` by recency (move the most-recently-spoken to the end), return with `Source = Cache`, no LLM call
3. Otherwise: call `_actorSelectionService.SelectActorsAsync`, update `LastContextFingerprint` and `LastActorOrdering`, return with `Source = LLM` (or `Scoring`/`Fallback` per the service)

**Alternatives considered**:
- Include per-character stat values in fingerprint → rejected — causes over-invalidation (any Desire increment triggers a re-query); the LLM's variance in actor choice is captured in the cached ordering's recency rotation, not in re-querying
- Include recent semantic events → rejected — semantic events only matter once the actor selection LLM sees them; the LLM call should explicitly receive the last ~3 events in its prompt, but the fingerprint should only invalidate when the candidate set itself changes

---

## R11: Affinity Matching — Multiple Entries Per (Character, Location)

**Decision**: Allow multiple `CharacterLocationAffinity` entries per (character, location), each with a distinct or null `TimeOfDay`. Conflict resolution: `Excluded > Required > Preferred`, then exact-time-match over wildcard (null TimeOfDay applies as fallback).

**Evidence** (clarification from the spec session Q1): "Multiple entries per (character, location) are allowed, each with a distinct or null TimeOfDay; conflicts resolved by Excluded > Required > Preferred precedence, then exact-time match over wildcard."

**Resolution algorithm (in `ResolveAvailableCharacters`)**:

1. Filter `Character.LocationAffinities` to entries where `LocationName == CurrentSceneLocation` (case-insensitive)
2. From those, find entries where `TimeOfDay == CurrentTimeOfDay` (exact match) → these are "specific rules"
3. If no exact-match entries exist, fall back to entries with `TimeOfDay == null` → these are "wildcard rules"
4. If both specific and wildcard rules apply for the same character, prefer the specific rules
5. From the applicable rules:
   - If any rule is `Excluded` → character excluded (highest precedence)
   - Else if any rule is `Required` → character required
   - Else if any rule is `Preferred` → character preferred (hint to LLM)
   - Else → no affinity, fall back to location-detection (`IsInScene` from `RolePlayScenePresenceHelper`)

**Alternatives considered**:
- Single affinity entry per (character, location) with `TimeOfDay` as a list → rejected (clarification B); less flexible than permitting rule-shape changes per time slot
- Last-writer-wins ordering → rejected; explicit precedence avoids ambiguity in UI-exposed data

---

## R12: Razor Editing Constraints (UI work)

**Decision**: Follow `.github/instructions/razor-editing.instructions.md` strictly when modifying `ScenarioEditor.razor` and `RolePlayWorkspace.razor`. The instructions require full-context reads, anti-hallucination checks for tag helpers, micro-step edits, and diff-only verification.

**Evidence**: `copilot-instructions.md` mandates these rules for all `.razor` edits in this repo.

**Key UI changes**:

- `ScenarioEditor.razor`: per-character affinity editor (Required/Preferred/Excluded/None dropdown + TimeOfDay dropdown) for each scenario location; default time-of-day dropdown in scenario settings
- `RolePlayWorkspace.razor`: time-of-day display (Auto indicator + manual override dropdown); per-character overrides panel (auto-participate checkbox, priority 0–100 slider, position Auto/First/Last); batch size slider (1–6)
- Both files already exist and are large — micro-step edits only; no full-file rewrites

**Alternatives considered**:
- New Razor component for affinity editor → could be extracted as a sub-component under `DreamGenClone.Web/Components/Scenarios/CharacterLocationAffinityEditor.razor` to avoid bloating `ScenarioEditor.razor`. Decision: extract a sub-component IF the editor section exceeds ~80 lines after the first pass. Default: inline in `ScenarioEditor.razor` to keep the per-character UI cohesive with existing character sections.

---

## R13: `EnableLocationServices` Config Toggle

**Decision**: Flip `EnableLocationServices` from `false` to `true` in both `DreamGenClone.Web/appsettings.json:97` and `DreamGenClone.Web/appsettings.Development.json:64`. The `_enableLocationServices` field defaults to `true` if missing (`RolePlayEngineService.cs:313`), so the toggle already exists in `RolePlayDecisionOptions`; we're flipping the stored value, not introducing a new config knob.

**Evidence**: `_enableLocationServices = rolePlayDecisionOptions?.Value.EnableLocationServices ?? true;` (L313) — already in DI; we only change the JSON values.

**Alternatives considered**:
- Introduce a new `EnableLlmLocationDetection` flag distinct from `EnableLocationServices` → rejected; introduces a confusing second toggle that maps to the same thing in code, with the regex path now gone. `EnableLocationServices` now means "LLM detection active" rather than "regex detection active" — the existing flag's semantic intent is correctly preserved.

---

## R14: `DefaultStartingLocationId` Seeding (Existing Field, Currently Unused)

**Decision**: In `CreateSessionAsync`, when `scenario.DefaultStartingLocationId` is non-null, resolve the matching `Location.Name` from `scenario.Locations` and set `session.AdaptiveState.CurrentSceneLocation = locationName` as the seed.

**Evidence**:
- `DreamGenClone.Web/Domain/Scenarios/Scenario.cs:105`: `public string? DefaultStartingLocationId { get; set; }` — already a scenario property, but never assigned to the session in `CreateSessionAsync`
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:404–410`: the existing block where `scenario.DefaultPersonaPerspectiveMode`, `scenario.DefaultThemeProfileId`, etc. are copied to the session. Add `if (scenario.DefaultStartingLocationId is not null) { var loc = scenario.Locations.FirstOrDefault(l => l.Id == scenario.DefaultStartingLocationId); if (loc?.Name is not null) session.AdaptiveState.CurrentSceneLocation = loc.Name.Trim(); }` in the same block.
- Same block is where `scenario.DefaultTimeOfDay` will be wired to `session.AdaptiveState.CurrentTimeOfDay` (new property).

**Alternatives considered**:
- Seed `CurrentSceneLocation` from `Location.Id` instead of `Location.Name` → rejected; `CurrentSceneLocation` is consistently used as a display-name string elsewhere (e.g., `RolePlayScenePresenceHelper.cs:30` uses it as a display name in `TrueLocation` comparison), so seeding from `Name` matches the existing contract

---

## R15: Verification — Build, Test, and Manual Smoke

**Decision**: Verify with `dotnet build` (whole solution), the `dotnet test` xUnit suite (new tests added under `DreamGenClone.Tests/RolePlay/`), plus one manual smoke test for the open-world flow.

**Build command** (from the workspace's existing tasks):
- `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` (or the helper `build-web` task)

**Tests to add**:
- `LocationDetectionServiceTests` — LLM success / failure / no-model configured / per-character locations / success=false on parse fail / unhandled completion client exception (test that the service rethrows — per R1, the goal is "fail explicit, leave state unchanged"; the caller's worker catches and logs)
- `TimeOfDayDetectionTests` — keyword presence, word boundary, manual override blocks auto-detect, "Auto" resumes
- `CharacterAvailabilityResolverTests` — Required/Excluded/Preferred/None + time-of-day gating + multi-affinity conflict + backdrop phase bypass
- `ActorScoringTests` — each weight term + recency bands + ResponsePriority additive + PreferredPosition hint + collisions between factors
- `ActorSelectionServiceTests` — Source = LLM / Cache / Scoring / Fallback
- `SemanticEventActorNameBackfillTests` — first run populates nulls, second run is no-op, missing `InteractionId` rows left untouched
- `ResolveSceneContinueActorsIntegrationTests` — full open-world flow with seeded scenario + scripted location/time shifts

**Manual smoke**: Configure a scenario with Home (Wife Required), Beach (Wife Preferred, Lifeguard Required), Neighbor (Home + Evening Required). Start at Home, narrate move to Beach, click overflow continue — verify Lifeguard appears, Neighbor disappears. Change time to Evening, navigate back Home — verify Neighbor reappears.

**Alternatives considered**:
- Skip integration tests for V1 → rejected — the constitution requires test coverage for major call paths; `ResolveSceneContinueActorsAsync` is the entry point users click dozens of times per session