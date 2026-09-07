namespace DreamGenClone.Web.Application.RolePlay.Models;

public sealed class CreateSceneAssetImageEditSessionRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
}

public sealed class EnqueueSceneAssetImageEditCompilationRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string RawIntent { get; set; } = string.Empty;
    public IReadOnlyList<string> ClarificationHistory { get; set; } = [];
}

public sealed class AppendSceneAssetImageEditPromptRevisionRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed class EnqueueSceneAssetImageEditRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string PromptRevisionId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public string PromptSha256 { get; set; } = string.Empty;
    public string EditorModelId { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }
}