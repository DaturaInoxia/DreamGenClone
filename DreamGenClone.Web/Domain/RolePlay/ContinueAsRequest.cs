namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class ContinueAsRequest
{
    public string SessionId { get; set; } = string.Empty;

    public List<string> SelectedIdentityIds { get; set; } = [];

    public List<ContinueAsActor> SelectedParticipants { get; set; } = [];

    public bool IncludeNarrative { get; set; }

    public string? CustomIdentityName { get; set; }

    public SubmissionSource TriggeredBy { get; set; } = SubmissionSource.ContinueAsPopupContinue;

    public bool IsClearAction { get; set; }
}
