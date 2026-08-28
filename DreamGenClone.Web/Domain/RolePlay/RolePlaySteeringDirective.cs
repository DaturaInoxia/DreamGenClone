namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Per-character steering directive carrying the resolved steering intent (B-075).
/// Serialized to RolePlayInteraction.SteeringMetadataJson on staged instruction rows.
/// </summary>
public sealed record RolePlaySteeringDirective
{
    /// <summary>Character ID matching CharacterStats key / CharacterPerspectives.</summary>
    public string TargetCharacterId { get; init; } = string.Empty;

    /// <summary>Display name for prompt injection and UI.</summary>
    public string? TargetCharacterLabel { get; init; }

    /// <summary>Narrative role (Wife, Husband, OtherMan) for catalog lookup.</summary>
    public string? TargetCharacterRole { get; init; }

    /// <summary>The user-selected direction.</summary>
    public DreamGenClone.Domain.RolePlay.SteerDirection Direction { get; init; }

    /// <summary>User/LLM-authored prose directive text.</summary>
    public string FreeTextDirective { get; init; } = string.Empty;
}
