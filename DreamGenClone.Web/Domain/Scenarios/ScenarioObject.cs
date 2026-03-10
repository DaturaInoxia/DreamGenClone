namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents an object/item entity in a scenario.
/// Can be backed by an object template or created inline.
/// </summary>
public class ScenarioObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Reference to an ObjectTemplate ID if this object is template-backed.
    /// </summary>
    public string? TemplateId { get; set; }
}
