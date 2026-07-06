# Contract: AftermathStateMachine

**Branch**: `001-husband-aftermath` | **Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

This contract documents the four state-machine flows that the `TimeSkipPhase` enum accepts after B-056 extends it with `AftermathCoupleInteraction = 3`. It is the authoritative behavioral contract for `RolePlayEngineService.TryDetectEncounterBoundaryAsync`, the overflow time-skip injection block, and the injector firing order during the new leg.

---

## Preconditions

The state machine MUST be entered only when ALL of the following hold:

1. **A theme is loaded** for the active session (`_rpThemeService.GetThemeAsync` returned non-null).
2. **An encounter boundary was detected** by `TryDetectEncounterBoundaryAsync` — the inference call returned a detection record whose `EvidenceSpan` passes `ContainsEncounterCompletionKeywords`.
3. **`CurrentTimeSkipPhase == None`** at detection time (re-entry guard per FR-012).
4. **`CurrentTimeSkipPhase` only transitions after detection or via the overflow leg-emission block** — no other codepath mutates it.

---

## Marker matrix

| `[ClimaxMode:multi-encounter]`? | `[Aftermath:husband-contrast]`? | Active phase | Flow |
|---|---|---|---|
| ❌ absent | ❌ absent | any | Unchanged — natural pacing; `encounter-completed` ignored **only if** neither marker is present anywhere in the theme. |
| ✓ present (Climax only) | ❌ absent | Climax | Existing B-051 flow: `None → CloseScene → AdvanceTime → None` (CloseScene directive text rewritten per FR-010 to include closure prose). |
| ❌ absent | ✓ present | any non-Reset | B-056 new: `None → AftermathCoupleInteraction → None` (closure-only leg — no AdvanceTime leg; natural pacing resumes after closure). |
| ✓ present | ✓ present | Climax | B-056 new: `None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None` (full three-leg flow — CloseScene advances to AftermathCoupleInteraction, then to AdvanceTime, then to None). |
| ✓ present | ✓ present | any non-Climax | B-056 new: `None → AftermathCoupleInteraction → None` (aftermath only — multi-encounter branch is Climax-locked; AdvanceTime leg MUST NOT fire in non-Climax phases per FR-005). |
| ❌ absent | ✓ present | Reset | Out of scope — `IsAftermathHusbandContrast` returns `false` for Reset; no aftermath fires. |
| ✓ absent/❌ | ✓ present | Reset | Out of scope — same as above. |

---

## Flow 1: Multi-encounter only (existing B-051, marker-absent) — unchanged behavior

```text
[Encounter boundary detected in Climax]
   state.CurrentEncounterNumber++;
   state.InteractionsInCurrentEncounter = 0;
   state.CurrentTimeSkipPhase = CloseScene;       // unchanged initial advance
   state.CharacterEncounterStates.Clear();
   state.IsStateDirty = true;

[Overflow batch reads CurrentTimeSkipPhase == CloseScene]
   directive = "Wrap up the current encounter naturally — bodies settle,
                afterglow passes, the characters separate. They get dressed
                and return to whatever they were doing before this happened.
                Do not advance time past this transition."
   // REWRITE per FR-010 — applies to marker-absent multi-encounter themes too
   state.CurrentTimeSkipPhase = AdvanceTime;
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=CloseScene, directive)

[Next Continue → overflow batch reads CurrentTimeSkipPhase == AdvanceTime]
   directive = "Advance time to a new moment — a different day or time, a
                new context, a new circumstance. Establish ordinary life."
   state.CurrentTimeSkipPhase = None;
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=AdvanceTime, directive)
```

**Outgoing post-conditions**: `CurrentTimeSkipPhase == None`, `CurrentEncounterNumber == N+1`, `InteractionsInCurrentEncounter == 0`, re-entry guard re-engages, detection can fire on the next encounter boundary.

---

## Flow 2: Aftermath only — `[Aftermath:husband-contrast]` present, multi-encounter absent

Applies to: BuildUp / Approaching / Committed / Climax (without multi-encounter marker) / any non-Reset phase.

