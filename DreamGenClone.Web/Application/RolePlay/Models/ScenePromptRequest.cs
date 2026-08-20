namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Request to run the pre-processor stage for an interaction.</summary>
public sealed class ScenePromptRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public SceneImageStudioSettings Settings { get; set; } = new();

    /// <summary>User-selected passage override (optional; default is the full interaction content).</summary>
    public string? ExcerptOverride { get; set; }

    /// <summary>Optional instruction for the "Refine prompt" iteration path.</summary>
    public string? RefineInstruction { get; set; }
}
