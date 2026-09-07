using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Generates a typed-vision reference candidate into the ProducedImage store.</summary>
public sealed class ProducedImageGenerationJobHandler : IBackgroundJobHandler, IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProducedImageRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly ILogger<ProducedImageGenerationJobHandler> _logger;

    public ProducedImageGenerationJobHandler(
        IProducedImageRepository repository,
        ISceneAssetStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        ILogger<ProducedImageGenerationJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.ProducedImageGeneration;

    public Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
        => HandlePayloadAsync(job.PayloadJson, cancellationToken);

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandlePayloadAsync(job.PayloadJson, cancellationToken);

    private async Task HandlePayloadAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ProducedImageGenerationJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Produced image generation payload is missing or invalid.");
        Validate(payload);

        var image = new ProducedImage
        {
            Kind = ProducedImageKind.ReferenceCandidate,
            BatchId = payload.BatchId.Trim(),
            TargetRef = payload.TargetRef.Trim(),
            ReferenceKind = payload.ReferenceKind,
            Status = ProducedImageStatus.Undecided,
            VisionSource = ProducedImageVisionSource.Typed,
            VisionText = payload.VisionText.Trim()
        };
        await _repository.InsertAsync(image, cancellationToken);

        ResolvedImageModel? resolvedModel = null;
        try
        {
            var model = (string.IsNullOrWhiteSpace(payload.ModelId)
                ? await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken)
                : await _modelResolutionService.ResolveImageModelByIdAsync(payload.ModelId, cancellationToken))
                ?? throw new InvalidOperationException("The requested image model could not be resolved.");
            resolvedModel = model;
            var compilation = SceneAssetPromptCompiler.Compile(
                image.VisionText,
                MapAssetType(payload.ReferenceKind),
                model);
            image.PromptCompiled = compilation.Prompt;
            image.ModelId = model.ModelIdentifier;
            image.EndpointId = model.ComfyUiUrl ?? model.ProviderBaseUrl;
            await _repository.UpdateAsync(image, cancellationToken);

            var bytes = await _imageClient.GenerateAsync(
                model,
                compilation.Prompt,
                payload.ImageSize.Trim(),
                null,
                null,
                cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                throw new ImageGenerationException(
                    SceneImageRefusalMessage.ForUser(
                        model.ModelIdentifier,
                        model.ProviderName,
                        SceneImageRefusalMode.EmptyOutput),
                    model.ProviderName,
                    reasonCode: "empty_response");
            }

            using var stream = new MemoryStream(bytes);
            var stored = await _storage.SaveAsync($"{image.Id}.png", stream, cancellationToken);
            image.StoragePath = stored.RelativePath;
            image.RefusalMode = SceneImageRefusalMode.None;
            image.UpdatedUtc = DateTime.UtcNow.ToString("O");
            await _repository.UpdateAsync(image, cancellationToken);

            _logger.LogInformation(
                "Produced image generated: ImageId={ImageId}, BatchId={BatchId}, Model={Model}",
                image.Id,
                image.BatchId,
                model.ModelIdentifier);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var refusalMode = ex is ImageGenerationException imageException
                ? ResolveRefusalMode(imageException)
                : SceneImageRefusalMode.None;
            image.Status = ProducedImageStatus.Rejected;
            image.RefusalMode = refusalMode;
            image.UpdatedUtc = DateTime.UtcNow.ToString("O");
            if (resolvedModel is not null)
            {
                image.ModelId = resolvedModel.ModelIdentifier;
                image.EndpointId = resolvedModel.ComfyUiUrl ?? resolvedModel.ProviderBaseUrl;
            }
            await _repository.UpdateAsync(image, cancellationToken);
            _logger.LogWarning(
                "Produced image generation failed: ImageId={ImageId}, BatchId={BatchId}, Error={Error}",
                image.Id,
                image.BatchId,
                ex.Message);
            throw;
        }
    }

    private static void Validate(ProducedImageGenerationJobPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.BatchId))
            throw new InvalidOperationException("Produced image generation payload requires a BatchId.");
        if (string.IsNullOrWhiteSpace(payload.TargetRef))
            throw new InvalidOperationException("Produced image generation payload requires a TargetRef.");
        if (!Enum.IsDefined(payload.ReferenceKind))
            throw new InvalidOperationException("Produced image generation payload requires a valid ReferenceKind.");
        if (string.IsNullOrWhiteSpace(payload.VisionText))
            throw new InvalidOperationException("Produced image generation payload requires VisionText.");
        if (string.IsNullOrWhiteSpace(payload.ImageSize))
            throw new InvalidOperationException("Produced image generation payload requires ImageSize.");
    }

    private static SceneAssetType MapAssetType(ProducedImageReferenceKind referenceKind) => referenceKind switch
    {
        ProducedImageReferenceKind.CharacterFace => SceneAssetType.CharacterFace,
        ProducedImageReferenceKind.CharacterBody => SceneAssetType.CharacterBody,
        ProducedImageReferenceKind.Wardrobe => SceneAssetType.Wardrobe,
        ProducedImageReferenceKind.Location => SceneAssetType.Location,
        _ => throw new InvalidOperationException($"Reference kind '{referenceKind}' has no scene asset compiler mapping.")
    };

    private static SceneImageRefusalMode ResolveRefusalMode(ImageGenerationException exception)
    {
        if (string.Equals(exception.ReasonCode, "empty_response", StringComparison.Ordinal))
            return SceneImageRefusalMode.EmptyOutput;
        if (string.Equals(exception.ReasonCode, "payment_required", StringComparison.Ordinal))
            return SceneImageRefusalMode.None;
        if (exception.StatusCode is >= 400 and < 500)
            return SceneImageRefusalMode.PolicyError;
        return SceneImageRefusalMode.None;
    }
}