```text
[Encounter boundary detected — generalized detection per FR-003]
   state.LastEncounterEvidenceSpan = detected.EvidenceSpan;
   state.CurrentTimeSkipPhase = AftermathCoupleInteraction;
   state.IsStateDirty = true;       // NO encounter# ++ — multi-encounter is dormant
   // (CharacterEncounterStates intentionally not cleared — natural-pacing
   //  scenes continue across the closure turn without forced reset)

[Overflow batch reads CurrentTimeSkipPhase == AftermathCoupleInteraction]
   HusbandAftermathInjector.ShouldFire → true     // fires at priority 85
   HusbandAftermathInjector.BuildText emits:
       "You just {LastEncounterEvidenceSpan}. Get dressed, return to the
        normal setting, and interact with your husband. Act normal to his
        face — the contrast IS the point: the secret reality of what just
        happened versus the calm performance of ordinary life. Conceal
        evidence — adjust your clothing, control your breathing, manage
        your tone, watch for traces (mess, scent, marks) that could
        betray you. Do not advance time past this husband-wife scene."

   ResolveSceneContinueActorsAsync returns [wife, husband] only (persona
       excluded by clarification Q1 — observes only). If either is
       missing → abort path (see "Abort path" below).

   FinalDirectiveInjector.ShouldFire → true (unchanged) but Fast Pacing HC
       is suppressed because CurrentTimeSkipPhase == AftermathCoupleInteraction.

[Overflow batch transitions phase]
   state.CurrentTimeSkipPhase = None;       // direct → no AdvanceTime leg
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=AftermathCoupleInteraction, directive)
```

**Outgoing post-conditions**: `CurrentTimeSkipPhase == None`, `LastEncounterEvidenceSpan` retained for diagnostic inspection until next detection (optional clear-on-detection by subsequent encounter). Re-entry guard re-engages; detection can fire on the next interaction.

---

## Flow 3: Multi-encounter + aftermath in Climax — full three-leg chain

```text
[Encounter boundary detected — multi-encounter branch active]
   state.CurrentEncounterNumber++;
   state.InteractionsInCurrentEncounter = 0;
   state.CurrentTimeSkipPhase = CloseScene;
   state.LastEncounterEvidenceSpan = detected.EvidenceSpan;  // captured now
   state.CharacterEncounterStates.Clear();
   state.IsStateDirty = true;

[Overflow batch reads CurrentTimeSkipPhase == CloseScene — marker detected]
   directive = FR-010 rewritten CloseScene prose (closure content)
   state.CurrentTimeSkipPhase = AftermathCoupleInteraction;   // NOT AdvanceTime
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=CloseScene, directive)

[Next overflow batch reads CurrentTimeSkipPhase == AftermathCoupleInteraction]
   HusbandAftermathInjector fires; directive = FR-007 contrast text
   ResolveSceneContinueActorsAsync returns [wife, husband] only
   FinalDirectiveInjector suppresses Fast Pacing HC (BuildText guard)
   state.CurrentTimeSkipPhase = AdvanceTime;       // multi-encounter advance fires
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=AftermathCoupleInteraction, directive)

[Next overflow batch reads CurrentTimeSkipPhase == AdvanceTime]
   directive = existing AdvanceTime prose
   state.CurrentTimeSkipPhase = None;
   state.IsStateDirty = true;
   emit MultiEncounterInstructionInjected(phase=AdvanceTime, directive)
```

**Outgoing post-conditions**: `CurrentTimeSkipPhase == None`, `CurrentEncounterNumber == N+1`.  

**Critical ordering invariant** (D7): Aftermath sits BEFORE the advance-time leg, not after — per the user's explicit correction.

---

## Flow 4: Aftermath + multi-encounter in non-Climax phase — aftermath-only subset

Same as Flow 2 — the multi-encounter advance leg is Climax-locked (existing behavior) and MUST NOT fire in non-Climax phases per FR-005. The detection branch routes to Flow 2's `CurrentTimeSkipPhase = AftermathCoupleInteraction` (no CloseScene leg), then transitions directly to `None` (no AdvanceTime leg). This preserves the existing natural-pacing semantics outside Climax.

---

## Abort path — `HusbandAftermathAbortedMissingSpouse`

**Trigger**: `ResolveSceneContinueActorsAsync` is invoked during `CurrentTimeSkipPhase == AftermathCoupleInteraction` and either wife or husband cannot be resolved from the scenario's `Characters` collection via the `ResolveSpouseForAftermathAsync` helper.

### Required side effects (atomic)

