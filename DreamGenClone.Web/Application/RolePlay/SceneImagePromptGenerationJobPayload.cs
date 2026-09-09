namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for the SceneImagePromptGeneration background job.</summary>
public sealed class SceneImagePromptGenerationJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string PromptRecordId { get; set; } = string.Empty;

    /// <summary>
    /// The Studio model dropdown selection at enqueue time. The generation job prefers it when its
    /// family matches the prompt record's style; otherwise it resolves the first enabled model of
    /// that style so the correct compiler/content policy runs.
    /// </summary>
    public string? RequestedImageModelId { get; set; }
}
