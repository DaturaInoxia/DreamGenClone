using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for the per-checkpoint compiler profiles (B-135 D13).
///
/// <para>
/// A profile is configuration in the database. There is no in-code fallback profile: a checkpoint with no row is
/// refused by name, which is the same posture the app already takes for model capability and gate thresholds.
/// </para>
/// </summary>
public interface IImageCompilerProfileRepository
{
    /// <summary>Creates the store and seeds the migration rows. Idempotent; never overwrites an edited row.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The profile for a checkpoint, matched on <c>RegisteredModel.ModelIdentifier</c> case-insensitively.
    /// Null when no row matches — the caller fails fast naming the checkpoint.
    /// </summary>
    Task<ImageCompilerProfile?> FindByCheckpointAsync(string checkpointIdentifier, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageCompilerProfile>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a profile. Validated first by <see cref="ImageCompilerProfileValidation"/>.</summary>
    Task UpsertAsync(ImageCompilerProfile profile, CancellationToken cancellationToken = default);
}
