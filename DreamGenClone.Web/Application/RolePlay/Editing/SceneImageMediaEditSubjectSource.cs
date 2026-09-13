using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>Reaches role-play scene images. The only scene-specific part of the edit pipeline.</summary>
public sealed class SceneImageMediaEditSubjectSource : IMediaEditSubjectSource
{
    private readonly ISceneImageRepository _images;
    private readonly ISceneImageStorageService _storage;

    public SceneImageMediaEditSubjectSource(ISceneImageRepository images, ISceneImageStorageService storage)
    {
        _images = images;
        _storage = storage;
    }

    public MediaEditSubjectKind Kind => MediaEditSubjectKind.SceneImage;

    public async Task<MediaEditSourceImage> RequireSourceAsync(
        MediaEditSubjectRef subject, string sourceImageId, CancellationToken cancellationToken = default)
    {
        if (subject.Kind != MediaEditSubjectKind.SceneImage)
            throw new InvalidOperationException($"The scene image source cannot serve subject kind '{subject.Kind}'.");
        if (string.IsNullOrWhiteSpace(subject.SubjectId) || string.IsNullOrWhiteSpace(sourceImageId))
            throw new InvalidOperationException("An interaction id and a source image id are required.");

        var image = await _images.GetImageAsync(sourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{sourceImageId}' was not found.");
        if (!string.Equals(image.InteractionId, subject.SubjectId, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(subject.SubjectScopeId)
                && !string.Equals(image.SessionId, subject.SubjectScopeId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Only a scene image owned by the selected interaction can be edited.");
        }

        if (image.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(image.FileRelativePath))
            throw new InvalidOperationException("Only a complete stored scene image can be edited.");

        return new MediaEditSourceImage(image.Id, image.FileRelativePath, image.Sha256);
    }

    public Task<Stream> OpenReadAsync(string fileRelativePath, CancellationToken cancellationToken = default)
        => _storage.OpenReadAsync(fileRelativePath, cancellationToken);
}
