using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>Single configuration-resolution path for the Qwen source-image editor.</summary>
public sealed class ImageEditorModelResolver : IImageEditorModelResolver
{
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;
    private readonly IModelManagerSecretProvider _secretProvider;
    private readonly IApiKeyEncryptionService _encryptionService;

    public ImageEditorModelResolver(
        IFunctionDefaultRepository functionDefaultRepository,
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository,
        IModelManagerSecretProvider secretProvider,
        IApiKeyEncryptionService encryptionService)
    {
        _functionDefaultRepository = functionDefaultRepository;
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
        _secretProvider = secretProvider;
        _encryptionService = encryptionService;
    }

    public async Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var functionDefault = await _functionDefaultRepository.GetByFunctionAsync(
            AppFunction.RolePlaySceneImageEditor, cancellationToken)
            ?? throw new ModelResolutionException(
                $"No image editor model configured for function '{AppFunction.RolePlaySceneImageEditor}'. Configure an image model and its Qwen editor workflow in Model Manager (/model-manager).");

        var model = await _modelRepository.GetByIdAsync(functionDefault.ModelId, cancellationToken);
        if (model is null || !model.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The default image editor model for function '{AppFunction.RolePlaySceneImageEditor}' is no longer available. Update the model assignment in Model Manager (/model-manager).");
        }

        if (model.ModelKind != ModelKind.Image)
        {
            throw new ModelResolutionException(
                $"Model '{model.DisplayName}' is not an image model (ModelKind={model.ModelKind}). Assign an image-kind model to '{AppFunction.RolePlaySceneImageEditor}' in Model Manager (/model-manager).");
        }

        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
        {
            throw new ModelResolutionException(
                $"The provider for function '{AppFunction.RolePlaySceneImageEditor}' default model is disabled. Enable the provider in Model Manager (/model-manager).");
        }

        if (provider.ImageCapability == ImageProviderCapability.None)
        {
            throw new ModelResolutionException(
                $"Provider '{provider.Name}' is not image-capable (ImageCapability=None). Set its image capability in Model Manager (/model-manager).");
        }

        if (provider.ImageProtocol is not (ImageProtocol.ComfyUi or ImageProtocol.ComfyUiServerless))
        {
            throw new ModelResolutionException(
                $"Image editor provider '{provider.Name}' must use the ComfyUI or RunPod Serverless image protocol. Set Image Protocol to ComfyUI or ComfyUI Serverless in Model Manager (/model-manager).");
        }

        if (provider.ContentPolicy == ImageContentPolicy.Unknown)
        {
            throw new ModelResolutionException(
                $"Image content policy not configured for image editor provider '{provider.Name}'. Set its content policy in Model Manager (/model-manager).");
        }

        // Serverless image editors need the RunPod API key. Prefer the DB-encrypted key; if the
        // provider has none, fall back to the git-ignored ModelManagerSecrets (by CredentialReference,
        // then provider name, then the default "RunPod" key) and encrypt it for the client path —
        // mirroring ModelResolutionService for generation/identity. No fallback default is invented;
        // if no key is configured anywhere, the serverless client fails fast with the RunPod 401.
        var apiKeyEncrypted = provider.ApiKeyEncrypted;
        if (string.IsNullOrEmpty(apiKeyEncrypted) && provider.ImageProtocol == ImageProtocol.ComfyUiServerless)
        {
            var secret = _secretProvider.Resolve(provider.CredentialReference)
                         ?? _secretProvider.Resolve(provider.Name)
                         ?? _secretProvider.Resolve("RunPod");
            if (!string.IsNullOrEmpty(secret))
            {
                apiKeyEncrypted = _encryptionService.Encrypt(secret);
            }
        }

        return new ResolvedImageEditorModel(
            ComfyUiUrl: provider.BaseUrl,
            ProviderTimeoutSeconds: provider.TimeoutSeconds,
            ApiKeyEncrypted: apiKeyEncrypted,
            ModelIdentifier: model.ModelIdentifier,
            ProviderName: provider.Name,
            ContentPolicy: provider.ContentPolicy,
            DiffusionModel: RequiredText(model.ImageEditorDiffusionModel, "diffusion model", model),
            TextEncoder: RequiredText(model.ImageEditorTextEncoder, "text encoder", model),
            Vae: RequiredText(model.ImageEditorVae, "VAE", model),
            Steps: RequiredPositive(model.ImageEditorSteps, "steps", model),
            Cfg: RequiredNonNegative(model.ImageEditorCfg, "CFG", model),
            Sampler: RequiredText(model.ImageEditorSampler, "sampler", model),
            Scheduler: RequiredText(model.ImageEditorScheduler, "scheduler", model),
            Denoise: RequiredNonNegative(model.ImageEditorDenoise, "denoise", model),
            AuraFlowShift: RequiredNonNegative(model.ImageEditorAuraFlowShift, "AuraFlow shift", model),
            CfgNormStrength: RequiredNonNegative(model.ImageEditorCfgNormStrength, "CFGNorm strength", model),
            ImageProtocol: provider.ImageProtocol);
    }

    private static string RequiredText(string? value, string setting, RegisteredModel model) =>
        string.IsNullOrWhiteSpace(value)
            ? throw MissingSetting(setting, model)
            : value.Trim();

    private static int RequiredPositive(int? value, string setting, RegisteredModel model) =>
        !value.HasValue || value.Value <= 0
            ? throw MissingSetting(setting, model)
            : value.Value;

    private static double RequiredNonNegative(double? value, string setting, RegisteredModel model) =>
        !value.HasValue || value.Value < 0
            ? throw MissingSetting(setting, model)
            : value.Value;

    private static ModelResolutionException MissingSetting(string setting, RegisteredModel model) => new(
        $"Image editor model '{model.DisplayName}' is missing required Qwen editor setting '{setting}'. Configure it in Model Manager (/model-manager).");
}