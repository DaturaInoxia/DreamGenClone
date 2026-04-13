namespace DreamGenClone.Domain.RolePlay;

public sealed class ScenarioGuidanceTemplate
{
    public string ScenarioId { get; set; } = string.Empty;

    public string? VariantId { get; set; }

    public Dictionary<string, string> PhaseGuidance { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> EmphasisPoints { get; set; } = [];

    public List<string> AvoidancePoints { get; set; } = [];

    public Dictionary<string, string> CharacterPerspectiveInstructions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}