using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record GenerateSceneBeatProductionPlanRequest(string CatalogueId, string BeatId);

public sealed record SceneBeatProductionPlanJobPayload(string PlanId, string AttemptId);

public interface ISceneBeatProductionPipelineService
{
    Task<SceneBeatProductionPlan> EnqueueAsync(
        GenerateSceneBeatProductionPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan> ReplaceAsync(
        GenerateSceneBeatProductionPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan?> GetCurrentAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(string planId, CancellationToken cancellationToken = default);
}