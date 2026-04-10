namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class ContinueAsResult
{
    public bool IsClearResult { get; set; }

    public bool Success { get; set; }

    public string? ValidationError { get; set; }

    public List<RolePlayInteraction> ParticipantOutputs { get; } = [];

    public RolePlayInteraction? NarrativeOutput { get; set; }

    /// <summary>When true, the session has signaled it is the user's turn (TakeTurns mode).</summary>
    public bool IsUserTurn { get; set; }
}
