---
status: planned
priority: high
appliesTo:
  - DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs
  - DreamGenClone.Web/Application/RolePlay/Injectors/TimeLocationInjector.cs
  - DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
---

# Fix: TimeLocationInjector Persona Location Misalignment

## Problem

The `TimeLocationInjector` (added in commit `883ed68`, branch `001-prompt-injection-refactor`)
emits a **HARD CONSTRAINT** for position > 1 actors:

> "Location Continuity (HARD CONSTRAINT): The scene is now at the time and location
> established by the first response this turn. Maintain this physical setting."

This directive forces **all** actors at position > 1 to adopt position 1's location, even when
they are elsewhere. This breaks scenarios where the persona (e.g., Ken, the Husband) is in a
different location than the lead actor (e.g., Dean in the shed with Becky).

### Observable Symptom

- Persona interaction `3f564458` in session `ee7d15c7-b46e-4737-8f60-bf2aceb7d186` (turn 10,
  response 3 of 3) generated a response mixing contexts because the HARD CONSTRAINT told Ken
  to "maintain the physical setting established by the first response this turn" (the shed),
  while Ken was actually in the trailer making a sandwich.
- The model's own reasoning acknowledged the conflict but felt compelled by the HARD
  CONSTRAINT to keep the shed's physical context.

### Why This Is a Regression

Before commit `883ed68`, the only position-related directive was the soft early-context
block at `BuildPromptAsync` line ~454:

  "Continue from your character's perspective — what you observe, feel, or what occupies
   your attention in this moment."

That softer wording allowed the LLM to write the persona in their own location. The refactor
added a late-position HARD CONSTRAINT that forces location alignment for all actors.

## Scope Constraint

This fix must work for scenarios with **any number of characters** (e.g., 3, 5, 10), not
just the 3-actor exhibitionism scenario. The injector runs per-actor, once per prompt build,
so each actor's context is resolved independently.

## Design: Option C — Reframe Location Continuity as Optional

### New Field on `PromptInjectionContext`

Add a nullable tri-state field `IsActorInScene` to `PromptInjectionContext`:

