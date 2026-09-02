namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for the SceneImagePromptGeneration background job.</summary>
public sealed class SceneImagePromptGenerationJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string PromptRecordId { get; set; } = string.Empty;

}
