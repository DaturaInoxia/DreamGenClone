namespace DreamGenClone.Domain.PromptTester;

public sealed class PromptTestRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Comment { get; set; }
    public string ModelIdentifier { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? SystemMessage { get; set; }
    public string UserPrompt { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.9;
    public int MaxTokens { get; set; } = 500;
    public string? ResultText { get; set; }
    public string? ResultError { get; set; }
    /// <summary>Total character count of the prompt sent to the model (system message + user prompt).</summary>
    public int PromptCharCount { get; set; }
    /// <summary>Word count of the model's response text (0 if error).</summary>
    public int ResultWordCount { get; set; }
    /// <summary>Character count of the model's response text (0 if error).</summary>
    public int ResultCharCount { get; set; }
    public double ElapsedSeconds { get; set; }
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
}
