# 004 — Phase-Stamp Mismatch: Interactions Stamped BuildUp After Committed Transition

**Created:** 2026-08-13
**Feature:** RP phase transition / interaction `NarrativePhaseAtCreation` stamping.
**Status:** Root cause under investigation — no fix applied yet.

## Report

Session `4c676f02-7bc8-453d-824a-03e0f10f0c62` (Dean). User reported interaction `0d9f9a1b-a928-4ae8-b18e-bcd24c0235e1` (idx 21) is stamped `BuildUp` but should be `Committed`.

Verified via DB:
- `RolePlayV2AdaptiveStates` for this session: `CurrentPhase = Committed`, `TurnCountInPhase = 2`, `TurnsSinceCommitment = 0`, `UpdatedUtc = 2026-08-14T02:09:18`.
- All interactions in the session are stamped `Opening`/`BuildUp` — **zero** `Committed`.

## Analysis

### Phase transition timeline (`RolePlayV2PhaseTransitions`, ordered by `OccurredUtc`)

| OccurredUtc | From | To | Trigger | Reason |
|---|---|---|---|---|
| 2026-08-14T01:43:36 | Opening | BuildUp | Threshold | OPENING_TO_BUILDUP |
| 2026-08-14T02:01:17 | BuildUp | Committed | Threshold | BUILDUP_TO_COMMITTED |

### Correction to earlier analysis — payload uses **camelCase** JSON keys

Initial query `b082_ixn_phases.sql` used `$.Interactions` / `$.NarrativePhaseAtCreation` and returned `(no rows)`. The session payload's top-level keys (`b082_topkeys.sql`) are **camelCase** (`id`, `title`, `interactions`, `characterPerspectives`, ...). Re-running with `$.interactions[*].narrativePhaseAtCreation` gives the real picture.

### Actual interaction phase stamps (`b082_ixn_camel.sql`)

The persisted payload ends at the `02:01:17.2251303Z` Narrative interaction (the one that sat on the BuildUp→Committed boundary). 27 rows total:

| Phase value | Earliest CreatedAt | Latest CreatedAt | Count |
|---|---|---|---|
| `0` (Opening) | 01:21:17.93 | 01:37:38.95 | 9 |
| `1` (BuildUp) | 01:43:55.03 | **02:01:17.22** | 17 |
| `null` (Instruction) | 01:48:03.49 | 01:48:03.49 | 1 |
| `2` (Committed) | — | — | **0** |

The LAST persisted interaction is at `02:01:17.2251303Z` — same instant as the BuildUp→Committed transition record (`02:01:17.2598720Z`). So the transition fired **immediately after** that Narrative interaction's pipeline save.

### The 5 "missing" post-transition interactions

The `b082_prepared.sql` log query reported 5 `InteractionPrepared` events at 02:04:02, 02:04:54, 02:05:48, 02:06:14, 02:06:37. **None of their IDs exist in the persisted payload** (`b082_missing_ixn.sql` returned `(no rows)`). These interactions were prepared/in-flight but the session row was last saved at `02:09:14` (`Sessions.UpdatedUtc`) / `02:09:18` (`RolePlayV2AdaptiveStates.UpdatedUtc`). Either:
- (a) those 5 interactions were never appended to the in-memory session list and saved, OR
- (b) the session payload was overwritten by a save that didn't include them.

`RolePlayV2AdaptiveStates` for this session: `CurrentPhase = Committed`, `TurnCountInPhase = 2`, `TurnsSinceCommitment = 0`, `UpdatedUtc = 2026-08-14T02:09:18`. So the V2 row advanced to Committed, but the interaction payload never gained Committed-stamped rows.

### Earlier hypothesis (DISPROVEN)

Earlier I believed the 5 interactions existed but were stamped BuildUp. They are not in the payload at all. The "all interactions stamped Opening/BuildUp" statement was comparing the wrong thing — the payload simply doesn't contain the post-transition ones.

### Stamping code path

`RolePlayContinuationService.cs` (~line 296):

```csharp
NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
```

