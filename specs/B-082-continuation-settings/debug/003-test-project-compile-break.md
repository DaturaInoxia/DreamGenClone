# 003 — Test Project Pre-existing Compile Break (stale B-080 test)

**Created:** 2026-08-13
**Feature:** B-082 implementation (uncovered while validating).

## Report

Building `DreamGenClone.Tests` failed with compile errors, blocking `dotnet test`:

1. `RolePlayParticipationGuardTests.cs` — `'RolePlayEngineService.GuaranteeParticipationSeats' does not contain a definition` (8 call sites).
2. `RolePlayParticipationGuardTests.cs` — `'RolePlayEngineService.AffinityStatus'` / `'RolePlayEngineService.AvailableCharacter'` inaccessible due to protection level.

## Analysis

- `GuaranteeParticipationSeats` is referenced **only** by the stale test file — it no longer exists anywhere in the codebase. The B-080 participation guard was superseded by the actor-selection pipeline (`ResolveAvailableCharacters → ScoreActorForAutoSelection → ActorSelectionService → OrderSelectedActors`); the method was removed in a later refactor but the test file was left behind.
- `AffinityStatus` and `AvailableCharacter` (nested types in `RolePlayEngineService`) are `private` at `HEAD` but the same stale test references them. Verified via `git show HEAD:...` — the break predates this session.
- The test project has `InternalsVisibleTo` (`DreamGenClone.csproj`), so the correct visibility is `internal`.

## Plan

1. Delete the stale `RolePlayParticipationGuardTests.cs` (its target method no longer exists).
2. Change `AffinityStatus` / `AvailableCharacter` from `private` to `internal` (zero-behavior visibility fix).

## Resolution

- Deleted `DreamGenClone.Tests/RolePlay/RolePlayParticipationGuardTests.cs`.
- `RolePlayEngineService.cs`: `private enum AffinityStatus` → `internal`; `private sealed record AvailableCharacter` → `internal`.

Test project builds 0 errors afterward.

## Validated

- [x] Confirmed — test project builds 0 errors; new B-082 tests run 15/15.
