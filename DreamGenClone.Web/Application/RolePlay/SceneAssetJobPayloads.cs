using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for a text-to-image scene asset generation job.</summary>
public sealed class SceneAssetGenerationJobPayload
{
    public string AssetId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ImageSize { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }

    /// <summary>
    /// The verified stance whose OpenPose skeleton conditions this render, or null for a plain text-to-image call.
    /// Null means "no pose was asked for" — never "use a default pose".
    /// </summary>
    public string? PoseStance { get; set; }

    /// <summary>ControlNet conditioning strength for <see cref="PoseStance"/>, required exactly when it is set.</summary>
    public double? PoseStrength { get; set; }

    /// <summary>
    /// The approved identity pack this render is conditioned on, or null for an unconditioned render. The HANDLER
    /// re-reads the pack and its face asset, so an unconditional fallback is impossible: a pack that is no longer
    /// approved fails the render rather than quietly producing a different person.
    /// </summary>
    public string? IdentityPackId { get; set; }

    /// <summary>The approved face asset within <see cref="IdentityPackId"/> to condition on.</summary>
    public string? IdentityFaceAssetId { get; set; }

    /// <summary>
    /// The canonical angle this render is asked for, or null when the render is not an angle render. The handler
    /// resolves the skeleton from the committed angle library by this value, so an angle whose skeleton does not
    /// exist fails the render rather than turning the body by inference.
    /// </summary>
    public string? BodyAngleView { get; set; }

    /// <summary>
    /// The ACCEPTED body image this angle render is based on, required exactly when <see cref="BodyAngleView"/> is
    /// set. The handler re-reads the image row and its bytes, so a source that was deleted or replaced between the
    /// queue and the render fails the render instead of producing a different body.
    /// </summary>
    public string? BodyAngleSourceImageId { get; set; }
}

/// <summary>Payload for a typed-vision reference candidate generation job.</summary>
public sealed record ProducedImageGenerationJobPayload
{
    public string BatchId { get; init; } = string.Empty;
    public string TargetRef { get; init; } = string.Empty;
    public ProducedImageReferenceKind ReferenceKind { get; init; }
    public string VisionText { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public string ImageSize { get; init; } = string.Empty;
}

/// <summary>Payload for a Qwen source-image edit that produces a new scene asset revision.</summary>
public sealed class SceneAssetEditingJobPayload
{
    public string AssetId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }
}

/// <summary>
/// Payload for the special "Generate Profile Pack" function: generate the 5 face views (front +
/// 3/4L, 3/4R, profL, profR) for one scenario character and save them into a draft identity pack
/// plus the asset library.
/// </summary>
public sealed class SceneAssetProfilePackJobPayload
{
    /// <summary>The scenario character this pack belongs to (characterProfileId).</summary>
    public string CharacterProfileId { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    /// <summary>Visual description used when no <see cref="FrontAssetId"/> is supplied.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>An existing complete asset to use as the front (identity anchor); skips front generation.</summary>
    public string? FrontAssetId { get; set; }

    /// <summary>Exact generation model used when the front image is generated.</summary>
    public string? FrontModelId { get; set; }

    /// <summary>Exact source-image editor model used for the four angle views.</summary>
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>Optional target draft pack; when null the job ensures a draft for the character.</summary>
    public string? IdentityPackId { get; set; }
}
