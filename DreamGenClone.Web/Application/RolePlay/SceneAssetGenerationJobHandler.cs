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
/// Runs a text-to-image scene asset generation (Juggernaut) for a pending
/// <see cref="SceneAssetKind.PromptGenerated"/> asset, saves the bytes to the asset library, and
/// marks the asset Complete/Failed.
/// </summary>
public sealed class SceneAssetGenerationJobHandler : IBackgroundJobHandler, IDurableBackgroundJobHandler
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
        => await HandleAsync(job.PayloadJson, cancellationToken);

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => await HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene asset generation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
            throw new InvalidOperationException("Scene asset generation payload requires an AssetId.");
        if (string.IsNullOrWhiteSpace(payload.ImageId))
            throw new InvalidOperationException("Scene asset generation payload requires an ImageId.");
        if (string.IsNullOrWhiteSpace(payload.ModelId))
            throw new InvalidOperationException("Scene asset generation payload requires an exact ModelId.");
        if (string.IsNullOrWhiteSpace(payload.ImageSize))
            throw new InvalidOperationException("Scene asset generation payload requires an ImageSize.");

        var asset = await _repository.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{payload.AssetId}' was not found.");
        var image = await _repository.GetImageAsync(payload.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{payload.ImageId}' was not found.");
        if (!string.Equals(image.AssetId, asset.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene asset generation image does not belong to the payload asset.");
        if (image.Status == SceneAssetStatus.Complete)
            return;
        if (image.Kind != SceneAssetKind.PromptGenerated)
            throw new InvalidOperationException("Scene asset generation jobs require a PromptGenerated image.");

        image.Status = SceneAssetStatus.Pending;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertImageAsync(image, cancellationToken);

        try
        {
            var model = await _modelResolutionService.ResolveImageModelByIdAsync(payload.ModelId, cancellationToken);
            var compilation = SceneAssetPromptCompiler.Compile(
                image.Prompt,
                asset.Type ?? throw new InvalidOperationException("Scene asset generation requires an explicit asset type."),
                model);
            image.AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                semanticDescription = image.Prompt,
                compiledPrompt = compilation.Prompt,
                compilation.CompilerId,
                compilation.CompilerVersion,
                requestedModelId = payload.ModelId,
                imageSize = payload.ImageSize
            }, JsonOptions);
            await _repository.UpsertImageAsync(image, cancellationToken);
            var bytes = await _imageClient.GenerateAsync(model, compilation.Prompt, payload.ImageSize, null, null, cancellationToken)
                ?? throw new InvalidOperationException("The image model returned no image bytes.");
            image.ModelSnapshotJson = JsonSerializer.Serialize(new
            {
                requestedModelId = payload.ModelId,
                model.ModelIdentifier,
                model.ProviderName,
                model.SceneImageModelFamily,
                model.PromptDialect,
                compilation.CompilerId,
                compilation.CompilerVersion
            }, JsonOptions);
            await CompleteWithBytesAsync(image, $"{image.Id}.png", bytes, cancellationToken);

            _logger.LogInformation("Scene asset image generated: AssetId={AssetId}, ImageId={ImageId}, Model={Model}", asset.Id, image.Id, model.ModelIdentifier);
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
            _logger.LogWarning("Scene asset image generation failed: AssetId={AssetId}, ImageId={ImageId}, Error={Error}", asset.Id, image.Id, ex.Message);
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
