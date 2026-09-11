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
}

public sealed class ReferenceStrategyResolver : IReferenceStrategyResolver
{
    private static readonly HashSet<string> GraphStrategies = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReferenceConditioning",
        "NativeMultiReference",
        "Lora",
        "ControlNet",
        "WardrobeTryOn"
    };

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