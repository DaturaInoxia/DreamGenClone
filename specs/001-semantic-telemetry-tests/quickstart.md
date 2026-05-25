# Quickstart: Semantic Telemetry and Event-Driven Evidence

**Feature Branch**: `001-semantic-telemetry-tests`  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

---

## What This Feature Delivers

Adds deterministic semantic-event evidence processing and debug telemetry to RP V2 with strict fail-fast, no-fallback behavior. Semantic evidence is additive to keyword evidence, constrained by configured cap/cooldown/lock rules, and consumed by theme ordering plus candidate fit.

Adds semantic-to-stat mapping support so configured semantic events can update adaptive stats (Desire/Restraint/Tension/Connection) before lifecycle gate evaluation.

---

## Prerequisites

1. .NET 9 SDK installed.
2. Local SQLite runtime database available.
3. Existing RolePlay test suite baseline runnable in `DreamGenClone.Tests`.

---

## Implementation Sequence

### Phase 0.5 - Task Scaffolding Notes

1. Keep semantic diagnostics reason codes centralized in `RPThemeModels.cs` so all services emit the same machine-readable reason values.
2. Add UI placeholders in `RolePlayWorkspace.razor` before semantic pipeline wiring to lock the target debug surface for event/confidence/delta fields.
3. Treat placeholders as non-functional scaffolding only; semantic value population is implemented in later phases.

### Phase A - Contracts and Models

1. Add semantic telemetry and delta-breakdown models in Domain/Application RolePlay layers.
2. Define diagnostics reason codes for mapping failures, confidence failures, and no-contribution status.
3. Ensure contracts expose applied/capped/suppressed fields and explicit semantic-step status.

### Phase B - Semantic Evaluation and Guards

1. Implement semantic event mapping resolution from canonical configured source.
2. Implement strict confidence range validation.
3. Abort semantic step on invalid payload/mapping/confidence and emit explicit diagnostics.
4. Compute semantic deltas and route through cap/cooldown/lock guards.

### Phase C - Pipeline and Debug Integration

1. Integrate semantic evidence application into existing evidence pipeline.
2. Keep keyword evidence path unchanged and additive with semantic path.
3. Update debug telemetry rendering to show events, confidence, applied/capped/suppressed deltas, suppression reasons, and no-contribution state.
4. Ensure ordering and candidate fit consume updated final snapshot.

### Phase E - Semantic Stat Mapping Integration

1. Configure semantic stat mappings in RP Theme detail under Semantic Stat Mappings.
2. Map each semantic event id to a target stat and bounded confidence range.
3. Verify suppression and cap behavior is surfaced in `semanticStatDeltaBreakdowns[]`.
4. Verify gate-relevant stats (for example AverageDesire) reflect post-semantic values before lifecycle evaluation.

### Phase D - Verification and Regression Safety

1. Add tests for mapping correctness and confidence fail-fast behavior.
2. Add repeated-event cap/cooldown suppression tests.
3. Add corruption progression test using semantic intent without keyword trigger.
4. Add blocked-theme lock regression tests.
5. Add end-to-end ranking/fit behavior tests showing semantic-driven outcome change.
6. Execute focused RolePlay test filters and targeted manual debug trace validation.

---

## Developer Commands

```powershell
# Build
dotnet build DreamGenClone.sln -v minimal

# Run RolePlay-focused tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter FullyQualifiedName~RolePlay -v minimal

# Run only semantic telemetry feature tests (example pattern)
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Semantic|FullyQualifiedName~Telemetry|FullyQualifiedName~Evidence" -v minimal

# Run application for manual debug verification
dotnet watch --project DreamGenClone.Web/DreamGenClone.csproj
```

---

## Manual Verification Flow

1. Start a debug-eligible RP session with semantic payload enabled.
2. Send one interaction that triggers a known semantic event.
3. Confirm telemetry includes event id, confidence, and applied/capped/suppressed deltas.
4. Send one interaction with no semantic contribution and confirm explicit no-contribution output.
5. Submit invalid semantic payload or out-of-range confidence and confirm fail-fast diagnostic with zero semantic deltas.
6. Replay repeated adjacent-turn events and confirm cap/cooldown suppression values are visible and bounded.
7. Validate blocked theme remains locked at zero despite positive semantic support.
8. Validate controlled scenario where semantic evidence changes ordering and candidate fit without keyword trigger.

### Manual Trace Fields Checklist

For each interaction inspected in the debug workspace or debug event metadata, confirm:

1. `semanticStepSucceeded` is present and true/false as expected.
2. `semanticEvents[]` includes `eventId`, `confidence`, `mappingId`, `direction`, and `themeTargets`.
3. `semanticDeltaBreakdowns[]` includes `rawDelta`, `appliedDelta`, `cappedDelta`, `suppressedDelta`, and `suppressionReasonCode`.
4. No-contribution interactions still emit semantic diagnostics with explicit no-contribution reason.
5. Semantic failure interactions emit explicit reason codes and keep semantic deltas at zero for the turn.
6. `semanticStatDeltaBreakdowns[]` includes `statName`, `rawDelta`, `appliedDelta`, `cappedDelta`, `suppressedDelta`, and `suppressionReasonCode`.

---

## Expected Evidence of Success

1. Telemetry is present for every debug-eligible interaction and remains understandable without code reading.
2. No fallback/default semantic path exists; invalid inputs/config fail explicitly.
3. Cap/cooldown and lock protections remain intact under repeated semantic signals.
4. Semantic evidence can change ranking/fit outcomes in controlled end-to-end tests.
5. Semantic stat mappings can raise or suppress gate-relevant stat values deterministically.

---

## Command Evidence (2026-05-18)

### T031: Semantic-focused filter

Command:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Semantic|FullyQualifiedName~Telemetry|FullyQualifiedName~Evidence" -v minimal
```

Result: failed at project compile phase due existing unrelated RolePlay finishing-move/receptivity model/test mismatches (41 compile errors in test project on latest rerun; for example missing `DesireBand`, `SelfRespectBand`, `EligibleDesireBands`, `EligibleOtherManDominanceBands`).

### T032: Full RolePlay filter

Command:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter FullyQualifiedName~RolePlay -v minimal
```

Result: failed at project compile phase with the same existing unrelated finishing-move/receptivity model/test mismatches. Initial attempt also encountered transient file-lock copy errors from a running `.NET Host` process; rerun after clearing the lock reached the stable baseline compile failures above.

### T056: Semantic stat follow-up verification (2026-05-19)

Command:

```powershell
dotnet build DreamGenClone.Web/DreamGenClone.csproj -v minimal
```

Result: succeeded. Modified runtime/UI files compile cleanly.

Command:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlayAdaptiveStateServiceTests|FullyQualifiedName~RPThemeCloneTests|FullyQualifiedName~PhaseLifecycleTransitionTests" -v minimal
```

Result: failed at test project compile phase due existing unrelated finishing-move/receptivity drift (`RPFinishingMoveMatrixRow.DesireBand` and related symbols missing in baseline tests). No new diagnostics were reported in modified files.
