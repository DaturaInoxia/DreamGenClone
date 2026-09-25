using System.Numerics;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The one shape a pose-library search takes. A blank field means "do not filter", never "use a default".</summary>
public sealed record PoseLibraryQuery(string? Keyword = null, string? Category = null, string? LibraryId = null);

/// <summary>What the authoring tool saves: a view of the rig plus where the result belongs.</summary>
/// <param name="Head">
/// How the head is turned relative to the body, or null for a neutral head. Optional so a body-only save reads
/// as what it is rather than carrying a fabricated zero rotation.
/// </param>
/// <param name="Keypoints">
/// An explicit keypoint set, which is what the drag editor produces. When it is present the rig is not projected:
/// the operator has moved the joints by hand and that result is what gets saved. <see cref="View"/> and
/// <see cref="Head"/> still describe the pose the edit STARTED from, so the recipe stays complete.
/// </param>
/// <param name="Drags">A human-readable log of the drags applied, recorded in the recipe.</param>
public sealed record AuthoredPoseRequest(
    string Name,
    string Category,
    string? Keywords,
    PoseView View,
    string LibraryId,
    PoseHeadRotation? Head = null,
    PosePerson? Keypoints = null,
    IReadOnlyList<string>? Drags = null);

public interface IPoseLibraryService
{
    Task<IReadOnlyList<PoseLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosePreset>> SearchAsync(PoseLibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>The categories actually present, for the filter, instead of a hardcoded list that can go stale.</summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken = default);

    Task<PoseLibrary> CreateLibraryAsync(
        string name, string description, CancellationToken cancellationToken = default);

    /// <summary>
    /// The library authored poses go into by default, created the first time it is needed through the ordinary
    /// create-library path rather than by a second, hidden creation route.
    /// </summary>
    Task<PoseLibrary> EnsureAuthoredLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The rig posed for a view, as COCO-18 keypoints. The rig is projected — never rotated as flat 2D keypoints —
    /// because only a real projection turns a yaw into foreshortening instead of a tilt.
    /// </summary>
    /// <param name="rotations">
    /// A complete rig pose to project, which is what <see cref="PoseRigFit"/> produces for a pose that came from
    /// the library, or null for the natural standing stance. Either way the result is projected, so a loaded pose
    /// turns with the same geometry as an authored one.
    /// </param>
    PosePerson ProjectAuthoredPose(
        PoseView view, PoseHeadRotation? head = null, Quaternion[]? rotations = null);

    /// <summary>Renders an authored pose for on-screen preview. Writes nothing.</summary>
    byte[] RenderAuthoredPreview(
        PoseView view, PoseHeadRotation? head, int canvas, Quaternion[]? rotations = null);

    /// <summary>
    /// Renders the head alone, framed on the head joints. A body-scale render gives the head a handful of pixels,
    /// so the head tool needs its own framing to be judgeable at all.
    /// </summary>
    byte[] RenderAuthoredHeadPreview(PoseView view, PoseHeadRotation head, int canvas);

