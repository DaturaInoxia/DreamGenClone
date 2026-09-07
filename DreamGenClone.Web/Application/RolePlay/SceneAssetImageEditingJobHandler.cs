using System.Security.Cryptography;
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

public sealed class SceneAssetImageEditingJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneAssetRepository _assetRepository;
    private readonly ISceneAssetImageEditRepository _editRepository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IImageEditorModelResolver _modelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly IReferenceStrategyResolver _referenceStrategyResolver;
    private readonly ILogger<SceneAssetImageEditingJobHandler> _logger;

    public SceneAssetImageEditingJobHandler(ISceneAssetRepository assetRepository, ISceneAssetImageEditRepository editRepository, ISceneAssetStorageService storage, IImageEditorModelResolver modelResolver, IImageEditingClient imageEditingClient, IReferenceStrategyResolver referenceStrategyResolver, ILogger<SceneAssetImageEditingJobHandler> logger)
    {
        _assetRepository = assetRepository;
        _editRepository = editRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _imageEditingClient = imageEditingClient;
        _referenceStrategyResolver = referenceStrategyResolver;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneAssetImageEditing;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetImageEditingJobPayload>(payloadJson) ?? throw new InvalidOperationException("Asset image editing payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId) || string.IsNullOrWhiteSpace(payload.ImageId) || string.IsNullOrWhiteSpace(payload.EditorModelId)) throw new InvalidOperationException("Asset image editing payload requires asset, image, and exact editor model ids.");
        var image = await _assetRepository.GetImageAsync(payload.ImageId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit image '{payload.ImageId}' was not found.");
        if (!string.Equals(image.AssetId, payload.AssetId, StringComparison.Ordinal)) throw new InvalidOperationException("Asset edit image does not belong to the payload asset.");
        if (image.Status == SceneAssetStatus.Complete) return;
        if (image.Kind != SceneAssetKind.Edited || string.IsNullOrWhiteSpace(image.SourceImageId) || string.IsNullOrWhiteSpace(image.SourceProvenanceJson)) throw new InvalidOperationException("Asset image editing requires an edited image with source and compiler provenance.");
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _assetRepository.UpsertImageAsync(image, cancellationToken);
        SceneAssetImageEditSession? editSession = null;
        try
        {
            using var provenance = JsonDocument.Parse(image.SourceProvenanceJson);
            var root = provenance.RootElement;
            var editSessionId = root.GetProperty("editSessionId").GetString() ?? throw new InvalidOperationException("Asset edit provenance is missing its edit session id.");
            var attemptId = root.GetProperty("compilationAttemptId").GetString() ?? throw new InvalidOperationException("Asset edit provenance is missing its compilation attempt id.");
            var revisionId = root.GetProperty("promptRevisionId").GetString() ?? throw new InvalidOperationException("Asset edit provenance is missing its prompt revision id.");
            var sourceSha256 = root.GetProperty("sourceImageSha256").GetString() ?? throw new InvalidOperationException("Asset edit provenance is missing its source checksum.");
            var promptSha256 = root.GetProperty("promptSha256").GetString() ?? throw new InvalidOperationException("Asset edit provenance is missing its prompt checksum.");
            editSession = await _editRepository.GetSessionAsync(editSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{editSessionId}' was not found.");
            if (!string.Equals(editSession.AssetId, image.AssetId, StringComparison.Ordinal) || !string.Equals(editSession.SourceImageId, image.SourceImageId, StringComparison.Ordinal)) throw new InvalidOperationException("Asset edit provenance does not belong to the queued asset image.");
            var revision = await _editRepository.GetExecutableRevisionAsync(editSession.Id, attemptId, revisionId, sourceSha256, promptSha256, cancellationToken);
            if (!string.Equals(revision.Prompt, image.Prompt, StringComparison.Ordinal)) throw new InvalidOperationException("The queued asset edit prompt does not match the accepted prompt revision.");
            var source = await _assetRepository.GetImageAsync(image.SourceImageId, cancellationToken) ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
            if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal) || source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath)) throw new InvalidOperationException("The source asset image is not complete, stored, and owned by the queued asset.");
            await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var sourceBytes = await SceneImageMultimodalInput.ReadAsync(sourceStream, int.MaxValue, cancellationToken);
            if (!string.Equals(sourceBytes.Sha256, sourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source asset image checksum changed after edit execution was queued.");
            var editor = await _modelResolver.ResolveByIdAsync(payload.EditorModelId, cancellationToken);
            await using var editSource = new MemoryStream(sourceBytes.Bytes);
            var bytes = await ExecuteEditAsync(image, payload, editor, editSource, source.Id, cancellationToken);
            await using var output = new MemoryStream(bytes);
            var stored = await _storage.SaveAsync($"{image.Id}.png", output, cancellationToken);
            image.ModelSnapshotJson = JsonSerializer.Serialize(new { requestedModelId = payload.EditorModelId, editor.ModelIdentifier, editor.ProviderName });
            image.Status = SceneAssetStatus.Complete;
            image.FileRelativePath = stored.RelativePath;
            image.MediaType = stored.MediaType;
            image.Width = stored.Width;
            image.Height = stored.Height;
            image.ByteLength = stored.ByteLength;
            image.Sha256 = stored.Sha256;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = image.CompletedUtc.Value;
            await _assetRepository.UpsertImageAsync(image, cancellationToken);
            await _editRepository.UpdateSessionStatusAsync(editSession.Id, SceneAssetImageEditSessionStatus.Completed, DateTime.UtcNow, image.CompletedUtc, cancellationToken);
            _logger.LogInformation("Asset image edit completed: AssetId={AssetId}, ImageId={ImageId}, SourceImageId={SourceImageId}, Model={Model}", image.AssetId, image.Id, source.Id, editor.ModelIdentifier);
        }
        catch (Exception ex)
        {
            image.Status = SceneAssetStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _assetRepository.UpsertImageAsync(image, cancellationToken);
            if (editSession is not null)
            {
                var latestAttempt = await _editRepository.GetLatestAttemptAsync(editSession.Id, cancellationToken);
                if (latestAttempt is not null)
                    await _editRepository.UpdateSessionStatusAsync(editSession.Id, SceneAssetImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            }
            _logger.LogWarning(ex, "Asset image edit failed: AssetId={AssetId}, ImageId={ImageId}", image.AssetId, image.Id);
            throw;
        }
    }

    private async Task<byte[]> ExecuteEditAsync(SceneAssetImage image, SceneAssetImageEditingJobPayload payload, ResolvedImageEditorModel editor, Stream sourceStream, string sourceImageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.ReferenceApplicationsJson))
            return await _imageEditingClient.EditAsync(editor, sourceStream, $"{sourceImageId}.png", image.Prompt, cancellationToken);

        var applications = JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(payload.ReferenceApplicationsJson, JsonOptions)
            ?? throw new InvalidOperationException("Asset edit reference applications are invalid.");
        var referenceApplications = applications.Where(application => application.UsesReference).ToList();
        if (referenceApplications.Count == 0)
            return await _imageEditingClient.EditAsync(editor, sourceStream, $"{sourceImageId}.png", image.Prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(editor.RegisteredModelId))
            throw new InvalidOperationException("Asset reference editing requires the exact registered editor model id.");

        var streams = new List<Stream>(referenceApplications.Count);
        try
        {
            var references = new List<ImageEditingReference>(referenceApplications.Count);
            for (var index = 0; index < referenceApplications.Count; index++)
            {
                var application = referenceApplications[index];
                var resolution = await _referenceStrategyResolver.ResolveAsync(editor.RegisteredModelId, application.Strategy, cancellationToken);
                if (!resolution.IsAvailable)
                    throw new InvalidOperationException($"Asset edit reference strategy '{application.Strategy}' for '{application.ElementKey}' is unavailable: {resolution.Reason}");
                if (!string.Equals(resolution.Strategy, "ReferenceConditioning", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Asset edit reference strategy '{resolution.Strategy}' for '{application.ElementKey}' is qualified but has no implemented Qwen reference-edit graph.");
                var assetImage = await _assetRepository.GetImageAsync(application.SceneAssetImageId!, cancellationToken)
                    ?? throw new InvalidOperationException($"Asset edit reference image '{application.SceneAssetImageId}' was not found.");
                if (!string.Equals(assetImage.AssetId, application.SceneAssetId, StringComparison.Ordinal)
                    || assetImage.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved
                    || assetImage.ProductionVersion != application.SceneAssetVersion
                    || !string.Equals(assetImage.Sha256, application.SceneAssetSha256, StringComparison.Ordinal)
                    || assetImage.Status != SceneAssetStatus.Complete
                    || string.IsNullOrWhiteSpace(assetImage.FileRelativePath))
                {
                    throw new InvalidOperationException($"Asset edit reference image '{application.SceneAssetImageId}' no longer matches its approved immutable selection.");
                }
                var stream = await _storage.OpenReadAsync(assetImage.FileRelativePath, cancellationToken);
                streams.Add(stream);
                references.Add(new ImageEditingReference(index + 1, application.SemanticRole, stream, $"{assetImage.Id}.png", assetImage.Sha256));
            }
            return await _imageEditingClient.EditWithReferencesAsync(editor, sourceStream, $"{sourceImageId}.png", image.Prompt, references, cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }
}