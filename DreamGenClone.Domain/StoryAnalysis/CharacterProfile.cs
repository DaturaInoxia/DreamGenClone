namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class CharacterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TargetGender { get; set; } = "Any";

    /// <summary>
    /// "Husband", "Wife", "OtherMan", or "Any".
    /// Determines which encounter behavioral dimensions are shown and validated.
    /// </summary>
    public string TargetRole { get; set; } = "Any";

    /// <summary>
    /// 7 canonical stats: Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect (0–100 each).
    /// Keys must be from AdaptiveStatCatalog.StatNames.
    /// </summary>
    public Dictionary<string, int> CharacterStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Role-specific encounter behavioral dimension values (0–100 each).
    /// Valid keys are determined by TargetRole via BehavioralDimensionCatalog.
    /// Empty for TargetRole = "Any".
    /// </summary>
    public Dictionary<string, int> EncounterStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional text appended after the generated tier text in the behavioral frame.
    /// When FullOverride is true and this is non-empty, this text is the entire behavioral frame.
    /// </summary>
    public string AdditionalNotes { get; set; } = string.Empty;

    /// <summary>
    /// When true and AdditionalNotes is non-empty, AdditionalNotes is used as the complete
    /// behavioral frame instead of generating dimension tier text. If AdditionalNotes is empty,
    /// this flag is ignored and tier text is generated normally.
    /// </summary>
    public bool FullOverride { get; set; }

    /// <summary>
    /// True for seeded archetype defaults. The UI will not allow deletion of seeded profiles.
    /// </summary>
    public bool IsSeeded { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
