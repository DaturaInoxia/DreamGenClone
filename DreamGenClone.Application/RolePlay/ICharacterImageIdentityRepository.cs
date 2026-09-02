using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// SQLite persistence for character image identity packs and their reference assets. Follows the
/// self-contained repository pattern used by the other scene-image repositories. Approval,
/// versioning, and delete-guard rules are enforced here so the UI and render compiler cannot
/// bypass them.
/// </summary>
public interface ICharacterImageIdentityRepository
{
    // ---- Pack lifecycle ----

    Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    /// <summary>Insert a new draft pack or update the mutable fields of an existing draft.</summary>
    Task<CharacterImageIdentityPack> UpsertDraftAsync(
        CharacterImageIdentityPack pack, CancellationToken cancellationToken = default);

    /// <summary>
    /// Freeze a draft pack into <see cref="CharacterImageIdentityPackStatus.Approved"/>. Requires a
    /// non-empty descriptor snapshot, an approved canonical face asset in this pack, and provenance
    /// plus confirmed/not-applicable consent on every asset.
    /// </summary>
    Task<CharacterImageIdentityPack> ApproveAsync(
        string packId,
        string descriptorSnapshotJson,
        string canonicalFaceAssetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retire an approved pack and create a new draft version carrying forward its descriptor and
    /// assets. Returns the new draft pack.
    /// </summary>
    Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>Delete a draft pack and its assets. Approved or superseded packs cannot be deleted.</summary>
    Task DeletePackAsync(string packId, CancellationToken cancellationToken = default);

    // ---- Asset lifecycle ----

    Task AddAssetAsync(SceneImageReferenceAsset asset, CancellationToken cancellationToken = default);

    Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        string packId, CancellationToken cancellationToken = default);

    Task UpdateAssetProvenanceAsync(
        string assetId,
        string sourceLabel,
        SceneImageReferenceConsentState consentState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set an asset's approval flag. Approving requires non-empty provenance and a non-unknown
    /// consent state.
    /// </summary>
    Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default);

    /// <summary>Set the non-blocking quality rating + notes on a draft-pack asset (informational only).</summary>
    Task UpdateAssetQualityAsync(
        string assetId,
        SceneImageReferenceQuality quality,
        string qualityNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Delete an asset belonging to a draft pack. Assets of frozen packs cannot be deleted.</summary>
    Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count assets that reference a file path. Used by the service layer to avoid deleting a shared
    /// reference file while any other asset still points at it.
    /// </summary>
    Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default);
}
