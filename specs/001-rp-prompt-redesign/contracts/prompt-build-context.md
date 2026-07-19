# Contract: Prompt Build Context

**Branch**: `001-rp-prompt-redesign`

Defines the immutable context record every slot receives. Built once per prompt by `RolePlayPromptBuilder` before any slot runs.

---

## Record Shape

```csharp
namespace DreamGenClone.Web.Application.RolePlay.Prompts;

public sealed record PromptBuildContext
{
    // ── Session ────────────────────────────────────────────────
    public required RolePlaySession Session { get; init; }

    // ── Actor ──────────────────────────────────────────────────
    public required ActorProfile ActorProfile { get; init; }
    public required PromptVariant Variant { get; init; }  // Character or Narrative

    // ── Phase ──────────────────────────────────────────────────
    public required string Phase { get; init; }  // NarrativePhase name: Opening, BuildUp, etc.

    // ── Turn metadata ──────────────────────────────────────────
    public required int? TurnIndex { get; init; }
    public required int? PositionInTurn { get; init; }
    public required int? TurnActorCount { get; init; }

    // ── User direction ─────────────────────────────────────────
    public required string PromptText { get; init; }  // may be generic default; Slot 16 decides

    // ── Budget ─────────────────────────────────────────────────
    public required int MaxPromptChars { get; init; }  // fail-fast if missing/invalid (FR-004)

    // ── World state (conditional — B-062) ──────────────────────
    public WorldStateData? WorldState { get; init; }  // null until B-062

    // ── Resolved data (pre-fetched by builder) ─────────────────
    public required ResolvedScenarioData Scenario { get; init; }
    public required ResolvedThemeData Theme { get; init; }
    public required ResolvedIntensityData Intensity { get; init; }
    public required ResolvedWritingStyleData WritingStyle { get; init; }

    // ── Memory ─────────────────────────────────────────────────
    public required IReadOnlyList<EncounterSummaryRecord> EncounterSummaries { get; init; }
    public required IReadOnlyList<RolePlayInteraction> RecentInteractions { get; init; }
}
```

---

## Resolved Data Sub-Records

```csharp
public sealed record ResolvedScenarioData
{
    public required string? ScenarioId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PlotDescription { get; init; }
    public required string WorldDescription { get; init; }
    public required string? TimeFrame { get; init; }
    public required IReadOnlyList<string> Goals { get; init; }
    public required IReadOnlyList<string> Conflicts { get; init; }
    public required IReadOnlyList<string> WorldRules { get; init; }
    public required IReadOnlyList<string> EnvironmentalDetails { get; init; }
    public required IReadOnlyList<string> NarrativeGuidelines { get; init; }
    public required IReadOnlyList<ScenarioCharacter> Characters { get; init; }
    public required IReadOnlyList<ScenarioLocation> Locations { get; init; }
    public required string? DefaultSteeringProfileId { get; init; }
    public required string? DefaultIntensityProfileId { get; init; }
}

public sealed record ResolvedThemeData
{
    public RPTheme? ActiveTheme { get; init; }
    public IReadOnlyList<string> PhaseGuidanceLines { get; init; } = [];
    public IReadOnlyList<string> PhaseDirectiveLines { get; init; } = [];
    public IReadOnlyList<RPThemeAIGuidanceNote> AiGuidanceNotes { get; init; } = [];
    public IReadOnlyList<string> HardConstraintLines { get; init; } = [];
}

public sealed record ResolvedIntensityData
{
    public IntensityLevel? BaseLevel { get; init; }
    public IntensityLevel? AdaptiveLevel { get; init; }
    public string? ResolvedLabel { get; init; }
    public string? Description { get; init; }
    public string? FloorOverride { get; init; }
    public string? CeilingOverride { get; init; }
    public SceneDirection SceneDirection { get; init; }  // pacing, time-shift, deepening
    public IReadOnlyList<string> AvailablePositions { get; init; } = [];
}

public sealed record ResolvedWritingStyleData
{
    public required string Description { get; init; }      // timeless
    public required string Example { get; init; }          // timeless
    public required string ProfileDefaultRuleOfThumb { get; init; }  // fail-fast if missing (FR-014)
    public required string PhaseRuleOfThumb { get; init; } // fail-fast if missing (FR-014)
    public required string StyleHint { get; init; }        // merged prose style + tone
}
```

---

## Construction Contract

`RolePlayPromptBuilder.BuildAsync` constructs `PromptBuildContext` by:

1. Resolving `ActorProfile` via `ActorProfileResolver` (fail-fast on unknown actor).
2. Resolving `Variant` from `PromptIntent` (`Narrative` intent → `Narrative` variant; `Message`/`Instruction` → `Character` variant).
3. Reading `MaxPromptChars` from `session.MaxPromptChars` — fail-fast with diagnostic (session ID) if null or <= 0 (FR-004).
4. Reading compression thresholds from session — fail-fast with diagnostic if any required threshold is null or <= 0 (FR-012a).
5. Pre-fetching all resolved data (scenario, theme, intensity, writing style) — fail-fast on missing required config (FR-014, FR-038).
6. Loading encounter summaries and recent interactions from repositories.

The context is then passed immutably to every slot. Slots MUST NOT mutate the context or fetch additional data — all data they need is pre-resolved.
