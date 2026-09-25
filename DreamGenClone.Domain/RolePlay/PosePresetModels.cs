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

    /// <summary>Provenance: authored vs extracted-from-image.</summary>
    public string? ProvenanceJson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
