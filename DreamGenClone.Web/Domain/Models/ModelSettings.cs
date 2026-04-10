namespace DreamGenClone.Web.Domain.Models;

/// <summary>
/// Per-session model configuration for generation overrides.
/// </summary>
public sealed class ModelSettings
{
    /// <summary>
    /// Registered model ID (GUID string) for session override, or null to use function default.
    /// </summary>
    public string? SessionModelId { get; set; }

    /// <summary>
    /// Temperature for sampling (0.0 = deterministic, 1.0+ = creative).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Top-p nucleus sampling threshold (0.0-1.0).
    /// </summary>
    public double TopP { get; set; } = 0.9;

    /// <summary>
    /// Maximum tokens to generate in a single completion.
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Whether the user has explicitly set an override (any field changed from default).
    /// </summary>
    public bool HasOverride => SessionModelId != null;

    /// <summary>
    /// Creates a shallow copy of this model settings.
    /// </summary>
    public ModelSettings Clone()
    {
        return new ModelSettings
        {
            SessionModelId = SessionModelId,
            Temperature = Temperature,
            TopP = TopP,
            MaxTokens = MaxTokens
        };
    }
}
