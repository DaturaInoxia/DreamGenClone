using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using System.Reflection;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySessionBaseStatInitializationTests
{
    [Fact]
    public async Task CreateSessionAsync_AppliesProfileDefaultsThenCharacterOverrides()
    {
        var characterProfiles = new RolePlayTestFactory.FakeCharacterProfileService();
        var profile = characterProfiles.Add(
            "Test Profile",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 30,
                ["Restraint"] = 60
            });

        var scenario = new Scenario
        {
            Id = "scenario-1",
            BaseStatProfileId = profile.Id,
            Characters =
            [
                new Character
                {
                    Id = "char-1",
                    Name = "Alice",
                    BaseStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Desire"] = 80
                    }
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario),
            characterProfileService: characterProfiles);

        var session = await service.CreateSessionAsync("Base Stats", scenario.Id);

        var aliceProfile = session.AdaptiveState.CharacterStats["Alice"];
        Assert.Equal(80, aliceProfile.Desire);
        Assert.Equal(60, aliceProfile.Restraint);
    }

    [Fact]
    public async Task CreateSessionAsync_MissingProfile_FallsBackToScenarioResolvedStats()
    {
        var scenario = new Scenario
        {
            Id = "scenario-2",
            BaseStatProfileId = "missing-profile",
            ResolvedBaseStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 40
            },
            Characters =
            [
                new Character
                {
                    Id = "char-1",
                    Name = "Alice"
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario),
            characterProfileService: new RolePlayTestFactory.FakeCharacterProfileService());

        var session = await service.CreateSessionAsync("Fallback", scenario.Id);

        var profile = session.AdaptiveState.CharacterStats["Alice"];
        Assert.Equal(40, profile.Desire);
    }

    [Fact]
    public async Task CreateSessionAsync_WithoutProfile_PreservesCharacterOnlyStats()
    {
        var scenario = new Scenario
        {
            Id = "scenario-3",
            Characters =
            [
                new Character
                {
                    Id = "char-1",
                    Name = "Alice",
                    BaseStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Desire"] = 22
                    }
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario),
            characterProfileService: new RolePlayTestFactory.FakeCharacterProfileService());

        var session = await service.CreateSessionAsync("Character Only", scenario.Id);

        var profile = session.AdaptiveState.CharacterStats["Alice"];
        Assert.Equal(22, profile.Desire);
    }

    [Fact]
    public void SyncSessionAdaptiveStateFromV2_UsesExistingNameKey_ForMatchingCharacterId()
    {
        var session = new RolePlaySession();
        session.CharacterPerspectives.Add(new RolePlayCharacterPerspective
        {
            CharacterId = "char-1",
            CharacterName = "Becky"
        });

        var v2State = new AdaptiveScenarioState
        {
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-1",
                    Desire = 73,
                    Restraint = 85,
                    Dominance = 90,
                    Loyalty = 15,
                    SelfRespect = 90,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 23, ["Connection"] = 85 }
                }
            ]
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "SyncSessionAdaptiveStateFromV2",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [session, v2State]);

        Assert.True(session.AdaptiveState.CharacterStats.ContainsKey("char-1"));
        Assert.False(session.AdaptiveState.CharacterStats.ContainsKey("Becky"));

        var syncedProfile = session.AdaptiveState.CharacterStats["char-1"];
        Assert.Equal(73, syncedProfile.Desire);
        Assert.Equal(85, syncedProfile.Restraint);
        Assert.Equal(23, syncedProfile.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50);
        Assert.Equal(85, syncedProfile.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50);
        Assert.Equal(90, syncedProfile.Dominance);
        Assert.Equal(15, syncedProfile.Loyalty);
        Assert.Equal(90, syncedProfile.SelfRespect);
    }

    private sealed class SingleScenarioService : IScenarioService
    {
        private readonly Scenario _scenario;

        public SingleScenarioService(Scenario scenario)
        {
            _scenario = scenario;
        }

        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotImplementedException();

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult(string.Equals(id, _scenario.Id, StringComparison.OrdinalIgnoreCase) ? _scenario : null);

        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(new List<Scenario> { _scenario });

        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => Task.FromResult(scenario);

        public Task<bool> DeleteScenarioAsync(string id) => Task.FromResult(false);

        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
