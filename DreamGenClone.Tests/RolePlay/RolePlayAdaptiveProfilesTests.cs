using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAdaptiveProfilesTests
{
    [Fact]
    public void ResolveEffectiveStyle_WhenManualTonePinEnabled_SuppressesAdaptiveDeltas()
    {
        var session = new RolePlaySession
        {
            IsIntensityManuallyPinned = true,
            IntensityFloorOverride = "(None)",
            IntensityCeilingOverride = "(None)"
        };

        session.Interactions.AddRange(
        [
            new RolePlayInteraction { Content = "1" },
            new RolePlayInteraction { Content = "2" },
            new RolePlayInteraction { Content = "3" },
            new RolePlayInteraction { Content = "4" },
            new RolePlayInteraction { Content = "5" },
            new RolePlayInteraction { Content = "6" },
            new RolePlayInteraction { Content = "7" },
            new RolePlayInteraction { Content = "8" },
            new RolePlayInteraction { Content = "9" },
            new RolePlayInteraction { Content = "10" },
            new RolePlayInteraction { Content = "11" },
            new RolePlayInteraction { Content = "12" },
            new RolePlayInteraction { Content = "13" },
            new RolePlayInteraction { Content = "14" }
        ]);

        session.AdaptiveState.CharacterStats["NPC"] = new CharacterStatBlock
        {
            Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 95
            }
        };
        session.AdaptiveState.ThemeTracker.PrimaryThemeId = "dominance";

        var (label, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.Emotional);

        Assert.Equal("Emotional", label);
        Assert.Contains("manual-pin=on", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("desire=", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("progression=", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("theme=", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveEffectiveStyle_WhenBoundsInverted_NormalizesAndClamps()
    {
        var session = new RolePlaySession
        {
            IntensityFloorOverride = "Erotic",
            IntensityCeilingOverride = "Emotional"
        };

        var (label, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12);

        Assert.Equal("Erotic", label);
        Assert.Contains("bounds=normalized", reason, StringComparison.Ordinal);
        Assert.Contains("floor=Erotic", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSessionAsync_WhenScenarioHasStyleProfile_SeedsSessionStyleProfile()
    {
        var scenario = new Scenario
        {
            Id = "scenario-1",
            Name = "Scenario",
            DefaultThemeProfileId = "ranking-default",
            DefaultIntensityProfileId = "tone-default",
            DefaultSteeringProfileId = "style-default",
            DefaultIntensityFloor = "Suggestive",
            DefaultIntensityCeiling = "Erotic"
        };

        var service = RolePlayTestFactory.CreateEngineService(scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Seed Test", scenario.Id);

        Assert.Equal("style-default", session.SelectedSteeringProfileId);
        Assert.Equal("ranking-default", session.SelectedThemeProfileId);
        Assert.Equal("tone-default", session.SelectedIntensityProfileId);
    }

    private sealed class SingleScenarioService(Scenario scenario) : IScenarioService
    {
        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => Task.FromResult(scenario);

        public Task<Scenario?> GetScenarioAsync(string id)
        {
            return Task.FromResult(string.Equals(id, scenario.Id, StringComparison.Ordinal)
                ? scenario
                : null);
        }

        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(new List<Scenario> { scenario });

        public Task<Scenario> SaveScenarioAsync(Scenario scenarioToSave) => Task.FromResult(scenarioToSave);

        public Task<bool> DeleteScenarioAsync(string id) => Task.FromResult(false);

        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
