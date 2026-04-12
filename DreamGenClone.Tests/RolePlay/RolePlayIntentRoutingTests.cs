using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayIntentRoutingTests
{
    [Theory]
    [InlineData(PromptIntent.Message, "continue-message", false, true)]
    [InlineData(PromptIntent.Narrative, "continue-narrative", false, true)]
    public void Resolve_ReturnsExpectedRouteForCharacterIntents(
        PromptIntent intent,
        string targetCommand,
        bool requiresInstructionPayload,
        bool requiresActorContext)
    {
        var router = new RolePlayPromptRouter();

        var route = router.Resolve(intent);

        Assert.Equal(intent, route.Intent);
        Assert.Equal(targetCommand, route.TargetCommand);
        Assert.Equal(requiresInstructionPayload, route.RequiresInstructionPayload);
        Assert.Equal(requiresActorContext, route.RequiresActorContext);
    }

    [Theory]
    [InlineData(PromptIntent.Message)]
    [InlineData(PromptIntent.Narrative)]
    public async Task SubmitPromptAsync_UsesSubmissionIntent(PromptIntent intent)
    {
        var continuation = new RecordingContinuationService();
        var (service, _) = CreateService(continuation);
        var session = await service.CreateSessionAsync("Routing Test", personaName: "Pilot");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Advance the scene.",
            Intent = intent,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns
        };

        _ = await service.SubmitPromptAsync(submission);

        Assert.Equal(intent, continuation.LastIntent);
        Assert.Equal(1, continuation.ContinueCallCount);
        Assert.Contains("Advance the scene.", continuation.LastPromptText);

        var updatedSession = await service.GetSessionAsync(session.Id);
        Assert.NotNull(updatedSession);
        Assert.Equal(2, updatedSession!.Interactions.Count);
        Assert.Equal("You", updatedSession.Interactions[0].ActorName);
        Assert.Equal("Advance the scene.", updatedSession.Interactions[0].Content);
        Assert.Equal("You", updatedSession.Interactions[1].ActorName);
    }

    [Fact]
    public async Task SubmitPromptAsync_MessageWithSceneCharacter_UsesSelectedCharacterForPromptAndResponse()
    {
        var continuation = new RecordingContinuationService();
        var (service, _) = CreateService(continuation);
        var session = await service.CreateSessionAsync("Routing Test", personaName: "Pilot");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Get dressed and leave the room.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "scene:becky",
            SelectedIdentityType = IdentityOptionSource.SceneCharacter,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns
        };

        _ = await service.SubmitPromptAsync(submission);

        var updatedSession = await service.GetSessionAsync(session.Id);
        Assert.NotNull(updatedSession);
        Assert.Equal(2, updatedSession!.Interactions.Count);
        Assert.Equal("Becky", updatedSession.Interactions[0].ActorName);
        Assert.Equal("Get dressed and leave the room.", updatedSession.Interactions[0].Content);
        Assert.Equal("Becky", updatedSession.Interactions[1].ActorName);
        Assert.Equal("Becky", continuation.LastCustomActorName);
    }

    [Fact]
    public async Task SubmitPromptAsync_Instruction_BypassesContinuationService()
    {
        var continuation = new RecordingContinuationService();
        var (service, _) = CreateService(continuation);
        var session = await service.CreateSessionAsync("Routing Test", personaName: "Pilot");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Advance the scene.",
            Intent = PromptIntent.Instruction,
            SubmittedVia = SubmissionSource.PlusButton,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns
        };

        var interaction = await service.SubmitPromptAsync(submission);

        Assert.Equal("Instruction", interaction.ActorName);
        Assert.Equal(0, continuation.ContinueCallCount);
    }

    [Fact]
    public async Task SubmitPromptAsync_PlusButton_MessageWithSceneCharacter_AddsSingleCharacterInteraction()
    {
        var continuation = new RecordingContinuationService();
        var (service, _) = CreateService(continuation);
        var session = await service.CreateSessionAsync("Routing Test", personaName: "Pilot");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Get dressed and leave the room.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "scene:becky",
            SelectedIdentityType = IdentityOptionSource.SceneCharacter,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns,
            SubmittedVia = SubmissionSource.PlusButton
        };

        var interaction = await service.SubmitPromptAsync(submission);

        var updatedSession = await service.GetSessionAsync(session.Id);
        Assert.NotNull(updatedSession);
        Assert.Single(updatedSession!.Interactions);
        Assert.Equal("Becky", updatedSession.Interactions[0].ActorName);
        Assert.Equal("Get dressed and leave the room.", updatedSession.Interactions[0].Content);
        Assert.Equal("Becky", interaction.ActorName);
        Assert.Equal(0, continuation.ContinueCallCount);
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService(RecordingContinuationService continuation)
    {
        var fakeSessionService = new FakeSessionService();
        var coreAutoSave = new FakeCoreAutoSaveCoordinator();
        var autoSave = new AutoSaveCoordinator(coreAutoSave, fakeSessionService, NullLogger<AutoSaveCoordinator>.Instance);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        var router = new RolePlayPromptRouter();
        var identities = new FakeRolePlayIdentityOptionsService();
        var validator = new RolePlayCommandValidator(behaviorMode);

        var service = new RolePlayEngineService(
            continuation,
            behaviorMode,
            router,
            identities,
            new RolePlayAdaptiveStateService(new RolePlayTestFactory.FakeThemeCatalogService()),
            validator,
            fakeSessionService,
            new RolePlayTestFactory.NullScenarioService(),
            new RolePlayTestFactory.FakeBaseStatProfileService(),
            autoSave,
            new RolePlayTestFactory.NullRolePlayDebugEventSink(),
            NullLogger<RolePlayEngineService>.Instance);

        return (service, fakeSessionService);
    }

    private sealed class RecordingContinuationService : IRolePlayContinuationService
    {
        public PromptIntent LastIntent { get; private set; }
        public string LastPromptText { get; private set; } = string.Empty;

        public string? LastCustomActorName { get; private set; }

        public int ContinueCallCount { get; private set; }

        public Task<RolePlayInteraction> ContinueAsync(
            RolePlaySession session,
            ContinueAsActor actor,
            string? customActorName,
            PromptIntent intent,
            string promptText,
            Func<string, Task>? onChunk = null,
            CancellationToken cancellationToken = default)
        {
            ContinueCallCount++;
            LastIntent = intent;
            LastPromptText = promptText;
            LastCustomActorName = customActorName;

            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.User,
                ActorName = string.IsNullOrWhiteSpace(customActorName) ? actor.ToString() : customActorName,
                Content = $"Recorded for {intent}"
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
            return Task.FromResult(new ContinueAsResult { Success = true });
        }
    }

    private sealed class FakeRolePlayIdentityOptionsService : IRolePlayIdentityOptionsService
    {
        public Task<IReadOnlyList<IdentityOption>> GetIdentityOptionsAsync(RolePlaySession session, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IdentityOption> options =
            [
                new IdentityOption
                {
                    Id = "persona:you",
                    DisplayName = "You",
                    SourceType = IdentityOptionSource.Persona,
                    Actor = ContinueAsActor.You,
                    IsAvailable = true
                },
                new IdentityOption
                {
                    Id = "scene:becky",
                    DisplayName = "Becky",
                    SourceType = IdentityOptionSource.SceneCharacter,
                    Actor = ContinueAsActor.Npc,
                    IsAvailable = true
                }
            ];

            return Task.FromResult(options);
        }

        public bool IsIdentityAvailableForIntent(RolePlaySession session, IdentityOption option, PromptIntent intent, out string? availabilityReason)
        {
            availabilityReason = null;
            return option.IsAvailable;
        }
    }

    private sealed class FakeCoreAutoSaveCoordinator : CoreAutoSaveCoordinator
    {
        public void RequestSave(string reason, Func<CancellationToken, Task> saveAction)
        {
            _ = saveAction(CancellationToken.None);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionService : ISessionService
    {
        private readonly Dictionary<string, RolePlaySession> _sessions = [];

        public Task SaveStorySessionAsync(StorySession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
        {
            _sessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<StorySession?>(null);

        public Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session);
        }

        public Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SessionListItem>>([]);

        public Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionExportEnvelope?>(null);

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_sessions.Remove(sessionId));
    }
}
