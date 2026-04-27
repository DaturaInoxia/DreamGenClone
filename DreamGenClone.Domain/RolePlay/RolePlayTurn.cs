namespace DreamGenClone.Domain.RolePlay;

public enum RolePlayTurnStatus
{
    Started = 0,
    Completed = 1,
    Failed = 2
}

public sealed class RolePlayTurn
{
    public string TurnId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string TurnKind { get; init; } = string.Empty;
    public string TriggerSource { get; init; } = string.Empty;
    public string? InitiatedByActorName { get; init; }
    public string? InputInteractionId { get; init; }
    public IReadOnlyList<string> OutputInteractionIds { get; init; } = [];
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public RolePlayTurnStatus Status { get; init; } = RolePlayTurnStatus.Started;
    public string? FailureReason { get; init; }
}