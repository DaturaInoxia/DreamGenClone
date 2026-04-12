namespace DreamGenClone.Domain.RolePlay;

public sealed class DecisionPoint
{
    public string DecisionPointId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public NarrativePhase Phase { get; set; }
    public string TriggerSource { get; set; } = string.Empty;
    public TransparencyMode TransparencyMode { get; set; } = TransparencyMode.Directional;
    public List<string> OptionIds { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
