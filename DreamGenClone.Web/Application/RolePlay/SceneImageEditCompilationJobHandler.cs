using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageEditCompilationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageRepository _imageRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly ISceneImageEditPromptCompiler _compiler;
    private readonly IRolePlayDebugEventSink _debugEvents;

    public SceneImageEditCompilationJobHandler(
        ISceneImageEditRepository editRepository,
        ISceneImageRepository imageRepository,
        ISceneImageStorageService storage,
        IMultimodalModelResolutionService modelResolver,
        IMultimodalCompletionClient completionClient,
        ISceneImageEditPromptCompiler compiler,
        IRolePlayDebugEventSink debugEvents)
    {
        _editRepository = editRepository;
        _imageRepository = imageRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _compiler = compiler;
        _debugEvents = debugEvents;
    }

    public string JobType => BackgroundJobTypes.SceneImageEditPromptCompilation;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageEditCompilationJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image edit compilation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AttemptId))
            throw new InvalidOperationException("Scene image edit compilation payload requires an attempt id.");

        var attempt = await _editRepository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{payload.AttemptId}' was not found.");
        if (attempt.Status is SceneImageEditCompilationAttemptStatus.Ready
            or SceneImageEditCompilationAttemptStatus.ClarificationRequired
            or SceneImageEditCompilationAttemptStatus.Invalid
            or SceneImageEditCompilationAttemptStatus.Failed)
            return;
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending)
            throw new InvalidOperationException($"Compilation attempt '{attempt.Id}' is already processing.");

        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        attempt.StartedUtc = DateTime.UtcNow;
        await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        SceneImageEditSession? editSession = null;
        try
        {
            editSession = await _editRepository.GetSessionAsync(attempt.EditSessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Edit session '{attempt.EditSessionId}' was not found.");
            var source = await _imageRepository.GetImageAsync(editSession.SourceImageId, cancellationToken)
                ?? throw new InvalidOperationException($"Source scene image '{editSession.SourceImageId}' was not found.");
            if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
                throw new InvalidOperationException("Compilation requires a stored, completed source image.");
            var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
            var currentSnapshot = SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved);
            if (!string.Equals(currentSnapshot, attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal))
                throw new InvalidOperationException("The compiler model configuration changed after this attempt was queued.");

            await using var stream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
            SceneImageMultimodalInput.Validate(input, resolved);
            if (!string.Equals(input.Sha256, editSession.SourceImageSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(input.Sha256, attempt.SourceImageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source image checksum changed after compilation was queued.");

            var clarificationHistory = string.IsNullOrWhiteSpace(attempt.ClarificationContextJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(attempt.ClarificationContextJson, JsonOptions)
                    ?? throw new InvalidOperationException("The compilation clarification snapshot is invalid.");
            var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(attempt.RawIntent, clarificationHistory));
            if (messages.SchemaVersion != attempt.CompilerSchemaVersion || messages.SystemPromptVersion != attempt.SystemPromptVersion)
                throw new InvalidOperationException("The compiler prompt contract changed after this attempt was queued.");

            await WriteEventAsync("SceneImageEditCompilationSent", editSession, attempt, resolved, stopwatch.ElapsedMilliseconds, null, cancellationToken);
            await _completionClient.CheckHealthAsync(resolved, cancellationToken);
            var completion = await _completionClient.GenerateAsync(
                resolved,
                new MultimodalCompletionRequest(
                    messages.SystemMessage,
                    messages.UserMessage,
                    new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256),
                    messages.ResponseSchemaName,
                    messages.ResponseSchema),
                cancellationToken);

            attempt.RawModelResponse = completion.Content;
            var result = _compiler.Parse(completion.Content, input.Width, input.Height);
            attempt.ParsedResultJson = JsonSerializer.Serialize(result, JsonOptions);
            attempt.Status = result.Status switch
            {
                SceneImageEditCompilationResultStatus.Ready => SceneImageEditCompilationAttemptStatus.Ready,
                SceneImageEditCompilationResultStatus.ClarificationRequired => SceneImageEditCompilationAttemptStatus.ClarificationRequired,
                SceneImageEditCompilationResultStatus.Invalid => SceneImageEditCompilationAttemptStatus.Invalid,
                _ => throw new InvalidOperationException("The compiler returned a non-terminal result.")
            };
            attempt.CompletedUtc = DateTime.UtcNow;
            await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);

            if (result.Status == SceneImageEditCompilationResultStatus.Ready)
            {
                var prompt = result.CompiledPrompt!;
                await _editRepository.CreateRevisionAsync(new SceneImageEditPromptRevision
                {
                    CompilationAttemptId = attempt.Id,
                    Ordinal = 0,
                    Prompt = prompt,
                    RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput,
                    PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))
                }, cancellationToken);
            }

            var sessionStatus = result.Status switch
            {
                SceneImageEditCompilationResultStatus.Ready => SceneImageEditSessionStatus.Ready,
                SceneImageEditCompilationResultStatus.ClarificationRequired => SceneImageEditSessionStatus.ClarificationRequired,
                SceneImageEditCompilationResultStatus.Invalid => SceneImageEditSessionStatus.Invalid,
                _ => throw new InvalidOperationException("The compiler returned a non-terminal result.")
            };
            await _editRepository.UpdateSessionStatusAsync(editSession.Id, sessionStatus, DateTime.UtcNow, cancellationToken: cancellationToken);
            await WriteEventAsync("SceneImageEditCompilationCompleted", editSession, attempt, resolved, stopwatch.ElapsedMilliseconds, result.Status.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            attempt.Status = SceneImageEditCompilationAttemptStatus.Failed;
            attempt.Error = ex.Message;
            attempt.CompletedUtc = DateTime.UtcNow;
            await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
            if (editSession is not null)
                await _editRepository.UpdateSessionStatusAsync(editSession.Id, SceneImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            await _debugEvents.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = editSession?.SessionId ?? string.Empty,
                InteractionId = editSession?.InteractionId,
                CorrelationId = attempt.Id,
                EventKind = "SceneImageEditCompilationFailed",
                Severity = "Error",
                Summary = "Scene image edit compilation failed.",
                DurationMs = checked((int)stopwatch.ElapsedMilliseconds),
                MetadataJson = JsonSerializer.Serialize(new { editSessionId = attempt.EditSessionId, attemptId = attempt.Id, sourceImageId = editSession?.SourceImageId, sourceSha256 = attempt.SourceImageSha256, errorType = ex.GetType().Name }, JsonOptions)
            }, cancellationToken);
            throw;
        }
    }

    private Task WriteEventAsync(
        string kind,
        SceneImageEditSession editSession,
        SceneImageEditCompilationAttempt attempt,
        ResolvedMultimodalModel resolved,
        long durationMs,
        string? resultStatus,
        CancellationToken cancellationToken) => _debugEvents.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = editSession.SessionId,
            InteractionId = editSession.InteractionId,
            CorrelationId = attempt.Id,
            EventKind = kind,
            Severity = "Info",
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            DurationMs = checked((int)durationMs),
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(new
            {
                editSessionId = editSession.Id,
                attemptId = attempt.Id,
                attempt.Ordinal,
                sourceImageId = editSession.SourceImageId,
                sourceSha256 = attempt.SourceImageSha256,
                attempt.CompilerSchemaVersion,
                attempt.SystemPromptVersion,
                resultStatus,
                resolved.RuntimeRevision,
                resolved.ArtifactRevision
            }, JsonOptions)
        }, cancellationToken);
}