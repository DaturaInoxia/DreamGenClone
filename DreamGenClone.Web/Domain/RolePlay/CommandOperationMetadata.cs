namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class CommandOperationMetadata
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; set; } = string.Empty;

    public PromptIntent Intent { get; set; } = PromptIntent.Message;

    public SubmissionSource SubmittedVia { get; set; } = SubmissionSource.SendButton;

    public string? SelectedIdentityId { get; set; }

    public IdentityOptionSource? SelectedIdentityType { get; set; }

    public IReadOnlyList<ContinueAsActor> ParticipantScope { get; set; } = [];

    public bool IncludeNarrative { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}