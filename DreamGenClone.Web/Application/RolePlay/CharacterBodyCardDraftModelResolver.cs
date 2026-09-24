using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves the body-card draft function (B-122) from Model Manager. Every check that can be made before a network
/// call is made here, and each refusal names what to configure — the studio surfaces the message verbatim, so an
/// unconfigured function reads as an instruction rather than as an empty draft.
/// </summary>
public sealed class CharacterBodyCardDraftModelResolver : ICharacterBodyCardDraftModelResolver
{
    private const AppFunction Function = AppFunction.RolePlayCharacterBodyCardDraft;

    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;

    public CharacterBodyCardDraftModelResolver(
        IFunctionDefaultRepository functionDefaultRepository,
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository)
    {
        _functionDefaultRepository = functionDefaultRepository;
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
    }

    public async Task<ResolvedStructuredTextFunction> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var functionDefault = await _functionDefaultRepository.GetByFunctionAsync(Function, cancellationToken)
            ?? throw new ModelResolutionException(
                $"No model configured for function '{Function}'. Configure it in Model Manager (/model-manager).");

        var configurationError = functionDefault.ValidateCharacterBodyCardDraftConfiguration();
        if (configurationError is not null)
            throw new ModelResolutionException($"Function '{Function}' configuration is invalid: {configurationError}");

        var model = await _modelRepository.GetByIdAsync(functionDefault.ModelId, cancellationToken);
        if (model is null || !model.IsEnabled)
            throw new ModelResolutionException(
                $"The model assigned to function '{Function}' is unavailable. Choose an enabled text model in "
                + "Model Manager (/model-manager).");
        if (model.ModelKind != ModelKind.Text)
            throw new ModelResolutionException(
                $"Function '{Function}' requires a text model, but '{model.DisplayName}' is a {model.ModelKind} model.");
        if (model.StructuredOutputMode is not (StructuredOutputMode.StrictJsonSchema or StructuredOutputMode.JsonObject))
            throw new ModelResolutionException(
                $"Model '{model.DisplayName}' requires an explicit supported structured-output mode, because the body "
                + "card draft is returned as JSON.");
        // The declared output capability is OPTIONAL on a registered model (the scene-beat resolver treats it the
        // same way), so it is only enforced when the model actually declares one.
        if (model.MaximumOutputTokens.HasValue && functionDefault.MaxTokens > model.MaximumOutputTokens.Value)
            throw new ModelResolutionException(
                $"Function '{Function}' Max Tokens exceeds model '{model.DisplayName}' maximum output capability.");        if (functionDefault.ThinkingMode == ThinkingMode.Default)
            throw new ModelResolutionException(
                $"Function '{Function}' requires an explicit thinking mode (Enabled or Disabled).");
        if (functionDefault.ThinkingMode == ThinkingMode.Enabled && !model.SupportsThinkingControl)
            throw new ModelResolutionException(
                $"Function '{Function}' configures thinking control, but model '{model.DisplayName}' does not "
                + "support it.");

        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
            throw new ModelResolutionException(
                $"The provider for function '{Function}' is unavailable or disabled ({model.ProviderId}).");
        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var providerUri)
            || providerUri.Scheme is not ("http" or "https"))
            throw new ModelResolutionException($"Function '{Function}' provider base URL is invalid.");
        if (string.IsNullOrWhiteSpace(provider.ChatCompletionsPath)
            || !provider.ChatCompletionsPath.StartsWith("/", StringComparison.Ordinal))
            throw new ModelResolutionException($"Function '{Function}' provider chat completions path is invalid.");
        if (provider.TimeoutSeconds < 1)
            throw new ModelResolutionException($"Function '{Function}' provider timeout must be positive.");

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

        return new ResolvedStructuredTextFunction(
            Function,
            model.Id,
            provider.Id,
            resolvedModel,
            model.StructuredOutputMode);
    }
}
