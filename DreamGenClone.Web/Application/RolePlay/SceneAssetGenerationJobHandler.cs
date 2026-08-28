using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs a text-to-image scene asset generation (Juggernaut) for a pending
/// <see cref="SceneAssetKind.PromptGenerated"/> asset, saves the bytes to the asset library, and
/// marks the asset Complete/Failed.
/// </summary>
public sealed class SceneAssetGenerationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly ILogger<SceneAssetGenerationJobHandler> _logger;

    public SceneAssetGenerationJobHandler(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        ILogger<SceneAssetGenerationJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneAssetGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException("Scene asset generation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
            throw new InvalidOperationException("Scene asset generation payload requires an AssetId.");

        var asset = await _repository.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{payload.AssetId}' was not found.");
        if (asset.Status == SceneAssetStatus.Complete)
            return;
        if (asset.Kind != SceneAssetKind.PromptGenerated)
            throw new InvalidOperationException("Scene asset generation jobs require a PromptGenerated asset.");

        asset.Status = SceneAssetStatus.Pending;
        asset.StartedUtc ??= DateTime.UtcNow;
        asset.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAsync(asset, cancellationToken);

        try
        {
            var model = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
            var bytes = await _imageClient.GenerateAsync(model, asset.Prompt, "1024x1024", null, null, cancellationToken)
                ?? throw new InvalidOperationException("The image model returned no image bytes.");
            asset.ModelSnapshotJson = JsonSerializer.Serialize(new { model.ModelIdentifier, model.ProviderName }, JsonOptions);
            await CompleteWithBytesAsync(asset, $"{asset.Id}.png", bytes, cancellationToken);

            _logger.LogInformation("Scene asset generated: AssetId={AssetId}, Model={Model}", asset.Id, model.ModelIdentifier);
        }
        catch (Exception ex)
        {
            asset.Status = SceneAssetStatus.Failed;
            asset.ErrorMessage = ex.Message;
            asset.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertAsync(asset, cancellationToken);
            _logger.LogWarning("Scene asset generation failed: AssetId={AssetId}, Error={Error}", asset.Id, ex.Message);
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
