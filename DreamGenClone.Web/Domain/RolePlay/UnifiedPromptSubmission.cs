namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class UnifiedPromptSubmission
{
    public string SessionId { get; set; } = string.Empty;

    public string PromptText { get; set; } = string.Empty;

    public PromptIntent Intent { get; set; } = PromptIntent.Message;

    public string SelectedIdentityId { get; set; } = string.Empty;

    public IdentityOptionSource SelectedIdentityType { get; set; } = IdentityOptionSource.SceneCharacter;

    public string? CustomIdentityName { get; set; }

    public BehaviorMode BehaviorModeAtSubmit { get; set; } = BehaviorMode.TakeTurns;

    public SubmissionSource SubmittedVia { get; set; } = SubmissionSource.SendButton;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public bool IsValid(out string validationError)
    {
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            validationError = "SessionId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PromptText))
        {
            validationError = "PromptText is required.";
            return false;
        }

        var requiresIdentity = Intent is PromptIntent.Message or PromptIntent.Narrative;
        if (requiresIdentity && string.IsNullOrWhiteSpace(SelectedIdentityId))
        {
            validationError = "SelectedIdentityId is required for character-scoped intents.";
            return false;
        }

        if (requiresIdentity
            && SelectedIdentityType == IdentityOptionSource.CustomCharacter
            && string.IsNullOrWhiteSpace(CustomIdentityName)
            && !SelectedIdentityId.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "Custom identity requires a custom name.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
