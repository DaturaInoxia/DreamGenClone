using System.Collections.Concurrent;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Singleton implementation of <see cref="IRolePlaySubmissionTracker"/>.
/// Keyed by sessionId; one active entry per session at a time.
/// </summary>
public sealed class RolePlaySubmissionTracker : IRolePlaySubmissionTracker
{
    private readonly ConcurrentDictionary<string, RolePlayRunningSubmission> _entries = new();
    private readonly ILogger<RolePlaySubmissionTracker> _logger;

    public RolePlaySubmissionTracker(ILogger<RolePlaySubmissionTracker> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public event Action<string>? OnJobStatusChanged;

    /// <inheritdoc/>
    public bool TryBeginSubmission(string sessionId, UnifiedPromptSubmission submission, Task engineTask, bool isResubmittable = true)
    {
        var entry = new RolePlayRunningSubmission
        {
            SessionId = sessionId,
            Payload = submission,
            IsResubmittable = isResubmittable
        };

        if (!_entries.TryAdd(sessionId, entry))
        {
            _logger.LogInformation(
                "Submission tracker rejected duplicate for session {SessionId} — active entry already present",
                sessionId);
            return false;
        }

        _logger.LogInformation(
            "Submission tracker registered background task: session={SessionId}, intent={Intent}",
            sessionId, submission.Intent);

        // Wire continuation on the thread pool — do not await here.
        _ = engineTask.ContinueWith(
            t => OnEngineTaskCompleted(t, sessionId),
            TaskScheduler.Default);

        return true;
    }

    /// <inheritdoc/>
    public RolePlayRunningSubmission? GetEntry(string sessionId) =>
        _entries.TryGetValue(sessionId, out var entry) ? entry : null;

    /// <inheritdoc/>
    public void AttachChunkCallback(string sessionId, Func<string, Task>? callback)
    {
        if (_entries.TryGetValue(sessionId, out var entry))
        {
            entry.ChunkCallbackWrapper.Attach(callback);
        }
    }

    /// <inheritdoc/>
    public void DetachChunkCallback(string sessionId)
    {
        if (_entries.TryGetValue(sessionId, out var entry))
        {
            entry.ChunkCallbackWrapper.Detach();
        }
    }

    /// <inheritdoc/>
    public void AcknowledgeFailure(string sessionId)
    {
        if (_entries.TryGetValue(sessionId, out var entry) &&
            entry.Status == RolePlaySubmissionStatus.Failed)
        {
            _entries.TryRemove(sessionId, out _);
            _logger.LogInformation(
                "Submission tracker: failure acknowledged for session {SessionId}",
                sessionId);
        }
    }

    // ---

    private void OnEngineTaskCompleted(Task task, string sessionId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry))
        {
            return;
        }

        if (task.IsCompletedSuccessfully)
        {
            entry.Status = RolePlaySubmissionStatus.Completed;
            _entries.TryRemove(sessionId, out _);

            _logger.LogInformation(
                "Submission tracker: background task completed for session {SessionId}",
                sessionId);
        }
        else
        {
            var ex = task.Exception?.GetBaseException() ?? task.Exception;
            entry.FailureMessage = ex?.Message ?? "Unknown error";
            entry.Status = RolePlaySubmissionStatus.Failed;
            entry.ChunkCallbackWrapper.Detach();

            _logger.LogError(
                ex,
                "Submission tracker: background task failed for session {SessionId}: {FailureMessage}",
                sessionId, entry.FailureMessage);
        }

        try
        {
            OnJobStatusChanged?.Invoke(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Submission tracker: OnJobStatusChanged handler threw for session {SessionId}", sessionId);
        }
    }
}
