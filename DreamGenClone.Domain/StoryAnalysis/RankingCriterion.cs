namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class ThemePreference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ProfileId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ThemeTier Tier { get; set; } = ThemeTier.Neutral;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
