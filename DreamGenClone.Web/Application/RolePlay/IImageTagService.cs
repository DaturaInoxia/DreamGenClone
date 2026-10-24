using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Which store an image's tags live in. The two are different rows in different stores, so an id is only
/// meaningful inside the one that issued it.</summary>
public enum ImageTagTarget
{
    /// <summary>An image in the asset library (a <c>SceneAssetImage</c>).</summary>
    AssetLibrary = 1,

    /// <summary>A rendered image (a <c>SceneImageRecord</c>) — a Studio attempt, a gallery render, an edit.</summary>
    Rendered = 2
}

/// <summary>
/// The IMAGE-tag surface of the app: reading, editing and searching the tags an image carries (B-140 D2).
/// </summary>
///
/// <para>
/// A narrow interface of its own rather than two more members on <see cref="ISceneAssetService"/>, and that is a design
/// choice rather than tidiness: tagging is a self-contained capability with exactly two consumers (the tag editor beside
/// an image, and the reference-image search), and every host that wants it should not have to depend on — or stub — the
/// whole asset lifecycle to get it.
/// </para>
///
/// <para>
/// <b>Both stores go through this one surface.</b> A rendered image is as findable as a library one — the operator
/// judges both in a review deck and looks for both by the same words — so the tag editor asks this service and names
/// the store, rather than each surface picking its own writer. The catalog (<c>ImageTagCatalog</c>) remains the single
/// owner of the VOCABULARY: the caller supplies words, never JSON, and a tag outside the catalog is refused by name
/// rather than stored in a shape no search could match.
/// </para>
/// </summary>
public interface IImageTagService
{
    /// <summary>
    /// Replaces an image's tags and returns the normalized list that was stored — the list the caller should now show,
    /// rather than the one it sent.
    /// </summary>
    Task<IReadOnlyList<string>> SetTagsAsync(
        ImageTagTarget target,
        string imageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Images in the ASSET LIBRARY whose tag list matches what the operator typed — the reference-image search. Matched
    /// on the tag's VALUE, so "kneeling" finds <c>stance:kneeling</c> without matching a character whose name happens to
    /// contain the word.
    /// </summary>
    /// <remarks>
    /// The asset library's search, deliberately: it answers "which reference images can I bind", and a rendered image
    /// is not one until it is approved and promoted. Rendered images carry tags so the review deck can show and edit
    /// them; widening this search to include them is a separate decision, not an omission.
    /// </remarks>
    Task<IReadOnlyList<SceneAssetImage>> SearchImagesByTagAsync(
        string tagQuery,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}
