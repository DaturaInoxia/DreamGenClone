namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneAssetImageEditCompilationJobPayload
{
    public string AttemptId { get; set; } = string.Empty;
}

public sealed class SceneAssetImageEditDescriptionJobPayload
{
    public string EditSessionId { get; set; } = string.Empty;
}

public sealed class SceneAssetImageEditingJobPayload
{
    public string AssetId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string EditorModelId { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }
}