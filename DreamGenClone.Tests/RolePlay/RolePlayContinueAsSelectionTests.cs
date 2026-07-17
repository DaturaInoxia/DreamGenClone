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
    public async Task AddInteractionAsync_EmitsSemanticDebugTelemetryFields()
    {
        var recordingSink = new RecordingDebugEventSink();
        var adaptiveService = new RolePlayAdaptiveStateService(
            new RolePlayTestFactory.FakeThemeCatalogService(),
            recordingSink,
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var service = RolePlayTestFactory.CreateEngineService(
            adaptiveStateService: adaptiveService,
            debugEventSink: recordingSink);

        var session = await service.CreateSessionAsync("debug semantic telemetry");
        await service.AddInteractionAsync(session.Id, ContinueAsActor.Npc, "Becky", "plain interaction without semantic markers");

        var adaptiveUpdate = Assert.Single(recordingSink.Records.Where(x => string.Equals(x.EventKind, "InteractionAdaptiveStateUpdated", StringComparison.Ordinal)));
        using var metadata = JsonDocument.Parse(adaptiveUpdate.MetadataJson);
        var root = metadata.RootElement;

        Assert.True(root.TryGetProperty("semanticStepSucceeded", out var semanticStep));
        Assert.True(semanticStep.GetBoolean());
        Assert.True(root.TryGetProperty("semanticEvents", out var semanticEvents));
        Assert.Equal(JsonValueKind.Array, semanticEvents.ValueKind);
        Assert.True(root.TryGetProperty("semanticDeltaBreakdowns", out var semanticDeltas));
        Assert.Equal(JsonValueKind.Array, semanticDeltas.ValueKind);
    }

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
