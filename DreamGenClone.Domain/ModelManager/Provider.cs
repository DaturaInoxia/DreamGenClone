namespace DreamGenClone.Domain.ModelManager;

public sealed class Provider
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";
    public ImageProviderCapability ImageCapability { get; set; } = ImageProviderCapability.None;
    public string ImageGenerationPath { get; set; } = "/v1/images/generations";
    public ImageContentPolicy ContentPolicy { get; set; } = ImageContentPolicy.Unknown;
    public ImageProtocol ImageProtocol { get; set; } = ImageProtocol.OpenAiImages;
    public int TimeoutSeconds { get; set; } = 120;
    public string? LifecycleStrategyIdentifier { get; set; }
    public string? ReadinessPath { get; set; }
    public string? ReadinessSuccessContractJson { get; set; }
    public int? TransitionTimeoutSeconds { get; set; }
    public int? TransitionMarginSeconds { get; set; }
    public string? ShutdownDrainPolicyJson { get; set; }
    public int? MaximumActiveRequests { get; set; }
    public int? QueueCapacity { get; set; }
    public string? CredentialReference { get; set; }
    public string? ServerIdentityPolicyJson { get; set; }
    public string? AllowedNetworkBoundary { get; set; }
    public string? ApiKeyEncrypted { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this is THE default provider. At most one provider carries this flag: the repository clears
    /// the flag from every other row when a provider is saved with it set, so the value is single-valued
    /// rather than a convention several call sites have to agree on. The model pickers order this provider's
    /// group first.
    /// </summary>
    public bool IsDefault { get; set; }

    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Free-text notes about this provider (e.g., pricing tier, rate limits, special capabilities).</summary>
    public string? Notes { get; set; }
}
