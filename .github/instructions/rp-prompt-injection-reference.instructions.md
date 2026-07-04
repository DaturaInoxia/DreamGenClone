# RP Prompt Injection Reference

**Purpose**: Complete reference for all theme phase-guidance markers and the injectors they control. Use this when analyzing prompts, debugging injector behavior, or answering "what does this marker do" questions.

**Applies to**: `DreamGenClone.Web/Application/RolePlay/Injectors/*.cs`, `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs`, `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs`, `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs`

---

## Architecture Overview

```
Theme Phase Guidance (DB: RPThemePhaseGuidance.GuidanceText)
    │  Contains markers like [Pacing:fast] [BeatStyle:episodic]
    ▼
SceneDirectionResolver (3-tier precedence)
    Tier 1: Profile-configured DirectorNote (overrides everything)
    Tier 2: Theme markers in current-phase guidance
    Tier 3: Phase defaults (hardcoded in resolver)
    │
    ▼
SceneDirection record (Pacing, BeatScope, TimeShift, Deepening, RequireScenePresence, DirectorNote)
    │
    ▼
PromptInjectionContext (built once per prompt by the coordinator)
    │
    ▼
SceneDirectionCoordinator → priority-sorted IPromptInjector[] loop
    │
    ├── 13 injectors fire in priority order (5, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80, 90, 100)
    │
    ▼
Prompt text output
```

---

## Marker-to-Injector Map

### Pacing Markers

| Marker | Resolver Output | Injectors Affected | Prompt Text |
|--------|----------------|-------------------|-------------|
| `[Pacing:slow]` | `ScenePacing.Slow` | `EscalationInjector` (pri 60) | *"Advance within the same beat — deepen, do not leap. Fill the response with sensory, emotional, and physical detail specific to this moment. Do not describe a new beat or position."* |
| | | `SceneTimeDirectionInjector` (pri 70) | *"Stay in the current moment. Do not skip forward."* or *"Focus on one beat per response."* |
| `[Pacing:medium]` | `ScenePacing.Medium` | `EscalationInjector` (pri 60) | *"Advance the scene with forward momentum. Cover one to two beats this response. Avoid repeating only hesitant or reset beats."* |
| | | `SceneTimeDirectionInjector` (pri 70) | *"Let the scene breathe without dragging. Cover one to two beats per response."* |
| `[Pacing:fast]` | `ScenePacing.Fast` | `EscalationInjector` (pri 60) | *"This is a fast-paced scene. Cover more story ground per response — advance through the full arc of this moment. Compress multiple beats into each response. Do not write only one beat when multiple beats fit naturally. If an encounter reaches its natural conclusion (orgasm, resolution, or scene end), advance to a new time or setting afterwards."* |
| | | `SceneTimeDirectionInjector` (pri 70) | *"Compress multiple beats into one response. Cover more story ground per response."* or *"Time must advance significantly — cover more story ground. Use clear transitions. Do not remain in the same time frame across consecutive responses."* |
| | | `FinalDirectiveInjector` (pri 100) | *"HARD CONSTRAINT — Fast Pacing Directive: This is a fast-paced scene. Cover more story ground per response — compress multiple beats into one. Do not fixate on a single beat. Advance through the full arc of the current moment toward its natural resolution. If the previous response already described a sexual act, advance to a new act, position, or time. Do not repeat."* |

> **Note**: `[Pacing:fast]` is the only marker that also fires the `FinalDirectiveInjector` (priority 100), placing a HARD CONSTRAINT at the **very end of the prompt** for maximum authority.

---

### Time Shift Markers

| Marker | Resolver Output | Injectors Affected | Prompt Text |
|--------|----------------|-------------------|-------------|
| `[TimeShift:within-timeframe]` | `TimeShiftPolicy.Small` | `SceneTimeDirectionInjector` (pri 70) | No time shift → *"No time shift — all beats occur within the current timeframe."* With time shift → *"Time must advance significantly — cover more story ground. Use clear transitions. Do not remain in the same time frame across consecutive responses."* |
| | | `TimeLocationInjector` (pri 10, position 2+ only) | *"You may also shift time or location, following the pacing and time shift rules."* (added alongside Location Continuity) |

