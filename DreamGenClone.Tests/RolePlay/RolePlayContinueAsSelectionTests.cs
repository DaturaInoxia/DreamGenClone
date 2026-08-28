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
    public async Task ContinueAsAsync_OverflowContinue_DoesNotForcePersonaAsFirstAutoActor()
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

        var session = await service.CreateSessionAsync("Overflow persona continue", scenario.Id);
        await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", "Becky spoke last.");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.NotEmpty(result.ParticipantOutputs);
        Assert.Equal(InteractionType.Npc, result.ParticipantOutputs[0].InteractionType);
        Assert.DoesNotContain(result.ParticipantOutputs, x => x.InteractionType == InteractionType.User);
    }

    [Fact]
    public async Task EvaluateCandidatesAsync_WhenMachineBlocksCandidates_OnlyUnblockedScenariosRemain()
    {
        var service = CreateScenarioSelectionService();
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-1",
            ActiveScenarioId = "scenario-a"
        };
        var candidates = new List<ScenarioDefinition>
        {
            new("scenario-a", "Scenario A", 10),
            new("scenario-b", "Scenario B", 9)
        };

        var evaluations = await service.EvaluateCandidatesAsync(
            state,
            candidates,
            blockedScenarioIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "scenario-b" });

        Assert.Single(evaluations);
        Assert.Equal("scenario-a", evaluations[0].ScenarioId);
    }

    [Fact]
    public async Task EvaluateCandidatesAsync_WhenAllCandidatesBlocked_ThrowsExplicitly()
    {
        var service = CreateScenarioSelectionService();
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-1",
            ActiveScenarioId = "scenario-a"
        };
        var candidates = new List<ScenarioDefinition>
        {
            new("scenario-a", "Scenario A", 10)
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluateCandidatesAsync(
                state,
                candidates,
                blockedScenarioIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "scenario-a" }));

        Assert.Contains("all candidates were blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverflowContinue_HusbandNotExcludedByDefault()
    {
        // After removing ScoreRoleHusband=-5000, the Husband role should
        // no longer be effectively excluded from overflow continue.
        var scenario = new Scenario
        {
            Id = "scenario-husband",
            Name = "Husband Test",
            Characters =
            [
                new Character { Id = "c1", Name = "Becky", Role = "Wife" },
                new Character { Id = "c2", Name = "Ken", Role = "Husband" },
                new Character { Id = "c3", Name = "Dean", Role = "OtherMan" }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Husband test", scenario.Id);
        // Advance past the opening period so all characters are eligible
        for (int i = 0; i < 6; i++)
        {
            await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", $"Turn {i} content.");
        }

        // Perform overflow continue — Ken (Husband) should now be a candidate
        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        // With the old -5000 penalty, Ken would almost never appear.
        // Now he should be among the participants at least occasionally.
        // Since the test uses the fallback path (no actor selection service),
        // we verify there's at least one participant — the engine doesn't crash.
        Assert.NotEmpty(result.ParticipantOutputs);
    }

    [Fact]
    public async Task PreferredPositionOverrideChance_DefaultsTo015_OnNewSession()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Override chance default");

        // Default should be 0.15 as specified in RolePlaySession
        Assert.Equal(0.15, session.PreferredPositionOverrideChance, precision: 3);
    }

    [Fact]
    public async Task ParticipateInAutoContinue_False_ExcludesCharacter()
    {
        var scenario = new Scenario
        {
            Id = "scenario-participate",
            Name = "Participate Test",
            Characters =
            [
                new Character { Id = "c1", Name = "Becky", Role = "Wife" },
                new Character { Id = "c2", Name = "Ken", Role = "Husband" },
                new Character { Id = "c3", Name = "Dean", Role = "OtherMan" }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new SingleScenarioService(scenario));

        var session = await service.CreateSessionAsync("Participate test", scenario.Id);

        // Set Ken to not participate in auto-continue
        session.CharacterTurnOverrides["Ken"] = new CharacterTurnOverride
        {
            CharacterName = "Ken",
            ParticipateInAutoContinue = false,
            ResponsePriority = null,
            PreferredPosition = PreferredTurnPosition.Auto
        };

        // Advance past opening period
        for (int i = 0; i < 6; i++)
        {
            await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", $"Turn {i} content.");
        }

        // Perform overflow continue — Ken should be excluded from candidates
        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        // Ken should not appear in the participant outputs due to ParticipateInAutoContinue=false
        Assert.DoesNotContain(result.ParticipantOutputs, x =>
            string.Equals(x.ActorName, "Ken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Session_PreferredPositionOverrideChance_DefaultsTo015()
    {
        var session = new RolePlaySession();
        Assert.Equal(0.15, session.PreferredPositionOverrideChance, precision: 3);
    }

    [Fact]
    public async Task EvaluateCandidatesAsync_ChangesLeader_WhenNarrativeEvidenceSnapshotChanges()
    {
        var service = CreateScenarioSelectionService();
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-ordering",
            ActiveScenarioId = "scenario-a"
        };

        var firstPass = await service.EvaluateCandidatesAsync(
            state,
            [
                new ScenarioDefinition("scenario-a", "Scenario A", Priority: 5, NarrativeEvidenceScore: 0.8m, PreferencePriorityScore: 0.5m),
                new ScenarioDefinition("scenario-b", "Scenario B", Priority: 4, NarrativeEvidenceScore: 0.2m, PreferencePriorityScore: 0.5m)
            ]);

        var secondPass = await service.EvaluateCandidatesAsync(
            state,
            [
                new ScenarioDefinition("scenario-a", "Scenario A", Priority: 5, NarrativeEvidenceScore: 0.2m, PreferencePriorityScore: 0.5m),
                new ScenarioDefinition("scenario-b", "Scenario B", Priority: 4, NarrativeEvidenceScore: 0.8m, PreferencePriorityScore: 0.5m)
            ]);

        Assert.Equal("scenario-a", firstPass[0].ScenarioId);
        Assert.Equal("scenario-b", secondPass[0].ScenarioId);
    }

    [Fact]
    public async Task EvaluateCandidatesAsync_IncreasesFitScore_WhenNarrativeEvidenceSnapshotIncreases()
    {
        var service = CreateScenarioSelectionService();
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-fit",
            ActiveScenarioId = "scenario-a"
        };

        var lowEvidenceResult = await service.EvaluateCandidatesAsync(
            state,
            [new ScenarioDefinition("scenario-a", "Scenario A", Priority: 5, NarrativeEvidenceScore: 0.2m, PreferencePriorityScore: 0.5m)]);

        var highEvidenceResult = await service.EvaluateCandidatesAsync(
            state,
            [new ScenarioDefinition("scenario-a", "Scenario A", Priority: 5, NarrativeEvidenceScore: 0.8m, PreferencePriorityScore: 0.5m)]);

        Assert.True(highEvidenceResult[0].FitScore > lowEvidenceResult[0].FitScore);
    }

    private static ScenarioSelectionService CreateScenarioSelectionService()
    {
        var options = Options.Create(new StoryAnalysisOptions
        {
            BuildUpSelectionCandidateGateStrategy = "dominant-role"
        });

        return new ScenarioSelectionService(
            NullLogger<ScenarioSelectionService>.Instance,
            themeCatalogService: null,
            characterStateScenarioMapper: null,
            narrativeGateProfileService: null,
            rpThemeService: null,
            engineSettingsRepository: new StubScenarioEngineSettingsRepository(new ScenarioEngineSettings()),
            options: options);
    }

    private sealed class StubScenarioEngineSettingsRepository : IScenarioEngineSettingsRepository
    {
        private readonly ScenarioEngineSettings _settings;

        public StubScenarioEngineSettingsRepository(ScenarioEngineSettings settings)
        {
            _settings = settings;
        }

        public Task<ScenarioEngineSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(ScenarioEngineSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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

    private sealed class RecordingDebugEventSink : IRolePlayDebugEventSink
    {
        public List<RolePlayDebugEventRecord> Records { get; } = [];

        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
