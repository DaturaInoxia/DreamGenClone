namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// User-authored, per-element overrides/removals for the canonical prompt payload. These are applied
/// as HARD SUBSTITUTIONS to the compiled Still brief's semantic snapshot at prompt-build time, so the
/// prompt compiler sees exactly one value per element (the override) rather than the original plus a
/// second copy. The persisted brief itself is never mutated.
///
/// Element keys are stable, explicit, and mirror the compiled Still brief's semantic snapshot shape:
///   scene.objects
///   moment.momentId | moment.temporalAnchor | moment.participantSummary | moment.compositionRationale | moment.productionRoles
///   frozenState.location | .environment | .timeOfDay | .lighting | .mood | .objects | .contacts
///   frozenState.event.anchor | frozenState.event.line
///   character:{key}.physicalLocation | .position | .actionOrObservation | .sightline | .clothing | .appearance
///   character:{key}.at.anchor | character:{key}.at.posture | character:{key}.facing.toward
/// where {key} is the character id (preferred) or the character name. An element key outside this set — including a
/// dropped restatement such as <c>moment.frozenState</c> or <c>frozenState.visualDescription</c> — fails fast at
/// prompt-build time with a diagnostic naming where the fact now lives, instead of being silently ignored or written
/// into the snapshot as a duplicate (B-167 D2, see <c>StillBriefElements</c>).
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
