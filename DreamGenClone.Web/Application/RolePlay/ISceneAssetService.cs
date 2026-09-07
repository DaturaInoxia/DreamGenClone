using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Orchestration surface for the app-wide asset library (Asset Studio). Creates assets by prompt or
/// upload, enqueues Qwen edits and the special profile-pack function, and provides list/view/
/// download/delete operations. The UI talks to this service, never to the repository or storage.
/// </summary>
public interface ISceneAssetService
{
    Task<SceneAsset> CreateAssetAsync(
        string name,
        SceneAssetType type,
        CancellationToken cancellationToken = default);

    Task<SceneAssetImage> AddGeneratedImageAsync(
        string assetId,
        string prompt,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null,
        string? candidateBatchId = null);

    Task<SceneAssetImage> AddUploadedImageAsync(
        string assetId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<SceneAssetImage> EnqueueImageEditAsync(
        string assetId,
        string sourceImageId,
        string editPrompt,
        string modelId,
        CancellationToken cancellationToken = default,
        string? candidateBatchId = null,
        IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null);

    Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(
        string assetId, CancellationToken cancellationToken = default);

    Task<SceneAssetImage?> GetImageAsync(
        string imageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(
        string candidateBatchId, CancellationToken cancellationToken = default);

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

    Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
        string imageId, CancellationToken cancellationToken = default);

    Task<SceneAsset> CreateFromPromptAsync(
        string name,
        string prompt,
        SceneAssetType type,
        string modelId,
        string imageSize,
        string? candidateBatchId = null,
        CancellationToken cancellationToken = default);

    Task<SceneAsset> CreateFromUploadAsync(
        string name,
        SceneAssetType type,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<SceneAsset> EnqueueEditAsync(
        string sourceAssetId,
        string name,
        string editPrompt,
        string modelId,
        CancellationToken cancellationToken = default);

    Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default);

    Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default);

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

    /// <summary>Open a complete asset's stored bytes for viewing/downloading.</summary>
    Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(
        string assetId, CancellationToken cancellationToken = default);

    Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default);
}
