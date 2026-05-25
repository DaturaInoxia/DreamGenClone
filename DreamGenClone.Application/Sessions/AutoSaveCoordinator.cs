using Microsoft.Extensions.Logging;

namespace DreamGenClone.Application.Sessions;

public sealed class AutoSaveCoordinator : IAutoSaveCoordinator, IDisposable
{
    private readonly TimeSpan _debounceWindow;
    private readonly ILogger<AutoSaveCoordinator> _logger;
    private readonly object _gate = new();

    private Timer? _timer;
    private Func<CancellationToken, Task>? _pendingSaveAction;
    private string _pendingReason = "unspecified";
    private Task _inFlightSave = Task.CompletedTask;

    public AutoSaveCoordinator(ILogger<AutoSaveCoordinator> logger)
        : this(TimeSpan.FromSeconds(1), logger)
    {
    }

    public AutoSaveCoordinator(TimeSpan debounceWindow, ILogger<AutoSaveCoordinator> logger)
    {
        _debounceWindow = debounceWindow;
        _logger = logger;
    }

    public void RequestSave(string reason, Func<CancellationToken, Task> saveAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(saveAction);

        lock (_gate)
        {
            _pendingReason = reason;
            _pendingSaveAction = saveAction;
            _timer ??= new Timer(OnDebounceElapsed);
            _timer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
        }

        _logger.LogInformation("Autosave requested for reason {Reason}", reason);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task>? saveAction;
        string reason;
        Task priorInFlight;

        lock (_gate)
        {
            saveAction = _pendingSaveAction;
            reason = _pendingReason;
            _pendingSaveAction = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            priorInFlight = _inFlightSave;
        }

        // Always wait for any previously started (e.g. fire-and-forget from the debounce timer)
        // save to complete before returning. Otherwise an awaited FlushAsync can return while a
        // prior save is still writing to the DB, and any work done after the await (such as
        // enqueueing background jobs that read the session from the DB) will see a stale snapshot.
        try { await priorInFlight.ConfigureAwait(false); }
        catch { /* prior save errors are logged by the save action itself */ }

        if (saveAction is null)
        {
            return;
        }

        _logger.LogInformation("Executing autosave flush for reason {Reason}", reason);
        var task = saveAction(cancellationToken);
        lock (_gate) { _inFlightSave = task; }
        await task.ConfigureAwait(false);
    }

    private void OnDebounceElapsed(object? state)
    {
        // Start the flush and track it so an awaited FlushAsync caller can join it.
        var task = FlushAsync();
        lock (_gate) { _inFlightSave = task; }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
