namespace DreamGenClone.Web.Application.RolePlay.Models;

using DreamGenClone.Web.Application.RolePlay.Editing;

public sealed class CreateSceneAssetImageEditSessionRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
}

public sealed class EnqueueSceneAssetImageEditCompilationRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string RawIntent { get; set; } = string.Empty;
    public IReadOnlyList<string> ClarificationHistory { get; set; } = [];
}

public sealed class AppendSceneAssetImageEditPromptRevisionRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed class EnqueueSceneAssetImageEditRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string PromptRevisionId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public string PromptSha256 { get; set; } = string.Empty;
    public string EditorModelId { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }
}

/// <summary>
/// Queues a preset edit pass of an existing asset image into a new derived image (B-133). Like an identity edit
/// there is no prompt compilation: the instruction is assembled deterministically from the picked preset's store
/// rows, and the preset plus its instruction checksum travel with the queued row so the run can re-derive it.
/// </summary>
public sealed class EnqueueSceneAssetImagePresetEditRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;

    /// <summary>The picked preset, e.g. <c>image.preset.lighting.indoor-dim</c>. It names its own axis.</summary>
    public string PresetKey { get; set; } = string.Empty;

    /// <summary>The editor model the editor form selected, used unchanged by the run.</summary>
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>
    /// The character whose override rows the instruction is assembled from, when the edit belongs to one. The run
    /// re-derives with the SAME scope, so an override cannot be assembled one way and executed another.
    /// </summary>
    public string? CharacterId { get; set; }

    /// <summary>The batch the derived image joins; defaults to the source's own batch, which is what an attempt deck shows.</summary>
    public string? CandidateBatchId { get; set; }
}

/// <summary>
/// Queues a face-only identity correction of an existing asset image into a new derived image. Like the
/// scene identity run there is no prompt compilation: the instruction is authored from the bound
/// characters, and the approved identity-pack faces travel with the queued row as its references.
/// </summary>
public sealed class EnqueueSceneAssetImageIdentityEditRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;

    /// <summary>The editor model the editor form selected, used unchanged by the run.</summary>
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>One entry per detected person that is bound to a character's approved face.</summary>
    public IReadOnlyList<ImageIdentitySelection>? Selections { get; set; }
}

/// <summary>
/// Queues a deterministic crop of an existing asset image. There is no editor model, no prompt and no
/// compiler artifact: the crop is an operation, so the row it produces records operation provenance.
/// </summary>
public sealed class EnqueueSceneAssetImageCropRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public MediaEditCropOperation? Crop { get; set; }
}

/// <summary>
/// Queues an enhance of an existing asset image. The upscale model and target edge are resolved
/// configuration carried on the run, so the produced row records exactly what was applied.
/// </summary>
public sealed class EnqueueSceneAssetImageEnhanceRequest
{
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public MediaEditEnhanceOperation? Enhance { get; set; }
}