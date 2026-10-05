using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Builds the reference set for a run. This was duplicated in the scene and asset editing handlers —
/// same 45-line loop twice, with only the qualified strategy name differing — so a fix landed twice
/// or, as happened, once.
///
/// <para>
/// It serves EVERY reference CHANNEL a binding can arrive through, because a binding the resolver does not claim is
/// a reference the model never receives — and it used to be dropped without a word. The channels are classified by
/// <see cref="ReferenceBindingShape.ChannelOf"/>, which is their one owner:
/// </para>
/// <list type="bullet">
/// <item><description><b>ApprovedAsset</b> — an approved, immutable <c>SceneAssets</c> image.</description></item>
/// <item><description><b>IdentityPack</b> — a character's approved pack face or build, out of the pack store rather
/// than the asset store.</description></item>
/// <item><description><b>PoseSkeleton</b> — deliberately NOT resolved here: on a render a pose binding is
/// consolidated into the render's own pose reference by <c>ImageStepPoseBinding</c> (so resolving it here as well
/// would send the skeleton twice), and on an edit the source image already fixes the pose.</description></item>
/// <item><description><b>ScratchImage</b> — no route exists on any surface, so it is REFUSED by name rather than
/// dropped.</description></item>
/// </list>
/// </summary>
public sealed class MediaEditReferenceResolver
{
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;
    private readonly IReferenceStrategyResolver _strategies;
    private readonly ICharacterImageIdentityRepository? _identity;
    private readonly ICharacterImageAssetStorageService? _identityStorage;

    public MediaEditReferenceResolver(
        ISceneAssetRepository assets,
        ISceneAssetStorageService storage,
        IReferenceStrategyResolver strategies,
        ICharacterImageIdentityRepository? identity = null,
        ICharacterImageAssetStorageService? identityStorage = null)
    {
        _assets = assets;
        _storage = storage;
        _strategies = strategies;
        _identity = identity;
        _identityStorage = identityStorage;
    }

    /// <summary>
    /// Resolves every binding that carries a reference image, verifying the strategy the SURFACE implements and the
    /// approved immutable selection behind each image. Returns an empty list when nothing references an image, which
    /// the caller turns into a plain text edit.
    /// </summary>
    /// <param name="surface">
    /// Which code path is asking. The strategies it implements come from
    /// <see cref="ReferenceStrategyCatalogue.ImplementedReferenceStrategiesFor"/>, so a surface cannot demand a
    /// strategy no graph of its owns — which is what failed every reference-carrying asset edit.
    /// </param>
    public async Task<IReadOnlyList<MediaEditReference>> ResolveAsync(
        string? registeredModelId,
        IReadOnlyList<ReferenceApplicationSelection> applications,
        ReferenceStrategyCatalogue.ReferenceImageSurface surface,
        CancellationToken cancellationToken = default)
    {
        // Classified by the SHARED owner, so this filter cannot drift from what a caller believes the resolver
        // serves - the failure mode being a caller that pairs a role list with this output by position.
        var claimed = applications.Where(binding => ChannelOfFor(surface, binding) is not null).ToList();
        if (claimed.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(registeredModelId))
        {
            throw new InvalidOperationException(
                "Reference editing requires the exact registered editor model id.");
        }

        var references = new List<MediaEditReference>(claimed.Count);
        for (var index = 0; index < claimed.Count; index++)
        {
            var application = claimed[index];
            var channel = ChannelOfFor(surface, application)!.Value;
            var resolution = await _strategies.ResolveAsync(registeredModelId, application.Strategy, cancellationToken);
            if (!resolution.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Reference strategy '{application.Strategy}' for '{application.ElementKey}' is unavailable: {resolution.Reason}");
            }

            // MEMBERSHIP, not equality against one hardcoded name: the set is the surface's own, so a strategy the
            // surface implements is accepted whatever it is called, and one it does not is refused with the reason.
            var implemented = ReferenceStrategyCatalogue.ImplementedReferenceStrategiesFor(surface);
            if (!implemented.Contains(resolution.Strategy, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Reference strategy '{resolution.Strategy}' for '{application.ElementKey}' is qualified, but has no "
                    + $"implemented graph in this {ReferenceStrategyCatalogue.Label(surface)}; it implements "
                    + $"{string.Join(", ", implemented)}.");            }

            references.Add(channel switch
            {
                ReferenceChannel.IdentityPack => await ResolveIdentityPackAsync(
                    application, index + 1, cancellationToken),
                _ => await ResolveApprovedAssetAsync(application, index + 1, cancellationToken)
            });
        }

        return references;
    }

