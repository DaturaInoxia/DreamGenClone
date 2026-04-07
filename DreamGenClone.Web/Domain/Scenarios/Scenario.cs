namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents a complete scenario definition.
/// Scenarios contain plot, setting, style, characters, locations, objects, openings, and examples.
/// </summary>
public class Scenario
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Plot component including conflicts and goals.
    /// </summary>
    public Plot Plot { get; set; } = new();
    
    /// <summary>
    /// Setting/world component.
    /// </summary>
    public Setting Setting { get; set; } = new();
    
    /// <summary>
    /// Narrative style and tone component.
    /// </summary>
    public Style Style { get; set; } = new();
    
    /// <summary>
    /// Characters available in this scenario.
    /// </summary>
    public List<Character> Characters { get; set; } = [];
    
    /// <summary>
    /// Locations available in this scenario.
    /// </summary>
    public List<Location> Locations { get; set; } = [];
    
    /// <summary>
    /// Objects/items available in this scenario.
    /// </summary>
    public List<ScenarioObject> Objects { get; set; } = [];
    
    /// <summary>
    /// Possible scenario openings/starts.
    /// </summary>
    public List<Opening> Openings { get; set; } = [];
    
    /// <summary>
    /// Example scenarios or interactions.
    /// </summary>
    public List<Example> Examples { get; set; } = [];
    
    /// <summary>
    /// Estimated token count for this scenario.
    /// Updated when scenario content changes.
    /// </summary>
    public int EstimatedTokenCount { get; set; }
    
    /// <summary>
    /// Creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last modification timestamp in UTC.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The ID of the parsed story this scenario was adapted from, if any.
    /// </summary>
    public string? SourceParsedStoryId { get; set; }

    /// <summary>
    /// Default ranking profile to use when creating sessions from this scenario.
    /// </summary>
    public string? DefaultRankingProfileId { get; set; }

    /// <summary>
    /// Default tone profile to use when creating sessions from this scenario.
    /// </summary>
    public string? DefaultToneProfileId { get; set; }
}
