using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What one import run did, so a caller can report it instead of assuming success.</summary>
/// <param name="PresetsImported">Presets this run created.</param>
/// <param name="PresetsAlreadyPresent">
/// Presets that already existed and were therefore left untouched — this is what keeps a re-run from
/// overwriting metadata the operator has edited.
/// </param>
/// <param name="SkeletonsRendered">Skeleton PNGs this run wrote.</param>
/// <param name="Skipped">Files that were not usable as a pose, each with its reason.</param>
public sealed record PoseLibraryImportResult(
    int Packs,
    int PresetsImported,
    int PresetsAlreadyPresent,
    int SkeletonsRendered,
    IReadOnlyList<string> Skipped);

/// <summary>What a pack's <c>pack.json</c> declares. Only the name is required; the rest are provenance.</summary>
internal sealed record PosePackManifest(
    string Name, string Description, string? Source, string? License, string? Attribution);

public interface IPoseLibraryImporter
{
    /// <summary>
    /// Imports every pack under the configured packs root, each into its own library. Idempotent: a second run
    /// adds no rows and does not touch an existing preset's metadata.
    /// </summary>
    Task<PoseLibraryImportResult> ImportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports one pack by its folder name — the path a freshly downloaded pack takes, so adding a pack never
    /// re-imports the whole library.
    /// </summary>
    Task<PoseLibraryImportResult> ImportPackAsync(string packFolderName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the BUNDLED pack if its poses are not already present, and returns null when there was nothing to do.
    ///
    /// This exists because a pack that ships with the app should simply be there. Requiring an operator to press
    /// Import before the library holds anything makes a correct, empty-result search look like a broken one — which
    /// is exactly how it was reported (2026-09-25): the pack was on disk, the table was empty, and "search does
    /// nothing" was an honest reading of the UI. Idempotent, so calling it on every visit costs one indexed query.
    /// </summary>
    Task<PoseLibraryImportResult?> EnsureBundledPackAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PoseLibraryImporter : IPoseLibraryImporter
{
    /// <summary>
    /// The three stances measured to HOLD under OpenPoseXL2 on this stack (see
    /// <see cref="BodyStanceSkeletons"/>). Nothing else is tagged known-good: the flags are a record of a
    /// measurement, and inventing them for the rest would turn a verified property into a claim.
    /// </summary>
    private static readonly HashSet<string> VerifiedHolding = new(StringComparer.OrdinalIgnoreCase)
    {
        "NSFW_standing/512768/NSFW_standing028.json",
        "NSFW_Squatting/512512/NSFW_Squatting029.json",
        "NSFW_Kneeling/512768/NSFW_Kneeling017.json"
    };

    private readonly IPosePresetRepository _presets;
    private readonly IWebHostEnvironment _environment;
    private readonly PoseLibraryOptions _options;
    private readonly ILogger<PoseLibraryImporter> _logger;

    public PoseLibraryImporter(
        IPosePresetRepository presets,
        IWebHostEnvironment environment,
        IOptions<PoseLibraryOptions> options,
        ILogger<PoseLibraryImporter> logger)
    {
        _presets = presets;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public Task<PoseLibraryImportResult> ImportAsync(CancellationToken cancellationToken = default) =>
        ImportPacksAsync(onlyPackFolder: null, cancellationToken);

    public async Task<PoseLibraryImportResult?> EnsureBundledPackAsync(
        CancellationToken cancellationToken = default)
    {
        // "Already present" is asked of the STORE, not of the filesystem: the pack files shipping in the repo is
        // exactly the situation that made an unseeded library look populated.
        var existing = await _presets.SearchAsync(
            keyword: null, category: null, libraryId: PoseLibraryIds.BundledPackFolder, cancellationToken);

        if (existing.Count > 0) return null;

        _logger.LogInformation(
            "Seeding the bundled pose pack '{Pack}' into an empty library (one time).",
            PoseLibraryIds.BundledPackFolder);

        return await ImportPackAsync(PoseLibraryIds.BundledPackFolder, cancellationToken);
    }

    public Task<PoseLibraryImportResult> ImportPackAsync(
        string packFolderName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packFolderName))
        {
            throw new InvalidOperationException("A pose pack folder name is required.");
        }

        return ImportPacksAsync(packFolderName.Trim(), cancellationToken);
    }

    private async Task<PoseLibraryImportResult> ImportPacksAsync(
        string? onlyPackFolder, CancellationToken cancellationToken)
    {
        var packsRoot = ResolvePacksRoot();
        var skeletonRoot = ResolveSkeletonFolder();
        Directory.CreateDirectory(skeletonRoot);

        var packFolders = Directory.EnumerateDirectories(packsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (packFolders.Length == 0)
        {
            throw new InvalidOperationException(
                $"No pose pack folders were found under '{packsRoot}'. A pack is a folder holding a pack.json plus "
                + "its OpenPose JSON files (see pose-packs/README.md).");
        }

        if (onlyPackFolder is not null)
        {
            var match = packFolders.FirstOrDefault(
                name => string.Equals(name, onlyPackFolder, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Pose pack folder '{onlyPackFolder}' does not exist under '{packsRoot}'. Present packs: "
                    + $"{string.Join(", ", packFolders)}.");
            }

            packFolders = [match];
        }

        var existing = (await _presets.ListAsync(cancellationToken))
            .Select(preset => preset.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var alreadyPresent = 0;
        var rendered = 0;
        var skipped = new List<string>();

        foreach (var packFolder in packFolders)
        {
            var packDirectory = Path.Combine(packsRoot, packFolder);
            var packSlug = PoseLibraryService.Slug(packFolder);
            var manifest = await ReadManifestAsync(packDirectory, packFolder, cancellationToken);

            await _presets.UpsertLibraryAsync(
                new PoseLibrary
                {
                    Id = packSlug,
                    Name = manifest.Name,
                    Description = manifest.Description,
                    IsSystem = true
                },
                cancellationToken);

            var skeletonFolder = Path.Combine(skeletonRoot, packSlug);
            Directory.CreateDirectory(skeletonFolder);

            foreach (var file in Directory.EnumerateFiles(packDirectory, "*.json", SearchOption.AllDirectories)
                         .Where(path => !string.Equals(
                             Path.GetFileName(path), ManifestFileName, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(packDirectory, file).Replace('\\', '/');

                PosePerson person;
                try
                {
                    person = OpenPosePoseJson.Parse(
                        await File.ReadAllTextAsync(file, cancellationToken), $"{packFolder}/{relative}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException)
                {
                    skipped.Add($"{packFolder}/{relative}: {ex.Message}");
                    continue;
                }

                var id = BuildPresetId(packSlug, relative);
                var skeletonFileName = $"{id}.png";
                var skeletonPath = Path.Combine(skeletonFolder, skeletonFileName);
                if (!File.Exists(skeletonPath))
                {
                    await File.WriteAllBytesAsync(
                        skeletonPath, PoseSkeletonRenderer.RenderPng(person), cancellationToken);
                    rendered++;
                }

                if (existing.Contains(id))
                {
                    alreadyPresent++;
                    continue;
                }

                var category = CategoryOf(relative);
                var number = NumberOf(relative);
                // Stored relative to the pose-library folder, under the pack's own sub-folder, so one reader
                // resolves every skeleton and two packs may each hold a same-named file.
                var skeletonRelative =
                    $"{_options.SkeletonFolder!.Replace('\\', '/').Trim('/')}/{packSlug}/{skeletonFileName}";

                await _presets.UpsertAsync(
                    new PosePreset
                    {
                        Id = id,
                        Name = $"{category} {number}",
                        Category = category,
                        LibraryId = packSlug,
                        Keywords = BuildKeywords(relative, category, number),
                        KeypointsJson = OpenPosePoseJson.Serialize(person),
                        SkeletonPngPath = skeletonRelative,
                        ThumbnailPath = skeletonRelative,
                        KnownGood = VerifiedHolding.Contains(relative),
                        ProvenanceJson = await BuildProvenanceAsync(
                            packSlug, manifest, relative, file, cancellationToken),
                        CreatedUtc = File.GetCreationTimeUtc(file)
                    },
                    cancellationToken);

                imported++;
            }
        }

        _logger.LogInformation(
            "Pose pack import from {PacksRoot}: {Packs} pack(s), {Imported} imported, {Present} already present, "
            + "{Rendered} skeletons rendered, {Skipped} skipped.",
            packsRoot, packFolders.Length, imported, alreadyPresent, rendered, skipped.Count);

        return new PoseLibraryImportResult(packFolders.Length, imported, alreadyPresent, rendered, skipped);
    }

    /// <summary>
    /// Reads a pack's manifest. A pack without one is refused by name: the importer needs a library name and a
    /// provenance, and deriving either from the folder name would turn a guess into a stored record.
    /// </summary>
    private static async Task<PosePackManifest> ReadManifestAsync(
        string packDirectory, string packFolder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(packDirectory, ManifestFileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Pose pack '{packFolder}' has no {ManifestFileName}, so the library it would create has no name "
                + $"or provenance. Add {ManifestFileName} — see pose-packs/README.md. Nothing is guessed from the "
                + "folder name.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Pose pack '{packFolder}/{ManifestFileName}' is not valid JSON ({ex.Message}).", ex);
        }

        if (root is not JsonObject manifest)
        {
            throw new InvalidOperationException(
                $"Pose pack '{packFolder}/{ManifestFileName}' must be a JSON object.");
        }

        var name = Value(manifest, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"Pose pack '{packFolder}/{ManifestFileName}' has no 'name', which is required — it becomes the "
                + "library name the operator searches.");
        }

        return new PosePackManifest(
            name,
            Value(manifest, "description") ?? string.Empty,
            Value(manifest, "source"),
            Value(manifest, "license"),
            Value(manifest, "attribution"));
    }

    private static string? Value(JsonObject manifest, string property) =>
        manifest[property] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : null;

    /// <summary>
    /// The manifest every pack must carry. Its presence is what lets the importer name a library and record a
    /// provenance instead of inventing both from a folder name.
    /// </summary>
    private const string ManifestFileName = "pack.json";

    /// <summary>
    /// A stable id derived from the pack and the file's path inside it, so re-running the importer addresses the
    /// same rows instead of duplicating the library, and two packs may each hold a same-named file.
    /// </summary>
    internal static string BuildPresetId(string packSlug, string relativePath)
    {
        var stem = relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^5]
            : relativePath;

        var builder = new StringBuilder($"pack-{packSlug}-");
        foreach (var ch in stem)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
            else if (builder[^1] != '-') builder.Append('-');
        }

        var id = builder.ToString().TrimEnd('-');
        return id[..Math.Min(id.Length, 180)];
    }

    private static string CategoryOf(string relativePath)
    {
        var firstSegment = relativePath.Split('/')[0];
        return firstSegment.StartsWith("NSFW_", StringComparison.OrdinalIgnoreCase)
            ? firstSegment["NSFW_".Length..].ToLowerInvariant()
            : firstSegment.ToLowerInvariant();
    }

    private static string NumberOf(string relativePath)
    {
        var stem = Path.GetFileNameWithoutExtension(relativePath);
        var digits = new string(stem.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return digits.Length > 0 ? digits : stem.ToLowerInvariant();
    }

    private static string BuildKeywords(string relativePath, string category, string number) =>
        string.Join(' ', new[]
        {
            category,
            number,
            Path.GetFileNameWithoutExtension(relativePath),
            relativePath.Split('/').ElementAtOrDefault(1) ?? string.Empty,
            "openpose",
            "pack"
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static async Task<string> BuildProvenanceAsync(
        string packSlug,
        PosePackManifest manifest,
        string relativePath,
        string file,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        var provenance = new JsonObject
        {
            ["pack"] = packSlug,
            ["source"] = manifest.Source ?? "bundled",
            ["attribution"] = manifest.Attribution ?? string.Empty,
            ["license"] = manifest.License ?? "unverified",
            ["relativePath"] = relativePath,
            ["sha256"] = Convert.ToHexString(hash).ToLowerInvariant(),
            ["importer"] = typeof(PoseLibraryImporter).FullName,
            ["importerVersion"] = 2
        };

        return provenance.ToJsonString();
    }

    private string ResolvePacksRoot()
    {
        if (string.IsNullOrWhiteSpace(_options.PacksRoot))
        {
            throw new InvalidOperationException(
                $"Configuration '{PoseLibraryOptions.SectionName}:PacksRoot' is required to import pose packs. "
                + "Set it to the folder holding one sub-folder per pack (for example '../pose-packs').");
        }

        var configured = _options.PacksRoot.Trim();
        var resolved = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));

        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException(
                $"The configured pose-packs root '{resolved}' (from '{PoseLibraryOptions.SectionName}:PacksRoot' "
                + $"= '{configured}') does not exist, so no pose can be imported.");
        }

        return resolved;
    }

    private string ResolveSkeletonFolder()
    {
        if (string.IsNullOrWhiteSpace(_options.SkeletonFolder))
        {
            throw new InvalidOperationException(
                $"Configuration '{PoseLibraryOptions.SectionName}:SkeletonFolder' is required — it is where the "
                + "rendered skeletons are written, relative to the web root.");
        }

        var webRoot = _environment.WebRootPath
            ?? throw new InvalidOperationException(
                "The app has no web root, so the pose skeletons cannot be written or served. The importer needs "
                + "the pose-library assets to live under the content root.");

        var folder = _options.SkeletonFolder.Trim().Replace('\\', '/').Trim('/');
        // Relative to the pose-library folder, so the stored path and the reader agree on one shape.
        return Path.GetFullPath(Path.Combine(
            webRoot,
            BodyStanceSkeletons.WebRootFolder,
            folder.Replace('/', Path.DirectorySeparatorChar)));
    }
}
