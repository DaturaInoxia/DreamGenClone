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
