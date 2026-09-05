using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Runs a manual Qwen source-image edit using the dedicated editor configuration.</summary>
public sealed class SceneImageEditingJobHandler : IBackgroundJobHandler
{
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly ICharacterImageAssetStorageService? _identityStorage;
    private readonly IImageEditorModelResolver _modelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<SceneImageEditingJobHandler> _logger;

    public SceneImageEditingJobHandler(
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        ILogger<SceneImageEditingJobHandler> logger)
        : this(repository, editRepository, storage, modelResolver, imageEditingClient, null, null, logger)
    {
    }

    public SceneImageEditingJobHandler(
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        ICharacterImageAssetStorageService? identityStorage,
        ILogger<SceneImageEditingJobHandler> logger)
        : this(repository, editRepository, storage, modelResolver, imageEditingClient, identityStorage, null, logger)
    {
    }

    public SceneImageEditingJobHandler(
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        ICharacterImageAssetStorageService? identityStorage,
        IRolePlayDebugEventSink? debugEventSink,
        ILogger<SceneImageEditingJobHandler> logger)
    {
        _repository = repository;
        _editRepository = editRepository;
        _storage = storage;
        _identityStorage = identityStorage;
        _modelResolver = modelResolver;
        _imageEditingClient = imageEditingClient;
        _debugEventSink = debugEventSink;
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
            if (image.ProductionStage == SceneImageProductionStage.Identity)
            {
                await ExecuteIdentityAsync(image, payload, cancellationToken);
                return;
            }
            if (image.ProductionStage == SceneImageProductionStage.Finish)
            {
                await ExecuteFinishAsync(image, payload, cancellationToken);
                return;
            }
            if (string.IsNullOrWhiteSpace(image.EditSessionId)
                || string.IsNullOrWhiteSpace(image.EditCompilationAttemptId)
                || string.IsNullOrWhiteSpace(image.EditPromptRevisionId)
                || string.IsNullOrWhiteSpace(image.EditCompilerProvenanceJson))
                throw new InvalidOperationException("Scene image editing requires exact compiler provenance.");
            using var provenance = JsonDocument.Parse(image.EditCompilerProvenanceJson);
            var sourceSha256 = provenance.RootElement.GetProperty("sourceImageSha256").GetString()
                ?? throw new InvalidOperationException("Edit provenance is missing the source checksum.");
            var promptSha256 = provenance.RootElement.GetProperty("promptSha256").GetString()
                ?? throw new InvalidOperationException("Edit provenance is missing the prompt checksum.");
            var revision = await _editRepository.GetExecutableRevisionAsync(
                image.EditSessionId,
                image.EditCompilationAttemptId,
                image.EditPromptRevisionId,
                sourceSha256,
                promptSha256,
                cancellationToken);
            if (!string.Equals(revision.Prompt, image.PromptSnapshot, StringComparison.Ordinal))
                throw new InvalidOperationException("The queued edit prompt does not match the accepted prompt revision.");

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
            image.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            image.Status = SceneImageStatus.Complete;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);
            var latestAttempt = await _editRepository.GetLatestAttemptAsync(image.EditSessionId, cancellationToken);
            if (latestAttempt?.Id == image.EditCompilationAttemptId)
                await _editRepository.UpdateSessionStatusAsync(image.EditSessionId, SceneImageEditSessionStatus.Completed, DateTime.UtcNow, image.CompletedUtc, cancellationToken);

