using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What to derive from the pose library. Both answers are stated, never defaulted.</summary>
/// <param name="LibraryId">
/// The pose library to turn into a suite, or null for EVERY library. One suite per library is the default shape because
/// a library is the unit an operator curates and a run is scoped to one suite.
/// </param>
/// <param name="IncludeUnplannable">
/// Whether to include poses whose metadata cannot justify a character reference (no declared direction or rating). They
/// are listed and their cell says why, so a gap in the pack is visible in the run instead of the pose quietly vanishing
/// from the suite.
/// </param>
public sealed record PoseLibrarySuiteRequest(string? LibraryId = null, bool IncludeUnplannable = true);

/// <summary>One suite derived from one pose library.</summary>
public sealed record PoseLibrarySuiteReport(
    string LibraryId,
    string LibraryName,
    string SuiteId,
    string SuiteName,
    int CellCount,
    int RemovedCells,
    IReadOnlyList<string> Problems);

/// <summary>
/// Turns the POSE LIBRARY into suites: one cell per pose preset (B-135).
///
/// <para>
/// This is the "the library IS a test suite" route. Nothing is hand-authored and nothing is duplicated: the cells are
/// derived from the presets, so importing a pack, correcting a pose's direction, or adding an angle in Character Studio
/// changes what the next run does without anyone editing a manifest.
/// </para>
///
/// <para>
/// <b>The cell records the POSE, not the angles.</b> A cell carries the preset id (plus its skeleton path for
/// provenance), the pose's own stored prompt, and a declared seed, so a run is reproducible. The face and body angles
/// the render also needs are NOT copied onto the cell: they are resolved at render time from the preset's current
/// metadata through <c>PoseMetadataPrompt.ReferencePlan</c>, so a corrected direction cannot leave a stale angle behind
/// on a cell that was built before the correction. The character is a RUN choice, so the same suite renders every pose
/// on whichever character the operator picks.
/// </para>
///
/// <para>
/// Rebuilding is idempotent on the library: the suite is matched by name (the importer's convention), cells are matched
/// by ordinal, and cells that no longer correspond to a preset are removed - otherwise a rebuilt suite would grow by
/// one copy of every cell per rebuild and a run would render the same pose three times.
/// </para>
/// </summary>
public interface IPoseLibrarySuiteBuilder
{
    Task<IReadOnlyList<PoseLibrarySuiteReport>> BuildAsync(
        PoseLibrarySuiteRequest request,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PoseLibrarySuiteBuilder : IPoseLibrarySuiteBuilder
{
    /// <summary>
    /// The suite name suffix and the seed base. Seeds are DECLARED per cell so a reproducible run has something to
    /// reproduce: a pose suite whose cells declared no seed could only ever be run in explore mode, which is the one
    /// mode that cannot answer "did this pose compose correctly, before and after a change".
    /// </summary>
    public const string SuiteNamePrefix = "Pose Library · ";

    public const long SeedBase = 60200;

    private readonly IPoseLibraryService _library;
    private readonly IImageSuiteRepository _suites;
    private readonly Microsoft.Extensions.Logging.ILogger<PoseLibrarySuiteBuilder> _logger;

    public PoseLibrarySuiteBuilder(
        IPoseLibraryService library,
        IImageSuiteRepository suites,
        Microsoft.Extensions.Logging.ILogger<PoseLibrarySuiteBuilder> logger)
    {
        _library = library;
        _suites = suites;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PoseLibrarySuiteReport>> BuildAsync(
        PoseLibrarySuiteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var libraries = await _library.ListLibrariesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.LibraryId))
        {
            libraries = libraries
                .Where(library => string.Equals(library.Id, request.LibraryId.Trim(), StringComparison.Ordinal))
                .ToList();
            if (libraries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Pose library '{request.LibraryId}' was not found, so no suite can be derived from it.");
            }
        }

        var reports = new List<PoseLibrarySuiteReport>();
        foreach (var library in libraries)
        {
            reports.Add(await BuildOneAsync(library, request, cancellationToken));
        }

        return reports;
    }

    private async Task<PoseLibrarySuiteReport> BuildOneAsync(
        PoseLibrary library,
        PoseLibrarySuiteRequest request,
        CancellationToken cancellationToken)
    {
        var presets = (await _library.SearchAsync(new PoseLibraryQuery(LibraryId: library.Id), cancellationToken))
            .OrderBy(preset => preset.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var suiteName = SuiteNamePrefix + library.Name;
        var suite = new ImageSuite
        {
            Name = suiteName,
            Version = 1,
            Kind = ImageSuiteKind.PoseLibrary,
            Status = ImageSuiteStatus.Draft,
            Description =
                $"Every pose in the '{library.Name}' library, one cell per pose. The pose's own declared direction and "
                + "rating decide which approved face and body angle the render conditions on, so a run picks the model "
                + "and the character and the suite supplies the poses.",
            Provenance = $"derived from pose library '{library.Name}' ({library.Id})"
        };

        // Matched on (Name, Version) exactly as the catalog importer does, so rebuilding updates the suite in place
        // rather than piling up versions nobody asked for.
        var existing = (await _suites.ListSuitesAsync(cancellationToken))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, suite.Name, StringComparison.OrdinalIgnoreCase)
                && candidate.Version == suite.Version);
        if (existing is not null)
        {
            suite.Id = existing.Id;
        }

        await _suites.UpsertSuiteAsync(suite, cancellationToken);

        var existingCells = existing is null
            ? []
            : await _suites.ListCellsAsync(suite.Id, cancellationToken);

        var problems = new List<string>();
        var ordinal = 0;
        var writtenCellIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            var plan = PoseMetadataPrompt.ReferencePlan(preset);
            var unplannable = PoseReferenceRequirement.Unplannable(preset.Name, plan);
            if (unplannable is not null)
            {
                if (!request.IncludeUnplannable)
                {
                    problems.Add($"{preset.Name}: left out — {plan.Rationale}");
                    continue;
                }

                // Included, and the cell itself says what is missing, so the gap shows up against the cell in a run
                // instead of the pose disappearing from the suite with no trace.
                problems.Add($"{preset.Name}: {unplannable}");
            }

            var cellId = existingCells
                .FirstOrDefault(cell => cell.Ordinal == ordinal)?.Id ?? Guid.NewGuid().ToString();
            writtenCellIds.Add(cellId);

            await _suites.UpsertCellAsync(
                new ImageSuiteCell
                {
                    Id = cellId,
                    SuiteId = suite.Id,
                    Ordinal = ordinal,
                    Name = preset.Name,
                    // Nothing was typed: the pose IS the direction, so there is no user input for a compiler to handle.
                    UserDirection = string.Empty,
                    // The pose's own stored prompt: the string the pose card shows and the pose proofs sent, so a suite
                    // render is comparable to the recorded pose results rather than a new experiment.
                    ExpectedPrompt = preset.MetadataPrompt,
                    BindingsJson = SerializePoseBinding(preset),
                    // No per-model variants: the wording is the pose's own, already model-ready.
                    VariantsJson = "{}",
                    ProblemsJson = JsonSerializer.Serialize(
                        unplannable is null ? Array.Empty<string>() : new[] { unplannable }),
                    SettingsJson = JsonSerializer.Serialize(new { seed = SeedBase + ordinal, steps = 30 }),
                    CheckpointProfileId = null,
                    CompilerLlmJson = "{}",
                    GatesJson = "[]",
                    UpdatedUtc = DateTime.UtcNow
                },
                cancellationToken);

            ordinal++;
        }