    /// <summary>
    /// Writes every named body angle to <paramref name="directory"/> as a pair: the projected keypoints the
    /// probe scores, and the skeleton image that conditions the render the keypoints are scored against.
    /// This is the input side of the only measurement that can justify calling an angle known-good.
    /// </summary>
    Task<IReadOnlyList<string>> ExportProjectedPosesAsync(
        string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the projected pose as a new preset: keypoints, the rendered skeleton, and the recipe that produced
    /// them. Refuses a duplicate name in the same library and refuses to write into a pack (a pack is re-imported,
    /// so an authored pose stored in one would be at the importer's mercy).
    /// </summary>
    Task<PosePreset> SaveAuthoredPoseAsync(
        AuthoredPoseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bytes of a preset's skeleton PNG. Fails loudly when the file is missing: silently conditioning on
    /// nothing produces a render that looks like every other candidate and is quietly not the pose that was asked for.
    /// </summary>
    Task<byte[]> ReadSkeletonAsync(string presetId, CancellationToken cancellationToken = default);

    /// <summary>The web path of a preset's skeleton, or null when the preset has no skeleton yet.</summary>
    string? SkeletonUrl(PosePreset preset);
}

/// <inheritdoc />
public sealed class PoseLibraryService : IPoseLibraryService
{
    private readonly IPosePresetRepository _presets;
    private readonly IWebHostEnvironment _environment;
    private readonly PoseStudioOptions _studio;

    public PoseLibraryService(
        IPosePresetRepository presets,
        IWebHostEnvironment environment,
        IOptions<PoseLibraryOptions> options,
        IOptions<PoseStudioOptions> studioOptions)
    {
        _presets = presets;
        _environment = environment;
        Options = options.Value;
        _studio = studioOptions.Value;
    }

    private PoseLibraryOptions Options { get; }

    public PosePerson ProjectAuthoredPose(
        PoseView view, PoseHeadRotation? head = null, Quaternion[]? rotations = null)
    {
        var mannequin = PoseMannequin.Standing();

        // A loaded library pose brings its own joint rotations — the rig fitted onto it — and is projected as it
        // stands, so it turns with real geometry rather than being re-derived from the stance. Otherwise the natural
        // standing stance is the starting point: with the arms hanging straight down and the legs straight and
        // together, a side view projects them onto the torso and onto each other, so a profile came out as a bare
        // vertical line.
        var pose = rotations ?? mannequin.StandingStance();

        if (pose.Length != mannequin.Joints.Count)
        {
            throw new InvalidOperationException(
                $"A loaded pose needs {mannequin.Joints.Count} joint rotations but {pose.Length} were given, so the "
                + "rig cannot be projected from it.");
        }

        // The head hangs from its own pivot above the neck, so turning the head leaves the body — and the arms,
        // which hang from the neck — exactly where they were.
        if (head is not null && !head.IsNeutral)
        {
            // Copied before writing: the caller keeps this array — the panel re-projects on every 5° press, and the
            // fit result it came from is still shown to the operator. Mutating it in place would drift the pose the
            // panel believes it loaded.
            pose = pose.ToArray();
            pose[mannequin.HeadIndex] = head.ToLocalRotation();
        }

        return PoseProjection.Project(mannequin, pose, view, _studio);
    }

    public byte[] RenderAuthoredPreview(
        PoseView view, PoseHeadRotation? head, int canvas, Quaternion[]? rotations = null) =>
        PoseSkeletonRenderer.RenderPng(ProjectAuthoredPose(view, head, rotations), canvas);

    public byte[] RenderAuthoredHeadPreview(PoseView view, PoseHeadRotation head, int canvas) =>
        PoseSkeletonRenderer.RenderPng(
            ProjectAuthoredPose(view, head), canvas, PoseMannequin.HeadCocoIndices);

    public async Task<PosePreset> SaveAuthoredPoseAsync(
        AuthoredPoseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("An authored pose needs a name.");
        }

        var category = request.Category?.Trim() ?? string.Empty;
        if (category.Length == 0)
        {
            throw new InvalidOperationException($"Pose '{name}' needs a category — the search matches on it.");
        }

        var libraryId = request.LibraryId?.Trim() ?? string.Empty;
        if (libraryId.Length == 0)
        {
            throw new InvalidOperationException($"Pose '{name}' needs a target library.");
        }

        var library = await _presets.GetLibraryAsync(libraryId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Pose library '{libraryId}' does not exist, so pose '{name}' has nowhere to go.");

        if (library.IsSystem)
        {
            throw new InvalidOperationException(
                $"Library '{library.Name}' is imported from a pack and is rebuilt on every import, so an authored "
                + "pose cannot be saved into it. Choose another library (the pack's poses stay searchable either way).");
        }

        var inLibrary = await _presets.SearchAsync(null, null, libraryId, cancellationToken);
        var clash = inLibrary.FirstOrDefault(
            preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"Library '{library.Name}' already holds a pose named '{clash.Name}'. Rename this one — overwriting "
                + "would silently change a pose something may already be using.");
        }

        // Two explicit sources and no third: either the operator dragged joints and those keypoints are the pose,
        // or the rig is projected from the view. Never both, and never a silent choice between them.
        var person = request.Keypoints is not null
            ? request.Keypoints
            : ProjectAuthoredPose(request.View, request.Head);
        var id = $"authored-{Slug(name)}-{Guid.NewGuid().ToString("N")[..8]}";
        var skeletonRelative = $"{Options.SkeletonFolder!.Replace('\\', '/').Trim('/')}/{libraryId}/{id}.png";

        var webRoot = _environment.WebRootPath
            ?? throw new InvalidOperationException(
                "The app has no web root, so an authored pose's skeleton cannot be written or served.");

        var skeletonPath = Path.Combine(
            webRoot,
            BodyStanceSkeletons.WebRootFolder,
            skeletonRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(skeletonPath)!);
        await File.WriteAllBytesAsync(
            skeletonPath, PoseSkeletonRenderer.RenderPng(person), cancellationToken);

        var preset = new PosePreset
        {
            Id = id,
            Name = name,
            Category = category,
            LibraryId = libraryId,
            Keywords = $"{category} {request.Keywords} authored {Kind(request)}".Trim(),
            KeypointsJson = OpenPosePoseJson.Serialize(person),
            SkeletonPngPath = skeletonRelative,
            ThumbnailPath = skeletonRelative,
            KnownGood = false,
            ProvenanceJson = BuildAuthoringProvenance(request),
            CreatedUtc = DateTime.UtcNow
        };

        await _presets.UpsertAsync(preset, cancellationToken);
        return preset;
    }

    /// <summary>
    /// Records how the pose was made, so it can be re-opened and turned again rather than being a one-way result.
    /// Deliberately does NOT set known-good: that flag is a measurement, and a fresh pose has not been measured.
    /// </summary>
    /// <summary>What the pose is, for its keywords: a projected rig pose or a hand-dragged one.</summary>
    private static string Kind(AuthoredPoseRequest request) =>
        request.Keypoints is null ? "mannequin" : "dragged";

    private string BuildAuthoringProvenance(AuthoredPoseRequest request)
    {
        var view = request.View;
        var provenance = new System.Text.Json.Nodes.JsonObject
        {
            ["source"] = "authored",
            ["kind"] = Kind(request),
            ["yawDegrees"] = view.YawDegrees,
            ["pitchDegrees"] = view.PitchDegrees,
            ["rollDegrees"] = view.RollDegrees,
            ["focalLengthPx"] = _studio.RequireFocalLengthPx(),
            ["cameraDistance"] = _studio.RequireCameraDistance(),
            ["canvas"] = _studio.RequireCanvas(),
            ["authoringVersion"] = 1
        };

        if (request.Head is { } head && !head.IsNeutral)
        {
            provenance["head"] = new System.Text.Json.Nodes.JsonObject
            {
                ["yawDegrees"] = head.YawDegrees,
                ["pitchDegrees"] = head.PitchDegrees,
                ["rollDegrees"] = head.RollDegrees
            };
        }

        if (request.Drags is { Count: > 0 } drags)
        {
            provenance["drags"] = new System.Text.Json.Nodes.JsonArray(
                drags.Select(drag => (System.Text.Json.Nodes.JsonNode)drag).ToArray());
        }

        return provenance.ToJsonString();
    }

    public Task<IReadOnlyList<PoseLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default) =>
        _presets.ListLibrariesAsync(cancellationToken);

