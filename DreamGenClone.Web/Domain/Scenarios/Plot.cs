namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents the plot component of a scenario.
/// Includes primary conflicts, story arcs, and narrative goals.
/// </summary>
public class Plot
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public List<string> Goals { get; set; } = [];
}
