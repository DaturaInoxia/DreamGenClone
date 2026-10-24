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
    /// The rendered image this binding supplies, out of the app's own scene-image store (an
    /// <c>ImageStepReferenceSourceKind.AppImage</c> binding).
    /// </summary>
    /// <remarks>
    /// An id space of its own, deliberately not <see cref="SceneAssetImageId"/>: a rendered image is not a
    /// <c>SceneAssets</c> row and has no approved image id or production version, so the approved-asset channel
    /// cannot serve it — and writing one there would make the approved-asset resolver's own validation refuse the
    /// binding for a version it will never have.
    /// </remarks>
    public string? ReferenceImageId { get; set; }

    /// <summary>
    /// The checksum <see cref="ReferenceImageId"/> carried when the operator bound it. The render re-reads the image
    /// and compares, so a binding naming an image whose bytes changed under it fails the run rather than conditioning
    /// on a different picture.
    /// </summary>
    public string? ReferenceImageSha256 { get; set; }

    /// <summary>
    /// What the OPERATOR says this reference is doing in this frame — which side it sits on, which way it faces, and
    /// whatever else they need the model told. Null (or empty) means they have said nothing, and the reference is
    /// described by the automatic role sentence alone (B-159).
    /// </summary>
    /// <remarks>
    /// Anchored to the reference, never to a numbered tag: the clause assigns the <c>&lt;imageN&gt;</c> at render from
    /// the order the images are actually sent, so adding a pack face (which shifts every location reference one slot
    /// later) cannot re-point this statement at a different image. It rides on the binding so it travels through the
    /// job payload and through every re-plan the composer performs.
    /// </remarks>
    public ReferenceDirections? Directions { get; set; }

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
    /// Whether this binding supplies a RENDERED image out of the app's own scene-image store, rather than an approved
    /// scene asset. The two are different stores with different id spaces, so a resolver serving one cannot serve the
    /// other by reading a <see cref="SceneAssetImageId"/> that is null on it.
    /// </summary>
    public bool UsesAppImage => !string.IsNullOrWhiteSpace(ReferenceImageId);

    /// <summary>
    /// Whether this binding supplies an image at all, by any channel. Prompt adaptation asks THIS question: an element
    /// is supplied whether the image was an approved asset, a rendered app image, a skeleton, or an approved
    /// identity-pack image, so each of them must replace the prose for the element it supplies.
    /// </summary>
    /// <remarks>
    /// A pose counts on its PRESET ID as well as on its recorded path, because the preset id is what the skeleton is
    /// read BY — the path is only the recorded artifact. This is the same rule
    /// <see cref="ReferenceApplicationSelectionValidation"/> already enforces ("a pose travels as a skeleton, and the
    /// preset ID is what makes it exact"), and stating it a second way here is what made a preset-only pose binding
    /// look like a binding that supplies nothing: on an edit it was then never resolved, and on a render
    /// <c>ImageStepPoseBinding</c> skipped it in silence.
    /// </remarks>
    public bool SuppliesImage => UsesReference
        || UsesAppImage
        || !string.IsNullOrWhiteSpace(SkeletonRelativePath)
        || !string.IsNullOrWhiteSpace(PosePresetId)
        || (!string.IsNullOrWhiteSpace(IdentityPackId) && !string.IsNullOrWhiteSpace(ReferenceAssetId));
}