            _logger.LogInformation("Scene image edit completed: SessionId={SessionId}, InteractionId={InteractionId}, ImageRecordId={ImageRecordId}, SourceImageId={SourceImageId}, Model={ModelIdentifier}, DurationMs={DurationMs}", payload.SessionId, payload.InteractionId, image.Id, source.Id, resolved.ModelIdentifier, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            image.Status = SceneImageStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);
            if (!string.IsNullOrWhiteSpace(image.EditSessionId) && !string.IsNullOrWhiteSpace(image.EditCompilationAttemptId))
            {
                var latestAttempt = await _editRepository.GetLatestAttemptAsync(image.EditSessionId, cancellationToken);
                if (latestAttempt?.Id == image.EditCompilationAttemptId)
                    await _editRepository.UpdateSessionStatusAsync(image.EditSessionId, SceneImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            }
            _logger.LogWarning(ex, "Scene image edit failed: SessionId={SessionId}, ImageRecordId={ImageRecordId}", payload.SessionId, image.Id);
            throw;
        }
    }

    private async Task ExecuteIdentityAsync(
        SceneImageRecord image,
        SceneImageEditingJobPayload payload,
        CancellationToken cancellationToken)
    {
        var identityStorage = _identityStorage
            ?? throw new InvalidOperationException("Identity image editing requires identity asset storage.");
        if (string.IsNullOrWhiteSpace(image.SourceImageId)
            || string.IsNullOrWhiteSpace(image.IdentityReferenceBindingsJson))
            throw new InvalidOperationException("Identity image editing requires a source image and persisted reference bindings.");
        var source = await _repository.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{image.SourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Identity source image must be complete and stored.");
        var bindings = JsonSerializer.Deserialize<IReadOnlyList<IdentityBinding>>(
            image.IdentityReferenceBindingsJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Identity reference bindings are invalid.");
        if (bindings.Count == 0 || bindings.Select(x => x.Ordinal).SequenceEqual(bindings.Select(x => x.Ordinal).OrderBy(x => x)) is false)
            throw new InvalidOperationException("Identity reference bindings must be non-empty and ordinally ordered.");

        var references = new List<ImageEditingReference>(bindings.Count);
        await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
        var referenceStreams = new List<Stream>(bindings.Count);
        try
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.FileRelativePath) || string.IsNullOrWhiteSpace(binding.Sha256))
                    throw new InvalidOperationException("Identity reference binding is missing exact asset metadata.");
                var stream = await identityStorage.OpenReadAsync(binding.FileRelativePath, cancellationToken);
                referenceStreams.Add(stream);
                references.Add(new ImageEditingReference(
                    binding.Ordinal,
                    $"CharacterFace:{binding.CharacterId}",
                    stream,
                    $"{binding.CharacterId}.png",
                    binding.Sha256));
            }

            var resolved = await _modelResolver.ResolveAsync(cancellationToken);
            await _loggerIdentityDispatchAsync(image, payload, resolved, source.Sha256, bindings, cancellationToken);
            var bytes = await _imageEditingClient.EditWithReferencesAsync(
                resolved,
                sourceStream,
                $"{source.Id}.png",
                image.PromptSnapshot,
                references,
                cancellationToken);
            await using var outputStream = new MemoryStream(bytes);
            image.FileRelativePath = await _storage.SaveAsync(payload.SessionId, $"{image.Id}.png", outputStream, cancellationToken);
            image.ModelIdentifier = resolved.ModelIdentifier;
            image.ProviderName = resolved.ProviderName;
            image.ContentPolicy = resolved.ContentPolicy;
            image.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            image.Status = SceneImageStatus.Complete;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);
        }
        finally
        {
            foreach (var stream in referenceStreams)
                await stream.DisposeAsync();
        }
    }

    private async Task ExecuteFinishAsync(
        SceneImageRecord image,
        SceneImageEditingJobPayload payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException("Finish image editing requires a source image id.");
        var source = await _repository.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{image.SourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Finish source image must be complete and stored.");
        var resolved = await _modelResolver.ResolveAsync(cancellationToken);
        var requestAdultContent = false;
        if (!string.IsNullOrWhiteSpace(image.EditIntentSnapshot))
        {
            using var intent = JsonDocument.Parse(image.EditIntentSnapshot);
            requestAdultContent = intent.RootElement.TryGetProperty("RequestAdultContent", out var value) && value.GetBoolean();
        }
        if (requestAdultContent && resolved.ContentPolicy is ImageContentPolicy.SfwFiltered or ImageContentPolicy.Unknown)
            throw new InvalidOperationException("Adult-content Finish edits are unavailable because the resolved editor model does not allow them.");

        await using var sourceStream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
        var bytes = await _imageEditingClient.EditAsync(
            resolved, sourceStream, $"{source.Id}.png", image.PromptSnapshot, cancellationToken);
        await using var outputStream = new MemoryStream(bytes);
        image.FileRelativePath = await _storage.SaveAsync(payload.SessionId, $"{image.Id}.png", outputStream, cancellationToken);
        image.ModelIdentifier = resolved.ModelIdentifier;
        image.ProviderName = resolved.ProviderName;
        image.ContentPolicy = resolved.ContentPolicy;
        image.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        image.Status = SceneImageStatus.Complete;
        image.CompletedUtc = DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.InsertImageAsync(image, cancellationToken);
    }

    private Task _loggerIdentityDispatchAsync(
        SceneImageRecord image,
        SceneImageEditingJobPayload payload,
        ResolvedImageEditorModel resolved,
        string? sourceSha256,
        IReadOnlyList<IdentityBinding> bindings,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "IdentityDispatch record={RecordId} session={SessionId} interaction={InteractionId} provider={Provider} model={Model} sourceSha256={SourceSha256} references={References}",
            image.Id,
            payload.SessionId,
            payload.InteractionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            sourceSha256,
            JsonSerializer.Serialize(bindings.Select(binding => new { binding.Ordinal, binding.CharacterId, binding.IdentityPackId, binding.IdentityPackVersion, binding.CanonicalFaceAssetId, binding.Sha256 })));
        if (_debugEventSink is null)
            return Task.CompletedTask;

        return _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = payload.SessionId,
            InteractionId = payload.InteractionId,
            EventKind = "SceneImageIdentityDispatch",
            Severity = "Info",
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            Summary = "Dispatched identity edit with ordered canonical face references.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                imageRecordId = image.Id,
                sourceImageId = image.SourceImageId,
                instruction = image.PromptSnapshot,
                settingsJson = image.SettingsJson,
                sourceSha256,
                references = bindings.Select(binding => new
                {
                    binding.Ordinal,
                    binding.CharacterId,
                    binding.IdentityPackId,
                    binding.IdentityPackVersion,
                    binding.CanonicalFaceAssetId,
                    binding.Sha256
                })
            })
        }, cancellationToken);
    }

    private sealed record IdentityBinding(
        int Ordinal,
        string CharacterId,
        string CharacterName,
        string IdentityPackId,
        int IdentityPackVersion,
        string CanonicalFaceAssetId,
        string FileRelativePath,
        string Sha256);
}