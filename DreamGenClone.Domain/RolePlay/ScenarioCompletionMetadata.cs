namespace DreamGenClone.Domain.RolePlay;

public sealed class ScenarioCompletionMetadata
{
    public string SessionId { get; set; } = string.Empty;
    public int CycleIndex { get; set; }
    public string ScenarioId { get; set; } = string.Empty;
    public NarrativePhase PeakPhase { get; set; }
    public string ResetReason { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
}
