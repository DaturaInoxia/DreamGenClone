namespace DreamGenClone.Components.Shared;

/// <summary>
/// Collapse state for the four sub-sections of <see cref="ProfileDetailDisplay"/>.
/// Owned by the consumer so collapse state persists across re-renders.
/// </summary>
public sealed class ProfileDetailCollapseState
{
    public bool DescriptionExpanded { get; set; }
    public bool CharacterStatsExpanded { get; set; }
    public bool BehaviourStatsExpanded { get; set; }
    public bool AdditionalNotesExpanded { get; set; }
}