- `true`  — actor is confirmed in the current scene location
- `false` — actor is confirmed NOT in the current scene location
- `null`  — unknown (location services off, scene location absent, or actor's truth state not tracked)

Resolution rule (matches the existing private helper at `RolePlayEngineService.cs:2458`):

  - If `session.AdaptiveState.CurrentSceneLocation` is null/whitespace → `IsActorInScene = null`
  - Else look up `session.AdaptiveState.CharacterLocations` for an entry matching the actor's name:
    - No entry, or `TrueLocation` is null/whitespace → `null`
    - `TrueLocation` equals `CurrentSceneLocation` (case-insensitive, trimmed) → `true`
    - Otherwise → `false`

This logic is extracted from the existing `IsActorInCurrentScene` helper. To avoid duplicating
the method (already private in `RolePlayEngineService`), either:

- **Option C.1**: Move `IsActorInCurrentScene` to a shared static helper (e.g.,
  `RolePlayScenePresenceHelper.IsActorInScene(session, actorName)`), call it from both
  `RolePlayEngineService.ResolveSceneContinueActorsAsync` and the
  `PromptInjectionContext` builder in `RolePlayContinuationService.BuildPromptAsync`.
- **Option C.2**: Inline the same check at the `PromptInjectionContext` construction site
  in `RolePlayContinuationService` line ~1167. This keeps the helper in
  `RolePlayEngineService` untouched.

**Recommended**: C.1 — shared helper. Avoids drift between the two call sites.

### Injector Changes: `TimeLocationInjector.BuildText`

Replace the current position-based two-branch logic with a tri-state based on
`context.IsActorInScene`:

#### Position 1 (unchanged for all states)

```
Time Span Reminder:
- You are the first response this turn. You may establish or shift the time and location for this turn.
- Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment.
```

#### Position > 1, `IsActorInScene == true`

```
Location Continuity (HARD CONSTRAINT):
- The scene is now at the time and location established by the first response this turn.
- Maintain this physical setting. Do not silently relocate any character.
- If a character moves, write the transition explicitly.
```

(Unchanged from current behavior.)

#### Position > 1, `IsActorInScene == false`

```
Location Continuity:
- You are NOT at the scene established by the first response this turn. Your character is elsewhere.
- Continue from your own location and perspective. Do not insert yourself into the scene just described.
- Only reference what your character can perceive from where they are.
- If your character later joins the scene, write the transition explicitly.
```

Key change: NO "HARD CONSTRAINT" label. This is a soft guidance block. The LLM is told
explicitly that it is elsewhere and should not insert itself into the scene.

#### Position > 1, `IsActorInScene == null` (unknown — fallback to pre-regression behavior)

```
Location Continuity:
- Continue from your character's perspective at their current location.
- If your character is not at the scene just described, write them at their own location
  and perspective. Do not assume they are present at the scene.
```

Key change: No "HARD CONSTRAINT". Soft directive that gives the LLM permission to write
the character elsewhere if appropriate. This restores the pre-refactor behavior where
the model had latitude to interpret the character's location.

#### Time Shift Permission (unchanged for all three states)

If `context.SceneDirection.Pacing == Fast` OR `context.SceneDirection.TimeShift != None`,
append the existing time-shift permission block. This is orthogonal to location continuity.

## Files Changed

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs` | Add `bool? IsActorInScene` property |
| `DreamGenClone.Web/Application/RolePlay/Injectors/TimeLocationInjector.cs` | Replace position-based two-branch logic with tri-state on `IsActorInScene` |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | At `PromptInjectionContext` construction (line ~1167), resolve `IsActorInScene` via shared helper |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Extract `IsActorInCurrentScene` to shared static helper (or use shared helper if C.1) |
| New file: `DreamGenClone.Web/Application/RolePlay/RolePlayScenePresenceHelper.cs` (if C.1) | Static helper class — `IsActorInScene(RolePlaySession, string actorName)` |

Estimated: ~40-50 lines of code changed/added across 4-5 files.

## Tests Required

### Unit Tests for `TimeLocationInjector`

1. **Position 1** — emits "Time Span Reminder" regardless of `IsActorInScene` state
2. **Position > 1, in-scene** — emits HARD CONSTRAINT "Maintain this physical setting"
3. **Position > 1, out-of-scene** — emits soft "You are NOT at the scene" directive, NO "HARD CONSTRAINT" label
4. **Position > 1, unknown** — emits soft "Continue from your character's perspective at their current location", NO "HARD CONSTRAINT" label
5. **Pacing: Fast override** — time-shift permission block appended after location continuity in all states

### Unit Tests for `RolePlayScenePresenceHelper` (if C.1)

1. Null `CurrentSceneLocation` → returns `null`
2. Character has matching `TrueLocation` → returns `true`
3. Character has different `TrueLocation` → returns `false`
4. Character has no location row → returns `null`
5. Case-insensitive location match

### Regression Coverage

Existing test suites that must continue to pass:
- `MultiEncounterTimeSkipTests`
- `AftermathHusbandContrastTests`
- `SessionMemoryInjectionTests`
- `IntimateBehavioralText` tests
- `SceneDirectionResolverTests`
- `PromptInjectorCaptureTests`

## Verification Steps

1. `dotnet build` — 0 errors
2. `dotnet test` for affected test suites — all pass
3. Manual: Create a session with an oblivious husband persona in a different location than the
   wife + other-man. Confirm:
   - Persona position > 1 prompt no longer contains "HARD CONSTRAINT" for location continuity
   - Persona writes from their own location, not the scene location
   - In-scene actors still get the HARD CONSTRAINT location continuity directive
4. Manual: Create a session where location services are disabled. Confirm the injector falls
   back to the soft "Continue from your character's perspective" wording (pre-refactor behavior).

## Out of Scope

- Location services themselves (truth state population) — separate concern
- Other injectors that may have similar persona-awareness gaps
- Memory injection filtering by actor (separate fix for `InjectSessionMemoryBlock`)
- Interaction history filtering by actor perspective (separate larger refactor)

## Follow-up Considerations

After this fix, two related issues remain (each is a separate fix):

1. **`InjectSessionMemoryBlock`** injects all characters' encounter summaries into every
   prompt. The persona sees encounter memory of NPCs having sex. That should be gated to
   the current actor's own summaries.

2. **`InjectCharacterBehavioralTexts`** injects the shared character description block
   (including intimate perspectives like "Becky's anticipation of Dean") into every actor's
   prompt. The persona should not see NPC intimate perspective texts about other NPCs.

These are tracked separately and should be spec'd after this fix lands.