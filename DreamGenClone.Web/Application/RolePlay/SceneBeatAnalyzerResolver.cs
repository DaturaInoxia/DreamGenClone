using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatAnalyzerResolver : ISceneBeatAnalyzerResolver
{
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;

    public SceneBeatAnalyzerResolver(
        IFunctionDefaultRepository functionDefaultRepository,
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository)
    {
        _functionDefaultRepository = functionDefaultRepository;
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
    }

    public async Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var function = AppFunction.RolePlaySceneBeatAnalyzer;
        var functionDefault = await _functionDefaultRepository.GetByFunctionAsync(function, cancellationToken)
            ?? throw new ModelResolutionException(
                $"No model configured for function '{function}'. Configure it in Model Manager (/model-manager).");

        var configurationError = functionDefault.ValidateSceneBeatAnalyzerConfiguration();
        if (configurationError is not null)
            throw new ModelResolutionException($"Function '{function}' configuration is invalid: {configurationError}");
        if (functionDefault.ThinkingMode == ThinkingMode.Default)
            throw new ModelResolutionException($"Function '{function}' requires an explicit thinking mode.");

        var model = await _modelRepository.GetByIdAsync(functionDefault.ModelId, cancellationToken);
        if (model is null || !model.IsEnabled)
            throw new ModelResolutionException($"The configured model for function '{function}' is unavailable.");
        if (model.ModelKind != ModelKind.Text)
            throw new ModelResolutionException($"Function '{function}' requires a text model.");
        if (model.StructuredOutputMode is not (StructuredOutputMode.StrictJsonSchema or StructuredOutputMode.JsonObject))
            throw new ModelResolutionException($"Model '{model.DisplayName}' requires an explicit supported structured-output mode.");
        if (model.MaximumContextTokens is <= 0)
            throw new ModelResolutionException($"Model '{model.DisplayName}' maximum context token capability must be positive when configured.");
        if (model.MaximumOutputTokens is <= 0)
            throw new ModelResolutionException($"Model '{model.DisplayName}' maximum output token capability must be positive when configured.");
        if (model.MaximumOutputTokens.HasValue && functionDefault.MaxTokens > model.MaximumOutputTokens.Value)
            throw new ModelResolutionException(
                $"Function '{function}' Max Tokens exceeds model '{model.DisplayName}' maximum output capability.");
        if (model.MaximumContextTokens.HasValue && functionDefault.MaxTokens > model.MaximumContextTokens.Value)
            throw new ModelResolutionException(
                $"Function '{function}' Max Tokens exceeds model '{model.DisplayName}' maximum context capability.");
        if (functionDefault.ThinkingMode != ThinkingMode.Default && !model.SupportsThinkingControl)
            throw new ModelResolutionException(
                $"Function '{function}' configures thinking control, but model '{model.DisplayName}' does not support it.");

        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
            throw new ModelResolutionException($"The configured provider for function '{function}' is unavailable.");
        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var providerUri)
            || providerUri.Scheme is not ("http" or "https"))
            throw new ModelResolutionException($"Function '{function}' provider base URL is invalid.");
        if (string.IsNullOrWhiteSpace(provider.ChatCompletionsPath)
            || !provider.ChatCompletionsPath.StartsWith("/", StringComparison.Ordinal))
            throw new ModelResolutionException($"Function '{function}' provider chat completions path is invalid.");
        if (provider.TimeoutSeconds < 1)
            throw new ModelResolutionException($"Function '{function}' provider timeout must be positive.");

        var resolvedModel = new ResolvedModel(
            provider.BaseUrl,
            provider.ChatCompletionsPath,
            provider.TimeoutSeconds,
            provider.ApiKeyEncrypted,
            model.ModelIdentifier,
            functionDefault.Temperature,
            functionDefault.TopP,
            functionDefault.MaxTokens,
            provider.Name,
            IsSessionOverride: false)
        {
            SupportsThinkingControl = model.SupportsThinkingControl,
            ThinkingMode = functionDefault.ThinkingMode
        };

        return new ResolvedSceneBeatAnalyzer(
            functionDefault.Id,
            model.Id,
            provider.Id,
            resolvedModel,
            model.StructuredOutputMode,
            model.MaximumContextTokens,
            model.MaximumOutputTokens,
            functionDefault.MaxConcurrentJobs!.Value,
            functionDefault.DurableJobLeaseSeconds!.Value,
            functionDefault.DurableJobPollIntervalMilliseconds!.Value,
            functionDefault.GetSceneBeatAnalyzerRetryDelaysSeconds(),
            functionDefault.DiagnosticsRetentionDays!.Value,
            functionDefault.MaximumCatalogueEntries!.Value);
    }
}