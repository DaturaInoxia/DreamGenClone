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
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE prompt-compilation lifecycle for every editable image. Behaviour is the union of the two
/// services it replaced, so a fix lands once — including the source-description job, which the asset
/// path previously enqueued without the durable queue.
/// </summary>
public sealed class MediaEditCompilationService : IMediaEditCompilationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMediaEditRepository _editRepository;
    private readonly MediaEditSubjectSourceResolver _sources;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly ISceneImageEditPromptCompiler _compiler;
    private readonly IDurableBackgroundJobQueue _queue;
    private readonly ISceneBeatAnalyzerResolver _durableSettingsResolver;
    private readonly TimeProvider _timeProvider;
    private readonly IImageEditorModelResolver _editorModels;
    private readonly IImageEditorEndpointReadiness _endpointReadiness;

    public MediaEditCompilationService(
        IMediaEditRepository editRepository,
        MediaEditSubjectSourceResolver sources,
        IMultimodalModelResolutionService modelResolver,
        ISceneImageEditPromptCompiler compiler,
        IDurableBackgroundJobQueue queue,
        ISceneBeatAnalyzerResolver durableSettingsResolver,
        TimeProvider timeProvider,
        IImageEditorModelResolver editorModels,
        IImageEditorEndpointReadiness endpointReadiness)
    {
        _editRepository = editRepository;
        _sources = sources;
        _modelResolver = modelResolver;
        _compiler = compiler;
        _queue = queue;
        _durableSettingsResolver = durableSettingsResolver;
        _timeProvider = timeProvider;
        _editorModels = editorModels;
        _endpointReadiness = endpointReadiness;
    }

    public async Task<MediaEditSession> CreateSessionAsync(
        CreateMediaEditSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("A media edit session requires an explicit subject kind.");

        var subject = new MediaEditSubjectRef(request.SubjectKind, request.SubjectId, request.SubjectScopeId);
        var source = await RequireSourceAsync(subject, request.SourceImageId, cancellationToken);
        var input = await ReadSourceAsync(subject, source, int.MaxValue, cancellationToken);

        var session = new MediaEditSession
        {
            SubjectKind = request.SubjectKind,
            SubjectId = request.SubjectId.Trim(),
            SubjectScopeId = string.IsNullOrWhiteSpace(request.SubjectScopeId) ? null : request.SubjectScopeId.Trim(),
            SourceImageId = source.ImageId,
            SourceImageSha256 = input.Sha256,
            Status = MediaEditSessionStatus.Active
        };
        await _editRepository.CreateSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<MediaEditCompilationAttempt> EnqueueCompilationAsync(
        EnqueueMediaEditCompilationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RawIntent))
            throw new InvalidOperationException("A non-empty edit intent is required for compilation.");
        if (request.ClarificationHistory.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Clarification history cannot contain empty entries.");

        var session = await GetActiveSessionAsync(request.EditSessionId, cancellationToken);
        var subject = MediaEditSubjectRef.FromSession(session);
        var source = await RequireSourceAsync(subject, session.SourceImageId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        var input = await ReadValidatedSourceAsync(subject, source, resolved, cancellationToken);
        RequireUnchangedChecksum(input.Sha256, session.SourceImageSha256,
            "The source image checksum changed after the edit session was created.");

        var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(request.RawIntent.Trim(), request.ClarificationHistory));
        var latest = await _editRepository.GetLatestAttemptAsync(session.Id, cancellationToken);
        var attempt = new MediaEditCompilationAttempt
        {
            EditSessionId = session.Id,
            Ordinal = latest is null ? 0 : latest.Ordinal + 1,
            RawIntent = request.RawIntent.Trim(),
            ClarificationContextJson = request.ClarificationHistory.Count == 0
                ? null
                : JsonSerializer.Serialize(request.ClarificationHistory, JsonOptions),
            SourceImageSha256 = input.Sha256,
            Status = SceneImageEditCompilationAttemptStatus.Pending,
            ResolvedModelSnapshotJson = SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved),
            CompilerSchemaVersion = messages.SchemaVersion,
            SystemPromptVersion = messages.SystemPromptVersion
        };
        await _editRepository.CreateAttemptAsync(attempt, cancellationToken);
        await _editRepository.UpdateSessionStatusAsync(session.Id, MediaEditSessionStatus.Active, DateTime.UtcNow, cancellationToken: cancellationToken);
        await EnqueueAsync(
            BackgroundJobTypes.MediaEditPromptCompilation,
            DurableJobLane.PromptCompilation,
            new MediaEditCompilationJobPayload { AttemptId = attempt.Id },
            attempt.Id,
            "Compilation attempt",
            await ResolveMaxAttemptsAsync(cancellationToken),
            DurableBackgroundJobStatus.Queued,
            cancellationToken);
        return attempt;
    }

    public async Task EnqueueDescriptionAsync(
        string editSessionId, bool force = false, CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(editSessionId, cancellationToken);
        if (!force && !string.IsNullOrWhiteSpace(session.DescriptionText))
            return;

        var subject = MediaEditSubjectRef.FromSession(session);
        var source = await RequireSourceAsync(subject, session.SourceImageId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        var input = await ReadValidatedSourceAsync(subject, source, resolved, cancellationToken);
        RequireUnchangedChecksum(input.Sha256, session.SourceImageSha256,
            "The source image checksum changed after the edit session was created.");

        await EnqueueAsync(
            BackgroundJobTypes.MediaEditDescription,
            DurableJobLane.PromptCompilation,
            new MediaEditDescriptionJobPayload { EditSessionId = session.Id },
            session.Id,
            "Media edit session description",
            await ResolveMaxAttemptsAsync(cancellationToken),
            DurableBackgroundJobStatus.Queued,
            cancellationToken);
    }

    public async Task EnqueueRunAsync(
        MediaEditRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("A media edit run requires an explicit subject kind.");
        if (string.IsNullOrWhiteSpace(request.ImageId))
            throw new InvalidOperationException("A media edit run requires the image row it was queued for.");
        if (string.IsNullOrWhiteSpace(request.EditorModelId))
            throw new InvalidOperationException(
                "A media edit run requires the explicitly chosen image editor model; the run must not pick one for itself.");
        if (request.MaxAttempts < 1)
            throw new InvalidOperationException("A media edit run requires an explicit attempt budget of at least one.");

        var imageId = request.ImageId.Trim();
        var editorModelId = request.EditorModelId.Trim();

        // The chosen model decides admission, and it is carried on the job so the run uses exactly the
        // model it was queued for. A local ComfyUI endpoint is always running (no cold start to pay for),
        // so it runs immediately; a serverless endpoint stages until it is warm, and a user start-now
        // activates it. Resolving the model again at run time is what would let the two disagree.
        var editorModel = await _editorModels.ResolveByIdAsync(editorModelId, cancellationToken);
        var admission = await ResolveAdmissionAsync(editorModel, cancellationToken);

        await EnqueueAsync(
            BackgroundJobTypes.MediaEditImageEditing,
            DurableJobLane.ImageEdit,
            new MediaEditImageEditingJobPayload
            {
                SubjectKind = request.SubjectKind,
                ImageId = imageId,
                OperationKind = MediaEditOperationKind.Edit,
                EditorModelId = editorModelId,
                ReferenceApplicationsJson = request.ReferenceApplicationsJson,
                ScopeId = string.IsNullOrWhiteSpace(request.ScopeId) ? null : request.ScopeId.Trim()
            },
            imageId,
            "Image edit",
            request.MaxAttempts,
            admission,
            cancellationToken);
    }

    public async Task EnqueueOperationRunAsync(
        MediaEditOperationRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("A media edit operation run requires an explicit subject kind.");
        if (string.IsNullOrWhiteSpace(request.ImageId))
            throw new InvalidOperationException("A media edit operation run requires the image row it was queued for.");
        if (request.Operation is null)
            throw new InvalidOperationException("A media edit operation run requires its operation parameters.");
        if (request.Operation.Kind == MediaEditOperationKind.Edit)
            throw new InvalidOperationException(
                "A media edit operation run must not be an edit; edits go through EnqueueRunAsync with their chosen editor model.");
        if (request.MaxAttempts < 1)
            throw new InvalidOperationException(
                "A media edit operation run requires an explicit attempt budget of at least one.");

        // Validate before queueing so a bad operation is refused at enqueue time, not inside the worker.
        request.Operation.Validate();

        var imageId = request.ImageId.Trim();

        // No model resolution and no endpoint readiness check: an operation is deterministic and local,
        // so nothing about it depends on a model being chosen or an endpoint being warm.
        await EnqueueAsync(
            BackgroundJobTypes.MediaEditImageEditing,
            DurableJobLane.ImageEdit,
            new MediaEditImageEditingJobPayload
            {
                SubjectKind = request.SubjectKind,
                ImageId = imageId,
                OperationKind = request.Operation.Kind,
                OperationJson = JsonSerializer.Serialize(request.Operation, JsonOptions),
                ScopeId = string.IsNullOrWhiteSpace(request.ScopeId) ? null : request.ScopeId.Trim()
            },
            imageId,
            "Image operation",
            request.MaxAttempts,
            DurableBackgroundJobStatus.Queued,
            cancellationToken);
    }

    /// <summary>
    /// Auto-run versus wait-for-user-start, decided from the chosen model alone: only a serverless
    /// endpoint can be cold, so only it can stage.
    /// </summary>
    private async Task<DurableBackgroundJobStatus> ResolveAdmissionAsync(
        ResolvedImageEditorModel editorModel, CancellationToken cancellationToken)
    {
        if (editorModel.ImageProtocol != ImageProtocol.ComfyUiServerless)
            return DurableBackgroundJobStatus.Queued;

        return await _endpointReadiness.IsWarmAsync(editorModel, cancellationToken)
            ? DurableBackgroundJobStatus.Queued
            : DurableBackgroundJobStatus.Staged;
    }

    public async Task<MediaEditPromptRevision> AppendPromptRevisionAsync(
        AppendMediaEditPromptRevisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new InvalidOperationException("A non-empty revised prompt is required.");

        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Media compilation attempt '{request.CompilationAttemptId}' was not found.");
        if (!string.Equals(attempt.EditSessionId, request.EditSessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The compilation attempt does not belong to the selected edit session.");

        var revisions = await _editRepository.ListRevisionsAsync(attempt.Id, cancellationToken);
        var prompt = request.Prompt.Trim();
        var revision = new MediaEditPromptRevision
        {
            CompilationAttemptId = attempt.Id,
            Ordinal = revisions.Count,
            Prompt = prompt,
            RevisionKind = SceneImageEditPromptRevisionKind.UserEdited,
            PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))
        };
        await _editRepository.CreateRevisionAsync(revision, cancellationToken);
        return revision;
    }

    public Task<MediaEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default)
        => _editRepository.GetSessionAsync(editSessionId, cancellationToken);

    public Task<MediaEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default)
        => _editRepository.GetLatestAttemptAsync(editSessionId, cancellationToken);

    public Task<IReadOnlyList<MediaEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default)
        => _editRepository.ListRevisionsAsync(attemptId, cancellationToken);

    private async Task<MediaEditSession> GetActiveSessionAsync(string editSessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(editSessionId))
            throw new InvalidOperationException("A media edit session id is required.");

        var session = await _editRepository.GetSessionAsync(editSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Media edit session '{editSessionId}' was not found.");
        if (session.Status == MediaEditSessionStatus.Completed)
            throw new InvalidOperationException("A completed media edit session cannot be changed.");
        return session;
    }

    private Task<MediaEditSourceImage> RequireSourceAsync(
        MediaEditSubjectRef subject, string sourceImageId, CancellationToken cancellationToken)
        => _sources.Resolve(subject.Kind).RequireSourceAsync(subject, sourceImageId, cancellationToken);

    private async Task<SceneImageSourceInput> ReadSourceAsync(
        MediaEditSubjectRef subject, MediaEditSourceImage source, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await _sources.Resolve(subject.Kind).OpenReadAsync(source.FileRelativePath, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, maximumBytes, cancellationToken);

        // Rows written before checksums were persisted (351 of 607 scene images on the dev DB) carry
        // none; for those the stored file is authoritative. A declared checksum must match exactly.
        if (!string.IsNullOrWhiteSpace(source.Sha256))
        {
            RequireUnchangedChecksum(input.Sha256, source.Sha256,
                "The stored source image bytes do not match their persisted checksum.");
        }

        return input;
    }

    private async Task<SceneImageSourceInput> ReadValidatedSourceAsync(
        MediaEditSubjectRef subject, MediaEditSourceImage source, ResolvedMultimodalModel resolved, CancellationToken cancellationToken)
    {
        await using var stream = await _sources.Resolve(subject.Kind).OpenReadAsync(source.FileRelativePath, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        return input;
    }

    private static void RequireUnchangedChecksum(string actual, string expected, string message)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private async Task EnqueueAsync<TPayload>(
        string jobType,
        DurableJobLane lane,
        TPayload payload,
        string dedupeKey,
        string label,
        int maxAttempts,
        DurableBackgroundJobStatus status,
        CancellationToken cancellationToken)
    {
        var createdUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var job = new DurableBackgroundJob
        {
            Id = $"{jobType}:{dedupeKey}",
            JobType = jobType,
            Lane = lane,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            DedupeKey = $"{jobType}:{dedupeKey}",
            Status = status,
            MaxAttempts = maxAttempts,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc
        };
        if (!await _queue.TryEnqueueAsync(job, cancellationToken))
            throw new InvalidOperationException($"{label} '{dedupeKey}' is already queued.");
    }

    private async Task<int> ResolveMaxAttemptsAsync(CancellationToken cancellationToken)
        => (await _durableSettingsResolver.ResolveAsync(cancellationToken)).RetryDelaysSeconds.Count + 1;
}
