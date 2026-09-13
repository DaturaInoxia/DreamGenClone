namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// The single create/edit request contract shared by Asset Studio, Production Studio and the roleplay
/// image editor. It replaces the parallel request shapes
/// (<c>EnqueueSceneAssetImageEditRequest</c> and <c>SceneImageEditRequest</c>) as the one DTO both
/// surfaces dispatch through. B124-010: designed now; wired into both surfaces by B124-012.
/// </summary>
public sealed class MediaEditRequest
{
    public string SourceImageId { get; set; } = string.Empty;

    public string SourceImageSha256 { get; set; } = string.Empty;

    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>The executed instruction (already compiled). Raw user intent must never be executed.</summary>
    public string CompiledPrompt { get; set; } = string.Empty;

    public string PromptSha256 { get; set; } = string.Empty;

    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }

    /// <summary>Optional pose conditioning — the pose foundation applied through the same path on every surface.</summary>
    public MediaEditPose? Pose { get; set; }

    /// <summary>Optional normalized region (0..1).</summary>
    public MediaEditRegion? Region { get; set; }

    /// <summary>Asset-studio candidate batch linkage; null for production edits.</summary>
    public string? CandidateBatchId { get; set; }

    /// <summary>Correlation/context id (session/interaction for production, asset id for asset studio).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Raw user intent, present only when a compilation step precedes execution.</summary>
    public string? Intent { get; set; }
}

public sealed record MediaEditPose
{
    /// <summary>Pose from the seeded library.</summary>
    public string? PosePresetId { get; set; }

    /// <summary>Or a pose extracted from an image via DW Pose.</summary>
    public string? ExtractedSourceImageId { get; set; }

    public float Strength { get; init; } = 0.7f;
}

public sealed record MediaEditRegion
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
}
