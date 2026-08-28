namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Actor profile kind resolved at prompt-build time from ContinueAsActor + PromptIntent.
/// Determines content filtering across all 17 slots.
/// </summary>
public enum ActorProfileKind
{
    Player = 0,
    NpcPresent = 1,
    NpcNonPresent = 2,
    Narrative = 3,
    Custom = 4
}
