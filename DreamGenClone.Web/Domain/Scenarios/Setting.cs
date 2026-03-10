namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents the setting/world component of a scenario.
/// Includes world description, rules, and environmental context.
/// </summary>
public class Setting
{
    public string? WorldDescription { get; set; }
    public string? TimeFrame { get; set; }
    public List<string> EnvironmentalDetails { get; set; } = [];
    public List<string> WorldRules { get; set; } = [];
}
