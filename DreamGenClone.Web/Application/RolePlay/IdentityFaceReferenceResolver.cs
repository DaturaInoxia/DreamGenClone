using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The approved face an identity conditioned render contributes, with the exact metadata that makes the
/// reference auditable (which pack version it came from, which asset, and its checksum).
/// </summary>
public sealed record ResolvedIdentityFaceReference(
    int Ordinal,
    string PackId,
    int PackVersion,
    string FaceAssetId,
    SceneImageReferenceFaceView? FaceView,
    string FileRelativePath,
    string Sha256);

/// <summary>
/// Resolves the APPROVED face reference a pack contributes, for every mechanism that conditions on a face.
/// This decision ("which approved face does this pack give us, and is it still valid") was inline in the
/// asset generation handler; the scene render path needs exactly the same answer, so it lives here once
/// rather than twice — the same lesson <see cref="Editing.MediaEditReferenceResolver"/> was extracted for.
///
/// Nothing is substituted: an exact face the caller named must be an approved face of that pack, and a
/// pack the caller named must own a canonical face. Every failure names what is wrong and what to do.
/// </summary>
public sealed class IdentityFaceReferenceResolver
{
    private readonly ICharacterImageIdentityRepository _identity;

    public IdentityFaceReferenceResolver(ICharacterImageIdentityRepository identity)
    {
        _identity = identity;
    }

    /// <summary>
    /// The face the caller explicitly selected (e.g. a body panel pick of a specific view). Fails when the
    /// asset is missing, belongs to another pack, is not a face, or is not approved.
    /// </summary>
    public async Task<ResolvedIdentityFaceReference> ResolveExactFaceAsync(
        int ordinal,
        string packId,
        string faceAssetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(faceAssetId))
        {
            throw new InvalidOperationException(
                "Identity conditioning requires an exact approved face asset id; none was provided.");
        }

        var pack = await RequireApprovedPackAsync(packId, cancellationToken);
        return await ResolveFaceAsync(ordinal, pack, faceAssetId.Trim(), cancellationToken);
    }

    /// <summary>
    /// The pack's own canonical face, for callers that selected a PACK rather than a face (the studio's
    /// identity pack selections). A pack without an approved canonical face fails rather than falling back
    /// to some other asset in the pack.
    /// </summary>
    public async Task<ResolvedIdentityFaceReference> ResolveCanonicalFaceAsync(
        int ordinal,
        string packId,
        CancellationToken cancellationToken = default)
    {
        var pack = await RequireApprovedPackAsync(packId, cancellationToken);
        if (string.IsNullOrWhiteSpace(pack.CanonicalFaceAssetId))
        {
            throw new InvalidOperationException(
                $"Identity pack '{pack.Id}' v{pack.Version} has no canonical face asset, so it cannot condition a render. "
                + "Approve a canonical face for the pack first.");
        }

        return await ResolveFaceAsync(ordinal, pack, pack.CanonicalFaceAssetId, cancellationToken);
    }

    private async Task<CharacterImageIdentityPack> RequireApprovedPackAsync(
        string packId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            throw new InvalidOperationException("Identity conditioning requires an exact identity pack id; none was provided.");
        }

        var pack = await _identity.GetPackAsync(packId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Identity pack '{packId}' was not found, so this render cannot be identity-conditioned. "
                + "Approve a face reference for this character and generate again.");
        if (pack.Status != CharacterImageIdentityPackStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Identity pack '{pack.Id}' is '{pack.Status}', not Approved. Only an approved pack may condition a "
                + "render — an unapproved reference would put an unreviewed identity into a render.");
        }

        return pack;
    }

    private async Task<ResolvedIdentityFaceReference> ResolveFaceAsync(
        int ordinal,
        CharacterImageIdentityPack pack,
        string faceAssetId,
        CancellationToken cancellationToken)
    {
        var asset = await _identity.GetAssetAsync(faceAssetId, cancellationToken)
            ?? throw new InvalidOperationException($"The identity face asset '{faceAssetId}' was not found.");
        if (!string.Equals(asset.IdentityPackId, pack.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Face asset '{asset.Id}' belongs to pack '{asset.IdentityPackId}', not '{pack.Id}', so it cannot "
                + "condition this render.");
        }

        if (asset.AssetKind != SceneImageReferenceAssetKind.Face || !asset.IsApproved)
        {
            throw new InvalidOperationException(
                $"Face asset '{asset.Id}' must be an APPROVED face to condition a render, but it is "
                + $"'{asset.AssetKind}'{(asset.IsApproved ? string.Empty : " and not approved")}.");
        }

        if (string.IsNullOrWhiteSpace(asset.FileRelativePath) || string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new InvalidOperationException(
                $"Face asset '{asset.Id}' has no stored file or checksum, so it cannot be sent as a reference.");
        }

        return new ResolvedIdentityFaceReference(
            ordinal,
            pack.Id,
            pack.Version,
            asset.Id,
            asset.FaceView,
            asset.FileRelativePath,
            asset.Sha256);
    }
}

