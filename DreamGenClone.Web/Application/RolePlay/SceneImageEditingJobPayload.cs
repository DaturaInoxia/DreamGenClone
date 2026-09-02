namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for a source-image edit job.</summary>
public sealed class SceneImageEditingJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string ImageRecordId { get; set; } = string.Empty;
}