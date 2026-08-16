using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Immutable context record every slot receives. Built once per prompt by
/// <see cref="RolePlayPromptBuilder"/> before any slot runs. Slots MUST NOT
/// mutate the context or fetch additional data — all data is pre-resolved.
/// </summary>
public sealed record PromptBuildContext
{
    // ── Session ────────────────────────────────────────────────
    public required RolePlaySession Session { get; init; }

    // ── Actor ──────────────────────────────────────────────────
    public required ActorProfile ActorProfile { get; init; }
    public required PromptVariant Variant { get; init; }

    // ── Phase ──────────────────────────────────────────────────
    public required string Phase { get; init; }

    // ── Turn metadata ──────────────────────────────────────────
    public required int? TurnIndex { get; init; }
    public required int? PositionInTurn { get; init; }
    public required int? TurnActorCount { get; init; }

    // ── User direction ─────────────────────────────────────────
    public required string PromptText { get; init; }

    // ── Budget ─────────────────────────────────────────────────
    public required int MaxPromptChars { get; init; }

    // ── World state (conditional — B-062) ──────────────────────
    public WorldStateData? WorldState { get; init; }

    // ── Resolved data (pre-fetched by builder) ─────────────────
    public required ResolvedScenarioData Scenario { get; init; }
    public required ResolvedThemeData Theme { get; init; }
    public required ResolvedIntensityData Intensity { get; init; }
    public required ResolvedWritingStyleData WritingStyle { get; init; }
    public required ResolvedNarrativeToneData NarrativeTone { get; init; }

    // ── Memory ─────────────────────────────────────────────────
    public required IReadOnlyList<EncounterSummaryRecord> EncounterSummaries { get; init; }
    public required IReadOnlyList<RolePlayInteraction> RecentInteractions { get; init; }

    // ── Interaction history enrichment ─────────────────────────
    /// <summary>
    /// Actor name → role mapping resolved from scenario characters.
    /// Used by <see cref="Slots.InteractionHistorySlot"/> to annotate each interaction.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ActorRoleMap { get; init; }

    // ── Pinned interactions ────────────────────────────────────
    /// <summary>
    /// Pinned interactions that are injected into every continuation prompt
    /// regardless of context-window position. Populated before the TakeLast filter.
    /// Used by <see cref="Slots.PinnedContextSlot"/> (Slot 8).
    /// </summary>
    public required IReadOnlyList<RolePlayInteraction> PinnedInteractions { get; init; }

    // ── Staged directions (B-076) ──────────────────────────────
    /// <summary>
    /// Staged interaction rows (IsStagedDirection=true) to be injected as a batch
    /// scene-directions block on this continuation, then graduated.
    /// Used by <see cref="Slots.StagedDirectionsSlot"/> (Slot 9). Read-only snapshot;
    /// graduation (flag flip) happens in the engine after the prompt is built.
    /// </summary>
    public required IReadOnlyList<RolePlayInteraction> StagedInteractions { get; init; }

    // ── Continuation settings override (B-082) ──────────────────
    /// <summary>
    /// Sticky continuation-settings override read from <see cref="RolePlaySession.ContinuationOverride"/>.
    /// Null when not set. Used by <see cref="Slots.ContinuationOverrideSlot"/> to render the
    /// otherwise-unconsumed scene-direction dimensions (Beat Style, Time Shift, Granularity,
    /// Scene Presence). Pacing/Deepening/word-count are consumed by their own slots from the
    /// already-overridden resolved data.
    /// </summary>
    public ContinuationOverride? Override { get; init; }

    /// <summary>
    /// Recent interactions wrapped with turn metadata (turn number, position, actor count).
    /// Computed by the builder from <see cref="RecentInteractions"/> ordering.
    /// Null when not yet resolved — slots should fall back to <see cref="RecentInteractions"/>.
    /// </summary>
    public IReadOnlyList<RecentInteractionEntry>? RecentInteractionEntries { get; init; }

