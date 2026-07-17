# Implementation Plan: Persona as a First-Class Character

**Date**: 2026-07-14
**Status**: Planned (not started)
**Design decisions**: Answers to Q1–Q6 per conversation

---

## Overview

The persona (POV character, "You") is currently handled as a special case — excluded from the candidate pipeline, given its own insertion rules. After this change, the persona is treated identically to NPC characters: included in location gates, affinity rules, scoring, LLM candidate pool, and Workspace ResponsePriority slider.

---

## Design Decisions (Answered)

| Question | Decision |
|---|---|
| Q1: Remove special insertion rules? | **Option A** — Remove first-6 `Insert(0)`, even `Add()`, odd skip. Persona is just another scored candidate. |
| Q2: How does persona get location affinities? | **Add `PersonaLocationAffinities` to `Scenario.cs`** — seeded to session on create, same as character affinities. |
| Q3: Include persona in LLM prompt? | **Yes** — treat same as any other candidate. Remove the system prompt exclusion line. |
| Q4: Track persona location? | **Yes** — persona must have a `CharacterLocationState` row like any other character. |
| Q5: Actor type in OverflowActorCandidate? | **?? Open — needs answer.** Should persona in the candidate list be `ContinueAsActor.You` or `ContinueAsActor.Npc`? |
| Q6: LLM candidate label for persona? | **?? Open — needs answer.** Should it show as `"You"` or `"You (Persona)"` in the candidate list? |

---

## Files to Change

### 1. `DreamGenClone.Web/Domain/Scenarios/Scenario.cs`
- Add `public List<CharacterLocationAffinity> PersonaLocationAffinities { get; set; } = [];`

### 2. `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- Add a **Persona Location Affinities** editor section in the Scenario Details card (not in characters list — persona isn't a scenario character). Same dropdown pattern as per-character affinities: per-location `AffinityType` + `TimeOfDay?`.

### 3. `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

#### a) `CreateSessionAsync` (alongside other seeding)
- Copy `scenario.PersonaLocationAffinities` to session (store on `RolePlaySession` or on `AdaptiveScenarioState`)
- Seed persona location row via `RolePlayCharacterStateMutator.EnsureCharacterLocationRows` for persona

#### b) `ResolveAvailableCharacters`
- **Remove** the `if (name == personaName) continue;` skip
- After the character loop, add a persona entry using `PersonaLocationAffinities` for affinity resolution
- Role: `"Persona"`, IsInScene: from `RolePlayScenePresenceHelper`

#### c) `ResolveSceneContinueActorsAsync`
- **Remove** the special persona insertion block (first-6 lead, even/odd)
- Persona flows through the pipeline naturally via `ResolveAvailableCharacters`
- Persona's `OverflowActorCandidate` uses `ContinueAsActor.You` (or `Npc` — depends on Q5) when `autoAllowedActors.Contains(ContinueAsActor.You)`
- The fallback at the bottom (`ResolveDefaultContinueActor`) already handles persona-only sessions

### 4. `DreamGenClone.Web/Application/RolePlay/RolePlayCharacterStateMutator.cs`
- `EnsureCharacterLocationRows`: also create a row for the persona (`state.CharacterStats` lookup by persona name, or add a parameter for persona name)

### 5. `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs`
- Remove the system prompt line `"The persona ('You' POV character) is NOT in this list — it is inserted separately by the engine."`

### 6. `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs`
- No change needed — `ActorCandidateInfo.Name` already holds the character name; persona will be `"You"` or whatever `session.PersonaName` is.

### 7. `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- The ResponsePriority slider loop already iterates `CharacterPerspectives` — persona is not typically in this list. Need to add persona as an entry in the priority sliders section.
- Change the foreach from `CharacterPerspectives` to `CharacterPerspectives` + persona

---

## Dependencies

| Step | Depends on | Description |
|---|---|---|
| 1. Scenario model | None | Add property |
| 2. ScenarioEditor | Step 1 | Add persona affinity UI |
| 3a. CreateSessionAsync | Step 1 | Wire seeding |
| 3b. ResolveAvailableCharacters | Step 3a | Include persona |
| 3c. Remove insertion rules | Step 3b | Clean up old logic |
| 4. Location state mutator | Step 1 | Add persona location row |
| 5. LLM prompt | Step 2 | Remove persona exclusion line |
| 6. Workspace UI | Step 3 | Add persona to sliders |

---

## Testing

1. Create scenario with persona affinities (e.g., persona `Required` at Home)
2. Start session — verify persona appears in `CurrentSceneLocation` data
3. Click overflow continue — verify persona is in the candidate rotation
4. Set persona `ResponsePriority = 100` — verify persona always appears first
5. Set persona `Excluded` from Beach — narrate move to Beach — verify persona is not in candidate pool
6. Workspace Adaptive tab — verify persona's location row appears