> **Phase defaults**: TimeShift defaults vary by phase — `None` for Reset, `Small` for BuildUp/Committed/Approaching, `Medium` for Climax. The marker overrides the default only when present.

---

### Deepening Markers

| Marker | Resolver Output | Injectors Affected | Prompt Text |
|--------|----------------|-------------------|-------------|
| `[Deepening:subsequent-actors]` | `DeepeningPolicy.SubsequentActors` | `EscalationInjector` (pri 60, position 2+ path) | *"Scene Deepening (Subsequent Actor): Deepen the current scene beat from your character's POV only. Do NOT advance to a new beat or position. Explore internal reactions, sensory details, and emotional responses to this moment."* |

> **Orthogonal to pacing**: Deepening overrides position 2+ behavior regardless of `[Pacing:*]` setting. Even under fast pacing, position 2+ will deepen rather than advance.

---

### Beat Style Markers

`[BeatStyle:single]` and `[BeatStyle:short]` are **metadata only** — they are parsed and stored on `SceneDirection.BeatScope` but no injector reads them (BeatStageInjector only fires for Extended). Beat advancement is fully controlled by `[Pacing:*]` markers.

| Marker | Resolver Output | BeatStageInjector Fires? | Prompt Text |
|--------|----------------|--------------------------|-------------|
| `[BeatStyle:single]` | `BeatScope.Single` | ❌ (fires only for Extended) | — |
| `[BeatStyle:short]` | `BeatScope.Short` | ❌ (fires only for Extended) | — |
| `[BeatStyle:episodic]` | `BeatScope.Extended` | ✅ (pri 90) | *"Beat Stage Context: Current beat scope: Extended. Stay present in the current moment — deepen sensory and emotional detail."* |

> `[BeatStyle:single]` and `[BeatStyle:short]` have no behavioral effect. Beat scope is redundant with pacing — `[Pacing:slow]` already tells the model to stay within a beat; `[Pacing:fast]` already tells it to compress multiple beats. `[BeatStyle:episodic]` is the only marker that adds independent value by explicitly demanding lingering (Extended scope), which is the opposite direction from default pacing.

---

### Climax Mode Markers

| Marker | Parser | Where Used | Status |
|--------|--------|------------|--------|
| `[ClimaxMode:quick-finish]` | `RolePlayAssistantPrompts.IsQuickFinishClimaxMode()` | Used in deprecated `BuildFramingGuards()` | 🔴 Retired — prose moved to theme phase guidance. Marker still parsed but no dedicated injector. |
| `[ClimaxMode:multi-encounter]` | `RolePlayAssistantPrompts.IsMultiEncounterClimax()` | Used in `RolePlayEngineService` for encounter detection | Active — enables `UpdateStateAndDetectEncounterAsync` to increment `CurrentEncounterNumber` and trigger time-skip injection between encounters. |

---

### Scene Presence Marker

| Marker | Resolver Output | Injectors Affected | Prompt Text |
|--------|----------------|-------------------|-------------|
| `[ScenePresence]` | `SceneDirection.RequireScenePresence = true` | `ScenePresenceInjector` (pri 75) | Scene Presence Contract: stay present, no time-skip. Opt-in only via theme marker. |

---

## Priority Map (All 13 Injectors)

| Priority | Injector | Owned By | ShouldFire Condition |
|----------|----------|----------|---------------------|
| 5 | `TurnContextInjector` | Engine | `PositionInTurn.HasValue` |
| 10 | `TimeLocationInjector` | Engine | Always |
| 20 | `BehavioralFrameInjector` | Engine | Always |
| 30 | `ThemeContractInjector` | Theme | `ActiveTheme != null` |
| 40 | `ThemeAIGuidanceInjector` | Theme | `AiGuidanceNotes.Count > 0` |
| 50 | `IntensityContractInjector` | Engine | Always |
| 60 | `EscalationInjector` | Theme | `!HasProfileDirective && Intent != Instruction` |
| 65 | `DirectorNoteInjector` | Theme | `HasProfileDirective` |
| 70 | `SceneTimeDirectionInjector` | Theme | `!HasProfileDirective` |
| 80 | `PositionListInjector` | Engine | Position list available in session |
| 90 | `BeatStageInjector` | Theme | `BeatScope == Extended` |
| 100 | `FinalDirectiveInjector` | Engine | Always (fast pacing reinforcement only when `Pacing == Fast` and `Intent == Message`) |

