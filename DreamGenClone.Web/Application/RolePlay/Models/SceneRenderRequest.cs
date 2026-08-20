namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Request to render an image from a prompt.</summary>
public sealed class SceneRenderRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string PromptRecordId { get; set; } = string.Empty;

    /// <summary>Final (possibly edited) prompt text sent to the image model.</summary>
    public string Prompt { get; set; } = string.Empty;

    public string? ImageSize { get; set; }

    /// <summary>Parent image id when regenerating.</summary>
    public string? RegenerateOfId { get; set; }
}
