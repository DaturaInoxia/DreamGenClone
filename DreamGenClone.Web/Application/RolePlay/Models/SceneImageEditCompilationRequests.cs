using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Web.Application.RolePlay.Models;

public sealed class CreateSceneImageEditSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
}

public sealed class EnqueueSceneImageEditCompilationRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string RawIntent { get; set; } = string.Empty;
    public IReadOnlyList<string> ClarificationHistory { get; set; } = [];

    /// <summary>
    /// The image editor model the instruction will be compiled FOR, so the compiler can match the editor's graph
    /// kind. Null falls back to the function-default editor.
    /// </summary>
    public string? EditorModelId { get; set; }

    /// <summary>The REGION this edit is confined to, or null to edit the whole frame. A set region adds the
    /// confinement clause to the compiled prompt (B135-008 N5).</summary>
    public MediaEditRegionOperation? Region { get; set; }

    /// <summary>The OUTPAINT extension this edit is, or null for a whole-frame or region edit. An outpaint adds
    /// the canvas-extension clause to the compiled prompt (CASE-24).</summary>
    public MediaEditOutpaintOperation? Outpaint { get; set; }
}

public sealed class AppendSceneImageEditPromptRevisionRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}