using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

internal static class RolePlayTestFactory
{
    internal static RolePlayEngineService CreateEngineService(
        IRolePlayContinuationService? continuationService = null,
        IRolePlayIdentityOptionsService? identityOptionsService = null)
    {
        var sessionService = new FakeSessionService();
        var coreAutoSave = new FakeCoreAutoSaveCoordinator();
        var autoSave = new AutoSaveCoordinator(coreAutoSave, sessionService, NullLogger<AutoSaveCoordinator>.Instance);
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        var validator = new RolePlayCommandValidator(behaviorMode);

        return new RolePlayEngineService(
            continuationService ?? new FakeContinuationService(),
            behaviorMode,
            new RolePlayPromptRouter(),
            identityOptionsService ?? new DefaultIdentityOptionsService(),
            new RolePlayAdaptiveStateService(),
            validator,
            sessionService,
            new NullScenarioService(),
            autoSave,
            NullLogger<RolePlayEngineService>.Instance);
    }

    internal sealed class FakeContinuationService : IRolePlayContinuationService
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
                InteractionType = actor switch
                {
                    ContinueAsActor.You => InteractionType.User,
                    ContinueAsActor.Npc => InteractionType.Npc,
                    ContinueAsActor.Custom => InteractionType.Custom,
                    _ => InteractionType.System
                },
                ActorName = actor.ToString(),
                Content = $"{intent}:{promptText.Trim()}"
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
            var result = new ContinueAsResult { Success = true };
            foreach (var actor in ContinueAsOrdering.OrderDistinct(actors))
            {
                result.ParticipantOutputs.Add(new RolePlayInteraction
                {
                    InteractionType = actor switch
                    {
                        ContinueAsActor.You => InteractionType.User,
                        ContinueAsActor.Npc => InteractionType.Npc,
                        ContinueAsActor.Custom => InteractionType.Custom,
                        _ => InteractionType.System
                    },
                    ActorName = actor.ToString(),
                    Content = $"continue:{promptText}"
                });
            }

            if (includeNarrative)
            {
                result.NarrativeOutput = new RolePlayInteraction
                {
                    InteractionType = InteractionType.System,
                    ActorName = "Narrative",
                    Content = "Narrative continuation"
                };
            }

            return Task.FromResult(result);
        }
    }

    internal sealed class DefaultIdentityOptionsService : IRolePlayIdentityOptionsService
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
                    Id = "char:npc",
                    DisplayName = "NPC",
                    SourceType = IdentityOptionSource.SceneCharacter,
                    Actor = ContinueAsActor.Npc,
                    IsAvailable = true
                },
                new IdentityOption
                {
                    Id = "custom:manual",
                    DisplayName = "Custom",
                    SourceType = IdentityOptionSource.CustomCharacter,
                    Actor = ContinueAsActor.Custom,
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

    internal sealed class FakeCoreAutoSaveCoordinator : CoreAutoSaveCoordinator
    {
        public void RequestSave(string reason, Func<CancellationToken, Task> saveAction)
            => _ = saveAction(CancellationToken.None);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FakeSessionService : ISessionService
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

    /// <summary>
    /// Scenario service that returns no scenarios. Used by engine service tests 
    /// that don't need scenario-aware multi-actor continue behavior.
    /// </summary>
    internal sealed class NullScenarioService : IScenarioService
    {
        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotImplementedException();
        public Task<Scenario?> GetScenarioAsync(string id) => Task.FromResult<Scenario?>(null);
        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(new List<Scenario>());
        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id) => throw new NotImplementedException();
        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
