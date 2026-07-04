# Injector Catalog & Contracts

**Phase**: 1 — Design & Contracts
**Date**: 2026-06-29
**Feature**: [spec.md](../spec.md)

## Interface Contract

Every behavioral injector implements:

```csharp
public interface IPromptInjector
{
    string Id { get; }                           // Unique identifier (kebab-case, e.g., "turn-context")
    int Priority { get; }                        // Assembly order (lower = earlier)
    bool ShouldFire(PromptInjectionContext context);
    string BuildText(PromptInjectionContext context);
}
```

## Injector Contracts

### 1. TurnContextInjector (Priority: 5) — Engine

**Id**: `turn-context`  
**ShouldFire**: `context.PositionInTurn.HasValue`  
**Text pattern**: "This is response {position} of {total} in this turn. The turn closes with a narrative response after all player responses."  
**Position-specific suffix**:
- Position 1: "You are the first response this turn."
- Position 2..N: "Continue from your character's perspective."
- Last position: — (no suffix, turn-close implied)

### 2. TimeLocationInjector (Priority: 10) — Engine (theme controls overrides)

**Id**: `time-location`  
**ShouldFire**: Always  
**Text** (position 1): "You are the first response this turn. You may establish or shift the time and location for this turn. Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment."  
**Text** (position > 1, no override markers): "The scene is now at the time and location established by the first response this turn. Maintain this physical setting. Do not silently relocate any character. If a character moves, write the transition explicitly."  
**Text** (position > 1, with `[Pacing:fast]` or `[TimeShift:*]` marker): Location Continuity text + "You may also shift time or location, following the pacing and time shift rules."

### 3. BehavioralFrameInjector (Priority: 20) — Engine

**Id**: `behavioral-frame`  
**ShouldFire**: Always  
**Text**: Character behavioral frames + stat state texts. Reads from `context.Session` behavioral frames.

### 4. ThemeContractInjector (Priority: 30) — Theme

**Id**: `theme-contract`  
**ShouldFire**: `context.ActiveTheme != null`  
**Text**: 
- Active Adaptive Theme Contract block header
- Phase guidance prose from active theme (selected by `context.Phase`)
- This is the ONLY injector that may read `context.Phase` for data selection — documented and justified

### 5. ThemeAIGuidanceInjector (Priority: 40) — Theme

**Id**: `theme-ai-guidance`  
**ShouldFire**: `context.AiGuidanceNotes.Count > 0`  
**Text**: AI Guidance Notes rendered by section, with HardConstraint section highlighted.

### 6. IntensityContractInjector (Priority: 50) — Engine

**Id**: `intensity-contract`  
**ShouldFire**: Always  
**Text**: "This governs WRITING STYLE and EXPLICITNESS LEVEL only — it does not override Phase Guidance. Phase Guidance specifies WHAT beats must occur; intensity specifies HOW they are written."  
Followed by resolved intensity description and writing contract.

### 7. EscalationInjector (Priority: 60) — Theme

**Id**: `escalation`  
**ShouldFire**: `!context.SceneDirection.HasProfileDirective && context.ActorStats != null && context.Intent != PromptIntent.Instruction`  
**Text** (varies by `SceneDirection.Pacing`):

| Pacing | Text |
|--------|------|
| Slow | "Advance within the same beat — deepen, do not leap. Fill the response with sensory, emotional, and physical detail specific to this moment. Do not describe a new beat or position." |
| Medium | "Advance the scene with forward momentum. Cover one to two beats this response. Avoid repeating only hesitant or reset beats." |
| Fast | "Compress multiple beats into this response. Advance to a new beat or position. Do not describe the same act or position that was the focus of the previous response. Every response should shift something concrete." |

**If `SceneDirection.Deepening == SubsequentActors` and position > 1**: Replace text with deepening-from-POV guidance.

### 8. DirectorNoteInjector (Priority: 65) — Theme

**Id**: `director-note`  
**ShouldFire**: `context.SceneDirection.HasProfileDirective`  
**Text**: `context.SceneDirection.DirectorNote` (verbatim).  
This injector fires INSTEAD of EscalationInjector and SceneTimeDirectionInjector (those return false from ShouldFire when `HasProfileDirective` is true).

### 9. SceneTimeDirectionInjector (Priority: 70) — Theme

**Id**: `scene-time-direction`  
**ShouldFire**: `!context.SceneDirection.HasProfileDirective`  
**Text** (varies by `SceneDirection.Pacing` and `SceneDirection.TimeShift`):

