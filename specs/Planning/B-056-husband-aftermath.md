# Plan: B-056 — Wife-Husband Aftermath Closure (Marker: `[Aftermath:husband-contrast]`)

**TL;DR**: The multi-encounter two-turn split (B-051, just shipped) jumps from the end of a sex scene straight to advance-time with no closure. The user describes this as "the sex scene has no closure — no getting dressed, no going back to whatever they were doing." `[Aftermath:husband-contrast]` fixes this by inserting a closure turn (sex ends → get dressed → return to husband → act normal → THEN advance time) and is generalizable to any phase where sex/exposure ends. Built on the existing `TimeSkipPhase` state machine — the enum value fits the existing column; only a new `LastEncounterEvidenceSpan` TEXT column is added to the DB.

---

## Goal / Scope

**In scope**:
1. Add `AftermathCoupleInteraction = 3` value to the existing `TimeSkipPhase` enum (Domain).
2. Make `[Aftermath:husband-contrast]` a first-class theme marker detected in any non-Reset phase, mirroring the existing `[ClimaxMode:multi-encounter]` pattern.
3. Generalize `TryDetectEncounterBoundaryAsync` to fire on `encounter-completed` for any phase carrying either marker (drop the Climax-only + multi-encounter-only gates; branch on which marker(s) are present after detection succeeds).
4. Insert the `AftermathCoupleInteraction` leg **before** any time-ahead advance, as the closure turn:
   - Multi-encounter Climax flow with marker: `None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None` (the closure leg sits between the two existing legs)
   - Any other phase with marker, encounter ends: `None → AftermathCoupleInteraction → None` (closure only — no multi-encounter advance leg)
   - Themes without the marker: unchanged (existing `CloseScene → AdvanceTime → None` two-leg flow for multi-encounter; natural pacing for others)
5. Rewrite the `CloseScene` directive text to explicitly include closure content (get dressed, separate, return to ordinary setting) — addresses the "no closure" complaint for themes that use multi-encounter without the marker too.
6. Add `HusbandAftermathInjector` (priority 85) that emits the wife-husband contrast directive when `CurrentTimeSkipPhase == AftermathCoupleInteraction`. Drives the per-actor framing via the existing injector pipeline.
7. Suppress `FinalDirectiveInjector`'s Fast Pacing HC during the aftermath leg (so pacing directives don't fight the closure directive).
8. Filter `ResolveSceneContinueActorsAsync` so when `CurrentTimeSkipPhase == AftermathCoupleInteraction`, the candidate batch is restricted to wife + husband (with explicit abort-and-log if either is missing — no silent fallback, per the strict-config rule).
9. Tests for the new state-machine leg, the marker detection, the injector, the actor filter, the Fast Pacing HC suppression, and the directive text.

