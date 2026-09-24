using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Optional pose conditioning for a generated asset image: which VERIFIED stance skeleton drives the render, and how
/// strongly. Only the stances in <see cref="BodyStanceSkeletons.Available"/> are accepted — the rest are poses this
/// stack has measured the model re-posing, so conditioning on one would not hold.
/// </summary>
public sealed record SceneAssetPoseConditioning(BodyReferenceStance Stance, double Strength);

/// <summary>/// The approved identity reference a generated asset image is conditioned on. The caller resolves and validates the
/// pack; the render path re-reads BOTH the pack and the asset before using them, so an image can never be conditioned
/// on a reference that was unapproved, superseded or deleted in between the queue and the render.
/// </summary>
public sealed record SceneAssetIdentityConditioning(string PackId, string FaceAssetId);

/// <summary>
/// A canonical body angle asked for as a RENDER rather than as an edit: the accepted body image supplies the build
/// and the committed angle skeleton supplies the turn (measured 2026-09-23 — cases body-angle-*, runbook
/// <c>specs/image-generator-tests/qwen-21-native-reference/RUNBOOK.md</c>). The source is named by IMAGE id: the
/// render path re-reads the image and its bytes, so a source that was deleted between the queue and the render fails
/// the render instead of quietly rendering from something else.
///
/// Measured, not assumed: without the accepted body the model invents a different build, and adding the face
/// reference on top changes nothing (mean absolute pixel difference 2.89/255).
/// </summary>
public sealed record SceneAssetBodyAngleConditioning(SceneImageReferenceBodyView View, string SourceImageId);

/// <summary>
/// Everything a generated asset image needs beyond its prompt, model and size.
///
/// ONE object rather than a growing list of optional parameters: the negative, the compiler provenance and the two
/// conditionings travel as a set, and a positional list made every addition a change to every caller and every test
/// double — three times over in one session.
/// </summary>
public sealed record SceneAssetImageGenerationOptions
{
    /// <summary>
    /// Null when no compiler authored the prompt (the render path compiles the description instead). The EMPTY string
    /// means the author deliberately chose an empty negative — a different fact from "there is none".
    /// </summary>
    public string? NegativePrompt { get; init; }

    /// <summary>The compiler that authored the prompt, or null when it is still a semantic description.</summary>
    public string? PromptCompilerId { get; init; }

    public SceneAssetPoseConditioning? Pose { get; init; }

    public SceneAssetIdentityConditioning? Identity { get; init; }

    /// <summary>A canonical angle rendered from an accepted body image, or null for a render that starts from text.</summary>
    public SceneAssetBodyAngleConditioning? BodyAngle { get; init; }
}

/// <summary>/// Orchestration surface for the app-wide asset library (Asset Studio). Creates assets by prompt or
/// upload, enqueues Qwen edits and the special profile-pack function, and provides list/view/
/// download/delete operations. The UI talks to this service, never to the repository or storage.
/// </summary>
public interface ISceneAssetService
{
    Task<SceneAsset> CreateAssetAsync(
        string name,
        SceneAssetType type,
        string? characterProfileId = null,
        CancellationToken cancellationToken = default);

    Task<SceneAssetImage> AddGeneratedImageAsync(
        string assetId,
        string prompt,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null,
        string? candidateBatchId = null,
        SceneAssetImageGenerationOptions? options = null);

    Task<SceneAssetImage> AddUploadedImageAsync(
        string assetId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default,
        string? candidateBatchId = null);

    /// <summary>
    /// Store an image produced from another image by a deterministic, in-process operation (no model), with the
    /// operation and the source's checksum recorded in the image's own provenance. The image is Complete when
    /// this returns: the bytes are already in hand, so nothing is queued. A model-backed operation belongs on
    /// the queue instead.
    /// </summary>
    Task<SceneAssetImage> AddDerivedImageAsync(
        string assetId,
        string sourceImageId,
        MediaEditOperationKind operation,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default,
        string? candidateBatchId = null);

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

    /// <summary>
    /// Store an image's eye-gate result on that image (B-121 note 001). The verdict describes the image,
    /// so it is written beside the image's own metadata rather than on the build that produced it.
    /// </summary>
    Task SetImageValidationResultAsync(
        string imageId,
        string? validationResultJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the front-pipeline steps that produced an image (B-121 note 002). Like the validation result,
    /// this describes the image itself, so it is written beside the image rather than on the build.
    /// </summary>
    Task SetImagePipelineStepsAsync(
        string imageId,
        string? pipelineStepsJson,
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
