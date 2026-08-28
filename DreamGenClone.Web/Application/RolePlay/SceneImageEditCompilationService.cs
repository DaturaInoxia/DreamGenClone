using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageEditCompilationService : ISceneImageEditCompilationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneImageRepository _imageRepository;
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly ISceneImageEditPromptCompiler _compiler;
    private readonly IBackgroundJobQueue _queue;

    public SceneImageEditCompilationService(
        ISceneImageRepository imageRepository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IMultimodalModelResolutionService modelResolver,
        ISceneImageEditPromptCompiler compiler,
        IBackgroundJobQueue queue)
    {
        _imageRepository = imageRepository;
        _editRepository = editRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _compiler = compiler;
        _queue = queue;
    }

    public async Task<SceneImageEditSession> CreateSessionAsync(CreateSceneImageEditSessionRequest request, CancellationToken cancellationToken = default)
    {
        var source = await RequireSourceAsync(request.SourceImageId, request.SessionId, request.InteractionId, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, int.MaxValue, cancellationToken);
        var session = new SceneImageEditSession
        {
            SourceImageId = source.Id,
            SourceImageSha256 = input.Sha256,
            SessionId = source.SessionId,
            InteractionId = source.InteractionId,
            Status = SceneImageEditSessionStatus.Active
        };
        await _editRepository.CreateSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<SceneImageEditCompilationAttempt> EnqueueCompilationAsync(EnqueueSceneImageEditCompilationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawIntent))
            throw new InvalidOperationException("A non-empty edit intent is required for compilation.");
        if (request.ClarificationHistory.Any(value => string.IsNullOrWhiteSpace(value)))
            throw new InvalidOperationException("Clarification history cannot contain empty entries.");

        var editSession = await _editRepository.GetSessionAsync(request.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{request.EditSessionId}' was not found.");
        if (editSession.Status == SceneImageEditSessionStatus.Completed)
            throw new InvalidOperationException("A completed edit session cannot be recompiled.");
        var source = await RequireSourceAsync(editSession.SourceImageId, editSession.SessionId, editSession.InteractionId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        if (!string.Equals(input.Sha256, editSession.SourceImageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source image checksum changed after the edit session was created.");

        var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(request.RawIntent.Trim(), request.ClarificationHistory));
        var latest = await _editRepository.GetLatestAttemptAsync(editSession.Id, cancellationToken);
        var attempt = new SceneImageEditCompilationAttempt
        {
            EditSessionId = editSession.Id,
            Ordinal = latest is null ? 0 : latest.Ordinal + 1,
            RawIntent = request.RawIntent.Trim(),
            ClarificationContextJson = request.ClarificationHistory.Count == 0 ? null : JsonSerializer.Serialize(request.ClarificationHistory, JsonOptions),
            SourceImageSha256 = input.Sha256,
            Status = SceneImageEditCompilationAttemptStatus.Pending,
            ResolvedModelSnapshotJson = SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved),
            CompilerSchemaVersion = messages.SchemaVersion,
            SystemPromptVersion = messages.SystemPromptVersion
        };
        await _editRepository.CreateAttemptAsync(attempt, cancellationToken);
        await _editRepository.UpdateSessionStatusAsync(editSession.Id, SceneImageEditSessionStatus.Active, DateTime.UtcNow, cancellationToken: cancellationToken);
        if (!_queue.Enqueue(
            BackgroundJobTypes.SceneImageEditPromptCompilation,
            JsonSerializer.Serialize(new SceneImageEditCompilationJobPayload { AttemptId = attempt.Id }, JsonOptions),
            attempt.Id))
        {
            throw new InvalidOperationException($"Compilation attempt '{attempt.Id}' is already queued.");
        }
        return attempt;
    }

    public async Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(editSessionId))
            throw new InvalidOperationException("An edit session id is required for description.");
        var editSession = await _editRepository.GetSessionAsync(editSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{editSessionId}' was not found.");
        if (!force && !string.IsNullOrWhiteSpace(editSession.DescriptionText))
            return;
        var source = await RequireSourceAsync(editSession.SourceImageId, editSession.SessionId, editSession.InteractionId, cancellationToken);
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        if (!string.Equals(input.Sha256, editSession.SourceImageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source image checksum changed after the edit session was created.");
        if (!_queue.Enqueue(
            BackgroundJobTypes.SceneImageEditDescription,
            JsonSerializer.Serialize(new SceneImageEditDescriptionJobPayload { EditSessionId = editSession.Id }, JsonOptions),
            editSession.Id))
        {
            throw new InvalidOperationException($"Edit session '{editSession.Id}' description job is already queued.");
        }
    }

    public async Task<SceneImageEditPromptRevision> AppendPromptRevisionAsync(AppendSceneImageEditPromptRevisionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new InvalidOperationException("A non-empty revised prompt is required.");
        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{request.CompilationAttemptId}' was not found.");
        if (!string.Equals(attempt.EditSessionId, request.EditSessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The compilation attempt does not belong to the selected edit session.");
        var revisions = await _editRepository.ListRevisionsAsync(attempt.Id, cancellationToken);
        var prompt = request.Prompt.Trim();
        var revision = new SceneImageEditPromptRevision
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

    public Task<SceneImageEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default) => _editRepository.GetSessionAsync(editSessionId, cancellationToken);
    public Task<SceneImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default) => _editRepository.GetLatestAttemptAsync(editSessionId, cancellationToken);
    public Task<IReadOnlyList<SceneImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default) => _editRepository.ListRevisionsAsync(attemptId, cancellationToken);

    private async Task<SceneImageRecord> RequireSourceAsync(string sourceImageId, string sessionId, string interactionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceImageId) || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(interactionId))
            throw new InvalidOperationException("Source image, session, and interaction ids are required.");
        var source = await _imageRepository.GetImageAsync(sourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{sourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("Only a stored, completed scene image can start or continue editing.");
        if (!string.Equals(source.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.InteractionId, interactionId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source scene image does not belong to the selected session and interaction.");
        return source;
    }
}