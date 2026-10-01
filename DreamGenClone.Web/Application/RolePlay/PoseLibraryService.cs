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
/// <param name="Metadata">
/// The operator's own metadata for this pose, when they set it in the editor. Null for every caller that is not the
/// editor — a pose saved without an explicit edit keeps "not declared" metadata and stays eligible for the pack
/// backfill, which is what the pack libraries rely on.
/// </param>
public sealed record AuthoredPoseRequest(
    string Name,
    string Category,
    string? Keywords,
    PoseView View,
    string LibraryId,
    PoseHeadRotation? Head = null,
    PosePerson? Keypoints = null,
    IReadOnlyList<string>? Drags = null,
    string? Origin = null,
    ExtractedPoseProvenance? Extraction = null,
    PoseMetadataEdit? Metadata = null);

/// <summary>
/// An operator's OWN metadata for a pose, as typed in the pose editor.
///
/// Deliberately not the same shape as <see cref="PoseMetadata"/>: that type also carries the review flag and its
/// note, which are a MEASUREMENT's output and have no business being typed in by hand.
/// </summary>
/// <param name="Prompt">
/// The prompt the pose renders with. The editor composes it from the four fields above unless the operator has written
/// their own, so this is the operator's text either way and is stored verbatim.
/// </param>
public sealed record PoseMetadataEdit(
    PoseStance Stance,
    PoseFacingDirection Direction,
    PoseCameraAngle Camera,
    PoseContentRating Rating,
    string Prompt);

