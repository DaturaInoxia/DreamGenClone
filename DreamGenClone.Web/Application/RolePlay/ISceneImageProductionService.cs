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
    Task<IReadOnlyList<SceneImageIdentityReadiness>> ResolveIdentityReadinessAsync(
        string productionGroupId,
        IReadOnlyList<SceneImageIdentityReferenceSelection>? selections = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve an explicit list of (character, chosen face) identity selections to their approved
    /// pack assets, independent of any production group or Moment. Exactly one approved pack per
    /// character is required, and each chosen face must be an approved, owned
    /// <see cref="SceneImageReferenceAssetKind.Face"/> asset of that pack.
    /// </summary>
    Task<IReadOnlyList<SceneImageIdentityReadiness>> ResolveCharacterIdentitySelectionsAsync(
        IReadOnlyList<SceneImageIdentityReferenceSelection> selections,
        CancellationToken cancellationToken = default);

    Task<CompiledMediaBrief> GetOrCreateStillBriefAsync(
        string productionGroupId,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup> GetOrCreateGroupAsync(
        CreateSceneImageProductionGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup> SkipIdentityAsync(
        string groupId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup> ClearIdentitySkipAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup?> GetCurrentGroupAsync(
        string momentEnrichmentId,
        string pov,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup?> GetGroupAsync(
        string groupId,
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