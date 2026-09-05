using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs a Qwen source-image edit for a pending <see cref="SceneAssetKind.Edited"/> asset. The source
/// asset is read from the library, edited with the configured editor model, and the result is saved
/// as a new asset revision (the source is untouched).
/// </summary>
public sealed class SceneAssetEditingJobHandler : IBackgroundJobHandler, IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IImageEditorModelResolver _modelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly ILogger<SceneAssetEditingJobHandler> _logger;

    public SceneAssetEditingJobHandler(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        ILogger<SceneAssetEditingJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolver = modelResolver;
        _imageEditingClient = imageEditingClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneAssetEditing;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
        => await HandleAsync(job.PayloadJson, cancellationToken);

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => await HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetEditingJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene asset editing payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
            throw new InvalidOperationException("Scene asset editing payload requires an AssetId.");
        if (string.IsNullOrWhiteSpace(payload.ImageId))
            throw new InvalidOperationException("Scene asset editing payload requires an ImageId.");
        if (string.IsNullOrWhiteSpace(payload.ModelId))
            throw new InvalidOperationException("Scene asset editing payload requires an exact ModelId.");

        var asset = await _repository.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{payload.AssetId}' was not found.");
        var image = await _repository.GetImageAsync(payload.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{payload.ImageId}' was not found.");
        if (!string.Equals(image.AssetId, asset.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene asset edit image does not belong to the payload asset.");
        if (image.Status == SceneAssetStatus.Complete)
            return;
        if (image.Kind != SceneAssetKind.Edited || string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException("Scene asset editing jobs require an Edited image with a source image.");

        var source = await _repository.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Scene asset editing requires a completed source asset with a stored image.");

        image.Status = SceneAssetStatus.Pending;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertImageAsync(image, cancellationToken);

        try
        {
            var editor = await _modelResolver.ResolveByIdAsync(payload.ModelId, cancellationToken);
            await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var bytes = await _imageEditingClient.EditAsync(editor, sourceStream, $"{source.Id}.png", image.Prompt, cancellationToken);
            image.ModelSnapshotJson = JsonSerializer.Serialize(new
            {
                requestedModelId = payload.ModelId,
                editor.ModelIdentifier,
                editor.ProviderName
            }, JsonOptions);
            await CompleteWithBytesAsync(image, $"{image.Id}.png", bytes, cancellationToken);

            _logger.LogInformation("Scene asset image edited: AssetId={AssetId}, ImageId={ImageId}, Source={SourceId}, Model={Model}", asset.Id, image.Id, source.Id, editor.ModelIdentifier);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            image.Status = SceneAssetStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertImageAsync(image, cancellationToken);
            _logger.LogWarning("Scene asset image editing failed: AssetId={AssetId}, ImageId={ImageId}, Error={Error}", asset.Id, image.Id, ex.Message);
            throw;
        }
    }

    private async Task CompleteWithBytesAsync(SceneAssetImage image, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        var stored = await _storage.SaveAsync(fileName, stream, cancellationToken);
        image.Status = SceneAssetStatus.Complete;
        image.FileRelativePath = stored.RelativePath;
        image.MediaType = stored.MediaType;
        image.Width = stored.Width;
        image.Height = stored.Height;
        image.ByteLength = stored.ByteLength;
        image.Sha256 = stored.Sha256;
        image.CompletedUtc = DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertImageAsync(image, cancellationToken);
    }
}
