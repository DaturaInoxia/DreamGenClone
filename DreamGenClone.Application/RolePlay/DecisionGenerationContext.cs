using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed class DecisionGenerationContext
{
    public string? ScenarioId { get; init; }
    public string? TriggerSource { get; init; }
    public NarrativePhase Phase { get; init; }
    public string? Who { get; init; }
    public string? What { get; init; }
    public string? PromptSnippet { get; init; }
    public string? AskingActorName { get; init; }
    public string? TargetActorId { get; init; }
    public TransparencyMode? TransparencyOverride { get; init; }
    public IReadOnlyList<CharacterStatProfileV2> RelevantActors { get; init; } = [];
}