`git blame` on this line: last changed by commit `59e8f7c2` (2026-07-13). Not touched by any recent commit. Other stamp sites: `InteractionRetryService.cs:307`, `RolePlayContinuationService.cs:401,476`, `RolePlayEngineService.cs:1619`, `RolePlayBranchService.cs:138,153` — all read `session.AdaptiveState.CurrentPhase` at construction time.

`session` arrives via `SubmitPromptAsync` → `GetSessionAsync(submission.SessionId)` → `SessionService.LoadRolePlaySessionAsync`, which sets:

```csharp
session.AdaptiveState = v2State;   // loaded from RolePlayV2AdaptiveStates
```

So on a **fresh** load, `session.AdaptiveState.CurrentPhase` reflects the V2 persisted value.

### Git archaeology — the regression window

User report: "sessions created yester[day] worked." Working session `85de44bb-aae7-42e7-ade8-9dbca312c8e5` updated `2026-08-13T04:50:38Z` = Aug 12 12:50am EDT, cycled through `BuildUp|Reset|Climax|Approaching|Committed|BuildUp` correctly. Failing session `4c676f02` started `2026-08-14T01:21:17Z` = Aug 13 9:21pm EDT.

Commits in the regression window (`git log --since="2026-08-12 23:00" --until="2026-08-13 22:00"`):

| Commit | Date (EDT) | Message |
|---|---|---|
| `fd5698c` (HEAD) | 2026-08-13 19:59 | i dont know |
| `fd65c10` | 2026-08-13 19:57 | Merge branch '079-attractiveness-tier-catalog' into development |
| `cb8b648` | 2026-08-13 19:52 | Backlog: B-079, B-080, B-081 done |
| `5282402` | 2026-08-13 19:45 | features complete |

All four are ~1.6h before the failing session. They touched:
- `fd5698c`: removed `GuaranteeParticipationSeats` from `RolePlayEngineService.cs` (participation, not phase).
- `fd65c10` (merge): added `GuaranteeParticipationSeats`, added Wife willingness scoring in `RolePlayAdaptiveStateService.cs`, added `RPThemeSemanticEventMappings.Description` column, added semantic event descriptions union in `SemanticInteractionAnalysisJobHandler.cs`.
- `5282402`: `SqlitePersistence.cs` (another `Description` migration), snapshot DB replaced (10MB → 18MB), Wife willingness, semantic job handler.

**None of these commits touch phase-stamping, `HydrateV2State`, `LoadAdaptiveStateAsync` SELECT column order, or `NarrativePhaseAtCreation` assignment.** The phase-stamping pipeline is unchanged since `968b176` (2026-07-26).

### Phase persistence timing (verified)

`DIAG:PipelineSave` is a `_logger.LogWarning` — it does NOT appear in `RolePlayDebugEvents`. So timing must be inferred from:
- `RolePlayV2PhaseTransitions.OccurredUtc = 02:01:17.2598720Z` (transition fired)
- `RolePlayV2AdaptiveStates.UpdatedUtc = 02:09:18.2803704Z` (last V2 save)
- `Sessions.UpdatedUtc = 02:09:14.0967232Z` (last session save)

The `AdaptiveStateUpdateSkipped` debug events were emitted on each interaction (the semantic-engine path, `EnableAdaptiveStateUpdates=false` is intentional/permanent — see comment at `RolePlayEngineService.cs:3476`). This is expected, not a bug.

## Root cause (revised)

The bug is **not** stale phase in the stamping line. The stamping line reads `session.AdaptiveState.CurrentPhase` correctly. The problem is the **interaction payload itself doesn't contain the post-transition interactions** — they were prepared (debug events written) but never persisted, OR the V2 advance to Committed happened in a pipeline run that didn't include the newly-built interactions in its save set.

### Current working root-cause candidates

