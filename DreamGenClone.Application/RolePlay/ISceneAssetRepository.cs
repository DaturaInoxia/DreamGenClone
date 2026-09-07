using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// SQLite persistence for the app-wide scene asset library. Follows the self-contained repository
/// pattern used by the other scene-image repositories. Assets are free-floating (not scoped to a
/// character) so the same library can back identity packs, locations, and wardrobe packs.
/// </summary>
public interface ISceneAssetRepository
{
    Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListByCandidateBatchAsync(
        string candidateBatchId, CancellationToken cancellationToken = default);

    Task<SceneAssetImage?> GetImageAsync(
        string imageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(
        string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(
        string candidateBatchId, CancellationToken cancellationToken = default);

    Task UpsertImageAsync(
        SceneAssetImage image, CancellationToken cancellationToken = default);

    Task SetImageCandidateDecisionAsync(
        string imageId,
        SceneAssetCandidateDecision decision,
        string? notes,
        CancellationToken cancellationToken = default);

    Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default);

    Task<SceneAssetImage> ApproveImageForProductionAsync(
        string imageId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default);

    /// <summary>Insert a new asset or update mutable fields (status, file metadata, error).</summary>
    Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default);

    Task UpdateCandidateFieldsAsync(
        string assetId,
        string? candidateBatchId,
        SceneAssetCandidateDecision? candidateDecision,
        string? candidateNotes,
        string? candidateSourceAssetId,
        CancellationToken cancellationToken = default);

    Task<SceneAsset> ApproveForProductionAsync(
        string assetId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default);

    Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default);

    Task DeleteAsync(string assetId, CancellationToken cancellationToken = default);

    /// <summary>Count assets that reference a file path (delete guard for shared files).</summary>
    Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default);
}
