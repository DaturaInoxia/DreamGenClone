using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>Reaches Asset Manager images. The only asset-specific part of the edit pipeline.</summary>
public sealed class SceneAssetMediaEditSubjectSource : IMediaEditSubjectSource
{
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;

    public SceneAssetMediaEditSubjectSource(ISceneAssetRepository assets, ISceneAssetStorageService storage)
    {
        _assets = assets;
        _storage = storage;
    }

    public MediaEditSubjectKind Kind => MediaEditSubjectKind.AssetImage;

    public async Task<MediaEditSourceImage> RequireSourceAsync(
        MediaEditSubjectRef subject, string sourceImageId, CancellationToken cancellationToken = default)
    {
        if (subject.Kind != MediaEditSubjectKind.AssetImage)
            throw new InvalidOperationException($"The asset image source cannot serve subject kind '{subject.Kind}'.");
        if (string.IsNullOrWhiteSpace(subject.SubjectId) || string.IsNullOrWhiteSpace(sourceImageId))
            throw new InvalidOperationException("An asset id and a source image id are required.");

        var asset = await _assets.GetAsync(subject.SubjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{subject.SubjectId}' was not found.");
        var image = await _assets.GetImageAsync(sourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{sourceImageId}' was not found.");
        if (!string.Equals(image.AssetId, asset.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Only a complete stored image owned by the selected asset can be edited.");
        if (image.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(image.FileRelativePath))
            throw new InvalidOperationException("Only a complete stored image owned by the selected asset can be edited.");

        return new MediaEditSourceImage(image.Id, image.FileRelativePath, image.Sha256);
    }

    public Task<Stream> OpenReadAsync(string fileRelativePath, CancellationToken cancellationToken = default)
        => _storage.OpenReadAsync(fileRelativePath, cancellationToken);
}
