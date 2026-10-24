using System.Security.Cryptography;
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
/// <item><description><b>PoseSkeleton</b> — a pose library skeleton, whose route is SURFACE-specific: a
/// <c>Generate</c> carries it through the render's own pose reference (<c>ImageStepPoseBinding</c>), so resolving it
/// here as well would send the skeleton twice; an <c>Edit</c> has no render settings, so the reference image is its
/// only route and it IS resolved.</description></item>
/// <item><description><b>ScratchImage</b> — no route exists on any surface, so it is REFUSED by name rather than
/// dropped.</description></item>
/// <item><description><b>AppImage</b> — a rendered image the app already holds, out of the scene-image store. Served
/// on either surface, because a rendered image needs no render settings any more than an approved asset does; it is
/// re-validated against the checksum the binding recorded.</description></item>
/// </list>
/// </summary>
public sealed class MediaEditReferenceResolver
{
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;
    private readonly IReferenceStrategyResolver _strategies;
    private readonly ICharacterImageIdentityRepository? _identity;
    private readonly ICharacterImageAssetStorageService? _identityStorage;
    private readonly IPoseLibraryService? _poses;
    private readonly ISceneImageRepository? _sceneImages;
    private readonly ISceneImageStorageService? _sceneImageStorage;

    public MediaEditReferenceResolver(
        ISceneAssetRepository assets,
        ISceneAssetStorageService storage,
        IReferenceStrategyResolver strategies,
        ICharacterImageIdentityRepository? identity = null,
        ICharacterImageAssetStorageService? identityStorage = null,
        IPoseLibraryService? poses = null,
        ISceneImageRepository? sceneImages = null,
        ISceneImageStorageService? sceneImageStorage = null)
    {
        _assets = assets;
        _storage = storage;
        _strategies = strategies;
        _identity = identity;
        _identityStorage = identityStorage;
        _poses = poses;
        _sceneImages = sceneImages;
        _sceneImageStorage = sceneImageStorage;
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
                ReferenceChannel.PoseSkeleton => await ResolvePoseSkeletonAsync(
                    application, index + 1, cancellationToken),
                ReferenceChannel.AppImage => await ResolveAppImageAsync(
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
    /// <b>Pose is the one channel whose route is SURFACE-specific</b>, and that is not a duplicated decision path. A
    /// <c>Generate</c> carries a pose through the render's own pose reference, consolidated by
    /// <c>ImageStepPoseBinding</c> before the render is queued, so resolving it here as a reference image as well
    /// would send the same skeleton twice. An <c>Edit</c> has no render settings and no <c>ImageStepPoseBinding</c>
    /// consumer at all, so a reference image is its ONLY route — and declining it there is what left a bound pose
    /// silently inert on every edit.
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
            // Carried by the render's own pose reference on a GENERATE; the only route there is on an EDIT. The
            // skeleton IS read in a reference slot as pose guidance by Qwen-Image-2.1 (measured 2026-09-23), which is
            // the same mechanism the render's own native-pose route uses.
            ReferenceChannel.PoseSkeleton => surface == ReferenceStrategyCatalogue.ReferenceImageSurface.Edit
                ? channel
                : null,

            // No surface has a route for an unapproved render from this chain. Fail fast, with the channel named.
            ReferenceChannel.ScratchImage => throw new InvalidOperationException(
                $"Reference for '{binding.ElementKey}' is bound to a scratch image, which has no implementable route on "
                + $"this {ReferenceStrategyCatalogue.Label(surface)}. Bind an approved scene asset "
                + "(or the character's approved identity-pack image) instead."),

            _ => channel
        };
    }

