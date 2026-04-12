using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ScenarioSelectionEngine : IScenarioSelectionEngine
{
    public Task<ScenarioSelectionResult> EvaluateAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        IReadOnlyList<ScenarioCandidateInput> candidates,
        ScenarioSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adaptiveState);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        var ranked = candidates
            .Where(x => x.IsEligible)
            .Select(x => new ScenarioScoreResult(
                x.ScenarioId,
                Clamp01(0.40 * x.CharacterAlignmentScore + 0.35 * x.NarrativeEvidenceScore + 0.25 * x.PreferencePriorityScore),
                "Weighted deterministic fit score"))
            .OrderByDescending(x => x.FitScore)
            .ThenBy(x => x.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ranked.Count == 0)
        {
            return Task.FromResult(new ScenarioSelectionResult(null, false, ranked, "No eligible scenarios"));
        }

        if (context.ManualOverrideRequested && !string.IsNullOrWhiteSpace(context.ManualOverrideScenarioId))
        {
            return Task.FromResult(new ScenarioSelectionResult(
                context.ManualOverrideScenarioId,
                false,
                ranked,
                "Manual override requested"));
        }

        var deferredForTie = ranked.Count >= 2 && ranked[0].FitScore - ranked[1].FitScore <= 0.10;
        if (deferredForTie)
        {
            return Task.FromResult(new ScenarioSelectionResult(null, true, ranked, "Top candidates tied, defer commitment"));
        }

        var canCommit = ranked[0].FitScore >= 0.60 && context.BuildUpInteractionCount >= 2;
        return Task.FromResult(new ScenarioSelectionResult(
            canCommit ? ranked[0].ScenarioId : null,
            false,
            ranked,
            canCommit ? "Commitment threshold met" : "Commitment threshold not met"));
    }

    private static double Clamp01(double value) => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);
}
