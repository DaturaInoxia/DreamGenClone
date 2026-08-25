using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Public orchestration surface for the scene-image feature. The studio, gallery, and workspace
/// interact with image generation exclusively through this interface.
/// </summary>
public interface ISceneImageService
{
    Task<SceneImageBeatAnalysisRecord> EnqueueBeatAnalysisAsync(
        SceneImageBeatGenerationRequest request, CancellationToken cancellationToken = default);

    Task<SceneImageBeatAnalysisRecord?> GetBeatAnalysisByTurnAsync(
        string sessionId, string turnId, CancellationToken cancellationToken = default);

    /// <summary>Enqueue pre-processor prompt generation. Fails fast on missing session/interaction
    /// or a missing pre-processor function default. Creates a Pending prompt record and enqueues a
    /// SceneImagePromptGeneration job (dedupes by record id).</summary>
    Task<SceneImagePromptRecord> EnqueuePromptAsync(
        ScenePromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Enqueue image rendering from a (possibly edited) prompt. Fails fast on missing
    /// session/interaction or a missing image function default. Creates a Pending image record
    /// referencing the prompt record and enqueues a SceneImageRendering job (dedupes by record id).</summary>
    Task<SceneImageRecord> EnqueueRenderAsync(
        SceneRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Enqueue a manual edit from an existing completed image using the configured source-image editor.</summary>
    Task<SceneImageRecord> EnqueueEditAsync(
        SceneImageEditRequest request, CancellationToken cancellationToken = default);

    Task<SceneImagePromptRecord?> GetPromptAsync(
        string sessionId, string promptId, CancellationToken cancellationToken = default);

    /// <summary>Most recent prompt record for an interaction (used when reopening the studio).</summary>
    Task<SceneImagePromptRecord?> GetLatestPromptAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);

    /// <summary>Most recent completed prompt for an exact beat and POV in the current analysis.</summary>
    Task<SceneImagePromptRecord?> GetLatestCompletedPromptAsync(
        string sessionId,
        string interactionId,
        string beatAnalysisId,
        string beatId,
        string pov,
        CancellationToken cancellationToken = default);

    /// <summary>Persist the user-edited prompt text back to an existing prompt record's
    /// <c>OutputPrompt</c> so a later studio reopen shows the edited version. Fails fast if the
    /// record is not found or already Complete.</summary>
    Task UpdatePromptOutputAsync(
        string sessionId, string promptId, string outputPrompt, CancellationToken cancellationToken = default);

    /// <summary>All images for one interaction (studio results strip).</summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);

    /// <summary>All images for a session (gallery page).</summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Interaction → image-count map for a session (workspace indicator). Counts Complete images only.</summary>
    Task<Dictionary<string, int>> CountImagesByInteractionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Delete an image: removes the DB row and the file on disk. Idempotent.</summary>
    Task DeleteImageAsync(
        string sessionId, string imageId, CancellationToken cancellationToken = default);
}
