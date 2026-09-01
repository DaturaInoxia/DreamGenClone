using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneMomentSetRepository
{
    Task CreateVersionAsync(
        SceneMomentSet momentSet,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default);

    Task<SceneMomentSet?> GetAsync(
        string momentSetId,
        CancellationToken cancellationToken = default);

    Task<SceneMomentSet?> GetCurrentAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<bool> TryStartAttemptAsync(
        string momentSetId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAttemptAsync(
        string momentSetId,
        SceneBeatAnalysisAttempt attempt,
        SceneMomentSetData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAttemptAsync(
        string momentSetId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelCurrentAsync(
        string momentSetId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);
}