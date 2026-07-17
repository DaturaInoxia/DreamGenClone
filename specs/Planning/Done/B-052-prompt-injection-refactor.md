# Plan: B-052 — Full Prompt Injection Refactor

## TL;DR

Centralize the 37+ independently-added prompt injects into a coordinated service with a clear split: **the engine owns turn structure** (Actor 1 sets anchor, Actor 2+ follows), **themes own narrative behavior** (markers, phase guidance prose, AI Guidance Notes). Refactor `BuildPromptAsync` from a 1000-line procedural pipeline into a priority-sorted injector loop orchestrated by `SceneDirectionCoordinator`. Resolve every known contradiction (Time Span Reminder vs Location Continuity, Scene Deepening vs Pacing, BuildUp guard vs time advancement) by replacing hardcoded phase detection with marker-driven decisions.

## Architecture: What the Engine Owns vs What Themes Control

### Hard Constraint: Intensity Is Style, Not Behavior

Intensity controls **HOW** content is written (vocabulary, anatomical detail, explicitness), never **WHAT** can happen narratively. No injector may gate on intensity level to decide whether to fire, suppress, or modify its text. The `IntensityContractInjector` injects the style contract and nothing else. Phase guidance and markers own narrative behavior.

### Hard Constraint: Phase Is Data Selection, Not Behavioral Branching

