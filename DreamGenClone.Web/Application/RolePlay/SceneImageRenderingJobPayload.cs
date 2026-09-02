namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for the SceneImageRendering background job.</summary>
public sealed class SceneImageRenderingJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string ImageRecordId { get; set; } = string.Empty;
}
