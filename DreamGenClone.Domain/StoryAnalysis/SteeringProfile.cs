namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class SteeringProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Example { get; set; } = string.Empty;

    public string RuleOfThumb { get; set; } = string.Empty;

    public Dictionary<string, int> ThemeAffinities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> EscalatingThemeIds { get; set; } = [];

    public Dictionary<string, int> StatBias { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}