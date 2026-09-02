using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The special "Generate Profile Pack" function: for one scenario character, produce the 5 face
/// views (front + 3/4L, 3/4R, profL, profR) and save them into a draft identity pack plus the asset
/// library. The front comes from an existing complete asset when supplied, otherwise it is generated
/// by the configured image model from the character description; the other four views are produced by
/// the canned Qwen angle edits (the exact validated steps from the pack proof).
/// </summary>
public sealed class SceneAssetProfilePackJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The exact validated canned edits (from build_pack_via_qwen.py ANGLES) — rotate ONLY the head
    // while preserving identity exactly. Kept as canned constants so users never type these.
    private static readonly IReadOnlyList<(SceneImageReferenceFaceView View, string Prompt)> AngleEdits =
    [
        (SceneImageReferenceFaceView.ThreeQuarterLeft,
            "Rotate the person's head and upper body slightly to their left, about a three-quarter view. " +
            "Keep the exact same face, hair, facial features, identity, clothing, and lighting unchanged. Only the head angle changes."),
        (SceneImageReferenceFaceView.ThreeQuarterRight,
            "Rotate the person's head and upper body slightly to their right, about a three-quarter view. " +
            "Keep the exact same face, hair, facial features, identity, clothing, and lighting unchanged. Only the head angle changes."),
        (SceneImageReferenceFaceView.ProfileLeft,
            "Rotate the person to a left profile, head turned fully to their left showing the left side of the face. " +
            "Keep the exact same face, hair, facial features, identity, clothing, and lighting unchanged. Only the head angle changes."),
        (SceneImageReferenceFaceView.ProfileRight,
            "Rotate the person to a right profile, head turned fully to their right showing the right side of the face. " +
            "Keep the exact same face, hair, facial features, identity, clothing, and lighting unchanged. Only the head angle changes.")
    ];

    private readonly ISceneAssetRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly ICharacterImageIdentityService _identityService;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly IImageEditorModelResolver _editorModelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly ILogger<SceneAssetProfilePackJobHandler> _logger;

    public SceneAssetProfilePackJobHandler(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        ICharacterImageIdentityService identityService,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IImageEditorModelResolver editorModelResolver,
        IImageEditingClient imageEditingClient,
        ILogger<SceneAssetProfilePackJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _identityService = identityService;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _editorModelResolver = editorModelResolver;
        _imageEditingClient = imageEditingClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneAssetProfilePackGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetProfilePackJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Profile pack generation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.CharacterProfileId))
            throw new InvalidOperationException("Profile pack generation requires a character profile id.");
        if (string.IsNullOrWhiteSpace(payload.FrontAssetId) && string.IsNullOrWhiteSpace(payload.Description))
            throw new InvalidOperationException("Profile pack generation requires a front photo or a character description.");

        // 1. Ensure an editable draft identity pack for the character.
        var packId = await EnsureDraftPackAsync(payload.CharacterProfileId, payload.IdentityPackId, cancellationToken);
        _logger.LogInformation("Profile pack generation started: Character={Character}, Pack={PackId}", payload.CharacterProfileId, packId);

        // 2. Resolve the front (identity anchor) bytes.
        byte[] frontBytes;
        string? frontModelLabel = null;
        if (!string.IsNullOrWhiteSpace(payload.FrontAssetId))
        {
            var frontAsset = await _repository.GetAsync(payload.FrontAssetId, cancellationToken)
                ?? throw new InvalidOperationException($"Front asset '{payload.FrontAssetId}' was not found.");
            if (frontAsset.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(frontAsset.FileRelativePath))
                throw new InvalidOperationException("The front asset must be complete and have a stored image.");
            frontBytes = await ReadFileBytesAsync(frontAsset.FileRelativePath, cancellationToken);
        }
        else
        {
            var model = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
            var prompt = BuildFrontPortraitPrompt(payload.CharacterName, payload.Description);
            frontBytes = await _imageClient.GenerateAsync(model, prompt, "1024x1024", null, null, cancellationToken)
                ?? throw new InvalidOperationException("The image model returned no front portrait bytes.");
            frontModelLabel = model.ModelIdentifier;
        }

        // Remove any carried-forward face reference assets in the draft so a fresh Generate Profile
        // Pack REPLACES (not duplicates) the previous pack's faces. Supersede copies the prior
        // approved pack's assets forward; without this the draft would keep both the old copied
        // faces and the new generated ones (duplicate views, stale first-per-view in the UI).
        // Full-body / wardrobe assets carried forward are intentionally kept - only the 5 face views
        // that this job regenerates are cleared.
        var carriedFaces = (await _identityService.ListAssetsAsync(packId, cancellationToken))
            .Where(a => a.AssetKind == SceneImageReferenceAssetKind.Face)
            .ToList();
        foreach (var carried in carriedFaces)
        {
            await _identityService.DeleteAssetAsync(carried.Id, cancellationToken);
        }
        if (carriedFaces.Count > 0)
        {
            _logger.LogInformation(
                "Profile pack generation cleared {Count} carried-forward face asset(s) from draft {PackId}",
                carriedFaces.Count, packId);
        }

        // 3. Upload the front as the Front face view.
        await UploadFaceAsync(packId, payload, SceneImageReferenceFaceView.Front, SceneAssetKind.ProfilePackFront, frontBytes, "front.png", cancellationToken);

        // 4. Produce the four angle views via the canned Qwen edits from the front.
        var editor = await _editorModelResolver.ResolveAsync(cancellationToken);
        foreach (var (view, prompt) in AngleEdits)
        {
            var pending = await CreatePendingFaceAsync(payload, view, SceneAssetKind.ProfilePackFace, cancellationToken);
            try
            {
                await using var frontStream = new MemoryStream(frontBytes);
                var bytes = await _imageEditingClient.EditAsync(editor, frontStream, "front.png", prompt, cancellationToken);
                await CompleteFaceAsync(pending, packId, payload, view, bytes, $"{view}.png", cancellationToken);
            }
            catch (Exception ex)
            {
                await FailFaceAsync(pending, ex, cancellationToken);
                throw;
            }
        }

        _logger.LogInformation(
            "Profile pack generation completed: Character={Character}, Pack={PackId}, FrontModel={FrontModel}, Editor={Editor}",
            payload.CharacterProfileId, packId, frontModelLabel ?? "uploaded", editor.ModelIdentifier);
    }

    private async Task<string> EnsureDraftPackAsync(
        string characterProfileId, string? requestedPackId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedPackId))
        {
            var pack = await _identityService.GetPackAsync(requestedPackId, cancellationToken)
                ?? throw new InvalidOperationException($"Identity pack '{requestedPackId}' was not found.");
            if (pack.Status != CharacterImageIdentityPackStatus.Draft)
                throw new InvalidOperationException("Profile pack generation requires a draft identity pack.");
            return pack.Id;
        }

        var packs = await _identityService.ListPacksAsync(characterProfileId, cancellationToken);
        var draft = packs.FirstOrDefault(p => p.Status == CharacterImageIdentityPackStatus.Draft);
        if (draft is not null)
        {
            return draft.Id;
        }

        var approved = packs
            .Where(p => p.Status == CharacterImageIdentityPackStatus.Approved)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();
        if (approved is not null)
        {
            return (await _identityService.SupersedePackAsync(approved.Id, cancellationToken)).Id;
        }

        return (await _identityService.CreateDraftPackAsync(characterProfileId, cancellationToken)).Id;
    }

    private async Task UploadFaceAsync(
        string packId,
        SceneAssetProfilePackJobPayload payload,
        SceneImageReferenceFaceView view,
        SceneAssetKind kind,
        byte[] bytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var pending = await CreatePendingFaceAsync(payload, view, kind, cancellationToken);
        try
        {
            await CompleteFaceAsync(pending, packId, payload, view, bytes, fileName, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailFaceAsync(pending, ex, cancellationToken);
            throw;
        }
    }

    private async Task<SceneAsset> CreatePendingFaceAsync(
        SceneAssetProfilePackJobPayload payload,
        SceneImageReferenceFaceView view,
        SceneAssetKind kind,
        CancellationToken cancellationToken)
    {
        var asset = new SceneAsset
        {
            Name = $"{payload.CharacterName} — {view}",
            Kind = kind,
            Status = SceneAssetStatus.Pending,
            // Explicit valid type (never rely on the DB column default — a stale 'General' default
            // would break ParseEnum<SceneAssetType> on read). Profile-pack views are character faces.
            Type = SceneAssetType.CharacterFace,
            Prompt = kind == SceneAssetKind.ProfilePackFront ? payload.Description : string.Empty,
            SourceAssetId = payload.FrontAssetId,
            FaceView = view,
            CharacterProfileId = payload.CharacterProfileId,
            StartedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        await _repository.UpsertAsync(asset, cancellationToken);
        return asset;
    }

    private async Task CompleteFaceAsync(
        SceneAsset asset,
        string packId,
        SceneAssetProfilePackJobPayload payload,
        SceneImageReferenceFaceView view,
        byte[] bytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(bytes);
        var identityAsset = await _identityService.UploadAssetAsync(
            packId, SceneImageReferenceAssetKind.Face, fileName, stream, view, cancellationToken);

        asset.IdentityPackId = packId;
        asset.Status = SceneAssetStatus.Complete;
        asset.FileRelativePath = identityAsset.FileRelativePath;
        asset.MediaType = identityAsset.MediaType;
        asset.Width = identityAsset.Width;
        asset.Height = identityAsset.Height;
        asset.ByteLength = identityAsset.ByteLength;
        asset.Sha256 = identityAsset.Sha256;
        asset.CompletedUtc = DateTime.UtcNow;
        asset.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAsync(asset, cancellationToken);
    }

    private async Task FailFaceAsync(SceneAsset asset, Exception ex, CancellationToken cancellationToken)
    {
        asset.Status = SceneAssetStatus.Failed;
        asset.ErrorMessage = ex.Message;
        asset.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAsync(asset, cancellationToken);
    }

    private async Task<byte[]> ReadFileBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.OpenReadAsync(relativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string BuildFrontPortraitPrompt(string characterName, string description)
    {
        var name = string.IsNullOrWhiteSpace(characterName) ? "the character" : characterName.Trim();
        var desc = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
        var lead = string.IsNullOrWhiteSpace(desc)
            ? $"Photorealistic frontal portrait of {name}, head and shoulders, facing the camera."
            : $"Photorealistic frontal portrait of {name}: {desc}. Head and shoulders, facing the camera.";
        return $"{lead} Neutral background, even lighting, sharp focus, natural skin texture.";
    }
}
