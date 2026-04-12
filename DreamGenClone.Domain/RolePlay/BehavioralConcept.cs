namespace DreamGenClone.Domain.RolePlay;

public sealed class BehavioralConcept
{
    public string ConceptId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string TriggerConditions { get; set; } = string.Empty;
    public string GuidanceText { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
