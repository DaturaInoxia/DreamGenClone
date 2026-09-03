using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
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
    private readonly IBackgroundJobQueue _backgroundJobQueue;
    private readonly ILogger<SceneAssetService> _logger;

    public SceneAssetService(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        ILogger<SceneAssetService> logger)
    {
        _repository = repository;
        _storage = storage;
        _backgroundJobQueue = backgroundJobQueue;
        _logger = logger;
    }

    public async Task<SceneAsset> CreateFromPromptAsync(
        string name, string prompt, string? size = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("An image prompt is required.");

        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Pending,
            Prompt = prompt.Trim()
        };
        await _repository.UpsertAsync(asset, cancellationToken);
        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneAssetGeneration,
            JsonSerializer.Serialize(new SceneAssetGenerationJobPayload { AssetId = asset.Id }, JsonOptions),
            dedupeKey: $"{BackgroundJobTypes.SceneAssetGeneration}:{asset.Id}");
        _logger.LogInformation("Enqueued scene asset generation: AssetId={AssetId}, Name={Name}", asset.Id, asset.Name);
        return asset;
    }

    public async Task<SceneAsset> CreateFromUploadAsync(
        string name, string fileName, Stream content, CancellationToken cancellationToken = default)
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
            // Uploads are character-face assets in Asset Studio; always set an explicit valid type
            // so a stale 'Type ... DEFAULT General' DB schema can never write an unparseable value.
            Type = SceneAssetType.CharacterFace,
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
        string sourceAssetId, string name, string editPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetId))
            throw new InvalidOperationException("A source asset is required to edit.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An asset name is required.");
        if (string.IsNullOrWhiteSpace(editPrompt))
            throw new InvalidOperationException("An edit instruction is required.");

        var source = await _repository.GetAsync(sourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset '{sourceAssetId}' was not found.");
        if (source.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException("Only completed assets can be edited.");

        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Kind = SceneAssetKind.Edited,
            Status = SceneAssetStatus.Pending,
            Prompt = editPrompt.Trim(),
            SourceAssetId = source.Id
        };
        await _repository.UpsertAsync(asset, cancellationToken);
        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneAssetEditing,
            JsonSerializer.Serialize(new SceneAssetEditingJobPayload { AssetId = asset.Id }, JsonOptions),
            dedupeKey: $"{BackgroundJobTypes.SceneAssetEditing}:{asset.Id}");
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

        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneAssetProfilePackGeneration,
            JsonSerializer.Serialize(payload, JsonOptions),
            dedupeKey: $"{BackgroundJobTypes.SceneAssetProfilePackGeneration}:{payload.CharacterProfileId}");
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
}
