using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneBeatDiagnosticsRepository
{
    Task<SceneBeatStageMetrics> GetMetricsAsync(
        SceneBeatPipelineStage stage,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneBeatDiagnosticAttemptSummary>> GetRecentDiagnosticsAsync(
        SceneBeatPipelineStage stage,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SceneBeatDiagnosticsPruneRun> PruneRawDiagnosticsAsync(
        string functionDefaultId,
        int retentionDays,
        DateTime cutoffUtc,
        DateTime prunedUtc,
        string actor,
        CancellationToken cancellationToken = default);
}

public interface ISceneBeatDiagnosticsService
{
    Task<IReadOnlyList<SceneBeatStageMetrics>> GetMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneBeatDiagnosticAttemptSummary>> GetRecentDiagnosticsAsync(
        SceneBeatPipelineStage stage,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SceneBeatDiagnosticsPruneRun> PruneExpiredAsync(
        string actor,
        CancellationToken cancellationToken = default);
}