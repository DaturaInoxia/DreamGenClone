using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Orchestrates character identity pack curation: creates drafts, ingests reference assets,
/// records provenance/consent, approves/supersedes versions, and deletes with file-reference
/// guards. The UI talks to this service, never to the repository or storage directly.
/// </summary>
public interface ICharacterImageIdentityService
{
    Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        string packId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the existing draft, create v1 with this scope when no packs exist, or throw if the character has
    /// only frozen versions. The scope is required persisted data (never a code default): a draft is neither
    /// narrowed nor raised by creating it again, and raising a <c>FaceOnly</c> draft to <c>BodyComplete</c> is the
    /// explicit <see cref="SetDraftPackScopeAsync"/> call.
    /// </summary>
    Task<CharacterImageIdentityPack> CreateDraftPackAsync(
        string characterProfileId,
        CharacterImageIdentityPackScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises a DRAFT pack's scope (<c>FaceOnly</c> to <c>BodyComplete</c>) and records the canonical full-body
    /// asset id — the unclothed <c>Front</c> full-body reference belonging to that same pack. An approved pack is
    /// refused rather than mutated: supersede it first, which is what keeps an approved pack immutable.
    /// </summary>
    Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(
        string packId,
        CharacterImageIdentityPackScope scope,
        string? canonicalFullBodyAssetId,
        CancellationToken cancellationToken = default);

    Task<CharacterImageIdentityPack> ApprovePackAsync(
        string packId,
        string descriptorSnapshotJson,
        string canonicalFaceAssetId,
        CancellationToken cancellationToken = default);

    Task<CharacterImageIdentityPack> SupersedePackAsync(string packId, CancellationToken cancellationToken = default);

    Task DeletePackAsync(string packId, CancellationToken cancellationToken = default);

    Task<SceneImageReferenceAsset> UploadAssetAsync(
        string packId,
        SceneImageReferenceAssetKind kind,
        string fileName,
        Stream content,
        SceneImageReferenceFaceView? faceView = null,
        SceneImageReferenceBodyView? bodyView = null,
        SceneImageReferenceBodyState? bodyState = null,
        CancellationToken cancellationToken = default);

    Task SetAssetProvenanceAsync(
        string assetId,
        string sourceLabel,
        SceneImageReferenceConsentState consentState,
        CancellationToken cancellationToken = default);

    Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default);

    /// <summary>Set the non-blocking quality rating + notes on a draft-pack asset (informational only).</summary>
    Task SetAssetQualityAsync(
        string assetId,
        SceneImageReferenceQuality quality,
        string qualityNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Re-run automatic quality analysis on an asset and persist the rating + reasons.</summary>
    Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(
        string assetId, CancellationToken cancellationToken = default);

    Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default);
}