| TimeShift | Pacing | Text |
|-----------|--------|------|
| None | Slow | "Stay in the current moment. Do not skip forward. Savor the moment with detailed sensory and emotional depth. One beat per response." |
| None | Medium | "Let the scene breathe without dragging. Cover one to two beats per response. No time shift — continue from the current moment." |
| None | Fast | "Compress multiple beats into one response. Cover more story ground per response. No time shift — all beats occur within the current timeframe." |
| Small/Medium/Large | Slow | "Focus on one beat per response. Time may advance naturally to the next moment. Use organic transitions." |
| Small/Medium/Large | Medium | "Cover one to two beats per response. Time may advance naturally — let transitions feel organic." |
| Small/Medium/Large | Fast | "Compress multiple beats. Time may advance significantly — cover more story ground. Use clear transitions." |

### 10. PositionListInjector (Priority: 80) — Engine

**Id**: `position-list`  
**ShouldFire**: Session has positions configured (non-empty position list)  
**Text**: Available positions list — "Available positions: {list}". Rendered from session data.

### 11. BeatStageInjector (Priority: 90) — Theme

**Id**: `beat-stage`  
**ShouldFire**: `context.SceneDirection.BeatScope != BeatScope.Single`  
**Text**: Beat Stage Context — episodic climax beat hints and instructions. Reads from theme's `[BeatStyle:episodic]` marker-driven beat data.

### 12. FinalDirectiveInjector (Priority: 100) — Engine

**Id**: `final-directive`  
**ShouldFire**: Always  
**Text**: Final writing directive based on `context.Intent`:
- Message → Perspective instruction: "Continue from your character's perspective."
- Narrative → Narrative close instruction
- Instruction → Instruction-specific directive

## Retired Injects

### BuildFramingGuards (fully deleted)

Previously emitted ~30 hardcoded phase-branched strings. All prose migrated to `RPThemePhaseGuidance.GuidanceText` fields and injected by `ThemeContractInjector`.

### ScenePacingContract + PacingDirective (merged)

Both old injects described time and pacing. Merged into `SceneTimeDirectionInjector` (single 6-case table).

## Non-Injector Inline Data Blocks (NOT converted)

The following remain inline in `BuildPromptAsync` as data assembly calls (not behavioral injects):

1. System header
2. POV Persona description
3. Behavioral Rules (intimate attributes)
4. Scenario Data (name, plot, setting, characters, locations, objects)
5. Style Profile
6. Interaction History
7. Session Memory (EncounterSummaryRecord)
8. Scene Continuity Anchor (character locations/perceptions)
9. Adaptive Character Stats
10. Active Theme Tracker (scores + evidence)
11. Scenario Guidance Context
12. Opening Period Guidance
13. Secondary Theme AI Guidance (Top2Blend)
14. Candidate Theme Menu (observing mode)
15. Steer Guidance
16. Time Skip Guidance
17. Profile Theme Tiers (must-have, prefer, dislike, dealbreaker)
18. Active Instruction (re-injected non-steer instruction)
19. Prompt Text (user's current input)
20. Behavioral Frame HCs (re-injection at end)
21. Theme Hard Constraint HCs (re-injection at end)
22. World Rule HCs (re-injection at end)
23. Scene Location Lock (currently behavioral but stays inline as single line)

## Data Flow

```
Theme Phase Guidance (DB)
  │
  ▼
SceneDirectionResolver ──► SceneDirection ──► PromptInjectionContext
  │                          (Pacing,            │
  │  [Pacing:*] marker        BeatScope,          ├── Session
  │  [TimeShift:*] marker     TimeShift,          ├── SceneDirection
  │  [Deepening:*] marker     Deepening,          ├── Phase
  │  [BeatStyle:*] marker     ClimaxSubPhase,     ├── PositionInTurn
  │                            DirectorNote)      ├── PhaseGuidanceLines
  │                              │                ├── AiGuidanceNotes
  │                              │                └── ThemeHardConstraints
  │                              │
  ▼                              ▼
SceneDirectionCoordinator ──► IPromptInjector[] loop
                                │
                        ┌───────┼───────────────┐
                        ▼       ▼               ▼
                 TurnContext  ThemeContract  Escalation  ...
                 (pos 5)      (pos 30)       (pos 60)
                        │       │               │
                        └───────┼───────────────┘
                                ▼
                         StringBuilder → Prompt text
```
