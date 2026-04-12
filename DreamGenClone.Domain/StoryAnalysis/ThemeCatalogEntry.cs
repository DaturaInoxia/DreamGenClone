namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class ThemeCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public int Weight { get; set; } = 1;

    public string Category { get; set; } = string.Empty;

    public Dictionary<string, int> StatAffinities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
