using System.Text.Json;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

public enum ReferenceStrategyResolutionStatus
{
    Possible,
    Unqualified,
    Impossible
}

public sealed record ReferenceStrategyResolution(
    ReferenceStrategyResolutionStatus Status,
    string Strategy,
    string Reason)
{
    public bool IsAvailable => Status == ReferenceStrategyResolutionStatus.Possible;
}

public interface IReferenceStrategyResolver
{
    Task<ReferenceStrategyResolution> ResolveAsync(
        string modelId,
        string strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How this model carries IDENTITY, through the ONE decision every caller shares (a configured
    /// IP-Adapter/PuLID mechanism first, then the model's own reference slots). Callers ask this instead of
    /// resolving a hardcoded strategy name, so a model that carries identity natively is offered rather than
    /// reported unavailable.
    /// </summary>
    Task<ReferenceStrategyResolution> ResolveIdentityAsync(
        string modelId,
        CancellationToken cancellationToken = default)
        => ReferenceStrategyResolver.ResolveIdentityAsync(this, modelId, cancellationToken);

    /// <summary>
    /// How this model carries a POSE, through the same ONE decision (a qualified OpenPose ControlNet graph
    /// first, then the model's own reference slots).
    /// </summary>
    Task<ReferenceStrategyResolution> ResolvePoseAsync(
        string modelId,
        CancellationToken cancellationToken = default)
        => ReferenceStrategyResolver.ResolvePoseAsync(this, modelId, cancellationToken);
}

public sealed class ReferenceStrategyResolver : IReferenceStrategyResolver
{
    private static readonly HashSet<string> GraphStrategies = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReferenceConditioning",
        "NativeMultiReference",
        "Lora",
        "ControlNet",
        "PoseControlNet",
        "WardrobeTryOn"
    };

    /// <summary>A configured identity mechanism applied to the sampler's model input (IP-Adapter, PuLID).</summary>
    public const string IdentityReferenceConditioning = "ReferenceConditioning";

    /// <summary>The model's OWN reference-image slots: the approved face is sent alongside the prompt in one call.</summary>
    public const string IdentityNativeMultiReference = "NativeMultiReference";

    /// <summary>A configured ControlNet graph that applies a pose skeleton to the sampler's conditioning.</summary>
    public const string PoseControlNet = "PoseControlNet";

    /// <summary>
    /// How a model carries IDENTITY, decided in ONE place so the picker's answer and the render's answer cannot
    /// differ. Preference order: the configured IP-Adapter/PuLID graph first (a dedicated mechanism), then the model's
    /// own native reference slots (Qwen-Image-2.1, where a face is a reference image rather than an applied
    /// mechanism). Both are asked through the SAME declaration + qualification contract the render path uses, so
    /// "offered" can never exceed "executable".
    ///
    /// When neither is available the more ACTIONABLE reason wins: Unqualified (declare it, or add a proof) beats
    /// Impossible (this endpoint can never do it), because only the first tells the operator what to do. When both
    /// share a status the answer is the PRIMARY mechanism's, so the same model always yields the same explanation
    /// rather than one that depends on which mechanism is declared less badly.
    /// </summary>
    public static async Task<ReferenceStrategyResolution> ResolveIdentityAsync(
        IReferenceStrategyResolver resolver,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var conditioning = await resolver.ResolveAsync(modelId, IdentityReferenceConditioning, cancellationToken);
        var native = await resolver.ResolveAsync(modelId, IdentityNativeMultiReference, cancellationToken);
        return Prefer(conditioning, native);
    }

    /// <summary>
    /// The same identity decision for callers that already hold the model and provider rows (a listing, for
    /// example, which must not take a second repository round trip per model). Both entry points share
    /// <see cref="Prefer"/>, so the picker's answer and the render's answer cannot drift apart.
    /// </summary>
    public static ReferenceStrategyResolution ResolveIdentity(RegisteredModel model, Provider provider)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(provider);
        return Prefer(
            Resolve(model, provider, IdentityReferenceConditioning),
            Resolve(model, provider, IdentityNativeMultiReference));
    }

