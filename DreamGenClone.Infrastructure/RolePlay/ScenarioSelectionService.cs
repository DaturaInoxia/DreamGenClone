using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioSelectionService : IScenarioSelectionService
{
    private const decimal NearTieThreshold = 0.8m;
    private const int RequiredConsecutiveLeadCount = 2;

    private readonly ILogger<ScenarioSelectionService> _logger;

    public ScenarioSelectionService(ILogger<ScenarioSelectionService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioDefinition> candidates,
        CancellationToken cancellationToken = default)
    {
        var tier = ScenarioEligibilityService.ResolveWillingnessTier(state);
        var stageBEligible = ScenarioEligibilityService.IsEligible(state) && !string.Equals(tier, "Blocked", StringComparison.Ordinal);
        var evaluationId = Guid.NewGuid().ToString("N");

        var evaluations = candidates
            .Select(candidate =>
            {
                var score = stageBEligible
                    ? ScenarioEligibilityService.ComputeFitScore(state, candidate)
                    : 0m;

                var confidence = Math.Clamp((double)score / 100d, 0d, 1d);
                var evaluation = new ScenarioCandidateEvaluation
                {
                    SessionId = state.SessionId,
                    EvaluationId = evaluationId,
                    ScenarioId = candidate.ScenarioId,
                    StageAWillingnessTier = tier,
                    StageBEligible = stageBEligible,
                    FitScore = score,
                    Confidence = decimal.Round((decimal)confidence, 3, MidpointRounding.AwayFromZero),
                    TieBreakKey = $"{candidate.Priority:D3}:{candidate.ScenarioId}",
                    Rationale = stageBEligible
                        ? $"Eligible candidate with computed score {score}."
                        : "Candidate blocked by willingness or eligibility pre-gate.",
                    EvaluatedUtc = DateTime.UtcNow
                };

                _logger.LogInformation(
                    RolePlayV2LogEvents.ScenarioCandidateEvaluated,
                    evaluation.SessionId,
                    evaluation.ScenarioId,
                    evaluation.StageAWillingnessTier,
                    evaluation.StageBEligible,
                    evaluation.FitScore);

                return evaluation;
            })
            .OrderByDescending(x => x.FitScore)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.TieBreakKey, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScenarioCandidateEvaluation>>(evaluations);
    }

    public Task<ScenarioCommitResult> TryCommitScenarioAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioCandidateEvaluation> evaluations,
        CancellationToken cancellationToken = default)
    {
        if (evaluations.Count == 0)
        {
            return Task.FromResult(new ScenarioCommitResult
            {
                Committed = false,
                UpdatedConsecutiveLeadCount = 0,
                Reason = "No candidates were provided."
            });
        }

        var ordered = evaluations
            .OrderByDescending(x => x.FitScore)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.TieBreakKey, StringComparer.Ordinal)
            .ToList();

        var leader = ordered[0];
        if (!leader.StageBEligible)
        {
            return Task.FromResult(new ScenarioCommitResult
            {
                Committed = false,
                UpdatedConsecutiveLeadCount = 0,
                Reason = "Leader is not eligible after two-stage gating.",
                SelectedEvaluation = leader
            });
        }

        var runnerUpScore = ordered.Count > 1 ? ordered[1].FitScore : decimal.MinValue;
        var lead = leader.FitScore - runnerUpScore;
        var nearTie = ordered.Count > 1 && lead <= NearTieThreshold;

        var updatedLeadCount = nearTie
            ? state.ConsecutiveLeadCount + 1
            : RequiredConsecutiveLeadCount;

        if (updatedLeadCount < RequiredConsecutiveLeadCount)
        {
            _logger.LogInformation(
                "RolePlayV2 near-tie resolved by hysteresis: SessionId={SessionId} ScenarioId={ScenarioId} Lead={Lead} Count={Count}",
                state.SessionId,
                leader.ScenarioId,
                lead,
                updatedLeadCount);

            return Task.FromResult(new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = leader.ScenarioId,
                UpdatedConsecutiveLeadCount = updatedLeadCount,
                Reason = "Near tie requires sustained lead before commit.",
                SelectedEvaluation = leader
            });
        }

        _logger.LogInformation(
            RolePlayV2LogEvents.ScenarioCommitted,
            state.SessionId,
            leader.ScenarioId,
            state.CycleIndex,
            NarrativePhase.Committed);

        return Task.FromResult(new ScenarioCommitResult
        {
            Committed = true,
            ScenarioId = leader.ScenarioId,
            UpdatedConsecutiveLeadCount = updatedLeadCount,
            Reason = nearTie
                ? "Committed after sustained near-tie lead."
                : "Committed immediately due to clear score lead.",
            SelectedEvaluation = leader
        });
    }
}
