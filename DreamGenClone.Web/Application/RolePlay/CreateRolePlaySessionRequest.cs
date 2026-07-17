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

    /// <summary>Character ID of the session's POV persona character.</summary>
    public string? PersonaCharacterId { get; init; }

    public string PersonaName { get; init; } = "You";
    public string PersonaDescription { get; init; } = string.Empty;
    public string? PersonaTemplateId { get; init; }
    public string PersonaGender { get; init; } = "Unknown";
    public string PersonaRole { get; init; } = "Unknown";
    public string? PersonaRelationTargetId { get; init; }
    public IReadOnlyDictionary<string, int> PersonaStatOverrides { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public DreamGenClone.Domain.Templates.PhysicalAttributes? PersonaPhysicalAttributes { get; init; }

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

    /// <summary>Per-character encounter profile IDs, keyed by character ID.</summary>
    public IReadOnlyDictionary<string, string> CharacterEncounterProfileIds { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional per-session override for the maximum number of phase milestones to inject into
    /// the continuation prompt. When null, the global <c>RolePlayMemoryOptions.MaxMilestonesToInject</c>
    /// default is used.
    /// </summary>
    public int? MaxMilestonesToInject { get; init; }

    /// <summary>
    /// Optional per-session override for the maximum number of ArcCompletion entries (prior arcs)
    /// to inject. When null, the global <c>RolePlayMemoryOptions.MaxArcCompletionsToInject</c>
    /// default is used.
    /// </summary>
    public int? MaxArcCompletionsToInject { get; init; }

    /// <summary>
    /// Optional per-session override for the maximum number of EncounterCompletion entries
    /// (encounter-boundary memories for the current arc) to inject. When null, the global
    /// <c>RolePlayMemoryOptions.MaxEncounterCompletionsToInject</c> default is used.
    /// </summary>
    public int? MaxEncounterCompletionsToInject { get; init; }
}
