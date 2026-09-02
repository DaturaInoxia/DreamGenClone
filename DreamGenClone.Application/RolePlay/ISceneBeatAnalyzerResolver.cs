using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.RolePlay;

public sealed record ResolvedSceneBeatAnalyzer(
    string FunctionDefaultId,
    string ModelId,
    string ProviderId,
    ResolvedModel Model,
    StructuredOutputMode StructuredOutputMode,
    int? MaximumContextTokens,
    int? MaximumOutputTokens,
    int MaxConcurrentJobs,
    int LeaseSeconds,
    int PollIntervalMilliseconds,
    IReadOnlyList<int> RetryDelaysSeconds,
    int DiagnosticsRetentionDays,
    int MaximumCatalogueEntries);

public interface ISceneBeatAnalyzerResolver
{
    Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default);
}