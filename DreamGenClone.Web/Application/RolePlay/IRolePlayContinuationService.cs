using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayContinuationService
{
    Task<RolePlayInteraction> ContinueAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? positionInTurn = null,
        int? turnActorCount = null);

    Task<ContinueAsResult> ContinueBatchAsync(
        RolePlaySession session,
        IReadOnlyList<ContinueAsActor> actors,
        bool includeNarrative,
        string? customActorName,
        string promptText,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> ContinueNarrativeAsync(
        RolePlaySession session,
        string actorName,
        string promptText,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? turnActorCount = null);

    /// <summary>
    /// B-088: Generates a narrative-variant interaction for a retry/rewrite through the
    /// narrative prompt builder + validation pipeline, using a caller-resolved model.
    /// Does not commit the interaction to the session or link it as an alternative.
    /// </summary>
    Task<RolePlayInteraction> ContinueNarrativeAsAlternativeAsync(
        RolePlaySession session,
        string actorName,
        string promptText,
        ResolvedModel resolved,
        string command,
        CancellationToken cancellationToken = default);
}