    /// <summary>
    /// Whether this surface resolves a reference IMAGE for a binding — the SAME predicate
    /// <see cref="ResolveAsync"/> selects by, exposed so a caller can ask the capability question at ENQUEUE time
    /// instead of copying the rule (a second copy would drift, which is the failure this class exists to prevent).
    ///
    /// <para>
    /// False means the binding travels by another route and this resolver never touches it: a text-only binding
    /// carries no image, a scratch image is refused by name when it IS resolved, and a pose comes from the render's
    /// own pose reference on a GENERATE (see <see cref="ChannelOfFor"/>). True means <see cref="ResolveAsync"/> will
    /// resolve the strategy against the model's own declaration and qualifications, and refuse the run when it cannot.
    /// </para>
    /// </summary>
    public static bool CarriesReferenceImage(
        ReferenceStrategyCatalogue.ReferenceImageSurface surface, ReferenceApplicationSelection binding)
        => ChannelOfFor(surface, binding) is not null;

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
            ReferenceBindingShape.SlotKindOf(application),
            // The container this view lives in: what lets the role clause tell two views of one place from two
            // different places sharing a frame.
            application.SceneAssetId,
            // The operator's own statement about this reference, if they made one (B-159).
            application.Directions);
    }

    /// <summary>
    /// A RENDERED image the app already holds — a Studio production attempt, a gallery render, an edit — read out of
    /// the scene-image store by its own id.
    ///
    /// <para>
    /// It is re-validated against the checksum the binding recorded, because that checksum is the only thing that
    /// makes the binding exact: the row's bytes can change under it (a regenerate writes a sibling, a rejected-bytes
    /// purge clears them), so an image that no longer matches fails the run rather than conditioning on a picture
    /// nobody chose.
    /// </para>
    ///
    /// <para>
    /// Approval is deliberately NOT required here. This channel is how any image the operator can see becomes a
    /// reference, and an image's approval state is a PRODUCTION question, not a reproducibility one — which is why
    /// the approved-scene-asset channel, whose whole contract is an approved immutable selection, is not widened to
    /// carry these.
    /// </para>
    /// </summary>
    private async Task<MediaEditReference> ResolveAppImageAsync(
        ReferenceApplicationSelection application,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var images = _sceneImages
            ?? throw new InvalidOperationException(
                "A reference bound to one of the app's own images requires the scene image repository.");
        var storage = _sceneImageStorage
            ?? throw new InvalidOperationException(
                "A reference bound to one of the app's own images requires the scene image storage service.");

        var imageId = application.ReferenceImageId!.Trim();
        var image = await images.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Reference image '{imageId}' was not found, so it cannot condition this run.");

        if (image.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(image.FileRelativePath))
        {
            throw new InvalidOperationException(
                $"Reference image '{imageId}' has no stored bytes ({image.Status}), so it cannot condition this run.");
        }

        if (string.IsNullOrWhiteSpace(image.Sha256))
        {
            throw new InvalidOperationException(
                $"Reference image '{imageId}' has no recorded checksum, so the run cannot prove which bytes it would "
                + "condition on.");
        }

        if (!string.Equals(image.Sha256, application.ReferenceImageSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reference image '{imageId}' no longer matches the checksum this reference was bound with, so the run "
                + "would condition on different bytes than the operator chose. Bind the image again.");
        }

        var relativePath = image.FileRelativePath;
        return new MediaEditReference(
            ordinal,
            // The operator's own name for the image wins, exactly as the approved-asset channel behaves: a location's
            // several views are told apart only by a name, and the binding's snapshot of it is what the row held when
            // they bound it.
            DescribeReference(
                application.SemanticRole,
                string.IsNullOrWhiteSpace(image.DisplayName) ? application.ReferenceLabel : image.DisplayName),
            $"{image.Id}.png",
            image.Sha256,
            token => storage.OpenReadAsync(relativePath, token),
            ReferenceBindingShape.SlotKindOf(application),
            // A rendered image lives in no asset container, so the role clause can never mistake two of them for two
            // views of one place. The operator's own label is what tells them apart.
            AssetContainerId: null,
            // The operator's own statement about this reference, if they made one (B-159).
            application.Directions);
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
    /// A pose library skeleton, read BY PRESET ID — never from the path the binding happens to carry, so a stale path
    /// cannot make the run condition on a different pose than the one the operator picked. This is the edit surface's
    /// only pose route: the render path consolidates a pose into the render's own pose reference
    /// (<c>ImageStepPoseBinding</c>), which an edit has nowhere to put.
    ///
    /// <para>
    /// A binding that names no preset fails fast rather than being skipped: on an edit a pose slot's whole meaning is
    /// the skeleton, so a pose that resolves to nothing is a reference the model never receives.
    /// </para>
    /// </summary>
    private async Task<MediaEditReference> ResolvePoseSkeletonAsync(
        ReferenceApplicationSelection application,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var poses = _poses
            ?? throw new InvalidOperationException(
                "A reference bound to a pose-library skeleton requires the pose library service.");

        if (string.IsNullOrWhiteSpace(application.PosePresetId))
        {
            throw new InvalidOperationException(
                "A pose reference is bound to this step but names no pose library preset, so this editor has no "
                + "skeleton to condition on. Clear the step's pose slot or pick the pose again from the pose library "
                + "— a preset id is required because the skeleton is read by id, never by a path a stored payload "
                + "could redirect.");
        }

        var presetId = application.PosePresetId.Trim();
        byte[] bytes;
        try
        {
            bytes = await poses.ReadSkeletonAsync(presetId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                $"Pose library preset '{presetId}' could not be read, so its skeleton cannot condition this edit: "
                + exception.Message, exception);
        }

        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Pose library preset '{presetId}' has no skeleton bytes, so it cannot condition this edit. "
                + "Re-import or re-render that pose in the pose library.");
        }

        var fileName = string.IsNullOrWhiteSpace(application.SkeletonRelativePath)
            ? $"{presetId}.png"
            : Path.GetFileName(application.SkeletonRelativePath);

        return new MediaEditReference(
            ordinal,
            $"pose reference ({presetId} library skeleton)",
            fileName,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
            ReferenceBindingShape.SlotKindOf(application),
            // A skeleton lives in no asset container, so the role clause can never mistake two poses for two views of
            // one place.
            AssetContainerId: null);
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
