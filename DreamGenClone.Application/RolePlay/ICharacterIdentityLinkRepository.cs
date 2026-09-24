using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// The store of explicit character-instance → character-template links (B-127). One row per linked instance; the
/// link is made by a human through the UI and is never inferred, so this store has no lookup-by-name and no
/// "best match" query on purpose.
/// </summary>
public interface ICharacterIdentityLinkRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>The template id this instance is linked to, or null when it is not linked.</summary>
    Task<string?> GetTemplateIdAsync(string ownerInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityLink>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the link. When the instance already links to a DIFFERENT template the call is refused unless
    /// <paramref name="replaceExisting"/> is true — re-pointing an identity is a deliberate act, never a side
    /// effect of clicking link twice.
    /// </summary>
    Task<CharacterIdentityLink> SaveAsync(
        CharacterIdentityLink link, bool replaceExisting, CancellationToken cancellationToken = default);

    Task DeleteAsync(string ownerInstanceId, CancellationToken cancellationToken = default);
}
