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
    public string? HusbandAwarenessProfileId { get; set; }
    public string? CurrentSceneLocation { get; set; }
    public List<CharacterLocationState> CharacterLocations { get; set; } = [];
    public List<CharacterLocationPerceptionState> CharacterLocationPerceptions { get; set; } = [];
    public List<CharacterStatProfileV2> CharacterSnapshots { get; set; } = [];
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
