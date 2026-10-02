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
/// <param name="MetadataFilled">
/// Existing presets whose pose metadata this run filled in. Separate from <paramref name="PresetsImported"/> because a
/// pack imported before metadata existed has rows and no metadata: those are counts of two different things, and
/// reporting one number for both would hide which of the two happened.
/// </param>
/// <param name="Skipped">Files that were not usable as a pose, each with its reason.</param>
public sealed record PoseLibraryImportResult(
    int Packs,
    int PresetsImported,
    int PresetsAlreadyPresent,
    int SkeletonsRendered,
    IReadOnlyList<string> Skipped,
    int MetadataFilled = 0);

/// <summary>What a pack's <c>pack.json</c> declares: its identity, its provenance, and what its poses ARE.</summary>
internal sealed record PosePackManifest(
    string Name,
    string Description,
    string? Source,
    string? License,
    string? Attribution,
    PosePackDeclarations Declarations);

    /// <summary>What a metadata backfill did, so the caller can report a count instead of asserting success.</summary>
/// <param name="Filled">Presets whose metadata was missing and has now been written.</param>
/// <param name="Unchanged">
/// Presets left exactly as they were, because metadata was already present. This is the number that makes the
/// backfill's "never overwrites" promise visible: a second run fills nothing.
/// </param>
/// <param name="NotDeclared">
/// Presets whose pack declares nothing for them, so there was nothing to write. Named rather than counted silently,
/// because it is the actionable state for a pack that needs a declaration block.
/// </param>
public sealed record PoseMetadataBackfillResult(
    int Examined,
    int Filled,
    int Unchanged,
    IReadOnlyList<string> NotDeclared);

