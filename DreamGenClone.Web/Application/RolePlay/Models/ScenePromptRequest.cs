using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Request to run the pre-processor stage for an interaction.</summary>
public sealed class ScenePromptRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public SceneImageStudioSettings Settings { get; set; } = new();
    public string? ProductionGroupId { get; set; }
    public string? CompiledMediaBriefId { get; set; }
    public string BeatAnalysisId { get; set; } = string.Empty;
    public string BeatSnapshotJson { get; set; } = string.Empty;

    /// <summary>Which prompt style (natural language vs Pony tags) to draft. Unknown = natural language.</summary>
    public SceneImagePromptStyle PromptStyle { get; set; } = SceneImagePromptStyle.Unknown;

    /// <summary>
    /// The image model the user has selected in the Studio dropdown (optional). When its family
    /// produces the requested <see cref="PromptStyle"/> it is the generation target; otherwise the
    /// first enabled model of that style is used so the correct compiler/content policy runs.
    /// </summary>
    public string? RequestedImageModelId { get; set; }

    /// <summary>User-selected passage override (optional; default is the full interaction content).</summary>
    public string? ExcerptOverride { get; set; }

    /// <summary>Optional instruction for the "Refine prompt" iteration path.</summary>
    public string? RefineInstruction { get; set; }

    public string Pov { get; set; } = string.Empty;
}
