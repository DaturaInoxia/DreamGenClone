using System.Diagnostics;
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
/// The ONE source-description job: a vision pass over the stored source image that persists a plain
/// description for the compiler to reason about. Both subject kinds share it.
/// </summary>
public sealed class MediaEditDescriptionJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMediaEditRepository _editRepository;
    private readonly MediaEditSubjectSourceResolver _sources;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly IRolePlayDebugEventSink? _debugEvents;
    private readonly ILogger<MediaEditDescriptionJobHandler> _logger;

    public MediaEditDescriptionJobHandler(
        IMediaEditRepository editRepository,
        MediaEditSubjectSourceResolver sources,
        IMultimodalModelResolutionService modelResolver,
        IMultimodalCompletionClient completionClient,
        ILogger<MediaEditDescriptionJobHandler> logger,
        IRolePlayDebugEventSink? debugEvents = null)
    {
        _editRepository = editRepository;
        _sources = sources;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _logger = logger;
        _debugEvents = debugEvents;
    }

    public string JobType => BackgroundJobTypes.MediaEditDescription;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<MediaEditDescriptionJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Media edit description payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.EditSessionId))
            throw new InvalidOperationException("Media edit description payload requires an edit session id.");

        var session = await _editRepository.GetSessionAsync(payload.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Media edit session '{payload.EditSessionId}' was not found.");
        if (!string.IsNullOrWhiteSpace(session.DescriptionText))
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var subject = MediaEditSubjectRef.FromSession(session);
            var source = await _sources.Resolve(subject.Kind).RequireSourceAsync(subject, session.SourceImageId, cancellationToken);
            var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);

            await using var stream = await _sources.Resolve(subject.Kind).OpenReadAsync(source.FileRelativePath, cancellationToken);
            var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
            SceneImageMultimodalInput.Validate(input, resolved);
            if (!string.Equals(input.Sha256, session.SourceImageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source image checksum changed after the edit session was created.");

            await _completionClient.CheckHealthAsync(resolved, cancellationToken);
            var completion = await _completionClient.GenerateAsync(
                resolved,
                new MultimodalCompletionRequest(
                    SceneImageDescriptionPromptBuilder.BuildSystemMessage(),
                    SceneImageDescriptionPromptBuilder.BuildUserMessage(),
                    new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256),
                    SceneImageDescriptionPromptBuilder.ResponseSchemaName,
                    SceneImageDescriptionPromptBuilder.CreateResponseSchema()),
                cancellationToken);

            var description = ParseDescription(completion.Content);
            await _editRepository.SetDescriptionAsync(session.Id, description, DateTime.UtcNow, cancellationToken);
            _logger.LogInformation(
                "Media edit description finished: Subject={SubjectKind}/{SubjectId}, Session={SessionId}, DurationMs={DurationMs}",
                session.SubjectKind, session.SubjectId, session.Id, stopwatch.ElapsedMilliseconds);
            await WriteEventAsync("MediaEditDescriptionCompleted", session, resolved, stopwatch.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Media edit description failed: Session={SessionId}, DurationMs={DurationMs}, ErrorType={ErrorType}",
                session.Id, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            await WriteEventAsync("MediaEditDescriptionFailed", session, null, stopwatch.ElapsedMilliseconds, cancellationToken, ex.Message);
            throw;
        }
    }

    private static string ParseDescription(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            throw new InvalidOperationException("Media edit description returned empty output.");
        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("description", out var description)
                || description.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(description.GetString()))
            {
                throw new InvalidOperationException("Media edit description response must contain a non-empty 'description' string.");
            }

            return description.GetString()!.Trim();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Media edit description returned malformed JSON.", ex);
        }
    }

    /// <summary>
    /// RP debug events are written for role-play scene images only; asset descriptions log instead.
    /// </summary>
    private async Task WriteEventAsync(
        string kind,
        MediaEditSession session,
        ResolvedMultimodalModel? resolved,
        long durationMs,
        CancellationToken cancellationToken,
        string? error = null)
    {
        if (_debugEvents is null || session.SubjectKind != MediaEditSubjectKind.SceneImage)
            return;

        await _debugEvents.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.SubjectScopeId ?? string.Empty,
            InteractionId = session.SubjectId,
            CorrelationId = session.Id,
            EventKind = kind,
            Severity = error is null ? "Info" : "Error",
            ModelIdentifier = resolved?.ModelIdentifier,
            ProviderName = resolved?.ProviderName,
            DurationMs = checked((int)durationMs),
            Summary = error is null ? kind : "Media edit description failed.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                subjectKind = session.SubjectKind.ToString(),
                subjectId = session.SubjectId,
                editSessionId = session.Id,
                sourceImageId = session.SourceImageId,
                error
            }, JsonOptions)
        }, cancellationToken);
    }
}
