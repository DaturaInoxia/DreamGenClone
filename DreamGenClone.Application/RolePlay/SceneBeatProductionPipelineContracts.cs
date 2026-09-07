using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Application.RolePlay;

public sealed record GenerateSceneBeatProductionPlanRequest(string CatalogueId, string BeatId);

public sealed record SceneBeatProductionPlanJobPayload(string PlanId, string AttemptId);

public sealed record SceneBeatProductionStatus(
    SceneBeatProductionPlan Plan,
    SceneBeatAnalysisAttempt? Attempt,
    DurableBackgroundJob? Job);

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

    Task<SceneBeatProductionPlan?> GetAsync(
        string planId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatProductionStatus?> GetCurrentStatusAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(string planId, CancellationToken cancellationToken = default);
}