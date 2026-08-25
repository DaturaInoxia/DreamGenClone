namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Manual source-image edit request from the scene-image studio.</summary>
public sealed class SceneImageEditRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string PromptRevisionId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public string PromptSha256 { get; set; } = string.Empty;

    [Obsolete("Use the compiled prompt revision identifiers and checksums. Raw instructions are never executed.")]
    public string Instruction { get; set; } = string.Empty;
}