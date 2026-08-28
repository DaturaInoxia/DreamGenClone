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

    public DreamGenClone.Domain.Templates.PhysicalAttributes? PhysicalAttributes { get; set; }

    /// <summary>
    /// Per-character location affinity rules. Multiple entries per (character, location)
    /// are allowed, each with a distinct or null TimeOfDay.
    /// Conflicts resolved by Excluded > Required > Preferred precedence,
    /// then exact-time match over wildcard (null TimeOfDay).
    /// </summary>
    public List<CharacterLocationAffinity> LocationAffinities { get; set; } = [];

    /// <summary>
    /// Seduction archetype identifiers (B-078). Values should match
    /// <c>SeductionArchetypeCatalog</c> entry Ids. Empty = no archetype configured →
    /// role-level <c>SteerRoleIntentCatalog</c> fallback applies.
    /// Only injected into prompts when Role is "OtherMan".
    /// </summary>
    public List<string> SeductionArchetypes { get; set; } = [];

    /// <summary>
    /// Optional default CharacterProfile (encounter profile) ID pre-selected
    /// when creating an RP session from this scenario. Can be overridden in the wizard.
    /// </summary>
    public string? DefaultEncounterProfileId { get; set; }

    /// <summary>
    /// Marks this character as the scenario's POV persona ("You").
    /// Exactly one character per scenario may have this flag set.
    /// The persona character is treated as a first-class character for
    /// location gates, affinities, scoring, and candidate selection.
    /// </summary>
    public bool IsPersona { get; set; }
}
