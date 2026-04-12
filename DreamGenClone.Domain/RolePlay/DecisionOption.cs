namespace DreamGenClone.Domain.RolePlay;

public sealed class DecisionOption
{
    public string OptionId { get; set; } = string.Empty;
    public string DecisionPointId { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public TransparencyMode VisibilityMode { get; set; } = TransparencyMode.Directional;
    public string Prerequisites { get; set; } = string.Empty;
    public string StatDeltaMap { get; set; } = string.Empty;
    public bool IsCustomResponseFallback { get; set; }
}