**Out of scope** (deferred):
- `AftermathCoupleInteraction` for `Reset` phase (post-arc) — explicitly skipped. Aftermath mid-RP only.
- B-055 I5 "visual-only boundary" detection in Approaching — stays in B-055 scope. B-056 fires only on the existing `encounter-completed` semantic event.
- `/skipaftermath` slash command — `steer` already covers user override.
- B-054 stays a separate backlog `new` item (it proposed adding the same enum value but specifically between multi-encounter boundaries only; B-056 generalizes the marker-driven approach and the enum extension naturally subsumes B-054's design — B-054 can later be marked `done done` referencing this plan).
- A dedicated "encounter completed" event for non-sex scenarios (e.g. public exposure without sex). The existing `encounter-completed` semantic mapping with its current keyword collage already covers exposure/interruption cases — no new mapping required for v1.

---

## Steps

### Phase A — Domain: enum + flag

`CurrentTimeSkipPhase` is already persisted as `INTEGER` in `RolePlayV2AdaptiveStates` (FR-006 patch landed). Value `3` fits the existing column with zero migration for this field. `LastEncounterEvidenceSpan` needs a new TEXT column (added in Phase A2).

1. **Edit `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`** — extend the `TimeSkipPhase` enum at line ~293:
   - Add `AftermathCoupleInteraction = 3` with XML doc describing the closure state.
   - Update enum summary comment to describe the optional third leg: `None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None`.
   - Add `string? LastEncounterEvidenceSpan { get; set; }` property to capture the detected evidence span at detection time so `HusbandAftermathInjector` can reference "what she just did" verbatim from the AI's own detection trace.
   - Document the IsStateDirty contract for the new field in the existing dirty-flag contract comment at line ~225.

### Phase A2 — DB migration for `LastEncounterEvidenceSpan`

2. **Edit `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs > EnsureSchemaAsync`**:
   - Add `if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "LastEncounterEvidenceSpan", cancellationToken))` block.
   - `ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN LastEncounterEvidenceSpan TEXT;`
   - Update the INSERT/UPDATE SQL to include `LastEncounterEvidenceSpan = $lastEncounterEvidenceSpan`.
   - Update the SELECT SQL to include the column.
   - Add `command.Parameters.AddWithValue("$lastEncounterEvidenceSpan", state.LastEncounterEvidenceSpan ?? (object)DBNull.Value);` in the write path.
   - Add `LastEncounterEvidenceSpan = reader.IsDBNull(colIdx) ? null : reader.GetString(colIdx)` in the read path.

### Phase B — Marker detection + helper

3. **Edit `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`** — add `IsAftermathHusbandContrast(RPTheme? theme, string phase)` mirroring the existing `IsMultiEncounterClimax` helper at line 52. Skips Reset phase explicitly (return false when `phase == "Reset"`).

4. **Edit `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs > EnsureEncounterCompletedMappingAsync`** — extend the `hasMapping` enforcement to also throw when the theme carries `[Aftermath:husband-contrast]` but lacks an `encounter-completed` mapping (currently only throws for `[ClimaxMode:multi-encounter]`). Match the existing fail-fast pattern; no new exception type.

### Phase C — Detection generalization (Option C)

5. **Edit `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs > TryDetectEncounterBoundaryAsync`** at line 4537:
   - Replace the `if (state.CurrentPhase != Climax) return;` early return with `if (state.CurrentPhase == Reset) return;`
   - Replace the `if (state.CurrentEncounterNumber <= 0) return;` early return with a marker-aware check: load the theme once at the top of the method, then if neither `IsMultiEncounterClimax(theme, phase)` nor `IsAftermathHusbandContrast(theme, phase)` is true, return early.
   - Move the `IsMultiEncounterClimax(theme, "Climax")` gate from line ~4579 down, going from a hard return to a branch contributor.
   - Move the `InteractionsInCurrentEncounter < minIxns` (min 4 interactions) guard (line ~4595) INSIDE the multi-encounter branch only — single-sex-act phases (BuildUp, Committed) shouldn't be gated by multi-encounter's premature-advance rule.
   - After detection succeeds + keyword gate passes + theme resolved, branch on which marker(s) are active:
     - Multi-encounter + Climax: bump `CurrentEncounterNumber`, reset `InteractionsInCurrentEncounter`, set `CurrentTimeSkipPhase = CloseScene` (existing behavior)
     - Aftermath marker present: set `state.LastEncounterEvidenceSpan = detected.EvidenceSpan`
     - Both markers + Climax: both consequences fire atomically (close-scene leg will execute first, then transition to aftermath leg per the state machine)
     - Aftermath only (non-Climax, or Climax without multi-encounter): set `CurrentTimeSkipPhase = AftermathCoupleInteraction` directly (skip CloseScene — there's no multi-encounter flow to close out)
   - Keep FR-008 re-entry guard: skip detection while `CurrentTimeSkipPhase != None`.

### Phase D — State machine extension in the overflow block

*Parallel with Phase C in the same file (`RolePlayEngineService.cs`); a single edit pass will land both.*

6. **Edit `RolePlayEngineService.cs > overflow time-skip injection block`** at line ~1532:
   - Change `CloseScene → AdvanceTime` to `CloseScene → AftermathCoupleInteraction` when the theme carries `[Aftermath:husband-contrast]`, otherwise `CloseScene → AdvanceTime` (existing).
   - Add a new branch for `AftermathCoupleInteraction`:
     - Multi-encounter active: directive = the aftermath contrast text, advance phase to `AdvanceTime`.
     - Multi-encounter inactive (any other phase): directive = the aftermath contrast text, advance phase to `None`.
   - Rewrite the existing `CloseScene` directive text from `"Close the current encounter naturally."` to an explicit closure directive: `"Wrap up the current encounter naturally — bodies settle, afterglow passes, the characters separate. They get dressed and return to whatever they were doing before this happened. Do not advance time past this transition."` — this addresses the "no closure" complaint for multi-encounter themes that do not carry the aftermath marker too.
   - Mark `IsStateDirty = true` on every phase mutation (existing for CloseScene/AdvanceTime; add for the new AftermathCoupleInteraction transitions).

7. **Edit `RolePlayEngineService.cs > HydrateV2State`** at line ~4264 — add restore of `LastEncounterEvidenceSpan` alongside the existing `CurrentTimeSkipPhase` / `CurrentEncounterNumber` / `InteractionsInCurrentEncounter` restore we added in the FR-006 patch.

### Phase E — Injector for the aftermath directive

8. **Create `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs`**:
   - `Id = "husband-aftermath"`
   - `Priority = 85` (fires after PositionList at 80, before BeatStage at 90)
   - `ShouldFire`: returns true when `context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction`
   - `BuildText`: emits the contrast directive referencing `context.Session.AdaptiveState.LastEncounterEvidenceSpan`:
     > You just {[EvidenceSpan or "had an intimate encounter with another man"]}. Get dressed, return to the normal setting, and interact with your husband. Act normal to his face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. Do not advance time past this husband-wife scene.
   - Result is a directive context block consistent with other injectors (no leading/trailing newlines per contract).

9. **Edit `DreamGenClone.Web/Application/RolePlay/Injectors/FinalDirectiveInjector.cs`** — `ShouldFire` predicate becomes: `=> !(context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction && context.SceneDirection.Pacing == ScenePacing.Fast)`. Suppresses the Fast Pacing HC during the aftermath turn only — the rest of the I7 wording (introducing "conclude the encounter first — get dressed, return to your partner — then advance time" to the regular Fast case for non-marker themes) is a separate B-055 I7 follow-up.

10. **Edit `DreamGenClone.Web/Program.cs`** at line ~122-135 — register `HusbandAftermathInjector` in the same `services.AddSingleton<IPromptInjector, ...>()` list as the existing 12 injectors.

### Phase F — Actor selection gating

11. **Edit `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs > ResolveSceneContinueActorsAsync`** at line 2185:
    - Add an early branch before the existing `_behaviorModeService.GetAllowedActors` call: when `session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction`, resolve the wife and husband character names from the scenario (via a new `ResolveSpouseForAftermathAsync` helper that wraps the existing `RelationTargetId == personaName` lookup in `BuildOpeningNarrativePromptAsync` at line ~1928).
    - Build a `List<OverflowActorCandidate>` with wife first, then husband, sourced from `scenario.Characters` filtered to those two names.
    - If wife is missing OR husband is missing from the scenario, **abort the aftermath leg explicitly**: log `HusbandAftermathAbortedMissingSpouse` debug event, clear `CurrentTimeSkipPhase = None` (or `AdvanceTime` if multi-encounter), set `IsStateDirty = true`, and return an empty list (the calling turn aborts cleanly — same fallback to no-overflow when candidates is empty). No silent default actors — per the no-fallback rule.

12. **Extract `ResolveSpouseForAftermathAsync` helper** in `RolePlayEngineService.cs` from the existing spouse-resolution logic in `BuildOpeningNarrativePromptAsync` so both code paths share one source of truth.

### Phase G — Test coverage

13. **Create `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`** — matrix tests (pure unit, mirror `MultiEncounterTimeSkipTests.cs` patterns):
    - `TimeSkipPhase_AftermathCoupleInteraction_HasValue3` — enum sanity
    - `IsAftermathHusbandContrast_ReturnsTrue_WhenMarkerPresent` (any phase)
    - `IsAftermathHusbandContrast_ReturnsFalse_ForResetPhase` — explicit out-of-scope
    - `IsAftermathHusbandContrast_ReturnsFalse_WhenMarkerAbsent`
    - `HusbandAftermathInjector_ShouldFire_WhenPhaseIsAftermathCoupleInteraction`
    - `HusbandAftermathInjector_ShouldNotFire_WhenPhaseIsCloseScene_OrAdvanceTime_OrNone`
    - `HusbandAftermathInjector_BuildText_ReferencesLastEncounterEvidenceSpan`
    - `FinalDirectiveInjector_SuppressesFastPacingHC_WhenAftermathPhaseActive`
    - `FinalDirectiveInjector_FiresNormally_WhenAftermathPhaseInactive` (regression)
    - `CloseScene_Phase_Transitions_To_AftermathCoupleInteraction_WhenMarkerPresent` (then to AdvanceTime)
    - `CloseScene_Phase_Transitions_To_AdvanceTime_WhenMarkerAbsent` (regression for the existing split)
    - `AftermathCoupleInteraction_Transitions_ToAdvanceTime_WhenMultiEncounter`
    - `AftermathCoupleInteraction_Transitions_ToNone_WhenNoMultiEncounter`
    - `AftermathHusbandActorFilter_ReturnsWifeThenHusband`
    - `AftermathHusbandActorFilter_AbortsAndLogs_WhenSpouseMissing`
    - `HasRecentUserInstruction_DeferStaysActiveDuringAftermathLeg` — extend existing deferral semantics to the new leg (FR-005 already covers all non-None phases; test confirms it).
    - `TryDetectEncounterBoundary_FiresInBuildUp_WhenMarkerPresent` — phase-gate relaxation covered
    - `TryDetectEncounterBoundary_SkipsInReset_EvenWithMarker` — out-of-scope enforced

### Phase H — Spec + backlog hygiene

14. **Edit `specs/Planning/backlog.md`** — move B-056 state `new → designed`, update its notes to reference this plan and the marker name. Also annotate B-054 with a note: "Subsumed by B-056's `AftermathCoupleInteraction = 3` enum extension. B-054 documented the original intent; B-056 delivers a generalized marker-driven version that works in any phase."

15. **Create `specs/Planning/B-056-husband-aftermath.md`** — this document, the design plan. Single source of truth for future contributors.

---

## Relevant files

- `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` — `TimeSkipPhase` enum (line 293) gets the new value; new `LastEncounterEvidenceSpan` property
- `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs > EnsureSchemaAsync` — new migration for `LastEncounterEvidenceSpan` TEXT column; INSERT/UPDATE/SELECT and parameter mapping
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — new `IsAftermathHusbandContrast` helper mirroring `IsMultiEncounterClimax` (line 52); extend `EnsureEncounterCompletedMappingAsync` to enforce the marker→mapping contract
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` —
  - `TryDetectEncounterBoundaryAsync` (line 4537): relax phase/encounter gates, branch on markers post-detection
  - Overflow time-skip block (line 1532): add `AftermathCoupleInteraction` branch between `CloseScene` and `AdvanceTime`; rewrite `CloseScene` directive text to include closure
  - `ResolveSceneContinueActorsAsync` (line 2185): wife+husband actor filter with explicit abort
  - `BuildOpeningNarrativePromptAsync` (line ~1928): extract spouse resolution to helper
  - `HydrateV2State` (line ~4264): restore `LastEncounterEvidenceSpan`
- `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` (new) — priority 85 injector
- `DreamGenClone.Web/Application/RolePlay/Injectors/FinalDirectiveInjector.cs` — `ShouldFire` adds Fast-Pacing-during-aftermath suppression
- `DreamGenClone.Web/Program.cs` (line 122-135) — register `HusbandAftermathInjector` in the DI loop
- `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs` (new) — unit test matrix
- `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — existing 28 tests still pass as-is (CloseScene → AdvanceTime still the default when marker absent)
- `specs/Planning/backlog.md` — state transition for B-056/B-054

---

## Verification

1. **Build**: `dotnet build DreamGenClone.sln --no-restore` — must report 0 errors. Verifies the enum extension, injector wiring, and detection refactor compile across Domain / Web / Infrastructure.
2. **Existing split tests**: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkipTests"` — 28 tests must still pass. Verifies the `CloseScene → AdvanceTime → None` path unchanged for themes without the marker.
3. **New aftermath tests**: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AftermathHusbandContrastTests"` — all new tests pass. Verifies the new leg, the injector, the actor filter, the Fast Pacing HC suppression, and the marker detection.
4. **RolePlay suite regression**: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"` — existing suite still green (no behavior change for marker-absent themes).
5. **DB inspection**: `dotnet run --project artifacts/tmp/dbquery -- schema RolePlayV2AdaptiveStates` to confirm `LastEncounterEvidenceSpan` column exists after schema bootstrap.
6. **Manual end-to-end smoke** (optional, post-implementation): seed a theme with `[ClimaxMode:multi-encounter] [Aftermath:husband-contrast]` in Climax phase guidance + `encounter-completed` semantic mapping, play past an encounter boundary in a multi-encounter flow, observe the debug events sequence `MultiEncounterInstructionInjected(CloseScene) → MultiEncounterInstructionInjected(AftermathCoupleInteraction) → MultiEncounterInstructionInjected(AdvanceTime)`, and verify the directive text for the aftermath leg mentions the husband and the contrast expectation.

---

## Decisions

The decisions are locked to the combination surfaced during planning:

- **D1 (scope)**: B-056 delivers the generalized marker-driven `AftermathCoupleInteraction` enum extension. B-054 is subsumed — its design proposed the same enum value but multi-encounter-only; B-056 works in any non-Reset phase. No separate `RequiresHusbandAftermath` flag — the marker-driven transition to `CurrentTimeSkipPhase = AftermathCoupleInteraction` is the sole state surface.
- **D2 (trigger)**: Encounter-completed semantic event, generalised to fire in any non-Reset phase carrying the marker. Single inference call per detection (Option C — one LLM call, branched consequence).
- **D3 (marker opt-in)**: Theme author adds `[Aftermath:husband-contrast]` to phase guidance text in the existing theme editor. No new UI control. Strict-config compliant (marker is editable persisted config; no hidden runtime default).
- **D4 (scope of non-multi-encounter)**: Fires; the closure leg runs alone (`AftermathCoupleInteraction → None`). Multi-encounter marker absent means no AdvanceTime leg — natural pacing resumes after closure.
- **D5 (I7 integration)**: `FinalDirectiveInjector` suppresses its Fast Pacing HC during the aftermath leg only. I7 wider wording improvements for non-marker Fast themes deferred to B-055.
- **D6 (actor selection)**: Filter to wife + husband, abort explicitly if either missing. No silent fallback. Persona excluded (the user is the husband's POV persona — the user observes but does not author this turn).
- **D7 (order in state machine)**: Aftermath is BEFORE advance-time, not after. Multi-encounter: `CloseScene → AftermathCoupleInteraction → AdvanceTime`. This is the user's explicit correction.
- **D8 (closure content)**: The existing `CloseScene` directive is rewritten to include explicit closure prose (get dressed, return to ordinary setting) — addresses the "no closure" complaint for both marker-present AND marker-absent multi-encounter themes.
- **D9 (evidence source)**: `LastEncounterEvidenceSpan` persisted field stores `detected.EvidenceSpan` at detection time — injector reads from state. Avoids textual re-derivation.

---

## State machine flows

| Theme config | Flow |
|---|---|
| Neither marker | Unchanged (natural pacing, encounter-completed ignored) |
| `[ClimaxMode:multi-encounter]` only | `CloseScene → AdvanceTime → None` (existing, but CloseScene directive text now includes closure prose — D8) |
| `[Aftermath:husband-contrast]` only | `AftermathCoupleInteraction → None` (closure in any phase, no advance leg) |
| Both markers in Climax | `CloseScene → AftermathCoupleInteraction → AdvanceTime → None` |

---

## Further Considerations

1. **Spouse identity resolution edge case**: scenarios without a clear `RelationTargetId == personaName` spouse (e.g. MFF or open-marriage setups) — should the abort path produce a visible UI diagnostic in the roleplay diagnostic panel, or only a debug log? **Recommended**: debug log only for v1; surface in the adaptive diagnostic panel later under B-049 (comprehensive data visibility). Discoverability is the user's problem to manage via scenario design, not the runtime's.
2. **Marker combination**: a theme with `[ClimaxMode:multi-encounter]` + `[Aftermath:husband-contrast]` + `[Pacing:fast]` in Climax guidance. The state machine becomes `CloseScene → AftermathCoupleInteraction → AdvanceTime` with Fast-Pacing suppressed only during the aftermath leg. **Recommended**: scope the Fast Pacing suppression to `AftermathCoupleInteraction` only, leave `CloseScene` and `AdvanceTime` under existing pacing directives. The closure leg alone needs the pause; the other two already have explicit time-shift instructions.
3. **Detection re-firing after the aftermath turn**: Aftermath clears to `None` (non-multi-encounter) or advances to `AdvanceTime` (multi-encounter). What if the model ends the aftermath leg with another suggestion of sex? The existing `IsCharacterHavingSex` gate + `CurrentTimeSkipPhase == None` re-entry guard naturally handle this — detection can re-engage on the next interaction. No additional cleanup needed.
