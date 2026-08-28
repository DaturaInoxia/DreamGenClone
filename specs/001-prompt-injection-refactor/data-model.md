# Data Model: Full Prompt Injection Refactor

**Phase**: 1 — Design & Contracts
**Date**: 2026-06-29
**Feature**: [spec.md](spec.md)

## Domain Entities

### SceneDirection (existing, modified)

**File**: `DreamGenClone.Domain/RolePlay/SceneDirection.cs`

**Change**: Add `DeepeningPolicy` enum + `Deepening` field.

```csharp
// NEW enum
public enum DeepeningPolicy
{
    None = 0,
    SubsequentActors = 1   // Position 2+ deepens from POV only, never advances beat/position
}

// Existing record — add Deepening field
public sealed record SceneDirection
{
    public ScenePacing Pacing { get; init; } = ScenePacing.Medium;
    public BeatScope BeatScope { get; init; } = BeatScope.Short;
    public TimeShiftPolicy TimeShift { get; init; } = TimeShiftPolicy.Small;
    public ClimaxSubPhase ClimaxSubPhase { get; init; } = ClimaxSubPhase.None;
    public DeepeningPolicy Deepening { get; init; } = DeepeningPolicy.None;   // NEW
    public string DirectorNote { get; init; } = string.Empty;
    public bool HasProfileDirective => !string.IsNullOrWhiteSpace(DirectorNote);
}

// Existing enums — unchanged:
public enum ScenePacing { Slow = 0, Medium = 1, Fast = 2 }
public enum BeatScope { Single = 0, Short = 1, Extended = 2 }
public enum TimeShiftPolicy { None = 0, Small = 1, Medium = 2, Large = 3 }
public enum ClimaxSubPhase { None = 0, Early = 1, Mid = 2, Late = 3 }
```

**Validation rules**:
- `DeepeningPolicy.None` is the default (no deepening constraint)
- `DeepeningPolicy.SubsequentActors` has no effect when `Pacing` would already prevent position 2+ advancement — it's an additional constraint, not a replacement
- `HasProfileDirective` is computed from `DirectorNote` — never stored separately

---

## New Types (Web/Application Layer)

### IPromptInjector (interface)

**File**: `DreamGenClone.Web/Application/RolePlay/IPromptInjector.cs`

```csharp
public interface IPromptInjector
{
    string Id { get; }                           // Unique inject name (e.g., "turn-context", "time-location")
    int Priority { get; }                        // Order in assembly (lower = earlier)
    bool ShouldFire(PromptInjectionContext context);
    string BuildText(PromptInjectionContext context);
}
```

**Contract rules**:
- `Id` MUST be unique across all registered injectors
- `Priority` values are relative; gaps (e.g., 5, 10, 20) allow future insertion
- `ShouldFire` MUST be idempotent for identical context
- `BuildText` MUST NOT throw for valid context (see FR-015 for invalid context handling)
- `BuildText` result MUST NOT contain leading/trailing newlines — coordinator handles spacing

### PromptInjectionContext (record)

**File**: `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs`

```csharp
public sealed record PromptInjectionContext
{
    // Session state
    public RolePlaySession Session { get; init; }
    
    // Resolved scene direction (from SceneDirectionResolver — single source of truth)
    public SceneDirection SceneDirection { get; init; }
    
    // Phase identifier — available for data-selection only (NOT for behavioral branching)
    public string Phase { get; init; }
    
    // Prompt metadata
    public PromptIntent Intent { get; init; }
    public int? PositionInTurn { get; init; }
    public string ActorName { get; init; }
    
    // Theme data
    public RPTheme? ActiveTheme { get; init; }
    public IReadOnlyDictionary<string, int>? ActorStats { get; init; }
    
    // Theme guidance text (pre-filtered for current phase)
    public IReadOnlyList<string> PhaseGuidanceLines { get; init; }
    public IReadOnlyList<string> PhaseDirectiveLines { get; init; }
    
    // Theme constraints
    public IReadOnlyList<RPThemeAIGuidanceNote> AiGuidanceNotes { get; init; }
    public IReadOnlyList<RPThemeHardConstraint> ThemeHardConstraints { get; init; }
    
    // Helper: checks if marker string exists in current-phase guidance
    public bool HasMarker(string marker)
        => PhaseGuidanceLines.Any(l => l.Contains($"[{marker}]"));
}
```

**Lifecycle**: Built once per `BuildPromptAsync` call, before the injector loop. Not cached across prompts.

### SceneDirectionCoordinator (service)

**File**: `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs`

