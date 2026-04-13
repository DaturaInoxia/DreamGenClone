using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterStateScenarioMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task EvaluateAllScenariosAsync_DefaultsToNeutral_WhenRulesMissing()
    {
        var mapper = new CharacterStateScenarioMapper(NullLogger<CharacterStateScenarioMapper>.Instance);
        var state = new AdaptiveScenarioState { SessionId = "s1" };

        var result = await mapper.EvaluateAllScenariosAsync(state,
        [
            new ThemeCatalogEntry { Id = "dominance", ScenarioFitRules = string.Empty }
        ]);

        Assert.True(result.TryGetValue("dominance", out var fit));
        Assert.NotNull(fit);
        Assert.Equal(0.5, fit.FitScore, 3);
    }

    [Fact]
    public async Task EvaluateAllScenariosAsync_AppliesThresholdsAndWeights()
    {
        var mapper = new CharacterStateScenarioMapper(NullLogger<CharacterStateScenarioMapper>.Instance);

        var state = new AdaptiveScenarioState
        {
            SessionId = "s1",
            CharacterSnapshots =
            [
                new CharacterStatProfileV2 { CharacterId = "alpha", Dominance = 88, Desire = 70, Tension = 60 },
                new CharacterStatProfileV2 { CharacterId = "beta", Desire = 75, Tension = 65, Restraint = 40 }
            ]
        };

        var rules = new ScenarioFitRules
        {
            RoleCharacterBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dominant"] = "alpha",
                ["submissive"] = "beta"
            },
            CharacterRoleRules =
            [
                new CharacterRoleRule
                {
                    RoleName = "dominant",
                    StatThresholds = new Dictionary<string, StatThresholdSpecification>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["dominance"] = new StatThresholdSpecification { MinimumValue = 65, OptimalMin = 75, OptimalMax = 95 },
                        ["desire"] = new StatThresholdSpecification { MinimumValue = 55 }
                    }
                },
                new CharacterRoleRule
                {
                    RoleName = "submissive",
                    StatThresholds = new Dictionary<string, StatThresholdSpecification>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["desire"] = new StatThresholdSpecification { MinimumValue = 55, OptimalMin = 65, OptimalMax = 90 },
                        ["tension"] = new StatThresholdSpecification { MinimumValue = 45 }
                    }
                }
            ],
            CharacterRoleWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dominant"] = 0.6,
                ["submissive"] = 0.4
            }
        };

        var result = await mapper.EvaluateAllScenariosAsync(state,
        [
            new ThemeCatalogEntry
            {
                Id = "dominance",
                ScenarioFitRules = JsonSerializer.Serialize(rules, JsonOptions)
            }
        ]);

        Assert.True(result.TryGetValue("dominance", out var fit));
        Assert.NotNull(fit);
        Assert.True(fit.FitScore > 0.8, $"Expected strong fit but got {fit.FitScore:0.###}");
        Assert.Empty(fit.Failures);
    }
}
