using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs a Qwen source-image edit for a pending <see cref="SceneAssetKind.Edited"/> asset. The source
/// asset is read from the library, edited with the configured editor model, and the result is saved
/// as a new asset revision (the source is untouched).
/// </summary>
public sealed class SceneAssetEditingJobHandler : IBackgroundJobHandler
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
    {
        var payload = JsonSerializer.Deserialize<SceneAssetEditingJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene asset editing payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
            throw new InvalidOperationException("Scene asset editing payload requires an AssetId.");
        if (string.IsNullOrWhiteSpace(payload.ModelId))
            throw new InvalidOperationException("Scene asset editing payload requires an exact ModelId.");

        var asset = await _repository.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{payload.AssetId}' was not found.");
        if (asset.Status == SceneAssetStatus.Complete)
            return;
        if (asset.Kind != SceneAssetKind.Edited || string.IsNullOrWhiteSpace(asset.SourceAssetId))
            throw new InvalidOperationException("Scene asset editing jobs require an Edited asset with a source asset.");

        var source = await _repository.GetAsync(asset.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset '{asset.SourceAssetId}' was not found.");
        if (source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Scene asset editing requires a completed source asset with a stored image.");

        asset.Status = SceneAssetStatus.Pending;
        asset.StartedUtc ??= DateTime.UtcNow;
        asset.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAsync(asset, cancellationToken);

        try
        {
            var editor = await _modelResolver.ResolveByIdAsync(payload.ModelId, cancellationToken);
            await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var bytes = await _imageEditingClient.EditAsync(editor, sourceStream, $"{source.Id}.png", asset.Prompt, cancellationToken);
            asset.ModelSnapshotJson = JsonSerializer.Serialize(new
            {
                requestedModelId = payload.ModelId,
                editor.ModelIdentifier,
                editor.ProviderName
            }, JsonOptions);
            await CompleteWithBytesAsync(asset, $"{asset.Id}.png", bytes, cancellationToken);

            _logger.LogInformation("Scene asset edited: AssetId={AssetId}, Source={SourceId}, Model={Model}", asset.Id, source.Id, editor.ModelIdentifier);
        }
        catch (Exception ex)
        {
            asset.Status = SceneAssetStatus.Failed;
            asset.ErrorMessage = ex.Message;
            asset.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertAsync(asset, cancellationToken);
            _logger.LogWarning("Scene asset editing failed: AssetId={AssetId}, Error={Error}", asset.Id, ex.Message);
            throw;
        }
    }

    private async Task CompleteWithBytesAsync(SceneAsset asset, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        var stored = await _storage.SaveAsync(fileName, stream, cancellationToken);
        asset.Status = SceneAssetStatus.Complete;
        asset.FileRelativePath = stored.RelativePath;
        asset.MediaType = stored.MediaType;
        asset.Width = stored.Width;
        asset.Height = stored.Height;
        asset.ByteLength = stored.ByteLength;
        asset.Sha256 = stored.Sha256;
        asset.CompletedUtc = DateTime.UtcNow;
        asset.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAsync(asset, cancellationToken);
    }
}
