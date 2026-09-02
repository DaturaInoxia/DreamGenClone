using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneImageProductionGroupRepository
{
    Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(
        CancellationToken cancellationToken = default);

    Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(
        SceneImageAttemptRetentionPolicy policy,
        long? expectedVersion,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        SceneImageProductionGroup group,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup?> GetCurrentAsync(
        string momentEnrichmentId,
        string pov,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneImageProductionGroup>> ListByInteractionAsync(
        string sessionId,
        string interactionId,
        CancellationToken cancellationToken = default);

    Task<ApprovedSceneFrameDecision?> GetApprovalDecisionAsync(
        string decisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    Task<ApprovedSceneFrameDecision> ApproveAsync(
        string groupId,
        string imageId,
        string sha256,
        string decidedBy,
        string? note,
        DateTime decisionUtc,
        CancellationToken cancellationToken = default);

    Task<ApprovedSceneFrameDecision> RevokeCurrentApprovalAsync(
        string groupId,
        string decidedBy,
        string? note,
        DateTime decisionUtc,
        CancellationToken cancellationToken = default);
}
