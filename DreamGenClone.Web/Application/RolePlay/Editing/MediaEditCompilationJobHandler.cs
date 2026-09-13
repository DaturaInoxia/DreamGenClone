using System.Diagnostics;
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
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE vision-compilation job. Replaces the scene and asset compile handlers, and gives both the
/// instrumentation the scene path had (timing, model/provider, sent/completed/failed events) — the
/// asset path previously had none, which is why its failures were invisible.
/// </summary>
public sealed class MediaEditCompilationJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMediaEditRepository _editRepository;
    private readonly MediaEditSubjectSourceResolver _sources;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly ISceneImageEditPromptCompiler _compiler;
    private readonly IRolePlayDebugEventSink? _debugEvents;
    private readonly ILogger<MediaEditCompilationJobHandler> _logger;

    public MediaEditCompilationJobHandler(
        IMediaEditRepository editRepository,
        MediaEditSubjectSourceResolver sources,
        IMultimodalModelResolutionService modelResolver,
        IMultimodalCompletionClient completionClient,
        ISceneImageEditPromptCompiler compiler,
        ILogger<MediaEditCompilationJobHandler> logger,
        IRolePlayDebugEventSink? debugEvents = null)
    {
        _editRepository = editRepository;
        _sources = sources;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _compiler = compiler;
        _logger = logger;
        _debugEvents = debugEvents;
    }

    public string JobType => BackgroundJobTypes.MediaEditPromptCompilation;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<MediaEditCompilationJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Media edit compilation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AttemptId))
            throw new InvalidOperationException("Media edit compilation payload requires an attempt id.");

        var attempt = await _editRepository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Media compilation attempt '{payload.AttemptId}' was not found.");
        if (attempt.Status is SceneImageEditCompilationAttemptStatus.Ready
            or SceneImageEditCompilationAttemptStatus.ClarificationRequired
            or SceneImageEditCompilationAttemptStatus.Invalid
            or SceneImageEditCompilationAttemptStatus.Failed)
        {
            return;
        }

        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending)
            throw new InvalidOperationException($"Media compilation attempt '{attempt.Id}' is already processing.");

        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        attempt.StartedUtc = DateTime.UtcNow;
        await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        MediaEditSession? session = null;
        ResolvedMultimodalModel? resolved = null;
        try
        {
            session = await _editRepository.GetSessionAsync(attempt.EditSessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Media edit session '{attempt.EditSessionId}' was not found.");
            var subject = MediaEditSubjectRef.FromSession(session);
            var source = await _sources.Resolve(subject.Kind).RequireSourceAsync(subject, session.SourceImageId, cancellationToken);
            resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
            if (!string.Equals(SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved), attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal))
                throw new InvalidOperationException("The compiler model configuration changed after this attempt was queued.");

            await using var stream = await _sources.Resolve(subject.Kind).OpenReadAsync(source.FileRelativePath, cancellationToken);
            var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
            SceneImageMultimodalInput.Validate(input, resolved);
            RequireUnchangedChecksum(input.Sha256, session.SourceImageSha256,
                "The source image checksum changed after compilation was queued.");
            RequireUnchangedChecksum(input.Sha256, attempt.SourceImageSha256,
                "The source image checksum changed after compilation was queued.");

            var clarificationHistory = string.IsNullOrWhiteSpace(attempt.ClarificationContextJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(attempt.ClarificationContextJson, JsonOptions)
                    ?? throw new InvalidOperationException("The compilation clarification snapshot is invalid.");
            var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(attempt.RawIntent, clarificationHistory));
            if (messages.SchemaVersion != attempt.CompilerSchemaVersion || messages.SystemPromptVersion != attempt.SystemPromptVersion)
                throw new InvalidOperationException("The compiler prompt contract changed after this attempt was queued.");

            await WriteEventAsync("MediaEditCompilationSent", session, attempt, resolved, stopwatch.ElapsedMilliseconds, null, cancellationToken);
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
                await _editRepository.CreateRevisionAsync(new MediaEditPromptRevision
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
                SceneImageEditCompilationResultStatus.Ready => MediaEditSessionStatus.Ready,
                SceneImageEditCompilationResultStatus.ClarificationRequired => MediaEditSessionStatus.ClarificationRequired,
                SceneImageEditCompilationResultStatus.Invalid => MediaEditSessionStatus.Invalid,
                _ => throw new InvalidOperationException("The compiler returned a non-terminal result.")
            };
            await _editRepository.UpdateSessionStatusAsync(session.Id, sessionStatus, DateTime.UtcNow, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Media edit compilation finished: Subject={SubjectKind}/{SubjectId}, Attempt={AttemptId}, Status={Status}, DurationMs={DurationMs}, Model={Model}",
                session.SubjectKind, session.SubjectId, attempt.Id, attempt.Status, stopwatch.ElapsedMilliseconds, resolved.ModelIdentifier);
            await WriteEventAsync("MediaEditCompilationCompleted", session, attempt, resolved, stopwatch.ElapsedMilliseconds, result.Status.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            attempt.Status = SceneImageEditCompilationAttemptStatus.Failed;
            attempt.Error = ex.Message;
            attempt.CompletedUtc = DateTime.UtcNow;
            await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
            if (session is not null)
            {
                await _editRepository.UpdateSessionStatusAsync(session.Id, MediaEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            }

            _logger.LogWarning(ex,
                "Media edit compilation failed: Attempt={AttemptId}, SourceImageId={SourceImageId}, DurationMs={DurationMs}, ErrorType={ErrorType}",
                attempt.Id, session?.SourceImageId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            await WriteEventAsync("MediaEditCompilationFailed", session, attempt, resolved, stopwatch.ElapsedMilliseconds, null, cancellationToken, ex);
            throw;
        }
    }

    private static void RequireUnchangedChecksum(string actual, string expected, string message)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// RP debug events are written for role-play scene images, where the debug view can show them.
    /// Asset edits log the same detail to the application log instead of the RP debug stream.
    /// </summary>
    private async Task WriteEventAsync(
        string kind,
        MediaEditSession? session,
        MediaEditCompilationAttempt attempt,
        ResolvedMultimodalModel? resolved,
        long durationMs,
        string? resultStatus,
        CancellationToken cancellationToken,
        Exception? error = null)
    {
        if (_debugEvents is null || session is null || session.SubjectKind != MediaEditSubjectKind.SceneImage)
            return;

        await _debugEvents.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.SubjectScopeId ?? string.Empty,
            InteractionId = session.SubjectId,
            CorrelationId = attempt.Id,
            EventKind = kind,
            Severity = error is null ? "Info" : "Error",
            ModelIdentifier = resolved?.ModelIdentifier,
            ProviderName = resolved?.ProviderName,
            DurationMs = checked((int)durationMs),
            Summary = error is null ? kind : "Media edit compilation failed.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                subjectKind = session.SubjectKind.ToString(),
                subjectId = session.SubjectId,
                editSessionId = attempt.EditSessionId,
                attemptId = attempt.Id,
                sourceImageId = session.SourceImageId,
                sourceSha256 = attempt.SourceImageSha256,
                resultStatus,
                errorType = error?.GetType().Name,
                error = error?.Message
            }, JsonOptions)
        }, cancellationToken);
    }
}
