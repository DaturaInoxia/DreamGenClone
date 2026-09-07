using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneAssetImageEditCompilationService : ISceneAssetImageEditCompilationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneAssetRepository _assetRepository;
    private readonly ISceneAssetImageEditRepository _editRepository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly ISceneImageEditPromptCompiler _compiler;
    private readonly IDurableBackgroundJobQueue _queue;
    private readonly ISceneBeatAnalyzerResolver _durableSettingsResolver;
    private readonly TimeProvider _timeProvider;

    public SceneAssetImageEditCompilationService(ISceneAssetRepository assetRepository, ISceneAssetImageEditRepository editRepository, ISceneAssetStorageService storage, IMultimodalModelResolutionService modelResolver, ISceneImageEditPromptCompiler compiler, IDurableBackgroundJobQueue queue, ISceneBeatAnalyzerResolver durableSettingsResolver, TimeProvider timeProvider)
    {
        _assetRepository = assetRepository;
        _editRepository = editRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _compiler = compiler;
        _queue = queue;
        _durableSettingsResolver = durableSettingsResolver;
        _timeProvider = timeProvider;
    }

    public async Task<SceneAssetImageEditSession> CreateSessionAsync(CreateSceneAssetImageEditSessionRequest request, CancellationToken cancellationToken = default)
    {
        var source = await RequireSourceAsync(request.AssetId, request.SourceImageId, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, int.MaxValue, cancellationToken);
        RequireUnchanged(input.Sha256, source.Sha256,
            "The stored source asset image bytes do not match their persisted checksum.");
        var session = new SceneAssetImageEditSession { AssetId = source.AssetId, SourceImageId = source.Id, SourceImageSha256 = input.Sha256, Status = SceneAssetImageEditSessionStatus.Active };
        await _editRepository.CreateSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<SceneAssetImageEditCompilationAttempt> EnqueueCompilationAsync(EnqueueSceneAssetImageEditCompilationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawIntent)) throw new InvalidOperationException("A non-empty edit intent is required for compilation.");
        if (request.ClarificationHistory.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Clarification history cannot contain empty entries.");
        var session = await GetActiveSessionAsync(request.EditSessionId, cancellationToken);
        var source = await RequireSourceAsync(session.AssetId, session.SourceImageId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        RequireUnchanged(input.Sha256, session.SourceImageSha256, "The source asset image checksum changed after the edit session was created.");
        var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(request.RawIntent.Trim(), request.ClarificationHistory));
        var latest = await _editRepository.GetLatestAttemptAsync(session.Id, cancellationToken);
        var attempt = new SceneAssetImageEditCompilationAttempt { EditSessionId = session.Id, Ordinal = latest is null ? 0 : latest.Ordinal + 1, RawIntent = request.RawIntent.Trim(), ClarificationContextJson = request.ClarificationHistory.Count == 0 ? null : JsonSerializer.Serialize(request.ClarificationHistory, JsonOptions), SourceImageSha256 = input.Sha256, Status = SceneImageEditCompilationAttemptStatus.Pending, ResolvedModelSnapshotJson = SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved), CompilerSchemaVersion = messages.SchemaVersion, SystemPromptVersion = messages.SystemPromptVersion };
        await _editRepository.CreateAttemptAsync(attempt, cancellationToken);
        await _editRepository.UpdateSessionStatusAsync(session.Id, SceneAssetImageEditSessionStatus.Active, DateTime.UtcNow, cancellationToken: cancellationToken);
        await EnqueueAsync(BackgroundJobTypes.SceneAssetImageEditPromptCompilation, DurableJobLane.PromptCompilation, new SceneAssetImageEditCompilationJobPayload { AttemptId = attempt.Id }, attempt.Id, "Compilation attempt", cancellationToken);
        return attempt;
    }

    public async Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(editSessionId, cancellationToken);
        if (!force && !string.IsNullOrWhiteSpace(session.DescriptionText)) return;
        var source = await RequireSourceAsync(session.AssetId, session.SourceImageId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        RequireUnchanged(input.Sha256, session.SourceImageSha256, "The source asset image checksum changed after the edit session was created.");
        await EnqueueAsync(BackgroundJobTypes.SceneAssetImageEditDescription, DurableJobLane.PromptCompilation, new SceneAssetImageEditDescriptionJobPayload { EditSessionId = session.Id }, session.Id, "Asset edit session description", cancellationToken);
    }

    public async Task<SceneAssetImageEditPromptRevision> AppendPromptRevisionAsync(AppendSceneAssetImageEditPromptRevisionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) throw new InvalidOperationException("A non-empty revised prompt is required.");
        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{request.CompilationAttemptId}' was not found.");
        if (!string.Equals(attempt.EditSessionId, request.EditSessionId, StringComparison.Ordinal)) throw new InvalidOperationException("The compilation attempt does not belong to the selected asset edit session.");
        var revisions = await _editRepository.ListRevisionsAsync(attempt.Id, cancellationToken);
        var prompt = request.Prompt.Trim();
        var revision = new SceneAssetImageEditPromptRevision { CompilationAttemptId = attempt.Id, Ordinal = revisions.Count, Prompt = prompt, RevisionKind = SceneImageEditPromptRevisionKind.UserEdited, PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))) };
        await _editRepository.CreateRevisionAsync(revision, cancellationToken);
        return revision;
    }

    public async Task<SceneAssetImage> EnqueueEditAsync(EnqueueSceneAssetImageEditRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EditorModelId)) throw new InvalidOperationException("An exact asset image editor model is required.");
        var source = await RequireSourceAsync(request.AssetId, request.SourceImageId, cancellationToken);
        var session = await GetActiveSessionAsync(request.EditSessionId, cancellationToken);
        if (!string.Equals(session.AssetId, source.AssetId, StringComparison.Ordinal) || !string.Equals(session.SourceImageId, source.Id, StringComparison.Ordinal)) throw new InvalidOperationException("The asset edit session does not belong to the selected source image and asset.");
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, int.MaxValue, cancellationToken);
        RequireUnchanged(input.Sha256, session.SourceImageSha256, "The source asset image checksum changed after the edit session was created.");
        RequireUnchanged(input.Sha256, request.SourceImageSha256, "The selected source asset image checksum is stale.");
        var revision = await _editRepository.GetExecutableRevisionAsync(session.Id, request.CompilationAttemptId, request.PromptRevisionId, request.SourceImageSha256, request.PromptSha256, cancellationToken);
        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{request.CompilationAttemptId}' was not found.");
        var provenance = JsonSerializer.Serialize(new { editSessionId = session.Id, compilationAttemptId = attempt.Id, attemptOrdinal = attempt.Ordinal, promptRevisionId = revision.Id, revisionOrdinal = revision.Ordinal, sourceImageSha256 = session.SourceImageSha256, promptSha256 = revision.PromptSha256, attempt.CompilerSchemaVersion, attempt.SystemPromptVersion, resolvedModelSnapshot = JsonSerializer.Deserialize<JsonElement>(attempt.ResolvedModelSnapshotJson) }, JsonOptions);
        var referenceApplicationsJson = SerializeReferenceApplications(request.ReferenceApplications);
        var candidateBatchId = string.IsNullOrWhiteSpace(request.CandidateBatchId) ? null : request.CandidateBatchId.Trim();
        var image = new SceneAssetImage
        {
            AssetId = source.AssetId,
            Kind = SceneAssetKind.Edited,
            Status = SceneAssetStatus.Pending,
            Prompt = revision.Prompt,
            SourceImageId = source.Id,
            SourceProvenanceJson = provenance,
            CandidateBatchId = candidateBatchId,
            CandidateDecision = candidateBatchId is null ? null : SceneAssetCandidateDecision.Undecided
        };
        await _assetRepository.UpsertImageAsync(image, cancellationToken);
        await EnqueueAsync(BackgroundJobTypes.SceneAssetImageEditing, DurableJobLane.ImageEdit, new SceneAssetImageEditingJobPayload
        {
            AssetId = source.AssetId,
            ImageId = image.Id,
            EditorModelId = request.EditorModelId.Trim(),
            CandidateBatchId = image.CandidateBatchId,
            ReferenceApplicationsJson = referenceApplicationsJson
        }, image.Id, "Asset image edit", cancellationToken);
        return image;
    }

    public Task<SceneAssetImageEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default) => _editRepository.GetSessionAsync(editSessionId, cancellationToken);
    public Task<SceneAssetImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default) => _editRepository.GetLatestAttemptAsync(editSessionId, cancellationToken);
    public Task<IReadOnlyList<SceneAssetImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default) => _editRepository.ListRevisionsAsync(attemptId, cancellationToken);

    private async Task<SceneAssetImageEditSession> GetActiveSessionAsync(string editSessionId, CancellationToken cancellationToken)
    {
        var session = await _editRepository.GetSessionAsync(editSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{editSessionId}' was not found.");
        if (session.Status == SceneAssetImageEditSessionStatus.Completed) throw new InvalidOperationException("A completed asset edit session cannot be changed.");
        return session;
    }

    private async Task<SceneAssetImage> RequireSourceAsync(string assetId, string sourceImageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(sourceImageId)) throw new InvalidOperationException("Asset and source image ids are required.");
        var asset = await _assetRepository.GetAsync(assetId, cancellationToken) ?? throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
        var source = await _assetRepository.GetImageAsync(sourceImageId, cancellationToken) ?? throw new InvalidOperationException($"Source scene asset image '{sourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, asset.Id, StringComparison.Ordinal) || source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath)) throw new InvalidOperationException("Only a complete stored image owned by the selected asset can be edited.");
        return source;
    }

    private async Task EnqueueAsync<TPayload>(string jobType, DurableJobLane lane, TPayload payload, string dedupeKey, string label, CancellationToken cancellationToken)
    {
        var settings = await _durableSettingsResolver.ResolveAsync(cancellationToken);
        var createdUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var job = new DurableBackgroundJob
        {
            Id = $"{jobType}:{dedupeKey}",
            JobType = jobType,
            Lane = lane,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            DedupeKey = $"{jobType}:{dedupeKey}",
            MaxAttempts = settings.RetryDelaysSeconds.Count + 1,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc
        };
        if (!await _queue.TryEnqueueAsync(job, cancellationToken)) throw new InvalidOperationException($"{label} '{dedupeKey}' is already queued.");
    }

    private static string? SerializeReferenceApplications(IReadOnlyList<ReferenceApplicationSelection>? applications)
    {
        if (applications is not { Count: > 0 }) return null;
        if (applications.Any(application => string.IsNullOrWhiteSpace(application.ElementKey)
            || string.IsNullOrWhiteSpace(application.SemanticRole)
            || string.IsNullOrWhiteSpace(application.Strategy)))
        {
            throw new InvalidOperationException("Every asset reference application requires an element key, semantic role, and strategy.");
        }
        if (applications.Select(application => application.ElementKey.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != applications.Count)
            throw new InvalidOperationException("Asset reference application element keys must be unique.");
        foreach (var application in applications)
        {
            var hasAsset = !string.IsNullOrWhiteSpace(application.SceneAssetId);
            if (hasAsset && (string.IsNullOrWhiteSpace(application.SceneAssetImageId)
                || application.SceneAssetVersion is null
                || string.IsNullOrWhiteSpace(application.SceneAssetSha256)))
            {
                throw new InvalidOperationException($"Asset reference application '{application.ElementKey}' is missing an exact approved asset version or checksum.");
            }
            if (!hasAsset && !string.Equals(application.Strategy, "TextOnly", StringComparison.Ordinal))
                throw new InvalidOperationException($"Asset reference application '{application.ElementKey}' requires an approved asset for strategy '{application.Strategy}'.");
            if (application.Strength is < 0m or > 1m)
                throw new InvalidOperationException($"Asset reference application '{application.ElementKey}' strength must be between 0 and 1.");
        }
        return JsonSerializer.Serialize(applications, JsonOptions);
    }

    private static void RequireUnchanged(string actual, string expected, string message)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message);
    }
}