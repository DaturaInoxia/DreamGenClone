using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Singleton service that tracks RP prompt submissions that are running in the background
/// (i.e. the submitting Blazor component may have been disposed due to page navigation).
/// <para>
/// One active entry is permitted per session at a time. Completed entries are removed
/// automatically; failed entries are retained until the user calls
/// <see cref="AcknowledgeFailure"/> to prevent accidental duplicate re-submission.
/// </para>
/// </summary>
public interface IRolePlaySubmissionTracker
{
    /// <summary>
    /// Fired with the <c>sessionId</c> whenever an entry's status changes
    /// (Running → Completed, or Running → Failed).
    /// Components subscribe while mounted and unsubscribe in DisposeAsync.
    /// </summary>
    event Action<string>? OnJobStatusChanged;

    /// <summary>
    /// Registers a new in-flight submission and begins monitoring
    /// <paramref name="engineTask"/> for completion or failure.
    /// </summary>
    /// <param name="isResubmittable">
    /// When <see langword="true"/> (default), a failure surfaces as a user-dismissible re-submit
    /// prompt. Pass <see langword="false"/> for Continue actions where re-submit is not appropriate.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the entry was registered; <see langword="false"/> if the
    /// session already has an active (Running or Failed) entry — the caller should surface
    /// a "response already in progress" message and not fire the engine call.
    /// </returns>
    bool TryBeginSubmission(string sessionId, UnifiedPromptSubmission submission, Task engineTask, bool isResubmittable = true);

    /// <summary>Returns the current entry for the session, or <see langword="null"/> if none.</summary>
    RolePlayRunningSubmission? GetEntry(string sessionId);

    /// <summary>
    /// Swaps the inner chunk callback on the entry's <see cref="RolePlayChunkCallbackWrapper"/>.
    /// No-op if there is no active entry for the session.
    /// </summary>
    void AttachChunkCallback(string sessionId, Func<string, Task>? callback);

    /// <summary>
    /// Sets the inner chunk callback to <see langword="null"/>.
    /// No-op if there is no active entry for the session.
    /// Called from the component's <c>DisposeAsync</c>.
    /// </summary>
    void DetachChunkCallback(string sessionId);

    /// <summary>
    /// Swaps the inner per-interaction completion callback on the entry's
    /// <see cref="RolePlayInteractionCallbackWrapper"/> (B-087). Delivers the finalized
    /// <see cref="RolePlayInteraction"/> plus turn-position metadata so the live component
    /// can render each completed interaction as it arrives instead of waiting for the full
    /// batch. No-op if there is no active entry for the session.
    /// </summary>
    void AttachInteractionCompletedCallback(string sessionId, Func<RolePlayInteraction, int, int, bool, Task>? callback);

    /// <summary>
    /// Sets the inner per-interaction completion callback to <see langword="null"/> (B-087).
    /// No-op if there is no active entry for the session.
    /// Called from the component's <c>DisposeAsync</c>.
    /// </summary>
    void DetachInteractionCompletedCallback(string sessionId);

    /// <summary>
    /// Removes a Failed entry for the session, unblocking future submissions.
    /// No-op if the entry is Running or absent (prevents accidental removal of in-flight work).
    /// </summary>
    void AcknowledgeFailure(string sessionId);
}