```csharp
public sealed class SceneDirectionCoordinator
{
    private readonly List<IPromptInjector> _injectors;
    private readonly ILogger<SceneDirectionCoordinator> _logger;
    
    public SceneDirectionCoordinator(IEnumerable<IPromptInjector> injectors, ILogger<SceneDirectionCoordinator> logger)
    {
        _injectors = injectors.OrderBy(i => i.Priority).ToList();
        _logger = logger;
    }
    
    public string BuildPrompt(PromptInjectionContext context)
    {
        var sb = new StringBuilder();
        var firingSequence = new List<string>();
        
        foreach (var injector in _injectors)
        {
            if (injector.ShouldFire(context))
            {
                sb.Append(injector.BuildText(context));
                firingSequence.Add($"{injector.Id}(p{injector.Priority})");
            }
        }
        
        _logger.LogInformation(
            "Prompt built: Session={SessionId} Phase={Phase} Position={Pos} Intent={Intent} " +
            "FiringSequence={Sequence} SceneDirection={@SceneDir}",
            context.Session.Id, context.Phase, context.PositionInTurn, context.Intent,
            string.Join(" -> ", firingSequence), context.SceneDirection);
        
        return sb.ToString();
    }
}
```

**Contract rules**:
- Injectors are sorted by `Priority` at construction time
- FR-015: Exceptions from `BuildText` propagate — no catch-log-skip
- FR-016: Information log emitted per prompt build with full context snapshot
- Coordinator does NOT handle spacing between injectors — each injector's text is self-contained with necessary newlines

---

## Injector Registry

### Priority Map

| Priority | Injector | Owned By | ShouldFire Condition |
|----------|----------|----------|---------------------|
| 5 | TurnContextInjector | Engine | `PositionInTurn.HasValue` |
| 10 | TimeLocationInjector | Engine | Always |
| 20 | BehavioralFrameInjector | Engine | Always |
| 30 | ThemeContractInjector | Theme | `ActiveTheme != null` |
| 40 | ThemeAIGuidanceInjector | Theme | `AiGuidanceNotes.Count > 0` |
| 50 | IntensityContractInjector | Engine | Always |
| 60 | EscalationInjector | Theme | `!SceneDirection.HasProfileDirective && ActorStats != null && Intent != Instruction` |
| 65 | DirectorNoteInjector | Theme | `SceneDirection.HasProfileDirective` |
| 70 | SceneTimeDirectionInjector | Theme | `!SceneDirection.HasProfileDirective` |
| 80 | PositionListInjector | Engine | Positions configured in session |
| 90 | BeatStageInjector | Theme | `SceneDirection.BeatScope != BeatScope.Single` |
| 100 | FinalDirectiveInjector | Engine | Always |

**Override mechanism**: When `HasProfileDirective` is true, `EscalationInjector` and `SceneTimeDirectionInjector` are suppressed. Only `DirectorNoteInjector` (priority 65) fires for beat/time direction. This is enforced via `ShouldFire`, not internal branching.

---

## Theme Phase Guidance Prose (existing, populated)

**Entity**: `RPThemePhaseGuidance` (existing in Domain)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | string | PK |
| `ThemeId` | string | FK to RPTheme |
| `Phase` | NarrativePhase | Which phase this guidance applies to |
| `GuidanceText` | string | Free-text prose + markers like `[Pacing:slow]`. Consumed by ThemeContractInjector |
| `DirectiveText` | string | Hard directives for the LLM |

**Validation**:
- `GuidanceText` is free-text — no structural validation except non-empty
- Markers are embedded as `[MarkerName:value]` within `GuidanceText` — parsed by `SceneDirectionResolver`, not by the theme entity
- A theme MUST have at least one `RPThemePhaseGuidance` per phase it wants to control; missing phases fall back to resolver tier-3 defaults

---

## Phase Defaults (SceneDirectionResolver internal)

| Phase | Default Pacing | Default BeatScope | Default TimeShift |
|-------|---------------|-------------------|-------------------|
| BuildUp | Medium | Short | Small |
| Committed | Medium | Short | Small |
| Approaching | Medium | Short | Small |
| Climax | Fast | Extended | Medium |
| Reset | Slow | Single | None |

These are hardcoded constants in `SceneDirectionResolver`. They are the tier-3 safety net — injectors never see them directly.

---

## Data Blocks (not converted — stay inline)

The following remain inline in `BuildPromptAsync` as data assembly calls:

- System header
- POV Persona
- Behavioral Rules
- Scenario Data (fetch + render)
- Style Profile
- Interaction History
- Session Memory
- Scene Continuity Anchor
- Adaptive Stats
- Active Theme Tracker
- Scenario Guidance Context (data portion)
- Opening Period Guidance
- Secondary Theme AI Guidance
- Candidate Theme Menu
- Steer Guidance
- Time Skip Guidance
- Profile Theme Tiers
- Active Instruction
- Prompt Text
- Behavioral Frame HCs (re-injection)
- Theme HC re-injection
- World Rules re-injection
