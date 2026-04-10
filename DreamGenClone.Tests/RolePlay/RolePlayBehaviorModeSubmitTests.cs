using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayBehaviorModeSubmitTests
{
    [Theory]
    [InlineData(BehaviorMode.TakeTurns)]
    [InlineData(BehaviorMode.Spectate)]
    public async Task SubmitPromptAsync_SnapshotsBehaviorModeAtSubmitOnSession(BehaviorMode modeAtSubmit)
    {
        var (service, _) = CreateService();
        var session = await service.CreateSessionAsync("Mode Snapshot Test");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Continue the scene.",
            Intent = PromptIntent.Narrative,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = modeAtSubmit
        };

        await service.SubmitPromptAsync(submission);

        var updated = await service.GetSessionAsync(session.Id);
        Assert.Equal(modeAtSubmit, updated!.BehaviorMode);
    }

    [Fact]
    public async Task SubmitPromptAsync_BehaviorModeAtSubmitDiffersFromCurrentMode_SessionModeUpdated()
    {
        var (service, _) = CreateService();
        var session = await service.CreateSessionAsync("Mode Switch Test");

        // Default session mode is TakeTurns
        Assert.Equal(BehaviorMode.TakeTurns, session.BehaviorMode);

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Describe the atmosphere.",
            Intent = PromptIntent.Narrative,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.Spectate
        };

        await service.SubmitPromptAsync(submission);

        var updated = await service.GetSessionAsync(session.Id);
        Assert.Equal(BehaviorMode.Spectate, updated!.BehaviorMode);
    }

    [Fact]
    public async Task SubmitPromptAsync_WhenSelectedIdentityIsUnavailable_ThrowsInvalidOperation()
    {
        var (service, _) = CreateService(unavailablePersona: true);
        var session = await service.CreateSessionAsync("Unavailable Identity Test");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            PromptText = "Do something",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.NpcOnly
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitPromptAsync(submission));
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService(
        bool unavailablePersona = false)
    {
        var fakeSessionService = new FakeSessionService();
        var coreAutoSave = new FakeCoreAutoSaveCoordinator();
        var autoSave = new AutoSaveCoordinator(
            coreAutoSave, fakeSessionService, NullLogger<AutoSaveCoordinator>.Instance);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        var router = new RolePlayPromptRouter();
        var validator = new RolePlayCommandValidator(behaviorMode);

        IRolePlayIdentityOptionsService identities = unavailablePersona
            ? new FakeIdentityOptionsWithUnavailablePersona()
            : new FakeIdentityOptionsService();

        var service = new RolePlayEngineService(
            new FakeRolePlayContinuationService(),
            behaviorMode,
            router,
            identities,
            new RolePlayAdaptiveStateService(),
            validator,
            fakeSessionService,
            new RolePlayTestFactory.NullScenarioService(),
            new RolePlayTestFactory.FakeBaseStatProfileService(),
            autoSave,
            new RolePlayTestFactory.NullRolePlayDebugEventSink(),
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
            Func<string, Task>? onChunk = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.Npc,
                ActorName = "NPC",
                Content = "Generated response."
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

    private sealed class FakeIdentityOptionsService : IRolePlayIdentityOptionsService
    {
        public Task<IReadOnlyList<IdentityOption>> GetIdentityOptionsAsync(
            RolePlaySession session, CancellationToken cancellationToken = default)
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

        public bool IsIdentityAvailableForIntent(RolePlaySession session, IdentityOption option, PromptIntent intent, out string? availabilityReason)
        {
            availabilityReason = null;
            return option.IsAvailable;
        }
    }

    private sealed class FakeIdentityOptionsWithUnavailablePersona : IRolePlayIdentityOptionsService
    {
        public Task<IReadOnlyList<IdentityOption>> GetIdentityOptionsAsync(
            RolePlaySession session, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IdentityOption> options =
            [
                new IdentityOption
                {
                    Id = "persona:you",
                    DisplayName = "You",
                    SourceType = IdentityOptionSource.Persona,
                    Actor = ContinueAsActor.You,
                    IsAvailable = false,
                    AvailabilityReason = "Not allowed in NpcOnly mode."
                }
            ];
            return Task.FromResult(options);
        }

        public bool IsIdentityAvailableForIntent(RolePlaySession session, IdentityOption option, PromptIntent intent, out string? availabilityReason)
        {
            availabilityReason = option.AvailabilityReason;
            return false;
        }
    }

    private sealed class FakeCoreAutoSaveCoordinator : CoreAutoSaveCoordinator
    {
        public void RequestSave(string reason, Func<CancellationToken, Task> saveAction)
            => _ = saveAction(CancellationToken.None);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionService : ISessionService
    {
        private readonly Dictionary<string, RolePlaySession> _sessions = [];

        public Task SaveStorySessionAsync(StorySession session, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken ct = default)
        {
            _sessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<StorySession?>(null);

        public Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken ct = default)
        {
            _sessions.TryGetValue(sessionId, out var s);
            return Task.FromResult(s);
        }

        public Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionListItem>>([]);

        public Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<SessionExportEnvelope?>(null);

        public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult(_sessions.Remove(sessionId));
    }
}
