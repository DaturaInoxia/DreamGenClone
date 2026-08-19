namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Wraps the streaming chunk callback with a swappable inner field so the chunk consumer
/// (a Blazor component) can be attached and detached at runtime without cancelling the
/// underlying engine call.
/// </summary>
public sealed class RolePlayChunkCallbackWrapper
{
    private volatile Func<string, Task>? _inner;

    /// <summary>
    /// Atomically swaps the active chunk consumer. Pass <see langword="null"/> to detach.
    /// </summary>
    public void Attach(Func<string, Task>? callback)
    {
        _inner = callback;
    }

    /// <summary>Sets the inner callback to <see langword="null"/>.</summary>
    public void Detach()
    {
        _inner = null;
    }

    /// <summary>
    /// Invokes the current inner callback with <paramref name="chunk"/>.
    /// If the inner callback throws <see cref="ObjectDisposedException"/> or
    /// <see cref="InvalidOperationException"/> (e.g. disposed JS interop), the exception is
    /// swallowed and the callback is detached — the engine call continues uninterrupted.
    /// </summary>
    public async Task InvokeAsync(string chunk)
    {
        var cb = _inner;
        if (cb is null) return;

        try
        {
            await cb(chunk).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            _inner = null;
        }
        catch (InvalidOperationException)
        {
            // Blazor JS interop can throw InvalidOperationException on a disposed circuit.
            _inner = null;
        }
    }
}

/// <summary>
/// Wraps the per-interaction completion callback (B-087) with a swappable inner field so the
/// chunk consumer (a Blazor component) can be attached and detached at runtime without
/// cancelling the underlying engine call. Mirrors <see cref="RolePlayChunkCallbackWrapper"/>.
/// The callback carries the finalized <see cref="RolePlayInteraction"/> plus turn-position
/// metadata so the workspace can render each completed interaction as it arrives instead of
/// waiting for the full batch.
/// </summary>
public sealed class RolePlayInteractionCallbackWrapper
{
    private volatile Func<RolePlayInteraction, int, int, bool, Task>? _inner;

    /// <summary>
    /// Atomically swaps the active completion consumer. Pass <see langword="null"/> to detach.
    /// </summary>
    public void Attach(Func<RolePlayInteraction, int, int, bool, Task>? callback)
    {
        _inner = callback;
    }

    /// <summary>Sets the inner callback to <see langword="null"/>.</summary>
    public void Detach()
    {
        _inner = null;
    }

    /// <summary>
    /// Invokes the current inner callback with the finalized interaction and turn-position metadata.
    /// If the inner callback throws <see cref="ObjectDisposedException"/> or
    /// <see cref="InvalidOperationException"/> (e.g. disposed JS interop), the exception is
    /// swallowed and the callback is detached — the engine call continues uninterrupted.
    /// </summary>
    public async Task InvokeAsync(RolePlayInteraction interaction, int positionInTurn, int turnActorCount, bool isNarrative)
    {
        var cb = _inner;
        if (cb is null) return;

        try
        {
            await cb(interaction, positionInTurn, turnActorCount, isNarrative).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            _inner = null;
        }
        catch (InvalidOperationException)
        {
            // Blazor JS interop can throw InvalidOperationException on a disposed circuit.
            _inner = null;
        }
    }
}

/// <summary>
/// In-memory record of a RP prompt submission that is currently running (or has failed)
/// in the background. Tracked by <see cref="DreamGenClone.Web.Application.RolePlay.IRolePlaySubmissionTracker"/>.
/// </summary>
public sealed class RolePlayRunningSubmission
{
    public required string SessionId { get; init; }

    /// <summary>
    /// Original submission payload — retained so a failed submission can be pre-filled
    /// for re-submission by the user.
    /// </summary>
    public required UnifiedPromptSubmission Payload { get; init; }

    /// <summary>Mutable: transitions Running → Completed or Running → Failed.</summary>
    public RolePlaySubmissionStatus Status { get; set; } = RolePlaySubmissionStatus.Running;

    /// <summary>Populated when <see cref="Status"/> is <see cref="RolePlaySubmissionStatus.Failed"/>; otherwise <see langword="null"/>.</summary>
    public string? FailureMessage { get; set; }

    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When <see langword="true"/> (default), a failure is surfaced as a user-dismissible
    /// re-submit prompt. When <see langword="false"/> (e.g. Continue actions), failure is
    /// shown inline and the entry is auto-acknowledged — no re-submit UI is offered.
    /// </summary>
    public bool IsResubmittable { get; init; } = true;

    /// <summary>
    /// Tracker-owned wrapper; the inner callback can be swapped by a returning component
    /// without cancelling the engine call.
    /// </summary>
    public RolePlayChunkCallbackWrapper ChunkCallbackWrapper { get; } = new();

    /// <summary>
    /// Tracker-owned wrapper for per-interaction completion notifications (B-087). The inner
    /// callback can be swapped by a returning component without cancelling the engine call.
    /// </summary>
    public RolePlayInteractionCallbackWrapper InteractionCallbackWrapper { get; } = new();
}
