using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ScenarioSelectionEngine : IScenarioSelectionEngine
{
    private readonly StoryAnalysisOptions _options;
    private readonly IReadOnlyDictionary<string, IScenarioFitScoreStrategy> _fitScoreStrategies;
    private readonly IReadOnlyDictionary<string, IScenarioTieBreakStrategy> _tieBreakStrategies;

    public ScenarioSelectionEngine(
        IEnumerable<IScenarioFitScoreStrategy>? fitScoreStrategies = null,
        IEnumerable<IScenarioTieBreakStrategy>? tieBreakStrategies = null,
        IOptions<StoryAnalysisOptions>? options = null)
    {
        _options = options?.Value ?? new StoryAnalysisOptions();

        fitScoreStrategies ??= [new WeightedBlendScenarioFitScoreStrategy()];
        tieBreakStrategies ??= [new TieWindowScenarioTieBreakStrategy()];

        _fitScoreStrategies = fitScoreStrategies
            .GroupBy(x => x.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

        _tieBreakStrategies = tieBreakStrategies
            .GroupBy(x => x.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);
    }

    public Task<ScenarioSelectionResult> EvaluateAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        IReadOnlyList<ScenarioCandidateInput> candidates,
        ScenarioSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adaptiveState);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        var fitScoreStrategy = ResolveFitScoreStrategy();
        var tieBreakStrategy = ResolveTieBreakStrategy();

        var ranked = candidates
            .Where(x => x.IsEligible)
            .Select(x => fitScoreStrategy.ScoreCandidate(x, adaptiveState, context))
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

        var tieDecision = tieBreakStrategy.Evaluate(
            ranked,
            context,
            Math.Clamp(_options.BuildUpSelectionTieDeltaThreshold, 0.0, 1.0));

        if (tieDecision.DeferredForTie)
        {
            return Task.FromResult(new ScenarioSelectionResult(null, true, ranked, tieDecision.Rationale));
        }

        var canCommit = ranked[0].FitScore >= Math.Clamp(_options.BuildUpSelectionCommitThreshold, 0.0, 1.0)
            && context.BuildUpInteractionCount >= 2;
        return Task.FromResult(new ScenarioSelectionResult(
            canCommit ? ranked[0].ScenarioId : null,
            false,
            ranked,
            canCommit ? "Commitment threshold met" : "Commitment threshold not met"));
    }

    private IScenarioFitScoreStrategy ResolveFitScoreStrategy()
    {
        if (!string.IsNullOrWhiteSpace(_options.BuildUpSelectionFitScoreStrategy)
            && _fitScoreStrategies.TryGetValue(_options.BuildUpSelectionFitScoreStrategy, out var configuredStrategy))
        {
            return configuredStrategy;
        }

        if (_fitScoreStrategies.TryGetValue(WeightedBlendScenarioFitScoreStrategy.StrategyKey, out var defaultStrategy))
        {
            return defaultStrategy;
        }

        return _fitScoreStrategies.Values.First();
    }

    private IScenarioTieBreakStrategy ResolveTieBreakStrategy()
    {
        if (!string.IsNullOrWhiteSpace(_options.BuildUpSelectionTieBreakStrategy)
            && _tieBreakStrategies.TryGetValue(_options.BuildUpSelectionTieBreakStrategy, out var configuredStrategy))
        {
            return configuredStrategy;
        }

        if (_tieBreakStrategies.TryGetValue(TieWindowScenarioTieBreakStrategy.StrategyKey, out var defaultStrategy))
        {
            return defaultStrategy;
        }

        return _tieBreakStrategies.Values.First();
    }

    private static double Clamp01(double value) => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);
}
