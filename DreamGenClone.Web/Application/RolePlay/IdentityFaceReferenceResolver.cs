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
