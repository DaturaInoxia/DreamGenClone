using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using ScenarioHistoryEntry = DreamGenClone.Domain.RolePlay.ScenarioHistoryEntry;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using System.Reflection;
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
                PhaseOverrideFloor = NarrativePhase.Approaching,
                PhaseOverrideScenarioId = "scenario-a",
                PhaseOverrideCycleIndex = 3,
                PhaseOverrideSource = "/nextphase",
                PhaseOverrideAppliedUtc = DateTime.UtcNow,
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
                    new CharacterStatProfileV2 { CharacterId = "char-a", Desire = 60, Restraint = 45, Dominance = 50, Loyalty = 60, SelfRespect = 50, RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 58, ["Connection"] = 55 } }
                ]
            };

            await repository.SaveAdaptiveStateAsync(state);
            var reloaded = await repository.LoadAdaptiveStateAsync("session-v2");

            Assert.NotNull(reloaded);
            Assert.Equal(state.ActiveScenarioId, reloaded!.ActiveScenarioId);
            Assert.Equal(state.CurrentPhase, reloaded.CurrentPhase);
            Assert.Equal(NarrativePhase.Approaching, reloaded.PhaseOverrideFloor);
            Assert.Equal("scenario-a", reloaded.PhaseOverrideScenarioId);
            Assert.Equal(3, reloaded.PhaseOverrideCycleIndex);
            Assert.Equal("/nextphase", reloaded.PhaseOverrideSource);
            Assert.NotNull(reloaded.PhaseOverrideAppliedUtc);
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
    public async Task ThemeMachineLifecycle_CooldownGate_BlocksUntilBothConditionsAreMet()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var transition = new RPThemeMachineTransition
        {
            TransitionId = "cooldown-next-eligible",
            FromStateCode = "ReintegrationCooldown",
            ToStateCode = "NextDisappearanceEligible",
            Priority = 10,
            TriggerType = "cooldown-eligibility",
            GateConfigJson = "{\"minimumInteractions\":4,\"requireReturnBeatCompleted\":true,\"returnBeatCompletionSignals\":[\"returned\"],\"returnBeatTransgressorRole\":\"Wife\",\"returnBeatPartnerRole\":\"Husband\"}",
            BlockReasonCode = "ReintegrationCooldownGateBlocked",
            IsEnabled = true
        };

        var result = await evaluator.EvaluateAsync(
            new AdaptiveScenarioState { SessionId = "session-1" },
            new ThemeMachineEvaluationContext
            {
                SessionId = "session-1",
                ActiveScenarioId = "theme-1",
                ThemeId = "theme-1",
                Snapshot = new ThemeMachineSessionSnapshot
                {
                    MachineKey = "infidelity-brief-disappearance",
                    ThemeId = "theme-1",
                    DefinitionId = "definition-1",
                    DefinitionVersion = 1,
                    CurrentStateCode = "ReintegrationCooldown",
                    TurnsInCurrentState = 3,
                    ReturnBeatCompleted = false,
                    LastEvaluatedUtc = DateTime.UtcNow
                },
                Transitions = [transition]
            });

        Assert.False(result.TransitionApplied);
        Assert.Equal("ReintegrationCooldown", result.UpdatedSnapshot.CurrentStateCode);
        Assert.Contains("ReintegrationCooldownGateBlocked", result.Directive.ReasonCodes);
        Assert.True(result.Directive.BlockDisappearanceCandidates);
    }

    [Fact]
    public async Task ThemeMachineLifecycle_CooldownGate_TransitionsWhenBothConditionsAreMet()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var transition = new RPThemeMachineTransition
        {
            TransitionId = "cooldown-next-eligible",
            FromStateCode = "ReintegrationCooldown",
            ToStateCode = "NextDisappearanceEligible",
            Priority = 10,
            TriggerType = "cooldown-eligibility",
            GateConfigJson = "{\"minimumInteractions\":4,\"requireReturnBeatCompleted\":true,\"returnBeatCompletionSignals\":[\"returned\"],\"returnBeatTransgressorRole\":\"Wife\",\"returnBeatPartnerRole\":\"Husband\"}",
            BlockReasonCode = "ReintegrationCooldownGateBlocked",
            IsEnabled = true
        };

        var result = await evaluator.EvaluateAsync(
            new AdaptiveScenarioState { SessionId = "session-1" },
            new ThemeMachineEvaluationContext
            {
                SessionId = "session-1",
                ActiveScenarioId = "theme-1",
                ThemeId = "theme-1",
                Snapshot = new ThemeMachineSessionSnapshot
                {
                    MachineKey = "infidelity-brief-disappearance",
                    ThemeId = "theme-1",
                    DefinitionId = "definition-1",
                    DefinitionVersion = 1,
                    CurrentStateCode = "ReintegrationCooldown",
                    TurnsInCurrentState = 4,
                    ReturnBeatCompleted = true,
                    LastEvaluatedUtc = DateTime.UtcNow
                },
                Transitions = [transition]
            });

        Assert.True(result.TransitionApplied);
        Assert.Equal("NextDisappearanceEligible", result.UpdatedSnapshot.CurrentStateCode);
        Assert.False(result.Directive.BlockDisappearanceCandidates);
    }

    [Fact]
    public async Task PendingDecisionPrompt_SkipsDeferredAndAppliedDecisionPoints()
    {
        var repository = new FakeRolePlayStateRepository();
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
        var repository = new FakeRolePlayStateRepository();
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
        var repository = new FakeRolePlayStateRepository();
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
    public async Task ContinueAs_AfterDecisionApply_SuppressesNarrativeForOneTurnOnly()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository, suppressNarrativeAfterDecision: true);
        var created = await service.CreateSessionAsync("Decision Narrative Suppression Session");

        var point = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-suppress-1",
            CreatedUtc = DateTime.UtcNow,
            AskingActorName = "Dean",
            TargetActorId = "Becky",
            TriggerSource = DecisionTrigger.CharacterDirectQuestion.ToString(),
            TransparencyMode = TransparencyMode.Directional,
            OptionIds = ["hold-back"]
        };

        await repository.SaveDecisionPointAsync(point,
        [
            new DecisionOption
            {
                DecisionPointId = "dp-suppress-1",
                OptionId = "hold-back",
                DisplayText = "Hold Back"
            }
        ]);

        var outcome = await service.ApplyDecisionAsync(created.Id, "dp-suppress-1", "hold-back");
        Assert.NotNull(outcome);
        Assert.True(outcome!.Applied);

        var firstContinue = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            IncludeNarrative = true,
            TriggeredBy = SubmissionSource.ContinueAsPopupContinue
        });

        Assert.True(firstContinue.Success);
        Assert.Null(firstContinue.NarrativeOutput);

        var afterFirst = await service.GetSessionAsync(created.Id);
        Assert.NotNull(afterFirst);
        Assert.False(afterFirst!.SuppressNextNarrativeAfterDecision);
        Assert.DoesNotContain(afterFirst.Interactions, x => string.Equals(x.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase));

        var secondContinue = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            IncludeNarrative = true,
            TriggeredBy = SubmissionSource.ContinueAsPopupContinue
        });

        Assert.True(secondContinue.Success);
        Assert.NotNull(secondContinue.NarrativeOutput);

        var afterSecond = await service.GetSessionAsync(created.Id);
        Assert.NotNull(afterSecond);
        Assert.Contains(afterSecond!.Interactions, x => string.Equals(x.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContinueAs_AfterDecisionApply_WithSuppressionDisabled_IncludesNarrativeImmediately()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository, suppressNarrativeAfterDecision: false);
        var created = await service.CreateSessionAsync("Decision Narrative Enabled Session");

        var point = new DecisionPoint
        {
            SessionId = created.Id,
            DecisionPointId = "dp-no-suppress-1",
            CreatedUtc = DateTime.UtcNow,
            AskingActorName = "Dean",
            TargetActorId = "Becky",
            TriggerSource = DecisionTrigger.CharacterDirectQuestion.ToString(),
            TransparencyMode = TransparencyMode.Directional,
            OptionIds = ["hold-back"]
        };

        await repository.SaveDecisionPointAsync(point,
        [
            new DecisionOption
            {
                DecisionPointId = "dp-no-suppress-1",
                OptionId = "hold-back",
                DisplayText = "Hold Back"
            }
        ]);

        var outcome = await service.ApplyDecisionAsync(created.Id, "dp-no-suppress-1", "hold-back");
        Assert.NotNull(outcome);
        Assert.True(outcome!.Applied);

        var firstContinue = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = created.Id,
            IncludeNarrative = true,
            TriggeredBy = SubmissionSource.ContinueAsPopupContinue
        });

        Assert.True(firstContinue.Success);
        Assert.NotNull(firstContinue.NarrativeOutput);

        var afterFirst = await service.GetSessionAsync(created.Id);
        Assert.NotNull(afterFirst);
        Assert.False(afterFirst!.SuppressNextNarrativeAfterDecision);
        Assert.Contains(afterFirst.Interactions, x => string.Equals(x.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OverrideAdaptiveTheme_SetsActiveScenarioImmediately()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Override Theme Session");
        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);

        session!.AdaptiveState.ThemeScores["dominance"] = new ThemeScoreState
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 62
        };
        session.AdaptiveState.ThemeScores["infidelity"] = new ThemeScoreState
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 40
        };
        session.AdaptiveState.ActiveScenarioId = "dominance";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching;
        session.AdaptiveState.InteractionsSinceCommitment = 5;
        await service.SaveSessionAsync(session);

        var overridden = await service.OverrideAdaptiveThemeAsync(created.Id, "infidelity");

        Assert.Equal("infidelity", overridden.AdaptiveState.ActiveScenarioId);
        Assert.Equal("infidelity", overridden.AdaptiveState.PrimaryThemeId);
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
        var repository = new FakeRolePlayStateRepository();
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

        session!.AdaptiveState.ThemeScores["dominance"] = new ThemeScoreState
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 72
        };
        session.AdaptiveState.ThemeScores["infidelity"] = new ThemeScoreState
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
        Assert.Equal("ManualOverride", reloaded.AdaptiveState.ThemeSelectionRule);
    }

    [Fact]
    public async Task OverrideAdaptiveTheme_RemainsPinnedAfterLegacyLockWindow()
    {
        var (service, _) = CreateService();
        var created = await service.CreateSessionAsync("Override Long Pin Session");
        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);

        session!.AdaptiveState.ThemeScores["dominance"] = new ThemeScoreState
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 90
        };
        session.AdaptiveState.ThemeScores["infidelity"] = new ThemeScoreState
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 35
        };
        session.AdaptiveState.ActiveScenarioId = "dominance";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;
        session.AdaptiveState.InteractionsSinceCommitment = 0;
        await service.SaveSessionAsync(session);

        await service.OverrideAdaptiveThemeAsync(created.Id, "infidelity");

        var overridden = await service.GetSessionAsync(created.Id);
        Assert.NotNull(overridden);
        overridden!.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;
        overridden.AdaptiveState.InteractionsSinceCommitment = 9;
        await service.SaveSessionAsync(overridden);

        await service.AddInteractionAsync(created.Id, ContinueAsActor.You, "dominate command obey control push harder");

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("infidelity", reloaded!.AdaptiveState.ActiveScenarioId);
        Assert.Equal("infidelity", reloaded.AdaptiveState.PrimaryThemeId);
        Assert.Equal("ManualOverride", reloaded.AdaptiveState.ThemeSelectionRule);
    }

    [Fact]
    public async Task RunAdaptivePipelinesAsync_NextPhase_AppliesPhaseOverrideLockForActiveRun()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Next Phase Lock Session");

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.AdaptiveState.ActiveScenarioId = "scenario-a";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;
        await repository.SaveAdaptiveStateAsync(new AdaptiveScenarioState
        {
            SessionId = session.Id,
            ActiveScenarioId = "scenario-a",
            CurrentPhase = NarrativePhase.Committed,
            ActiveFormulaVersion = "rpv2-default",
            LastEvaluationUtc = DateTime.UtcNow
        });

        await InvokeRunAdaptivePipelineAsync(service, session, NarrativePhase.Approaching);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching, reloaded!.AdaptiveState.CurrentPhase);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching, reloaded.AdaptiveState.PhaseOverrideFloor);
        Assert.Equal("scenario-a", reloaded.AdaptiveState.PhaseOverrideScenarioId);
        Assert.Equal(reloaded.AdaptiveState.CompletedScenarios, reloaded.AdaptiveState.PhaseOverrideCycleIndex);
        Assert.Equal("/nextphase", reloaded.AdaptiveState.PhaseOverrideSource);
        Assert.NotNull(reloaded.AdaptiveState.PhaseOverrideAppliedUtc);
    }

    [Fact]
    public async Task SubmitPromptAsync_ActivePhaseFloor_PreventsBackslide()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Next Phase Floor Session");

        var staleState = new AdaptiveScenarioState
        {
            SessionId = created.Id,
            ActiveScenarioId = "scenario-a",
            CurrentPhase = NarrativePhase.BuildUp,
            InteractionCountInPhase = 0,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = 0,
            ActiveFormulaVersion = "rpv2-default",
            PhaseOverrideFloor = NarrativePhase.Approaching,
            PhaseOverrideScenarioId = "scenario-a",
            PhaseOverrideCycleIndex = 0,
            PhaseOverrideSource = "/nextphase",
            PhaseOverrideAppliedUtc = DateTime.UtcNow
        };
        await repository.SaveAdaptiveStateAsync(staleState);

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.AdaptiveState.ActiveScenarioId = "scenario-a";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp;
        await service.SaveSessionAsync(session);

        await service.SubmitPromptAsync(new UnifiedPromptSubmission
        {
            SessionId = created.Id,
            PromptText = "continue naturally",
            Intent = PromptIntent.Instruction,
            SelectedIdentityId = string.Empty,
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns,
            SubmittedVia = SubmissionSource.SendButton
        });

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching, reloaded!.AdaptiveState.CurrentPhase);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching, reloaded.AdaptiveState.PhaseOverrideFloor);
    }

    [Fact]
    public async Task SubmitPromptAsync_BuildUpBackfillsActiveScenario_WhenMissing()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("BuildUp Invariant Session");

        var staleState = new AdaptiveScenarioState
        {
            SessionId = created.Id,
            ActiveScenarioId = null,
            CurrentPhase = NarrativePhase.BuildUp,
            InteractionCountInPhase = 0,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = 0,
            ActiveFormulaVersion = "rpv2-default"
        };
        await repository.SaveAdaptiveStateAsync(staleState);

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.AdaptiveState.ActiveScenarioId = null;
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp;
        session.AdaptiveState.ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
        {
            ["scenario-a"] = new ThemeScoreState
            {
                ThemeId = "scenario-a",
                ThemeName = "Scenario A",
                Score = 95
            }
        };
        await service.SaveSessionAsync(session);

        await service.SubmitPromptAsync(new UnifiedPromptSubmission
        {
            SessionId = created.Id,
            PromptText = "continue naturally",
            Intent = PromptIntent.Instruction,
            SelectedIdentityId = string.Empty,
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns,
            SubmittedVia = SubmissionSource.SendButton
        });

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.False(string.IsNullOrWhiteSpace(reloaded!.AdaptiveState.ActiveScenarioId));
        if (reloaded.AdaptiveState.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
        {
            Assert.False(string.IsNullOrWhiteSpace(reloaded.AdaptiveState.ActiveScenarioId));
        }
    }

    [Fact]
    public async Task RunAdaptivePipelinesAsync_NextPhaseFromClimax_ClearsPhaseOverrideLockOnReset()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Next Phase Reset Session");

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.AdaptiveState.ActiveScenarioId = "scenario-a";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax;
        await repository.SaveAdaptiveStateAsync(new AdaptiveScenarioState
        {
            SessionId = session.Id,
            ActiveScenarioId = "scenario-a",
            CurrentPhase = NarrativePhase.Climax,
            ActiveFormulaVersion = "rpv2-default",
            LastEvaluationUtc = DateTime.UtcNow
        });

        await InvokeRunAdaptivePipelineAsync(service, session, NarrativePhase.Reset);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp, reloaded!.AdaptiveState.CurrentPhase);
        Assert.Null(reloaded.AdaptiveState.PhaseOverrideFloor);
        Assert.Null(reloaded.AdaptiveState.PhaseOverrideScenarioId);
        Assert.Null(reloaded.AdaptiveState.PhaseOverrideCycleIndex);
        Assert.Null(reloaded.AdaptiveState.PhaseOverrideSource);
        Assert.Null(reloaded.AdaptiveState.PhaseOverrideAppliedUtc);
    }

    [Fact]
    public async Task SubmitPromptAsync_Steer_DoesNotProgressPhaseState()
    {
        var repository = new FakeRolePlayStateRepository();
        var (service, _) = CreateService(repository);
        var created = await service.CreateSessionAsync("Steer Session");

        var baseline = new AdaptiveScenarioState
        {
            SessionId = created.Id,
            ActiveScenarioId = "scenario-a",
            CurrentPhase = NarrativePhase.Committed,
            InteractionCountInPhase = 7,
            ConsecutiveLeadCount = 2,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = 0,
            ActiveFormulaVersion = "rpv2-default"
        };
        await repository.SaveAdaptiveStateAsync(baseline);

        var session = await service.GetSessionAsync(created.Id);
        Assert.NotNull(session);
        session!.AdaptiveState.ActiveScenarioId = "scenario-a";
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;
        await service.SaveSessionAsync(session);

        var interaction = await service.SubmitPromptAsync(new UnifiedPromptSubmission
        {
            SessionId = created.Id,
            PromptText = "/steer increase emotional tension with subtle jealousy cues",
            Intent = PromptIntent.Instruction,
            SelectedIdentityId = string.Empty,
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns,
            SubmittedVia = SubmissionSource.SendButton
        });

        Assert.Equal("Instruction", interaction.ActorName);

        var persisted = await repository.LoadAdaptiveStateAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(NarrativePhase.Committed, persisted!.CurrentPhase);
        Assert.Equal(7, persisted.InteractionCountInPhase);

        var reloaded = await service.GetSessionAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DreamGenClone.Domain.RolePlay.NarrativePhase.Committed, reloaded!.AdaptiveState.CurrentPhase);
    }

    private static async Task InvokeRunAdaptivePipelineAsync(
        RolePlayEngineService service,
        RolePlaySession session,
        NarrativePhase manualTarget)
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "RunAdaptivePipelinesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invokeResult = method!.Invoke(service,
        [
            session,
            DecisionTrigger.InteractionStart,
            CancellationToken.None,
            false,
            manualTarget
        ]);

        var task = Assert.IsAssignableFrom<Task>(invokeResult);
        await task;
    }

    [Fact]
    public async Task BuildScenarioCandidates_CompletedScenarioPenalty_DemotesRepeatedTheme()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["repeat-theme"] = new ThemeScoreState
                        {
                            ThemeId = "repeat-theme",
                            ThemeName = "Repeat Theme",
                            Score = 96
                        },
                        ["fresh-theme"] = new ThemeScoreState
                        {
                            ThemeId = "fresh-theme",
                            ThemeName = "Fresh Theme",
                            Score = 91
                        }
                    },
                ScenarioHistory =
                [
                    new ScenarioHistoryEntry { ScenarioId = "repeat-theme" },
                    new ScenarioHistoryEntry { ScenarioId = "repeat-theme" },
                    new ScenarioHistoryEntry { ScenarioId = "repeat-theme" }
                ]
            }
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "BuildScenarioCandidatesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var invokeResult = method!.Invoke(service, [session, CancellationToken.None]);
        Assert.IsType<Task<List<ScenarioDefinition>>>(invokeResult);

        var candidates = await (Task<List<ScenarioDefinition>>)invokeResult!;
        Assert.NotEmpty(candidates);

        var top = candidates[0];
        var repeat = Assert.Single(candidates.Where(x => x.ScenarioId == "repeat-theme"));
        var fresh = Assert.Single(candidates.Where(x => x.ScenarioId == "fresh-theme"));

        Assert.Equal("fresh-theme", top.ScenarioId);
        Assert.True(fresh.NarrativeEvidenceScore > repeat.NarrativeEvidenceScore);
    }

    [Fact]
    public async Task BuildScenarioCandidates_RecentCompletedThemeGetsAdditionalPenalty()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["older-theme"] = new ThemeScoreState
                        {
                            ThemeId = "older-theme",
                            ThemeName = "Older Theme",
                            Score = 90
                        },
                        ["recent-theme"] = new ThemeScoreState
                        {
                            ThemeId = "recent-theme",
                            ThemeName = "Recent Theme",
                            Score = 90
                        }
                    },
                ScenarioHistory =
                [
                    new ScenarioHistoryEntry { ScenarioId = "older-theme" },
                    new ScenarioHistoryEntry { ScenarioId = "recent-theme" }
                ]
            }
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "BuildScenarioCandidatesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var invokeResult = method!.Invoke(service, [session, CancellationToken.None]);
        Assert.IsType<Task<List<ScenarioDefinition>>>(invokeResult);

        var candidates = await (Task<List<ScenarioDefinition>>)invokeResult!;
        Assert.NotEmpty(candidates);

        var older = Assert.Single(candidates.Where(x => x.ScenarioId == "older-theme"));
        var recent = Assert.Single(candidates.Where(x => x.ScenarioId == "recent-theme"));

        Assert.True(older.NarrativeEvidenceScore > recent.NarrativeEvidenceScore);
        Assert.True(older.PreferencePriorityScore > recent.PreferencePriorityScore);
    }

    [Fact]
    public async Task BuildScenarioCandidates_SessionThemeSelections_MostRecentCompletedThemeGetsImmediatePenalty()
    {
        // When a session uses explicit theme selections, the most-recently-completed theme must
        // receive penalized input scores (cumulative + recent) and a reduced FitScoreMultiplier
        // so it yields to other selected themes even when CharacterAlignment would dominate.
        var themeA = new DreamGenClone.Domain.RolePlay.RPTheme { Id = "theme-a", Label = "Theme A", IsEnabled = true };
        var themeB = new DreamGenClone.Domain.RolePlay.RPTheme { Id = "theme-b", Label = "Theme B", IsEnabled = true };
        var stubThemeService = new StubListOnlyRpThemeService([themeA, themeB]);

        var service = RolePlayTestFactory.CreateEngineService(rpThemeService: stubThemeService);

        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = themeA.Id, Tier = DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave },
                new SessionThemeSelection { ThemeId = themeB.Id, Tier = DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave }
            ],
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        [themeA.Id] = new ThemeScoreState { ThemeId = themeA.Id, Score = 90 },
                        [themeB.Id] = new ThemeScoreState { ThemeId = themeB.Id, Score = 90 }
                    },
                // theme-a was just completed — it is the most recent entry
                ScenarioHistory = [new ScenarioHistoryEntry { ScenarioId = themeA.Id }]
            }
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "BuildScenarioCandidatesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invokeResult = method!.Invoke(service, [session, CancellationToken.None]);
        Assert.IsType<Task<List<ScenarioDefinition>>>(invokeResult);
        var candidates = await (Task<List<ScenarioDefinition>>)invokeResult!;

        Assert.NotEmpty(candidates);
        var candidateA = Assert.Single(candidates.Where(x => x.ScenarioId == themeA.Id));
        var candidateB = Assert.Single(candidates.Where(x => x.ScenarioId == themeB.Id));

        // theme-a (just completed) must be penalized; theme-b (never played) must score higher
        Assert.True(candidateB.NarrativeEvidenceScore > candidateA.NarrativeEvidenceScore,
            $"Expected theme-b NarrativeEvidence ({candidateB.NarrativeEvidenceScore}) > theme-a ({candidateA.NarrativeEvidenceScore})");
        Assert.True(candidateB.PreferencePriorityScore > candidateA.PreferencePriorityScore,
            $"Expected theme-b PreferencePriority ({candidateB.PreferencePriorityScore}) > theme-a ({candidateA.PreferencePriorityScore})");

        // theme-a must carry a reduced FitScoreMultiplier (in recentCompletedIds); theme-b must be unpenalized
        Assert.True(candidateA.FitScoreMultiplier < 1m,
            $"Expected theme-a FitScoreMultiplier < 1 (got {candidateA.FitScoreMultiplier})");
        Assert.Equal(1m, candidateB.FitScoreMultiplier);
    }

    [Fact]
    public async Task ApplyThemeSemiResetAsync_BoostsConfiguredSuccessorThemes()
    {
        var sourceTheme = new DreamGenClone.Domain.RolePlay.RPTheme
        {
            Id = "theme-source",
            Label = "Theme Source",
            IsEnabled = true,
            SuccessorThemeLinks =
            [
                new RPThemeSuccessorLink
                {
                    SourceThemeId = "theme-source",
                    SuccessorThemeId = "theme-successor",
                    ScoreBoost = 15m,
                    SortOrder = 1
                }
            ]
        };

        var service = RolePlayTestFactory.CreateEngineService(
            rpThemeService: new StubListOnlyRpThemeService([sourceTheme]));

        var tracker = new AdaptiveScenarioState
        {
            ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
            {
                ["theme-source"] = new ThemeScoreState { ThemeId = "theme-source", Score = 80 },
                ["theme-successor"] = new ThemeScoreState { ThemeId = "theme-successor", Score = 20 }
            }
        };

        var method = typeof(RolePlayEngineService).GetMethod(
            "ApplyThemeSemiResetAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(service, [tracker, "theme-source", CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(result);
        await task;

        Assert.Equal(18, tracker.ThemeScores["theme-successor"].Score);
    }

    private sealed class StubListOnlyRpThemeService : IRPThemeService
    {
        private readonly IReadOnlyList<DreamGenClone.Domain.RolePlay.RPTheme> _themes;
        public StubListOnlyRpThemeService(IReadOnlyList<DreamGenClone.Domain.RolePlay.RPTheme> themes) => _themes = themes;

        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult(_themes);

        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPThemeProfileThemeAssignment>>([]);

        public Task<DreamGenClone.Domain.RolePlay.RPTheme> SaveThemeAsync(DreamGenClone.Domain.RolePlay.RPTheme theme, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>> ResolveSemanticEventMappingsByProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_themes.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPThemeProfile> SaveProfileAsync(DreamGenClone.Domain.RolePlay.RPThemeProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPThemeMachineDefinition> SaveMachineDefinitionAsync(DreamGenClone.Domain.RolePlay.RPThemeMachineDefinition definition, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPThemeMachineDefinition>> ListMachineDefinitionsAsync(string themeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPThemeMachineDefinition?> GetMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ActivateMachineDefinitionAsync(string themeId, string machineKey, int version, string actorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task MigrateSessionMachineVersionAsync(string sessionId, string themeId, string machineKey, int targetVersion, string actorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(DreamGenClone.Domain.RolePlay.RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(DreamGenClone.Domain.RolePlay.RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(DreamGenClone.Domain.RolePlay.RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(IReadOnlyList<RPThemeImportFile> files, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishLocation> SaveFinishLocationAsync(DreamGenClone.Domain.RolePlay.RPFinishLocation entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishFacialType> SaveFinishFacialTypeAsync(DreamGenClone.Domain.RolePlay.RPFinishFacialType entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(DreamGenClone.Domain.RolePlay.RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(DreamGenClone.Domain.RolePlay.RPFinishHisControlLevel entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPFinishTransitionAction> SaveFinishTransitionActionAsync(DreamGenClone.Domain.RolePlay.RPFinishTransitionAction entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.RolePlay.RPPosition> SavePositionAsync(DreamGenClone.Domain.RolePlay.RPPosition entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPPosition>> ListPositionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RPPosition>> ListPositionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeletePositionAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static (RolePlayEngineService Service, FakeSessionService SessionService) CreateService(
        IRolePlayStateRepository? v2StateRepository = null,
        bool suppressNarrativeAfterDecision = false)
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
            new RolePlayTestFactory.FakeCharacterProfileService(),
            autoSave,
            new RolePlayTestFactory.NullRolePlayDebugEventSink(),
            NullLogger<RolePlayEngineService>.Instance,
            decisionPointService: new DecisionPointService(NullLogger<DecisionPointService>.Instance),
            scenarioLifecycleService: new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance),
            stateRepository: v2StateRepository,
            rolePlayDecisionOptions: Options.Create(new RolePlayDecisionOptions
            {
                SuppressNarrativeAfterDecision = suppressNarrativeAfterDecision
            }));

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
            CancellationToken cancellationToken = default,
            int? turnIndex = null,
            int? positionInTurn = null,
            int? turnActorCount = null)
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

        public Task<RolePlayInteraction> ContinueNarrativeAsync(
            RolePlaySession session,
            string actorName,
            string promptText,
            CancellationToken cancellationToken = default,
            int? turnIndex = null,
            int? turnActorCount = null)
        {
            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = actorName,
                Content = promptText,
                GeneratedByCommand = "Narrative"
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

    private sealed class FakeRolePlayStateRepository : IRolePlayStateRepository
    {
        private readonly Dictionary<string, AdaptiveScenarioState> _states = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DecisionPoint> _points = [];
        private readonly Dictionary<string, IReadOnlyList<DecisionOption>> _options = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RolePlayTurn> _turns = [];

        public Task<RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
        {
            var turn = new RolePlayTurn
            {
                TurnId = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                TurnIndex = _turns.Count(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)) + 1,
                TurnKind = turnKind,
                TriggerSource = triggerSource,
                InitiatedByActorName = initiatedByActorName,
                InputInteractionId = inputInteractionId,
                StartedUtc = DateTime.UtcNow,
                Status = RolePlayTurnStatus.Started
            };
            _turns.Add(turn);
            return Task.FromResult(turn);
        }

        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default)
        {
            var index = _turns.FindIndex(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.TurnId, turnId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _turns[index];
                _turns[index] = new RolePlayTurn
                {
                    TurnId = existing.TurnId,
                    SessionId = existing.SessionId,
                    TurnIndex = existing.TurnIndex,
                    TurnKind = existing.TurnKind,
                    TriggerSource = existing.TriggerSource,
                    InitiatedByActorName = existing.InitiatedByActorName,
                    InputInteractionId = existing.InputInteractionId,
                    OutputInteractionIds = outputInteractionIds,
                    StartedUtc = existing.StartedUtc,
                    CompletedUtc = DateTime.UtcNow,
                    Status = succeeded ? RolePlayTurnStatus.Completed : RolePlayTurnStatus.Failed,
                    FailureReason = failureReason
                };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RolePlayTurn>>(_turns
                .Where(x => string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.TurnIndex)
                .Take(take)
                .Reverse()
                .ToList());

        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
        {
            _states[state.SessionId] = CloneState(state);
            return Task.CompletedTask;
        }

        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryGetValue(sessionId, out var state)
                ? CloneState(state)
                : null);
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
        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ThemeMachineDiagnosticEvent>>([]);
        public Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);

        private static AdaptiveScenarioState CloneState(AdaptiveScenarioState state)
        {
            return new AdaptiveScenarioState
            {
                SessionId = state.SessionId,
                ActiveScenarioId = state.ActiveScenarioId,
                ActiveVariantId = state.ActiveVariantId,
                CurrentPhase = state.CurrentPhase,
                InteractionCountInPhase = state.InteractionCountInPhase,
                ConsecutiveLeadCount = state.ConsecutiveLeadCount,
                LastEvaluationUtc = state.LastEvaluationUtc,
                CycleIndex = state.CycleIndex,
                ActiveFormulaVersion = state.ActiveFormulaVersion,
                SelectedWillingnessProfileId = state.SelectedWillingnessProfileId,
                SelectedNarrativeGateProfileId = state.SelectedNarrativeGateProfileId,
                // HusbandAwarenessProfileId removed B-042 — see CharacterEncounterProfileIds
                PhaseOverrideFloor = state.PhaseOverrideFloor,
                PhaseOverrideScenarioId = state.PhaseOverrideScenarioId,
                PhaseOverrideCycleIndex = state.PhaseOverrideCycleIndex,
                PhaseOverrideSource = state.PhaseOverrideSource,
                PhaseOverrideAppliedUtc = state.PhaseOverrideAppliedUtc,
                CurrentSceneLocation = state.CurrentSceneLocation,
                CharacterLocations = state.CharacterLocations
                    .Select(x => new CharacterLocationState
                    {
                        CharacterId = x.CharacterId,
                        TrueLocation = x.TrueLocation,
                        IsHidden = x.IsHidden,
                        UpdatedUtc = x.UpdatedUtc
                    })
                    .ToList(),
                CharacterLocationPerceptions = state.CharacterLocationPerceptions
                    .Select(x => new CharacterLocationPerceptionState
                    {
                        ObserverCharacterId = x.ObserverCharacterId,
                        TargetCharacterId = x.TargetCharacterId,
                        PerceivedLocation = x.PerceivedLocation,
                        Confidence = x.Confidence,
                        HasLineOfSight = x.HasLineOfSight,
                        IsInProximity = x.IsInProximity,
                        KnowledgeSource = x.KnowledgeSource,
                        UpdatedUtc = x.UpdatedUtc
                    })
                    .ToList(),
                CharacterSnapshots = state.CharacterSnapshots
                    .Select(x => new CharacterStatProfileV2
                    {
                        CharacterId = x.CharacterId,
                        Desire = x.Desire,
                        Restraint = x.Restraint,
                        RuntimeEncounterStats = x.RuntimeEncounterStats != null ? new Dictionary<string, int>(x.RuntimeEncounterStats) : null,
                        Dominance = x.Dominance,
                        Loyalty = x.Loyalty,
                        SelfRespect = x.SelfRespect,
                        SnapshotUtc = x.SnapshotUtc
                    })
                    .ToList()
            };
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
                ActiveVariantId TEXT NULL,
                CurrentPhase TEXT NOT NULL,
                InteractionCountInPhase INTEGER NOT NULL,
                ConsecutiveLeadCount INTEGER NOT NULL,
                LastEvaluationUtc TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ActiveFormulaVersion TEXT NOT NULL,
                SelectedWillingnessProfileId TEXT NULL,
                SelectedNarrativeGateProfileId TEXT NULL,
                HusbandAwarenessProfileId TEXT NULL,
                PhaseOverrideFloor TEXT NULL,
                PhaseOverrideScenarioId TEXT NULL,
                PhaseOverrideCycleIndex INTEGER NULL,
                PhaseOverrideSource TEXT NULL,
                PhaseOverrideAppliedUtc TEXT NULL,
                CurrentSceneLocation TEXT NULL,
                CharacterLocationsJson TEXT NOT NULL DEFAULT '[]',
                CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]',
                CharacterSnapshotsJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries (
                Id                          TEXT NOT NULL PRIMARY KEY,
                SessionId                   TEXT NOT NULL,
                CharacterId                 TEXT NOT NULL,
                SummaryType                 TEXT NOT NULL,
                CycleIndex                  INTEGER NOT NULL DEFAULT 0,
                FromPhase                   TEXT NOT NULL,
                ToPhase                     TEXT NOT NULL,
                OccurredUtc                 TEXT NOT NULL,
                InteractionCountInPhase     INTEGER NOT NULL DEFAULT 0,
                SceneLocation               TEXT NULL,
                ActiveThemeId               TEXT NULL,
                FinishingMoveId             TEXT NULL,
                PositionIdsJson             TEXT NULL,
                CharacterStatsSnapshotJson  TEXT NOT NULL DEFAULT '{}',
                TemplateSummary             TEXT NOT NULL DEFAULT '',
                LlmSummary                  TEXT NULL,
                LlmEnhancedUtc              TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
