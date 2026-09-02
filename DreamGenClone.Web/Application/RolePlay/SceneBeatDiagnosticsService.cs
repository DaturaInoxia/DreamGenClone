using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatDiagnosticsService : ISceneBeatDiagnosticsService
{
    private static readonly SceneBeatPipelineStage[] Stages =
    [
        SceneBeatPipelineStage.Catalogue,
        SceneBeatPipelineStage.BeatProduction,
        SceneBeatPipelineStage.MomentDiscovery,
        SceneBeatPipelineStage.MomentEnrichment
    ];

    private readonly ISceneBeatDiagnosticsRepository _repository;
    private readonly ISceneBeatAnalyzerResolver _analyzerResolver;
    private readonly TimeProvider _timeProvider;

    public SceneBeatDiagnosticsService(
        ISceneBeatDiagnosticsRepository repository,
        ISceneBeatAnalyzerResolver analyzerResolver,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _analyzerResolver = analyzerResolver;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<SceneBeatStageMetrics>> GetMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var metrics = new List<SceneBeatStageMetrics>(Stages.Length);
        foreach (var stage in Stages)
            metrics.Add(await _repository.GetMetricsAsync(stage, cancellationToken));
        return metrics;
    }

    public Task<IReadOnlyList<SceneBeatDiagnosticAttemptSummary>> GetRecentDiagnosticsAsync(
        SceneBeatPipelineStage stage,
        int limit,
        CancellationToken cancellationToken = default)
        => _repository.GetRecentDiagnosticsAsync(stage, limit, cancellationToken);

    public async Task<SceneBeatDiagnosticsPruneRun> PruneExpiredAsync(
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Diagnostics prune actor is required.", nameof(actor));

        var analyzer = await _analyzerResolver.ResolveAsync(cancellationToken);
        var prunedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = prunedUtc.AddDays(-analyzer.DiagnosticsRetentionDays);
        return await _repository.PruneRawDiagnosticsAsync(
            analyzer.FunctionDefaultId,
            analyzer.DiagnosticsRetentionDays,
            cutoffUtc,
            prunedUtc,
            actor.Trim(),
            cancellationToken);
    }
}