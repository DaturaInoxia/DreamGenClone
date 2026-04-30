using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IClimaxBeatRepository
{
    /// <summary>
    /// Returns all beat entries ordered by BeatCode.
    /// </summary>
    Task<IReadOnlyList<ClimaxBeatEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the entry for the given beat code, or null if not found.
    /// </summary>
    Task<ClimaxBeatEntry?> GetByCodeAsync(string beatCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or replaces the given entry (keyed by BeatCode). Invalidates the cache.
    /// </summary>
    Task SaveAsync(ClimaxBeatEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the entry with the given beat code. No-op if not found. Invalidates the cache.
    /// </summary>
    Task DeleteAsync(string beatCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// If the table is empty, inserts the canonical 32 sub-beats. Safe to call multiple times.
    /// </summary>
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all rows and re-inserts the canonical 32 sub-beats. Invalidates the cache.
    /// </summary>
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