> **Override mechanism**: When `HasProfileDirective` is true (profile-configured DirectorNote present), `EscalationInjector` and `SceneTimeDirectionInjector` are suppressed. Only `DirectorNoteInjector` (priority 65) fires for beat/time direction. **End-of-prompt authority**: `FinalDirectiveInjector` (priority 100) is the last thing the LLM reads before generating — its HARD CONSTRAINT text carries maximum weight.

---

## Phase Defaults (Tier 3 — used when no marker is present)

| Phase | Default Pacing | Default BeatScope | Default TimeShift |
|-------|---------------|-------------------|-------------------|
| Opening | Medium | Short | Small |
| BuildUp | Medium | Short | Small |
| Committed | Medium | Short | Small |
| Approaching | Medium | Short | Small |
| Climax | **Fast** | Short | **Medium** |
| Reset | **Slow** | **Single** | **None** |

These are hardcoded in `SceneDirectionResolver.PhaseDefaultPacingMap`, `PhaseDefaultBeatScopeMap`, and `PhaseDefaultTimeShiftMap`.

---

## Engine-Owned (Always Present, Not Marker-Gated)

The following are **not controlled by markers**. They are structural parts of every prompt:

| Section | Source Location | When |
|---------|----------------|------|
| System header | `BuildPromptAsync` line ~451 | Always: "You are continuing an interactive role-play scene." |
| Persona Role/Relation | Inline | Always |
| Scenario data | Inline | When scenario is bound (name, plot, setting, characters, locations, objects, style, intensity) |
| Interaction history | Inline | Always: `session.GetContextView().TakeLast(windowSize)` |
| Session memory | Inline | When encounter summaries exist |
| Scene continuity anchor | Inline | When location services enabled |
| Adaptive character stats | Inline | When stats exist |
| Active theme tracker | Inline | When theme scores exist (scores, evidence, selection rule) |
| Scenario guidance context | Inline | Always when scenario bound |
| Theme AI guidance notes | Inline (`AppendThemeAIGuidance`) | When theme exists and AI notes enabled |
| Theme hard constraints | Inline | When constraints exist |
| Profile theme tiers | Inline | When theme profile is set (must-have, prefer, dislike, dealbreaker) |
| Steer guidance | Inline | When `/steer` instruction is in prompt |
| Time skip guidance | Inline | When `/timeskip` instruction or multi-encounter encounter completion |
| Active instruction (persistent) | Inline | When recent instruction in history |
| Perspective instruction | Inline | Based on `ResolvePerspectiveMode` — controls 1st/3rd person POV |

---

## Diagnostic Checklist

When investigating why a prompt doesn't have the expected text:

1. **Check that the theme guidance has the marker**: Query the DB:
   ```sql
   SELECT pg.Phase, pg.GuidanceText FROM RPThemePhaseGuidance pg
   JOIN RPThemes t ON pg.ThemeId = t.Id
   WHERE t.Id = '<themeId>';
   ```

2. **Check that the resolver produces the expected SceneDirection**: Verify the marker is in the current phase's guidance (markers are phase-scoped — `[Pacing:fast]` in Climax only activates during Climax).

3. **Check that the injector's ShouldFire returns true**:
   - `EscalationInjector`: requires `!HasProfileDirective && Intent != Instruction`
   - `DirectorNoteInjector`: requires `HasProfileDirective` (supersedes Escalation + SceneTime)
   - `BeatStageInjector`: requires `BeatScope == Extended`

4. **Check that no override is suppressing the injector**: Profile-configured `DirectorNote` suppresses `EscalationInjector` and `SceneTimeDirectionInjector`.

5. **Check that the prompt is being rebuilt with new code**: DB stores prompt text from before the current build. Restart the app and generate a new turn to see updated injector text.

6. **Check the end of the prompt for maximum authority**: `FinalDirectiveInjector` (priority 100) is the last injector. Its HARD CONSTRAINT text has the highest authority.