/// <summary>
/// What a facing recompute did. <paramref name="Repointed"/> is the count that matters — those are poses whose stored
/// direction was wrong because the only measurement that existed could not see a turn. <paramref name="Flagged"/> is
/// the count of poses whose two turn signals disagreed, which keep their stored direction and are marked for the
/// operator rather than guessed at.
/// </summary>
/// <param name="LeftAlone">
/// Rows that were not the recompute's business: an operator's own metadata, a library whose pack is not on disk, or a
/// category that declares nothing.
/// </param>
public sealed record PoseFacingRecomputeResult(
    int Examined,
    int Repointed,
    int Flagged,
    int LeftAlone);

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
    /// <summary>
    /// Fills in pose metadata for every stored preset that does not have any yet, and changes nothing else.
    ///
    /// This exists because metadata arrived AFTER the packs did: 579 rows already sit in the dev database with no
    /// stance, no rating and no prompt, and requiring the operator to delete and re-import the library to get metadata
    /// would also throw away the skeletons, the keywords and any pose they had edited. A preset that already has
    /// metadata is left untouched, so running this repeatedly costs one query and writes nothing.
    /// </summary>
    Task<PoseMetadataBackfillResult> EnsureMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-points the DIRECTION of poses that are already stored, from a fresh measurement of their own keypoints, and
    /// leaves every other field on the row as it was.
    ///
    /// This exists because the direction axis was measurable all along and was never measured: the shoulder ORDERING
    /// can only say front from back, and it stays negative through a 45-degree turn, so every 3/4 pose in the library
    /// was stored as "front" and could not be corrected by re-running anything. <see cref="EnsureMetadataAsync"/>
    /// cannot fix them either — it is fill-only by design, so a row that already has metadata is invisible to it.
    ///
    /// An OPERATOR-EDITED row is never recomputed, and a row whose pack declares nothing is skipped rather than
    /// guessed at. A row whose two turn signals disagree keeps its stored direction and is flagged for review.
    /// </summary>
    Task<PoseFacingRecomputeResult> RecomputeFacingAsync(CancellationToken cancellationToken = default);}

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
            .ToDictionary(preset => preset.Id, preset => preset, StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var alreadyPresent = 0;
        var rendered = 0;
        var metadataFilled = 0;
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

                if (existing.TryGetValue(id, out var stored))
                {
                    alreadyPresent++;

                    // The row is old news, but its METADATA may not exist yet: the packs were imported before
                    // metadata did. The rows are left alone unless they are missing metadata, in which case they are
                    // filled — which is what lets 579 already-imported poses gain a prompt without a re-import.
                    if (HasNoMetadata(stored))
                    {
                        var declaration = manifest.Declarations.For(CategoryOf(relative), relative);
                        if (!declaration.IsEmpty)
                        {
                            await FillMetadataAsync(stored, person, declaration, cancellationToken);
                            metadataFilled++;
                        }
                    }

                    continue;
                }

                var category = CategoryOf(relative);
                var number = NumberOf(relative);
                // Stored relative to the pose-library folder, under the pack's own sub-folder, so one reader
                // resolves every skeleton and two packs may each hold a same-named file.
                var skeletonRelative =
                    $"{_options.SkeletonFolder!.Replace('\\', '/').Trim('/')}/{packSlug}/{skeletonFileName}";

                await _presets.UpsertAsync(
                    Describe(
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
                        person,
                        manifest.Declarations.For(category, relative)),
                    cancellationToken);

                imported++;
            }
        }

        _logger.LogInformation(
            "Pose pack import from {PacksRoot}: {Packs} pack(s), {Imported} imported, {Present} already present, "
            + "{Rendered} skeletons rendered, {Filled} metadata filled in, {Skipped} skipped.",
            packsRoot, packFolders.Length, imported, alreadyPresent, rendered, metadataFilled, skipped.Count);

        return new PoseLibraryImportResult(
            packFolders.Length, imported, alreadyPresent, rendered, skipped, metadataFilled);
    }

    /// <summary>
    /// Re-points the direction of every stored pose whose OWN keypoints decide a different one, writing only through
    /// <see cref="Describe"/> so a recomputed pose and an imported pose are classified by the same code.
    /// </summary>
    public async Task<PoseFacingRecomputeResult> RecomputeFacingAsync(CancellationToken cancellationToken = default)
    {
        var packsRoot = ResolvePacksRoot();
        var stored = await _presets.ListAsync(cancellationToken);
        var declarations = await LoadDeclarationsAsync(packsRoot, cancellationToken);

        var examined = 0;
        var repointed = 0;
        var flagged = 0;
        var leftAlone = 0;

        foreach (var preset in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An operator's own metadata is never recomputed, and that is the whole reason the marker is stored.
            if (preset.MetadataOperatorEdited || !declarations.TryGetValue(preset.LibraryId, out var pack))
            {
                leftAlone++;
                continue;
            }

            var relative = RelativePathFromProvenance(preset);
            var declaration = pack.For(preset.Category, relative ?? string.Empty);
            if (declaration.IsEmpty)
            {
                leftAlone++;
                continue;
            }

            examined++;

            var recomputed = Describe(preset, ReadStoredPose(preset), declaration);

            if (recomputed.Direction == preset.Direction
                && recomputed.MetadataNeedsReview == preset.MetadataNeedsReview)
            {
                leftAlone++;
                continue;
            }

            if (recomputed.Direction != preset.Direction) repointed++;
            if (recomputed.MetadataNeedsReview) flagged++;

            await _presets.UpdateMetadataAsync(recomputed, cancellationToken);
        }

        _logger.LogInformation(
            "Pose facing recompute: {Examined} examined, {Repointed} re-pointed, {Flagged} flagged for review, "
            + "{LeftAlone} left alone.",
            examined, repointed, flagged, leftAlone);

        return new PoseFacingRecomputeResult(examined, repointed, flagged, leftAlone);
    }

    /// <summary>
    /// The declarations of every pack on disk, keyed by the library id that pack imported as, so a preset's own
    /// LibraryId finds its pack without a second pass over the folders per preset.
    /// </summary>
    private async Task<Dictionary<string, PosePackDeclarations>> LoadDeclarationsAsync(
        string packsRoot, CancellationToken cancellationToken)
    {
        var declarations = new Dictionary<string, PosePackDeclarations>(StringComparer.OrdinalIgnoreCase);
        foreach (var packFolder in Directory.EnumerateDirectories(packsRoot))
        {
            var folderName = Path.GetFileName(packFolder);
            if (string.IsNullOrWhiteSpace(folderName)) continue;

            var manifest = await ReadManifestAsync(packFolder, folderName, cancellationToken);
            declarations[PoseLibraryService.Slug(folderName)] = manifest.Declarations;
        }

        return declarations;
    }

    /// <summary>
    /// Fills in metadata for every stored preset that has none, reading the DECLARATION from the pack and the
    /// measurement from the preset's own stored keypoints.
    ///
    /// The stored keypoints are what get measured rather than the pack file, deliberately: the stored pose is what a
    /// render actually sends, so a pose the operator edited is classified from the pose that will be used.
    /// </summary>
    public async Task<PoseMetadataBackfillResult> EnsureMetadataAsync(CancellationToken cancellationToken = default)
    {
        var packsRoot = ResolvePacksRoot();
        var stored = await _presets.ListAsync(cancellationToken);
        var declarations = await LoadDeclarationsAsync(packsRoot, cancellationToken);

        var filled = 0;
        var unchanged = 0;
        var notDeclared = new List<string>();

        foreach (var preset in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!declarations.TryGetValue(preset.LibraryId, out var pack)) continue;

            if (!HasNoMetadata(preset))
            {
                unchanged++;
                continue;
            }

            // The per-file override is keyed by the pose's path inside its pack, which the import recorded in the
            // provenance. A preset whose provenance is missing still gets its category's default; what it does not get
            // is a guess at which file it came from.
            var relative = RelativePathFromProvenance(preset);
            var declaration = pack.For(preset.Category, relative ?? string.Empty);

            if (declaration.IsEmpty)
            {
                // Named once per category rather than once per pose: the fix is one block in one pack.json.
                notDeclared.Add($"{preset.LibraryId}/{preset.Category}");
                continue;
            }

            await FillMetadataAsync(preset, ReadStoredPose(preset), declaration, cancellationToken);
            filled++;
        }

        var missingDeclarations = notDeclared.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        _logger.LogInformation(
            "Pose metadata backfill: {Examined} examined, {Filled} filled, {Unchanged} already had metadata, "
            + "{NotDeclared} category/categories with no declaration in their pack.json.",
            stored.Count, filled, unchanged, missingDeclarations.Length);

        return new PoseMetadataBackfillResult(stored.Count, filled, unchanged, missingDeclarations);
    }

    /// <summary>
    /// True when a preset carries no metadata at all. Both conditions are asked because a half-filled row is possible:
    /// a rating can be declared for the pack while the category declares no stance, and re-running the backfill must
    /// then still be the thing that completes it.
    ///
    /// An OPERATOR-EDITED row is never "no metadata", whatever its values say. That is the one case the values cannot
    /// express: an operator who declares only a camera angle leaves a row that looks untouched by the three fields
    /// below, and filling it would discard the very edit the editor exists to make.
    /// </summary>
    private static bool HasNoMetadata(PosePreset preset) =>
        !preset.MetadataOperatorEdited
        && preset.ContentRating == PoseContentRating.Unrated
        && preset.Stance == PoseStance.Unknown
        && string.IsNullOrEmpty(preset.MetadataPrompt);

    /// <summary>
    /// Copies a preset with its metadata derived from the pose and its declaration, leaving every other field as it
    /// was. Shared by the import and the backfill so both produce the same metadata for the same pose — two code paths
    /// that derived it separately could disagree, and the disagreement would be invisible until a render looked wrong.
    /// </summary>
    private static PosePreset Describe(
        PosePreset preset, PosePerson person, PoseMetadataDeclaration declaration)
    {
        var metadata = PoseMetadataAnalyzer.Classify(person, declaration);

        return new PosePreset
        {
            Id = preset.Id,
            Name = preset.Name,
            Category = preset.Category,
            LibraryId = preset.LibraryId,
            Keywords = preset.Keywords,
            KeypointsJson = preset.KeypointsJson,
            SkeletonPngPath = preset.SkeletonPngPath,
            ThumbnailPath = preset.ThumbnailPath,
            KnownGood = preset.KnownGood,
            ProvenanceJson = preset.ProvenanceJson,
            CreatedUtc = preset.CreatedUtc,
            Stance = metadata.Stance,
            Direction = metadata.Direction,
            CameraAngle = metadata.Camera,
            ContentRating = metadata.Rating,
            MetadataPrompt = metadata.Prompt,
            MetadataNeedsReview = metadata.NeedsReview,
            MetadataReviewNote = metadata.ReviewNote,
            // Carried across rather than reset: this method only ever writes metadata, and a fill must not be able to
            // clear the marker that says an operator owns this row.
            MetadataOperatorEdited = preset.MetadataOperatorEdited
        };
    }

    /// <summary>Derives a stored preset's metadata and writes ONLY the metadata columns back.</summary>
    private async Task<bool> FillMetadataAsync(
        PosePreset preset,
        PosePerson person,
        PoseMetadataDeclaration declaration,
        CancellationToken cancellationToken)
    {
        var described = Describe(preset, person, declaration);
        await _presets.UpdateMetadataAsync(described, cancellationToken);
        return true;
    }

    /// <summary>The preset's own keypoints, which is the pose a render would send.</summary>
    private static PosePerson ReadStoredPose(PosePreset preset) =>
        OpenPosePoseJson.Parse(preset.KeypointsJson, $"stored pose '{preset.Id}'");

    /// <summary>The pose's path inside its pack, as recorded at import, or null when the provenance does not say.</summary>
    private static string? RelativePathFromProvenance(PosePreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.ProvenanceJson)) return null;

        try
        {
            var root = JsonNode.Parse(preset.ProvenanceJson);
            return root?["relativePath"] is JsonValue value && value.TryGetValue<string>(out var path)
                && !string.IsNullOrWhiteSpace(path)
                    ? path.Replace('\\', '/')
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
            Value(manifest, "attribution"),
            PosePackDeclarations.Parse(manifest, packFolder));
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
