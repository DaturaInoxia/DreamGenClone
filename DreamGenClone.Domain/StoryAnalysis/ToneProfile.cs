namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class ToneProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ToneIntensity Intensity { get; set; } = ToneIntensity.SensualMature;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}