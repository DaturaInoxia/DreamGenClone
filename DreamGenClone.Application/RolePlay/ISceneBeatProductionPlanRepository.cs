using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneBeatProductionPlanRepository
{
    Task CreateVersionAsync(
        SceneBeatProductionPlan plan,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan?> GetAsync(string planId, CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan?> GetCurrentAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<bool> TryStartAttemptAsync(
        string planId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAttemptAsync(
        string planId,
        SceneBeatAnalysisAttempt attempt,
        SceneBeatProductionPlanData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAttemptAsync(
        string planId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelCurrentAsync(
        string planId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);
}