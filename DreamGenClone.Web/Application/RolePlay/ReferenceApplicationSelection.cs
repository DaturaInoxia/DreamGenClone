using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ReferenceApplicationSelection
{
    public string ElementKey { get; set; } = string.Empty;
    public string SemanticRole { get; set; } = string.Empty;
    public SceneAssetType AssetType { get; set; }
    public string? ActorKey { get; set; }
    public string Strategy { get; set; } = "TextOnly";
    public decimal? Strength { get; set; } = 1m;
    public string? SceneAssetId { get; set; }
    public string? SceneAssetImageId { get; set; }
    public int? SceneAssetVersion { get; set; }
    public string? SceneAssetSha256 { get; set; }
    public string BindingSnapshotJson { get; set; } = "{}";

    /// <summary>
    /// 1-based position in the ordered reference list. Null on a selection persisted before ordering existed, whose
    /// order is then the element order it was written with — normalised at load, never guessed per render.
    /// </summary>
    /// <remarks>
    /// The ordinal is REQUEST DATA, not a display detail: slot order decides placement, because the first reference
    /// anchors the frame (measured: with the location first the man was left; with a face first the woman was).
    /// </remarks>
    public int? Ordinal { get; set; }

    /// <summary>
    /// What this binding IS, as opposed to which legacy element key it came from — an
    /// <c>ImageStepSlotKind</c> name. Null on a legacy selection, which falls back to <see cref="ElementKey"/>.
    /// Kept as text to mirror <see cref="Strategy"/> and to stay loadable for rows written before slots existed.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Where the reference image came from — an <c>ImageStepReferenceSourceKind</c> name. Null means an approved
    /// scene asset, which is the only source the legacy bindings ever had.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Path to the OpenPose skeleton a Pose slot bound, relative to the pose library root. A skeleton is a reference
    /// IMAGE, so it travels through this same ordered channel rather than a second mechanism - and unlike an approved
    /// asset it has no production version or checksum of its own to validate against.
    /// </summary>
    public string? SkeletonRelativePath { get; set; }

    /// <summary>
    /// The pose LIBRARY PRESET a Pose slot bound, when it bound one.
    /// </summary>
    /// <remarks>
    /// The id rather than only the path, because the render reads a preset's skeleton BY ID so that a stale path can
    /// never make it read a file other than the preset it named. The path still travels beside it as the recorded
    /// artifact. Without this the pose-binding route had no caller at all: the option existed on the generation request
    /// and nothing in the app ever set it, so a bound pose was dropped silently (found 2026-09-27).
    /// </remarks>
    public string? PosePresetId { get; set; }

    /// <summary>
    /// The identity pack an <c>IdentityPackAsset</c> binding came from, with <see cref="ReferenceAssetId"/> naming the
    /// pack's own <c>SceneImageReferenceAsset</c> row. Two fields rather than one because a pack asset id is only
    /// meaningful inside its pack, and the pack is what the consumer has to look the image up in.
    /// </summary>
    public string? IdentityPackId { get; set; }

    /// <summary>The approved pack image this binding supplies. See <see cref="IdentityPackId"/>.</summary>
    public string? ReferenceAssetId { get; set; }

    /// <summary>
    /// What that pack image is, in words — "Front · Clothed", from <c>IdentityPackReferenceLabels.Describe</c>. Stored
    /// beside the ids rather than resolved at display time because the surfaces that show a binding do not all hold the
    /// identity service, and a pack asset id means nothing to the operator reading it. Null on a binding written before
    /// this existed, which then shows the ids exactly as it used to.
    /// </summary>
    public string? ReferenceLabel { get; set; }

    public bool UsesReference => !string.IsNullOrWhiteSpace(SceneAssetId)
        && !string.IsNullOrWhiteSpace(SceneAssetImageId);

    /// <summary>
    /// Whether this binding supplies an image at all, by any channel. Prompt adaptation asks THIS question: an element
    /// is supplied whether the image was an approved asset, a skeleton, or an approved identity-pack image, so each of
    /// them must replace the prose for the element it supplies.
    /// </summary>
    public bool SuppliesImage => UsesReference
        || !string.IsNullOrWhiteSpace(SkeletonRelativePath)
        || (!string.IsNullOrWhiteSpace(IdentityPackId) && !string.IsNullOrWhiteSpace(ReferenceAssetId));
}