    // ── Character details (pre-resolved by builder, indexed by character ID) ──
    /// <summary>
    /// Rich character detail keyed by character ID. Populated by the builder from scenario character data.
    /// Null when not yet resolved (slots should handle null gracefully).
    /// </summary>
    public IReadOnlyDictionary<string, ResolvedCharacterDetail>? CharacterDetails { get; init; }

    // ── Behavioral frames (pre-resolved by builder, keyed by character ID/name) ──
    /// <summary>
    /// Character behavioral frames from scenario guidance. Keyed by character ID or name.
    /// Used by <see cref="Slots.BehavioralFramesSlot"/> (Slot 13). Null when not yet resolved.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CharacterBehavioralFrames { get; init; }

    // ── Character stat state texts (pre-resolved by builder, keyed by character label) ──
    /// <summary>
    /// Per-character current-state text derived from runtime stats (Desire, Restraint, etc.)
    /// via <see cref="Domain.StoryAnalysis.CharacterStatTextCatalog"/>.
    /// Used by <see cref="Slots.BehavioralFramesSlot"/> (Slot 13) alongside behavioral frames.
    /// Null when not yet resolved.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CharacterStatStateTexts { get; init; }

    // ── Merged scenario guidance text (B-034) ────────────────────────────
    /// <summary>
    /// The merged scenario guidance text produced by <see cref="RolePlayContinuationService"/>
    /// via <c>ScenarioGuidanceContextFactory</c>. Carries the phase guidance, stat interpretation,
    /// and the B-034 unified "Wife Willingness to Cheat" block (verdict + ceiling band lines).
    /// Currently dropped in the 17-slot path — wired here so a slot can render it.
    /// Null when not yet resolved.
    /// </summary>
    public string? ScenarioGuidanceText { get; init; }
}

// ── World State sub-record (conditional, B-062) ─────────────────

public sealed record WorldStateData
{
    public int DayNumber { get; init; }
    public int? TotalDays { get; init; }
    public string? DayOfWeek { get; init; }
    public string? TimePhase { get; init; }
    public string? SpecificTime { get; init; }
    public string? WeatherCondition { get; init; }
    public decimal? TemperatureCelsius { get; init; }
    public string? HumidityDescription { get; init; }
    public string? WorldRhythm { get; init; }
    public string? TemporalPressure { get; init; }
}

// ── Resolved data sub-records ──────────────────────────────────

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
    public required IReadOnlyList<ResolvedLocationData> Locations { get; init; }
    public required string? DefaultSteeringProfileId { get; init; }
    public required string? DefaultIntensityProfileId { get; init; }
    public required string? DefaultStartingLocationName { get; init; }

    /// <summary>
    /// Opening-period guidance text (001-opening-period). Populated from the scenario
    /// definition; when null, the opening-guidance slot falls back to the default constant.
    /// </summary>
    public string? OpeningGuidanceText { get; init; }
}

/// <summary>
/// A scenario location resolved for prompt injection. Carries both the name
/// (always present) and the description (present only for the current scene
/// to save tokens; other locations are listed by name only).
/// </summary>
public sealed record ResolvedLocationData(string Name, string? Description);

public sealed record ResolvedThemeData
{
    public RPTheme? ActiveTheme { get; init; }
    public IReadOnlyList<string> PhaseGuidanceLines { get; init; } = [];
    public IReadOnlyList<string> PhaseDirectiveLines { get; init; } = [];
    public IReadOnlyList<RPThemeAIGuidanceNote> AiGuidanceNotes { get; init; } = [];
    public IReadOnlyList<string> HardConstraintLines { get; init; } = [];

    /// <summary>Available theme arcs for Opening phase (Label, Description). Null when no themes available or phase is not Opening.</summary>
    public IReadOnlyList<(string Label, string Description)>? AvailableArcLabels { get; init; }
}

public sealed record ResolvedIntensityData
{
    public IntensityLevel? BaseLevel { get; init; }
    public IntensityLevel? AdaptiveLevel { get; init; }
    public string? ResolvedLabel { get; init; }
    public string? Description { get; init; }
    public string? FloorOverride { get; init; }
    public string? CeilingOverride { get; init; }
    public SceneDirection? SceneDirection { get; init; }
    public IReadOnlyList<string> AvailablePositions { get; init; } = [];

