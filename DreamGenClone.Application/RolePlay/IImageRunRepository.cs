using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for image runs and their cells (B-135 B135-014).
///
/// <para>
/// A run is EVIDENCE, not configuration, so this store is append-and-update rather than upsert-by-name: the same suite
/// may be run many times and every execution is its own row. Nothing is seeded and nothing is ever silently rewritten
/// across runs — a re-run creates a new run, because overwriting one would destroy the comparison the run exists for.
/// </para>
/// </summary>
public interface IImageRunRepository
{
    /// <summary>Creates the store if absent. Idempotent.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task InsertRunAsync(ImageRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a run in place. Deliberately narrow: the suite id, version and kind are immutable, so a run cannot be
    /// retargeted at a different suite's cells after the fact.
    /// </summary>
    Task UpdateRunAsync(ImageRun run, CancellationToken cancellationToken = default);

    Task<ImageRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Runs newest first. Optionally scoped to one suite, which is what a comparison view needs.</summary>
    Task<IReadOnlyList<ImageRun>> ListRunsAsync(string? suiteId = null, CancellationToken cancellationToken = default);

    Task UpsertCellAsync(ImageRunCell cell, CancellationToken cancellationToken = default);

    Task<ImageRunCell?> GetCellAsync(string cellId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageRunCell>> ListCellsAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Removes a run and its cells. Used by run history purge (B135-029); never used to "fix" a run.</summary>
    Task DeleteRunAsync(string runId, CancellationToken cancellationToken = default);
}
