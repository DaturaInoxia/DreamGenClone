using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelResolutionService : IModelResolutionService
{
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;
    private readonly ILogger<ModelResolutionService> _logger;

    public ModelResolutionService(
        IFunctionDefaultRepository functionDefaultRepository,
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository,
        ILogger<ModelResolutionService> logger)
    {
        _functionDefaultRepository = functionDefaultRepository;
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
        _logger = logger;
    }

    public async Task<ResolvedModel> ResolveAsync(
        AppFunction function,
        string? sessionModelId = null,
        double? sessionTemperature = null,
        double? sessionTopP = null,
        int? sessionMaxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();

        // Session override path
        if (!string.IsNullOrEmpty(sessionModelId))
        {
            var modelLookupStopwatch = Stopwatch.StartNew();
            var sessionModel = await _modelRepository.GetByIdAsync(sessionModelId, cancellationToken);
            modelLookupStopwatch.Stop();
            if (sessionModel is null || !sessionModel.IsEnabled)
            {
                throw new ModelResolutionException(
                    $"Session override model '{sessionModelId}' is not available. Select a different model in the settings panel or clear the override.");
            }

            var providerLookupStopwatch = Stopwatch.StartNew();
            var sessionProvider = await _providerRepository.GetByIdAsync(sessionModel.ProviderId, cancellationToken);
            providerLookupStopwatch.Stop();
            if (sessionProvider is null || !sessionProvider.IsEnabled)
            {
                throw new ModelResolutionException(
                    $"Provider for session override model '{sessionModel.DisplayName}' is disabled. Enable the provider in Model Manager or select a different model.");
            }

            // Use session parameters with fallback to function default parameters
            var functionDefaultLookupStopwatch = Stopwatch.StartNew();
            var functionDefault = await _functionDefaultRepository.GetByFunctionAsync(function, cancellationToken);
            functionDefaultLookupStopwatch.Stop();

            var resolved = new ResolvedModel(
                ProviderBaseUrl: sessionProvider.BaseUrl,
                ChatCompletionsPath: sessionProvider.ChatCompletionsPath,
                ProviderTimeoutSeconds: sessionProvider.TimeoutSeconds,
                ApiKeyEncrypted: sessionProvider.ApiKeyEncrypted,
                ModelIdentifier: sessionModel.ModelIdentifier,
                Temperature: sessionTemperature ?? functionDefault?.Temperature ?? 0.7,
                TopP: sessionTopP ?? functionDefault?.TopP ?? 0.9,
                MaxTokens: sessionMaxTokens ?? functionDefault?.MaxTokens ?? 500,
                ProviderName: sessionProvider.Name,
                IsSessionOverride: true);

            totalStopwatch.Stop();

            _logger.LogInformation(
                "Model resolved via session override: Function={Function}, Model={ModelIdentifier}, Provider={ProviderName}, ModelLookupMs={ModelLookupMs}, ProviderLookupMs={ProviderLookupMs}, FunctionDefaultLookupMs={FunctionDefaultLookupMs}, TotalMs={TotalMs}",
                function,
                resolved.ModelIdentifier,
                resolved.ProviderName,
                modelLookupStopwatch.ElapsedMilliseconds,
                providerLookupStopwatch.ElapsedMilliseconds,
                functionDefaultLookupStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);

            return resolved;
        }

        // Function default path
        var functionDefaultLookupStopwatchDefaultPath = Stopwatch.StartNew();
        var funcDefault = await _functionDefaultRepository.GetByFunctionAsync(function, cancellationToken);
        functionDefaultLookupStopwatchDefaultPath.Stop();
        if (funcDefault is null)
        {
            throw new ModelResolutionException(
                $"No model configured for function '{function}'. Configure a default model in Model Manager (/model-manager).");
        }

        var modelLookupStopwatchDefaultPath = Stopwatch.StartNew();
        var model = await _modelRepository.GetByIdAsync(funcDefault.ModelId, cancellationToken);
        modelLookupStopwatchDefaultPath.Stop();
        if (model is null || !model.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The default model for function '{function}' is no longer available. Update the model assignment in Model Manager (/model-manager).");
        }

        var providerLookupStopwatchDefaultPath = Stopwatch.StartNew();
        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        providerLookupStopwatchDefaultPath.Stop();
        if (provider is null || !provider.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The provider for function '{function}' default model is disabled. Enable the provider in Model Manager (/model-manager).");
        }

        var result = new ResolvedModel(
            ProviderBaseUrl: provider.BaseUrl,
            ChatCompletionsPath: provider.ChatCompletionsPath,
            ProviderTimeoutSeconds: provider.TimeoutSeconds,
            ApiKeyEncrypted: provider.ApiKeyEncrypted,
            ModelIdentifier: model.ModelIdentifier,
            Temperature: funcDefault.Temperature,
            TopP: funcDefault.TopP,
            MaxTokens: funcDefault.MaxTokens,
            ProviderName: provider.Name,
            IsSessionOverride: false);

        totalStopwatch.Stop();

        _logger.LogInformation(
            "Model resolved via function default: Function={Function}, Model={ModelIdentifier}, Provider={ProviderName}, FunctionDefaultLookupMs={FunctionDefaultLookupMs}, ModelLookupMs={ModelLookupMs}, ProviderLookupMs={ProviderLookupMs}, TotalMs={TotalMs}",
            function,
            result.ModelIdentifier,
            result.ProviderName,
            functionDefaultLookupStopwatchDefaultPath.ElapsedMilliseconds,
            modelLookupStopwatchDefaultPath.ElapsedMilliseconds,
            providerLookupStopwatchDefaultPath.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds);

        return result;
    }
}
