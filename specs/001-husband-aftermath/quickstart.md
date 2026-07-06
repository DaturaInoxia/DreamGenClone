# Quickstart: Wife-Husband Aftermath Closure

**Branch**: `001-husband-aftermath` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This quickstart walks through authoring a theme that uses `[Aftermath:husband-contrast]` and verifying the closure turn fires. The recipe targets local Blazor Server runtime against the dev SQLite store — no cloud dependency.

---

## Prerequisites

1. Working directory: `d:\src\DreamGenClone` on branch `001-husband-aftermath`.
2. The dev SQLite DB exists at `DreamGenClone.Web/data/dreamgenclone.dev.db`.
3. The web app runs locally per the helper scripts in `helpers/`.
4. `artifacts/tmp/dbquery` console tool is built and runnable.

---

## Author the theme

### 1. Open the theme editor

Navigate to the roleplay theme editor (the existing theme-management page) and either:

- **Edit an existing Climax-capable theme** → add the `[Aftermath:husband-contrast]` marker to the Climax phase's `GuidanceText` field; or
- **Create a new theme** modeled on the production multi-encounter Climax theme and add both markers.

### 2. Sample theme phase guidance (Climax entry — both markers)

```text
GuidanceText:
[ClimaxMode:multi-encounter] [Aftermath:husband-contrast]

The Climax phase supports multiple discrete encounters within one play
session. After each encounter ends, the wife returns to her husband and
acts normal — the contrast IS the point.
```

### 3. Add the `encounter-completed` semantic event mapping

Open the theme's semantic mappings editor and ensure an entry with `EventId = "encounter-completed"` exists. The keyword collage is the production set (orgasm / interruption / separation / afterglow / etc.). The strict-config rule (FR-011) requires this mapping to be present whenever `[Aftermath:husband-contrast]` is set; the engine fails fast at session init if missing.

### 4. Save the theme

The theme is now opt-in for husband-contrast aftermath closure. Setting up the scenario:

---

## Author the scenario

The scenario must include the wife character and a husband character whose `RelationTargetId` points at the session's persona name. The existing `BuildOpeningNarrativePromptAsync` uses the same `RelationTargetId == personaName` lookup (lines 2730–2755) — the same convention applies to the aftermath spouse resolver.

### Sample scenario characters

| Name | Role | RelationTargetId |
|---|---|---|
| You | Persona | (none — persona) |
| Anna | Wife | You |
| Mark | Husband | (none — persona is the husband's POV) |

> Note: the user IS the husband's POV persona. The wife (Anna) has `RelationTargetId = "You"` (matching the persona name). The third-party lover — whoever he is — participates in the encounter, exits in the CloseScene leg, and is excluded from the AftermathCoupleInteraction candidate batch by the actor filter.

---

## Play the session

1. Start a roleplay session using the test theme + scenario.
2. Trigger an encounter boundary in Climax — interaction history must reach the multi-encounter detection threshold (the existing min-4-interactions guard applies inside the multi-encounter branch).
3. When the encounter completes (the AI emits keywords that pass `ContainsEncounterCompletionKeywords`), the `TryDetectEncounterBoundaryAsync` path fires and sets `CurrentTimeSkipPhase = CloseScene`.
4. On the next Continue, the overflow batch reads `CloseScene` → emits the rewritten CloseScene directive → transitions to `AftermathCoupleInteraction`.
5. On the next Continue, the overflow batch reads `AftermathCoupleInteraction` → `HusbandAftermathInjector` fires (priority 85), emitting the contrast directive text. The actor filter restricts candidates to wife + husband.
6. On the next Continue, the overflow batch reads `AdvanceTime` → emits the advance-time directive → transitions to `None`.
7. Natural pacing resumes for the next encounter in the multi-encounter chain.

---

## Verify

### Build

```powershell
dotnet build DreamGenClone.sln --no-restore
```

Must report 0 errors.

### Existing regression tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj `
  --filter "FullyQualifiedName~MultiEncounterTimeSkip" --no-build
```

Expect 28 passing tests (existing B-051 suite unchanged).

### New aftermath tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj `
  --filter "FullyQualifiedName~AftermathHusbandContrastTests" --no-build
```

Expect all new tests passing (~18 cases, pure unit per `MultiEncounterTimeSkipTests` patterns).

### RolePlay suite

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj `
  --filter "FullyQualifiedName~RolePlay" --no-build
```

Existing suite must remain green.

### DB inspection — schema

```powershell
dotnet run --project artifacts/tmp/dbquery -- schema RolePlayV2AdaptiveStates
```

Confirm `LastEncounterEvidenceSpan TEXT` column exists (mirrors the existing column list plus the new field).

### Diagnostic panel inspection

While a session is in mid-aftermath, open the roleplay diagnostic panel (the existing surface that consumes `RolePlayDebugEventRecord` entries). Observe:

- `MultiEncounterInstructionInjected` event with `phase = "CloseScene"`,
- Followed by `MultiEncounterInstructionInjected` with `phase = "AftermathCoupleInteraction"`,
- Followed by `MultiEncounterInstructionInjected` with `phase = "AdvanceTime"`.

The aftermath directive text in the second event MUST mention the husband explicitly and the contrast expectation.

### Abort path (negative test)

Start a session where the persona is the wife (or the husband's `RelationTargetId` doesn't resolve to the persona — e.g., a scenario without a clear spouse relation). Trigger an encounter boundary with the `[Aftermath:husband-contrast]` marker set.

Expected: `HusbandAftermathAbortedMissingSpouse` debug event with `Severity = "Warning"` appears in the diagnostic panel; the aftermath leg aborts; the state machine clears to `None` (or `AdvanceTime` if multi-encounter is also active); no erroneous content is written by the injector; the user can resume play normally.

---

## Manual verification summary

| Step | Expected observation |
|---|---|
| Theme + scenario configuration honored | Session starts cleanly; no `MissingEncounterCompletedMapping` exception |
| Encounter boundary detected | `MultiEncounterInstructionInjected(phase=CloseScene)` debug event |
| Closure-leg directive fires | `MultiEncounterInstructionInjected(phase=AftermathCoupleInteraction)` debug event; directive references husband explicitly |
| Actor filter restriction | Only wife + husband appear as overflow candidates during the closure turn; persona excluded |
| Fast Pacing HC suppression | The Fast Pacing HC block is absent from the after-leg prompt (compared to a non-aftermath Fast theme — the block IS present there) |
| Advance leg completes | `MultiEncounterInstructionInjected(phase=AdvanceTime)` event; `CurrentTimeSkipPhase` returns to `None`; re-entry guard re-engages |
| Regression | Existing 28 multi-encounter tests pass; existing RolePlay suite stays green |