/// <summary>
/// The approved BODY reference a pack contributes, with the metadata that makes it auditable: which pack
/// version, which asset, which canonical slot and state it claims, and its checksum.
///
/// The slot and the state travel with the resolution because they are the two facts the caller decided on
/// (a clothed cell must not be conditioned on an unclothed reference — measured 2026-09-23, when bare-shouldered
/// references made a clothed render come out unclothed), so they must be re-asserted here rather than inferred.
/// </summary>
public sealed record ResolvedIdentityBodyReference(
    int Ordinal,
    string PackId,
    int PackVersion,
    string BodyAssetId,
    SceneImageReferenceBodyView? BodyView,
    SceneImageReferenceBodyState? BodyState,
    string FileRelativePath,
    string Sha256);

/// <summary>
/// Resolves the APPROVED full-body reference a pack contributes — the sibling of
/// <see cref="IdentityFaceReferenceResolver"/> for the body-build axis. Deliberately a separate class rather
/// than a second method on the face resolver: the face path conditions every identity render in the app, and
/// rewriting it to share these checks would put a proven path at risk for a naming win. The two therefore
/// mirror each other on purpose.
///
/// Nothing is substituted: an asset the caller named must be an approved full-body asset of that approved
/// pack, and it must declare the canonical view and state the cell asked for. Every failure names what is
/// wrong and what to do — a body reference that cannot resolve must fail the render, never quietly leave the
/// build to the model's imagination.
/// </summary>
public sealed class IdentityBodyReferenceResolver
{
    private readonly ICharacterImageIdentityRepository _identity;

    public IdentityBodyReferenceResolver(ICharacterImageIdentityRepository identity)
    {
        _identity = identity;
    }

    public async Task<ResolvedIdentityBodyReference> ResolveExactBodyAsync(
        int ordinal,
        string packId,
        string bodyAssetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bodyAssetId))
        {
            throw new InvalidOperationException(
                "Body reference conditioning requires an exact approved full-body asset id; none was provided.");
        }

        if (string.IsNullOrWhiteSpace(packId))
        {
            throw new InvalidOperationException(
                "Body reference conditioning requires an exact identity pack id; none was provided.");
        }

        var pack = await _identity.GetPackAsync(packId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Identity pack '{packId}' was not found, so this render cannot be conditioned on the character's "
                + "body. Approve a body reference for this character and generate again.");
        if (pack.Status != CharacterImageIdentityPackStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Identity pack '{pack.Id}' is '{pack.Status}', not Approved. Only an approved pack may condition a "
                + "render — an unapproved reference would put an unreviewed body into a render.");
        }

        var asset = await _identity.GetAssetAsync(bodyAssetId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"The identity body asset '{bodyAssetId}' was not found.");
        if (!string.Equals(asset.IdentityPackId, pack.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Body asset '{asset.Id}' belongs to pack '{asset.IdentityPackId}', not '{pack.Id}', so it cannot "
                + "condition this render.");
        }

        if (asset.AssetKind != SceneImageReferenceAssetKind.FullBody || !asset.IsApproved)
        {
            throw new InvalidOperationException(
                $"Body asset '{asset.Id}' must be an APPROVED full-body reference to condition a render, but it is "
                + $"'{asset.AssetKind}'{(asset.IsApproved ? string.Empty : " and not approved")}.");
        }

        if (asset.BodyView is null || asset.BodyState is null)
        {
            throw new InvalidOperationException(
                $"Body asset '{asset.Id}' declares no canonical view and state, so it cannot be matched to a coverage "
                + "cell. Set the body slot and state on the body card first.");
        }

        if (string.IsNullOrWhiteSpace(asset.FileRelativePath) || string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new InvalidOperationException(
                $"Body asset '{asset.Id}' has no stored file or checksum, so it cannot be sent as a reference.");
        }

        return new ResolvedIdentityBodyReference(
            ordinal,
            pack.Id,
            pack.Version,
            asset.Id,
            asset.BodyView,
            asset.BodyState,
            asset.FileRelativePath,
            asset.Sha256);
    }
}
