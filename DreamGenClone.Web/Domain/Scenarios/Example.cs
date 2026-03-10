namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents a sample example or demonstration scenario.
/// </summary>
public class Example
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Title { get; set; }
    public string? Text { get; set; }
}
