using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinueAsSelectionTests
{
    [Fact]
    public async Task ContinueAsAsync_SelectedIdentityIds_HonorsAvailability()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Continue As");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            SelectedIdentityIds = ["custom:adhoc", "persona:you"],
            IncludeNarrative = false
        });

        Assert.True(result.Success);
        Assert.Collection(
            result.ParticipantOutputs,
            item => Assert.Equal("You", item.ActorName));
    }

    [Fact]
    public async Task ContinueAsAsync_NoSelection_UsesContextDrivenFallback()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Fallback continue");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.Single(result.ParticipantOutputs);
    }

    [Fact]
    public async Task ContinueAsAsync_OverflowContinue_UsesPersonaWhenPersonaIsNextNaturalActor()
    {
        var scenario = new Scenario
        {
            Id = "scenario-1",
            Name = "Overflow Persona",
            Characters =
            [
                new Character { Id = "npc-1", Name = "Becky" },
                new Character { Id = "npc-2", Name = "Ken" }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Overflow persona continue", scenario.Id, personaName: "Pilot");
        await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", "Becky spoke last.");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.NotEmpty(result.ParticipantOutputs);
        Assert.Equal(InteractionType.User, result.ParticipantOutputs[0].InteractionType);
        Assert.Equal("You", result.ParticipantOutputs[0].ActorName);
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
