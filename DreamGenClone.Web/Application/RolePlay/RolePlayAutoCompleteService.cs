using System.Collections.Concurrent;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayAutoCompleteService : IRolePlayAutoCompleteService
{
    private readonly IRolePlayEngineService _engine;
    private readonly ILogger<RolePlayAutoCompleteService> _logger;
    private readonly ConcurrentDictionary<string, AutoCompleteState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);

    public RolePlayAutoCompleteService(
        IRolePlayEngineService engine,
        ILogger<RolePlayAutoCompleteService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public AutoCompleteState? GetState(string sessionId)
    {
        _states.TryGetValue(sessionId, out var state);
        return state;
    }

    public async Task<AutoCompleteState> RunAsync(
        string sessionId,
        AutoCompleteOptions options,
        CancellationToken cancellationToken)
    {
        var state = new AutoCompleteState
        {
            SessionId = sessionId,
            Status = AutoCompleteStatus.Running,
            MaxTurns = options.MaxTurns,
            CurrentTurn = 0,
            ThemesCompleted = 0,
            TotalThemes = 0,
            CurrentActivity = "Starting..."
        };

        _states[sessionId] = state;

        try
        {
            // Load initial session to count total themes
            var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
            if (session is null)
            {
                state.Status = AutoCompleteStatus.Completed;
                state.StopReason = AutoCompleteStopReason.Error;
                state.ErrorMessage = "Session not found.";
                AppendLog(state, "ERROR: Session not found.");
                return state;
            }

            var selectedThemeIds = session.SessionThemeSelections
                .Select(s => s.ThemeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            state.TotalThemes = selectedThemeIds.Count;
            AppendLog(state, $"Auto-complete started. Max turns: {options.MaxTurns}. Themes: {state.TotalThemes}.");

            while (state.CurrentTurn < options.MaxTurns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Reload session to get latest state
                session = await _engine.GetSessionAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    state.Status = AutoCompleteStatus.Completed;
                    state.StopReason = AutoCompleteStopReason.Error;
                    state.ErrorMessage = "Session lost during auto-complete.";
                    AppendLog(state, "ERROR: Session lost.");
                    return state;
                }

                // Check stop condition: all selected themes have completed (appear in ScenarioHistory)
                if (selectedThemeIds.Count > 0)
                {
                    var completedThemeIds = session.AdaptiveState.ScenarioHistory
                        .Select(h => h.ScenarioId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var completedCount = selectedThemeIds.Count(id => completedThemeIds.Contains(id));
                    state.ThemesCompleted = completedCount;

                    if (completedCount >= selectedThemeIds.Count)
                    {
                        state.Status = AutoCompleteStatus.Completed;
                        state.StopReason = AutoCompleteStopReason.AllThemesCompleted;
                        AppendLog(state, $"All {state.TotalThemes} theme(s) completed. Stopping.");
                        return state;
                    }
                }

                var phase = session.AdaptiveState.CurrentPhase;
                state.CurrentActivity = $"Turn {state.CurrentTurn + 1}/{options.MaxTurns} — Phase: {phase}";
                AppendLog(state, $"Turn {state.CurrentTurn + 1}: phase={phase}, themesDone={state.ThemesCompleted}/{state.TotalThemes}");

                // Execute the default continuation — same as the workspace's main Continue button
                var request = new ContinueAsRequest
                {
                    SessionId = sessionId,
                    SelectedIdentityIds = [],
                    TriggeredBy = SubmissionSource.MainOverflowContinue
                };

                try
                {
                    var result = await _engine.ContinueAsAsync(request, onChunk: null, cancellationToken);

                    if (!result.Success && !string.IsNullOrWhiteSpace(result.ValidationError))
                    {
                        AppendLog(state, $"  Warning: {result.ValidationError}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendLog(state, $"  Error: {ex.Message}");
                    _logger.LogWarning(ex, "Auto-complete turn failed for session {SessionId} at turn {Turn}", sessionId, state.CurrentTurn + 1);
                    // Continue to next turn despite error
                }

                state.CurrentTurn++;

                // Delay between turns to allow background jobs (semantic analysis,
                // debounced session save to PayloadJson) to complete before reload.
                await Task.Delay(3000, cancellationToken);
            }

            // Max turns reached
            state.Status = AutoCompleteStatus.Completed;
            state.StopReason = AutoCompleteStopReason.MaxTurnsReached;
            state.CurrentActivity = "Done — max turns reached.";
            AppendLog(state, $"Max turns ({options.MaxTurns}) reached. Stopping.");
            return state;
        }
        catch (OperationCanceledException)
        {
            state.Status = AutoCompleteStatus.Cancelled;
            state.StopReason = AutoCompleteStopReason.Cancelled;
            state.CurrentActivity = "Cancelled.";
            AppendLog(state, "Auto-complete cancelled by user.");
            _logger.LogInformation("Auto-complete cancelled for session {SessionId} at turn {Turn}", sessionId, state.CurrentTurn);
            return state;
        }
        catch (Exception ex)
        {
            state.Status = AutoCompleteStatus.Completed;
            state.StopReason = AutoCompleteStopReason.Error;
            state.ErrorMessage = ex.Message;
            state.CurrentActivity = "Error.";
            AppendLog(state, $"FATAL ERROR: {ex.Message}");
            _logger.LogError(ex, "Auto-complete failed for session {SessionId}", sessionId);
            return state;
        }
    }

    private static void AppendLog(AutoCompleteState state, string message)
    {
        state.RecentLog.Add(message);
        // Keep only last 20 log entries
        while (state.RecentLog.Count > 20)
        {
            state.RecentLog.RemoveAt(0);
        }
    }
}
