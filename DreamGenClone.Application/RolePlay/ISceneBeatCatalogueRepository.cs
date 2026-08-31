using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneBeatCatalogueRepository
{
    Task CreateVersionAsync(
        SceneBeatCatalogue catalogue,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue?> GetAsync(string catalogueId, CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue?> GetCurrentByTurnAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<bool> TryStartAttemptAsync(
        string catalogueId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        string executionSettingsJson,
        DateTime startedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAttemptAsync(
        string catalogueId,
        SceneBeatAnalysisAttempt attempt,
        IReadOnlyList<SceneBeatCatalogueEntry> entries,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAttemptAsync(
        string catalogueId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelCurrentAsync(
        string catalogueId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);
}