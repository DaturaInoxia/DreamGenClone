using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Manual source-image edit request from the scene-image studio.</summary>
public sealed class SceneImageEditRequest
{    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string PromptRevisionId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public string PromptSha256 { get; set; } = string.Empty;
    public string EditorModelId { get; set; } = string.Empty;
    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }

    /// <summary>
    /// The REGION this edit is confined to, or null to edit the whole frame (CASE-21). It travels with the edit rather
    /// than on its own request because a region edit IS an edit: the same instruction, the same compiled prompt
    /// revision, the same chosen editor model - plus the rectangle that pins everything outside it.
    ///
    /// Coordinates are PERCENT of the frame, so the same numbers mean the same place whatever the source's pixel size.
    /// A region needs a model whose graph can confine an edit (Qwen-Image-2.1), which is refused at enqueue with the
    /// Model Manager fix rather than after a render has been paid for.
    /// </summary>
    public MediaEditRegionOperation? Region { get; set; }

    [Obsolete("Use the compiled prompt revision identifiers and checksums. Raw instructions are never executed.")]
    public string Instruction { get; set; } = string.Empty;
}

/// <summary>
/// A PRESET edit pass on an existing scene image (B-133): relight it, or change its facial expression, from a picked
/// preset. Like the asset-store twin there is no compilation step - the instruction is assembled deterministically from
/// the preset's store rows - and the preset plus its instruction checksum are recorded on the queued row so the run
/// re-derives and proves the same text.
/// </summary>
public sealed class SceneImagePresetEditRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;

    /// <summary>The picked preset, e.g. <c>image.preset.lighting.indoor-dim</c>. It names its own axis.</summary>
    public string PresetKey { get; set; } = string.Empty;

    /// <summary>The editor model the editor form selected, used unchanged by the run.</summary>
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>The character whose override rows the instruction is assembled from, when the edit belongs to one.</summary>
    public string? CharacterId { get; set; }
}