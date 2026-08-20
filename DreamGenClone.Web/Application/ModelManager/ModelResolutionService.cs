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
            resolved = resolved with
            {
                SupportsThinkingControl = sessionModel.SupportsThinkingControl,
                ThinkingMode = functionDefault?.ThinkingMode ?? ThinkingMode.Default
            };

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
            IsSessionOverride: false) with
        {
            SupportsThinkingControl = model.SupportsThinkingControl,
            ThinkingMode = funcDefault.ThinkingMode
        };

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

    /// <inheritdoc />
    public Task<ResolvedModel> ResolveImagePromptModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default)
    {
        // The pre-processor is a standard text completion model; reuse the existing resolution
        // path. Missing configuration fails fast with the standard "no model configured" error.
        return ResolveAsync(AppFunction.RolePlaySceneImagePreprocessor, sessionOverrideId, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResolvedImageModel> ResolveImageModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var funcDefault = await _functionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySceneImage, cancellationToken);
        if (funcDefault is null)
        {
            throw new ModelResolutionException(
                $"No image model configured for function '{AppFunction.RolePlaySceneImage}'. Add an image model + function default in Model Manager (/model-manager).");
        }

        var model = await _modelRepository.GetByIdAsync(funcDefault.ModelId, cancellationToken);
        if (model is null || !model.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The default image model for function '{AppFunction.RolePlaySceneImage}' is no longer available. Update the model assignment in Model Manager (/model-manager).");
        }

        if (model.ModelKind != ModelKind.Image)
        {
            throw new ModelResolutionException(
                $"Model '{model.DisplayName}' is not an image model (ModelKind={model.ModelKind}). Assign an image-kind model to '{AppFunction.RolePlaySceneImage}' in Model Manager (/model-manager).");
        }

        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The provider for function '{AppFunction.RolePlaySceneImage}' default model is disabled. Enable the provider in Model Manager (/model-manager).");
        }

        if (provider.ImageCapability == ImageProviderCapability.None)
        {
            throw new ModelResolutionException(
                $"Provider '{provider.Name}' is not image-capable (ImageCapability=None). Set its image capability in Model Manager (/model-manager).");
        }

        if (provider.ContentPolicy == ImageContentPolicy.Unknown)
        {
            throw new ModelResolutionException(
                $"Image content policy not configured for provider '{provider.Name}'. Set its content policy (SFW-filtered or adult-allowed) in Model Manager (/model-manager).");
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Image model resolved: Function={Function}, Model={ModelIdentifier}, Provider={ProviderName}, Policy={ContentPolicy}, Capability={ImageCapability}, TotalMs={TotalMs}",
            AppFunction.RolePlaySceneImage,
            model.ModelIdentifier,
            provider.Name,
            provider.ContentPolicy,
            provider.ImageCapability,
            stopwatch.ElapsedMilliseconds);

        return new ResolvedImageModel(
            ProviderBaseUrl: provider.BaseUrl,
            ImageGenerationPath: provider.ImageGenerationPath,
            ProviderTimeoutSeconds: provider.TimeoutSeconds,
            ApiKeyEncrypted: provider.ApiKeyEncrypted,
            ModelIdentifier: model.ModelIdentifier,
            ContentPolicy: provider.ContentPolicy,
            ProviderName: provider.Name,
            IsSessionOverride: !string.IsNullOrEmpty(sessionOverrideId));
    }
}
