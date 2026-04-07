using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IInteractionRetryService
{
    Task<RolePlayInteraction> RetryAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> RetryWithModelAsync(
        RolePlaySession session,
        string interactionId,
        string modelId,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> RetryAsAsync(
        RolePlaySession session,
        string interactionId,
        ContinueAsActor actor,
        string? customActorName = null,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> MakeLongerAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> MakeShorterAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> AskToRewriteAsync(
        RolePlaySession session,
        string interactionId,
        string instruction,
        CancellationToken cancellationToken = default);
}
