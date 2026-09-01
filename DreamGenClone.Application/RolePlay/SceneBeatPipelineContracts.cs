using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record GenerateSceneBeatCatalogueRequest(string SessionId, string TurnId);

public sealed record SceneBeatCatalogueJobPayload(string CatalogueId, string AttemptId);

public sealed record SceneBeatAnalyzerExecutionSnapshot(
    string FunctionDefaultId,
    string ModelId,
    string ProviderId,
    string ProviderBaseUrl,
    string ChatCompletionsPath,
    int ProviderTimeoutSeconds,
    bool RequiresCredential,
    string ModelIdentifier,
    double Temperature,
    double TopP,
    int MaxTokens,
    string ProviderName,
    bool SupportsThinkingControl,
    ThinkingMode ThinkingMode,
    StructuredOutputMode StructuredOutputMode,
    int? MaximumContextTokens,
    int? MaximumOutputTokens,
    int MaxConcurrentJobs,
    int LeaseSeconds,
    int PollIntervalMilliseconds,
    IReadOnlyList<int> RetryDelaysSeconds,
    int DiagnosticsRetentionDays,
    int MaximumCatalogueEntries)
{
    public static SceneBeatAnalyzerExecutionSnapshot FromResolved(ResolvedSceneBeatAnalyzer analyzer)
        => new(
            analyzer.FunctionDefaultId,
            analyzer.ModelId,
            analyzer.ProviderId,
            analyzer.Model.ProviderBaseUrl,
            analyzer.Model.ChatCompletionsPath,
            analyzer.Model.ProviderTimeoutSeconds,
            !string.IsNullOrWhiteSpace(analyzer.Model.ApiKeyEncrypted),
            analyzer.Model.ModelIdentifier,
            analyzer.Model.Temperature,
            analyzer.Model.TopP,
            analyzer.Model.MaxTokens,
            analyzer.Model.ProviderName,
            analyzer.Model.SupportsThinkingControl,
            analyzer.Model.ThinkingMode,
            analyzer.StructuredOutputMode,
            analyzer.MaximumContextTokens,
            analyzer.MaximumOutputTokens,
            analyzer.MaxConcurrentJobs,
            analyzer.LeaseSeconds,
            analyzer.PollIntervalMilliseconds,
            analyzer.RetryDelaysSeconds,
            analyzer.DiagnosticsRetentionDays,
            analyzer.MaximumCatalogueEntries);

    public ResolvedSceneBeatAnalyzer ToResolved(string? encryptedCredential)
        => new(
            FunctionDefaultId,
            ModelId,
            ProviderId,
            new ResolvedModel(
                ProviderBaseUrl,
                ChatCompletionsPath,
                ProviderTimeoutSeconds,
                encryptedCredential,
                ModelIdentifier,
                Temperature,
                TopP,
                MaxTokens,
                ProviderName,
                IsSessionOverride: false)
            {
                SupportsThinkingControl = SupportsThinkingControl,
                ThinkingMode = ThinkingMode
            },
            StructuredOutputMode,
            MaximumContextTokens,
            MaximumOutputTokens,
            MaxConcurrentJobs,
            LeaseSeconds,
            PollIntervalMilliseconds,
            RetryDelaysSeconds,
            DiagnosticsRetentionDays,
            MaximumCatalogueEntries);
}

public interface ISceneBeatPipelineService
{
    Task<SceneBeatCatalogue> EnqueueCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue> ReplaceCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue?> GetCurrentCatalogueAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);

    Task CancelCatalogueAsync(string catalogueId, CancellationToken cancellationToken = default);
}