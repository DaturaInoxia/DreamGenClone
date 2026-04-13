namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class ScenarioDefinitionEntity
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Weight { get; set; } = 1;

    public string VariantOf { get; set; } = string.Empty;

    public bool IsScenarioDefining { get; set; } = true;

    public List<string> Keywords { get; set; } = [];

    public List<string> DirectionalKeywords { get; set; } = [];

    public Dictionary<string, int> StatAffinities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // JSON payload consumed by CharacterStateScenarioMapper for fit scoring.
    public string ScenarioFitRules { get; set; } = string.Empty;

    // JSON payload reserved for phase-specific prompt/guidance generation.
    public string PhaseGuidance { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
