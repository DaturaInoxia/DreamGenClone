using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySessionLifecycleTests
{
    [Fact]
    public async Task OpenSession_Start_TransitionsStatusToInProgress()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Test Session");

        var opened = await service.OpenSessionAsync(created.Id, RolePlaySessionOpenAction.Start);

        Assert.Equal(RolePlaySessionStatus.InProgress, opened.Status);
    }

    [Fact]
    public async Task OpenSession_ContinueOnNotStarted_Throws()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Test Session");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenSessionAsync(created.Id, RolePlaySessionOpenAction.Continue));
    }

    [Fact]
    public async Task DeleteSession_HardDeletesFromPersistenceAndCache()
    {
        var (service, sessionService) = CreateService();
        var created = await service.CreateSessionAsync("Delete Me");

        var deleted = await service.DeleteSessionAsync(created.Id);
        var loaded = await service.GetSessionAsync(created.Id);

        Assert.True(deleted);
        Assert.Equal(created.Id, sessionService.LastDeletedSessionId);
        Assert.Null(loaded);
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService()
    {
        var fakeSessionService = new FakeSessionService();
        var coreAutoSave = new FakeCoreAutoSaveCoordinator();
        var autoSave = new AutoSaveCoordinator(coreAutoSave, fakeSessionService, NullLogger<AutoSaveCoordinator>.Instance);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        var continuation = new FakeRolePlayContinuationService();
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

    private sealed class FakeRolePlayContinuationService : IRolePlayContinuationService
    {
        public Task<RolePlayInteraction> ContinueAsync(
            RolePlaySession session,
            ContinueAsActor actor,
            string? customActorName,
            PromptIntent intent,
            string promptText,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.Npc,
                ActorName = "NPC",
                Content = "Generated"
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

        public string? LastDeletedSessionId { get; private set; }

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
        {
            LastDeletedSessionId = sessionId;
            return Task.FromResult(_sessions.Remove(sessionId));
        }
    }
}
