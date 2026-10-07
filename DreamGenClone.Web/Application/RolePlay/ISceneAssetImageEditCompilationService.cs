using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneAssetImageEditCompilationService
{
    Task<SceneAssetImageEditSession> CreateSessionAsync(CreateSceneAssetImageEditSessionRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt> EnqueueCompilationAsync(EnqueueSceneAssetImageEditCompilationRequest request, CancellationToken cancellationToken = default);
    Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditPromptRevision> AppendPromptRevisionAsync(AppendSceneAssetImageEditPromptRevisionRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImage> EnqueueEditAsync(EnqueueSceneAssetImageEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a face-only identity correction of an existing asset image into a new derived image. No prompt
    /// compilation, edit session or revision is involved: the instruction is authored from the bound
    /// characters and the approved identity-pack faces become the run's references, exactly as the scene
    /// identity stage does. The run uses the editor model the editor form selected.
    /// </summary>
    Task<SceneAssetImage> EnqueueIdentityEditAsync(
        EnqueueSceneAssetImageIdentityEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a PRESET edit pass of an existing asset image into a new derived image (B-133): relight it, or change
    /// its facial expression, from a picked preset. Like an identity edit there is no prompt compilation - the
    /// instruction is assembled deterministically from the preset's store rows - and the preset plus its instruction
    /// checksum are recorded on the row so the run re-derives and proves the same text.
    /// </summary>
    Task<SceneAssetImage> EnqueuePresetEditAsync(
        EnqueueSceneAssetImagePresetEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a multi-angle CAMERA edit pass of an existing asset image into a new derived image: orbit the source
    /// subject to a picked azimuth/elevation/distance. Like a preset edit there is no prompt compilation - the
    /// instruction is assembled deterministically from the editor LoRA's <c>&lt;sks&gt;</c> grammar - and the pose plus its
    /// instruction checksum are recorded on the row so the run re-derives and proves the same text.
    /// </summary>
    Task<SceneAssetImage> EnqueueMultiAngleEditAsync(
        EnqueueSceneAssetImageMultiAngleEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a deterministic crop of an existing asset image into a new derived image. No editor model,
    /// prompt or compilation revision is involved.
    /// </summary>
    Task<SceneAssetImage> EnqueueCropAsync(EnqueueSceneAssetImageCropRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an enhance (ComfyUI upscale + scale down) of an existing asset image into a new derived
    /// image. The upscaler and target edge come from persisted configuration, carried on the run.
    /// </summary>
    Task<SceneAssetImage> EnqueueEnhanceAsync(EnqueueSceneAssetImageEnhanceRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneAssetImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
}