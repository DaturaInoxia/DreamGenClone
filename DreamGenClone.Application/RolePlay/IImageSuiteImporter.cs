using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>What one catalog import did, and everything it noticed while doing it.</summary>
public sealed record ImageSuiteImportReport(
    string SuiteId,

    /// <summary>The suite name taken from the manifest.</summary>
    string SuiteName,

    int Version,

    /// <summary>How many positions became cells.</summary>
    int CellCount,

    /// <summary>Where the catalog lives, so a re-import is obviously the same catalog.</summary>
    string ManifestPath,

    /// <summary>Suite-level gaps (a missing model legend, a skipped position file).</summary>
    IReadOnlyList<string> SuiteProblems,

    /// <summary>Per-cell gaps, already stored on the cells, surfaced here so an import can be read at a glance.</summary>
    IReadOnlyList<string> CellProblems)
{
    /// <summary>Total gaps. Zero means the catalog is complete for the models its legend declares.</summary>
    public int ProblemCount => SuiteProblems.Count + CellProblems.Count;
}

/// <summary>
/// Imports agent-authored prompt catalogs into runnable suites (B-135).
///
/// <para>
/// <b>The app never writes a manifest.</b> Catalogs are edited in source control by an agent and imported here, so the
/// app's copy of a catalog is always derived. Re-importing is the update path: it rewrites the suite's cells in place,
/// and because a RUN copies the cells it executed, re-importing cannot rewrite the record of a run that already
/// happened — which is what makes "iterate on the prompts, then run again" safe.
/// </para>
///
/// <para>
/// <b>Nothing here refuses a catalog.</b> A catalog with gaps imports, and the gaps ride along as problems on the cells
/// and in the report. A catalog whose positions cannot be rendered still tells you what is missing, which is more useful
/// than a tool that will not start.
/// </para>
/// </summary>
public interface IImageSuiteImporter
{
    /// <summary>
    /// Imports every catalog found under the configured root. A directory whose manifest describes no positions is
    /// skipped rather than imported as an empty suite — those are proof manifests, not prompt catalogs.
    /// </summary>
    Task<IReadOnlyList<ImageSuiteImportReport>> ImportAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Imports one catalog by path. Used when a caller knows exactly which catalog it wants.</summary>
    Task<ImageSuiteImportReport> ImportAsync(string manifestPath, CancellationToken cancellationToken = default);

    /// <summary>The catalogs currently discoverable, so a UI can list them before importing.</summary>
    IReadOnlyList<string> DiscoverManifests();
}
