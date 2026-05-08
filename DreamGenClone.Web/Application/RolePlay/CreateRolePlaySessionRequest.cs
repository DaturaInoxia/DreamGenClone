using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// All parameters required to create a new role-play session.
/// Used by the session-create wizard so that per-session theme selections,
/// character stat overrides, and the awareness profile can be passed in a single object.
/// </summary>
public sealed class CreateRolePlaySessionRequest
{
    public string Title { get; init; } = "Role-Play Session";

    public string? ScenarioId { get; init; }

    public string PersonaName { get; init; } = "You";

    public string PersonaDescription { get; init; } = string.Empty;

    public string? PersonaTemplateId { get; init; }

    public string PersonaGender { get; init; } = "Unknown";

    public string PersonaRole { get; init; } = "Unknown";

    public string? PersonaRelationTargetId { get; init; }

    /// <summary>
    /// Explicit per-session theme selections set by the user during session create.
    /// When non-empty, these selections override the scenario's default RPThemeProfile.
    /// Themes not in this list are not seeded into the adaptive tracker.
    /// </summary>
    public IReadOnlyList<SessionThemeSelection> ThemeSelections { get; init; } = [];

    /// <summary>
    /// Starting stat overrides per character, keyed by character ID.
    /// Applied after the scenario default stats are merged, so these are the final starting values.
    /// </summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>> CharacterStatOverrides { get; init; }
        = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Starting stat overrides for the persona character, keyed by stat name.
    /// Applied after the persona template stats are seeded.
    /// </summary>
    public IReadOnlyDictionary<string, int> PersonaStatOverrides { get; init; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional Husband Awareness Profile to associate with this session.</summary>
    public string? AwarenessProfileId { get; init; }
}