Injectors may read `context.Phase` **only to select theme data** (which phase's guidance prose to inject, which markers apply). They must **never** branch on phase to emit hardcoded C# text. This distinction:
- ✅ **Allowed**: `ThemeContractInjector` reads `context.Phase` to select the right phase's guidance prose from the active theme
- ❌ **Forbidden**: `if (phase == "Climax") { sb.Append("hardcoded text") }`

The `Phase` field exists on `PromptInjectionContext` for data-selection purposes only. Any injector that reads it must justify why phase-aware data selection is necessary (documented in its `// <summary>`).

### Engine-Owned (hardcoded, structural — not marker-gated)

The engine enforces the **Turn Structure Contract**. This is baked into all continuation prompts because it's a structural fact about how turns work, not a stylistic choice:

```
Turn N:
  Actor 1 (position 1)   → sets the anchor: time, location, beat.
                            Time Span Reminder fires. May advance.
  Actor 2 (position 2..N) → follows the anchor by default.
                            Location Continuity fires. No time/location shift
                            unless markers override.
  ...
  Narrative               → closes the turn, may set up next anchor.
```

| Structural Rule | How It's Enforced |
|---|---|
| Actor 1 can advance time/location | **Baked in** — Time Span Reminder fires for position 1 |
| Actor 2+ follows the anchor by default | **Baked in** — Location Continuity fires for position > 1; Time Span Reminder suppressed |
| Turn Context: "response X of N, then a narrative close" | **Baked in** — positional context text |
| "Continue from your character's perspective" for NPC turns | **Baked in** — perspective instruction |
| Intensity governs style, not behavior | **Baked in** — Intensity Writing Contract injected in every prompt: "This governs WRITING STYLE and EXPLICITNESS LEVEL only — it does not override Phase Guidance. Phase Guidance specifies WHAT beats must occur; intensity specifies HOW they are written." |

### Theme-Controlled (markers, phase guidance prose, AI notes)

Everything about *how* the narrative behaves within that structure comes from the theme:

| Narrative Behavior | Controlled By |
|---|---|
| How fast Actor 1 advances beats | `[Pacing:slow\|medium\|fast]` marker |
| Whether Actor 1 can shift time at all | `[TimeShift:within-timeframe]` marker |
| Whether Actor 2+ can also advance | `[Pacing:fast]` or `[TimeShift:*]` marker |
| Whether Actor 2+ must only deepen POV | `[Deepening:subsequent-actors]` marker |
| What content is appropriate for the current phase | Phase guidance **free-text prose** (BuildUp tone, Climax intensity, etc.) |
| Hard constraints on character behavior | AI Guidance Notes (HardConstraint section) |
| Turn-level override of all pacing/beat/time | Profile-configured `SceneDirective.DirectorNote` |

**Pacing is turn-level**, not position-specific. `[Pacing:slow]` means Actor 1 advances one beat with deep sensory detail, then Actor 2+ deepen from POV of that same beat. `[Pacing:fast]` means Actor 1 compresses multiple beats, and Actor 2+ may also advance rapidly. `[Deepening:subsequent-actors]` is orthogonal — when present, it overrides the Actor 2+ behavior regardless of pacing: Actor 2+ always deepens from POV, never advances beat/position, even under fast pacing.

## Background — Session efcbf70f: Real Root Cause

The session `efcbf70f` (Campground Intimacy) exhibited a timeline split: Dean wrote a mid-afternoon beach scene, Becky wrote a dawn bedroom scene immediately after.

**What actually happened** (confirmed via prompt text extraction):
1. The Narrative (ix [25]) established a **dual-frame**: dawn bedroom + "Ten hours later" beach foreshadow
2. Dean (ix [26], position 1) received the Time Span Reminder + Turn Context "response 1 of 2" — he jumped to mid-afternoon beach. This was correct: as position 1, he can set the anchor.
3. Becky (ix [27], position 2) received Dean's beach scene in her interaction history, BUT also received:
   - **Location Continuity HC**: "maintain the previous physical setting"
   - **Time Span Reminder**: "scenes may skip forward; does not have to be immediate continuation"
   - **BuildUp guard**: "tension and anticipation only — do not write explicit sexual acts"
   - **Turn Context**: "Continue from your character's perspective"

**Root cause**: The BuildUp guard (hardcoded to `phase == BuildUp`) made writing Becky at the beach — in a swimsuit, mid-afternoon, alone with Dean — feel like it would violate "no explicit sexual acts." The LLM chose the safer dawn bedroom frame, which was still in context from the Narrative's dual-frame. The contradictory directives (Location Continuity vs Time Span Reminder) gave the LLM permission to make either choice. The BuildUp guard made the choice for it.

**The fix isn't a single marker** — it's the Turn Structure Contract:
- Dean (position 1) set the anchor → mid-afternoon beach. Correct.
- Becky (position 2) should have been told: "The scene is now at the mid-afternoon beach. Location Continuity applies. Do not shift time or location." She should have written herself INTO the beach — swimsuit on, tension building, internal reflection — which is valid BuildUp content (tension and anticipation, not explicit acts).
- The BuildUp guard is fine as phase guidance prose. The bug was that Becky was allowed to ignore the anchor Dean set.

## Phase 1: Audit — Catalog Every Prompt Inject

**Goal**: Create a single source-of-truth catalog of all 37+ injects. No changes — pure documentation.

**Steps**:
1. Create `specs/Planning/prompt-injection-catalog.md` with fields for each inject:
   - `id`, `type` (HARD CONSTRAINT / soft directive / contract / guidance), `sourceFile` + line range
   - `exactText` (template with placeholders documented)
   - `currentConditions` (phase, intent, positionInTurn, etc.)
   - `desiredControl` (marker? directive? AI note? structural?)
   - `position` in assembly order
   - `conflictsWith`, `dependsOn`
2. Grep-confirm no missing injects
3. Map current assembly order as dependency graph

**Files**: `specs/Planning/prompt-injection-catalog.md` (new)
**Dependency**: None (parallel-safe)

## Phase 2: Conflict Resolution — Marker Definitions & Rules

### 2a. Turn Structure Contract (Engine-Owned — Baked Into Every Prompt)

These are NOT markers. They are structural facts the engine enforces:

| Position | Inject Fired | Text |
|---|---|---|
| Position 1 | Time Span Reminder | "You are the first response this turn. You may establish or shift the time and location for this turn. Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment." |
| Position > 1 | Location Continuity HC (enhanced) | "The scene is now at the time and location established by the first response this turn. Maintain this physical setting. Do not silently relocate any character. If a character moves, write the transition explicitly." |
| Position > 1 AND no override markers | *(no time shift text)* | Location Continuity is the sole authority |
| Position > 1 AND `[Pacing:fast]` or `[TimeShift:*]` | Modified Time Span | "You may also shift time or location, following the pacing and time shift rules." |

### 2b. Markers (Theme-Controlled)

| Marker | Controls | Applies To |
|---|---|---|
| `[Pacing:slow\|medium\|fast]` | Beat advancement speed per turn. **Turn-level** — affects all actors. | All positions |
| `[TimeShift:within-timeframe]` | Whether time can be advanced at all (for position 1). Whether position 2+ may also advance time. | All positions |
| `[Deepening:subsequent-actors]` | When present, Actor 2+ deepens from POV only — never advances beat/position. **Orthogonal to pacing**. Overrides position 2+ advancement regardless of `[Pacing:*]`. | Position > 1 only |
| `[ClimaxMode:quick-finish]` | Quick-release, concealment-focused Climax framing (already works) | All positions |
| `[BeatStyle:episodic]` | Whether Beat Stage Context is injected | All positions |

> **Marker resolution scope**: Markers are read from **the current phase's guidance lines only**, not from all phases. `SceneDirectionResolver` passes only the current phase's guidance to marker parsers. This means `[Pacing:fast]` in a Climax phase guidance only activates during Climax. If no marker is found in the current phase guidance, the resolver falls back to phase defaults (see Phase 3).

### 2c. Pacing Effects (Full Table)

| Pacing | Actor 1 | Actor 2+ (no markers) | Actor 2+ (with `[Deepening:subsequent-actors]`) |
|---|---|---|---|
| `slow` | Advance 1 beat. Linger in sensory/emotional detail. No time shift. | Follow anchor. May also linger. Small reflection ok. | Deepen same beat from POV. No advancement. |
| `medium` | Advance 1-2 beats. Reasonable transitions. Small time shift ok. | Follow anchor. May lightly advance. | Deepen same beat from POV. No advancement. |
| `fast` | Compress multiple beats. Jump time/location freely. | May also advance rapidly. | Deepen from POV only — cannot advance even under fast pacing. |
| *(no marker)* | Default to `medium` behavior | Default to `medium` behavior | N/A (marker must be present) |

### 2d. Escalation Guidance (replaces current hardcoded Climax text)

| Pacing | Text |
|---|---|
| `slow` | "Advance within the same beat — deepen, do not leap. Fill the response with sensory, emotional, and physical detail specific to this moment. Do not describe a new beat or position." |
| `medium` | "Advance the scene with forward momentum. Cover one to two beats this response. Avoid repeating only hesitant or reset beats." |
| `fast` | "Compress multiple beats into this response. Advance to a new beat or position. Do not describe the same act or position that was the focus of the previous response. Every response should shift something concrete." |

### 2e. Merged Scene Time Direction (replaces Scene Pacing Contract + Pacing Directive)

Both old injects describe time and pacing — they are merged into a single injector that reads `SceneDirection.Pacing` and `SceneDirection.TimeShift`:

| TimeShift | Pacing | Text |
|---|---|---|
| `None` | `slow` | "Stay in the current moment. Do not skip forward. Savor the moment with detailed sensory and emotional depth. One beat per response." |
| `None` | `medium` | "Let the scene breathe without dragging. Cover one to two beats per response. No time shift — continue from the current moment." |
| `None` | `fast` | "Compress multiple beats into one response. Cover more story ground per response. No time shift — all beats occur within the current timeframe." |
| `Small/Medium/Large` | `slow` | "Focus on one beat per response. Time may advance naturally to the next moment. Use organic transitions." |
| `Small/Medium/Large` | `medium` | "Cover one to two beats per response. Time may advance naturally — let transitions feel organic." |
| `Small/Medium/Large` | `fast` | "Compress multiple beats. Time may advance significantly — cover more story ground. Use clear transitions." |

### 2f. Phase Guidance — Free-Text Prose (Not Markers)

The following currently hardcoded constraints become **free-text in theme phase guidance prose**. No markers needed — the LLM reads them as narrative direction:

- BuildUp tone ("tension and anticipation only")
- Climax intensity/urgency expectations
- Scenario-alignment reminders ("keep all beats aligned to the scenario")
- Any phase-specific content that is stylistic advice, not structural behavior

The `RolePlayAssistantPrompts.BuildFramingGuards()` method (currently phase-branched) is retired. Its content moves into the theme's phase guidance fields where it belongs.

**Files**: `specs/Planning/prompt-injection-catalog.md` (updated)
**Dependency**: Depends on Phase 1
**Verification**: Every conflict has a marker or prose-based resolution. No `if (phase == ...)` in any injector condition.

### 2g. Phase Guidance Prose Migration

**Goal**: Move every string currently hardcoded in `BuildFramingGuards()` into theme phase guidance prose fields, then migrate seed data.

**Steps**:
1. Enumerate every string emitted by `BuildFramingGuards()` (BuildUp tone, Climax intensity, scenario alignment, quick-finish mode, multi-encounter mode)
2. Map each to the corresponding theme phase-guidance field
3. Write seed data migration — update all existing theme records so the prose lives in the theme, not in C#
4. Verify no `BuildFramingGuards()` call site remains after migration (it is fully retired)

**Files**: Theme seed data files (multiple)
**Dependency**: Depends on Phase 2a-2f (marker definitions must be stable first)
**Verification**: `BuildFramingGuards()` is deleted. No hardcoded phase text remains in C#. Every theme has its phase guidance prose in the database.

## Phase 3: Build the SceneDirectionResolver

**Goal**: Complete the `SceneDirectionResolver.Resolve()` method so it produces a fully-resolved `SceneDirection` from the 3-tier precedence: profile directive > theme markers > phase defaults. This is the load-bearing data source for every injector in Phase 4.

**Current state**: `SceneDirectionResolver.cs` is a 62-line scaffold. Its `Resolve()` method calls 5 helper methods that do not exist: `NormalizePhase`, `ResolvePacing`, `ResolveBeatScope`, `ResolveTimeShift`, `SanitizeNote`. It references `PhaseDefaultPacing`, `PhaseDefaultBeatScope`, `PhaseDefaultTimeShift` constants that are also undefined. It is never called anywhere in production or tests.

**Steps**:
1. Define `PhaseDefaultPacing`, `PhaseDefaultBeatScope`, `PhaseDefaultTimeShift` — one set per phase (BuildUp, Committed, Approaching, Climax, Reset, etc.)
2. Implement `NormalizePhase(string? phase)` — normalize phase string to canonical enum/constant
3. Implement `ResolvePacing(normalizedPhase, activeTheme, climaxSubPhase)` — tier 1: profile directive → tier 2: `[Pacing:*]` marker in current-phase guidance → tier 3: phase default
4. Implement `ResolveBeatScope(normalizedPhase, activeTheme, climaxSubPhase)` — same 3-tier pattern
5. Implement `ResolveTimeShift(normalizedPhase, activeTheme, climaxSubPhase)` — same 3-tier pattern. `[TimeShift:within-timeframe]` marker maps to `TimeShiftPolicy.Small` (or the resolved value).
6. Implement `SanitizeNote(string?)` — null-guard and whitespace trim
7. Add `Deepening` resolution: if current-phase guidance contains `[Deepening:subsequent-actors]`, set `DeepeningPolicy.SubsequentActors`
8. Wire a call site — before Phase 4's coordinator can use it, `Resolve()` must be called at least once in a test to validate the full pipeline

**SceneDirection record update**: Add a `Deepening` field:
```csharp
public sealed record SceneDirection
{
    public ScenePacing Pacing { get; init; } = ScenePacing.Medium;
    public BeatScope BeatScope { get; init; } = BeatScope.Short;
    public TimeShiftPolicy TimeShift { get; init; } = TimeShiftPolicy.Small;
    public ClimaxSubPhase ClimaxSubPhase { get; init; } = ClimaxSubPhase.None;
    public DeepeningPolicy Deepening { get; init; } = DeepeningPolicy.None;  // NEW
    public string DirectorNote { get; init; } = string.Empty;
    public bool HasProfileDirective => !string.IsNullOrWhiteSpace(DirectorNote);
}

public enum DeepeningPolicy { None = 0, SubsequentActors = 1 }
```

**Marker resolution scope** (design decision): The resolver scans **only the current phase's** guidance lines for markers. If `[Pacing:fast]` appears only in Climax guidance, it only activates during Climax. Other phases default to phase defaults. This matches the existing `GetThemePhaseGuidanceLines` pattern.

**Dependency**: Depends on Phase 2 (marker definitions must be stable)
**Verification**: `SceneDirectionResolver.Resolve()` returns correct values for representative inputs (BuildUp no markers → phase defaults; Climax with `[Pacing:fast]` + `[Deepening:subsequent-actors]` → Pacing=Fast, Deepening=SubsequentActors; profile directive present → DirectorNote populated, HasProfileDirective=true). `dotnet build` passes.

## Phase 4: Build the Coordinator Service

**Goal**: Implement `SceneDirectionCoordinator` — the bridge between `SceneDirectionResolver` (built in Phase 3) and `BuildPromptAsync`. Convert the ~11 behavioral injects into `IPromptInjector` implementations; ~23 data blocks stay inline in `BuildPromptAsync`.

**Injector scope**: Only the behavioral/structural injects become `IPromptInjector` implementations. The following ~23 data assembly blocks **stay inline** in `BuildPromptAsync` (scenario fetch, characters, locations, memory, stats, theme tracker, opening period guidance, steer guidance, time skip guidance, scenario priorities, theme profile, etc.). They are data sources, not behavioral decisions — the coordinator loop injects between them at priority-determined insertion points.

**Steps**:

### 4a. Define `IPromptInjector` interface
```csharp
public interface IPromptInjector
{
    string Id { get; }                          // Unique inject name
    int Priority { get; }                       // Order in assembly
    bool ShouldFire(PromptInjectionContext context);
    string BuildText(PromptInjectionContext context);
}
```

### 4b. Define `PromptInjectionContext` record
Contains **all resolved values** that injectors need — injectors read from context, they do NOT detect phase or resolve markers themselves:

```csharp
public sealed record PromptInjectionContext
{
    public RolePlaySession Session { get; init; }
    public SceneDirection SceneDirection { get; init; }   // Resolved from markers→directive→defaults
    public string Phase { get; init; }                     // Available but injectors SHOULD NOT branch on it
    public PromptIntent Intent { get; init; }
    public int? PositionInTurn { get; init; }
    public string ActorName { get; init; }
    public RPTheme? ActiveTheme { get; init; }
    public IReadOnlyDictionary<string, int>? ActorStats { get; init; }
    public IReadOnlyList<string> PhaseGuidanceLines { get; init; }       // Raw phase guidance text from theme
    public IReadOnlyList<string> PhaseDirectiveLines { get; init; }     // Directive lines from theme
    public IReadOnlyList<RPThemeAIGuidanceNote> AiGuidanceNotes { get; init; }
    public IReadOnlyList<RPThemeHardConstraint> ThemeHardConstraints { get; init; }
    public bool HasMarker(string marker)                                // Helper: checks theme phase guidance markers
        => PhaseGuidanceLines.Any(l => l.Contains($"[{marker}]"));
}
```

### 4c. Refactor each inject into an `IPromptInjector` implementation

Injectors read from `context.PositionInTurn` (structural, engine-owned) and `context.SceneDirection` (marker-resolved, theme-controlled). They do NOT branch on `context.Phase` to emit hardcoded text (see Architecture: Phase Is Data Selection, Not Behavioral Branching).

| Injector | What It Does | Control | Priority | Owned By |
|---|---|---|---|---|
| `TurnContextInjector` | "Response X of N, narrative closes after, continue from your perspective" | `context.PositionInTurn` | 5 | **Engine** |
| `TimeLocationInjector` | Position 1: Time Span Reminder. Position > 1: Location Continuity HC (enhanced with anchor). Position > 1 + marker overrides: modified time shift text. | `context.PositionInTurn` + `SceneDirection.TimeShift` + `SceneDirection.Pacing` | 10 | **Engine** structure, theme controls overrides |
| `BehavioralFrameInjector` | Character behavioral frames + stat state texts | Always fires | 20 | Engine |
| `ThemeContractInjector` | Active Adaptive Theme Contract block + phase guidance prose (BuildUp tone, Climax intensity, scenario alignment — all free-text from theme). Reads `context.Phase` to select phase-relevant guidance — allowed under data-selection rule. | Phase guidance prose from active theme | 30 | Theme |
| `ThemeAIGuidanceInjector` | Theme AI guidance notes + hard constraints | AI Guidance Notes | 40 | Theme |
| `IntensityContractInjector` | Resolved Intensity + Intensity Writing Contract | Always fires | 50 | Engine |
| `EscalationInjector` | Escalation Guidance text — varies by `SceneDirection.Pacing` (see 2d). If `SceneDirection.Deepening == SubsequentActors` and position > 1: scene deepening text instead. **ShouldFire returns false when `SceneDirection.HasProfileDirective` is true** (DirectorNoteInjector takes over). | `SceneDirection.Pacing` + `SceneDirection.Deepening` + `context.PositionInTurn` | 60 | Theme |
| `DirectorNoteInjector` | Profile-configured `SceneDirective.DirectorNote` — turn-level override of all pacing/beat/time. **Only fires when `SceneDirection.HasProfileDirective` is true.** | `SceneDirection.HasProfileDirective` + `SceneDirection.DirectorNote` | 65 | Theme |
| `SceneTimeDirectionInjector` | **Merged** Scene Pacing Contract + Pacing Directive (see 2e). **ShouldFire returns false when `SceneDirection.HasProfileDirective` is true** (DirectorNote takes over). | `SceneDirection.Pacing` + `SceneDirection.TimeShift` | 70 | Theme |
| `PositionListInjector` | Available Positions list — always fires when session has positions configured. Not marker-gated. | Session config (are positions assigned?) | 80 | Engine |
| `BeatStageInjector` | Beat Stage Context | `SceneDirection.BeatScope` (non-Single) | 90 | Theme |
| `FinalDirectiveInjector` | Final writing directive + perspective instruction | Intent-based (Message/Narrative/Instruction) | 100 | Engine |

> **Data blocks (~23) stay inline**: Scenario fetch, characters, locations, memory, stats, theme tracker, opening period, steer guidance, time skip guidance, scenario priorities, theme profile, etc. are data assembly — not behavioral injects. They remain in `BuildPromptAsync` as inline calls. The coordinator loop interleaves behavioral injectors at priority-determined points among these data blocks.

> **DirectorNote override mechanism**: When `HasProfileDirective` is true, `EscalationInjector.ShouldFire` and `SceneTimeDirectionInjector.ShouldFire` return `false`. Only `DirectorNoteInjector` fires, emitting the `DirectorNote` verbatim. This keeps the override clean — no branching inside injectors.

Note: `BuildFramingGuards()` is retired (migrated in Phase 2g). Its content (BuildUp tone, Climax intensity, scenario alignment) lives in theme phase guidance prose — injected by `ThemeContractInjector`.

**Key difference from v1**: NO injector has `if (phase == "Climax")` or `if (phase is "BuildUp" or "Reset")`. Instead:
- `EscalationInjector` checks `SceneDirection.Pacing` (resolved from `[Pacing:*]` marker)
- `TimeLocationInjector` checks `context.PositionInTurn` + `SceneDirection.TimeShift`
- `SceneTimeDirectionInjector` checks `SceneDirection.Pacing` and `SceneDirection.TimeShift`

If a theme has no markers, `SceneDirectionResolver` falls back to phase defaults (those still exist in the resolver as a safety net). But the injectors themselves don't know about phases.

### 4d. Wire `SceneDirectionCoordinator` into `BuildPromptAsync`
Replace the ~1000-line procedural pipeline with a loop over registered injectors sorted by priority:

```csharp
// Build context once
var sceneDir = SceneDirectionResolver.Resolve(phase, activeTheme, ...);
var context = new PromptInjectionContext { Session = session, SceneDirection = sceneDir, ... };

// One loop replaces 1000 lines of if/else
foreach (var injector in _injectors.OrderBy(i => i.Priority))
{
    if (injector.ShouldFire(context))
    {
        sb.Append(injector.BuildText(context));
    }
}
```

### 4e. SceneDirectionResolver — built in Phase 3
`SceneDirectionResolver.Resolve()` returns a fully-resolved `SceneDirection` from 3-tier precedence: profile directive > theme markers > phase defaults. It was built in Phase 3. This phase wires it as the single source of truth for all injectors.

### 4f. Marker summary

| Marker | Controls | Applies To |
|---|---|---|
| `[Pacing:slow\|medium\|fast]` | Beat advancement speed per turn. Turn-level — all actors. | All positions |
| `[TimeShift:within-timeframe]` | Whether time can be advanced (position 1). Whether position 2+ may advance. Also controls `SceneDirection.TimeShift` for `SceneTimeDirectionInjector`. | All positions |
| `[Deepening:subsequent-actors]` | Position 2+ deepens from POV only — never advances beat/position. Orthogonal to pacing. | Position > 1 |
| `[ClimaxMode:quick-finish]` | Quick-release, concealment-focused Climax framing | All positions |
| `[BeatStyle:episodic]` | Whether Beat Stage Context is injected | All positions |

Position list injection is **not marker-gated** — it fires whenever the session has positions configured. It's data, not behavioral control.

Seed data: existing themes need `[Deepening:subsequent-actors]` added to preserve current Climax behavior. Phase guidance prose stays as-is.

**Files modified**:
- `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs` — full implementation (currently stub)
- `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs` — new context record
- `DreamGenClone.Web/Application/RolePlay/IPromptInjector.cs` — new interface
- `DreamGenClone.Web/Application/RolePlay/Injectors/*.cs` — ~11 new injector files
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — replace prompt assembly loop, remove redundant Append* methods
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — strip phase-branching from `BuildFramingGuards` (move guard text into theme phase guidance prose where it belongs), keep utility helpers
- `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs` — already clean, no changes needed
- Theme seed data — add `[Deepening:subsequent-actors]` marker to existing theme phase guidance

**Dependency**: Depends on Phase 3 (SceneDirectionResolver must be built and unit-tested first)
**Verification**: Build succeeds (0 errors). Existing prompt assertions in tests still pass. No `if phase ==` in any injector (except `ThemeContractInjector` selecting phase-relevant data — documented and justified). `SceneDirectionResolver.Resolve()` is wired and its output is the single source of truth for all injectors.

## Phase 5: Inject Text Audit — Verify Output Parity

**Goal**: Verify refactored injectors produce correct prompt output. Exact text equivalence is impossible by design (the refactor intentionally merges/rewrites text — merged Scene Time Direction, retired `BuildFramingGuards`). Instead, use structural + regression + negative assertions.

**Steps**:
1. **Structural parity test**: For a representative session state, assert the same set of behavioral directives appears in the prompt (Time Span Reminder, Location Continuity, Escalation Guidance, Scene Time Direction, etc.) even if wording differs.
2. **Must-appear assertions**: Specific strings that MUST still appear (e.g., "maintain this physical setting" for position > 1, "response X of N" turn context).
3. **Must-NOT-appear assertions**: Strings that must be absent — no contradictory Time Span Reminder + Location Continuity for position > 1 without override markers, no hardcoded phase-branching artifacts.
4. Run ALL existing prompt tests (SceneWritingDirectivePromptTests, SessionMemoryInjectionTests, etc.)
5. Run `dotnet test DreamGenClone.Tests` — verify no regressions beyond the 15 known pre-existing failures

**Dependency**: Depends on Phase 4
**Verification**: Structural parity test passes (same directives present). Must-appear strings found. Must-NOT strings absent. All existing prompt tests pass. `dotnet test` shows 0 new failures.

## Phase 6: Session efcbf70f — Regression Test

**Goal**: Specific regression test that proves the timeline-split bug is fixed.

**Steps**:
1. Create a test that simulates the efcbf70f state — a session with `[TimeShift:within-timeframe]` marker and one without
2. Assert that WITH the marker → Time Span Reminder is emitted (forward skips allowed)
3. Assert that WITHOUT the marker → Time Span Reminder is suppressed, only Location Continuity fires
4. Assert that parallel NPC generation produces timeline-consistent prompts (no contradictory directives)

**Dependency**: Depends on Phase 4-5
**Verification**: Test passes. Prompt shows unambiguous time/location authority — no contradictory directives.

## Phase 7: Documentation — Update Prompt Injection Catalog

**Goal**: The catalog becomes a living document kept in sync with the code.

**Steps**:
1. Update `specs/Planning/prompt-injection-catalog.md` to reflect the new injector architecture
2. Add Mermaid diagram showing: theme markers → SceneDirectionResolver → Coordinator → Injectors → Prompt
3. Document the marker convention: `[MarkerName:value]` in theme phase guidance
4. Add a `// <summary>` to each `IPromptInjector` implementation referencing the catalog ID

**Dependency**: Depends on Phase 4-5
**Verification**: Catalog is accurate. New injectors are all listed. Marker conventions documented.

## Relevant Files

- `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs` — **build out** (currently 62-line scaffold, 5 helper methods undefined) — Phase 3
- `DreamGenClone.Domain/RolePlay/SceneDirection.cs` — add `DeepeningPolicy` enum + `Deepening` field — Phase 3
- `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs` — full implementation (currently empty stub) — Phase 4
- `DreamGenClone.Web/Application/RolePlay/IPromptInjector.cs` — new interface — Phase 4
- `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs` — new context record — Phase 4
- `DreamGenClone.Web/Application/RolePlay/Injectors/*.cs` — ~12 new injector files (11 behavioral + DirectorNoteInjector) — Phase 4
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — primary target (refactor `BuildPromptAsync`, remove redundant `Append*` methods) — Phase 4
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — strip phase-branching from `BuildFramingGuards` (move guard text into theme phase guidance prose), keep utility helpers — Phase 4
- `DreamGenClone.Tests/RolePlay/SceneWritingDirectivePromptTests.cs` — existing tests to preserve — Phase 5
- `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs` — existing tests to preserve — Phase 5
- `DreamGenClone.Tests/RolePlay/PromptSanitizerTests.cs` — existing tests to preserve — Phase 5
- `DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs` — existing tests + new regression test — Phase 6
- `DreamGenClone.Tests/RolePlay/SceneDirectionResolverTests.cs` — **new** unit tests for resolver — Phase 3
- `DreamGenClone.Tests/RolePlay/PromptInjectorCaptureTests.cs` — **new** structural parity + negative assertion tests — Phase 5
- `specs/Planning/prompt-injection-catalog.md` — new audit catalog — Phase 1
- Theme seed data files — add `[Deepening:subsequent-actors]` marker + migrated phase guidance prose — Phase 2g

## Verification

1. `dotnet build DreamGenClone.sln` — 0 errors (Phases 3-7)
2. `dotnet test DreamGenClone.Tests` — 0 new failures (15 pre-existing behavioral failures tolerated)
3. `SceneDirectionResolver.Resolve()` returns correct values for representative inputs — Phase 3 gate
4. Existing prompt assertion tests pass unchanged — Phase 5
5. Structural parity test passes: same behavioral directives present, must-appear strings found, must-NOT strings absent — Phase 5
6. Regression test for session efcbf70f proves no contradictory timeline directives — Phase 6
7. No `if (phase == "Climax")` or similar hardcoded-phase-branching in any injector (data-selection reads allowed per Architecture rule) — Phase 4-7
8. No injector gates on intensity level — intensity is style, not behavior — Phase 4-7
9. `SceneDirectionResolver` is wired and its output is the single source of truth for all injectors — Phase 4 gate
10. `BuildFramingGuards()` is fully deleted; its prose lives in theme seed data — Phase 2g, Phase 4

## Decisions

- **In scope**: Audit catalog, Turn Structure Contract (engine-owned), marker definitions, `SceneDirectionResolver` build-out, `DeepeningPolicy` enum, `DirectorNoteInjector` override mechanism, coordinator service, 11 behavioral injectors + 23 inline data blocks, prose migration (Phase 2g), structural parity test (not text equivalence), efcbf70f regression test, `[Deepening:subsequent-actors]` marker in seed data
- **Out of scope**: New prompt injects, UI changes, new domain models beyond `DeepeningPolicy`, performance optimization, Narrative mode markers (deferred), converting ~23 data blocks to injectors (deferred)
- **Architecture**: Injector pattern + coordinator loop. Injectors read `context.PositionInTurn` (engine) and `context.SceneDirection` (marker-resolved). NO hardcoded phase-branching (data-selection reads allowed per Architecture rule). `DirectorNote` overrides via injector exclusion, not internal branching.
- **Engine vs Theme split**: Turn structure (position roles, time/location anchor) is engine-owned and baked into every prompt. Narrative behavior (pacing, beat advancement, deepening, positions) is theme-controlled via markers resolved onto `SceneDirection`.
- **Phase defaults in SceneDirectionResolver**: Kept as tier 3 safety net. Injectors never see them directly — they see only the resolved `SceneDirection`.
- **Marker scope**: Current-phase guidance lines only. Not global across all phases.
- **Phase as data selector**: Injectors may read `context.Phase` to select theme data (phase guidance prose). They may never branch on phase to emit hardcoded C# text.

## Further Considerations

1. **Seed data markers**: Existing themes need `[Deepening:subsequent-actors]` added to preserve current Climax behavior (position 2+ deepens from POV). Also add `DeepeningPolicy.SubsequentActors` to the `SceneDirection` resolution when this marker is detected.
2. **Seed data prose migration (Phase 2g)**: Every string currently in `BuildFramingGuards()` must be migrated to theme phase guidance fields. Recommend a DB query to audit all themes' current phase guidance, then a scripted seed update.
3. **`SceneDirection` record update**: Adding `DeepeningPolicy` enum and `Deepening` field touches the Domain layer — coordinate with any other branches touching `SceneDirection.cs`.
4. **`BeatScope` and `ClimaxSubPhase` usage**: Currently resolved by `SceneDirectionResolver` but only `BeatScope` maps to `BeatStageInjector`. `ClimaxSubPhase` (Early/Mid/Late) has no dedicated injector in v1 — it's available for future use but not consumed by any current injector. Document this intentional gap.
5. **Order sensitivity**: LLM prompts are order-sensitive. The coordinator loop interleaves behavioral injectors at priority-determined points among inline data blocks. Validate prompt ordering doesn't regress LLM behavior.
6. **`PositionInTurn` source**: This is a pass-through of the existing `BuildPromptAsync` parameter `positionInTurn`, computed upstream in `RolePlayEngineService`. `PromptInjectionContext.PositionInTurn` copies it — no re-derivation.
