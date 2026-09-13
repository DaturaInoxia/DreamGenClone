using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>Identifies the owning subject of an edit session.</summary>
public sealed record MediaEditSubjectRef(MediaEditSubjectKind Kind, string SubjectId, string? SubjectScopeId = null)
{
    public static MediaEditSubjectRef ForSceneImage(string sessionId, string interactionId)
        => new(MediaEditSubjectKind.SceneImage, interactionId, sessionId);

    public static MediaEditSubjectRef ForAssetImage(string assetId)
        => new(MediaEditSubjectKind.AssetImage, assetId);

    public static MediaEditSubjectRef FromSession(MediaEditSession session)
        => new(session.SubjectKind, session.SubjectId, session.SubjectScopeId);
}

/// <summary>The stored source image an edit session starts from.</summary>
public sealed record MediaEditSourceImage(string ImageId, string FileRelativePath, string Sha256);

/// <summary>
/// The one subject-specific seam of the edit pipeline: how to reach the source image's row and bytes.
/// Everything else (compile, describe, run, provenance) is shared.
/// </summary>
public interface IMediaEditSubjectSource
{
    MediaEditSubjectKind Kind { get; }

    /// <summary>
    /// Loads the source image and enforces that it is complete, stored, and owned by the subject.
    /// </summary>
    Task<MediaEditSourceImage> RequireSourceAsync(
        MediaEditSubjectRef subject, string sourceImageId, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string fileRelativePath, CancellationToken cancellationToken = default);
}

/// <summary>Selects the source seam for a subject kind. A missing seam fails fast — never a default.</summary>
public sealed class MediaEditSubjectSourceResolver
{
    private readonly IReadOnlyList<IMediaEditSubjectSource> _sources;

    public MediaEditSubjectSourceResolver(IEnumerable<IMediaEditSubjectSource> sources)
        => _sources = sources.ToList();

    public IMediaEditSubjectSource Resolve(MediaEditSubjectKind kind)
        => _sources.FirstOrDefault(source => source.Kind == kind)
            ?? throw new InvalidOperationException($"No media edit subject source is registered for subject kind '{kind}'.");
}
