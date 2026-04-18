using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class NarrativePhaseManager : INarrativePhaseManager
{
    private const double CommittedScoreBase = 60;
    private const double CommittedDesireBase = 65;
    private const double CommittedRestraintBase = 45;
    private const int CommittedInteractionsBase = 3;
    private const double ApproachingScoreBase = 80;
    private const double ApproachingDesireBase = 75;
    private const double ApproachingRestraintBase = 35;
    private const int ApproachingInteractionsBase = 2;

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
        var completedScenarios = Math.Max(0, signals.CompletedScenarios);
        var committedThresholds = ResolveCommittedThresholds(completedScenarios);
        var approachingThresholds = ResolveApproachingThresholds(completedScenarios);

        if (signals.ManualScenarioOverrideRequested)
        {
            next = "Reset";
            reason = "Manual scenario override requested";
        }
        else if (current == "Committed"
            && adaptiveState.ActiveScenarioScore >= committedThresholds.ScoreMin
            && adaptiveState.AverageDesire >= committedThresholds.DesireMin
            && adaptiveState.AverageRestraint <= committedThresholds.RestraintMax
            && signals.InteractionsSinceCommitment >= committedThresholds.InteractionsMin)
        {
            next = "Approaching";
            reason = "Committed to Approaching threshold met";
        }
        else if (current == "Approaching"
            && (signals.ExplicitClimaxRequested
                || (adaptiveState.ActiveScenarioScore >= approachingThresholds.ScoreMin
                    && adaptiveState.AverageDesire >= approachingThresholds.DesireMin
                    && adaptiveState.AverageRestraint <= approachingThresholds.RestraintMax
                    && signals.InteractionsInApproaching >= approachingThresholds.InteractionsMin)))
        {
            next = "Climax";
            reason = signals.ExplicitClimaxRequested ? "Explicit climax override" : "Approaching to Climax threshold met";
        }
        else if (current == "Climax"
            && (signals.ClimaxCompletionDetected || signals.InteractionsInApproaching >= 2))
        {
            next = "Reset";
            reason = signals.ClimaxCompletionDetected
                ? "Climax completion detected"
                : "Climax auto-complete threshold met";
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

    private static (double ScoreMin, double DesireMin, double RestraintMax, int InteractionsMin) ResolveCommittedThresholds(int completedScenarios)
    {
        var cycle = Math.Max(0, completedScenarios);
        return (
            ScoreMin: Math.Max(45, CommittedScoreBase - (cycle * 4)),
            DesireMin: Math.Max(50, CommittedDesireBase - (cycle * 4)),
            RestraintMax: Math.Min(60, CommittedRestraintBase + (cycle * 4)),
            InteractionsMin: Math.Max(2, CommittedInteractionsBase - Math.Min(cycle, 1)));
    }

    private static (double ScoreMin, double DesireMin, double RestraintMax, int InteractionsMin) ResolveApproachingThresholds(int completedScenarios)
    {
        var cycle = Math.Max(0, completedScenarios);
        return (
            ScoreMin: Math.Max(55, ApproachingScoreBase - (cycle * 8)),
            DesireMin: Math.Max(60, ApproachingDesireBase - (cycle * 6)),
            RestraintMax: Math.Min(55, ApproachingRestraintBase + (cycle * 6)),
            InteractionsMin: ApproachingInteractionsBase);
    }
}
