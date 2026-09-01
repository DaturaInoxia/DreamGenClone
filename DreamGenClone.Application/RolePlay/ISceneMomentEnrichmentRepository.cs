using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneMomentEnrichmentRepository
{
    Task CreateRevisionAsync(
        SceneMomentEnrichment enrichment,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment?> GetAsync(
        string enrichmentId,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment?> GetCurrentAsync(
        string momentSetId,
        string momentId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<bool> TryStartAttemptAsync(
        string enrichmentId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAttemptAsync(
        string enrichmentId,
        SceneBeatAnalysisAttempt attempt,
        SceneMomentEnrichmentData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAttemptAsync(
        string enrichmentId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelCurrentAsync(
        string enrichmentId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);
}
