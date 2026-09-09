using System.Text.Json;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>
/// Resolves a <see cref="ResolvedPoseImageModel"/> for the pose-conditioned (ControlNet OpenPose)
/// render path. Additive to <see cref="IModelResolutionService"/> so the wide model-resolution
/// contract is untouched. Strict and no-fallback: every field is a configured Model Manager value;
/// missing/unknown configuration fails fast with an explicit diagnostic.
/// </summary>
public interface IPoseImageModelResolver
{
    Task<ResolvedPoseImageModel> ResolveAsync(string modelId, CancellationToken cancellationToken = default);
}

public sealed class PoseImageModelResolver : IPoseImageModelResolver
{
    private static readonly string ControlNetStrategy = "PoseControlNet";

    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;
    private readonly ILogger<PoseImageModelResolver> _logger;

    public PoseImageModelResolver(
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository,
        ILogger<PoseImageModelResolver> logger)
    {
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
        _logger = logger;
    }

    public async Task<ResolvedPoseImageModel> ResolveAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ModelResolutionException("A registered image model id is required to resolve a pose image model.");
        }

        var model = await _modelRepository.GetByIdAsync(modelId, cancellationToken)
            ?? throw new ModelResolutionException($"Pose image model '{modelId}' was not found. Select an enabled image model in the Studio or Model Manager (/model-manager).");
        if (!model.IsEnabled)
        {
            throw new ModelResolutionException(
                $"Pose image model '{model.DisplayName}' is disabled. Enable it in Model Manager (/model-manager).");
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
                $"The provider for model '{model.DisplayName}' is disabled. Enable the provider in Model Manager (/model-manager).");
        }
        if (provider.ImageCapability == ImageProviderCapability.None)
        {
            throw new ModelResolutionException(
                $"Provider '{provider.Name}' is not image-capable (ImageCapability=None). Set its image capability in Model Manager (/model-manager).");
        }
        if (provider.ContentPolicy == ImageContentPolicy.Unknown)
        {
            throw new ModelResolutionException(
                $"Image content policy not configured for provider '{provider.Name}'. Set its content policy in Model Manager (/model-manager).");
        }

        // Pose conditioning runs the OpenPose ControlNet graph — only the local ComfyUI path has the
        // proven graph + weights today. Serverless/hosted cannot execute it; fail fast (no fallback).
        if (provider.ImageProtocol != ImageProtocol.ComfyUi)
        {
            throw new ModelResolutionException(
                $"Pose conditioning (ControlNet) requires the local ComfyUI protocol, but provider '{provider.Name}' uses protocol '{provider.ImageProtocol}'. Configure a Local ComfyUI model for pose renders.");
        }

        // The workflow builder is the SDXL OpenPose graph. Only SDXL-family models are enabled today;
        // Pony/FLUX/API need their own qualified graph before they can be declared.
        if (model.SceneImageModelFamily != SceneImageModelFamily.Sdxl)
        {
            throw new ModelResolutionException(
                $"Pose conditioning (ControlNet OpenPose) is only qualified for SDXL-family models today, but model '{model.DisplayName}' is family '{model.SceneImageModelFamily}'. Enable an SDXL-family local ComfyUI model for pose renders.");
        }

        var declared = ParseStringArray(model.SupportedVisualStrategiesJson, nameof(model.SupportedVisualStrategiesJson))
            .Contains(ControlNetStrategy, StringComparer.OrdinalIgnoreCase);
        if (!declared)
        {
            throw new ModelResolutionException(
                $"Model '{model.DisplayName}' does not declare the '{ControlNetStrategy}' visual strategy in Model Manager. Declare it before requesting a pose render.");
        }

        var qualification = ParseQualifications(model.CapabilityQualificationsJson)
            .FirstOrDefault(entry =>
                entry.Strategy.Equals(ControlNetStrategy, StringComparison.OrdinalIgnoreCase)
                && entry.EndpointId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase));

        if (qualification is null || !qualification.Qualified || string.IsNullOrWhiteSpace(qualification.ProofId))
        {
            throw new ModelResolutionException(
                $"No passing '{ControlNetStrategy}' qualification proof exists for model '{model.DisplayName}' on endpoint '{provider.Id}'. Add one in Model Manager before requesting a pose render.");
        }
        if (string.IsNullOrWhiteSpace(qualification.AdapterRef))
        {
            throw new ModelResolutionException(
                $"ControlNet adapter reference is not configured in the '{ControlNetStrategy}' qualification for model '{model.DisplayName}'. Configure the OpenPose weight path in Model Manager.");
        }
        if (qualification.DefaultStrength is not { } strength || strength <= 0 || strength > 1)
        {
            throw new ModelResolutionException(
                $"ControlNet default strength must be in (0, 1], but model '{model.DisplayName}' has {qualification.DefaultStrength} in its '{ControlNetStrategy}' qualification.");
        }

        _logger.LogInformation(
            "Pose image model resolved: Model={ModelIdentifier}, Provider={ProviderName}, ControlNet={Adapter}, DefaultStrength={Strength}",
            model.ModelIdentifier, provider.Name, qualification.AdapterRef, strength);

        return new ResolvedPoseImageModel(
            ProviderBaseUrl: provider.BaseUrl,
            ProviderTimeoutSeconds: provider.TimeoutSeconds,
            ModelIdentifier: model.ModelIdentifier,
            ContentPolicy: provider.ContentPolicy,
            ProviderName: provider.Name,
            ControlNetAdapterRef: qualification.AdapterRef.Trim(),
            DefaultStrength: strength,
            ImageProtocol: ImageProtocol.ComfyUi);
    }

    private static string[] ParseStringArray(string json, string fieldName)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                ?? throw new InvalidOperationException($"Model Manager field '{fieldName}' must contain a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Model Manager field '{fieldName}' contains invalid JSON.", exception);
        }
    }

    private static CapabilityQualification[] ParseQualifications(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CapabilityQualification[]>(json)
                ?? throw new InvalidOperationException("Model Manager field 'CapabilityQualificationsJson' must contain a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Model Manager field 'CapabilityQualificationsJson' contains invalid JSON.", exception);
        }
    }

    private sealed class CapabilityQualification
    {
        public string Strategy { get; set; } = string.Empty;
        public string EndpointId { get; set; } = string.Empty;
        public bool Qualified { get; set; }
        public string ProofId { get; set; } = string.Empty;

        /// <summary>Configured OpenPose ControlNet weight path (ComfyUI-listed name).</summary>
        public string? AdapterRef { get; set; }

        /// <summary>Configured default conditioning strength (0 &lt; strength &lt;= 1).</summary>
        public double? DefaultStrength { get; set; }
    }
}
