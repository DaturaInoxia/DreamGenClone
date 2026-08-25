namespace DreamGenClone.Web.Application.RolePlay.Models;

public sealed class CreateSceneImageEditSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
}

public sealed class EnqueueSceneImageEditCompilationRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string RawIntent { get; set; } = string.Empty;
    public IReadOnlyList<string> ClarificationHistory { get; set; } = [];
}

public sealed class AppendSceneImageEditPromptRevisionRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}