using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record CreateSceneImageProductionGroupRequest(
    string SessionId,
    string InteractionId,
    string CatalogueId,
    string BeatId,
    string BeatProductionPlanId,
    int BeatProductionPlanVersion,
    string MomentSetId,
    int MomentSetVersion,
    string MomentId,
    string MomentEnrichmentId,
    int MomentEnrichmentRevision,
    string Pov,
    string? CameraIntentSnapshotJson);

public interface ISceneImageProductionService
{
    Task<CompiledMediaBrief> GetOrCreateStillBriefAsync(
        string productionGroupId,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup> GetOrCreateGroupAsync(
        CreateSceneImageProductionGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup?> GetCurrentGroupAsync(
        string momentEnrichmentId,
        string pov,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneImageRecord>> ListAttemptsAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    Task SetDispositionAsync(
        string imageId,
        string groupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        CancellationToken cancellationToken = default);

    Task<ApprovedSceneFrameDecision> ApproveAsync(
        string groupId,
        string imageId,
        string sha256,
        string decidedBy,
        string? note,
        CancellationToken cancellationToken = default);

    Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(
        CancellationToken cancellationToken = default);

    Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(
        SceneImageAttemptRetentionPolicy policy,
        long? expectedVersion,
        CancellationToken cancellationToken = default);

    Task PurgeRejectedBytesAsync(
        string imageId,
        string requestedBy,
        CancellationToken cancellationToken = default);

    Task<SceneAsset> PromoteApprovedFrameAsync(
        string groupId,
        string name,
        SceneAssetType type,
        string? associationMetadataJson,
        string? characterProfileId,
        string requestedBy,
        CancellationToken cancellationToken = default);
}