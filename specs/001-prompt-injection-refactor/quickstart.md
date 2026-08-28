# Quickstart: Full Prompt Injection Refactor

**Phase**: 1 — Design & Contracts
**Date**: 2026-06-29

## What This Feature Does

Replaces the ~1100-line `BuildPromptAsync` procedural pipeline with a `SceneDirectionCoordinator` that runs a priority-sorted loop of `IPromptInjector` implementations. The engine enforces turn structure (position 1 = anchor, position 2+ = follow), and themes control narrative behavior via markers in phase guidance prose (`[Pacing:slow]`, `[Deepening:subsequent-actors]`, etc.).

## Key Files

| File | Purpose |
|------|---------|
| `DreamGenClone.Domain/RolePlay/SceneDirection.cs` | Add `DeepeningPolicy` enum + `Deepening` field |
| `DreamGenClone.Web/Application/RolePlay/IPromptInjector.cs` | **New** — injector interface |
| `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs` | **New** — context record |
| `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs` | **Build out** — coordinator service |
| `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs` | **Complete** — 5 helper methods, phase defaults |
| `DreamGenClone.Web/Application/RolePlay/Injectors/*.cs` | **New** — 12 injector files |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Refactor `BuildPromptAsync` loop |
| `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` | Delete `BuildFramingGuards`; migrate marker utilities |
| `DreamGenClone.Tests/RolePlay/SceneDirectionResolverTests.cs` | **New** — resolver unit tests |
| `DreamGenClone.Tests/RolePlay/PromptInjectorCaptureTests.cs` | **New** — structural parity + negative tests |
| `DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs` | Add efcbf70f regression test |

## Setup

```bash
# The branch already exists
git checkout 001-prompt-injection-refactor

# Build
dotnet build DreamGenClone.sln

# Run existing tests before changes
dotnet test DreamGenClone.Tests
```

## Implementation Order

### Step 1: Domain — Add DeepeningPolicy (5 min)

Add `DeepeningPolicy` enum and `Deepening` field to `SceneDirection` record in `DreamGenClone.Domain/RolePlay/SceneDirection.cs`.

**Verify**: `dotnet build` succeeds.

### Step 2: Define interfaces + context (10 min)

Create `IPromptInjector.cs` and `PromptInjectionContext.cs` in `DreamGenClone.Web/Application/RolePlay/`.

**Verify**: `dotnet build` succeeds.

### Step 3: Complete SceneDirectionResolver (30 min)

Implement the 5 missing helper methods in `SceneDirectionResolver.cs`:
- `NormalizePhase(string?)` — normalize to canonical form
- `ResolvePacing(...)` — 3-tier: directive > marker > phase default
- `ResolveBeatScope(...)` — same pattern
- `ResolveTimeShift(...)` — same pattern
- `SanitizeNote(string?)` — null-guard + trim

Add `PhaseDefaultPacing`, `PhaseDefaultBeatScope`, `PhaseDefaultTimeShift` constants.
Add `Deepening` resolution: scan phase guidance for `[Deepening:subsequent-actors]`.

Create `SceneDirectionResolverTests.cs` with representative inputs.

**Verify**: Resolver tests pass. `dotnet build` succeeds.

### Step 4: Build coordinator service (15 min)

Implement `SceneDirectionCoordinator` with injector list, ordering, logging.
Wire DI registration in `Program.cs` (or existing service registration).

**Verify**: `dotnet build` succeeds.

### Step 5: Create injectors (2–3 hrs)

Create 12 files in `DreamGenClone.Web/Application/RolePlay/Injectors/`:

| # | File | Priority |
|---|------|----------|
| 1 | `TurnContextInjector.cs` | 5 |
| 2 | `TimeLocationInjector.cs` | 10 |
| 3 | `BehavioralFrameInjector.cs` | 20 |
| 4 | `ThemeContractInjector.cs` | 30 |
| 5 | `ThemeAIGuidanceInjector.cs` | 40 |
| 6 | `IntensityContractInjector.cs` | 50 |
| 7 | `EscalationInjector.cs` | 60 |
| 8 | `DirectorNoteInjector.cs` | 65 |
| 9 | `SceneTimeDirectionInjector.cs` | 70 |
| 10 | `PositionListInjector.cs` | 80 |
| 11 | `BeatStageInjector.cs` | 90 |
| 12 | `FinalDirectiveInjector.cs` | 100 |

**Verify**: Each injector's `ShouldFire` and `BuildText` produce correct output for representative context values.

### Step 6: Refactor BuildPromptAsync (1 hr)

Replace the behavioral inject sections of `BuildPromptAsync` with:
1. Build `PromptInjectionContext`
2. Call `SceneDirectionResolver.Resolve()` once
3. Run `SceneDirectionCoordinator.BuildPrompt(context)`
4. Insert result at the appropriate position among inline data blocks

Remove redundant `Append*` methods from `RolePlayContinuationService`.
Delete `BuildFramingGuards()` from `RolePlayAssistantPrompts`.

**Verify**: `dotnet build` succeeds.

### Step 7: Create structural parity tests (30 min)

Create `PromptInjectorCaptureTests.cs`:
- Test that behavioral directive set is the same as pre-refactor (structural assertion)
- Test that must-appear strings are present (e.g., "maintain this physical setting")
- Test that must-NOT-appear strings are absent (e.g., contradictory Time Span + Location Continuity)

**Verify**: New tests pass.

### Step 8: Add efcbf70f regression test (15 min)

Add test to `RolePlayIntentRoutingTests.cs`:
- Simulate position 2 prompt in Campground Intimacy state
- Assert no contradictory time/location directives
- Test with and without `[TimeShift:within-timeframe]` marker

**Verify**: Regression test passes.

### Step 9: Migrate theme seed data (30 min)

- Add `[Deepening:subsequent-actors]` marker to existing theme phase guidance
- Populate phase guidance prose for themes that lack it (was previously C#-only in `BuildFramingGuards`)

**Verify**: DB query shows all themes have phase guidance prose populated.

### Step 10: Full validation (15 min)

```bash
dotnet build DreamGenClone.sln
dotnet test DreamGenClone.Tests
```

**Verify**: 0 build errors, 0 new test failures.

## Verification Checklist

- [ ] `dotnet build DreamGenClone.sln` — 0 errors
- [ ] `dotnet test DreamGenClone.Tests` — 0 new failures (15 pre-existing tolerated)
- [ ] No `if (phase == "Climax")` in any injector (data-selection reads allowed with justification)
- [ ] No injector gates on intensity level
- [ ] `SceneDirectionResolver` is wired and single source of truth for injectors
- [ ] `BuildFramingGuards()` is deleted
- [ ] Coordinator logs injector firing sequence at Information level
- [ ] efcbf70f regression test passes
- [ ] Structural parity test passes (same behavioral directives)
- [ ] Theme seed data contains `[Deepening:subsequent-actors]` marker + migrated prose
