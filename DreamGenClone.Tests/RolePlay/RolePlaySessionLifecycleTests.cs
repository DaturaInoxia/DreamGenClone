using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task V2StatePersistenceReload_RemainsConsistent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-v2-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new RolePlayStateRepository(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));
            await CreateV2TablesAsync(dbPath);

            var state = new AdaptiveScenarioState
            {
                SessionId = "session-v2",
                ActiveScenarioId = "scenario-a",
                CurrentPhase = NarrativePhase.Committed,
                InteractionCountInPhase = 2,
                ConsecutiveLeadCount = 1,
                CycleIndex = 3,
                ActiveFormulaVersion = "rpv2-default",
                CharacterSnapshots =
                [
                    new CharacterStatProfileV2 { CharacterId = "char-a", Desire = 60, Restraint = 45, Tension = 58, Connection = 55, Dominance = 50, Loyalty = 60, SelfRespect = 50 }
                ]
            };

            await repository.SaveAdaptiveStateAsync(state);
            var reloaded = await repository.LoadAdaptiveStateAsync("session-v2");

            Assert.NotNull(reloaded);
            Assert.Equal(state.ActiveScenarioId, reloaded!.ActiveScenarioId);
            Assert.Equal(state.CurrentPhase, reloaded.CurrentPhase);
            Assert.Single(reloaded.CharacterSnapshots);
            Assert.Equal(60, reloaded.CharacterSnapshots[0].Desire);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite may briefly hold file handles after async disposal on some systems.
                }
            }
        }
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
                Content = "Generated"
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

    private static async Task CreateV2TablesAsync(string dbPath)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlayV2AdaptiveStates (
                SessionId TEXT PRIMARY KEY,
                ActiveScenarioId TEXT NULL,
                CurrentPhase TEXT NOT NULL,
                InteractionCountInPhase INTEGER NOT NULL,
                ConsecutiveLeadCount INTEGER NOT NULL,
                LastEvaluationUtc TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ActiveFormulaVersion TEXT NOT NULL,
                CharacterSnapshotsJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
