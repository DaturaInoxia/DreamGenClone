namespace DreamGenClone.Web.Domain.Scenarios;

using DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Represents a character entity in a scenario.
/// Can be backed by a character template or created inline.
/// </summary>
public class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string Role { get; set; } = "Unknown";

    public string Gender { get; set; } = "Unknown";

    /// <summary>
    /// Optional linked relation target used to disambiguate paired roles such as wife/husband.
    /// Target can be another scenario character id or the special persona target token.
    /// </summary>
    public string? RelationTargetId { get; set; }
    
    /// <summary>
    /// Reference to a CharacterTemplate ID if this character is template-backed.
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Optional base stat values applied when a role-play session starts.
    /// </summary>
    public Dictionary<string, int> BaseStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default role-play perspective mode for this character when a session is created.
    /// </summary>
    public CharacterPerspectiveMode PerspectiveMode { get; set; } = CharacterPerspectiveMode.ThirdPersonExternalOnly;
}
