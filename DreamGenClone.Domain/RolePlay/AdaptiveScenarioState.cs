namespace DreamGenClone.Domain.RolePlay;

public sealed class AdaptiveScenarioState
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveScenarioId { get; set; }
    public string? ActiveVariantId { get; set; }
    public NarrativePhase CurrentPhase { get; set; } = NarrativePhase.BuildUp;
    public int InteractionCountInPhase { get; set; }
    public int ConsecutiveLeadCount { get; set; }
    public DateTime LastEvaluationUtc { get; set; } = DateTime.UtcNow;
    public int CycleIndex { get; set; }
    public string ActiveFormulaVersion { get; set; } = string.Empty;
    public string? SelectedWillingnessProfileId { get; set; }
    public string? SelectedNarrativeGateProfileId { get; set; }
    public string? HusbandAwarenessProfileId { get; set; }
    public NarrativePhase? PhaseOverrideFloor { get; set; }
    public string? PhaseOverrideScenarioId { get; set; }
    public int? PhaseOverrideCycleIndex { get; set; }
    public string? PhaseOverrideSource { get; set; }
    public DateTime? PhaseOverrideAppliedUtc { get; set; }
    public string? CurrentSceneLocation { get; set; }
    public List<CharacterLocationState> CharacterLocations { get; set; } = [];
    public List<CharacterLocationPerceptionState> CharacterLocationPerceptions { get; set; } = [];
    public List<CharacterStatProfileV2> CharacterSnapshots { get; set; } = [];

    /// <summary>
    /// BeatCode of the current sub-beat cursor during Climax phase, e.g. "1a", "8g".
    /// Null when not in Climax phase.
    /// </summary>
    public string? CurrentBeatCode { get; set; }

    /// <summary>
    /// How many turns have elapsed in the current sub-beat since the cursor last advanced.
    /// Reset to 0 when CurrentBeatCode changes.
    /// </summary>
    public int TurnsInCurrentBeat { get; set; }
}

public sealed class CharacterLocationState
{
    public string CharacterId { get; set; } = string.Empty;
    public string? TrueLocation { get; set; }
    public bool IsHidden { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CharacterLocationPerceptionState
{
    public string ObserverCharacterId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;
    public string? PerceivedLocation { get; set; }
    public int Confidence { get; set; }
    public bool HasLineOfSight { get; set; }
    public bool IsInProximity { get; set; }
    public string KnowledgeSource { get; set; } = "unknown";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
