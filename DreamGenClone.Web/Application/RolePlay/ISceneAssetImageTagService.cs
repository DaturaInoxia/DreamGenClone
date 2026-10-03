using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The IMAGE-tag surface of the asset library: reading, editing and searching the tags an image carries (B-140 D2).
///
/// <para>
/// A narrow interface of its own rather than two more members on <see cref="ISceneAssetService"/>, and that is a design
/// choice rather than tidiness: tagging is a self-contained capability with exactly two consumers (the tag editor beside
/// an image, and the reference-image search), and every host that wants it should not have to depend on — or stub — the
/// whole asset lifecycle to get it. The implementation is the same <c>SceneAssetService</c>, so there is still ONE owner
/// of the rules.
/// </para>
///
/// <para>
/// The vocabulary is <c>ImageTagCatalog</c>'s: the caller supplies words, never JSON, and a tag outside the catalog is
/// refused by name rather than stored in a shape no search could match.
/// </para>
/// </summary>
public interface ISceneAssetImageTagService
{
    /// <summary>
    /// Replaces an image's tags and returns the normalized list that was stored — the list the caller should now show,
    /// rather than the one it sent.
    /// </summary>
    Task<IReadOnlyList<string>> SetImageTagsAsync(
        string imageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Images whose tag list matches what the operator typed — the reference-image search. Matched on the tag's VALUE,
    /// so "kneeling" finds <c>stance:kneeling</c> without matching a character whose name happens to contain the word.
    /// </summary>
    Task<IReadOnlyList<SceneAssetImage>> SearchImagesByTagAsync(
        string tagQuery,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}
