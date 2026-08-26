using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelResolutionService : IModelResolutionService, IMultimodalModelResolutionService
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
            IsSessionOverride: !string.IsNullOrEmpty(sessionOverrideId),
            ImageProtocol: provider.ImageProtocol,
            ComfyUiUrl: provider.ImageProtocol == ImageProtocol.ComfyUi ? provider.BaseUrl : null);
    }

    public async Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveImageModelAsync(sessionOverrideId, cancellationToken);

        var funcDefault = await _functionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySceneImage, cancellationToken);
        var model = funcDefault is null ? null : await _modelRepository.GetByIdAsync(funcDefault.ModelId, cancellationToken);
        if (model is null)
        {
            throw new ModelResolutionException(
                $"No identity model configured for function '{AppFunction.RolePlaySceneImage}'. Configure its identity mechanism in Model Manager (/model-manager).");
        }

        if (!Enum.TryParse<SceneImageIdentityMechanism>(model.IdentityMechanism, ignoreCase: true, out var mechanism)
            || mechanism == SceneImageIdentityMechanism.Unknown)
        {
            throw new ModelResolutionException(
                $"Identity mechanism not configured or unknown for model '{model.DisplayName}' (IdentityMechanism='{model.IdentityMechanism}'). " +
                "Set it to IpAdapter or PuLid in Model Manager (/model-manager).");
        }

        if (model.IdentityStrength is not { } strength || strength <= 0)
        {
            throw new ModelResolutionException(
                $"Identity strength not configured for model '{model.DisplayName}'. Set a positive IdentityStrength in Model Manager (/model-manager).");
        }

        if (string.IsNullOrWhiteSpace(model.IdentityAdapterRef))
        {
            throw new ModelResolutionException(
                $"Identity adapter reference not configured for model '{model.DisplayName}'. Set IdentityAdapterRef in Model Manager (/model-manager).");
        }

        _logger.LogInformation(
            "Identity image model resolved: Model={ModelIdentifier}, Mechanism={Mechanism}, Strength={Strength}, Adapter={Adapter}",
            resolved.ModelIdentifier,
            mechanism,
            strength,
            model.IdentityAdapterRef);

        return new ResolvedIdentityImageModel(
            ProviderBaseUrl: resolved.ProviderBaseUrl,
            ProviderTimeoutSeconds: resolved.ProviderTimeoutSeconds,
            ModelIdentifier: resolved.ModelIdentifier,
            ContentPolicy: resolved.ContentPolicy,
            ProviderName: resolved.ProviderName,
            Mechanism: mechanism,
            AdapterRef: model.IdentityAdapterRef.Trim(),
            ClipVisionRef: string.IsNullOrWhiteSpace(model.IdentityClipVisionRef) ? null : model.IdentityClipVisionRef.Trim(),
            IdentityStrength: strength);
    }

    public async Task<ResolvedMultimodalModel> ResolveAsync(
        AppFunction function,
        CancellationToken cancellationToken = default)
    {
        if (function is not AppFunction.RolePlaySceneImageEditPromptCompiler
            and not AppFunction.RolePlaySceneImageValidator)
        {
            throw new ModelResolutionException($"Function '{function}' is not a multimodal scene-image function.");
        }

        var functionDefault = await _functionDefaultRepository.GetByFunctionAsync(function, cancellationToken)
            ?? throw new ModelResolutionException($"No model configured for function '{function}'. Configure it in Model Manager (/model-manager).");
        var model = await _modelRepository.GetByIdAsync(functionDefault.ModelId, cancellationToken);
        if (model is null || !model.IsEnabled)
            throw new ModelResolutionException($"The configured model for function '{function}' is unavailable.");
        if (!model.SupportsImageInput)
            throw new ModelResolutionException($"Model '{model.DisplayName}' does not have image-input capability enabled.");

        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
            throw new ModelResolutionException($"The configured provider for function '{function}' is unavailable.");

        var providerBaseUrl = Require(provider.BaseUrl, "provider base endpoint", function);
        if (!Uri.TryCreate(providerBaseUrl, UriKind.Absolute, out var providerUri)
            || providerUri.Scheme is not ("http" or "https"))
        {
            throw new ModelResolutionException($"Function '{function}' has an invalid provider base endpoint.");
        }
        var chatCompletionsPath = RequirePath(provider.ChatCompletionsPath, "chat completions path", function);
        var readinessPath = RequirePath(provider.ReadinessPath, "readiness path", function);
        Require(provider.ReadinessSuccessContractJson, "readiness success contract", function);
        Require(provider.LifecycleStrategyIdentifier, "lifecycle strategy", function);
        Require(provider.CredentialReference, "credential reference", function);
        Require(provider.ApiKeyEncrypted, "inference credential", function);
        if (!Enum.TryParse<ModelLifecycleStrategy>(provider.LifecycleStrategyIdentifier, out var lifecycleStrategy)
            || lifecycleStrategy == ModelLifecycleStrategy.Unknown)
        {
            throw new ModelResolutionException($"Function '{function}' has invalid lifecycle strategy '{provider.LifecycleStrategyIdentifier}'.");
        }
        if (provider.ContentPolicy == ImageContentPolicy.Unknown)
            throw new ModelResolutionException($"Function '{function}' requires an explicit image content policy.");
        if (functionDefault.Temperature < 0)
            throw new ModelResolutionException($"Function '{function}' requires a non-negative configured temperature.");
        if (functionDefault.TopP is <= 0 or > 1)
            throw new ModelResolutionException($"Function '{function}' requires configured top-p in the range (0, 1].");

        var acceptedMediaTypes = RequireMediaTypes(model.AcceptedInputMediaTypes, function);
        return new ResolvedMultimodalModel(
            provider.Id,
            model.Id,
            providerBaseUrl,
            chatCompletionsPath,
            readinessPath,
            provider.ReadinessSuccessContractJson!,
            RequirePositive(provider.TimeoutSeconds, "request timeout", function),
            RequirePositive(provider.TransitionTimeoutSeconds, "transition timeout", function),
            RequirePositive(provider.TransitionMarginSeconds, "transition margin", function),
            provider.CredentialReference!,
            provider.ApiKeyEncrypted,
            Require(model.ModelIdentifier, "model identifier", function),
            Require(provider.Name, "provider name", function),
            provider.ContentPolicy,
            lifecycleStrategy,
            RequirePositive(model.MaximumInputImages, "maximum input images", function),
            RequirePositive(model.MaximumInputImageBytes, "maximum input image bytes", function),
            RequirePositive(model.MaximumInputImagePixels, "maximum input image pixels", function),
            RequirePositive(model.MaximumInputImageDimension, "maximum input image dimension", function),
            acceptedMediaTypes,
            RequirePositive(model.MaximumResponseBytes, "maximum response bytes", function),
            RequirePositive(provider.MaximumActiveRequests, "maximum active requests", function),
            RequirePositive(provider.QueueCapacity, "queue capacity", function),
            functionDefault.Temperature,
            functionDefault.TopP,
            RequirePositive(functionDefault.MaxTokens, "maximum output tokens", function),
            model.RuntimeRevision,
            model.ArtifactRevision);
    }

    private static IReadOnlySet<string> RequireMediaTypes(string? configured, AppFunction function)
    {
        Require(configured, "accepted input media types", function);
        var values = configured!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (values.Count == 0 || values.Any(value => !value.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
            throw new ModelResolutionException($"Function '{function}' has invalid accepted input media types.");
        return values;
    }

    private static string Require(string? value, string name, AppFunction function) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ModelResolutionException($"Function '{function}' requires configured {name}.");

    private static string RequirePath(string? value, string name, AppFunction function)
    {
        var path = Require(value, name, function);
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ModelResolutionException($"Function '{function}' has invalid configured {name}.");
        return path;
    }

    private static int RequirePositive(int? value, string name, AppFunction function) =>
        value is > 0 ? value.Value : throw new ModelResolutionException($"Function '{function}' requires positive configured {name}.");

    private static long RequirePositive(long? value, string name, AppFunction function) =>
        value is > 0 ? value.Value : throw new ModelResolutionException($"Function '{function}' requires positive configured {name}.");
}
