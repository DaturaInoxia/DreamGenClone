namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// User-authored, per-element overrides/removals for the canonical prompt payload. These are applied
/// as HARD SUBSTITUTIONS to the compiled Still brief's semantic snapshot at prompt-build time, so the
/// prompt compiler sees exactly one value per element (the override) rather than the original plus a
/// second copy. The persisted brief itself is never mutated.
///
/// Element keys are stable, explicit, and mirror the compiled Still brief's semantic snapshot shape:
///   scene.location | scene.environment | scene.timeOfDay | scene.lighting | scene.mood | scene.objects
///   moment.temporalAnchor | moment.frozenState | moment.visibleAction | moment.compositionRationale | moment.productionRoles
///   frozenState.visualDescription | frozenState.continuityState
///   continuity.start.stateSummary | continuity.end.stateSummary
///   character:{key}.physicalLocation | .position | .actionOrObservation | .sightline | .clothing | .appearance
/// where {key} is the character id (preferred) or the character name. An element key outside this set
/// fails fast at prompt-build time instead of being silently ignored.
/// </summary>
public sealed class ScenePromptOverrides
{
    public List<ScenePromptFieldOverride> Fields { get; set; } = [];

    /// <summary>Character keys whose entries (and appearance line) are removed from the prompt entirely.</summary>
    public List<string> RemovedCharacters { get; set; } = [];

    public bool IsEmpty => Fields.Count == 0 && RemovedCharacters.Count == 0;
}

/// <summary>One element override: replace the element's value, or remove it entirely.</summary>
public sealed class ScenePromptFieldOverride
{
    /// <summary>Stable element key (see <see cref="ScenePromptOverrides"/>).</summary>
    public string ElementKey { get; set; } = string.Empty;

    /// <summary>Replacement text. Ignored when <see cref="Removed"/> is true.</summary>
    public string? Value { get; set; }

    /// <summary>When true the element is removed from the payload (produces no value at all).</summary>
    public bool Removed { get; set; }
}
