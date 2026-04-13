using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioSelectionService : IScenarioSelectionService
{
    private const decimal NearTieThreshold = 0.8m;
    private const int RequiredConsecutiveLeadCount = 2;

    private readonly ILogger<ScenarioSelectionService> _logger;
    private readonly IThemeCatalogService? _themeCatalogService;
    private readonly ICharacterStateScenarioMapper? _characterStateScenarioMapper;

    public ScenarioSelectionService(
        ILogger<ScenarioSelectionService> logger,
        IThemeCatalogService? themeCatalogService = null,
        ICharacterStateScenarioMapper? characterStateScenarioMapper = null)
    {
        _logger = logger;
        _themeCatalogService = themeCatalogService;
        _characterStateScenarioMapper = characterStateScenarioMapper;
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

                var score = stageBEligible ? decimal.Round(weightedScore01 * 100m, 3, MidpointRounding.AwayFromZero) : 0m;

                var confidence = Math.Clamp((double)score / 100d, 0d, 1d);
                var evaluation = new ScenarioCandidateEvaluation
                {
                    SessionId = state.SessionId,
                    EvaluationId = evaluationId,
                    ScenarioId = candidate.ScenarioId,
                    StageAWillingnessTier = tier,
                    StageBEligible = stageBEligible,
                    CharacterAlignmentScore = decimal.Round(characterAlignmentScore, 3, MidpointRounding.AwayFromZero),
                    NarrativeEvidenceScore = decimal.Round(narrativeEvidenceScore, 3, MidpointRounding.AwayFromZero),
                    PreferencePriorityScore = decimal.Round(preferencePriorityScore, 3, MidpointRounding.AwayFromZero),
                    FitScore = score,
                    Confidence = decimal.Round((decimal)confidence, 3, MidpointRounding.AwayFromZero),
                    TieBreakKey = $"{candidate.Priority:D3}:{candidate.ScenarioId}",
                    Rationale = stageBEligible
                        ? BuildRationale(score, characterAlignmentScore, narrativeEvidenceScore, preferencePriorityScore, fit)
                        : "Candidate blocked by willingness or eligibility pre-gate.",
                    DetailsJson = BuildDetailsJson(candidate, fit),
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
        ScenarioFitResult? fit)
    {
        var mapperContext = fit is null ? string.Empty : $" Mapper: {fit.Rationale}";
        return $"Weighted fit {fitScore:0.###} from character={characterAlignmentScore:0.###}, narrative={narrativeEvidenceScore:0.###}, preference={preferencePriorityScore:0.###}.{mapperContext}";
    }

    private static string BuildDetailsJson(ScenarioDefinition candidate, ScenarioFitResult? fit)
    {
        var details = new
        {
            scenarioId = candidate.ScenarioId,
            candidate.Priority,
            fitResult = fit is null
                ? null
                : new
                {
                    fit.ScenarioId,
                    fit.FitScore,
                    roleScores = fit.CharacterRoleScores,
                    fit.Rationale,
                    fit.Failures
                }
        };

        return JsonSerializer.Serialize(details);
    }

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
