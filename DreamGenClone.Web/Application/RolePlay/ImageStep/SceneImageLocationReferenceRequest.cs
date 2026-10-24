using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// A HOST's request for one image to be bound as a step's location reference — the "use as reference" command on an
/// image card, where a card offers to reload what made an image AND put that image into the composition.
/// </summary>
/// <remarks>
/// <para>
/// The host states the image; the COMPOSER decides where it goes and how it is planned. That split is deliberate: the
/// slot order, the strategy and the ordinal are the step's own business, and a host that built the binding itself would
/// be a second author of a plan the planner then has to accept or reject.
/// </para>
///
/// <para>
/// It means THE reference of the slot it lands in — one image, replacing whatever the slot held — because the command
/// it carries ("use this image as the location") names one place. Binding a second view of that place is a separate,
/// operator-driven pick, not something this request can imply.
/// </para>
///
/// <para>
/// It names the CHANNEL the image travels by, through a factory per channel rather than one loose set of fields. The two
/// channels are different stores with different ids — a rendered image is addressed by its scene-image id, an approved
/// asset image by its asset id and image id — and a record carrying all of them would let a caller fill in the wrong
/// pair and produce a binding the render then refuses for a reason that looks like nothing.
/// </para>
/// </remarks>
public sealed record SceneImageLocationReferenceRequest
{
    private SceneImageLocationReferenceRequest(
        ImageStepReferenceSourceKind sourceKind,
        string imageId,
        string sha256,
        string? displayName,
        string? assetId,
        int? productionVersion)
    {
        SourceKind = sourceKind;
        ImageId = imageId;
        Sha256 = sha256;
        DisplayName = displayName;
        AssetId = assetId;
        ProductionVersion = productionVersion;
    }

    /// <summary>The channel the image travels by. It decides which store the render reads it from.</summary>
    public ImageStepReferenceSourceKind SourceKind { get; }

    /// <summary>The image's id IN ITS OWN STORE: a scene-image record id, or an asset image id.</summary>
    public string ImageId { get; }

    /// <summary>The checksum the binding has to record, so the render can prove the bytes have not moved.</summary>
    public string Sha256 { get; }

    /// <summary>The operator's name for the image, when it has one.</summary>
    public string? DisplayName { get; }

    /// <summary>The container asset id, on the approved-asset channel only.</summary>
    public string? AssetId { get; }

    /// <summary>The production version, on the approved-asset channel only.</summary>
    public int? ProductionVersion { get; }

    /// <summary>The identity of this request, for a composer that applies one once per request.</summary>
    /// <remarks>
    /// It carries a NONCE as well as the image, so two presses of "use as reference" for the same image are two
    /// requests. Keyed on the image alone, the second press would be de-duplicated as already applied — and it would be
    /// indistinguishable from the first, which is what makes "I cleared the reference and asked for it again" silently
    /// do nothing.
    /// </remarks>
    public string Key => $"{SourceKind}|{ImageId}|{_nonce}";

    private readonly string _nonce = Guid.NewGuid().ToString("N");

    /// <summary>An image the app itself rendered — a Studio attempt, a gallery render, an edit.</summary>
    public static SceneImageLocationReferenceRequest ForRenderedImage(
        string imageId, string sha256, string? displayName = null)
    {
        Require(imageId, "scene image id");
        Require(sha256, "image checksum");
        return new SceneImageLocationReferenceRequest(
            ImageStepReferenceSourceKind.AppImage, imageId.Trim(), sha256.Trim(), displayName, null, null);
    }

    /// <summary>
    /// An APPROVED asset image. The approval is required rather than assumed: this channel's whole contract is an
    /// approved immutable selection, and the render re-validates the version and checksum against it.
    /// </summary>
    public static SceneImageLocationReferenceRequest ForApprovedAssetImage(
        string assetId, string imageId, int productionVersion, string sha256, string? displayName = null)
    {
        Require(assetId, "asset id");
        Require(imageId, "asset image id");
        Require(sha256, "image checksum");
        return new SceneImageLocationReferenceRequest(
            ImageStepReferenceSourceKind.ApprovedSceneAsset,
            imageId.Trim(),
            sha256.Trim(),
            displayName,
            assetId.Trim(),
            productionVersion);
    }

    /// <summary>
    /// The slot source this request binds, in the channel it named. The strategy comes from the caller because it is the
    /// step's own resolution — the request knows WHICH image, never HOW the model reads it.
    /// </summary>
    public ImageStepSlotSource ToSlotSource(string strategy)
        => SourceKind switch
        {
            ImageStepReferenceSourceKind.AppImage => new ImageStepSlotSource(
                SourceKind: ImageStepReferenceSourceKind.AppImage,
                Strategy: strategy,
                ReferenceImageId: ImageId,
                ReferenceImageSha256: Sha256,
                ReferenceLabel: DisplayName),

            ImageStepReferenceSourceKind.ApprovedSceneAsset => new ImageStepSlotSource(
                SourceKind: ImageStepReferenceSourceKind.ApprovedSceneAsset,
                Strategy: strategy,
                SceneAssetId: AssetId,
                SceneAssetImageId: ImageId,
                SceneAssetVersion: ProductionVersion,
                SceneAssetSha256: Sha256,
                ReferenceLabel: DisplayName),

            _ => throw new InvalidOperationException(
                $"A location reference request named source '{SourceKind}', which has no route. Build the request through "
                + "one of its factories rather than constructing it directly.")
        };

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"A location reference request requires the {label}.");
        }
    }
}