    public Task<IReadOnlyList<PosePreset>> SearchAsync(
        PoseLibraryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _presets.SearchAsync(query.Keyword, query.Category, query.LibraryId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var all = await _presets.ListAsync(cancellationToken);
        return all
            .Select(preset => preset.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PoseLibrary> CreateLibraryAsync(
        string name, string description, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A pose library needs a name.");
        }

        var id = Slug(trimmed);
        var existing = await _presets.GetLibraryAsync(id, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"A pose library named '{existing.Name}' already exists (id '{existing.Id}'). Choose another name.");
        }

        var library = new PoseLibrary
        {
            Id = id,
            Name = trimmed,
            Description = description?.Trim() ?? string.Empty,
            IsSystem = false
        };

        await _presets.UpsertLibraryAsync(library, cancellationToken);
        return library;
    }

    public async Task<PoseLibrary> EnsureAuthoredLibraryAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _presets.GetLibraryAsync(PoseLibraryIds.Authored, cancellationToken);
        if (existing is not null) return existing;

        return await CreateLibraryAsync(
            PoseLibraryIds.AuthoredName,
            "Poses authored in the app: turned and projected from the rig, and later edited by hand.",
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ExportProjectedPosesAsync(
        string directory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("A folder is required to export the projected poses into.");
        }

        var target = Path.IsPathRooted(directory.Trim())
            ? directory.Trim()
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, directory.Trim()));
        Directory.CreateDirectory(target);

