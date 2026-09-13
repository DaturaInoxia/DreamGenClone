using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Opens an edit session for a stored source image of any subject kind.</summary>
public sealed class CreateMediaEditSessionRequest
{
    public MediaEditSubjectKind SubjectKind { get; set; }

    /// <summary>Interaction id for scene images, asset id for asset images.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>The role-play session id when the subject has one; null for assets.</summary>
    public string? SubjectScopeId { get; set; }

    public string SourceImageId { get; set; } = string.Empty;
}

public sealed class EnqueueMediaEditCompilationRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string RawIntent { get; set; } = string.Empty;
    public IReadOnlyList<string> ClarificationHistory { get; set; } = [];
}

public sealed class AppendMediaEditPromptRevisionRequest
{
    public string EditSessionId { get; set; } = string.Empty;
    public string CompilationAttemptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
