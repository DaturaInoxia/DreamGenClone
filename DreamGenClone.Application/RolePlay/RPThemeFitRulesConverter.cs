using System.Text.Json;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public static class RPThemeFitRulesConverter
{
    public static string BuildScenarioFitRulesJson(
        RPTheme theme,
        IReadOnlyDictionary<string, string>? roleCharacterBindings = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (theme.FitRules.Count == 0)
        {
            return string.Empty;
        }

        var rules = new ScenarioFitRules();

        foreach (var fitRule in theme.FitRules)
        {
            var roleName = CharacterRoleCatalog.Normalize(fitRule.RoleName);
            if (string.IsNullOrWhiteSpace(roleName)
                || string.Equals(roleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var roleRule = new CharacterRoleRule
            {
                RoleName = roleName
            };

            foreach (var clause in fitRule.Clauses)
            {
                var statName = (clause.StatName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(statName))
                {
                    continue;
                }

                var threshold = ConvertClauseToThreshold(clause.Comparator, clause.Threshold, clause.PenaltyWeight);
                if (threshold is null)
                {
                    continue;
                }

                roleRule.StatThresholds[statName] = threshold;
            }

            if (roleRule.StatThresholds.Count == 0)
            {
                continue;
            }

            rules.CharacterRoleRules.Add(roleRule);
            rules.CharacterRoleWeights[roleName] = Math.Clamp(fitRule.RoleWeight, 0.1, 10.0);
        }

        if (rules.CharacterRoleRules.Count == 0)
        {
            return string.Empty;
        }

        if (roleCharacterBindings is not null)
        {
            foreach (var roleRule in rules.CharacterRoleRules)
            {
                if (roleCharacterBindings.TryGetValue(roleRule.RoleName, out var characterId)
                    && !string.IsNullOrWhiteSpace(characterId))
                {
                    rules.RoleCharacterBindings[roleRule.RoleName] = characterId.Trim();
                }
            }
        }

        return JsonSerializer.Serialize(rules);
    }

    private static StatThresholdSpecification? ConvertClauseToThreshold(
        string? comparator,
        double threshold,
        double penaltyWeight)
    {
        var normalizedComparator = (comparator ?? ">=").Trim();
        var clampedThreshold = Math.Clamp(threshold, 0.0, 100.0);
        var clampedPenalty = Math.Clamp(penaltyWeight, 0.0, 10.0);

        return normalizedComparator switch
        {
            ">=" => new StatThresholdSpecification
            {
                MinimumValue = clampedThreshold,
                PenaltyWeight = clampedPenalty
            },
            ">" => new StatThresholdSpecification
            {
                MinimumValue = Math.Min(100.0, clampedThreshold + 1.0),
                PenaltyWeight = clampedPenalty
            },
            "<=" => new StatThresholdSpecification
            {
                MaximumValue = clampedThreshold,
                PenaltyWeight = clampedPenalty
            },
            "<" => new StatThresholdSpecification
            {
                MaximumValue = Math.Max(0.0, clampedThreshold - 1.0),
                PenaltyWeight = clampedPenalty
            },
            "=" or "==" => new StatThresholdSpecification
            {
                MinimumValue = clampedThreshold,
                MaximumValue = clampedThreshold,
                PenaltyWeight = clampedPenalty
            },
            _ => null
        };
    }
}
