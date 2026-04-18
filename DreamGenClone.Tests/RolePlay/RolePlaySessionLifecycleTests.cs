using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Application.RolePlay;
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
                CurrentSceneLocation = "Secluded Garden",
                CharacterLocations =
                [
                    new CharacterLocationState { CharacterId = "char-a", TrueLocation = "Secluded Garden", IsHidden = false }
                ],
                CharacterLocationPerceptions =
                [
                    new CharacterLocationPerceptionState
                    {
                        ObserverCharacterId = "char-a",
                        TargetCharacterId = "char-a",
                        PerceivedLocation = "Secluded Garden",
                        Confidence = 100,
                        HasLineOfSight = true,
                        IsInProximity = true,
                        KnowledgeSource = "self"
                    }
                ],
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
            Assert.Equal("Secluded Garden", reloaded.CurrentSceneLocation);
            Assert.Single(reloaded.CharacterLocations);
            Assert.Single(reloaded.CharacterLocationPerceptions);
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

    [Fact]
    public async Task PendingDecisionPrompt_SkipsDeferredAndAppliedDecisionPoints()
    {
        var repository = new FakeRolePlayV2StateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Queue Session");

        var pointOld = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-old",
            CreatedUtc = DateTime.UtcNow.AddMinutes(-2),
            TransparencyMode = TransparencyMode.Directional
        };
        var pointNew = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-new",
            CreatedUtc = DateTime.UtcNow.AddMinutes(-1),
            TransparencyMode = TransparencyMode.Directional
        };
        await repository.SaveDecisionPointAsync(pointOld,
        [
            new DecisionOption { DecisionPointId = "dp-old", OptionId = "old-1", DisplayText = "Old" }
        ]);
        await repository.SaveDecisionPointAsync(pointNew,
        [
            new DecisionOption { DecisionPointId = "dp-new", OptionId = "new-1", DisplayText = "New" }
        ]);

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.DeferredDecisionPointIds.Add("dp-new");
        await service.SaveSessionAsync(session);

        var prompt = await service.GetPendingDecisionPromptAsync(created.Id);

        Assert.NotNull(prompt);
        Assert.Equal("dp-old", prompt!.DecisionPoint.DecisionPointId);
    }

    [Fact]
    public async Task DeferRestoreAndSkipDecisionPoint_UpdatesQueueAndSessionState()
    {
        var repository = new FakeRolePlayV2StateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Deferred Session");

        var point = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-1",
            CreatedUtc = DateTime.UtcNow,
            AskingActorName = "Becky",
            TargetActorId = "Dean",
            TriggerSource = DecisionTrigger.CharacterDirectQuestion.ToString(),
            TransparencyMode = TransparencyMode.Directional
        };
        await repository.SaveDecisionPointAsync(point,
        [
            new DecisionOption { DecisionPointId = "dp-1", OptionId = "opt-1", DisplayText = "Answer" }
        ]);

        var deferred = await service.DeferDecisionPointAsync(created.Id, "dp-1");
        Assert.True(deferred);

        var pendingAfterDefer = await service.GetPendingDecisionPromptAsync(created.Id);
        Assert.Null(pendingAfterDefer);

        var deferredPrompts = await service.GetDeferredDecisionPromptsAsync(created.Id);
        Assert.Single(deferredPrompts);
        Assert.Equal("dp-1", deferredPrompts[0].DecisionPoint.DecisionPointId);

        var restored = await service.RestoreDeferredDecisionPointAsync(created.Id, "dp-1");
        Assert.True(restored);

        var pendingAfterRestore = await service.GetPendingDecisionPromptAsync(created.Id);
        Assert.NotNull(pendingAfterRestore);
        Assert.Equal("dp-1", pendingAfterRestore!.DecisionPoint.DecisionPointId);

        var skipped = await service.SkipDecisionPointAsync(created.Id, "dp-1");
        Assert.True(skipped);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Contains("dp-1", reloaded!.AppliedDecisionPointIds);
        Assert.DoesNotContain("dp-1", reloaded.DeferredDecisionPointIds);
        Assert.DoesNotContain(reloaded.Interactions, x => x.ActorName == "Decision Outcome");
    }

    [Fact]
    public async Task ApplyDecision_WithSteeringInstructions_AppendsConciseInstructionInteraction()
    {
        var repository = new FakeRolePlayV2StateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Apply Decision Instruction Session");

        var point = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-steering-1",
            CreatedUtc = DateTime.UtcNow,
            AskingActorName = "Dean",
            TargetActorId = "Becky",
            TriggerSource = DecisionTrigger.CharacterDirectQuestion.ToString(),
            TransparencyMode = TransparencyMode.Directional,
            OptionIds = ["hold-back", "custom"]
        };

        await repository.SaveDecisionPointAsync(point,
        [
            new DecisionOption
            {
                DecisionPointId = "dp-steering-1",
                OptionId = "hold-back",
                DisplayText = "Hold Back",
                CharacterDirectionInstruction = "Character Direction (Becky): keep language calm and boundaried.",
                ChatInstruction = "Chat Instruction: de-escalate tension over the next two turns."
            },
            new DecisionOption
            {
                DecisionPointId = "dp-steering-1",
                OptionId = "custom",
                DisplayText = "Custom"
            }
        ]);

        var outcome = await service.ApplyDecisionAsync(created.Id, "dp-steering-1", "hold-back");

        Assert.NotNull(outcome);
        Assert.True(outcome!.Applied);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        var instruction = reloaded!.Interactions.LastOrDefault(x => x.ActorName.Contains("Instruction", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(instruction);
        Assert.Equal("\"Hold Back\"", instruction!.Content);
        Assert.DoesNotContain("Decision Steering Payload", instruction.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Character Direction", instruction.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Chat Instruction", instruction.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverrideAdaptiveTheme_SetsActiveScenarioImmediately()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Override Theme Session");
        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);

        session!.AdaptiveState.ThemeTracker.Themes["dominance"] = new ThemeTrackerItem
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 62
        };
        session.AdaptiveState.ThemeTracker.Themes["infidelity"] = new ThemeTrackerItem
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 40
        };
        session.AdaptiveState.ActiveScenarioId = "dominance";
        session.AdaptiveState.CurrentNarrativePhase = DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching;
        session.AdaptiveState.InteractionsSinceCommitment = 5;
        await service.SaveSessionAsync(session);

        var overridden = await service.OverrideAdaptiveThemeAsync(created.Id, "infidelity");

        Assert.Equal("infidelity", overridden.AdaptiveState.ActiveScenarioId);
        Assert.Equal("infidelity", overridden.AdaptiveState.ThemeTracker.PrimaryThemeId);
        Assert.Equal(0, overridden.AdaptiveState.InteractionsSinceCommitment);
    }

    [Fact]
    public async Task OverrideAdaptiveTheme_ThrowsWhenThemeUnavailable()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Override Theme Missing");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OverrideAdaptiveThemeAsync(created.Id, "theme-does-not-exist"));
    }

    [Fact]
    public async Task ApplyDecision_InjectsSelectedDialogueIntoInstructionPayload()
    {
        var repository = new FakeRolePlayV2StateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Dialogue Injection Session");

        var point = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-dialogue-1",
            CreatedUtc = DateTime.UtcNow,
            AskingActorName = "Dean",
            TargetActorId = "Becky",
            TriggerSource = DecisionTrigger.CharacterDirectQuestion.ToString(),
            TransparencyMode = TransparencyMode.Explicit,
            OptionIds = ["lean-in"]
        };

        await repository.SaveDecisionPointAsync(point,
        [
            new DecisionOption
            {
                DecisionPointId = "dp-dialogue-1",
                OptionId = "lean-in",
                DisplayText = "\"Yeah, that sounds good.\"",
                CharacterDirectionInstruction = "Character Direction (Becky): answer naturally and stay warm.",
                ChatInstruction = "Chat Instruction: continue smoothly from the selected answer."
            }
        ]);

        var outcome = await service.ApplyDecisionAsync(created.Id, "dp-dialogue-1", "lean-in");

        Assert.NotNull(outcome);
        Assert.True(outcome!.Applied);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);

        var instruction = reloaded!.Interactions.LastOrDefault(x => x.ActorName == "Instruction");
        if (instruction is null)
        {
            instruction = reloaded.Interactions.LastOrDefault(x => x.ActorName.Contains("Instruction", StringComparison.OrdinalIgnoreCase));
        }

        Assert.NotNull(instruction);
        Assert.Contains("Instruction", instruction!.ActorName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("\"Yeah, that sounds good.\"", instruction.Content);
        Assert.DoesNotContain("Decision Steering Payload", instruction.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Character Direction", instruction.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Chat Instruction", instruction.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverrideAdaptiveTheme_PersistsAcrossNextInteraction()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Override Persists Session");
        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);

        session!.AdaptiveState.ThemeTracker.Themes["dominance"] = new ThemeTrackerItem
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 72
        };
        session.AdaptiveState.ThemeTracker.Themes["infidelity"] = new ThemeTrackerItem
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 45
        };
        session.AdaptiveState.ActiveScenarioId = "dominance";
        session.AdaptiveState.InteractionsSinceCommitment = 0;
        await service.SaveSessionAsync(session);

        await service.OverrideAdaptiveThemeAsync(created.Id, "infidelity");
        await service.AddInteractionAsync(created.Id, ContinueAsActor.You, "dominate command obey control");

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("infidelity", reloaded!.AdaptiveState.ActiveScenarioId);
        Assert.Equal("ManualOverride", reloaded.AdaptiveState.ThemeTracker.ThemeSelectionRule);
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService(IRolePlayV2StateRepository? v2StateRepository = null)
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
            NullLogger<RolePlayEngineService>.Instance,
            decisionPointService: new DecisionPointService(NullLogger<DecisionPointService>.Instance),
            v2StateRepository: v2StateRepository);

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

    private sealed class FakeRolePlayV2StateRepository : IRolePlayV2StateRepository
    {
        private readonly List<DecisionPoint> _points = [];
        private readonly Dictionary<string, IReadOnlyList<DecisionOption>> _options = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<AdaptiveScenarioState?>(null);
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScenarioCandidateEvaluation>>([]);
        public Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NarrativePhaseTransitionEvent>>([]);
        public Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default)
        {
            _points.RemoveAll(x => string.Equals(x.DecisionPointId, decisionPoint.DecisionPointId, StringComparison.OrdinalIgnoreCase));
            _points.Add(decisionPoint);
            _options[decisionPoint.DecisionPointId] = options;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
        {
            var items = _points
                .Where(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedUtc)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<DecisionPoint>>(items);
        }

        public Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default)
            => Task.FromResult(_options.TryGetValue(decisionPointId, out var options)
                ? options
                : (IReadOnlyList<DecisionOption>)[]);

        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UnsupportedSessionError>>([]);
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
                ActiveVariantId TEXT NULL,
                CurrentPhase TEXT NOT NULL,
                InteractionCountInPhase INTEGER NOT NULL,
                ConsecutiveLeadCount INTEGER NOT NULL,
                LastEvaluationUtc TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ActiveFormulaVersion TEXT NOT NULL,
                SelectedWillingnessProfileId TEXT NULL,
                HusbandAwarenessProfileId TEXT NULL,
                CurrentSceneLocation TEXT NULL,
                CharacterLocationsJson TEXT NOT NULL DEFAULT '[]',
                CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]',
                CharacterSnapshotsJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
