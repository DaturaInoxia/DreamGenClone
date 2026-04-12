namespace DreamGenClone.Domain.RolePlay;

public sealed class NarrativePhaseTransitionEvent
{
    public string TransitionId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public NarrativePhase FromPhase { get; set; }
    public NarrativePhase ToPhase { get; set; }
    public TransitionTriggerType TriggerType { get; set; }
    public string EvidencePayload { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
