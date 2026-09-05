using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Default asset-library orchestration service. Creates assets (prompt/upload), enqueues edits and
/// profile-pack generation onto the background job queue, and provides query/delete/download
/// operations with the file-reference guard applied on deletion.
/// </summary>
public sealed class SceneAssetService : ISceneAssetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IDurableBackgroundJobQueue _backgroundJobQueue;
    private readonly ISceneBeatAnalyzerResolver _durableSettingsResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SceneAssetService> _logger;

    public SceneAssetService(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        IDurableBackgroundJobQueue backgroundJobQueue,
        ISceneBeatAnalyzerResolver durableSettingsResolver,
        TimeProvider timeProvider,
        ILogger<SceneAssetService> logger)
    {
        _repository = repository;
        _storage = storage;
        _backgroundJobQueue = backgroundJobQueue;
        _durableSettingsResolver = durableSettingsResolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SceneAsset> CreateAssetAsync(
        string name,
        SceneAssetType type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (!Enum.IsDefined(type))
            throw new InvalidOperationException("An asset type is required.");

        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Type = type,
            IsContainerOnly = true,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Pending
        };
        await _repository.UpsertAsync(asset, cancellationToken);
        _logger.LogInformation("Created scene asset container: AssetId={AssetId}, Name={Name}, Type={Type}", asset.Id, asset.Name, asset.Type);
        return asset;
    }

    public async Task<SceneAssetImage> AddGeneratedImageAsync(
        string assetId,
        string prompt,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("An image description is required.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image model is required.");
        if (string.IsNullOrWhiteSpace(imageSize))
            throw new InvalidOperationException("An image size is required.");

        var image = new SceneAssetImage
        {
            AssetId = asset.Id,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Pending,
            Prompt = prompt.Trim()
        };
        var payload = new SceneAssetGenerationJobPayload
        {
            AssetId = asset.Id,
            ImageId = image.Id,
            ModelId = modelId.Trim(),
            ImageSize = imageSize.Trim()
        };
        image.AssociationMetadataJson = JsonSerializer.Serialize(payload, JsonOptions);
        await _repository.UpsertImageAsync(image, cancellationToken);
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneAssetGeneration,
            DurableJobLane.ImageRender,
            JsonSerializer.Serialize(payload, JsonOptions),
            $"{BackgroundJobTypes.SceneAssetGeneration}:{image.Id}",
            cancellationToken);
        _logger.LogInformation("Enqueued scene asset image generation: AssetId={AssetId}, ImageId={ImageId}", asset.Id, image.Id);
        return image;
    }

    public async Task<SceneAssetImage> AddUploadedImageAsync(
        string assetId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A file name is required.");

        var image = new SceneAssetImage
        {
            AssetId = asset.Id,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Pending
        };
        var extension = SafeImageExtension(fileName);
        var stored = await _storage.SaveAsync($"{image.Id}{extension}", content, cancellationToken);
        image.Status = SceneAssetStatus.Complete;
        image.FileRelativePath = stored.RelativePath;
        image.MediaType = stored.MediaType;
        image.Width = stored.Width;
        image.Height = stored.Height;
        image.ByteLength = stored.ByteLength;
        image.Sha256 = stored.Sha256;
        image.CompletedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        image.UpdatedUtc = image.CompletedUtc.Value;
        await _repository.UpsertImageAsync(image, cancellationToken);
        _logger.LogInformation("Uploaded scene asset image: AssetId={AssetId}, ImageId={ImageId}", asset.Id, image.Id);
        return image;
    }

    public async Task<SceneAssetImage> EnqueueImageEditAsync(
        string assetId,
        string sourceImageId,
        string editPrompt,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        var source = await _repository.GetImageAsync(sourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{sourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, asset.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The source image does not belong to this asset.");
        if (source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Only completed stored images can be edited.");
        if (string.IsNullOrWhiteSpace(editPrompt))
            throw new InvalidOperationException("An edit instruction is required.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image editor model is required.");

        var image = new SceneAssetImage
        {
            AssetId = asset.Id,
            Kind = SceneAssetKind.Edited,
            Status = SceneAssetStatus.Pending,
            Prompt = editPrompt.Trim(),
            SourceImageId = source.Id
        };
        var payload = new SceneAssetEditingJobPayload
        {
            AssetId = asset.Id,
            ImageId = image.Id,
            ModelId = modelId.Trim()
        };
        image.AssociationMetadataJson = JsonSerializer.Serialize(payload, JsonOptions);
        await _repository.UpsertImageAsync(image, cancellationToken);
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneAssetEditing,
            DurableJobLane.ImageEdit,
            JsonSerializer.Serialize(payload, JsonOptions),
            $"{BackgroundJobTypes.SceneAssetEditing}:{image.Id}",
            cancellationToken);
        _logger.LogInformation("Enqueued scene asset image edit: AssetId={AssetId}, ImageId={ImageId}, SourceImageId={SourceImageId}", asset.Id, image.Id, source.Id);
        return image;
    }

    public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(
        string assetId, CancellationToken cancellationToken = default)
        => _repository.ListImagesAsync(assetId, cancellationToken);

    public Task<SceneAssetImage?> GetImageAsync(
        string imageId, CancellationToken cancellationToken = default)
        => _repository.GetImageAsync(imageId, cancellationToken);

    public async Task<SceneAssetImage> ApproveImageForProductionAsync(
        string imageId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default)
    {
        var approved = await _repository.ApproveImageForProductionAsync(
            imageId, sourceProvenanceJson, consentState, licenseState, licenseLabel,
            approvedUseScope, contentPolicyKey, compatibilityMetadataJson, cancellationToken);
        _logger.LogInformation(
            "Approved scene asset image for production: AssetId={AssetId}, ImageId={ImageId}",
            approved.AssetId, approved.Id);
        return approved;
    }

    public async Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
        string imageId, CancellationToken cancellationToken = default)
    {
        var image = await _repository.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{imageId}' was not found.");
        if (image.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(image.FileRelativePath))
            throw new InvalidOperationException($"Scene asset image '{imageId}' is not ready to download.");
        var asset = await RequireAssetAsync(image.AssetId, cancellationToken);
        var stream = await _storage.OpenReadAsync(image.FileRelativePath, cancellationToken);
        return (asset, image, stream);
    }

    public async Task<SceneAsset> CreateFromPromptAsync(
        string name,
        string prompt,
        SceneAssetType type,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("An asset description is required.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image model is required.");
        if (string.IsNullOrWhiteSpace(imageSize))
            throw new InvalidOperationException("An image size is required.");

        var payload = new SceneAssetGenerationJobPayload
        {
            ModelId = modelId.Trim(),
            ImageSize = imageSize.Trim()
        };
        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Pending,
            Type = type,
            Prompt = prompt.Trim(),
            AssociationMetadataJson = string.Empty
        };
        payload.AssetId = asset.Id;
        payload.ImageId = asset.Id;
        asset.AssociationMetadataJson = JsonSerializer.Serialize(payload, JsonOptions);
        await _repository.UpsertAsync(asset, cancellationToken);
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneAssetGeneration,
            DurableJobLane.ImageRender,
            JsonSerializer.Serialize(payload, JsonOptions),
            $"{BackgroundJobTypes.SceneAssetGeneration}:{asset.Id}",
            cancellationToken);
        _logger.LogInformation("Enqueued scene asset generation: AssetId={AssetId}, Name={Name}", asset.Id, asset.Name);
        return asset;
    }

    public async Task<SceneAsset> CreateFromUploadAsync(
        string name,
        SceneAssetType type,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A file name is required.");

        var assetId = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || extension.Contains(' '))
        {
            extension = ".png";
        }

        var stored = await _storage.SaveAsync($"{assetId}{extension.ToLowerInvariant()}", content, cancellationToken);
        var asset = new SceneAsset
        {
            Id = assetId,
            Name = name.Trim(),
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Complete,
            Type = type,
            FileRelativePath = stored.RelativePath,
            MediaType = stored.MediaType,
            Width = stored.Width,
            Height = stored.Height,
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            CompletedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        await _repository.UpsertAsync(asset, cancellationToken);
        _logger.LogInformation("Uploaded scene asset: AssetId={AssetId}, Name={Name}", asset.Id, asset.Name);
        return asset;
    }

    public async Task<SceneAsset> EnqueueEditAsync(
        string sourceAssetId,
        string name,
        string editPrompt,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetId))
            throw new InvalidOperationException("A source asset is required to edit.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (string.IsNullOrWhiteSpace(editPrompt))
            throw new InvalidOperationException("An edit instruction is required.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image editor model is required.");

        var source = await _repository.GetAsync(sourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset '{sourceAssetId}' was not found.");
        if (source.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException("Only completed assets can be edited.");

        var payload = new SceneAssetEditingJobPayload
        {
            ModelId = modelId.Trim()
        };
        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Kind = SceneAssetKind.Edited,
            Status = SceneAssetStatus.Pending,
            Type = source.Type,
            Prompt = editPrompt.Trim(),
            SourceAssetId = source.Id,
            AssociationMetadataJson = string.Empty
        };
        payload.AssetId = asset.Id;
        payload.ImageId = asset.Id;
        asset.AssociationMetadataJson = JsonSerializer.Serialize(payload, JsonOptions);
        await _repository.UpsertAsync(asset, cancellationToken);
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneAssetEditing,
            DurableJobLane.ImageEdit,
            JsonSerializer.Serialize(payload, JsonOptions),
            $"{BackgroundJobTypes.SceneAssetEditing}:{asset.Id}",
            cancellationToken);
        _logger.LogInformation("Enqueued scene asset edit: AssetId={AssetId}, Source={SourceId}", asset.Id, source.Id);
        return asset;
    }

    public async Task EnqueueProfilePackAsync(
        SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload.CharacterProfileId))
            throw new InvalidOperationException("A scenario character is required to generate a profile pack.");
        if (string.IsNullOrWhiteSpace(payload.FrontAssetId) && string.IsNullOrWhiteSpace(payload.Description))
            throw new InvalidOperationException("Provide a front photo or a character description to generate a profile pack.");
        if (string.IsNullOrWhiteSpace(payload.FrontAssetId) && string.IsNullOrWhiteSpace(payload.FrontModelId))
            throw new InvalidOperationException("An exact front image model is required when generating the front portrait.");
        if (string.IsNullOrWhiteSpace(payload.EditorModelId))
            throw new InvalidOperationException("An exact image editor model is required for profile-pack angles.");

        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneAssetProfilePackGeneration,
            DurableJobLane.ImageEdit,
            JsonSerializer.Serialize(payload, JsonOptions),
            $"{BackgroundJobTypes.SceneAssetProfilePackGeneration}:{payload.CharacterProfileId}",
            cancellationToken,
            useStableJobId: false);
        _logger.LogInformation("Enqueued profile pack generation: Character={Character}, FrontAsset={FrontAsset}",
            payload.CharacterProfileId, payload.FrontAssetId ?? "(generate from description)");
    }

    public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
        => _repository.GetAsync(assetId, cancellationToken);

    public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default)
        => _repository.ListByPackAsync(identityPackId, cancellationToken);

    public async Task<SceneAsset> ApproveForProductionAsync(
        string assetId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default)
    {
        var approved = await _repository.ApproveForProductionAsync(
            assetId, sourceProvenanceJson, consentState, licenseState, licenseLabel,
            approvedUseScope, contentPolicyKey, compatibilityMetadataJson, cancellationToken);
        _logger.LogInformation(
            "Approved scene asset for production: AssetId={AssetId}, Version={Version}, UseScope={UseScope}, ContentPolicy={ContentPolicy}",
            approved.Id, approved.ProductionVersion, approved.ApprovedUseScope, approved.ContentPolicyKey);
        return approved;
    }

    public async Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(
        string assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
        if (asset.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(asset.FileRelativePath))
            throw new InvalidOperationException($"Scene asset '{assetId}' is not ready to download.");
        var stream = await _storage.OpenReadAsync(asset.FileRelativePath, cancellationToken);
        return (asset, stream);
    }

    public async Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
        if (!string.IsNullOrWhiteSpace(asset.IdentityPackId))
        {
            throw new InvalidOperationException(
                "This asset belongs to an identity pack. Delete it from the Character Identity page instead.");
        }

        await _repository.DeleteAsync(asset.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(asset.FileRelativePath)
            && await _repository.CountByFilePathAsync(asset.FileRelativePath, cancellationToken) == 0)
        {
            await _storage.DeleteAsync(asset.FileRelativePath, cancellationToken);
        }

        _logger.LogInformation("Deleted scene asset: AssetId={AssetId}", asset.Id);
    }

    private async Task EnqueueDurableAsync(
        string jobType,
        DurableJobLane lane,
        string payloadJson,
        string dedupeKey,
        CancellationToken cancellationToken,
        bool useStableJobId = true)
    {
        var settings = await _durableSettingsResolver.ResolveAsync(cancellationToken);
        var createdUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var job = new DurableBackgroundJob
        {
            Id = useStableJobId ? dedupeKey : Guid.NewGuid().ToString("N"),
            JobType = jobType,
            Lane = lane,
            PayloadJson = payloadJson,
            DedupeKey = dedupeKey,
            MaxAttempts = settings.RetryDelaysSeconds.Count + 1,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc
        };
        if (!await _backgroundJobQueue.TryEnqueueAsync(job, cancellationToken))
            throw new InvalidOperationException($"A durable '{jobType}' job with key '{dedupeKey}' is already active.");
    }

    private async Task<SceneAsset> RequireAssetAsync(string assetId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            throw new InvalidOperationException("An asset is required.");
        return await _repository.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
    }

    private static string SafeImageExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || extension.Contains(' ')
            ? ".png"
            : extension.ToLowerInvariant();
    }
}
