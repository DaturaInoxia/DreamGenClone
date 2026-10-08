using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The durable payload for a video render: the record IS the job's state, so the payload carries its id.</summary>
public sealed record SceneVideoRenderingJobPayload(string RecordId);

/// <summary>
/// What the composer resolved to open on (R4): the seed reference plus whatever the origin already implies about
/// the scene, so a coverage plan's own camera/motion intent lands in the Intent tab instead of being retyped.
/// </summary>
/// <param name="OriginKind">Which origin produced this seed.</param>
/// <param name="OriginId">The scene image id, asset image id, or coverage plan id.</param>
/// <param name="FileRelativePath">Relative path under the scene-image root, when the origin implies a picture.</param>
/// <param name="FileName">The file name to upload the seed under.</param>
/// <param name="SuggestedTitle">A title for the composition, from the origin when it has one.</param>
/// <param name="CoverageCameraIntent">The coverage plan's camera intent, verbatim.</param>
/// <param name="CoverageMotionIntent">The coverage plan's motion intent, verbatim.</param>
/// <param name="CoverageDurationHint">The coverage plan's duration intent, verbatim.</param>
/// <param name="SessionId">The session the origin belongs to, when there is one.</param>
/// <param name="InteractionId">The interaction the origin belongs to, when there is one.</param>
public sealed record SceneVideoSeed(
    SceneVideoOriginKind OriginKind,
    string? OriginId,
    string? FileRelativePath,
    string? FileName,
    string? SuggestedTitle,
    string? CoverageCameraIntent,
    string? CoverageMotionIntent,
    string? CoverageDurationHint,
    string? SessionId,
    string? InteractionId);

/// <summary>
/// One composition to queue: the record (with its compiled prompt snapshot and full settings), the ordered
/// references it renders from, and whether it starts now or is staged for later (R3).
/// </summary>
public sealed record SceneVideoComposeRequest(
    SceneVideoRecord Record,
    IReadOnlyList<SceneVideoReference> References,
    bool StartImmediately);

/// <summary>
/// The Video Composer's application surface: seed resolution for every route, composition persistence, and the
/// staged/queued/start/cancel lifecycle over the durable video lane.
/// </summary>
public interface ISceneVideoService
{
    /// <summary>
    /// Resolves what a route seeds from. Returns null for the standalone origin. Fails fast when an id does not
    /// resolve to a real, usable picture rather than opening an empty composer that silently lost its reference.
    /// </summary>
    Task<SceneVideoSeed?> ResolveSeedAsync(
        SceneVideoOriginKind originKind,
        string? originId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the bytes of a stored reference image (all image kinds live under the scene-image root).</summary>
    Task<byte[]> ReadReferenceBytesAsync(string fileRelativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a reference's bytes from the root it names (B-156). A continuation's anchor frame lives under the
    /// SCENE-VIDEO root, and the root is selected explicitly rather than searched - a silent search would hide a
    /// misconfigured reference.
    /// </summary>
    Task<byte[]> ReadReferenceBytesAsync(
        SceneVideoReference reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a continuation of a finished clip (B-156): walks the chain, enforces the configured budget,
    /// extracts and hashes the source's final frame, and returns a seed. It NEVER creates a record - the composer
    /// creates one when the operator queues or saves.
    /// </summary>
    Task<SceneVideoContinuationSeed> PrepareContinuationAsync(
        string sourceVideoId,
        SceneVideoContinuationKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>The clips that continue this one, newest first (lineage view).</summary>
    Task<IReadOnlyList<SceneVideoRecord>> ListContinuationsAsync(
        string sourceVideoId, CancellationToken cancellationToken = default);

    /// <summary>Persists the composition and admits it to the video lane, staged or queued.</summary>
    Task<SceneVideoRecord> EnqueueAsync(
        SceneVideoComposeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Starts a staged composition (the Queue tab's "Start now" on a staged job).</summary>
    Task<SceneVideoRecord> StartStagedAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a composition that has not finished.</summary>
    Task<SceneVideoRecord> CancelAsync(string recordId, CancellationToken cancellationToken = default);

    Task<SceneVideoRecord?> GetAsync(string recordId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneVideoRecord>> ListRecentAsync(
        int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneVideoRecord>> ListBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);
}
