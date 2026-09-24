namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves any character id to the character template that owns its identity (B-127) — the ONE resolution path.
/// There is no second one, and no caller may guess: an id that cannot be resolved is refused with the fix named,
/// never mapped onto a template by name, description or "closest match".
/// </summary>
public interface ICharacterIdentityOwnerResolver
{
    /// <summary>
    /// Resolves <paramref name="ownerId"/> — a character template id, a scenario character id, or a character
    /// asset id — to its identity owner.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When the id is none of those, or when the character has no character template yet, naming what to do.
    /// </exception>
    Task<CharacterIdentityOwner> ResolveAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which namespace <paramref name="ownerId"/> belongs to, or null when it names no character at all. The
    /// explicit link action asks this before writing a link, so a typo cannot create a link for a character that
    /// does not exist.
    /// </summary>
    Task<CharacterIdentityOwnerKind?> IdentifyAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every scenario character and character asset that resolves to <paramref name="characterTemplateId"/> — the
    /// instances of one character, for the UI's owner view. Includes instances that are only linked by an explicit
    /// link row.
    /// </summary>
    Task<IReadOnlyList<CharacterIdentityOwner>> ListInstancesAsync(
        string characterTemplateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every scenario character and character asset that resolves to NO template — the explicit link action's work
    /// list (B-127). Each candidate carries the resolver's own refusal as its reason, and <c>CanLink</c> is true only
    /// when writing a link row can actually change the outcome (a scenario character that already carries a template
    /// reference is resolved from the scenario, not from a link, so linking it would be a silent no-op).
    /// </summary>
    Task<IReadOnlyList<CharacterIdentityCandidate>> ListUnlinkedAsync(
        CancellationToken cancellationToken = default);
}
