using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class PersonaInteractionSelectionTests
{
    [Fact]
    public void BehaviorModeService_Spectate_AllowsExplicitPersonaOnly()
    {
        var service = new BehaviorModeService(Microsoft.Extensions.Logging.Abstractions.NullLogger<BehaviorModeService>.Instance);

        Assert.False(service.IsContinuationAllowed(BehaviorMode.Spectate, ContinueAsActor.You, explicitSelection: false));
        Assert.True(service.IsContinuationAllowed(BehaviorMode.Spectate, ContinueAsActor.You, explicitSelection: true));
    }

    [Fact]
    public async Task ContinueAsAsync_Overflow_DoesNotForcePersonaEachTurn()
    {
        var continuation = new CapturingContinuationService();
        var service = RolePlayTestFactory.CreateEngineService(
            continuationService: continuation,
            scenarioService: new SingleScenarioService(new Scenario
            {
                Id = "sc1",
                Characters =
                [
                    new Character { Id = "c1", Name = "Aria" },
                    new Character { Id = "c2", Name = "Bruno" }
                ]
            }));

        var created = await service.CreateSessionAsync("Persona Overflow", scenarioId: "sc1", personaName: "Alex");
        created.AutoNarrative = false;
        created.SceneContinueBatchSize = 2;
        await service.SaveSessionAsync(created);

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.ParticipantOutputs.Count);
        Assert.DoesNotContain(result.ParticipantOutputs, x => x.InteractionType == InteractionType.User);
    }

    [Fact]
    public async Task ContinueAsAsync_Overflow_CanIncludePersonaOccasionally_OutOfScene()
    {
        var continuation = new CapturingContinuationService();
        var service = RolePlayTestFactory.CreateEngineService(
            continuationService: continuation,
            scenarioService: new SingleScenarioService(new Scenario
            {
                Id = "sc1",
                Characters =
                [
                    new Character { Id = "c1", Name = "Aria" },
                    new Character { Id = "c2", Name = "Bruno" }
                ]
            }));

        var created = await service.CreateSessionAsync("Persona Overflow Occasional", scenarioId: "sc1", personaName: "Alex");
        created.AutoNarrative = false;
        created.SceneContinueBatchSize = 3;
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Npc, ActorName = "Aria", Content = "..." });
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Custom, ActorName = "Bruno", Content = "..." });
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Npc, ActorName = "Aria", Content = "..." });
        await service.SaveSessionAsync(created);

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.Contains(result.ParticipantOutputs, x => x.InteractionType == InteractionType.User);
    }

    [Fact]
    public async Task ContinueAsAsync_Overflow_BatchSizeOne_CanStillSelectPersonaWhenEligible()
    {
        var continuation = new CapturingContinuationService();
        var service = RolePlayTestFactory.CreateEngineService(
            continuationService: continuation,
            scenarioService: new SingleScenarioService(new Scenario
            {
                Id = "sc1",
                Characters =
                [
                    new Character { Id = "c1", Name = "Aria" },
                    new Character { Id = "c2", Name = "Bruno" }
                ]
            }));

        var created = await service.CreateSessionAsync("Persona Batch1", scenarioId: "sc1", personaName: "Alex");
        created.AutoNarrative = false;
        created.SceneContinueBatchSize = 1;
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Npc, ActorName = "Aria", Content = "..." });
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Custom, ActorName = "Bruno", Content = "..." });
        created.Interactions.Add(new RolePlayInteraction { InteractionType = InteractionType.Npc, ActorName = "Aria", Content = "..." });
        await service.SaveSessionAsync(created);

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.Single(result.ParticipantOutputs);
        Assert.Equal(InteractionType.User, result.ParticipantOutputs[0].InteractionType);
    }

    private sealed class CapturingContinuationService : IRolePlayContinuationService
    {
        public Task<RolePlayInteraction> ContinueAsync(
            RolePlaySession session,
            ContinueAsActor actor,
            string? customActorName,
            PromptIntent intent,
            string promptText,
            Func<string, Task>? onChunk = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = actor switch
                {
                    ContinueAsActor.You => InteractionType.User,
                    ContinueAsActor.Npc => InteractionType.Npc,
                    ContinueAsActor.Custom => InteractionType.Custom,
                    _ => InteractionType.System
                },
                ActorName = customActorName ?? actor.ToString(),
                Content = promptText
            });
        }

        public Task<ContinueAsResult> ContinueBatchAsync(
            RolePlaySession session,
            IReadOnlyList<ContinueAsActor> actors,
            bool includeNarrative,
            string? customActorName,
            string promptText,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class SingleScenarioService : DreamGenClone.Web.Application.Scenarios.IScenarioService
    {
        private readonly Scenario _scenario;

        public SingleScenarioService(Scenario scenario)
        {
            _scenario = scenario;
        }

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult(string.Equals(id, _scenario.Id, StringComparison.OrdinalIgnoreCase) ? _scenario : null);

        public Task<Scenario> CreateScenarioAsync(string name, string? description = null)
            => throw new NotImplementedException();

        public Task<List<Scenario>> GetAllScenariosAsync()
            => Task.FromResult<List<Scenario>>([_scenario]);

        public Task<Scenario> SaveScenarioAsync(Scenario scenario)
            => Task.FromResult(scenario);

        public Task<bool> DeleteScenarioAsync(string id)
            => Task.FromResult(false);

        public Task<Scenario> CloneScenarioAsync(string id, string newName)
            => throw new NotImplementedException();
    }
}
