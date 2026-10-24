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

    /// <summary>Enqueue an edit using the exact latest accepted compiler revision and source checksum.</summary>
    Task<SceneImageRecord> EnqueueEditAsync(
        SceneImageEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a PRESET edit pass on a completed scene image (B-133): relight it, or change its facial expression, from
    /// a picked preset. No compiler artifact is involved - the instruction is assembled deterministically from the
    /// preset's store rows - so the row records the preset and its instruction checksum instead of a revision.
    /// </summary>
    Task<SceneImageRecord> EnqueuePresetEditAsync(
        SceneImagePresetEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a multi-angle CAMERA edit pass on a completed scene image: orbit the source subject to a picked
    /// azimuth/elevation/distance. No compiler artifact is involved - the instruction is assembled deterministically
    /// from the editor LoRA's <c>&lt;sks&gt;</c> grammar - so the row records the pose and its instruction checksum.
    /// </summary>
    Task<SceneImageRecord> EnqueueMultiAngleEditAsync(
        SceneImageMultiAngleEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a deterministic crop of a completed scene image into a new derived image. No editor model,
    /// prompt revision or compiler artifact is involved — the crop is an operation, so the row it creates
    /// records the operation and inherits its source's lineage.
    /// </summary>
    Task<SceneImageRecord> EnqueueCropAsync(
        SceneImageCropRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue an enhance (ComfyUI upscale + scale down) of a completed scene image into a new derived
    /// image. Like a crop it is an operation — no editor model, prompt revision or compiler artifact — so
    /// the row it creates records the operation and inherits its source's lineage.
    /// </summary>
    Task<SceneImageRecord> EnqueueEnhanceAsync(
        SceneImageEnhanceRequest request, CancellationToken cancellationToken = default);

    Task<SceneImageRecord> EnqueueIdentityAsync(
        SceneImageIdentityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a separate identity-correction edit from the scene image editor. Binds approved
    /// identity-pack faces to the detected people of a completed source image and produces an
    /// immutable Identity-stage child lineage image. No typed content prompt is required — the
    /// face-only instruction is service-authored from the per-person bindings.
    /// </summary>
    Task<SceneImageRecord> EnqueueEditorIdentityAsync(
        SceneImageEditorIdentityRequest request, CancellationToken cancellationToken = default);

    Task<SceneImageRecord> EnqueueFinishAsync(
        SceneImageFinishRequest request, CancellationToken cancellationToken = default);

    /// <summary>Marks an exact in-flight image attempt as cancelled after its durable job is cancelled.</summary>
    Task<bool> TryCancelImageAsync(
        string sessionId,
        string imageId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Most recent completed production prompt for a group/brief. Pass <paramref name="promptStyle"/>
    /// to restrict to one prompt style (natural language vs Pony tags); legacy Unknown rows are
    /// treated as natural language. When null, the latest completed prompt of any style is returned.
    /// </summary>
    Task<SceneImagePromptRecord?> GetLatestCompletedProductionPromptAsync(
        string sessionId,
        string interactionId,
        string productionGroupId,
        string compiledMediaBriefId,
        SceneImagePromptStyle? promptStyle = null,
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

    /// <summary>All immutable attempts in one production group, newest first.</summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByProductionGroupAsync(
        string productionGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One rendered image's OWN stored row, by id — the facts a review surface shows (its tags, its settings snapshot,
    /// the prompt that was sent) rather than a projection of them. Null when the id names no row.
    ///
    /// <para>
    /// A read, so it carries no session guard. Every listing here is unguarded the same way; the guard belongs on the
    /// writes, which is where it is.
    /// </para>
    /// </summary>
    Task<SceneImageRecord?> GetImageAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set an attempt disposition without changing execution status.</summary>
    Task SetDispositionAsync(
        string imageId,
        string productionGroupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        CancellationToken cancellationToken = default);

    /// <summary>Interaction → image-count map for a session (workspace indicator). Counts Complete images only.</summary>
    Task<Dictionary<string, int>> CountImagesByInteractionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Delete an image: removes the DB row and the file on disk. Idempotent.</summary>
    Task DeleteImageAsync(
        string sessionId, string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the operator's name for a rendered image — what a list shows and what a location reference is described by
    /// — and returns the row as it now stands. A blank name clears it back to unnamed. Fails fast when the image does
    /// not exist or belongs to another session.
    /// </summary>
    Task<SceneImageRecord> SetDisplayNameAsync(
        string sessionId, string imageId, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The app's own rendered images that may stand as a reference, within one search scope: complete, still holding
    /// their bytes, and carrying a checksum — because those three are exactly what the reference resolver re-reads
    /// before it conditions on one.
    ///
    /// <para>
    /// The eligibility rule lives HERE rather than in each search surface, so a list can never offer an image the
    /// resolver would then refuse. A scope that names a level but omits the id that level needs fails with a
    /// diagnostic instead of being widened: answering "everything in the session" to a question about one moment is
    /// how a narrow search silently becomes a different one.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SceneImageRecord>> ListSearchableImagesAsync(
        SceneImageSearchScope scope, CancellationToken cancellationToken = default);
}