        var written = new List<string>();
        foreach (var (label, view) in PoseNamedViews.BodyTargets)
        {
            var person = ProjectAuthoredPose(view);
            var slug = PoseNamedViews.Slug(label);

            // The keypoints are the candidate the probe scores; the skeleton is the conditioning image a renderer
            // needs to produce the plate it scores them against. Both are written here so the folder the tool is
            // pointed at holds everything the measurement needs, and neither can drift from the other.
            var jsonPath = Path.Combine(target, $"{slug}.json");
            await File.WriteAllTextAsync(jsonPath, OpenPosePoseJson.Serialize(person), cancellationToken);
            written.Add(jsonPath);

            var skeletonPath = Path.Combine(target, $"{slug}.skeleton.png");
            await File.WriteAllBytesAsync(
                skeletonPath, PoseSkeletonRenderer.RenderPng(person, _studio.RequireCanvas()), cancellationToken);
            written.Add(skeletonPath);
        }

        return written;
    }

    public async Task<byte[]> ReadSkeletonAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var preset = await _presets.GetAsync(presetId, cancellationToken)
            ?? throw new InvalidOperationException($"Pose preset '{presetId}' does not exist.");

        if (string.IsNullOrWhiteSpace(preset.SkeletonPngPath))
        {
            throw new InvalidOperationException(
                $"Pose preset '{preset.Name}' has no skeleton image, so it cannot condition a render.");
        }

        var webRoot = _environment.WebRootPath
            ?? throw new InvalidOperationException(
                "The app has no web root, so pose skeletons cannot be read. Conditioning needs the "
                + "pose-library assets to be present in the content root.");

        var relative = preset.SkeletonPngPath.Replace('\\', '/').TrimStart('/');
        var path = Path.Combine(
            webRoot,
            BodyStanceSkeletons.WebRootFolder,
            relative.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The skeleton file '{relative}' for pose preset '{preset.Name}' is missing from "
                + $"'{BodyStanceSkeletons.WebRootFolder}/'. Fix: re-run the pose-library import "
                + "(it re-renders any missing skeleton), or re-render the preset. Conditioning cannot proceed without it.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"The skeleton file '{path}' is empty, so it cannot condition a render. Fix: re-run the "
                + "pose-library import to re-render it.");
        }

        return bytes;
    }

    public string? SkeletonUrl(PosePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return string.IsNullOrWhiteSpace(preset.SkeletonPngPath)
            ? null
            : $"/{BodyStanceSkeletons.WebRootFolder}/{preset.SkeletonPngPath.Replace('\\', '/').TrimStart('/')}";
    }

    /// <summary>Turns a display name into a stable, readable id that doubles as the primary key.</summary>
    internal static string Slug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? throw new InvalidOperationException($"'{value}' has no name characters to build an id from.") : slug;
    }
}
