namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents a possible opening/starting point for a scenario.
/// </summary>
public class Opening
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Title { get; set; }
    public string? Text { get; set; }

    /// <summary>
    /// Optional reference to a <see cref="Location.Id"/> in the parent scenario.
    /// When set, the session will start with <c>CurrentSceneLocation</c> seeded
    /// to this location's display name.
    /// </summary>
    public string? LocationId { get; set; }
}
