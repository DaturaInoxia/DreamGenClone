using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SemanticEventInferenceRequest
{
    public required string SessionId { get; init; }

    public required string InteractionId { get; init; }

    public required string ActorName { get; init; }

    public required string InteractionText { get; init; }

    public required IReadOnlyList<string> ContextTurns { get; init; }

    public required IReadOnlyList<string> AllowedEventIds { get; init; }

    /// <summary>
    /// Optional human-readable descriptions for each event ID. When provided, each
    /// entry is included in the inference prompt so the model understands what the
    /// event ID means rather than guessing from the ID string alone. Used by the
    /// sync encounter-boundary detection path to disambiguate precise event semantics.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EventDescriptions { get; init; }

    /// <summary>
    /// Optional AppFunction override for model resolution. When not set,
    /// defaults to RolePlaySemanticAnalysis.
    /// </summary>
    public AppFunction? AppFunction { get; init; }
}

public sealed class SemanticEventInferenceResult
{
    public bool Success { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public required IReadOnlyList<SemanticInferredEvent> Events { get; init; }

    public required string RawModelOutput { get; init; }

    public required string PromptSystem { get; init; }

    public required string PromptUser { get; init; }
}

public sealed class SemanticInferredEvent
{
    public required string EventId { get; init; }

    public required decimal Confidence { get; init; }

    public string? ActorName { get; init; }

    public string? TargetCharacterName { get; init; }

    public string? EvidenceSpan { get; init; }
}
