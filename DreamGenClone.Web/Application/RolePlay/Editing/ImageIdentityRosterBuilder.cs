using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE projection from an identity owner to a character the identity tab can bind faces to (B-127:
/// the owner is the character template, never the scenario instance). Every store's roster applies the
/// same rule — exactly one approved pack, carrying approved face references — so the two rosters differ
/// only in WHICH owners they enumerate, never in what makes an owner eligible.
/// </summary>
internal static class ImageIdentityRosterBuilder
{
    /// <summary>
    /// Builds the choice for one identity owner, or the reason it cannot be bound.
    ///
    /// The caller's id is resolved to the character TEMPLATE first (B-127). That resolution is not optional:
    /// packs belong to the template, so a scenario character id read directly against the pack store returns
    /// nothing, and the roster used to report "no approved identity pack" for characters that do have one
    /// (reported 2026-09-24 from the scene-image editor: Becky's approved v8 pack lives under her template while
    /// the roster asked for her scenario instance).
    /// </summary>
    /// <returns>
    /// The choice, or null with <c>Reason</c> set to why not — the resolver's own refusal when the character has
    /// no template, otherwise which eligibility rule failed. A listing reports the reason rather than throwing,
    /// because one unlinked character must not take out a roster that other characters satisfy.
    /// </returns>
    internal static async Task<(ImageIdentityCharacterChoice? Choice, string? Reason)> TryBuildChoiceAsync(
        ICharacterImageIdentityService identity,
        ICharacterIdentityOwnerResolver owners,
        string ownerId,
        string ownerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return (null, null);
        }

        string templateId;
        try
        {
            templateId = (await owners.ResolveAsync(ownerId, cancellationToken)).TemplateId;
        }
        catch (InvalidOperationException refusal)
        {
            // The resolver already says what to do (link the character to its template), so it is repeated verbatim.
            return (null, refusal.Message);
        }

        var packs = await identity.ListPacksAsync(templateId, cancellationToken);
        var approved = packs
            .Where(pack => pack.Status == CharacterImageIdentityPackStatus.Approved)
            .OrderByDescending(pack => pack.Version)
            .ToArray();
        var name = string.IsNullOrWhiteSpace(ownerName) ? ownerId : ownerName;
        if (approved.Length != 1)
        {
            return (null, approved.Length == 0
                ? $"Character '{name}' has no approved identity pack."
                : $"Character '{name}' has multiple approved identity packs; exactly one is required.");
        }

        var pack = approved[0];
        var assets = await identity.ListAssetsAsync(pack.Id, cancellationToken);
        var faces = assets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.Face
                && asset.IsApproved
                && asset.FaceView.HasValue
                && !string.IsNullOrWhiteSpace(asset.FileRelativePath)
                && !string.IsNullOrWhiteSpace(asset.Sha256))
            .OrderBy(asset => asset.FaceView)
            .ToList();
        if (faces.Count == 0)
        {
            return (null,
                $"Character '{name}' approved identity pack v{pack.Version} has no approved face reference "
                + "with an angle tag, a stored file and a checksum.");
        }

        return (new ImageIdentityCharacterChoice(
            templateId,
            name,
            pack.Id,
            pack.Version,
            faces.Any(face => string.Equals(face.Id, pack.CanonicalFaceAssetId, StringComparison.Ordinal))
                ? pack.CanonicalFaceAssetId!
                : faces[0].Id,
            faces), null);
    }
}