    /// <summary>
    /// How a model carries a POSE, decided in the same ONE place as identity and through the same declaration +
    /// qualification contract, so a caller can never take a pose route the model does not actually qualify.
    /// Preference order: the configured OpenPose ControlNet graph first (a dedicated conditioning mechanism), then
    /// the model's own reference slots — measured 2026-09-23: Qwen-Image-2.1 reads an OpenPose skeleton placed in a
    /// reference slot as pose guidance, so a model with native references can carry a pose without any ControlNet.
    /// </summary>
    public static async Task<ReferenceStrategyResolution> ResolvePoseAsync(
        IReferenceStrategyResolver resolver,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var controlNet = await resolver.ResolveAsync(modelId, PoseControlNet, cancellationToken);
        var native = await resolver.ResolveAsync(modelId, IdentityNativeMultiReference, cancellationToken);
        return Prefer(controlNet, native);
    }

    /// <summary>The same pose decision for callers that already hold the model and provider rows.</summary>
    public static ReferenceStrategyResolution ResolvePose(RegisteredModel model, Provider provider)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(provider);
        return Prefer(
            Resolve(model, provider, PoseControlNet),
            Resolve(model, provider, IdentityNativeMultiReference));
    }

    /// <summary>
    /// Every visual strategy this model can ACTUALLY execute right now - <c>TextOnly</c> first, then each graph
    /// strategy this endpoint declares AND qualifies. Listings and pickers offer exactly this set, so a strategy a
    /// surface offers is one the render accepts, and one it omits (a native-reference model's
    /// <c>NativeMultiReference</c>, for instance) is not silently unavailable to the operator. Asked through
    /// <see cref="Resolve"/> so the offered set and the executed set come from the same decision.
    /// </summary>
    public static IReadOnlyList<string> ListAvailableStrategies(RegisteredModel model, Provider provider)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(provider);

        // Ordered, not alphabetical: the text-only path is the baseline every endpoint has, and the graph strategies
        // follow in their declared order so the picker lists a model's real capabilities deterministically.
        var available = new List<string> { "TextOnly" };
        available.AddRange(DeclaredStrategies(model)
            .Where(strategy => GraphStrategies.Contains(strategy))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(strategy => Resolve(model, provider, strategy).IsAvailable));
        return available;
    }

    private static IReadOnlyList<string> DeclaredStrategies(RegisteredModel model)
    {
        try
        {
            return ParseStringArray(model.SupportedVisualStrategiesJson, nameof(model.SupportedVisualStrategiesJson));
        }
        catch (InvalidOperationException)
        {
            // A malformed declaration explains itself at Resolve time, where the operator sees the reason; a listing
            // must not throw for one bad row, so an unreadable declaration simply contributes no graph strategies.
            return [];
        }
    }

    /// <summary>
    /// Chooses between a model's PRIMARY mechanism for a behaviour and its native reference slots. Shared by the
    /// identity and pose decisions so both explain themselves the same way.
    /// </summary>
    private static ReferenceStrategyResolution Prefer(
        ReferenceStrategyResolution conditioning,
        ReferenceStrategyResolution native)
    {
        if (conditioning.IsAvailable)
        {
            return conditioning;
        }

        if (native.IsAvailable)
        {
            return native;
        }

        if (conditioning.Status == native.Status)
        {
            return conditioning;
        }

        return conditioning.Status == ReferenceStrategyResolutionStatus.Impossible ? native : conditioning;
    }

    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IProviderRepository _providerRepository;

    public ReferenceStrategyResolver(
        IRegisteredModelRepository modelRepository,
        IProviderRepository providerRepository)
    {
        _modelRepository = modelRepository;
        _providerRepository = providerRepository;
    }

    public async Task<ReferenceStrategyResolution> ResolveAsync(
        string modelId,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model id is required.", nameof(modelId));
        if (string.IsNullOrWhiteSpace(strategy))
            throw new ArgumentException("A reference strategy is required.", nameof(strategy));

        var model = await _modelRepository.GetByIdAsync(modelId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Model '{modelId}' is not registered in Model Manager.");
        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken)
            ?? throw new InvalidOperationException($"Provider '{model.ProviderId}' for model '{model.Id}' is not registered in Model Manager.");

        return Resolve(model, provider, strategy.Trim());
    }

    public static ReferenceStrategyResolution Resolve(
        RegisteredModel model,
        Provider provider,
        string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            throw new ArgumentException("A reference strategy is required.", nameof(strategy));

        var normalizedStrategy = strategy.Trim();
        if (normalizedStrategy.Equals("TextOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new(ReferenceStrategyResolutionStatus.Possible, "TextOnly", "Text-only rendering is available on every configured image endpoint.");
        }

        if (!GraphStrategies.Contains(normalizedStrategy))
        {
            return new(ReferenceStrategyResolutionStatus.Impossible, normalizedStrategy, "The requested visual strategy is not supported by the capability contract.");
        }

        var executionClass = provider.ImageProtocol switch
        {
            ImageProtocol.OpenAiImages => ProviderExecutionClass.HostedApi,
            ImageProtocol.ComfyUi => ProviderExecutionClass.DedicatedPod,
            ImageProtocol.ComfyUiServerless => ProviderExecutionClass.SelfBuiltServerless,
            _ => throw new InvalidOperationException($"Provider '{provider.Id}' has an unsupported image protocol '{provider.ImageProtocol}'.")
        };

        if (executionClass == ProviderExecutionClass.HostedApi)
        {
            return new(
                ReferenceStrategyResolutionStatus.Impossible,
                normalizedStrategy,
                "Hosted image APIs cannot execute graph strategies such as IP-Adapter, PuLID, LoRA, ControlNet, or wardrobe try-on.");
        }

        var declaredStrategies = ParseStringArray(model.SupportedVisualStrategiesJson, nameof(model.SupportedVisualStrategiesJson));
        if (!declaredStrategies.Contains(normalizedStrategy, StringComparer.OrdinalIgnoreCase))
        {
            return new(
                ReferenceStrategyResolutionStatus.Unqualified,
                normalizedStrategy,
                $"Model '{model.DisplayName}' does not declare support for '{normalizedStrategy}' in Model Manager.");
        }

        var qualifications = ParseQualifications(model.CapabilityQualificationsJson);
        var qualification = qualifications.FirstOrDefault(entry =>
            entry.Strategy.Equals(normalizedStrategy, StringComparison.OrdinalIgnoreCase)
            && entry.EndpointId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase));

        if (qualification is null || !qualification.Qualified || string.IsNullOrWhiteSpace(qualification.ProofId))
        {
            return new(
                ReferenceStrategyResolutionStatus.Unqualified,
                normalizedStrategy,
                $"No passing qualification proof exists for '{normalizedStrategy}' on endpoint '{provider.Id}'.");
        }

        return new(
            ReferenceStrategyResolutionStatus.Possible,
            normalizedStrategy,
            $"'{normalizedStrategy}' is declared and qualified for model '{model.DisplayName}' on endpoint '{provider.Id}'.");
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

    private static readonly JsonSerializerOptions QualificationJsonOptions = new(JsonSerializerDefaults.Web);

    private static CapabilityQualification[] ParseQualifications(string json)
    {
        try
        {
            // Case-insensitive property binding so qualification entries authored as PascalCase
            // (Model Details editor / SQL) or camelCase (unit fixtures) both bind to the CLR shape.
            return JsonSerializer.Deserialize<CapabilityQualification[]>(json, QualificationJsonOptions)
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
    }
}