        // A pose that was DELETED from the library must leave the suite, or the next run renders a pose that no longer
        // exists (and its cell would still look legitimate in the report).
        var removed = 0;
        foreach (var stale in existingCells.Where(cell => !writtenCellIds.Contains(cell.Id)))
        {
            await _suites.DeleteCellAsync(stale.Id, cancellationToken);
            removed++;
        }

        _logger.LogInformation(
            "Derived suite '{Suite}' from pose library '{Library}': {Cells} cell(s), {Removed} removed, "
            + "{Problems} problem(s)",
            suiteName, library.Name, ordinal, removed, problems.Count);

        return new PoseLibrarySuiteReport(
            library.Id, library.Name, suite.Id, suiteName, ordinal, removed, problems);
    }

    /// <summary>
    /// The pose this cell stands for, as ONE declared binding: the source is the pose library, and the preset id is what
    /// the render reads the skeleton (and the angles) by. Written in the same shape the composer and the compiler use, so
    /// a cell and a hand-built step cannot disagree about what a pose reference is.
    /// </summary>
    private static string SerializePoseBinding(PosePreset preset) =>
        JsonSerializer.Serialize(new[]
        {
            new ReferenceApplicationSelection
            {
                ElementKey = nameof(ImageStepSlotKind.Pose),
                Kind = nameof(ImageStepSlotKind.Pose),
                SemanticRole = $"pose reference ({preset.Name} library skeleton)",
                Source = nameof(ImageStepReferenceSourceKind.PoseLibrarySkeleton),
                Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
                PosePresetId = preset.Id,
                SkeletonRelativePath = preset.SkeletonPngPath,
                Ordinal = 1
            }
        });
}
