using System.Text.Json.Nodes;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// Reads prompt catalogs off disk and writes them into the suite store (B-135).
///
/// <para>
/// Discovery is deliberately shallow: catalogs are folders directly under the root (<c>baseline/</c>, <c>biglust/</c>,
/// <c>sex-slideshow/</c>), each with a <c>manifest.json</c>. Recursing would sweep up every <c>runs/*/manifest.json</c>
/// produced by past proof harnesses, which are records of what happened, not prompt catalogs to run — and importing
/// them would fill the store with empty suites.
/// </para>
///
/// <para>
/// A discovered manifest that yields no positions is skipped silently: the proof folders carry a manifest of a
/// different shape, and reporting them as broken catalogs would be noise on every import.
/// </para>
/// </summary>
public sealed class ImageSuiteImporter : IImageSuiteImporter
{
    private readonly IImageSuiteRepository _suites;
    private readonly ILogger<ImageSuiteImporter> _logger;
    private readonly string _manifestRoot;

    public ImageSuiteImporter(
        IImageSuiteRepository suites,
        IOptions<PlaygroundOptions> options,
        ILogger<ImageSuiteImporter> logger)
    {
        _suites = suites;
        _logger = logger;
        _manifestRoot = ResolveManifestRoot(options.Value.ManifestRoot);
    }

    /// <summary>The root the importer resolved, so a UI can say where it is looking.</summary>
    public string ManifestRoot => _manifestRoot;

    public IReadOnlyList<string> DiscoverManifests()
    {
        if (!Directory.Exists(_manifestRoot))
        {
            return [];
        }

        // Depth 1 only: one folder per catalog.
        return Directory
            .EnumerateDirectories(_manifestRoot)
            .Select(directory => Path.Combine(directory, "manifest.json"))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ImageSuiteImportReport>> ImportAllAsync(CancellationToken cancellationToken = default)
    {
        var reports = new List<ImageSuiteImportReport>();
        foreach (var manifestPath in DiscoverManifests())
        {
            cancellationToken.ThrowIfCancellationRequested();

            PromptSuiteManifest manifest;
            try
            {
                manifest = Read(manifestPath);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                // One unreadable catalog must not stop the others importing.
                _logger.LogInformation("Skipped catalog '{ManifestPath}': {Message}", manifestPath, exception.Message);
                continue;
            }

            if (manifest.Positions.Count == 0)
            {
                // A proof manifest rather than a prompt catalog. Not an error, and not worth a report entry.
                continue;
            }

            reports.Add(await ImportParsedAsync(manifestPath, manifest, cancellationToken));
        }

        return reports;
    }

    public async Task<ImageSuiteImportReport> ImportAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new InvalidOperationException("A manifest path is required.");
        }

