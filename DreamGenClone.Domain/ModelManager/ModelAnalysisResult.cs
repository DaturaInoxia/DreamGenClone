namespace DreamGenClone.Domain.ModelManager;

public sealed class ModelAnalysisResult
{
    public string ModelIdentifier { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Core parameter suggestions (applicable across most functions).</summary>
    public double SuggestedTemperature { get; set; }
    public double SuggestedTopP { get; set; }
    public int SuggestedMaxTokens { get; set; }

    /// <summary>Per-function recommended overrides where the model's optimal settings differ by task.</summary>
    public List<FunctionRecommendation> FunctionRecommendations { get; set; } = [];

    /// <summary>Additional parameter suggestions beyond the core three (e.g., repetition_penalty, frequency_penalty, min_p, top_k).
    /// Extensible — new parameters can be surfaced here without changing the schema.</summary>
    public Dictionary<string, object> AdditionalParameters { get; set; } = [];

    /// <summary>Assessment of model strengths and weaknesses for this application's use cases.</summary>
    public string ModelCapabilityNotes { get; set; } = string.Empty;

    /// <summary>Detailed reasoning for the recommendations.</summary>
    public string Reasoning { get; set; } = string.Empty;

    public string AnalysedUtc { get; set; } = DateTime.UtcNow.ToString("o");
}

public sealed class FunctionRecommendation
{
    public string FunctionName { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double TopP { get; set; }
    public int MaxTokens { get; set; }
    public string Notes { get; set; } = string.Empty;
}
