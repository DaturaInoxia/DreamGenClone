namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// B-075: Payload for the background steer-generation job.
/// Snapshots session state inline so the job doesn't need to re-read
/// the session from DB (avoids race conditions with payload saving).
/// </summary>
public sealed class SteerGenerationJobPayload
{
    public required string SessionId { get; init; }
    public string? ScenarioId { get; init; }

    /// <summary>Per-character snapshot: name, role, canonical stats, encounter dimensions.</summary>
    public required IReadOnlyList<CharacterStatSnapshot> CharacterSnapshots { get; init; }

    public string? Phase { get; init; }
    public string? PrimaryThemeId { get; init; }
    public string? CurrentLocation { get; init; }

    /// <summary>Last 6 interaction content texts (abbreviated).</summary>
    public required IReadOnlyList<string> RecentInteractionTexts { get; init; }

    // Model settings from session.
    public string? SessionModelId { get; init; }
    public double? SessionTemperature { get; init; }
    public double? SessionTopP { get; init; }
    public int? SessionMaxTokens { get; init; }

    // Scenario-level context for the assistant context builder.
    public string? ScenarioSummary { get; init; }
    public IReadOnlyList<string> CharacterSummaries { get; init; } = [];

    // Theme context for steering option generation.
    public string? ThemeLabel { get; init; }
    public string? ThemeDescription { get; init; }
    public IReadOnlyList<string> ThemePhaseGuidanceLines { get; init; } = [];

    // ── B-077: Willingness-gap config (snapshot at enqueue time) ──────────
    public bool WillingnessGapSteeringEnabled { get; init; }
    public string WillingnessGapSteeringDirective { get; init; } = string.Empty;
    public double WillingnessDesireLoyaltyWeight { get; init; } = 0.5;
    public double WillingnessBehaviorWeight { get; init; } = 0.5;
    public double WillingnessMaritalDeficitWeight { get; init; } = 0.25;
    public int WillingnessVerdictNoMax { get; init; } = 40;
    public int WillingnessVerdictMaybeMax { get; init; } = 70;

    /// <summary>
    /// B-077: Active theme's semantic stat mappings at enqueue time
    /// (only the fields needed by WillingnessSteerGapResolver).
    /// </summary>
    public IReadOnlyList<SemanticStatMappingSnapshot>? SemanticStatMappingSnapshots { get; init; }
}

/// <summary>
/// B-075: Snapshot of one character's key stats at auto-steer generation time.
/// </summary>
public sealed class CharacterStatSnapshot
{
    public required string CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public string? Role { get; init; }

    public int Desire { get; init; }
    public int Restraint { get; init; }
    public int Dominance { get; init; }
    public int Loyalty { get; init; }
    public int SelfRespect { get; init; }

    /// <summary>Runtime encounter dimensions (Wife/Husband only).</summary>
    public IReadOnlyDictionary<string, double>? EncounterDimensions { get; init; }
}

/// <summary>
/// B-077: Lightweight snapshot of a semantic stat mapping row for the background steer job.
/// Carries only the fields needed by WillingnessSteerGapResolver.
/// </summary>
public sealed class SemanticStatMappingSnapshot
{
    public required string EventId { get; init; }
    public required string TargetStat { get; init; }
    public required string Direction { get; init; }
}
