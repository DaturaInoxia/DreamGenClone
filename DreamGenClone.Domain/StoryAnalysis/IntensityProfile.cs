namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class IntensityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IntensityLevel Intensity { get; set; } = IntensityLevel.SensualMature;

    public int BuildUpPhaseOffset { get; set; }

    public int CommittedPhaseOffset { get; set; }

    public int ApproachingPhaseOffset { get; set; } = 1;

    public int ClimaxPhaseOffset { get; set; } = 2;

    public int ResetPhaseOffset { get; set; } = -1;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public int GetPhaseOffset(NarrativePhase phase)
    {
        return phase switch
        {
            NarrativePhase.BuildUp => BuildUpPhaseOffset,
            NarrativePhase.Committed => CommittedPhaseOffset,
            NarrativePhase.Approaching => ApproachingPhaseOffset,
            NarrativePhase.Climax => ClimaxPhaseOffset,
            NarrativePhase.Reset => ResetPhaseOffset,
            _ => 0
        };
    }
}