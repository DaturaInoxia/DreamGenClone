namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Prose-focused narrative defaults for a scenario.
/// Includes narrative tone, prose style preferences, and presentation constraints.
/// </summary>
public class NarrativeSettings
{
    public string? NarrativeTone { get; set; }
    public string? ProseStyle { get; set; }
    public string? PointOfView { get; set; }
    public List<string> NarrativeGuidelines { get; set; } = [];
}