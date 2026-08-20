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

    /// <summary>Full <c>SceneImageStudioSettings</c> snapshot (JSON) used for this render. Stored on
    /// the image record so the studio can restore the exact settings ("continue from this image").</summary>
    public string? SettingsJson { get; set; }

    /// <summary>Parent image id when regenerating.</summary>
    public string? RegenerateOfId { get; set; }
}
