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

    /// <summary>
    /// Writes a compiled prompt onto an image row that already exists, with the compiler that authored it.
    /// </summary>
    /// <remarks>
    /// Needed because <see cref="UpsertImageAsync"/> never updates <c>Prompt</c> (a prompt is what an image WAS made
    /// from, so an ordinary upsert must not rewrite it). The wardrobe tab relies on the distinction: it creates the row
    /// the moment the operator asks for an image — so there is something to see, and something to attach a failure to -
    /// and the prompt-drafting job fills the text in afterwards.
    /// </remarks>
    Task SetImagePromptAsync(
        string imageId,
        string prompt,
        string promptCompilerId,
        string? negativePrompt,
        string? associationMetadataJson,
        CancellationToken cancellationToken = default);

    Task SetImageCandidateDecisionAsync(
        string imageId,
        SceneAssetCandidateDecision decision,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an image's tag list. The ONE writer of <c>TagsJson</c>, called by the tag editor; the completion step
    /// uses <see cref="AddImageTagsAsync"/> so it cannot erase a tag that was added while the render ran.
    ///
    /// <para>
    /// A narrow UPDATE rather than an upsert, for the same reason <see cref="SetImagePromptAsync"/> is: tags are
    /// edited by hand, and an ordinary save of the image row must not be able to overwrite them. Tags are supplied
    /// already in catalog shape (<c>prefix:value</c>) by <c>ImageTagCatalog</c>, which owns the vocabulary and the
    /// normalization — this layer stores the list and does not decide what a tag means.
    /// </para>
    /// </summary>
    Task SetImageTagsAsync(
        string imageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ADDS tags to an image, keeping the ones it already carries, and returns the resulting list.
    ///
    /// <para>
    /// This is what a completed render calls. Tags are the one field on an image that can be edited while a render is
    /// still running — the row exists from the moment it is queued — so the completion step unions rather than
    /// replaces: a render that overwrote the list would silently delete a tag an operator had just added, and a tag is
    /// exactly the kind of fact nobody notices going missing.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> AddImageTagsAsync(
        string imageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the operator-entered name of an image and returns the updated row. The ONE writer of <c>DisplayName</c>.
    ///
    /// <para>
    /// A narrow UPDATE rather than an upsert, for the same reason <see cref="SetImageTagsAsync"/> is: the name is
    /// typed by a person, and every other save of the image row (a render completing, an edit stage writing back the
    /// row it produced) must not be able to erase it. A blank name is refused — an unnamed accepted location image is
    /// the state the column exists to prevent, so no path produces one.
    /// </para>
    ///
    /// <para>
    /// Implemented by <c>SceneAssetRepository</c>. The default REFUSES loudly rather than doing nothing, so a store
    /// that cannot honour a naming request says so instead of appearing to succeed.
    /// </para>
    /// </summary>
    Task<SceneAssetImage> SetImageDisplayNameAsync(
        string imageId,
        string displayName,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"'{nameof(SetImageDisplayNameAsync)}' is not implemented by {GetType().Name}, so this image cannot be named.");

    /// <summary>
    /// Every image whose tag list contains a tag matching <paramref name="tagQuery"/>, newest first.
    ///
    /// <para>
    /// The query is matched against the tag VALUE, not the raw JSON text: the caller normalizes what the operator
    /// typed (see <c>ImageTagCatalog.Normalize</c>) and this matches <c>prefix:value</c> pairs whose value
    /// contains it, so typing "kneel" finds <c>stance:kneeling</c> and typing a character's name does not match a
    /// stance. An empty query yields nothing rather than the whole library — a search that returns everything when
    /// asked for nothing is a list, not a search.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SceneAssetImage>> SearchImagesByTagAsync(
        string tagQuery,
        int maxResults = 200,
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

    /// <summary>
    /// The inverse of <see cref="ApproveImageForProductionAsync"/>: takes an image back out of production so it stops
    /// being offered as a reference and becomes deletable again. Approval is what a reference picker reads and what
    /// blocks <see cref="DeleteImageAsync"/>, so without this an image approved by mistake could never be removed.
    ///
    /// <para>
    /// Implemented by <c>SceneAssetRepository</c>. The default REFUSES loudly rather than doing nothing, so a store
    /// that cannot honour the request says so instead of appearing to have taken the image out of production.
    /// </para>
    /// </summary>
    Task<SceneAssetImage> RevokeImageApprovalAsync(
        string imageId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"'{nameof(RevokeImageApprovalAsync)}' is not implemented by {GetType().Name}, so this image cannot be taken out of production.");

    /// <summary>Insert a new asset or update mutable fields (status, file metadata, error).</summary>
    Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default);

    Task UpdateCandidateFieldsAsync(
        string assetId,
        string? candidateBatchId,
        SceneAssetCandidateDecision? candidateDecision,
        string? candidateNotes,
        string? candidateSourceAssetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an asset. The name is the ONLY field the asset workspace lets an operator set directly: everything else
    /// about an asset is produced by a render or set by an approval, and a delete is irreversible.
    /// </summary>
    Task RenameAsync(string assetId, string name, CancellationToken cancellationToken = default);

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
