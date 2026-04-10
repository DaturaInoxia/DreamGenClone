namespace DreamGenClone.Domain.ModelManager;

public sealed class RegisteredModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProviderId { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Context window size in tokens (e.g. 4096, 8192, 32768, 131072). 0 = unknown.</summary>
    public int ContextWindowSize { get; set; }

    /// <summary>Quantization level if applicable (e.g., "Q4_K_M", "Q8_0", "FP16", "FP32", ""). Empty = unknown/full-precision.</summary>
    public string Quantization { get; set; } = string.Empty;

    /// <summary>Approximate parameter count (e.g., "7B", "13B", "70B", "8x7B"). Empty = unknown.</summary>
    public string ParameterCount { get; set; } = string.Empty;

    /// <summary>Free-text notes about this model (e.g., fine-tune details, known strengths/weaknesses, special tokens).</summary>
    public string? Notes { get; set; }

    /// <summary>Provider name, populated from JOIN queries (not persisted separately).</summary>
    public string? ProviderName { get; set; }
}
