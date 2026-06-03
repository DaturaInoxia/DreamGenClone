namespace DreamGenClone.Domain.RolePlay;

public sealed class CharacterStatProfileV2
{
    public string CharacterId { get; set; } = string.Empty;
    public int Desire { get; set; }
    public int Restraint { get; set; }
    public int Dominance { get; set; }
    public int Loyalty { get; set; }
    public int SelfRespect { get; set; }
    public DateTime SnapshotUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Stat values captured at session seed time or when the user explicitly applies a new stat
    /// profile via the Scenario tab. Used as the per-character decay target for the Reset phase.
    /// Empty for sessions that have not yet seeded baselines.
    /// </summary>
    public Dictionary<string, int> BaselineStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-stat deltas applied during the most recent scoring step. Used by the UI delta column
    /// and by the Reset-phase decay calculation. Reset to empty after each turn boundary.
    /// </summary>
    public Dictionary<string, int> LastStatDeltas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime? LastStatDeltaUpdatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Per-character mutable encounter dimension values that evolve during play via stat drift.
    /// Null until the first stat delta fires. Persisted in CharacterSnapshotsJson.
    /// </summary>
    public Dictionary<string, int>? RuntimeEncounterStats { get; set; }

    /// <summary>
    /// The character's narrative role (e.g. "Wife", "Husband", "The Other Man").
    /// Persisted in CharacterSnapshotsJson so CharacterRoles can be rebuilt on session load
    /// without requiring a separate column or extra data at runtime.
    /// </summary>
    public string? CharacterRole { get; set; }
}
