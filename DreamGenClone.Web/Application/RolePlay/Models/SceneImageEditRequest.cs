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