using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DreamGenClone.Infrastructure.Configuration;
using System.Text.Json;
using ThemeCatalogEntry = DreamGenClone.Domain.StoryAnalysis.ThemeCatalogEntry;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioSelectionService : IScenarioSelectionService
{
    private readonly ILogger<ScenarioSelectionService> _logger;
    private readonly IThemeCatalogService? _themeCatalogService;
    private readonly ICharacterStateScenarioMapper? _characterStateScenarioMapper;
    private readonly INarrativeGateProfileService? _narrativeGateProfileService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IScenarioEngineSettingsRepository? _engineSettingsRepository;
    private readonly StoryAnalysisOptions _options;

    public ScenarioSelectionService(
        ILogger<ScenarioSelectionService> logger,
        IThemeCatalogService? themeCatalogService = null,
        ICharacterStateScenarioMapper? characterStateScenarioMapper = null,
        INarrativeGateProfileService? narrativeGateProfileService = null,
        IRPThemeService? rpThemeService = null,
        IScenarioEngineSettingsRepository? engineSettingsRepository = null,
        IOptions<StoryAnalysisOptions>? options = null)
    {
        _logger = logger;
        _themeCatalogService = themeCatalogService;
        _characterStateScenarioMapper = characterStateScenarioMapper;
        _narrativeGateProfileService = narrativeGateProfileService;
        _rpThemeService = rpThemeService;
        _engineSettingsRepository = engineSettingsRepository;
        _options = options?.Value ?? new StoryAnalysisOptions();
    }

    public async Task<IReadOnlyList<ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioDefinition> candidates,
        CancellationToken cancellationToken = default)
    {
        if (_engineSettingsRepository is null)
            throw new InvalidOperationException(
                "ScenarioEngineSettingsRepository is not available. " +
                "Engine settings are required for candidate evaluation; ensure IScenarioEngineSettingsRepository is registered in DI.");

        var engineSettings = await _engineSettingsRepository.LoadAsync(cancellationToken);

        var tier = ScenarioEligibilityService.ResolveWillingnessTier(state, engineSettings);
        var stageBEligible = !string.Equals(tier, "Blocked", StringComparison.Ordinal);
        var evaluationId = Guid.NewGuid().ToString("N");
        var fitResults = await ResolveFitResultsAsync(state, candidates, cancellationToken);

        var charWeight = Clamp01((decimal)(engineSettings.CandidateCharacterAlignmentWeight));
        var narWeight = Clamp01((decimal)(engineSettings.CandidateNarrativeEvidenceWeight));
        var prefWeight = Clamp01((decimal)(engineSettings.CandidatePreferencePriorityWeight));

        var evaluations = candidates
            .Select(candidate =>
            {
                var fallbackFit = ScenarioEligibilityService.ComputeFitScore(state, candidate, engineSettings);
                var characterAlignmentScore = fitResults.TryGetValue(candidate.ScenarioId, out var fit)
                    ? Clamp01((decimal)fit.FitScore)
                    : Clamp01(fallbackFit / 100m);

                var narrativeEvidenceScore = Clamp01(candidate.NarrativeEvidenceScore);
                var preferencePriorityScore = Clamp01(candidate.PreferencePriorityScore);
                var weightedScore01 = Clamp01(
                    (characterAlignmentScore * charWeight)
                    + (narrativeEvidenceScore * narWeight)
                    + (preferencePriorityScore * prefWeight));

                var gate = EvaluateCandidateGate(candidate.ScenarioId, fit, stageBEligible);

                var weightedScore = decimal.Round(weightedScore01 * 100m, 3, MidpointRounding.AwayFromZero);
                var gateFailPenaltyMultiplier = Clamp01((decimal)_options.GateFailScorePenaltyMultiplier);
                var score = gate.Passed
                    ? weightedScore
                    : decimal.Round(weightedScore * gateFailPenaltyMultiplier, 3, MidpointRounding.AwayFromZero);

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
                    UnpenalizedFitScore = weightedScore,
                    Confidence = decimal.Round((decimal)confidence, 3, MidpointRounding.AwayFromZero),
                    TieBreakKey = $"{candidate.Priority:D3}:{candidate.ScenarioId}",
                    Rationale = gate.Passed
                        ? BuildRationale(score, characterAlignmentScore, narrativeEvidenceScore, preferencePriorityScore, fit, gate.Reason)
                        : $"{gate.Reason} Penalized weighted score from {weightedScore:0.###} to {score:0.###} (multiplier={gateFailPenaltyMultiplier:0.###}).",
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
        if (_characterStateScenarioMapper is null)
        {
            return new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        }

        var entriesToEvaluate = new List<ThemeCatalogEntry>();

        var directRuleCandidates = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioId) && !string.IsNullOrWhiteSpace(x.ScenarioFitRulesJson))
            .GroupBy(x => x.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        foreach (var candidate in directRuleCandidates)
        {
            entriesToEvaluate.Add(new ThemeCatalogEntry
            {
                Id = candidate.ScenarioId,
                Label = candidate.Name,
                ScenarioFitRules = candidate.ScenarioFitRulesJson ?? string.Empty,
                IsEnabled = true
            });
        }

        var candidateIds = candidates
            .Select(x => x.ScenarioId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unresolvedCandidateIds = candidateIds
            .Where(x => directRuleCandidates.All(d => !string.Equals(d.ScenarioId, x, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (unresolvedCandidateIds.Count > 0 && _themeCatalogService is not null)
        {
            var catalogEntries = await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);
            if (catalogEntries.Count > 0)
            {
                var relevantCatalogEntries = catalogEntries
                    .Where(x => unresolvedCandidateIds.Contains(x.Id))
                    .ToList();

                entriesToEvaluate.AddRange(relevantCatalogEntries);
            }
        }

        if (entriesToEvaluate.Count == 0)
        {
            return new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        }

        return await _characterStateScenarioMapper.EvaluateAllScenariosAsync(state, entriesToEvaluate, cancellationToken);
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
        var parsedFitRules = ParseScenarioFitRules(candidate.ScenarioFitRulesJson);

        var fitRuleSource = !string.IsNullOrWhiteSpace(candidate.ScenarioFitRuleSource)
            ? candidate.ScenarioFitRuleSource
            : !string.IsNullOrWhiteSpace(candidate.ScenarioFitRulesJson)
                ? "candidate-inline"
                : "theme-catalog";

        var details = new
        {
            scenarioId = candidate.ScenarioId,
            candidate.Priority,
            fitRuleSource,
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
                    roleWeights = parsedFitRules?.CharacterRoleWeights,
                    roleCharacterBindings = parsedFitRules?.RoleCharacterBindings,
                    clauseEvaluations = fit.ClauseEvaluations,
                    fit.Rationale,
                    fit.Failures
                }
        };

        return JsonSerializer.Serialize(details);
    }

    private static ScenarioFitRules? ParseScenarioFitRules(string? fitRulesJson)
    {
        if (string.IsNullOrWhiteSpace(fitRulesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScenarioFitRules>(fitRulesJson);
        }
        catch
        {
            return null;
        }
    }

    private CandidateGateDecision EvaluateCandidateGate(string scenarioId, ScenarioFitResult? fit, bool stageBEligible)
    {
        if (!stageBEligible)
        {
            return new CandidateGateDecision(false, "pre-gate", "Candidate blocked by willingness or eligibility pre-gate.");
        }

        var rawStrategy = _options.BuildUpSelectionCandidateGateStrategy;
        if (string.IsNullOrWhiteSpace(rawStrategy))
            throw new InvalidOperationException(
                "BuildUpSelectionCandidateGateStrategy is not configured. " +
                "A gate strategy value is required; set it in appsettings or via the engine settings UI.");

        var strategy = rawStrategy.Trim();
        if (!string.Equals(strategy, "dominant-role", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unrecognized BuildUpSelectionCandidateGateStrategy '{strategy}'. " +
                "The only supported V2 strategy is 'dominant-role'. Update the configuration to remove the legacy strategy.");
        }

        if (fit is null || fit.CharacterRoleScores.Count == 0)
        {
            return new CandidateGateDecision(true, "dominant-role", "No role fit breakdown available; dominant-role gate fallback passed.");
        }

        var roleFailures = ParseRoleFailures(fit.Failures);

        var passingRoles = fit.CharacterRoleScores
            .Where(x => !roleFailures.Contains(x.Key))
            .OrderByDescending(x => x.Value)
            .ToList();

        if (passingRoles.Count > 0)
        {
            var top = passingRoles[0];
            return new CandidateGateDecision(
                true,
                "dominant-role",
                $"Dominant-role gate passed via role '{top.Key}' (no clause failures, score={top.Value:0.###}).");
        }

        var bestRole = fit.CharacterRoleScores
            .OrderByDescending(x => x.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(bestRole.Key))
        {
            return new CandidateGateDecision(false, "dominant-role", "Dominant-role gate failed: no eligible role scores.");
        }

        return new CandidateGateDecision(
            false,
            "dominant-role",
            $"Dominant-role gate failed for scenario '{scenarioId}': all evaluated roles have failed clauses ({string.Join(", ", roleFailures)}).");
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

    public async Task<ScenarioCommitResult> TryCommitScenarioAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioCandidateEvaluation> evaluations,
        CancellationToken cancellationToken = default)
    {
        if (_engineSettingsRepository is null)
            throw new InvalidOperationException(
                "ScenarioEngineSettingsRepository is not available. " +
                "Engine settings are required for scenario commit evaluation; ensure IScenarioEngineSettingsRepository is registered in DI.");

        var engineSettings = await _engineSettingsRepository.LoadAsync(cancellationToken);
        var nearTieThreshold = (decimal)engineSettings.NearTieThreshold;
        var requiredLeadCount = engineSettings.RequiredConsecutiveLeadCount;

        if (evaluations.Count == 0)
        {
            return new ScenarioCommitResult
            {
                Committed = false,
                UpdatedConsecutiveLeadCount = 0,
                Reason = "No candidates were provided."
            };
        }

        var ordered = evaluations
            .OrderByDescending(x => x.FitScore)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.TieBreakKey, StringComparer.Ordinal)
            .ToList();

        var leader = ordered[0];
        if (!leader.StageBEligible)
        {
            return new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = leader.ScenarioId,
                UpdatedConsecutiveLeadCount = 0,
                Reason = "Leader is not eligible after two-stage gating.",
                SelectedEvaluation = leader
            };
        }

        if (state.CurrentPhase == NarrativePhase.BuildUp)
        {
            var profileGate = await EvaluateBuildUpProfileGateAsync(state, leader, cancellationToken);
            if (!profileGate.Passed)
            {
                return new ScenarioCommitResult
                {
                    Committed = false,
                    ScenarioId = leader.ScenarioId,
                    UpdatedConsecutiveLeadCount = 0,
                    Reason = profileGate.Reason,
                    SelectedEvaluation = leader,
                    AuditMetadataJson = profileGate.AuditMetadataJson
                };
            }

            var commitCandidateAuditMetadataJson = profileGate.AuditMetadataJson;

            // If the gate passed and the leading scenario is already the active scenario for this
            // session, the scenario selection decision was already made in a prior cycle. Skip
            // tie-breaking hysteresis entirely and commit directly — there is nothing left to decide.
            var leaderIsActiveScenario = !string.IsNullOrWhiteSpace(state.ActiveScenarioId)
                && string.Equals(state.ActiveScenarioId, leader.ScenarioId, StringComparison.OrdinalIgnoreCase);
            if (leaderIsActiveScenario)
            {
                _logger.LogInformation(
                    "RolePlayV2 BuildUp commit: active scenario confirmed as leader, skipping tie-breaker: SessionId={SessionId} ScenarioId={ScenarioId}",
                    state.SessionId,
                    leader.ScenarioId);

                return new ScenarioCommitResult
                {
                    Committed = true,
                    ScenarioId = leader.ScenarioId,
                    UpdatedConsecutiveLeadCount = requiredLeadCount,
                    Reason = "Committed because gate passed and leader matches already-active scenario.",
                    SelectedEvaluation = leader,
                    AuditMetadataJson = commitCandidateAuditMetadataJson
                };
            }

            var lead = ordered.Count > 1
                ? leader.FitScore - ordered[1].FitScore
                : decimal.MaxValue;
            var nearTie = ordered.Count > 1 && lead <= nearTieThreshold;
            var tieSet = nearTie
                ? ordered.Where(x => leader.FitScore - x.FitScore <= nearTieThreshold).ToList()
                : [];
            var tieSetSummary = tieSet.Count > 0
                ? string.Join(", ", tieSet.Select(x => $"{x.ScenarioId}:{x.FitScore:0.###}"))
                : string.Empty;

            var updatedLeadCount = nearTie
                ? state.ConsecutiveLeadCount + 1
                : requiredLeadCount;

            if (updatedLeadCount < requiredLeadCount)
            {
                _logger.LogInformation(
                    "RolePlayV2 near-tie resolved by hysteresis: SessionId={SessionId} ScenarioId={ScenarioId} Lead={Lead} Count={Count}",
                    state.SessionId,
                    leader.ScenarioId,
                    lead,
                    updatedLeadCount);

                return new ScenarioCommitResult
                {
                    Committed = false,
                    ScenarioId = leader.ScenarioId,
                    UpdatedConsecutiveLeadCount = updatedLeadCount,
                    Reason = $"Near tie requires sustained lead before commit. leadDelta={lead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{tieSetSummary}]",
                    SelectedEvaluation = leader,
                    AuditMetadataJson = commitCandidateAuditMetadataJson
                };
            }

            var sameAsActiveScenario = !string.IsNullOrWhiteSpace(state.ActiveScenarioId)
                && string.Equals(state.ActiveScenarioId, leader.ScenarioId, StringComparison.OrdinalIgnoreCase);
            var inActiveArc = state.CurrentPhase is not NarrativePhase.BuildUp and not NarrativePhase.Reset;
            if (sameAsActiveScenario && inActiveArc)
            {
                return new ScenarioCommitResult
                {
                    Committed = false,
                    ScenarioId = leader.ScenarioId,
                    UpdatedConsecutiveLeadCount = updatedLeadCount,
                    Reason = nearTie
                        ? $"Leader unchanged; active scenario already committed. Suppressing no-op recommit. leadDelta={lead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{tieSetSummary}]"
                        : $"Leader unchanged; active scenario already committed. Suppressing no-op recommit. leadDelta={lead:0.###}",
                    SelectedEvaluation = leader,
                    AuditMetadataJson = commitCandidateAuditMetadataJson
                };
            }

            _logger.LogInformation(
                RolePlayV2LogEvents.ScenarioCommitted,
                state.SessionId,
                leader.ScenarioId,
                state.CycleIndex,
                NarrativePhase.Committed);

            return new ScenarioCommitResult
            {
                Committed = true,
                ScenarioId = leader.ScenarioId,
                UpdatedConsecutiveLeadCount = updatedLeadCount,
                Reason = nearTie
                    ? $"Committed after sustained near-tie lead. leadDelta={lead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{tieSetSummary}]"
                    : $"Committed immediately due to clear score lead. leadDelta={lead:0.###}",
                SelectedEvaluation = leader,
                AuditMetadataJson = commitCandidateAuditMetadataJson
            };
        }

        var nonBuildUpLead = ordered.Count > 1
            ? leader.FitScore - ordered[1].FitScore
            : decimal.MaxValue;
        var nonBuildUpNearTie = ordered.Count > 1 && nonBuildUpLead <= nearTieThreshold;
        var nonBuildUpTieSet = nonBuildUpNearTie
            ? ordered.Where(x => leader.FitScore - x.FitScore <= nearTieThreshold).ToList()
            : [];
        var nonBuildUpTieSetSummary = nonBuildUpTieSet.Count > 0
            ? string.Join(", ", nonBuildUpTieSet.Select(x => $"{x.ScenarioId}:{x.FitScore:0.###}"))
            : string.Empty;

        var nonBuildUpUpdatedLeadCount = nonBuildUpNearTie
            ? state.ConsecutiveLeadCount + 1
            : requiredLeadCount;

        if (nonBuildUpUpdatedLeadCount < requiredLeadCount)
        {
            _logger.LogInformation(
                "RolePlayV2 near-tie resolved by hysteresis: SessionId={SessionId} ScenarioId={ScenarioId} Lead={Lead} Count={Count}",
                state.SessionId,
                leader.ScenarioId,
                nonBuildUpLead,
                nonBuildUpUpdatedLeadCount);

            return new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = leader.ScenarioId,
                UpdatedConsecutiveLeadCount = nonBuildUpUpdatedLeadCount,
                Reason = $"Near tie requires sustained lead before commit. leadDelta={nonBuildUpLead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{nonBuildUpTieSetSummary}]",
                SelectedEvaluation = leader
            };
        }

        var nonBuildUpSameAsActiveScenario = !string.IsNullOrWhiteSpace(state.ActiveScenarioId)
            && string.Equals(state.ActiveScenarioId, leader.ScenarioId, StringComparison.OrdinalIgnoreCase);
        var nonBuildUpInActiveArc = state.CurrentPhase is not NarrativePhase.BuildUp and not NarrativePhase.Reset;
        if (nonBuildUpSameAsActiveScenario && nonBuildUpInActiveArc)
        {
            return new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = leader.ScenarioId,
                UpdatedConsecutiveLeadCount = nonBuildUpUpdatedLeadCount,
                Reason = nonBuildUpNearTie
                    ? $"Leader unchanged; active scenario already committed. Suppressing no-op recommit. leadDelta={nonBuildUpLead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{nonBuildUpTieSetSummary}]"
                    : $"Leader unchanged; active scenario already committed. Suppressing no-op recommit. leadDelta={nonBuildUpLead:0.###}",
                SelectedEvaluation = leader
            };
        }

        _logger.LogInformation(
            RolePlayV2LogEvents.ScenarioCommitted,
            state.SessionId,
            leader.ScenarioId,
            state.CycleIndex,
            NarrativePhase.Committed);

        return new ScenarioCommitResult
        {
            Committed = true,
            ScenarioId = leader.ScenarioId,
            UpdatedConsecutiveLeadCount = nonBuildUpUpdatedLeadCount,
            Reason = nonBuildUpNearTie
                ? $"Committed after sustained near-tie lead. leadDelta={nonBuildUpLead:0.###}, tieThreshold={nearTieThreshold:0.###}, tieSet=[{nonBuildUpTieSetSummary}]"
                : $"Committed immediately due to clear score lead. leadDelta={nonBuildUpLead:0.###}",
            SelectedEvaluation = leader
        };
    }

    private async Task<BuildUpGateEvaluation> EvaluateBuildUpProfileGateAsync(
        AdaptiveScenarioState state,
        ScenarioCandidateEvaluation leader,
        CancellationToken cancellationToken)
    {
        NarrativeGateProfile? profile;
        var profileSource = "selected";
        if (!string.IsNullOrWhiteSpace(state.SelectedNarrativeGateProfileId))
        {
            if (_narrativeGateProfileService is null)
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    "BuildUp profile gate missing required configuration: selected narrative gate profile service is unavailable.");
            }

            profile = await _narrativeGateProfileService.GetAsync(state.SelectedNarrativeGateProfileId, cancellationToken);
            if (profile is null)
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    $"BuildUp profile gate missing required configuration: selected narrative gate profile '{state.SelectedNarrativeGateProfileId}' not found.");
            }
        }
        else
        {
            if (_rpThemeService is null)
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    "BuildUp profile gate missing required configuration: RP theme service is unavailable.");
            }

            var themeId = !string.IsNullOrWhiteSpace(leader.ScenarioId)
                ? leader.ScenarioId
                : state.ActiveScenarioId;
            if (string.IsNullOrWhiteSpace(themeId))
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    "BuildUp profile gate missing required configuration: no leader scenario id is available for theme-local gate rules.");
            }

            var theme = await _rpThemeService.GetThemeAsync(themeId, cancellationToken);
            if (theme is null)
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    $"BuildUp profile gate missing required configuration: theme '{themeId}' not found.");
            }

            if (theme.NarrativeGateRules.Count == 0)
            {
                return BuildUpGateEvaluation.CreateNotConfigured(
                    $"BuildUp profile gate missing required configuration: theme '{theme.Id}' has no narrative gate rules.");
            }

            profile = new NarrativeGateProfile
            {
                Id = theme.Id,
                Name = "Theme Local Rules",
                Rules = theme.NarrativeGateRules
                    .OrderBy(rule => rule.SortOrder)
                    .Select((rule, index) => new NarrativeGateRule
                    {
                        SortOrder = index + 1,
                        FromPhase = rule.FromPhase,
                        ToPhase = rule.ToPhase,
                        MetricKey = rule.MetricKey,
                        Comparator = rule.Comparator,
                        Threshold = rule.Threshold
                    })
                    .ToList()
            };
            profileSource = "theme-local";
        }

        if (profile is null || profile.Rules.Count == 0)
        {
            return BuildUpGateEvaluation.CreateNotConfigured("BuildUp profile gate not configured.");
        }

        var rules = profile.Rules
            .Where(rule => string.Equals(rule.FromPhase, "BuildUp", StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.ToPhase, "Committed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.SortOrder)
            .ToList();

        if (rules.Count == 0)
        {
            return BuildUpGateEvaluation.CreateNotConfigured("BuildUp profile gate not configured.");
        }

        var averageDesire = state.CharacterSnapshots.Count == 0 ? 50m : (decimal)state.CharacterSnapshots.Average(x => x.Desire);
        var averageRestraint = state.CharacterSnapshots.Count == 0 ? 50m : (decimal)state.CharacterSnapshots.Average(x => x.Restraint);
        var metricValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [NarrativeGateMetricKeys.ActiveScenarioScore] = leader.FitScore,
            [NarrativeGateMetricKeys.AverageDesire] = averageDesire,
            [NarrativeGateMetricKeys.AverageRestraint] = averageRestraint,
            [NarrativeGateMetricKeys.InteractionsSinceCommitment] = state.InteractionCountInPhase
        };

        var audits = new List<BuildUpGateRuleAudit>(rules.Count);

        foreach (var rule in rules)
        {
            if (!metricValues.TryGetValue(rule.MetricKey, out var actualValue))
            {
                audits.Add(new BuildUpGateRuleAudit(
                    rule.SortOrder,
                    rule.MetricKey,
                    rule.Comparator,
                    rule.Threshold,
                    null,
                    false,
                    "MetricUnavailable"));

                return BuildUpGateEvaluation.CreateFailed(
                    profile,
                    profileSource,
                    audits,
                    $"BuildUp profile gate blocked commit: metric '{rule.MetricKey}' unavailable.");
            }

            if (!TryCompare(actualValue, rule.Comparator, rule.Threshold, out var passed))
            {
                audits.Add(new BuildUpGateRuleAudit(
                    rule.SortOrder,
                    rule.MetricKey,
                    rule.Comparator,
                    rule.Threshold,
                    actualValue,
                    false,
                    "InvalidComparator"));

                return BuildUpGateEvaluation.CreateFailed(
                    profile,
                    profileSource,
                    audits,
                    $"BuildUp profile gate blocked commit: comparator '{rule.Comparator}' is invalid for metric '{rule.MetricKey}'.");
            }

            audits.Add(new BuildUpGateRuleAudit(
                rule.SortOrder,
                rule.MetricKey,
                rule.Comparator,
                rule.Threshold,
                actualValue,
                passed,
                passed ? "Passed" : "ThresholdNotMet"));

            if (!passed)
            {
                return BuildUpGateEvaluation.CreateFailed(
                    profile,
                    profileSource,
                    audits,
                    $"BuildUp profile gate blocked commit: {rule.MetricKey} {rule.Comparator} {rule.Threshold:0.###} not met (actual={actualValue:0.###}) in profile '{profile.Name}'.");
            }
        }

        return BuildUpGateEvaluation.CreatePassed(
            profile,
            profileSource,
            audits,
            $"BuildUp profile gate passed via profile '{profile.Name}'.");
    }

    private static bool TryCompare(decimal actual, string comparator, decimal threshold, out bool passed)
    {
        switch (comparator.Trim())
        {
            case NarrativeGateComparators.GreaterThanOrEqual:
                passed = actual >= threshold;
                return true;
            case NarrativeGateComparators.GreaterThan:
                passed = actual > threshold;
                return true;
            case NarrativeGateComparators.LessThanOrEqual:
                passed = actual <= threshold;
                return true;
            case NarrativeGateComparators.LessThan:
                passed = actual < threshold;
                return true;
            case NarrativeGateComparators.Equal:
                passed = actual == threshold;
                return true;
            default:
                passed = false;
                return false;
        }
    }

    private sealed record BuildUpGateRuleAudit(
        int SortOrder,
        string MetricKey,
        string Comparator,
        decimal Threshold,
        decimal? Actual,
        bool Passed,
        string Status);

    private sealed record BuildUpGateAuditEnvelope(
        bool Passed,
        bool Configured,
        string? ProfileId,
        string? ProfileName,
        string? ProfileSource,
        IReadOnlyList<BuildUpGateRuleAudit> Rules,
        string Reason);

    private sealed class BuildUpGateEvaluation
    {
        public bool Passed { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string AuditMetadataJson { get; init; } = "{}";

        public static BuildUpGateEvaluation CreateNotConfigured(string reason)
        {
            var audit = new BuildUpGateAuditEnvelope(
                Passed: false,
                Configured: false,
                ProfileId: null,
                ProfileName: null,
                ProfileSource: null,
                Rules: [],
                Reason: reason);

            return new BuildUpGateEvaluation
            {
                Passed = false,
                Reason = reason,
                AuditMetadataJson = JsonSerializer.Serialize(audit)
            };
        }

        public static BuildUpGateEvaluation CreateFailed(
            NarrativeGateProfile profile,
            string profileSource,
            IReadOnlyList<BuildUpGateRuleAudit> audits,
            string reason)
        {
            var audit = new BuildUpGateAuditEnvelope(
                Passed: false,
                Configured: true,
                ProfileId: profile.Id,
                ProfileName: profile.Name,
                ProfileSource: profileSource,
                Rules: audits,
                Reason: reason);

            return new BuildUpGateEvaluation
            {
                Passed = false,
                Reason = reason,
                AuditMetadataJson = JsonSerializer.Serialize(audit)
            };
        }

        public static BuildUpGateEvaluation CreatePassed(
            NarrativeGateProfile profile,
            string profileSource,
            IReadOnlyList<BuildUpGateRuleAudit> audits,
            string reason)
        {
            var audit = new BuildUpGateAuditEnvelope(
                Passed: true,
                Configured: true,
                ProfileId: profile.Id,
                ProfileName: profile.Name,
                ProfileSource: profileSource,
                Rules: audits,
                Reason: reason);

            return new BuildUpGateEvaluation
            {
                Passed = true,
                Reason = reason,
                AuditMetadataJson = JsonSerializer.Serialize(audit)
            };
        }
    }
}
