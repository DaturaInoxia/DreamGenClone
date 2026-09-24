using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// The single owner of a character's <see cref="CharacterBodyCard"/> (B-122 Phase 0). One row per
/// character; a character has exactly one body card, and this store is the only mutable source of it.
/// </summary>
public interface ICharacterBodyCardRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>The character's body card, or null when none has been recorded yet.</summary>
    Task<CharacterBodyCard?> GetAsync(string characterProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the card under optimistic concurrency: <paramref name="expectedVersion"/> must equal the stored
    /// version (<c>0</c> creates the first row). A mismatch throws rather than overwriting another editor's
    /// work, and the returned card carries the new version.
    /// </summary>
    Task<CharacterBodyCard> SaveAsync(
        CharacterBodyCard card, int expectedVersion, CancellationToken cancellationToken = default);
}
