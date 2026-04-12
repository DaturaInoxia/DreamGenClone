namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class IntensityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IntensityLevel Intensity { get; set; } = IntensityLevel.SensualMature;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}