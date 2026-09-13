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

    public string Category { get; set; } = string.Empty;

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
