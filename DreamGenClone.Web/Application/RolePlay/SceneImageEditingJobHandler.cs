using System.Diagnostics;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Runs a manual Qwen source-image edit using the dedicated editor configuration.</summary>
public sealed class SceneImageEditingJobHandler : IBackgroundJobHandler
{
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageStorageService _storage;
    private readonly IImageEditorModelResolver _modelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly ILogger<SceneImageEditingJobHandler> _logger;

    public SceneImageEditingJobHandler(
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        ILogger<SceneImageEditingJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolver = modelResolver;
        _imageEditingClient = imageEditingClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneImageEditing;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageEditingJobPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException("Scene image editing job payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.SessionId) || string.IsNullOrWhiteSpace(payload.InteractionId) || string.IsNullOrWhiteSpace(payload.ImageRecordId))
            throw new InvalidOperationException("Scene image editing job payload requires SessionId, InteractionId, and ImageRecordId.");

        var image = await _repository.GetImageAsync(payload.ImageRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image edit record '{payload.ImageRecordId}' was not found.");
        if (image.Status == SceneImageStatus.Complete)
            return;
        if (image.Operation != SceneImageOperation.Edit || string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException("Scene image editing jobs require an edit record with a source image id.");

        image.Status = SceneImageStatus.Generating;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.InsertImageAsync(image, cancellationToken);

        try
        {
            var source = await _repository.GetImageAsync(image.SourceImageId, cancellationToken)
                ?? throw new InvalidOperationException($"Source scene image '{image.SourceImageId}' was not found.");
            if (source.Status != SceneImageStatus.Complete
                || !string.Equals(source.SessionId, payload.SessionId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.InteractionId, payload.InteractionId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(source.FileRelativePath))
            {
                throw new InvalidOperationException("The source scene image is not a complete image for this session and interaction.");
            }

            var resolved = await _modelResolver.ResolveAsync(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var bytes = await _imageEditingClient.EditAsync(resolved, sourceStream, $"{source.Id}.png", image.PromptSnapshot, cancellationToken);
            stopwatch.Stop();

            await using var outputStream = new MemoryStream(bytes);
            image.FileRelativePath = await _storage.SaveAsync(payload.SessionId, $"{image.Id}.png", outputStream, cancellationToken);
            image.ModelIdentifier = resolved.ModelIdentifier;
            image.ProviderName = resolved.ProviderName;
            image.ContentPolicy = resolved.ContentPolicy;
            image.Status = SceneImageStatus.Complete;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);

            _logger.LogInformation("Scene image edit completed: SessionId={SessionId}, InteractionId={InteractionId}, ImageRecordId={ImageRecordId}, SourceImageId={SourceImageId}, Model={ModelIdentifier}, DurationMs={DurationMs}", payload.SessionId, payload.InteractionId, image.Id, source.Id, resolved.ModelIdentifier, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            image.Status = SceneImageStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);
            _logger.LogWarning(ex, "Scene image edit failed: SessionId={SessionId}, ImageRecordId={ImageRecordId}", payload.SessionId, image.Id);
            throw;
        }
    }
}