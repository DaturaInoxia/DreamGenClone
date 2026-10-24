using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The one place that decides which store an image's tags live in, and the one place that normalizes them.
/// </summary>
/// <remarks>
/// <para>
/// It exists so a tag editor can be handed an image id and a target rather than a store: two stores hold tags now (the
/// asset library and the rendered-image store), and a component that had to choose between two writers would be the
/// second copy of this decision. The catalog stays the owner of the vocabulary; this class only routes.
/// </para>
///
/// <para>
/// Neither repository is reached from the Web layer directly — each store keeps its own write — so there is still ONE
/// owner of the rules per store, and ONE owner of which store serves a target.
/// </para>
/// </remarks>
public sealed class ImageTagService : IImageTagService
{
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneImageRepository _rendered;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImageTagService> _logger;

    public ImageTagService(
        ISceneAssetRepository assets,
        ISceneImageRepository rendered,
        TimeProvider timeProvider,
        ILogger<ImageTagService> logger)
    {
        _assets = assets;
        _rendered = rendered;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SetTagsAsync(
        ImageTagTarget target,
        string imageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("An image id is required to set tags.");
        }

        ArgumentNullException.ThrowIfNull(tags);

        // Normalized ONCE, here, for both stores: an empty result means the tags were cleared, which is a real edit
        // rather than an error.
        var normalized = ImageTagCatalog.NormalizeAll(tags);

        switch (target)
        {
            case ImageTagTarget.AssetLibrary:
                await _assets.SetImageTagsAsync(imageId.Trim(), normalized, cancellationToken);
                break;

            case ImageTagTarget.Rendered:
                await _rendered.SetTagsAsync(
                    imageId.Trim(), normalized, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                break;

            default:
                throw new InvalidOperationException(
                    $"Image tag target '{target}' names no store, so its tags cannot be written. Add the store "
                    + "deliberately rather than letting a target fall through to another one's rows.");
        }

        _logger.LogInformation(
            "Image tags set: Target={Target}, ImageId={ImageId}, Count={Count}", target, imageId, normalized.Count);
        return normalized;
    }

    public async Task<IReadOnlyList<SceneAssetImage>> SearchImagesByTagAsync(
        string tagQuery,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
        => await _assets.SearchImagesByTagAsync(
            ImageTagCatalog.Normalize(tagQuery), maxResults, cancellationToken);
}
