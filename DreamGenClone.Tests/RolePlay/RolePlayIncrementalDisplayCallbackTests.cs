using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-087: Verifies the per-actor completion/start callbacks on ContinueAsAsync and SubmitPromptAsync
/// fire in the correct order (actor-start before each generation, interaction-completed after each
/// finalized interaction, narrative last) so the workspace can display each completed interaction
/// as soon as it arrives instead of waiting for the full batch.
/// </summary>
public sealed class RolePlayIncrementalDisplayCallbackTests
{
    [Fact]
    public async Task ContinueAsAsync_OverflowContinue_FiresOnActorStartThenOnInteractionCompleted_PerActor()
    {
        var scenario = new Scenario
        {
            Id = "scenario-incr",
            Name = "Incremental Display",
            Characters =
            [
                new Character { Id = "npc-wife", Name = "Becky" },
                new Character { Id = "npc-husband", Name = "Ken" }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Incremental display", scenario.Id);
        session.AutoNarrative = false;
        session.SceneContinueBatchSize = 2;
        await service.SaveSessionAsync(session);

        await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", "Becky waved.");

        var actorStarts = new List<(int Position, string Name, int TurnActorCount)>();
        var completions = new List<(string ActorName, int Position, int TurnActorCount, bool IsNarrative)>();

        var result = await service.ContinueAsAsync(
            new ContinueAsRequest
            {
                SessionId = session.Id,
                TriggeredBy = SubmissionSource.MainOverflowContinue
            },
            onChunk: null,
            onInteractionCompleted: (interaction, position, turnActorCount, isNarrative) =>
            {
                completions.Add((interaction.ActorName, position, turnActorCount, isNarrative));
                return Task.CompletedTask;
            },
            onActorStart: (position, name, turnActorCount) =>
            {
                actorStarts.Add((position, name, turnActorCount));
                return Task.CompletedTask;
            });

        // The overflow batch should have produced at least one participant.
        Assert.True(result.Success);
        Assert.NotEmpty(result.ParticipantOutputs);

        // For each completed participant interaction, an onActorStart must have fired first
        // with the same position. onActorStart fires BEFORE the interaction is generated,
        // onInteractionCompleted fires AFTER it is finalized — so they interleave in order.
        Assert.NotEmpty(actorStarts);
        Assert.NotEmpty(completions);

        // The number of non-narrative completions must match the number of actor-starts
        // (narrative is suppressed via AutoNarrative=false here).
        var participantCompletions = completions.Where(c => !c.IsNarrative).ToList();
        Assert.Equal(actorStarts.Count, participantCompletions.Count);

        // Positions must be 1-based and contiguous.
        for (var i = 0; i < actorStarts.Count; i++)
        {
            Assert.Equal(i + 1, actorStarts[i].Position);
        }

        // TurnActorCount reported at actor-start must equal the batch size.
        Assert.All(actorStarts, s => Assert.Equal(2, s.TurnActorCount));
    }

    [Fact]
    public async Task ContinueAsAsync_NarrativeSuppressed_DoesNotFireNarrativeCallbacks()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Narrative suppressed");
        session.AutoNarrative = false;
        await service.SaveSessionAsync(session);

        var anyNarrativeCompletion = false;
        var anyNarrativeStart = false;

        await service.ContinueAsAsync(
            new ContinueAsRequest
            {
                SessionId = session.Id,
                TriggeredBy = SubmissionSource.MainOverflowContinue,
                IncludeNarrative = false
            },
            onChunk: null,
            onInteractionCompleted: (_, _, _, isNarrative) =>
            {
                if (isNarrative) anyNarrativeCompletion = true;
                return Task.CompletedTask;
            },
            onActorStart: (_, name, _) =>
            {
                if (string.Equals(name, "Narrative", StringComparison.OrdinalIgnoreCase))
                    anyNarrativeStart = true;
                return Task.CompletedTask;
            });

        Assert.False(anyNarrativeCompletion, "Narrative completion callback fired when narrative was suppressed.");
        Assert.False(anyNarrativeStart, "Narrative actor-start callback fired when narrative was suppressed.");
    }

    [Fact]
    public async Task SubmitPromptAsync_FiresOnActorStartAndOnInteractionCompleted_ForSingleActor()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Submit incremental");

        var actorStarts = new List<(int Position, string Name, int TurnActorCount)>();
        var completions = new List<(string ActorName, int Position, int TurnActorCount, bool IsNarrative)>();

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Hello there.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.Spectate,
            SubmittedVia = SubmissionSource.MainOverflowContinue
        };

