namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class HusbandAwarenessProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int AwarenessLevel { get; set; } = 20;

    public int AcceptanceLevel { get; set; } = 20;

    public int VoyeurismLevel { get; set; } = 0;

    public int ParticipationLevel { get; set; } = 0;

    public int HumiliationDesire { get; set; } = 0;

    public int EncouragementLevel { get; set; } = 0;

    public int RiskTolerance { get; set; } = 10;

    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