        var manifest = Read(manifestPath);
        return await ImportParsedAsync(manifestPath, manifest, cancellationToken);
    }

    private PromptSuiteManifest Read(string manifestPath)
    {
        // Position paths inside a manifest are relative to the manifest's own folder, which is what makes the catalogs
        // movable: nothing in a catalog names an absolute path.
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? _manifestRoot;
        return PromptSuiteManifestValidation.ParseManifest(
            File.ReadAllText(manifestPath),
            relativePath => File.ReadAllText(Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private async Task<ImageSuiteImportReport> ImportParsedAsync(
        string manifestPath,
        PromptSuiteManifest manifest,
        CancellationToken cancellationToken)
    {
        var suite = new ImageSuite
        {
            Name = manifest.Suite,
            Version = 1,
            Kind = ImageSuiteKind.Catalog,
            Status = ImageSuiteStatus.Draft,
            Description = manifest.Purpose,
            Provenance = $"imported from {Path.GetFileName(Path.GetDirectoryName(manifestPath))}/manifest.json"
        };

        // Matched on (Name, Version), so re-importing the same catalog updates the same suite rather than piling up
        // versions nobody asked for. A run is unaffected: it copied its cells when it started.
        var existing = (await _suites.ListSuitesAsync(cancellationToken))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, suite.Name, StringComparison.OrdinalIgnoreCase)
                && candidate.Version == suite.Version);
        if (existing is not null)
        {
            suite.Id = existing.Id;
        }

        await _suites.UpsertSuiteAsync(suite, cancellationToken);

        var cellProblems = new List<string>();
        for (var ordinal = 0; ordinal < manifest.Positions.Count; ordinal++)
        {
            var position = manifest.Positions[ordinal];
            var cell = new ImageSuiteCell
            {
                SuiteId = suite.Id,
                Ordinal = ordinal,
                Name = position.Title,
                UserDirection = position.UserInput,
                ExpectedPrompt = position.Expected,
                BindingsJson = position.BindingsJson,
                VariantsJson = SerializeVariants(position),
                ProblemsJson = System.Text.Json.JsonSerializer.Serialize(position.Problems),

                // The catalog's declared negative rides along in the cell's SETTINGS, which is what it is: one of the
                // render settings this prompt was tested with. Stated plainly because it matters: the render path
                // sources a negative from the CHECKPOINT's compiler profile (the app is not allowed to invent one), so a
                // catalog negative is currently recorded and not sent. Sending it is a deliberate follow-up, not an
                // oversight - it changes what every catalog render submits.
                SettingsJson = MergeNegative(position),

                // A catalog is model-independent: the checkpoint is chosen when the suite is RUN, so the cell pins no
                // profile and inherits no compiler LLM.
                CheckpointProfileId = null,
                CompilerLlmJson = "{}",
                GatesJson = "[]",
                UpdatedUtc = DateTime.UtcNow
            };

            await _suites.UpsertCellAsync(cell, cancellationToken);
            cellProblems.AddRange(position.Problems.Select(problem => $"{position.Id}: {problem}"));
        }

        _logger.LogInformation(
            "Imported catalog '{Suite}' ({Cells} cells) from {ManifestPath} with {Problems} problem(s)",
            suite.Name, manifest.Positions.Count, manifestPath, manifest.Problems.Count + cellProblems.Count);

        return new ImageSuiteImportReport(
            suite.Id,
            suite.Name,
            suite.Version,
            manifest.Positions.Count,
            manifestPath,
            manifest.Problems,
            cellProblems);
    }

    private static string SerializeVariants(PromptSuitePosition position) =>
        System.Text.Json.JsonSerializer.Serialize(position.Variants);

    /// <summary>
    /// The position's settings with its declared negative folded in, so one field holds everything the catalog said
    /// about how to render this prompt.
    /// </summary>
    private static string MergeNegative(PromptSuitePosition position)
    {
        if (position.Negative.Length == 0)
        {
            return position.SettingsJson;
        }

        var settings = JsonNode.Parse(position.SettingsJson)?.AsObject() ?? [];
        settings["negative"] = position.Negative;
        return settings.ToJsonString();
    }

    /// <summary>
    /// Resolves the catalog root. An absolute configured path wins; otherwise the value (or the default) is tried
    /// against the working directory and each of its parents, so the app finds the catalogs whether it is started from
    /// the repo root or from <c>DreamGenClone.Web</c>.
    /// </summary>
    internal static string ResolveManifestRoot(string? configured)
    {
        var candidate = string.IsNullOrWhiteSpace(configured) ? PlaygroundOptions.DefaultManifestRoot : configured.Trim();
        if (Path.IsPathRooted(candidate))
        {
            return candidate;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var probe = Path.Combine(directory.FullName, candidate.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(probe))
            {
                return probe;
            }

            directory = directory.Parent;
        }

        // Not found: returned unresolved so the UI can say where it looked instead of silently importing nothing.
        return Path.Combine(Directory.GetCurrentDirectory(), candidate);
    }
}
