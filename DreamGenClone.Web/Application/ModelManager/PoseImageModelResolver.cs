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

        // Which families a GRAPH exists for — NOT which families are permitted. Permission comes from the declared
        // strategy and its passing qualification below; this is the narrower, factual question of whether
        // ComfyUIPoseConditionedImageClient can build a graph for the family, and it has to stay in step with that
        // client, because a family that passes here with no graph fails at RENDER time instead of before queueing.
        //
        // SDXL (thibaud OpenPoseXL2) and FLUX (XLabs OpenPose) are both proven. FLUX is not SDXL with a different
        // checkpoint: it is a separate graph. Pony is SDXL-based, but no Pony pose render has been proven, so it stays
        // refused rather than silently inheriting the SDXL graph — and a refusal here names the remedy.
        if (model.SceneImageModelFamily is not (SceneImageModelFamily.Sdxl or SceneImageModelFamily.Flux))
        {
            throw new ModelResolutionException(
                $"No pose-conditioned (OpenPose) graph exists for model '{model.DisplayName}' (family '{model.SceneImageModelFamily}'). "
                + "The proven graphs are SDXL (thibaud OpenPoseXL2) and FLUX (XLabs OpenPose). Enable one of those, or add the "
                + "graph before declaring the strategy.");
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

        var flux = model.SceneImageModelFamily == SceneImageModelFamily.Flux
            ? ResolveFluxRefs(model.DisplayName, qualification)
            : null;

        _logger.LogInformation(
            "Pose image model resolved: Model={ModelIdentifier}, Family={Family}, Provider={ProviderName}, ControlNet={Adapter}, DefaultStrength={Strength}",
            model.ModelIdentifier, model.SceneImageModelFamily, provider.Name, qualification.AdapterRef, strength);

        return new ResolvedPoseImageModel(
            ProviderBaseUrl: provider.BaseUrl,
            ProviderTimeoutSeconds: provider.TimeoutSeconds,
            ModelIdentifier: model.ModelIdentifier,
            ContentPolicy: provider.ContentPolicy,
            ProviderName: provider.Name,
            ControlNetAdapterRef: qualification.AdapterRef.Trim(),
            DefaultStrength: strength,
            ImageProtocol: ImageProtocol.ComfyUi,
            Family: model.SceneImageModelFamily,
            Flux: flux);
    }

    /// <summary>
    /// The FLUX graph's own references, every one of them required and every one of them configured in this model's
    /// 'PoseControlNet' qualification. Nothing here is defaulted: the graph cannot run without these files, and a
    /// substituted text encoder or VAE would render a silently different picture rather than report a problem.
    /// </summary>
    private static FluxPoseRefs ResolveFluxRefs(string displayName, CapabilityQualification qualification)
    {
        string Require(string? value, string field) => string.IsNullOrWhiteSpace(value)
            ? throw new ModelResolutionException(
                $"Model '{displayName}' is FLUX and declares '{ControlNetStrategy}', but its qualification is missing "
                + $"'{field}'. Add it to CapabilityQualificationsJson in Model Manager (/model-manager): the FLUX OpenPose "
                + "graph loads a UNET, two text encoders and a VAE, and cannot run without each one.")
            : value.Trim();

        if (qualification.Guidance is not { } guidance || guidance <= 0)
        {
            throw new ModelResolutionException(
                $"FLUX pose guidance must be a positive number, but model '{displayName}' has "
                + $"{qualification.Guidance?.ToString() ?? "none"} in its '{ControlNetStrategy}' qualification. Set 'Guidance' in Model Manager.");
        }

        if (qualification.Steps is not { } steps || steps <= 0)
        {
            throw new ModelResolutionException(
                $"FLUX pose steps must be positive, but model '{displayName}' has "
                + $"{qualification.Steps?.ToString() ?? "none"} in its '{ControlNetStrategy}' qualification. Set 'Steps' in Model Manager.");
        }

        if (qualification.TimestepToStartCfg is not { } timestep || timestep < 0)
        {
            throw new ModelResolutionException(
                $"FLUX pose 'TimestepToStartCfg' must be zero or positive, but model '{displayName}' has "
                + $"{qualification.TimestepToStartCfg?.ToString() ?? "none"} in its '{ControlNetStrategy}' qualification. Set it in Model Manager.");
        }

        return new FluxPoseRefs(
            UnetName: Require(qualification.UnetName, "UnetName"),
            ClipName1: Require(qualification.ClipName1, "ClipName1"),
            ClipName2: Require(qualification.ClipName2, "ClipName2"),
            VaeName: Require(qualification.VaeName, "VaeName"),
            ControlNetModelName: Require(qualification.ControlNetModelName, "ControlNetModelName"),
            Guidance: guidance,
            Steps: steps,
            TimestepToStartCfg: timestep);
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

        // ── FLUX only (the XLabs OpenPose graph). Every one is required when the model's family is Flux, and none is
        //    defaulted, because the graph loads each file by name from the ComfyUI host.

        /// <summary>UNET name under models/diffusion_models: the local host serves FLUX as a UNET, not a checkpoint.</summary>
        public string? UnetName { get; set; }

        /// <summary>First DualCLIPLoader text encoder (t5xxl).</summary>
        public string? ClipName1 { get; set; }

        /// <summary>Second DualCLIPLoader text encoder (clip_l).</summary>
        public string? ClipName2 { get; set; }

        /// <summary>VAE name under models/vae (ae.safetensors).</summary>
        public string? VaeName { get; set; }

        /// <summary>LoadFluxControlNet.model_name — which FLUX variant the ControlNet was built for.</summary>
        public string? ControlNetModelName { get; set; }

        /// <summary>FluxGuidance / XlabsSampler.true_gs.</summary>
        public double? Guidance { get; set; }

        /// <summary>Sampler steps.</summary>
        public int? Steps { get; set; }

        /// <summary>XlabsSampler.timestep_to_start_cfg.</summary>
        public int? TimestepToStartCfg { get; set; }
    }
}
