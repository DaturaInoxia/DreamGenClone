using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public enum AutoCompleteStopReason
{
    MaxTurnsReached,
    AllThemesCompleted,
    Cancelled,
    Error
}

public sealed class AutoCompleteOptions
{
    public int MaxTurns { get; init; } = 50;
}

public sealed class AutoCompleteState
{
    public string SessionId { get; init; } = string.Empty;
    public AutoCompleteStatus Status { get; set; } = AutoCompleteStatus.Idle;
    public int CurrentTurn { get; set; }
    public int MaxTurns { get; set; }
    public int ThemesCompleted { get; set; }
    public int TotalThemes { get; set; }
    public string? CurrentActivity { get; set; }
    public AutoCompleteStopReason? StopReason { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> RecentLog { get; set; } = [];
}

public enum AutoCompleteStatus
{
    Idle,
    Running,
    Completed,
    Cancelled
}

public interface IRolePlayAutoCompleteService
{
    /// <summary>
    /// Runs the auto-complete loop for the given session. Returns a summary result when the loop
    /// ends (max turns reached, all themes completed, cancelled, or error).
    /// </summary>
    Task<AutoCompleteState> RunAsync(
        string sessionId,
        AutoCompleteOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current state of an in-progress or completed auto-complete run, or null if none.
    /// </summary>
    AutoCompleteState? GetState(string sessionId);
}
