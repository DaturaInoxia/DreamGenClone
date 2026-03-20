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
    [InlineData(PromptIntent.Message)]
    [InlineData(PromptIntent.Narrative)]
    [InlineData(PromptIntent.Instruction)]
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
        Assert.Equal("Advance the scene.", continuation.LastPromptText);
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService(RecordingContinuationService continuation)
    {
        var fakeSessionService = new FakeSessionService();
        var coreAutoSave = new FakeCoreAutoSaveCoordinator();
        var autoSave = new AutoSaveCoordinator(coreAutoSave, fakeSessionService, NullLogger<AutoSaveCoordinator>.Instance);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        var router = new RolePlayPromptRouter();
        var identities = new FakeRolePlayIdentityOptionsService();

        var service = new RolePlayEngineService(
            continuation,
            behaviorMode,
            router,
            identities,
            fakeSessionService,
            autoSave,
            NullLogger<RolePlayEngineService>.Instance);

        return (service, fakeSessionService);
    }

    private sealed class RecordingContinuationService : IRolePlayContinuationService
    {
        public PromptIntent LastIntent { get; private set; }

        public string LastPromptText { get; private set; } = string.Empty;

        public Task<RolePlayInteraction> ContinueAsync(
            RolePlaySession session,
            ContinueAsActor actor,
            string? customActorName,
            PromptIntent intent,
            string promptText,
            CancellationToken cancellationToken = default)
        {
            LastIntent = intent;
            LastPromptText = promptText;

            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.User,
                ActorName = actor.ToString(),
                Content = $"Recorded for {intent}"
            });
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
                }
            ];

            return Task.FromResult(options);
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