    // ── Writing directives from IntensityProfile (plan-amendment 2026-07-22) ──
    public required string ProseStyleDirective { get; init; }
    public required string VoiceDirective { get; init; }
    public required string ToneDirective { get; init; }
    public required string FocusDirective { get; init; }
    public required string HeatLevelDirective { get; init; }
}

public sealed record ResolvedWritingStyleData
{
    /// <summary>Timeless example — always kept, never trimmed.</summary>
    public required string Example { get; init; }

    /// <summary>Phase-specific Rule-of-Thumb from PhaseRuleOfThumb table. Fail-fast if missing (FR-014).</summary>
    public required string PhaseRuleOfThumb { get; init; }

    /// <summary>Merged prose style + tone hint.</summary>
    public required string StyleHint { get; init; }

    // ── New SteeringProfile fields (FR-005, FR-006) ────────────────
    // Fail-fast at prompt build time if empty/zero.

    /// <summary>Immersion rule for Character variant. Fail-fast if empty (FR-006).</summary>
    public required string ImmersionDirective { get; init; }

    /// <summary>Action rule for Character variant. Fail-fast if empty (FR-006).</summary>
    public required string ActionDirective { get; init; }

    /// <summary>Minimum word count for Character variant. Fail-fast if &lt;= 0 (FR-006).</summary>
    public required int WordTargetMin { get; init; }

    /// <summary>Maximum word count for Character variant. Fail-fast if &lt;= 0 (FR-006).</summary>
    public required int WordTargetMax { get; init; }

    /// <summary>Minimum word count for Narrative variant. Fail-fast if &lt;= 0 (FR-006).</summary>
    public required int NarrativeWordTargetMin { get; init; }

    /// <summary>Maximum word count for Narrative variant. Fail-fast if &lt;= 0 (FR-006).</summary>
    public required int NarrativeWordTargetMax { get; init; }

    /// <summary>
    /// Word target marker resolved from phase guidance ([targetwords:small/medium/large]).
    /// When non-null, the WordTargetMin/Max values have been overridden from the marker mapping.
    /// Defaults to "small" when no marker is present.
    /// </summary>
    public string? WordTargetMarker { get; init; }
}

// ── Narrative tone sub-record (FR-007, FR-008) ──────────────────

/// <summary>
/// Resolved narrative tone data from <see cref="NarrativeSettings"/>.
/// Uses 3-tier resolution: new Tone → legacy NarrativeTone → null.
/// </summary>
public sealed record ResolvedNarrativeToneData
{
    /// <summary>Resolved tone (mood/attitude). Null if all sources empty.</summary>
    public string? Tone { get; init; }

    /// <summary>Language complexity. Null if not configured.</summary>
    public string? Register { get; init; }

    /// <summary>Subject emphasis. Null if not configured.</summary>
    public string? Focus { get; init; }
}

// ── Interaction history enrichment sub-record ──────────────────

/// <summary>
/// A single recent interaction wrapped with turn metadata for enriched
/// interaction history output in <see cref="Slots.InteractionHistorySlot"/>.
/// </summary>
public sealed record RecentInteractionEntry
{
    public required RolePlayInteraction Interaction { get; init; }
    public required int TurnNumber { get; init; }
    public required int PositionInTurn { get; init; }
    public required int TurnActorCount { get; init; }
}

// ── Character detail sub-record ────────────────────────────────

/// <summary>
/// Rich character detail pre-resolved by the builder for use by character-aware slots.
/// Keyed by character ID in <see cref="PromptBuildContext.CharacterDetails"/>.
/// </summary>
public sealed record ResolvedCharacterDetail
{
    /// <summary>Character description text.</summary>
    public string? Description { get; init; }

    /// <summary>Formatted appearance block (from PhysicalAttributesFormatter).</summary>
    public string? AppearanceText { get; init; }

    /// <summary>One-line comparison text for non-present characters.</summary>
    public string? ComparisonText { get; init; }

    /// <summary>Character gender (for formatting).</summary>
    public string Gender { get; init; } = "Unknown";
}
