namespace DreamGenClone.Domain.RolePlay;

public sealed class AdaptiveScenarioState
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveScenarioId { get; set; }
    public NarrativePhase CurrentPhase { get; set; } = NarrativePhase.BuildUp;
    public int InteractionCountInPhase { get; set; }
    public int ConsecutiveLeadCount { get; set; }
    public DateTime LastEvaluationUtc { get; set; } = DateTime.UtcNow;
    public int CycleIndex { get; set; }
    public string ActiveFormulaVersion { get; set; } = string.Empty;
    public List<CharacterStatProfileV2> CharacterSnapshots { get; set; } = [];
}
