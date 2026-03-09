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

        lock (_gate)
        {
            saveAction = _pendingSaveAction;
            reason = _pendingReason;
            _pendingSaveAction = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        if (saveAction is null)
        {
            return;
        }

        _logger.LogInformation("Executing autosave flush for reason {Reason}", reason);
        await saveAction(cancellationToken);
    }

    private void OnDebounceElapsed(object? state)
    {
        _ = FlushAsync();
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