1. **Interaction never persisted.** The 5 `InteractionPrepared` events fired (in `RolePlayContinuationService.ContinueAsync` ~line 420 — writes debug event AFTER building the interaction object), but the interaction was discarded before being added to `session.Interactions` (or before the session auto-save ran). Review: `RolePlayEngineService.SubmitPromptAsync` lines 1155–1196 — the `interaction` returns from `_continuationService.ContinueAsync(...)` and is added to `session.Interactions`. Check whether an exception path or the `GraduateStagedDirections` `finally` discards them.
2. **Pipeline-save ordering.** The final session save at `02:09:14` ran AFTER the BuildUp→Committed transition (`02:01:17`). If `session.Interactions` was populated but the auto-save uses an older snapshot reference, the new interactions may not make it to DB. Review `_autoSaveCoordinator.QueueRolePlaySessionSave`.
3. **BuildUp→Committed transition snapshot vs SaveAdaptiveState timing.** The V2 row's `UpdatedUtc` (`02:09:18`) is 8 minutes after the transition. Only the V2 state row was saved at `02:09:18` — the Sessions table was saved at `02:09:14`. A fan-out where the pipeline advances V2 phase but doesn't append the newly-built interactions is consistent with this.

### User-reported hypothesis (2026-08-13) — F5 refresh mid-session

User believes the issue happened because they **hit F5 (page refresh) in the middle of a session** while a CSS update was being applied. The refresh discarded the in-flight continuation state.

This is **consistent with the evidence**: the 5 `InteractionPrepared` events at 02:04:02–02:06:37 fired but never made it to the persisted payload. If the page reloaded before the auto-save flushed those interactions, the in-memory `session.Interactions` (including the Committed-phase batch) would be lost, and the next save would persist only the pre-refresh state — leaving `RolePlayV2AdaptiveStates` at `Committed` (advanced by the pipeline) while `Sessions.PayloadJson` still ends at the `02:01:17` BuildUp-stamped interaction.

**Verification pending (later):**
- Confirm whether the 5 in-flight interactions appear in any debug event's `MetadataJson` (e.g. `LlmResponseReceived` output) — proving they were generated but dropped.
- Reproduce: start a session, trigger a continuation, hit F5 mid-generation, and check whether the generated interaction is missing from the payload after reload.
- If confirmed, the fix is not in phase-stamping at all — it's about making the continuation result survive a mid-flight page refresh (e.g. flushing the interaction to the turn/session store before the response is fully streamed, or saving the batch before reload).

### Files to focus on for fix (per user "fix the issue now with current code as it is")

- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — `SubmitPromptAsync` (lines 1020–1215) interaction append + pipeline save sequence; `RunRolePlayV2PipelinesAsync` (line 3897) where the BuildUp→Committed transition at 4298 mutates `v2State.CurrentPhase`.
- `DreamGenClone.Web/Application/Sessions/SessionService.cs` — `LoadRolePlaySessionAsync` (loads `session.AdaptiveState` from V2); session save path.
- `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` — `SaveAdaptiveStateAsync`, `SaveSessionAsync`.

### Working baseline for comparison

Working session `85de44bb` V2 row: `BuildUp | 4 turns | UpdatedUtc 2026-08-13T04:50:38` with transition sequence `BuildUp|Reset|Climax|Approaching|Committed|BuildUp` (6 transitions). The fact that yesterday's session reached Committed and reset cleanly to BuildUp confirms the stamping+transition machinery does function — pointing to a subtle ordering/state-reference bug rather than a logic flip.

## Resolution

None yet. Per Change Control Rule, awaiting explicit user "go ahead" before touching code.

## Validated

- [x] Phase transition record timestamps confirmed (`RolePlayV2PhaseTransitions`).
- [x] Persisted interaction phase distribution confirmed against camelCase keys (`$.interactions[*].narrativePhaseAtCreation`).
- [x] Verified 5 post-transition prepared interactions are NOT in payload.
- [x] Git archaeology: phase-stamping pipeline code unchanged since 2026-07-26; regression commits in window are unrelated to phase.
- [ ] Pending — verify F5-mid-session hypothesis: the 5 in-flight interactions (02:04–02:06) were generated then dropped by page refresh before save.
- [ ] Pending — confirm whether a continuation result survives a mid-flight page refresh (reproduce).
- [ ] Pending — if confirmed, fix = persist in-flight continuation before refresh loses it (not a phase-stamping fix).
- [ ] Pending — verify whether `RunRolePlayV2PipelinesAsync` BuildUp→Committed branch advances V2 state without carrying forward the in-flight interaction.
