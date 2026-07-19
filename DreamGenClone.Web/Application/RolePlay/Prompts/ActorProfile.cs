using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Actor profile resolved at prompt-build time from ContinueAsActor + PromptIntent.
/// Determines content filtering across all 17 slots per the 5-profile matrix.
/// </summary>
public sealed record ActorProfile
{
    public required ActorProfileKind Kind { get; init; }
    public required string ActorName { get; init; }
    public required string ActorRole { get; init; }
    public required IReadOnlyList<string> PresentCharacterIds { get; init; }
    public required IReadOnlyList<string> AllCharacterIds { get; init; }
}
