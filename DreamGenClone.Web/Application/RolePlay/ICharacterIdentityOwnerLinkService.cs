using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The explicit, human-driven link between a character instance and the character template that owns its identity
/// (B-127). This is the ONLY write path for identity ownership: nothing infers it, and the UI's link action goes
/// through here so the template is validated before a row is written.
/// </summary>
public interface ICharacterIdentityOwnerLinkService
{
    /// <summary>
    /// Links <paramref name="characterId"/> (a scenario character or a character asset) to
    /// <paramref name="characterTemplateId"/>. Refuses an id that names no character, a template that is not a
    /// character template, and a re-point onto a different template unless <paramref name="replaceExisting"/> is
    /// true — re-pointing an identity is deliberate.
    /// </summary>
    Task<CharacterIdentityLink> LinkAsync(
        string characterId,
        string characterTemplateId,
        string? linkedBy = null,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default);

    Task UnlinkAsync(string characterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityLink>> ListAsync(CancellationToken cancellationToken = default);
}