1. Emit Serilog structured log:
   ```
   _logger.LogWarning(
       "HusbandAftermathAbortedMissingSpouse: SessionId={SessionId}, PersonaName={PersonaName}, SpouseName={SpouseName}, Reason={Reason}",
       session.Id, personaName, spouseName, "spouse or persona unresolvable from scenario characters");
   ```
2. Emit `RolePlayDebugEventRecord` for the diagnostic panel surface:
   ```
   EventKind = "HusbandAftermathAbortedMissingSpouse"
   Severity = "Warning"
   Summary = "Aftermath leg aborted — spouse unresolvable from scenario"
   MetadataJson = JsonSerializer.Serialize(new {
       sessionId, personaName, spouseName,
       hadMultiEncounter = IsMultiEncounterClimax(theme, "Climax")
   })
   ```
3. Mutate state:
   ```csharp
   session.AdaptiveState.CurrentTimeSkipPhase = IsMultiEncounterClimax(theme, "Climax")
       ? TimeSkipPhase.AdvanceTime     // skip closure, advance to next encounter
       : TimeSkipPhase.None;           // no advance leg — natural pacing resumes
   session.AdaptiveState.IsStateDirty = true;
   ```
4. Return an empty `List<OverflowActorCandidate>()` — the caller's no-overflow cleanup path engages silently. No silent default actors.

### Forbidden

- Auto-picking any non-wife/non-persona character as a stand-in husband (no-fallback violation).
- Throwing an exception that escapes the engine turn (gameplay must continue).
- Silently clearing the phase without logging (no-silent-fallback violation).
- Re-attempting spouse resolution on the same interaction (idempotent abort — observed once per `CurrentTimeSkipPhase == AftermathCoupleInteraction` entry).

---

## Injector firing order during `AftermathCoupleInteraction`

| Priority | Injector | ShouldFire (aftermath phase) | Output |
|---|---|---|---|
| 5 | TurnContextInjector | (unchanged) | Turn-history context |
| 10 | TimeLocationInjector | (unchanged) | Time/location continuity |
| 20 | BehavioralFrameInjector | (unchanged) | Character identity and role (incl. wife / husband) |
| 30 | ThemeContractInjector | (unchanged) | Phase guidance + directive theme text |
| 40 | ThemeAIGuidanceInjector | (unchanged) | Theme-author AI guidance notes |
| 50 | IntensityContractInjector | (unchanged) | Tone/intensity contract |
| 60 | EscalationInjector | (unchanged) | Escalation contract |
| 70 | SceneTimeDirectionInjector | (unchanged) | Scene time direction |
| 75 | ScenePresenceInjector | (unchanged) | Scene presence (theme-controlled) |
| 80 | PositionListInjector | (unchanged) | Position-list inlining |
| **85** | **HusbandAftermathInjector** | **TRUE — fires** (was N/A before B-056) | **FR-007 contrast directive** |
| 90 | BeatStageInjector | (unchanged) | Beat-stage context |
| 100 | FinalDirectiveInjector | (unchanged) | Base closer unchanged; **Fast Pacing HC suppressed in `BuildText`** |

---

## Compatibility invariants

1. **Legacy `TimeSkipPending` back-compat**: The existing reader fallback at `RolePlayStateRepository.cs:595` (`(reader.GetInt32(34) != 0 ? TimeSkipPhase.CloseScene : TimeSkipPhase.None)`) is unaffected. Value `3` is never written by legacy code; new code uses the `CurrentTimeSkipPhase = 35` reader path that casts `(TimeSkipPhase)reader.GetInt32(35)` directly.

2. **No markers present → existing behavior**: Flow 1 is byte-equivalent to the pre-B-056 production behavior except for the CloseScene directive text rewrite (FR-010), which is a deliberate narrative-quality improvement, not a state-machine change.

3. **Marker presence is opt-in at theme level**: Both `[Aftermath:husband-contrast]` and `[ClimaxMode:multi-encounter]` live in theme PhaseGuidance text — authors add/remove them via theme editor. The runtime never defaults a theme into an aftermath flow.

4. **Abort path is observability-first**: Missing-spouse scenarios produce a Warning-level structured Serilog log + a debug-event-record entry. The diagnostic panel surfaces the warning; no UI toast interrupts gameplay (per research Task 9 and the spec's recommendation to defer to B-049).