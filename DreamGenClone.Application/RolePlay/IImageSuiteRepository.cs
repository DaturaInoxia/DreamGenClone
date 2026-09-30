using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for image suites and their cells (B-135 B135-010, D4).
///
/// <para>
/// Suites are CONFIGURATION (authored in the Playground), so this store seeds nothing: an empty table is a legitimate
/// state, unlike the compiler profiles where a missing row must be refused. Seeding a catalog happens through an
/// explicit import (B135-032), never on open.
/// </para>
/// </summary>
public interface IImageSuiteRepository
{
    /// <summary>Creates the store if absent. Idempotent.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSuite>> ListSuitesAsync(CancellationToken cancellationToken = default);

    Task<ImageSuite?> GetSuiteAsync(string suiteId, CancellationToken cancellationToken = default);

    /// <summary>Writes a suite, matched on (Name, Version) so a version bump is a new row rather than an overwrite.</summary>
    Task UpsertSuiteAsync(ImageSuite suite, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSuiteCell>> ListCellsAsync(string suiteId, CancellationToken cancellationToken = default);

    /// <summary>Writes a cell, matched on (SuiteId, Ordinal) so a cell's position is its identity within a version.</summary>
    Task UpsertCellAsync(ImageSuiteCell cell, CancellationToken cancellationToken = default);

    Task DeleteCellAsync(string cellId, CancellationToken cancellationToken = default);
}
