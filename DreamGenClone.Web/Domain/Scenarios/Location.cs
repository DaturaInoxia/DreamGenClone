namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents a location entity in a scenario.
/// Can be backed by a location template or created inline.
/// </summary>
public class Location
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Reference to a LocationTemplate ID if this location is template-backed.
    /// </summary>
    public string? TemplateId { get; set; }
}
