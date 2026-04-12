using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class NarrativePhaseManager : INarrativePhaseManager
{
    public Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        NarrativeSignalSnapshot signals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adaptiveState);
        ArgumentNullException.ThrowIfNull(signals);

        var current = adaptiveState.CurrentNarrativePhase;
        var next = current;
        var reason = "No transition";

        if (signals.ManualScenarioOverrideRequested)
        {
            next = "Reset";
            reason = "Manual scenario override requested";
        }
        else if (current == "Committed"
            && adaptiveState.ActiveScenarioScore >= 60
            && adaptiveState.AverageDesire >= 65
            && adaptiveState.AverageRestraint <= 45
            && signals.InteractionsSinceCommitment >= 3)
        {
            next = "Approaching";
            reason = "Committed to Approaching threshold met";
        }
        else if (current == "Approaching"
            && (signals.ExplicitClimaxRequested
                || (adaptiveState.ActiveScenarioScore >= 80
                    && adaptiveState.AverageDesire >= 75
                    && adaptiveState.AverageRestraint <= 35
                    && signals.InteractionsInApproaching >= 2)))
        {
            next = "Climax";
            reason = signals.ExplicitClimaxRequested ? "Explicit climax override" : "Approaching to Climax threshold met";
        }
        else if (current == "Climax" && signals.ClimaxCompletionDetected)
        {
            next = "Reset";
            reason = "Climax completion detected";
        }
        else if (current == "Reset")
        {
            next = "BuildUp";
            reason = "Reset completed";
        }

        return Task.FromResult(new PhaseTransitionResult(current, next, !string.Equals(current, next, StringComparison.Ordinal), reason));
    }

    public Task<AdaptiveScenarioSnapshot> ApplyResetAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        ResetTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adaptiveState);
        ArgumentNullException.ThrowIfNull(trigger);

        var reset = new AdaptiveScenarioSnapshot(
            ActiveScenarioId: null,
            CurrentNarrativePhase: "BuildUp",
            InteractionCount: adaptiveState.InteractionCount,
            AverageDesire: Math.Max(50, adaptiveState.AverageDesire - 20),
            AverageRestraint: adaptiveState.AverageRestraint < 50
                ? Math.Min(50, adaptiveState.AverageRestraint + 10)
                : Math.Max(50, adaptiveState.AverageRestraint - 10),
            ActiveScenarioScore: Math.Max(0, adaptiveState.ActiveScenarioScore * 0.3));

        return Task.FromResult(reset);
    }
}
