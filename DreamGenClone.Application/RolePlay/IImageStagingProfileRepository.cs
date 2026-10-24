using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for the per-surface staging profiles (B-157).
///
/// <para>
/// A staging profile is configuration in the database. There is no in-code fallback profile: a render for a surface
/// with no row is refused by name, which is the same posture the app takes for compiler profiles, model capability
/// and gate thresholds.
/// </para>
/// </summary>
public interface IImageStagingProfileRepository
{
    /// <summary>Creates the store and seeds the migration rows. Idempotent; never overwrites an edited row.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The profile for a surface. Null when no row matches — the caller fails fast naming the surface, because a
    /// stand-in profile would place subjects the operator never configured.
    /// </summary>
    Task<ImageStagingProfile?> FindBySurfaceAsync(ImageStagingSurface surface, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageStagingProfile>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a profile. Validated first by <see cref="ImageStagingProfileValidation"/>.</summary>
    Task UpsertAsync(ImageStagingProfile profile, CancellationToken cancellationToken = default);
}
