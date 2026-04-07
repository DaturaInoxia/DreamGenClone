using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IInteractionCommandService
{
    Task<bool> ToggleFlagAsync(
        RolePlaySession session,
        string interactionId,
        InteractionFlag flag,
        CancellationToken cancellationToken = default);

    Task UpdateContentAsync(
        RolePlaySession session,
        string interactionId,
        string newContent,
        CancellationToken cancellationToken = default);

    Task DeleteInteractionAsync(
        RolePlaySession session,
        string interactionId,
        bool deleteBelow,
        CancellationToken cancellationToken = default);

    Task<int> NavigateAlternativeAsync(
        RolePlaySession session,
        string interactionId,
        int direction,
        CancellationToken cancellationToken = default);

    Task DeleteAlternativeAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default);
}