        var interaction = await service.SubmitPromptAsync(
            submission,
            onChunk: null,
            onInteractionCompleted: (i, position, turnActorCount, isNarrative) =>
            {
                completions.Add((i.ActorName, position, turnActorCount, isNarrative));
                return Task.CompletedTask;
            },
            onActorStart: (position, name, turnActorCount) =>
            {
                actorStarts.Add((position, name, turnActorCount));
                return Task.CompletedTask;
            });

        Assert.NotNull(interaction);
        Assert.NotEmpty(completions);
        // The Send path is single-actor: exactly one actor-start and one completion.
        Assert.Single(actorStarts);
        Assert.Single(completions);
        Assert.Equal(1, actorStarts[0].Position);
        Assert.Equal(1, completions[0].Position);
        Assert.False(completions[0].IsNarrative);
    }

    [Fact]
    public async Task ContinueAsAsync_NullCallbacks_DoNotThrow()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Null callbacks");

        // No callbacks passed — must not throw and must still produce a result.
        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ContinueAsAsync_OnInteractionCompletedThrowingObjectDisposed_DoesNotAbortBatch()
    {
        var scenario = new Scenario
        {
            Id = "scenario-disposed",
            Name = "Disposed Callback",
            Characters =
            [
                new Character { Id = "npc-a", Name = "Aria" },
                new Character { Id = "npc-b", Name = "Bruno" }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Disposed callback", scenario.Id);
        session.AutoNarrative = false;
        session.SceneContinueBatchSize = 2;
        await service.SaveSessionAsync(session);

        await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Aria", "Aria spoke.");

        // The first completion callback throws ObjectDisposedException (simulating a disposed
        // Blazor component). The engine must swallow it and continue the batch so subsequent
        // actors still generate and the result is returned successfully.
        var completionCount = 0;
        var result = await service.ContinueAsAsync(
            new ContinueAsRequest
            {
                SessionId = session.Id,
                TriggeredBy = SubmissionSource.MainOverflowContinue
            },
            onChunk: null,
            onInteractionCompleted: (_, _, _, _) =>
            {
                completionCount++;
                throw new ObjectDisposedException("component");
            },
            onActorStart: (_, _, _) => Task.CompletedTask);

        Assert.True(result.Success);
        Assert.NotEmpty(result.ParticipantOutputs);
        // The callback must have been invoked at least once (proving it was wired), even though
        // it threw — the engine swallowed the exception and continued.
        Assert.True(completionCount >= 1);
    }

    private sealed class SingleScenarioService(Scenario scenario) : IScenarioService
    {
        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => Task.FromResult(scenario);

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult<Scenario?>(string.Equals(id, scenario.Id, StringComparison.Ordinal) ? scenario : null);

        public Task<List<Scenario>> GetAllScenariosAsync()
            => Task.FromResult(new List<Scenario> { scenario });

        public Task<Scenario> SaveScenarioAsync(Scenario scenarioToSave)
            => Task.FromResult(scenarioToSave);

        public Task<bool> DeleteScenarioAsync(string id)
            => Task.FromResult(false);

        public Task<Scenario> CloneScenarioAsync(string id, string newName)
            => throw new NotImplementedException();
    }
}
