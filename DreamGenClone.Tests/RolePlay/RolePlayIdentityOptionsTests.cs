using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayIdentityOptionsTests
{
    [Fact]
    public async Task GetIdentityOptions_NoScenario_ReturnsPersonaAndCustomOnly()
    {
        var session = new RolePlaySession { ScenarioId = null, PersonaName = "Alex" };
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        Assert.Contains(options, o => o.SourceType == IdentityOptionSource.Persona && o.DisplayName == "Alex");
        Assert.Contains(options, o => o.SourceType == IdentityOptionSource.CustomCharacter);
        Assert.DoesNotContain(options, o => o.SourceType == IdentityOptionSource.SceneCharacter);
    }

    [Fact]
    public async Task GetIdentityOptions_WithScenario_IncludesSceneCharacters()
    {
        var scenario = new Scenario
        {
            Id = "sc1",
            Characters =
            [
                new Character { Id = "c1", Name = "Aria" },
                new Character { Id = "c2", Name = "Bruno" }
            ]
        };

        var session = new RolePlaySession { ScenarioId = "sc1" };
        var service = CreateService(scenario);

        var options = await service.GetIdentityOptionsAsync(session);

        Assert.Contains(options, o => o.SourceType == IdentityOptionSource.SceneCharacter && o.DisplayName == "Aria");
        Assert.Contains(options, o => o.SourceType == IdentityOptionSource.SceneCharacter && o.DisplayName == "Bruno");
    }

    [Fact]
    public async Task GetIdentityOptions_SceneCharacters_HaveNpcActor()
    {
        var scenario = new Scenario
        {
            Id = "sc1",
            Characters = [new Character { Id = "c1", Name = "Quinn" }]
        };

        var session = new RolePlaySession { ScenarioId = "sc1" };
        var service = CreateService(scenario);

        var options = await service.GetIdentityOptionsAsync(session);

        var sceneChar = options.Single(o => o.SourceType == IdentityOptionSource.SceneCharacter);
        Assert.Equal(ContinueAsActor.Npc, sceneChar.Actor);
    }

    [Fact]
    public async Task GetIdentityOptions_PersonaOption_HasYouActor()
    {
        var session = new RolePlaySession { PersonaName = "Morgan" };
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        var persona = options.Single(o => o.SourceType == IdentityOptionSource.Persona);
        Assert.Equal(ContinueAsActor.You, persona.Actor);
        Assert.Equal("Morgan", persona.DisplayName);
    }

    [Fact]
    public async Task GetIdentityOptions_NpcOnlyMode_SceneCharactersAvailable_PersonaNotAvailable()
    {
        var scenario = new Scenario
        {
            Id = "sc1",
            Characters = [new Character { Id = "c1", Name = "Sera" }]
        };

        var session = new RolePlaySession { ScenarioId = "sc1", BehaviorMode = BehaviorMode.NpcOnly };
        var service = CreateService(scenario);

        var options = await service.GetIdentityOptionsAsync(session);

        var sceneChar = options.Single(o => o.SourceType == IdentityOptionSource.SceneCharacter);
        var persona = options.Single(o => o.SourceType == IdentityOptionSource.Persona);

        Assert.True(sceneChar.IsAvailable);
        Assert.False(persona.IsAvailable);
    }

    [Fact]
    public async Task GetIdentityOptions_EmptyCharacterName_IsSkipped()
    {
        var scenario = new Scenario
        {
            Id = "sc1",
            Characters =
            [
                new Character { Id = "c1", Name = "  " },
                new Character { Id = "c2", Name = "Zara" }
            ]
        };

        var session = new RolePlaySession { ScenarioId = "sc1" };
        var service = CreateService(scenario);

        var options = await service.GetIdentityOptionsAsync(session);

        var sceneChars = options.Where(o => o.SourceType == IdentityOptionSource.SceneCharacter).ToList();
        Assert.Single(sceneChars);
        Assert.Equal("Zara", sceneChars[0].DisplayName);
    }

    private static RolePlayIdentityOptionsService CreateService(Scenario? scenario = null)
    {
        var scenarioService = new FakeScenarioService(scenario);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        return new RolePlayIdentityOptionsService(scenarioService, behaviorMode);
    }

    private sealed class FakeScenarioService : IScenarioService
    {
        private readonly Scenario? _scenario;

        public FakeScenarioService(Scenario? scenario) => _scenario = scenario;

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult(_scenario?.Id == id ? _scenario : null);

        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotImplementedException();
        public Task<List<Scenario>> GetAllScenariosAsync() => throw new NotImplementedException();
        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id) => throw new NotImplementedException();
        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
