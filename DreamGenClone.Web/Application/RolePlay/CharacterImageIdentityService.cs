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
    private readonly IReferenceImageQualityAnalyzer _analyzer;
    private readonly ILogger<CharacterImageIdentityService> _logger;

    public CharacterImageIdentityService(
        ICharacterImageIdentityRepository repository,
        ICharacterImageAssetStorageService storage,
        IReferenceImageQualityAnalyzer analyzer,
        ILogger<CharacterImageIdentityService> logger)
    {
        _repository = repository;
        _storage = storage;
        _analyzer = analyzer;
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
        string characterProfileId,
        CharacterImageIdentityPackScope scope,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        var packs = await _repository.ListPacksAsync(characterProfileId, cancellationToken);
        var existingDraft = packs.FirstOrDefault(p => p.Status == CharacterImageIdentityPackStatus.Draft);
        if (existingDraft is not null)
        {
            if (!ScopeCovers(existingDraft.PackScope, scope))
            {
                throw new InvalidOperationException(
                    $"Draft identity pack '{existingDraft.Id}' is {existingDraft.PackScope}, which does not cover the "
                    + $"requested {scope}. Creating the draft again never raises it: raise it explicitly with "
                    + "SetDraftPackScopeAsync, naming the canonical full-body asset.");
            }

            return existingDraft;
        }

        if (packs.Count > 0)
        {
            throw new InvalidOperationException(
                "This character already has identity pack versions. Supersede the latest approved pack to create a new draft version.");
        }

        var pack = new CharacterImageIdentityPack
        {
            CharacterTemplateId = characterProfileId,
            Version = 1,
            Status = CharacterImageIdentityPackStatus.Draft,
            PackScope = scope
        };
        var created = await _repository.UpsertDraftAsync(pack, cancellationToken);
        _logger.LogInformation("Created identity pack draft {PackId} v{Version} ({Scope}) for character {CharacterId}", created.Id, created.Version, created.PackScope, characterProfileId);
        return created;
    }

    /// <summary>
    /// Scope and canonical-full-body pointer are draft-time data: <c>ApproveAsync</c> writes neither, so a pack
    /// that claims to be body-complete must already carry both before approval. This is the ONE way to raise a
    /// draft's scope, and it refuses to narrow one — a pack holding full-body references is body-complete by
    /// definition, and weakening the claim would be a silent scope change.
    /// </summary>
    public async Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(
        string packId,
        CharacterImageIdentityPackScope scope,
        string? canonicalFullBodyAssetId,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        var pack = await _repository.GetPackAsync(packId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Identity pack '{pack.Id}' is {pack.Status}; only a draft pack can change scope. Supersede it to "
                + "create an editable draft, and promote into that.");
        }

        if (!ScopeCovers(scope, pack.PackScope))
        {
            throw new InvalidOperationException(
                $"Identity pack '{pack.Id}' is {pack.PackScope} and cannot be narrowed to {scope}: a pack that "
                + "carries full-body references is a body-complete pack.");
        }

        if (scope == CharacterImageIdentityPackScope.FaceOnly)
        {
            if (!string.IsNullOrWhiteSpace(canonicalFullBodyAssetId))
            {
                throw new InvalidOperationException(
                    "A FaceOnly identity pack cannot carry a canonical full-body asset id.");
            }

            pack.CanonicalFullBodyAssetId = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(canonicalFullBodyAssetId))
            {
                throw new InvalidOperationException(
                    "A BodyComplete identity pack requires the canonical full-body asset id (the unclothed Front "
                    + "full-body reference).");
            }

            var assets = await _repository.ListAssetsAsync(pack.Id, cancellationToken);
            var canonical = assets.FirstOrDefault(a =>
                string.Equals(a.Id, canonicalFullBodyAssetId.Trim(), StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    "The canonical full-body asset must belong to the pack whose scope is being set.");
            if (canonical.AssetKind != SceneImageReferenceAssetKind.FullBody)
            {
                throw new InvalidOperationException("The canonical full-body asset must be a full-body reference.");
            }

            if (canonical.BodyState != SceneImageReferenceBodyState.Unclothed
                || canonical.BodyView != SceneImageReferenceBodyView.Front)
            {
                throw new InvalidOperationException(
                    "The canonical full-body asset must be the unclothed Front full-body reference.");
            }

            pack.CanonicalFullBodyAssetId = canonical.Id;
        }

        pack.PackScope = scope;
        var saved = await _repository.UpsertDraftAsync(pack, cancellationToken);
        _logger.LogInformation(
            "Identity pack {PackId} scope set to {Scope} (canonical full-body: {CanonicalBody})",
            saved.Id,
            saved.PackScope,
            saved.CanonicalFullBodyAssetId ?? "none");
        return saved;
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
        SceneImageReferenceFaceView? faceView = null,
        SceneImageReferenceBodyView? bodyView = null,
        SceneImageReferenceBodyState? bodyState = null,
        CancellationToken cancellationToken = default)
    {
        var pack = await _repository.GetPackAsync(packId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException("Reference assets can only be uploaded to a draft pack.");
        if (kind == SceneImageReferenceAssetKind.Face && faceView is null)
            throw new InvalidOperationException(
                "A face reference asset requires a face view (Front, ThreeQuarterLeft, ThreeQuarterRight, ProfileLeft, ProfileRight).");
        if (kind != SceneImageReferenceAssetKind.Face && faceView is not null)
            throw new InvalidOperationException("Only face reference assets carry a face view.");
        if (kind == SceneImageReferenceAssetKind.FullBody && bodyState is null)
            throw new InvalidOperationException("A full-body reference asset requires an explicit body state (Clothed or Unclothed).");
        if (kind != SceneImageReferenceAssetKind.FullBody && (bodyView is not null || bodyState is not null))
            throw new InvalidOperationException("Only full-body reference assets carry a body view or body state.");

        var assetId = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || extension.Contains(' '))
        {
            extension = ".png";
        }

        var stored = await _storage.SaveAsync(pack.CharacterTemplateId, $"{assetId}{extension.ToLowerInvariant()}", content, cancellationToken);
        await using var analyzeStream = await _storage.OpenReadAsync(stored.RelativePath, cancellationToken);
        (var rating, var qualityNotes) = _analyzer.Analyze(analyzeStream, stored.Width ?? 0, stored.Height ?? 0, stored.ByteLength);

        var asset = new SceneImageReferenceAsset
        {
            Id = assetId,
            IdentityPackId = packId,
            AssetKind = kind,
            FaceView = faceView,
            BodyView = bodyView,
            BodyState = bodyState,
            FileRelativePath = stored.RelativePath,
            MediaType = stored.MediaType,
            Width = stored.Width,
            Height = stored.Height,
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            QualityRating = rating,
            QualityNotes = qualityNotes,
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

    public Task SetAssetQualityAsync(
        string assetId,
        SceneImageReferenceQuality quality,
        string qualityNotes,
        CancellationToken cancellationToken = default)
        => _repository.UpdateAssetQualityAsync(assetId, quality, qualityNotes, cancellationToken);

    public async Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(
        string assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' was not found.");
        await using var stream = await _storage.OpenReadAsync(asset.FileRelativePath, cancellationToken);
        (var rating, var notes) = _analyzer.Analyze(stream, asset.Width ?? 0, asset.Height ?? 0, asset.ByteLength);
        await _repository.UpdateAssetQualityAsync(asset.Id, rating, notes, cancellationToken);
        asset.QualityRating = rating;
        asset.QualityNotes = notes;
        _logger.LogInformation("Analysed quality for reference asset {AssetId}: {Rating}", asset.Id, rating);
        return asset;
    }

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

    private static void RequireScope(CharacterImageIdentityPackScope scope)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new InvalidOperationException(
                $"Unsupported identity pack scope '{(int)scope}'; FaceOnly or BodyComplete is required.");
        }
    }

    /// <summary>
    /// Whether an existing scope already satisfies a requested one. A <c>FaceOnly</c> pack holds the five face
    /// slots; a <c>BodyComplete</c> pack is a strict superset of it (its face half is unconditional), so it
    /// covers both requests — that is the whole relation, stated once.
    /// </summary>
    private static bool ScopeCovers(
        CharacterImageIdentityPackScope existing, CharacterImageIdentityPackScope requested)
        => existing switch
        {
            CharacterImageIdentityPackScope.FaceOnly => requested == CharacterImageIdentityPackScope.FaceOnly,
            CharacterImageIdentityPackScope.BodyComplete => true,
            _ => throw new InvalidOperationException($"Unsupported identity pack scope '{existing}'.")
        };
}
