namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for a source-image edit job.</summary>
public sealed class SceneImageEditingJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string ImageRecordId { get; set; } = string.Empty;
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>
    /// Serialized <c>SceneImageLoraSelection</c> list the operator picked for this edit (B-143), or null for none.
    /// It rides here rather than in the edit request alone so the QUEUED row records the stack the operator chose.
    /// </summary>
    public string? SceneLorasJson { get; set; }
}