    /// <summary>
    /// The channel a binding arrives through on this surface, or null when the surface has no route for it.
    ///
    /// <para>
    /// A channel with no route is refused BY NAME here rather than filtered out. A binding the resolver declines to
    /// claim is a reference the model never receives, and the operator has no way to tell that from a reference that
    /// was applied and ignored — which is the silent drop the reference rules forbid.
    /// </para>
    ///
    /// <para>
    /// <b>Pose is the one deliberate exception</b>, and it is not a drop: a pose binding is consolidated into the
    /// render's own pose reference by <c>ImageStepPoseBinding</c> before the render is queued, so resolving it here
    /// as a reference image as well would send the same skeleton twice. On an edit the source image already fixes the
    /// pose, so a pose binding inherited from the source render's own bindings is inert by design.
    /// </para>
    /// </summary>
    private static ReferenceChannel? ChannelOfFor(
        ReferenceStrategyCatalogue.ReferenceImageSurface surface, ReferenceApplicationSelection binding)
    {
        if (!binding.SuppliesImage
            || string.Equals(binding.Strategy, ReferenceStrategyCatalogue.TextOnly, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var channel = ReferenceBindingShape.ChannelOf(binding);
        return channel switch
        {
            // Carried by the pose reference, never as a reference image. See this method's remarks.
            ReferenceChannel.PoseSkeleton => null,

            // No surface has a route for an unapproved render from this chain. Fail fast, with the channel named.
            ReferenceChannel.ScratchImage => throw new InvalidOperationException(
                $"Reference for '{binding.ElementKey}' is bound to a scratch image, which has no implementable route on "
                + $"this {ReferenceStrategyCatalogue.Label(surface)}. Bind an approved scene asset "
                + "(or the character's approved identity-pack image) instead."),

            _ => channel
        };
    }

    /// <summary>
    /// An approved scene-asset image, re-validated against the approved immutable selection the binding recorded so a
    /// reference superseded or unapproved between queueing and rendering fails the run rather than quietly
    /// conditioning on a different picture.
    /// </summary>
    private async Task<MediaEditReference> ResolveApprovedAssetAsync(
        ReferenceApplicationSelection application,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var assetImage = await _assets.GetImageAsync(application.SceneAssetImageId!, cancellationToken)
            ?? throw new InvalidOperationException($"Reference image '{application.SceneAssetImageId}' was not found.");
        if (!string.Equals(assetImage.AssetId, application.SceneAssetId, StringComparison.Ordinal)
            || assetImage.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved
            || assetImage.ProductionVersion != application.SceneAssetVersion
            || !string.Equals(assetImage.Sha256, application.SceneAssetSha256, StringComparison.Ordinal)
            || assetImage.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(assetImage.FileRelativePath))
        {
            throw new InvalidOperationException(
                $"Reference image '{application.SceneAssetImageId}' no longer matches its approved immutable selection.");
        }

        var relativePath = assetImage.FileRelativePath;
        return new MediaEditReference(
            ordinal,
            DescribeReference(application.SemanticRole, assetImage.DisplayName),
            $"{assetImage.Id}.png",
            assetImage.Sha256,
            token => _storage.OpenReadAsync(relativePath, token),
            ReferenceBindingShape.SlotKindOf(application));
    }

    /// <summary>
    /// A character's approved identity-PACK image (a face or a build). It is a different store from the approved
    /// scene assets: a pack image is a <c>SceneImageReferenceAsset</c> addressed by its own pack id, so it carries no
    /// scene asset id and the asset branch can never serve it — which is why a bound pack face or build used to be
    /// filtered out with no error at all, leaving every edit unable to condition on a character's identity.
    ///
    /// <para>
    /// The pack is re-read through the same resolvers a scene render uses, so a pack image superseded, unapproved or
    /// deleted between queueing and rendering fails the run instead of producing a different person.
    /// </para>
    /// </summary>
    private async Task<MediaEditReference> ResolveIdentityPackAsync(
        ReferenceApplicationSelection application,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var identity = _identity
            ?? throw new InvalidOperationException(
                "A reference bound to a character identity-pack image requires the identity repository.");
        var storage = _identityStorage
            ?? throw new InvalidOperationException(
                "A reference bound to a character identity-pack image requires the identity asset storage service.");

        var slotKind = ReferenceBindingShape.SlotKindOf(application);
        var packId = application.IdentityPackId!;
        var assetId = application.ReferenceAssetId!;
        string role;
        string fileName;
        string relativePath;
        string sha256;

        switch (slotKind)
        {
            case ImageStepSlotKind.Face:
            {
                var face = await new IdentityFaceReferenceResolver(identity)
                    .ResolveExactFaceAsync(ordinal, packId, assetId, cancellationToken);
                role = $"approved identity face reference ({face.FaceView})";
                fileName = $"{face.FaceAssetId}.png";
                relativePath = face.FileRelativePath;
                sha256 = face.Sha256;
                break;
            }

            case ImageStepSlotKind.Body:
            {
                var body = await new IdentityBodyReferenceResolver(identity)
                    .ResolveExactBodyAsync(ordinal, packId, assetId, cancellationToken);
                role = $"approved identity build reference ({body.BodyView}/{body.BodyState})";
                fileName = $"{body.BodyAssetId}.png";
                relativePath = body.FileRelativePath;
                sha256 = body.Sha256;
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Reference '{application.ElementKey}' is bound to an identity-pack image, but a pack carries "
                    + "faces and builds only. Bind it from an approved scene asset instead.");
        }

        return new MediaEditReference(
            ordinal,
            // The operator's own name for the image outranks the derived role, exactly as the asset channel behaves.
            ReferenceRoleClauses.LabelFor(application, role) ?? role,
            fileName,
            sha256,
            token => storage.OpenReadAsync(relativePath, token),
            slotKind);
    }

    /// <summary>
    /// What one reference IS, for the render's own provenance record — "location continuity (Left side)".
    ///
    /// <para>
    /// The name is read from the RESOLVED image row rather than from the binding's snapshot of it, because this
    /// method already holds the authoritative row and a location's accepted images are told apart only by the name an
    /// operator typed. Without it a multi-image location renders as several references all called "location
    /// continuity", which is the same unusable record the picker had before names existed.
    /// </para>
    ///
    /// <para>
    /// An unnamed image degrades to the element's own role, exactly as it read before names existed: nothing is
    /// invented to fill the gap.
    /// </para>
    /// </summary>
    private static string DescribeReference(string semanticRole, string? displayName)
    {
        var role = semanticRole?.Trim() ?? string.Empty;
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0) return role;
        return role.Length == 0 ? name : $"{role} ({name})";
    }

    /// <summary>
    /// Instruction for a reference-conditioned run: the source image is the base, references are
    /// guidance only, and unrelated detail is preserved.
    /// </summary>
    public static string BuildReferenceAwareInstruction(
        string instruction,
        IReadOnlyList<ReferenceApplicationSelection> applications)
    {
        var identityApplications = applications
            .Where(application => application.AssetType == SceneAssetType.CharacterFace
                || string.Equals(application.ElementKey, "Identity", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var identityConstraint = identityApplications.Count == 0
            ? string.Empty
            : " Identity references are face-local guidance only: change only the selected character face identity in the existing scene. Treat the existing scene's visible neck and body skin tone as authoritative and harmonize the corrected face's skin tone, undertone, exposure, and shading with that body under the scene lighting; do not import a mismatched complexion from the reference, and do not copy the reference image's body, pose, clothing, framing, background, lighting, or composition.";

        return $"The first input image is the existing scene and is the base image. Additional reference images are guidance only, never replacement images. {instruction.Trim()}{identityConstraint} Preserve all unrelated people, objects, scene geometry, framing, crop, lighting, colors, and composition exactly unless the instruction explicitly requests that specific change.";
    }
}
