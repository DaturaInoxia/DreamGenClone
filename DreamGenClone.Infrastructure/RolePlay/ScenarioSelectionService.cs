using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DreamGenClone.Infrastructure.Configuration;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioSelectionService : IScenarioSelectionService
{
    private const decimal NearTieThreshold = 0.8m;
    private const int RequiredConsecutiveLeadCount = 2;

    private readonly ILogger<ScenarioSelectionService> _logger;
    private readonly IThemeCatalogService? _themeCatalogService;
    private readonly ICharacterStateScenarioMapper? _characterStateScenarioMapper;
    private readonly StoryAnalysisOptions _options;

    public ScenarioSelectionService(
        ILogger<ScenarioSelectionService> logger,
        IThemeCatalogService? themeCatalogService = null,
        ICharacterStateScenarioMapper? characterStateScenarioMapper = null,
        IOptions<StoryAnalysisOptions>? options = null)
    {
        _logger = logger;
        _themeCatalogService = themeCatalogService;
        _characterStateScenarioMapper = characterStateScenarioMapper;
        _options = options?.Value ?? new StoryAnalysisOptions();
    }

    public async Task<IReadOnlyList<ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioDefinition> candidates,
        CancellationToken cancellationToken = default)
    {
        var tier = ScenarioEligibilityService.ResolveWillingnessTier(state);
        var stageBEligible = ScenarioEligibilityService.IsEligible(state) && !string.Equals(tier, "Blocked", StringComparison.Ordinal);
        var evaluationId = Guid.NewGuid().ToString("N");
        var fitResults = await ResolveFitResultsAsync(state, candidates, cancellationToken);

        var evaluations = candidates
            .Select(candidate =>
            {
                var fallbackFit = ScenarioEligibilityService.ComputeFitScore(state, candidate);
                var characterAlignmentScore = fitResults.TryGetValue(candidate.ScenarioId, out var fit)
                    ? Clamp01((decimal)fit.FitScore)
                    : Clamp01(fallbackFit / 100m);

                var narrativeEvidenceScore = Clamp01(candidate.NarrativeEvidenceScore);
                var preferencePriorityScore = Clamp01(candidate.PreferencePriorityScore);
                var weightedScore01 = Clamp01(
                    (characterAlignmentScore * 0.50m)
                    + (narrativeEvidenceScore * 0.30m)
                    + (preferencePriorityScore * 0.20m));

                var gate = EvaluateCandidateGate(candidate.ScenarioId, fit, stageBEligible);

                var score = gate.Passed
                    ? decimal.Round(weightedScore01 * 100m, 3, MidpointRounding.AwayFromZero)
                    : 0m;

                var confidence = Math.Clamp((double)score / 100d, 0d, 1d);
                var evaluation = new ScenarioCandidateEvaluation
                {
                    SessionId = state.SessionId,
                    EvaluationId = evaluationId,
                    ScenarioId = candidate.ScenarioId,
                    StageAWillingnessTier = tier,
                    StageBEligible = gate.Passed,
                    CharacterAlignmentScore = decimal.Round(characterAlignmentScore, 3, MidpointRounding.AwayFromZero),
                    NarrativeEvidenceScore = decimal.Round(narrativeEvidenceScore, 3, MidpointRounding.AwayFromZero),
                    PreferencePriorityScore = decimal.Round(preferencePriorityScore, 3, MidpointRounding.AwayFromZero),
                    FitScore = score,
                    Confidence = decimal.Round((decimal)confidence, 3, MidpointRounding.AwayFromZero),
                    TieBreakKey = $"{candidate.Priority:D3}:{candidate.ScenarioId}",
                    Rationale = gate.Passed
                        ? BuildRationale(score, characterAlignmentScore, narrativeEvidenceScore, preferencePriorityScore, fit, gate.Reason)
                        : gate.Reason,
                    DetailsJson = BuildDetailsJson(candidate, fit, gate),
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

        return evaluations;
    }

    private async Task<IReadOnlyDictionary<string, ScenarioFitResult>> ResolveFitResultsAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioDefinition> candidates,
        CancellationToken cancellationToken)
    {
        if (_characterStateScenarioMapper is null || _themeCatalogService is null)
        {
            return new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        }

        var catalogEntries = await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);
        if (catalogEntries.Count == 0)
        {
            return new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        }

        var candidateIds = candidates
            .Select(x => x.ScenarioId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relevantCatalogEntries = catalogEntries
            .Where(x => candidateIds.Contains(x.Id))
            .ToList();

        if (relevantCatalogEntries.Count == 0)
        {
            return new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        }

        return await _characterStateScenarioMapper.EvaluateAllScenariosAsync(state, relevantCatalogEntries, cancellationToken);
    }

    private static string BuildRationale(
        decimal fitScore,
        decimal characterAlignmentScore,
        decimal narrativeEvidenceScore,
        decimal preferencePriorityScore,
        ScenarioFitResult? fit,
        string gateReason)
    {
        var mapperContext = fit is null ? string.Empty : $" Mapper: {fit.Rationale}";
        return $"Weighted fit {fitScore:0.###} from character={characterAlignmentScore:0.###}, narrative={narrativeEvidenceScore:0.###}, preference={preferencePriorityScore:0.###}. Gate: {gateReason}.{mapperContext}";
    }

    private static string BuildDetailsJson(ScenarioDefinition candidate, ScenarioFitResult? fit, CandidateGateDecision gate)
    {
        var details = new
        {
            scenarioId = candidate.ScenarioId,
            candidate.Priority,
            gate = new
            {
                gate.Passed,
                gate.Strategy,
                gate.Reason
            },
            fitResult = fit is null
                ? null
                : new
                {
                    fit.ScenarioId,
                    fit.FitScore,
                    roleScores = fit.CharacterRoleScores,
                    clauseEvaluations = fit.ClauseEvaluations,
                    fit.Rationale,
                    fit.Failures
                }
        };

        return JsonSerializer.Serialize(details);
    }

    private CandidateGateDecision EvaluateCandidateGate(string scenarioId, ScenarioFitResult? fit, bool stageBEligible)
    {
        if (!stageBEligible)
        {
            return new CandidateGateDecision(false, "legacy", "Candidate blocked by willingness or eligibility pre-gate.");
        }

        var strategy = (_options.BuildUpSelectionCandidateGateStrategy ?? "legacy").Trim();
        if (!string.Equals(strategy, "dominant-role", StringComparison.OrdinalIgnoreCase))
        {
            return new CandidateGateDecision(true, "legacy", "Legacy candidate gate passed.");
        }

        if (fit is null || fit.CharacterRoleScores.Count == 0)
        {
            return new CandidateGateDecision(true, "dominant-role", "No role fit breakdown available; dominant-role gate fallback passed.");
        }

        var roleFailures = ParseRoleFailures(fit.Failures);
        var minScore = Math.Clamp(_options.BuildUpSelectionDominantRoleMinScore, 0.0, 1.0);

        var passingRoles = fit.CharacterRoleScores
            .Where(x => x.Value >= minScore && !roleFailures.Contains(x.Key))
            .OrderByDescending(x => x.Value)
            .ToList();

        if (passingRoles.Count > 0)
        {
            var top = passingRoles[0];
            return new CandidateGateDecision(
                true,
                "dominant-role",
                $"Dominant-role gate passed via role '{top.Key}' score={top.Value:0.###} (threshold={minScore:0.###}).");
        }

        var bestRole = fit.CharacterRoleScores
            .OrderByDescending(x => x.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(bestRole.Key))
        {
            return new CandidateGateDecision(false, "dominant-role", "Dominant-role gate failed: no eligible role scores.");
        }

        var failureSuffix = roleFailures.Contains(bestRole.Key)
            ? "best role has one or more failed clauses"
            : "best role score below threshold";
        return new CandidateGateDecision(
            false,
            "dominant-role",
            $"Dominant-role gate failed for scenario '{scenarioId}': best role '{bestRole.Key}' score={bestRole.Value:0.###}, threshold={minScore:0.###}, {failureSuffix}.");
    }

    private static HashSet<string> ParseRoleFailures(IReadOnlyList<string> failures)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var failure in failures)
        {
            if (string.IsNullOrWhiteSpace(failure))
            {
                continue;
            }

            var delimiterIndex = failure.IndexOf('.', StringComparison.Ordinal);
            if (delimiterIndex <= 0)
            {
                continue;
            }

            roles.Add(failure[..delimiterIndex].Trim());
        }

        return roles;
    }

    private sealed record CandidateGateDecision(bool Passed, string Strategy, string Reason);

    private static decimal Clamp01(decimal value)
        => Math.Clamp(decimal.Round(value, 4, MidpointRounding.AwayFromZero), 0m, 1m);

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

        var lead = ordered.Count > 1
            ? leader.FitScore - ordered[1].FitScore
            : decimal.MaxValue;
        var nearTie = ordered.Count > 1 && lead <= NearTieThreshold;
        var tieSet = nearTie
            ? ordered.Where(x => leader.FitScore - x.FitScore <= NearTieThreshold).ToList()
            : [];
        var tieSetSummary = tieSet.Count > 0
            ? string.Join(", ", tieSet.Select(x => $"{x.ScenarioId}:{x.FitScore:0.###}"))
            : string.Empty;

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
                Reason = $"Near tie requires sustained lead before commit. leadDelta={lead:0.###}, tieThreshold={NearTieThreshold:0.###}, tieSet=[{tieSetSummary}]",
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
                ? $"Committed after sustained near-tie lead. leadDelta={lead:0.###}, tieThreshold={NearTieThreshold:0.###}, tieSet=[{tieSetSummary}]"
                : $"Committed immediately due to clear score lead. leadDelta={lead:0.###}",
            SelectedEvaluation = leader
        });
    }
}
