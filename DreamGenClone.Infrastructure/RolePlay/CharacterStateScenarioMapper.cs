using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class CharacterStateScenarioMapper : ICharacterStateScenarioMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<CharacterStateScenarioMapper> _logger;

    public CharacterStateScenarioMapper(ILogger<CharacterStateScenarioMapper> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<string, ScenarioFitResult>> EvaluateAllScenariosAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ThemeCatalogEntry> catalogEntries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogEntries);

        var results = new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase);
        var snapshots = state.CharacterSnapshots
            .ToDictionary(x => x.CharacterId, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalogEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                continue;
            }

            results[entry.Id] = EvaluateScenario(entry.Id, entry.ScenarioFitRules, snapshots);
        }

        return Task.FromResult<IReadOnlyDictionary<string, ScenarioFitResult>>(results);
    }

    private ScenarioFitResult EvaluateScenario(
        string scenarioId,
        string? rulesJson,
        IReadOnlyDictionary<string, CharacterStatProfileV2> snapshots)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
        {
            return new ScenarioFitResult
            {
                ScenarioId = scenarioId,
                FitScore = 0.5,
                Rationale = "No scenario fit rules configured; default fit score applied."
            };
        }

        ScenarioFitRules? rules;
        try
        {
            rules = JsonSerializer.Deserialize<ScenarioFitRules>(rulesJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse scenario fit rules for {ScenarioId}", scenarioId);
            return new ScenarioFitResult
            {
                ScenarioId = scenarioId,
                FitScore = 0.5,
                Rationale = "Scenario fit rules are malformed; default fit score applied.",
                Failures = ["Malformed ScenarioFitRules JSON"]
            };
        }

        if (rules is null || rules.CharacterRoleRules.Count == 0)
        {
            return new ScenarioFitResult
            {
                ScenarioId = scenarioId,
                FitScore = 0.5,
                Rationale = "Scenario fit rules are empty; default fit score applied."
            };
        }

        var roleScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var roleExplanations = new List<string>();

        foreach (var roleRule in rules.CharacterRoleRules)
        {
            var roleName = string.IsNullOrWhiteSpace(roleRule.RoleName) ? "default" : roleRule.RoleName.Trim();
            var mappedCharacterId = ResolveCharacterId(roleName, rules.RoleCharacterBindings);
            var profile = TryResolveProfile(mappedCharacterId, snapshots);

            var roleScore = EvaluateRole(roleName, roleRule, profile, failures, roleExplanations);
            roleScores[roleName] = roleScore;
        }

        var weightedScore = CombineRoleScores(roleScores, rules.CharacterRoleWeights);
        weightedScore = ApplyScenarioModifiers(weightedScore, roleScores, rules.ScenarioModifiers, failures);

        var rationale = roleExplanations.Count == 0
            ? "Scenario fit evaluation completed."
            : string.Join(" ", roleExplanations);

        return new ScenarioFitResult
        {
            ScenarioId = scenarioId,
            FitScore = Clamp01(weightedScore),
            CharacterRoleScores = roleScores,
            Rationale = rationale,
            Failures = failures
        };
    }

    private static string ResolveCharacterId(string roleName, IReadOnlyDictionary<string, string> bindings)
    {
        if (bindings.TryGetValue(roleName, out var explicitBinding) && !string.IsNullOrWhiteSpace(explicitBinding))
        {
            return explicitBinding.Trim();
        }

        return roleName;
    }

    private static CharacterStatProfileV2 TryResolveProfile(
        string characterId,
        IReadOnlyDictionary<string, CharacterStatProfileV2> snapshots)
    {
        if (!string.IsNullOrWhiteSpace(characterId) && snapshots.TryGetValue(characterId, out var mapped))
        {
            return mapped;
        }

        return CharacterStatProfileV2Accessor.CreateDefault(characterId);
    }

    private static double EvaluateRole(
        string roleName,
        CharacterRoleRule roleRule,
        CharacterStatProfileV2 profile,
        List<string> failures,
        List<string> roleExplanations)
    {
        if (roleRule.StatThresholds.Count == 0)
        {
            roleExplanations.Add($"Role '{roleName}' had no stat thresholds; neutral score applied.");
            return 0.5;
        }

        double weightedTotal = 0;
        double totalWeight = 0;

        foreach (var (statNameRaw, threshold) in roleRule.StatThresholds)
        {
            var statName = statNameRaw.Trim();
            var value = ReadStat(profile, statName);
            var statWeight = roleRule.StatWeights.TryGetValue(statName, out var configuredWeight)
                ? Math.Max(configuredWeight, 0.0)
                : 1.0;

            var component = EvaluateStatComponent(roleName, statName, value, threshold, failures, roleExplanations);
            weightedTotal += component * statWeight;
            totalWeight += statWeight;
        }

        if (totalWeight <= 0)
        {
            roleExplanations.Add($"Role '{roleName}' had zero effective stat weight; neutral score applied.");
            return 0.5;
        }

        var score = weightedTotal / totalWeight;
        roleExplanations.Add($"Role '{roleName}' score={score:0.###}.");
        return Clamp01(score);
    }

    private static double EvaluateStatComponent(
        string roleName,
        string statName,
        int value,
        StatThresholdSpecification threshold,
        List<string> failures,
        List<string> roleExplanations)
    {
        var penaltyWeight = Math.Max(threshold.PenaltyWeight, 0.0);
        var component = 1.0;

        if (threshold.MinimumValue is not null && value < threshold.MinimumValue.Value)
        {
            var denominator = Math.Max(threshold.MinimumValue.Value, 1.0);
            var penalty = ((threshold.MinimumValue.Value - value) / denominator) * penaltyWeight;
            component -= penalty;
            failures.Add($"{roleName}.{statName} below minimum ({value} < {threshold.MinimumValue.Value:0.###})");
        }

        if (threshold.MaximumValue is not null && value > threshold.MaximumValue.Value)
        {
            var denominator = Math.Max(100.0 - threshold.MaximumValue.Value, 1.0);
            var penalty = ((value - threshold.MaximumValue.Value) / denominator) * penaltyWeight;
            component -= penalty;
            failures.Add($"{roleName}.{statName} above maximum ({value} > {threshold.MaximumValue.Value:0.###})");
        }

        if (threshold.OptimalMin is not null && threshold.OptimalMax is not null)
        {
            var optimalMin = Math.Min(threshold.OptimalMin.Value, threshold.OptimalMax.Value);
            var optimalMax = Math.Max(threshold.OptimalMin.Value, threshold.OptimalMax.Value);
            if (value >= optimalMin && value <= optimalMax)
            {
                var center = (optimalMin + optimalMax) / 2.0;
                var halfRange = Math.Max((optimalMax - optimalMin) / 2.0, 1.0);
                var centrality = 1.0 - (Math.Abs(value - center) / halfRange);
                var bonus = Math.Max(centrality, 0.0) * (0.20 * Math.Max(penaltyWeight, 0.25));
                component += bonus;
                roleExplanations.Add($"{roleName}.{statName} in optimal range ({optimalMin:0.###}-{optimalMax:0.###}).");
            }
        }

        return Clamp01(component);
    }

    private static double CombineRoleScores(
        IReadOnlyDictionary<string, double> roleScores,
        IReadOnlyDictionary<string, double> roleWeights)
    {
        if (roleScores.Count == 0)
        {
            return 0.5;
        }

        double weightedTotal = 0;
        double totalWeight = 0;
        foreach (var (roleName, score) in roleScores)
        {
            var roleWeight = roleWeights.TryGetValue(roleName, out var configuredWeight)
                ? Math.Max(configuredWeight, 0.0)
                : 1.0;

            weightedTotal += score * roleWeight;
            totalWeight += roleWeight;
        }

        if (totalWeight <= 0)
        {
            return 0.5;
        }

        return weightedTotal / totalWeight;
    }

    private static double ApplyScenarioModifiers(
        double currentScore,
        IReadOnlyDictionary<string, double> roleScores,
        IReadOnlyList<ScenarioModifierRule> modifiers,
        List<string> failures)
    {
        var adjusted = currentScore;

        foreach (var modifier in modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier.Type))
            {
                continue;
            }

            if (modifier.Type.Equals("ScoreMultiplier", StringComparison.OrdinalIgnoreCase))
            {
                adjusted *= modifier.Value;
                continue;
            }

            if (modifier.Type.Equals("MinimumRequiredRoleScore", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(modifier.RoleName)
                && roleScores.TryGetValue(modifier.RoleName, out var roleScore)
                && roleScore < modifier.Value)
            {
                failures.Add($"{modifier.RoleName} role score below required minimum ({roleScore:0.###} < {modifier.Value:0.###})");
                adjusted *= 0.7;
            }
        }

        return Clamp01(adjusted);
    }

    private static int ReadStat(CharacterStatProfileV2 profile, string statName)
        => CharacterStatProfileV2Accessor.GetStatOrDefault(profile, statName, AdaptiveStatCatalog.DefaultValue);

    private static double Clamp01(double value)
        => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);
}
