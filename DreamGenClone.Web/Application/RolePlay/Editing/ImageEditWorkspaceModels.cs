using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Which store an editable image belongs to. The edit workspace treats both stores through
/// <see cref="IImageEditWorkspaceService"/>; the kind only selects the adapter.
/// </summary>
public enum ImageEditSubjectKind
{
    SceneImage = 0,
    AssetImage = 1
}

/// <summary>
/// Identifies the image being edited, plus whatever routing context its store needs.
/// </summary>
public sealed record ImageEditSubject(
    ImageEditSubjectKind Kind,
    string ImageId,
    string? AssetId = null,
    string? SessionId = null,
    string? InteractionId = null,
    string? CandidateBatchId = null)
{
    public static ImageEditSubject ForSceneImage(string sessionId, string interactionId, string imageId)
        => new(ImageEditSubjectKind.SceneImage, imageId, SessionId: sessionId, InteractionId: interactionId);

    public static ImageEditSubject ForAssetImage(string assetId, string imageId)
        => new(ImageEditSubjectKind.AssetImage, imageId, AssetId: assetId);
}

/// <summary>The image the edit starts from.</summary>
public sealed record ImageEditSource(
    string ImageId,
    string FileRelativePath,
    string? ImageSize,
    string StatusText,
    bool IsComplete);

/// <summary>A durable edit session for one source image.</summary>
public sealed record ImageEditSessionView(
    string Id,
    string StatusText,
    bool IsCompleted,
    string? DescriptionText,
    string SourceImageSha256);

/// <summary>One prompt-compilation attempt against a session.</summary>
public sealed record ImageEditAttemptView(
    string Id,
    string StatusText,
    string? ParsedResultJson,
    string? Error,
    bool IsInFlight,
    bool IsFailed);

/// <summary>One stored prompt revision (compiler output or user edit).</summary>
public sealed record ImageEditRevisionView(string Id, int Ordinal, string Prompt, string PromptSha256, bool IsUserEdited);

/// <summary>The rendered result of an edit.</summary>
public sealed record ImageEditResultView(
    string ImageId,
    string? FileRelativePath,
    string StatusText,
    bool IsComplete,
    bool IsInFlight,
    string? ErrorMessage);

/// <summary>One image in the source's derived lineage chain.</summary>
public sealed record ImageEditLineageItem(
    string ImageId,
    string FileRelativePath,
    string StatusText,
    bool IsComplete,
    bool IsEdit,
    string? PromptSnapshot,
    DateTime CreatedUtc);

/// <summary>Everything the workspace hands to the adapter to run one edit.</summary>
public sealed record ImageEditRunRequest(
    ImageEditSubject Subject,
    string SessionId,
    string AttemptId,
    string RevisionId,
    string SourceImageSha256,
    string PromptSha256,
    string EditorModelId,
    IReadOnlyList<ReferenceApplicationSelection> ReferenceApplications);

/// <summary>Current workspace state, raised to hosts after every refresh.</summary>
public sealed record ImageEditWorkspaceSnapshot(
    ImageEditSubject Subject,
    string? SessionId,
    string? CompilationAttemptId,
    string? PromptRevisionId,
    string? ResultImageId,
    bool ResultComplete,
    string? SourceImageId);

/// <summary>A character choice that has exactly one approved identity pack with approved face references.</summary>
public sealed record ImageIdentityCharacterChoice(
    string CharacterId,
    string CharacterName,
    string IdentityPackId,
    int IdentityPackVersion,
    string CanonicalFaceAssetId,
    IReadOnlyList<SceneImageReferenceAsset> Faces);

/// <summary>The identity roster available for a subject, with an explicit reason when it is empty.</summary>
public sealed record ImageIdentityRosterResult(
    IReadOnlyList<ImageIdentityCharacterChoice> Characters,
    string? UnavailableReason);

/// <summary>One detected person bound to a character's approved face reference.</summary>
public sealed record ImageIdentitySelection(
    string TargetKey,
    string VisibleLocator,
    SceneImageEditTargetRegion? Region,
    string CharacterId,
    string CharacterName,
    string ReferenceAssetId);
