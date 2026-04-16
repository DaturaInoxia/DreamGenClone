using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using System.Reflection;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySessionBaseStatInitializationTests
{
    [Fact]
    public async Task CreateSessionAsync_AppliesProfileDefaultsThenCharacterOverrides()
    {
        var baseStatProfiles = new RolePlayTestFactory.FakeBaseStatProfileService();
        var profile = await baseStatProfiles.CreateAsync(
            "Test Profile",
            "Defaults",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Connection"] = 30,
                ["Tension"] = 60
            },
            CharacterGenderCatalog.Female,
            CharacterRoleCatalog.Wife);

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
                        ["Connection"] = 80
                    }
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario),
            baseStatProfileService: baseStatProfiles);

        var session = await service.CreateSessionAsync("Base Stats", scenario.Id);

        var stats = session.AdaptiveState.CharacterStats["Alice"].Stats;
        Assert.Equal(80, stats["Connection"]);
        Assert.Equal(60, stats["Tension"]);
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
            baseStatProfileService: new RolePlayTestFactory.FakeBaseStatProfileService());

        var session = await service.CreateSessionAsync("Fallback", scenario.Id);

        var stats = session.AdaptiveState.CharacterStats["Alice"].Stats;
        Assert.Equal(40, stats["Desire"]);
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
                        ["Jealousy"] = 22
                    }
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario),
            baseStatProfileService: new RolePlayTestFactory.FakeBaseStatProfileService());

        var session = await service.CreateSessionAsync("Character Only", scenario.Id);

        var stats = session.AdaptiveState.CharacterStats["Alice"].Stats;
        Assert.Single(stats);
        Assert.Equal(22, stats["Tension"]);
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
        session.AdaptiveState.CharacterStats["Becky"] = new CharacterStatBlock
        {
            CharacterId = "char-1",
            Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 50,
                ["Restraint"] = 50,
                ["Tension"] = 50,
                ["Connection"] = 50,
                ["Dominance"] = 50,
                ["Loyalty"] = 50,
                ["SelfRespect"] = 50
            }
        };

        var v2State = new AdaptiveScenarioState
        {
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-1",
                    Desire = 73,
                    Restraint = 85,
                    Tension = 23,
                    Connection = 85,
                    Dominance = 90,
                    Loyalty = 15,
                    SelfRespect = 90
                }
            ]
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "SyncSessionAdaptiveStateFromV2",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [session, v2State]);

        Assert.True(session.AdaptiveState.CharacterStats.ContainsKey("Becky"));
        Assert.False(session.AdaptiveState.CharacterStats.ContainsKey("char-1"));

        var stats = session.AdaptiveState.CharacterStats["Becky"].Stats;
        Assert.Equal(73, stats["Desire"]);
        Assert.Equal(85, stats["Restraint"]);
        Assert.Equal(23, stats["Tension"]);
        Assert.Equal(85, stats["Connection"]);
        Assert.Equal(90, stats["Dominance"]);
        Assert.Equal(15, stats["Loyalty"]);
        Assert.Equal(90, stats["SelfRespect"]);
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
