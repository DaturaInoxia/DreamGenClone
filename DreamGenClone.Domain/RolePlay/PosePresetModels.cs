namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// A reusable, named pose preset: COCO-18 body keypoints plus a pre-rendered skeleton thumbnail and a
/// known-good flag. The pose foundation of the shared create/edit primitive — authored in the Pose
/// Studio (B-118) or extracted via DW Pose, and consumed by the pose-conditioned render (B-117) and
/// every edit/create surface as a first-class parameter.
/// </summary>
public sealed class PosePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>What kind of pose this is (standing, kneeling, …). Not the collection it lives in.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The <see cref="PoseLibrary"/> this preset belongs to. Empty on rows that predate the library
    /// model, which the importer assigns; a search by library id therefore never matches them silently.
    /// </summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>
    /// Free-text keywords the keyword search matches, in addition to the name and the category. Stored
    /// as one string so a preset is a single row and a search stays one query.
    /// </summary>
    public string Keywords { get; set; } = string.Empty;

    /// <summary>Serialized COCO-18 body keypoints.</summary>
    public string KeypointsJson { get; set; } = "[]";

    /// <summary>Pre-rendered skeleton PNG used as the ControlNet conditioning image.</summary>
    public string? SkeletonPngPath { get; set; }

    public string? ThumbnailPath { get; set; }

    /// <summary>Verified to hold under OpenPoseXL2 (standing / squatting / kneeling-feet-down).</summary>
    public bool KnownGood { get; set; }

    /// <summary>
    /// What the figure is doing, which way it faces and where the camera sits, plus the wording the pose should be
    /// rendered with. Stored on the preset rather than recomputed on every read, so the pose card, the test render and
    /// the audit report all read the same recorded values.
    ///
    /// <see cref="PoseStance.Unknown"/> and <see cref="PoseContentRating.Unrated"/> are the state of a pose whose pack
    /// declares nothing. That is deliberately not an error: a newly downloaded pack imports and is searchable, and its
    /// poses say "not declared" instead of the app inventing a stance, a rating or the references that follow from it.
    /// </summary>
    public PoseStance Stance { get; set; } = PoseStance.Unknown;

    /// <summary>Which way the figure faces — the axis the face and body reference angles are matched on.</summary>
    public PoseFacingDirection Direction { get; set; } = PoseFacingDirection.Unknown;

    /// <summary>Where the camera sits relative to the figure.</summary>
    public PoseCameraAngle CameraAngle { get; set; } = PoseCameraAngle.Unknown;

    /// <summary>Clothed or unclothed. Declared per pack; selects the body reference's state.</summary>
    public PoseContentRating ContentRating { get; set; } = PoseContentRating.Unrated;

    /// <summary>
    /// The prompt this pose is rendered with, composed from the fields above. Empty for a pose whose pack declares no
    /// rating — a prompt cannot be written without knowing whether the subject is clothed.
    /// </summary>
    public string MetadataPrompt { get; set; } = string.Empty;

    /// <summary>True when a measurement of the keypoints disagrees with the declared metadata.</summary>
    public bool MetadataNeedsReview { get; set; }

    /// <summary>The measured evidence behind <see cref="MetadataNeedsReview"/>.</summary>
    public string MetadataReviewNote { get; set; } = string.Empty;

    /// <summary>Provenance: authored vs extracted-from-image.</summary>
    public string? ProvenanceJson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
