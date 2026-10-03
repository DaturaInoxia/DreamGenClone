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

    /// <summary>The outpaint extension this edit is confined to, or null to keep the frame (CASE-24). Mutually
    /// exclusive with <see cref="Region"/>: the newly exposed strip is generated and the original preserved.</summary>
    public MediaEditOutpaintOperation? Outpaint { get; set; }

    /// <summary>
    /// The scene LoRAs (unlock / act / anatomy / style) the operator picked for THIS edit, or null for none. It is
    /// carried onto the queued run rather than left on the page for the same reason the references are: the stack that
    /// renders the edit has to be the stack the operator saw when they queued it.
    /// </summary>
    public IReadOnlyList<SceneImageLoraSelection>? SceneLoras { get; set; }

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

    /// <summary>
    /// The REGION this edit is confined to, or null to edit the whole frame. A preset edit is the same instruction
    /// either way - a relight of one part of the frame is a relight - so the rectangle rides on the request instead of
    /// naming a second kind of run. Coordinates are PERCENT of the frame, and a model whose graph cannot confine an
    /// edit is refused at enqueue rather than after a render.
    /// </summary>
    public MediaEditRegionOperation? Region { get; set; }
}