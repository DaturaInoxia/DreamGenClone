namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents a possible opening/starting point for a scenario.
/// </summary>
public class Opening
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Title { get; set; }
    public string? Text { get; set; }
}
