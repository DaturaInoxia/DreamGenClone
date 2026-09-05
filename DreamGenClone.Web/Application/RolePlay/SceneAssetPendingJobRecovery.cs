using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneAssetPendingJobRecovery : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _assets;
    private readonly IDurableBackgroundJobQueue _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SceneAssetPendingJobRecovery> _logger;

    public SceneAssetPendingJobRecovery(
        ISceneAssetRepository assets,
        IDurableBackgroundJobQueue jobs,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SceneAssetPendingJobRecovery> logger)
    {
        _assets = assets;
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var pending = new List<SceneAssetImage>();
        foreach (var asset in await _assets.ListAsync(cancellationToken))
        {
            pending.AddRange((await _assets.ListImagesAsync(asset.Id, cancellationToken))
                .Where(image => image.Status == SceneAssetStatus.Pending
                    && image.Kind is SceneAssetKind.PromptGenerated or SceneAssetKind.Edited));
        }
        if (pending.Count == 0)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var settings = await scope.ServiceProvider
            .GetRequiredService<ISceneBeatAnalyzerResolver>()
            .ResolveAsync(cancellationToken);
        foreach (var image in pending)
        {
            var jobType = image.Kind == SceneAssetKind.PromptGenerated
                ? BackgroundJobTypes.SceneAssetGeneration
                : BackgroundJobTypes.SceneAssetEditing;
            var lane = image.Kind == SceneAssetKind.PromptGenerated
                ? DurableJobLane.ImageRender
                : DurableJobLane.ImageEdit;
            var jobId = $"{jobType}:{image.Id}";
            var existing = await _jobs.GetAsync(jobId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Status is DurableBackgroundJobStatus.Failed or DurableBackgroundJobStatus.Cancelled)
                {
                    await FailImageAsync(
                        image,
                        existing.ErrorMessage ?? $"The durable {jobType} job ended with status {existing.Status}.",
                        cancellationToken);
                }
                continue;
            }

            if (!HasValidPersistedRequest(image))
            {
                await FailImageAsync(
                    image,
                    "Processing was interrupted before the exact model request was persisted. Create a new image; no model or settings were inferred.",
                    cancellationToken);
                continue;
            }

            var createdUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var enqueued = await _jobs.TryEnqueueAsync(new DurableBackgroundJob
            {
                Id = jobId,
                JobType = jobType,
                Lane = lane,
                PayloadJson = image.AssociationMetadataJson!,
                DedupeKey = jobId,
                MaxAttempts = settings.RetryDelaysSeconds.Count + 1,
                CreatedUtc = createdUtc,
                UpdatedUtc = createdUtc
            }, cancellationToken);
            if (!enqueued)
            {
                await FailImageAsync(
                    image,
                    "The persisted image request could not be recovered because another active job owns its key.",
                    cancellationToken);
                continue;
            }

            _logger.LogInformation(
                "Recovered pending scene asset image job: AssetId={AssetId}, ImageId={ImageId}, JobType={JobType}",
                image.AssetId,
                image.Id,
                jobType);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool HasValidPersistedRequest(SceneAssetImage image)
    {
        if (string.IsNullOrWhiteSpace(image.AssociationMetadataJson))
            return false;

        try
        {
            if (image.Kind == SceneAssetKind.PromptGenerated)
            {
                var payload = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(image.AssociationMetadataJson, JsonOptions);
                return payload is not null
                    && string.Equals(payload.AssetId, image.AssetId, StringComparison.Ordinal)
                    && string.Equals(payload.ImageId, image.Id, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(payload.ModelId)
                    && !string.IsNullOrWhiteSpace(payload.ImageSize);
            }

            var editPayload = JsonSerializer.Deserialize<SceneAssetEditingJobPayload>(image.AssociationMetadataJson, JsonOptions);
            return editPayload is not null
                && string.Equals(editPayload.AssetId, image.AssetId, StringComparison.Ordinal)
                && string.Equals(editPayload.ImageId, image.Id, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(editPayload.ModelId)
                && !string.IsNullOrWhiteSpace(image.SourceImageId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task FailImageAsync(
        SceneAssetImage image,
        string message,
        CancellationToken cancellationToken)
    {
        image.Status = SceneAssetStatus.Failed;
        image.ErrorMessage = message;
        image.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _assets.UpsertImageAsync(image, cancellationToken);
        if (string.Equals(image.Id, image.AssetId, StringComparison.Ordinal))
        {
            var legacyAsset = await _assets.GetAsync(image.AssetId, cancellationToken);
            if (legacyAsset is not null && !legacyAsset.IsContainerOnly)
            {
                legacyAsset.Status = SceneAssetStatus.Failed;
                legacyAsset.ErrorMessage = message;
                legacyAsset.UpdatedUtc = image.UpdatedUtc;
                await _assets.UpsertAsync(legacyAsset, cancellationToken);
            }
        }
        _logger.LogWarning(
            "Pending scene asset image could not be recovered: AssetId={AssetId}, ImageId={ImageId}, Reason={Reason}",
            image.AssetId,
            image.Id,
            message);
    }
}