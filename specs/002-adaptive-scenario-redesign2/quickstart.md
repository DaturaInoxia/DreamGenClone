# Quickstart: Adaptive Scenario Selection Engine Redesign 2

**Feature Branch**: `002-adaptive-scenario-redesign2`  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

## What This Feature Delivers

- Deterministic single-scenario commitment per cycle
- Ordered phase lifecycle: BuildUp -> Committed -> Approaching -> Climax -> Reset
- Clarified threshold gates and tie-deferral behavior
- Reset-first manual scenario override
- Phase-aware scenario-specific guidance for continuation prompts
- Scenario cycle history for audit and recap

## Phase 1 Baseline Status

- Added application-layer scenario engine interface stubs.
- Added DI placeholder registrations for scenario engine services.
- Next: replace placeholders with concrete infrastructure implementations in foundational tasks.

## Implementation Sequence

1. Add/extend domain models for phase, active scenario commitment, and scenario metadata history.
2. Introduce scenario selection and phase management interfaces in Application layer.
3. Implement infrastructure services for candidate evaluation, transition evaluation, and reset semantics.
4. Integrate selection and phase manager into role-play adaptive state update pipeline.
5. Integrate guidance context factory into prompt-building flow for continuation.
6. Add structured Information logs and debug-event metadata for all major transition events.
7. Add unit and integration tests for transition gates, tie deferral, manual override, and deterministic replay.

## Core Thresholds (from clarifications)

- BuildUp -> Committed: fit >= 0.60 and at least 2 build-up interactions.
- Tie deferral: top-two fit delta <= 0.10, defer and re-evaluate after at least 1 additional interaction.
- Committed -> Approaching: score >= 60, desire >= 65, restraint <= 45, and at least 3 interactions since commitment.
- Approaching -> Climax: score >= 80, desire >= 75, restraint <= 35, and at least 2 interactions in approaching.
- Manual scenario override: force Reset first, then new BuildUp with requested scenario as top priority.

## Local Run and Validation

```powershell
# Build
 dotnet build DreamGenClone.sln

# Run tests for this solution
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj

# Start web app
 dotnet run --project DreamGenClone.Web/DreamGenClone.csproj

# Targeted US2/US3 validation
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~NarrativePhaseManagerTests|FullyQualifiedName~ScenarioResetCycleTests|FullyQualifiedName~RolePlayAdaptiveStateServiceScenarioTests|FullyQualifiedName~RolePlayContinuationScenarioGuidanceTests"

# Affected-scope regression pass
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~StoryAnalysis|FullyQualifiedName~RolePlay"
```

## Suggested Test Focus

- Candidate scoring determinism for repeated identical inputs.
- Tie deferral and additional-interaction re-evaluation behavior.
- Manual override from each phase to reset-first path.
- Approaching/climax guidance framing consistency per active scenario.
- Reset behavior preventing immediate climax re-entry.
