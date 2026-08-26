using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Default identity-pack curation service. Coordinates the identity repository and the reference
/// asset storage service so the UI performs one operation per mutation, with the file-reference
/// guard applied on deletion.
/// </summary>
public sealed class CharacterImageIdentityService : ICharacterImageIdentityService
{
    private readonly ICharacterImageIdentityRepository _repository;
    private readonly ICharacterImageAssetStorageService _storage;
    private readonly ILogger<CharacterImageIdentityService> _logger;

    public CharacterImageIdentityService(
        ICharacterImageIdentityRepository repository,
        ICharacterImageAssetStorageService storage,
        ILogger<CharacterImageIdentityService> logger)
    {
        _repository = repository;
        _storage = storage;
        _logger = logger;
    }

    public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
        => _repository.ListPacksAsync(characterProfileId, cancellationToken);

    public Task<CharacterImageIdentityPack?> GetPackAsync(
        string packId, CancellationToken cancellationToken = default)
        => _repository.GetPackAsync(packId, cancellationToken);

    public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        string packId, CancellationToken cancellationToken = default)
        => _repository.ListAssetsAsync(packId, cancellationToken);

    public async Task<CharacterImageIdentityPack> CreateDraftPackAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        var packs = await _repository.ListPacksAsync(characterProfileId, cancellationToken);
        var existingDraft = packs.FirstOrDefault(p => p.Status == CharacterImageIdentityPackStatus.Draft);
        if (existingDraft is not null)
        {
            return existingDraft;
        }

        if (packs.Count > 0)
        {
            throw new InvalidOperationException(
                "This character already has identity pack versions. Supersede the latest approved pack to create a new draft version.");
        }

        var pack = new CharacterImageIdentityPack
        {
            CharacterProfileId = characterProfileId,
            Version = 1,
            Status = CharacterImageIdentityPackStatus.Draft
        };
        var created = await _repository.UpsertDraftAsync(pack, cancellationToken);
        _logger.LogInformation("Created identity pack draft {PackId} v{Version} for character {CharacterId}", created.Id, created.Version, characterProfileId);
        return created;
    }

    public Task<CharacterImageIdentityPack> ApprovePackAsync(
        string packId,
        string descriptorSnapshotJson,
        string canonicalFaceAssetId,
        CancellationToken cancellationToken = default)
        => _repository.ApproveAsync(packId, descriptorSnapshotJson, canonicalFaceAssetId, cancellationToken);

    public async Task<CharacterImageIdentityPack> SupersedePackAsync(
        string packId, CancellationToken cancellationToken = default)
    {
        var next = await _repository.SupersedeAsync(packId, cancellationToken);
        _logger.LogInformation("Superseded identity pack {PackId} -> {NextId} v{Version}", packId, next.Id, next.Version);
        return next;
    }

    public async Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
    {
        var pack = await _repository.GetPackAsync(packId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        var assets = await _repository.ListAssetsAsync(packId, cancellationToken);

        await _repository.DeletePackAsync(packId, cancellationToken);

        foreach (var asset in assets)
        {
            await DeleteFileIfUnreferencedAsync(asset.FileRelativePath, cancellationToken);
        }

        _logger.LogInformation("Deleted identity pack {PackId}", packId);
    }

    public async Task<SceneImageReferenceAsset> UploadAssetAsync(
        string packId,
        SceneImageReferenceAssetKind kind,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var pack = await _repository.GetPackAsync(packId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException("Reference assets can only be uploaded to a draft pack.");

        var assetId = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || extension.Contains(' '))
        {
            extension = ".png";
        }

        var stored = await _storage.SaveAsync(pack.CharacterProfileId, $"{assetId}{extension.ToLowerInvariant()}", content, cancellationToken);

        var asset = new SceneImageReferenceAsset
        {
            Id = assetId,
            IdentityPackId = packId,
            AssetKind = kind,
            FileRelativePath = stored.RelativePath,
            MediaType = stored.MediaType,
            Width = stored.Width,
            Height = stored.Height,
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            IsApproved = false
        };

        try
        {
            await _repository.AddAssetAsync(asset, cancellationToken);
        }
        catch
        {
            // Do not leave an orphaned file when the asset row could not be written.
            await _storage.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw;
        }

        _logger.LogInformation("Uploaded identity reference asset {AssetId} ({Kind}) to pack {PackId}", asset.Id, kind, packId);
        return asset;
    }

    public Task SetAssetProvenanceAsync(
        string assetId,
        string sourceLabel,
        SceneImageReferenceConsentState consentState,
        CancellationToken cancellationToken = default)
        => _repository.UpdateAssetProvenanceAsync(assetId, sourceLabel, consentState, cancellationToken);

    public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default)
        => _repository.SetAssetApprovalAsync(assetId, isApproved, cancellationToken);

    public async Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' was not found.");

        await _repository.DeleteAssetAsync(assetId, cancellationToken);
        await DeleteFileIfUnreferencedAsync(asset.FileRelativePath, cancellationToken);

        _logger.LogInformation("Deleted identity reference asset {AssetId}", assetId);
    }

    private async Task DeleteFileIfUnreferencedAsync(string fileRelativePath, CancellationToken cancellationToken)
    {
        var remaining = await _repository.CountAssetsByFilePathAsync(fileRelativePath, cancellationToken);
        if (remaining == 0)
        {
            await _storage.DeleteAsync(fileRelativePath, cancellationToken);
        }
    }
}
