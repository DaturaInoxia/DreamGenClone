using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

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

    /// <summary>
    /// Saves the operator's UNCOMMITTED prompt-panel state — the source they picked, the intent they typed, and the
    /// per-element edits from the Prompt Input inspector — on the group, so work they have not compiled yet survives a
    /// reload.
    /// </summary>
    /// <remarks>
    /// Generate Prompt is not the only save point any more. It stores a COMPILE; this stores the panel, which is what
    /// an operator expects a reload to bring back. The inspector edits were the last piece missing: they lived only in
    /// the page's memory, so a reload discarded them and the next Generate sent no overrides at all.
    /// </remarks>
    Task<SceneImageProductionGroup> SavePromptDraftAsync(
        string groupId,
        SceneImagePromptSource source,
        string? userIntent,
        ScenePromptOverrides? overrides = null,
        SceneImageExplicitness explicitness = SceneImageExplicitness.Explicit,
        CameraHolderPartsMode cameraHolderParts = CameraHolderPartsMode.None,
        string? cameraHolderPartsText = null,
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

    /// <summary>
    /// Withdraw the group's CURRENT approval, leaving it with none.
    ///
    /// <para>
    /// It exists so the review deck's two routes offer one set of verdicts: an asset candidate has always been
    /// revocable, and a production frame was not, so the same operator gesture on two halves of one deck did two
    /// different things. It is refused by name when the group has no current approval to withdraw, or is archived.
    /// </para>
    /// </summary>
    Task<ApprovedSceneFrameDecision> RevokeApprovalAsync(
        string groupId,
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