/// <summary>
/// Where an EXTRACTED pose came from: the image that was read, the host that read it, and the graph it ran. Recorded
/// because an extracted pose is a measurement of a picture rather than a pose anyone authored, and a stored pose whose
/// origin is anonymous cannot be judged or reproduced.
/// </summary>
/// <param name="SourceImageSha256">The bytes the pose was read from, so the same picture is traceable.</param>
/// <param name="SourceImageLabel">What the operator was looking at, for a readable provenance record.</param>
/// <param name="HeadOnly">
/// True when the pose carries a face channel and is a head pose rather than a body pose. Recorded because the two are
/// different things that a render treats differently, and a stored pose must say which it is rather than leaving it to
/// be worked out from how many points happen to be in it.
/// </param>
public sealed record ExtractedPoseProvenance(
    string SourceImageSha256,
    string SourceImageLabel,
    string ProviderName,
    string Endpoint,
    string NodeName,
    string NodeSignature,
    string WorkflowVersion,
    bool HeadOnly);

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

    /// <summary>
    /// A loaded library pose TURNED by the rig rather than replaced by it: the pose keeps its own keypoints, hands,
    /// face and proportions, and the rig supplies only the MOTION.
    /// </summary>
    /// <param name="stored">The library pose as stored, in its own source-image pixels.</param>
    /// <param name="fittedView">
    /// The view the rig was fitted at — the baseline of the motion. A separate argument from <paramref name="view"/>
    /// on purpose: with the two equal the motion is zero by construction, which is what makes an unturned loaded pose
    /// project to exactly itself.
    /// </param>
    /// <param name="view">The view now asked for.</param>
    /// <param name="rotations">The rig pose fitted onto the library pose.</param>
    /// <param name="head">A head turn, applied to the rig on both sides of the difference so the head's own motion
    /// travels with the rest of it.</param>
    PosePerson ProjectLoadedPose(
        PosePerson stored,
        PoseView fittedView,
        PoseView view,
        Quaternion[] rotations,
        PoseHeadRotation? head = null);

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
    /// OVERWRITES an existing preset with the pose and metadata now in the editor.
    ///
    /// A separate operation from <see cref="SaveAuthoredPoseAsync"/> on purpose, not a flag on it: creation refuses a
    /// name already in the library, while overwriting is nothing but taking one. It also does NOT refuse a pack
    /// (system) library — metadata written over an imported row survives, because the importer skips any row an
    /// operator has edited. That refusal exists to stop authored POSES being stored where a re-import would lose them.
    /// </summary>
    Task<PosePreset> OverwritePoseAsync(
        string id, AuthoredPoseRequest request, CancellationToken cancellationToken = default);

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
        // A HEAD TURN IS A DELTA ON THE POSE'S OWN HEAD, not a replacement of it. Reported 2026-09-27: "the Head does
        // not, it mangled the whole pose". The cause was writing `head.ToLocalRotation()` straight over the head joint:
        // for a fitted pose that joint already holds the head angle the source photograph had, so the first press did
        // not turn the head by 5°, it SNAPPED it from the pose's own angle to the requested one — a 40° jump on a pose
        // whose head sat at -35°. Composing onto the existing rotation makes 0° mean "leave it as the pose has it" and
        // makes every press a genuine step, which is the whole point of a 5° control.
        if (head is not null && !head.IsNeutral)
        {
            // Copied before writing: the caller keeps this array — the panel re-projects on every 5° press, and the
            // fit result it came from is still shown to the operator. Mutating it in place would drift the pose the
            // panel believes it loaded.
            pose = pose.ToArray();
            pose[mannequin.HeadIndex] = Quaternion.Normalize(head.ToLocalRotation() * pose[mannequin.HeadIndex]);
        }

        return PoseProjection.Project(mannequin, pose, view, _studio);
    }

    /// <summary>
    /// A loaded library pose TURNED by the rig, instead of being replaced by it.
    ///
    /// The rig is fitted to ONE 2D view, and one view does not determine the 3D pose: several rig poses project to
    /// nearly the same picture from the view they were fitted at and diverge the moment the figure turns. So showing
    /// the fitted rig's projection — which is what this did — discarded the operator's pose and put a different figure
    /// on screen the instant they pressed a 5° arrow, losing the hands entirely because the rig is body-only.
    /// Measured 2026-09-27: the loaded figure was REPLACED, not turned (a 5° press moved joints 1.2–1.7% of height
    /// against 0.4–0.7% for the same press on the rig's own pose).
    ///
    /// The library pose then keeps its own keypoints, hands, face and proportions, and is turned by a RIGID ROTATION in
    /// 3D rather than by moving each joint along the rig's own displacement. That distinction was measured, and it is the
    /// whole of this method: a displacement field is only valid for the figure that produced it, so adding the rig's
    /// per-joint deltas to a pose with different proportions at different depths bent the skeleton instead of turning it.
    /// Measured 2026-09-27 over four 5° presses: the worst bone came to **0.634 of its length** — a bone crushed to 63%,
    /// reported as "it skews and slants things and make abnormal body positions". A single affine fitted to the same
    /// motion halved that (0.873) and a rigid rotation removes the cause outright: a rotation cannot shear a bone.
    ///
    /// How a joint is turned without depth of its own: its depth is BORROWED from the rig's corresponding joint at the
    /// view the rig was fitted at — the only place a depth exists — and its position on the canvas is unprojected at that
    /// depth, rotated, and projected back. At the fitted view the rotation is the identity, so an unturned pose maps to
    /// exactly itself.
    /// </summary>
    public PosePerson ProjectLoadedPose(
        PosePerson stored,
        PoseView fittedView,
        PoseView view,
        Quaternion[] rotations,
        PoseHeadRotation? head = null)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(fittedView);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(rotations);

        if (stored.Body.Count != PosePerson.BodyJointCount)
        {
            throw new InvalidOperationException(
                $"A loaded pose needs {PosePerson.BodyJointCount} body keypoints for the rig's turn to be applied "
                + $"to, but this one has {stored.Body.Count}.");
        }

        var mannequin = PoseMannequin.Standing();
        var pose = rotations;

        // The head turn is applied to the rig BEFORE its depths are read, so the head's own motion is part of the one
        // rigid rotation instead of a second, separate displacement. Composed onto the FIT's head rather than written
        // over it, for the reason spelled out in ProjectAuthoredPose: replacing it turns the first press into a jump.
        if (head is not null && !head.IsNeutral)
        {
            pose = rotations.ToArray();
            pose[mannequin.HeadIndex] = Quaternion.Normalize(head.ToLocalRotation() * pose[mannequin.HeadIndex]);
        }

        // A joint's DEPTH exists in exactly one place: the rig fitted onto this pose, at the view it was fitted at. The
        // loaded pose supplies its own positions and borrows those depths, which is what makes the turn a rigid rotation
        // of ONE figure rather than a displacement field applied to a different one.
        var world = PoseProjection.WorldPositions(mannequin, pose, fittedView);
        var turn = PoseProjection.RotationBetween(fittedView, view);

        var focal = _studio.RequireFocalLengthPx();
        var cameraDistance = _studio.RequireCameraDistance();
        var canvas = _studio.RequireCanvas();
        var centre = canvas / 2.0;

        // The stored pose is in its own source pixels while the projection works on the canvas, so it is mapped on and
        // the result mapped back. The mapping is a uniform scale and a translation, so it cannot distort anything — and
        // it is what makes the result independent of the resolution the pose happens to be stored at.
        var framing = PoseSkeletonRenderer.ComputeFraming(stored, canvas);
        var inverse = framing.Inverse();

        PoseKeypoint Turn(PoseKeypoint point, int jointIndex)
        {
            var onCanvas = framing.Apply(point);
            var depth = world[jointIndex].Z;
            var distance = cameraDistance - depth;

            if (distance <= 0)
            {
                throw new InvalidOperationException(
                    $"The rig's joint {jointIndex} sits at depth {depth:0.###}, at or behind a camera at distance "
                    + $"{cameraDistance:0.###}, so a point riding it cannot be turned.");
            }

            // Undo the projection, placing the point in the rig's 3D frame at the depth it borrows...
            var x = (onCanvas.X - centre) * distance / focal;
            var y = -(onCanvas.Y - centre) * distance / focal;

            // ...turn the whole figure rigidly, which cannot shear, stretch a bone or move two joints inconsistently...
            var turned = Vector3.Transform(new Vector3((float)x, (float)y, (float)depth), turn);
            var turnedDistance = cameraDistance - turned.Z;

            if (turnedDistance <= 0)
            {
                throw new InvalidOperationException(
                    $"Turning to this view puts a joint at depth {turned.Z:0.###}, at or behind a camera at distance "
                    + $"{cameraDistance:0.###}. Bring the view back towards the fitted one.");
            }

            // ...and project it back, so the pose stays in the pixels it was stored in.
            return inverse.Apply(new PoseKeypoint(
                centre + (focal * turned.X / turnedDistance),
                centre - (focal * turned.Y / turnedDistance),
                point.Confidence));
        }

        // An absent joint stays absent, with its placeholder coordinate untouched: it is below the visibility floor either
        // way, and turning it would invent a position the pose never contained.
        //
        // The stored confidence is CARRIED, not re-derived. It is the pose's own data, and the fit's visibility is a
        // guess about a figure with no hands; replacing it would make joints vanish from a pose the operator only asked
        // to turn. The cost, stated rather than hidden: a pose turned as far as profile keeps whichever head joints its
        // source photograph showed.
        IReadOnlyList<PoseKeypoint> TurnCluster(IReadOnlyList<PoseKeypoint> cluster, int jointIndex) =>
            cluster
                .Select(point => point.Confidence <= OpenPosePoseJson.VisibilityFloor
                    ? point
                    : Turn(point, jointIndex))
                .ToArray();

        var body = new PoseKeypoint[PosePerson.BodyJointCount];
        for (var index = 0; index < body.Length; index++)
        {
            var source = stored.Body[index];

            body[index] = source.Confidence <= OpenPosePoseJson.VisibilityFloor
                ? source
                : Turn(source, mannequin.CocoIndices[index]);
        }

        return new PosePerson
        {
            Body = body,

            // The hands ride the wrist they hang from and the face rides the head, each at ITS joint's depth, so a cluster
            // turns as part of the same rigid figure instead of being left behind or reshaped. The rig has no hand model,
            // so a hand's own shape is the library's data and is not touched.
            RightHand = TurnCluster(
                stored.RightHand, mannequin.CocoIndices[OpenPosePoseJson.RightWristIndex]),
            LeftHand = TurnCluster(
                stored.LeftHand, mannequin.CocoIndices[OpenPosePoseJson.LeftWristIndex]),
            Face = TurnCluster(stored.Face, mannequin.HeadIndex)
        };
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

        // The operator's own metadata, when the editor supplied it. Absent for every other caller, which is what keeps
        // a pose saved without an explicit edit eligible for the pack backfill.
        if (request.Metadata is { } metadata) ApplyMetadata(preset, metadata);

        await _presets.UpsertAsync(preset, cancellationToken);
        return preset;
    }

    /// <summary>
    /// OVERWRITES an existing preset: the pose you have open is written back over the row it came from.
    ///
    /// Name, category, keypoints and metadata are replaced; the library, the keywords, the recorded provenance, the
    /// known-good measurement and the creation date are the ROW's, not the edit's, and are carried across untouched —
    /// a pack row's keywords are how the library's search still finds it after its camera angle was corrected.
    /// </summary>
    public async Task<PosePreset> OverwritePoseAsync(
        string id, AuthoredPoseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException(
                "Overwriting a pose needs the id of the pose being written over; without it there is no row to write.");
        }

        var existing = await _presets.GetAsync(id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Pose preset '{id}' is not in the library, so there is nothing to overwrite. Save it as a new pose "
                + "instead.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("An overwritten pose still needs a name — the library lists poses by it.");
        }

        var category = request.Category?.Trim() ?? string.Empty;
        if (category.Length == 0)
        {
            throw new InvalidOperationException($"Pose '{name}' needs a category — the search matches on it.");
        }

        // The same two explicit sources as creation, and never both: the operator's dragged keypoints are the pose, or
        // the rig is projected from the view.
        var person = request.Keypoints is not null
            ? request.Keypoints
            : ProjectAuthoredPose(request.View, request.Head);

        // The skeleton is re-rendered over the row's OWN file, because the pose still IS that row: a fresh path would
        // orphan the old PNG and leave two files on disk for the one pose the library shows. A row with no skeleton is
        // left without one rather than pointed at a file nothing serves.
        if (!string.IsNullOrWhiteSpace(existing.SkeletonPngPath))
        {
            var webRoot = _environment.WebRootPath
                ?? throw new InvalidOperationException(
                    "The app has no web root, so this pose's skeleton cannot be written or served.");

            var skeletonPath = Path.Combine(
                webRoot,
                BodyStanceSkeletons.WebRootFolder,
                existing.SkeletonPngPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(skeletonPath)!);
            await File.WriteAllBytesAsync(
                skeletonPath, PoseSkeletonRenderer.RenderPng(person), cancellationToken);
        }

        var preset = new PosePreset
        {
            Id = existing.Id,
            Name = name,
            Category = category,
            LibraryId = existing.LibraryId,
            Keywords = existing.Keywords,
            KeypointsJson = OpenPosePoseJson.Serialize(person),
            SkeletonPngPath = existing.SkeletonPngPath,
            ThumbnailPath = existing.ThumbnailPath,
            KnownGood = existing.KnownGood,
            ProvenanceJson = existing.ProvenanceJson,
            CreatedUtc = existing.CreatedUtc,
            Stance = existing.Stance,
            Direction = existing.Direction,
            CameraAngle = existing.CameraAngle,
            ContentRating = existing.ContentRating,
            MetadataPrompt = existing.MetadataPrompt,
            MetadataNeedsReview = existing.MetadataNeedsReview,
            MetadataReviewNote = existing.MetadataReviewNote,
            MetadataOperatorEdited = existing.MetadataOperatorEdited
        };

        if (request.Metadata is { } metadata) ApplyMetadata(preset, metadata);

        await _presets.UpsertAsync(preset, cancellationToken);
        return preset;
    }

    /// <summary>
    /// Writes an operator's metadata onto a preset and marks the row as operator-edited.
    ///
    /// The marker is not bookkeeping: it is what makes the importer and the backfill leave this row alone from now on,
    /// including when the operator set one field and deliberately left the rest "not declared".
    ///
    /// The review flag and its note are CLEARED. They are a measurement's output — "the declaration disagrees with the
    /// keypoints" — and an operator saving this pose has answered the question that flag asked. Leaving it set would
    /// re-raise a disagreement against the operator's own declaration on every read.
    /// </summary>
    private static void ApplyMetadata(PosePreset preset, PoseMetadataEdit metadata)
    {
        preset.Stance = metadata.Stance;
        preset.Direction = metadata.Direction;
        preset.CameraAngle = metadata.Camera;
        preset.ContentRating = metadata.Rating;
        preset.MetadataPrompt = metadata.Prompt;
        preset.MetadataNeedsReview = false;
        preset.MetadataReviewNote = string.Empty;
        preset.MetadataOperatorEdited = true;
    }

    /// <summary>
    /// Records how the pose was made, so it can be re-opened and turned again rather than being a one-way result.
    /// Deliberately does NOT set known-good: that flag is a measurement, and a fresh pose has not been measured.
    /// </summary>
    /// <summary>What the pose is, for its keywords: a projected rig pose, a hand-dragged one, or one read off an image.</summary>
    private static string Kind(AuthoredPoseRequest request) =>
        request.Origin ?? (request.Keypoints is null ? "mannequin" : "dragged");

    private string BuildAuthoringProvenance(AuthoredPoseRequest request)
    {
        // An extracted pose carries keypoints and NOTHING else: it did not come from the rig, so recording a view, a
        // focal length or a camera distance would be recording values that took no part in producing it. The rig's
        // own record is written for the two sources that really are projections.
        if (request.Extraction is { } extracted)
        {
            return new System.Text.Json.Nodes.JsonObject
            {
                ["source"] = "extracted",
                ["kind"] = Kind(request),
                ["sourceImageSha256"] = extracted.SourceImageSha256,
                ["sourceImage"] = extracted.SourceImageLabel,
                ["provider"] = extracted.ProviderName,
                ["endpoint"] = extracted.Endpoint,
                ["node"] = extracted.NodeName,
                ["nodeSignature"] = extracted.NodeSignature,
                ["workflowVersion"] = extracted.WorkflowVersion,
                // Which KIND of pose this is, stated rather than inferred from its point count.
                ["headOnly"] = extracted.HeadOnly
            }.ToJsonString();
        }

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
