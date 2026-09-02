using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record GenerateSceneMomentsRequest(string BeatProductionPlanId);

public sealed record SceneMomentDiscoveryJobPayload(string MomentSetId, string AttemptId);

public interface ISceneMomentDiscoveryPipelineService
{
    Task<SceneMomentSet> EnqueueAsync(
        GenerateSceneMomentsRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneMomentSet> ReplaceAsync(
        GenerateSceneMomentsRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneMomentSet?> GetCurrentAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(string momentSetId, CancellationToken cancellationToken = default);
}