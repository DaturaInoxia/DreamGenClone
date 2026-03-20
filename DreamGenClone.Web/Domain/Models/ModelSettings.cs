namespace DreamGenClone.Web.Domain.Models;

/// <summary>
/// Per-session model configuration for LM Studio generation.
/// </summary>
public sealed class ModelSettings
{
    /// <summary>
    /// LM Studio model name (e.g., "utena-7b-nsfw-v2-i1").
    /// </summary>
    public string Model { get; set; } = "utena-7b-nsfw-v2-i1";

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
    /// Creates a shallow copy of this model settings.
    /// </summary>
    public ModelSettings Clone()
    {
        return new ModelSettings
        {
            Model = Model,
            Temperature = Temperature,
            TopP = TopP,
            MaxTokens = MaxTokens
        };
    }
}
