using System.Reflection;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayThemeMachineCommandTests
{
    [Theory]
    [InlineData("/returnbeat", true)]
    [InlineData("/return-beat complete", true)]
    [InlineData("/returnbeatdone", true)]
    [InlineData("return beat complete", false)]
    [InlineData("/steer keep scene grounded", false)]
    public void ContainsReturnBeatCompletionCommand_DetectsSupportedSlashCommands(string content, bool expected)
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ContainsReturnBeatCompletionCommand",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = Assert.IsType<bool>(method!.Invoke(null, [content]));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryApplyExplicitReturnBeatCompletion_AppliesOnlyInRequiredStates()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "TryApplyExplicitReturnBeatCompletion",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var eligibleState = new AdaptiveScenarioState
        {
            ThemeMachineSnapshot = new ThemeMachineSessionSnapshot
            {
                CurrentStateCode = "ReturnBeatRequired",
                ReturnBeatCompleted = false
            }
        };

        var applied = Assert.IsType<bool>(method!.Invoke(null, [eligibleState]));
        Assert.True(applied);
        Assert.True(eligibleState.ThemeMachineSnapshot!.ReturnBeatCompleted);

        var ineligibleState = new AdaptiveScenarioState
        {
            ThemeMachineSnapshot = new ThemeMachineSessionSnapshot
            {
                CurrentStateCode = "PublicBaseline",
                ReturnBeatCompleted = false
            }
        };

        var notApplied = Assert.IsType<bool>(method.Invoke(null, [ineligibleState]));
        Assert.False(notApplied);
        Assert.False(ineligibleState.ThemeMachineSnapshot!.ReturnBeatCompleted);
    }

    [Fact]
    public void ResolveReturnBeatCompletionSignals_ReadsConfiguredCooldownSignals()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatCompletionSignals",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var transitions = new List<RPThemeMachineTransition>
        {
            new()
            {
                TransitionId = "cooldown-1",
                FromStateCode = "ReintegrationCooldown",
                ToStateCode = "NextDisappearanceEligible",
                TriggerType = "cooldown-eligibility",
                GateConfigJson = JsonSerializer.Serialize(new
                {
                    minimumInteractions = 3,
                    requireReturnBeatCompleted = true,
                    returnBeatCompletionSignals = new[] { "returned safely", "back at home" },
                    returnBeatTransgressorRole = "Wife",
                    returnBeatPartnerRole = "Husband"
                }),
                BlockReasonCode = "ReintegrationCooldownGateBlocked",
                IsEnabled = true
            }
        };

        var result = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(null, ["session-1", "ReintegrationCooldown", transitions]));
        Assert.Equal(2, result.Count);
        Assert.Contains("returned safely", result, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("back at home", result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveReturnBeatCompletionSignals_ThrowsWhenRequiredSignalsMissing()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatCompletionSignals",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var transitions = new List<RPThemeMachineTransition>
        {
            new()
            {
                TransitionId = "cooldown-1",
                FromStateCode = "ReintegrationCooldown",
                ToStateCode = "NextDisappearanceEligible",
                TriggerType = "cooldown-eligibility",
                GateConfigJson = JsonSerializer.Serialize(new
                {
                    minimumInteractions = 3,
                    requireReturnBeatCompleted = true,
                    returnBeatTransgressorRole = "Wife",
                    returnBeatPartnerRole = "Husband"
                }),
                BlockReasonCode = "ReintegrationCooldownGateBlocked",
                IsEnabled = true
            }
        };

        var ex = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, ["session-1", "ReintegrationCooldown", transitions]));

        Assert.NotNull(ex.InnerException);
        Assert.Contains("returnBeatCompletionSignals", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveReturnBeatDetectionConfig_ReadsConfiguredDynamicRolePair()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatDetectionConfig",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var transitions = new List<RPThemeMachineTransition>
        {
            new()
            {
                TransitionId = "cooldown-1",
                FromStateCode = "ReintegrationCooldown",
                ToStateCode = "NextDisappearanceEligible",
                TriggerType = "cooldown-eligibility",
                GateConfigJson = JsonSerializer.Serialize(new
                {
                    minimumInteractions = 3,
                    requireReturnBeatCompleted = true,
                    returnBeatCompletionSignals = new[] { "returned safely" },
                    returnBeatTransgressorRole = "Girlfriend",
                    returnBeatPartnerRole = "Boyfriend"
                }),
                BlockReasonCode = "ReintegrationCooldownGateBlocked",
                IsEnabled = true
            }
        };

        var result = method!.Invoke(null, ["session-1", "ReintegrationCooldown", transitions]);
        Assert.NotNull(result);

        var resultType = result!.GetType();
        var transgressorRole = Assert.IsType<string>(resultType.GetProperty("TransgressorRoleName")!.GetValue(result));
        var partnerRole = Assert.IsType<string>(resultType.GetProperty("PartnerRoleName")!.GetValue(result));

        Assert.Equal("Girlfriend", transgressorRole);
        Assert.Equal("Boyfriend", partnerRole);
    }

    [Fact]
    public void ResolveReturnBeatDetectionConfig_ThrowsWhenRolePairMissing()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatDetectionConfig",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var transitions = new List<RPThemeMachineTransition>
        {
            new()
            {
                TransitionId = "cooldown-1",
                FromStateCode = "ReintegrationCooldown",
                ToStateCode = "NextDisappearanceEligible",
                TriggerType = "cooldown-eligibility",
                GateConfigJson = JsonSerializer.Serialize(new
                {
                    minimumInteractions = 3,
                    requireReturnBeatCompleted = true,
                    returnBeatCompletionSignals = new[] { "returned safely" }
                }),
                BlockReasonCode = "ReintegrationCooldownGateBlocked",
                IsEnabled = true
            }
        };

        var ex = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, ["session-1", "ReintegrationCooldown", transitions]));

        Assert.NotNull(ex.InnerException);
        Assert.Contains("returnBeatTransgressorRole", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyConfiguredReturnBeatCompletion_AppliesFromRoleDialogue()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "TryApplyConfiguredReturnBeatCompletion",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction
                {
                    Id = "interaction-1",
                    InteractionType = InteractionType.Npc,
                    ActorName = "Wife",
                    Content = "I'm back in the room.",
                    CreatedAt = DateTime.UtcNow.AddSeconds(-5)
                },
                new RolePlayInteraction
                {
                    Id = "interaction-2",
                    InteractionType = InteractionType.Npc,
                    ActorName = "Husband",
                    Content = "I can see you're here now.",
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };

        var state = new AdaptiveScenarioState
        {
            ThemeMachineSnapshot = new ThemeMachineSessionSnapshot
            {
                CurrentStateCode = "ReintegrationCooldown",
                ReturnBeatCompleted = false
            }
        };

        var wifeTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wife" };
        var husbandTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Husband" };

        var resultObject = method!.Invoke(null,
        [
            session,
            state,
            new List<string> { "returned safely" },
            wifeTargets,
            husbandTargets
        ]);
        Assert.NotNull(resultObject);

        var result = ((bool Applied, string? MatchedSignal, string? SourceInteractionId))resultObject!;
        Assert.True(result.Applied);
        Assert.Equal("transgressor-partner-direct-dialogue", result.MatchedSignal);
        Assert.Equal("interaction-2", result.SourceInteractionId);
        Assert.True(state.ThemeMachineSnapshot.ReturnBeatCompleted);
    }

    [Fact]
    public void TryApplyConfiguredReturnBeatCompletion_AppliesFromSameSceneAndAcknowledgementSignal()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "TryApplyConfiguredReturnBeatCompletion",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction
                {
                    Id = "interaction-1",
                    InteractionType = InteractionType.Npc,
                    ActorName = "Wife",
                    Content = "She stepped back into the living room.",
                    CreatedAt = DateTime.UtcNow.AddSeconds(-10)
                },
                new RolePlayInteraction
                {
                    Id = "interaction-2",
                    InteractionType = InteractionType.Custom,
                    ActorName = "Narrator",
                    Content = "The air stayed tense for a moment.",
                    CreatedAt = DateTime.UtcNow.AddSeconds(-5)
                },
                new RolePlayInteraction
                {
                    Id = "interaction-3",
                    InteractionType = InteractionType.Npc,
                    ActorName = "Husband",
                    Content = "I see you returned safely.",
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };

        var state = new AdaptiveScenarioState
        {
            CurrentSceneLocation = "living-room",
            CharacterLocations =
            [
                new CharacterLocationState { CharacterId = "Wife", TrueLocation = "living-room" },
                new CharacterLocationState { CharacterId = "Husband", TrueLocation = "living-room" }
            ],
            CharacterLocationPerceptions =
            [
                new CharacterLocationPerceptionState
                {
                    ObserverCharacterId = "Husband",
                    TargetCharacterId = "Wife",
                    IsInProximity = true,
                    HasLineOfSight = true,
                    Confidence = 100,
                    KnowledgeSource = "scene"
                }
            ],
            ThemeMachineSnapshot = new ThemeMachineSessionSnapshot
            {
                CurrentStateCode = "ReintegrationCooldown",
                ReturnBeatCompleted = false
            }
        };

        var wifeTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wife" };
        var husbandTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Husband" };

        var resultObject = method!.Invoke(null,
        [
            session,
            state,
            new List<string> { "returned safely" },
            wifeTargets,
            husbandTargets
        ]);
        Assert.NotNull(resultObject);

        var result = ((bool Applied, string? MatchedSignal, string? SourceInteractionId))resultObject!;
        Assert.True(result.Applied);
        Assert.Equal("returned safely", result.MatchedSignal);
        Assert.Equal("interaction-3", result.SourceInteractionId);
        Assert.True(state.ThemeMachineSnapshot.ReturnBeatCompleted);
    }

    [Fact]
    public void TryApplyConfiguredReturnBeatCompletion_DoesNotApplyFromNarratorKeywordWithoutRoleEvidence()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "TryApplyConfiguredReturnBeatCompletion",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction
                {
                    Id = "interaction-1",
                    InteractionType = InteractionType.Custom,
                    ActorName = "Narrator",
                    Content = "He returned safely to the house and the scene stabilized.",
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };

        var state = new AdaptiveScenarioState
        {
            ThemeMachineSnapshot = new ThemeMachineSessionSnapshot
            {
                CurrentStateCode = "ReintegrationCooldown",
                ReturnBeatCompleted = false
            }
        };

        var wifeTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wife" };
        var husbandTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Husband" };

        var resultObject = method!.Invoke(null,
        [
            session,
            state,
            new List<string> { "returned safely" },
            wifeTargets,
            husbandTargets
        ]);
        Assert.NotNull(resultObject);

        var result = ((bool Applied, string? MatchedSignal, string? SourceInteractionId))resultObject!;
        Assert.False(result.Applied);
        Assert.Null(result.MatchedSignal);
        Assert.Null(result.SourceInteractionId);
        Assert.False(state.ThemeMachineSnapshot.ReturnBeatCompleted);
    }

    [Fact]
    public void ResolveReturnBeatRoleBindingsFromEvaluations_ReadsLatestActiveScenarioBindings()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatRoleBindingsFromEvaluations",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var evaluations = new List<ScenarioCandidateEvaluation>
        {
            new()
            {
                SessionId = "session-1",
                EvaluationId = "older",
                ScenarioId = "infidelity-brief-disappearance",
                EvaluatedUtc = DateTime.UtcNow.AddMinutes(-2),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    fitResult = new
                    {
                        roleCharacterBindings = new Dictionary<string, string>
                        {
                            [CharacterRoleCatalog.Wife] = "wife-old",
                            [CharacterRoleCatalog.Husband] = "husband-old"
                        }
                    }
                })
            },
            new()
            {
                SessionId = "session-1",
                EvaluationId = "latest",
                ScenarioId = "infidelity-brief-disappearance",
                EvaluatedUtc = DateTime.UtcNow,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    fitResult = new
                    {
                        roleCharacterBindings = new Dictionary<string, string>
                        {
                            [CharacterRoleCatalog.Wife] = "wife-char",
                            [CharacterRoleCatalog.Husband] = "Ken"
                        }
                    }
                })
            }
        };

        var resultObject = method!.Invoke(null,
        [
            "session-1",
            "infidelity-brief-disappearance",
            CharacterRoleCatalog.Wife,
            CharacterRoleCatalog.Husband,
            evaluations
        ]);
        Assert.NotNull(resultObject);

        var result = ((string TransgressorActorId, string PartnerActorId))resultObject!;
        Assert.Equal("wife-char", result.TransgressorActorId);
        Assert.Equal("Ken", result.PartnerActorId);
    }

    [Fact]
    public void ResolveReturnBeatRoleBindingsFromEvaluations_ThrowsWhenRequiredRoleMissing()
    {
        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatRoleBindingsFromEvaluations",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var evaluations = new List<ScenarioCandidateEvaluation>
        {
            new()
            {
                SessionId = "session-1",
                EvaluationId = "latest",
                ScenarioId = "infidelity-brief-disappearance",
                EvaluatedUtc = DateTime.UtcNow,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    fitResult = new
                    {
                        roleCharacterBindings = new Dictionary<string, string>
                        {
                            [CharacterRoleCatalog.Wife] = "wife-char"
                        }
                    }
                })
            }
        };

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null,
        [
            "session-1",
            "infidelity-brief-disappearance",
            CharacterRoleCatalog.Wife,
            CharacterRoleCatalog.Husband,
            evaluations
        ]));

        Assert.NotNull(ex.InnerException);
        Assert.Contains("roleCharacterBindings", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveReturnBeatRoleActorTargetsAsync_UsesCandidateBindings_WhenScenarioCannotBeLoaded()
    {
        var repository = new CandidateOnlyStateRepository(
        [
            new ScenarioCandidateEvaluation
            {
                SessionId = "session-1",
                EvaluationId = "latest",
                ScenarioId = "infidelity-brief-disappearance",
                EvaluatedUtc = DateTime.UtcNow,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    fitResult = new
                    {
                        roleCharacterBindings = new Dictionary<string, string>
                        {
                            [CharacterRoleCatalog.Wife] = "wife-char",
                            [CharacterRoleCatalog.Husband] = "Ken"
                        }
                    }
                })
            }
        ]);

        var service = RolePlayTestFactory.CreateEngineService(
            scenarioService: new RolePlayTestFactory.NullScenarioService(),
            stateRepository: repository);

        var method = typeof(RolePlayEngineService).GetMethod(
            "ResolveReturnBeatRoleActorTargetsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var session = new RolePlaySession
        {
            Id = "session-1",
            ScenarioId = "stale-session-scenario",
            PersonaName = "Ken",
            CharacterPerspectives =
            [
                new RolePlayCharacterPerspective { CharacterId = "wife-char", CharacterName = "Becky" },
                new RolePlayCharacterPerspective { CharacterId = "Ken", CharacterName = "Ken" }
            ]
        };
        session.AdaptiveState.CharacterStats["wife-char"] = new CharacterStatProfileV2 { CharacterId = "wife-char" };
        session.AdaptiveState.CharacterStats["Ken"] = new CharacterStatProfileV2 { CharacterId = "Ken" };

        var state = new AdaptiveScenarioState
        {
            ActiveScenarioId = "infidelity-brief-disappearance",
            CharacterSnapshots =
            [
                new CharacterStatProfileV2 { CharacterId = "wife-char" },
                new CharacterStatProfileV2 { CharacterId = "Ken" }
            ]
        };

        var invokeResult = method!.Invoke(service,
        [
            session,
            state,
            CharacterRoleCatalog.Wife,
            CharacterRoleCatalog.Husband,
            CancellationToken.None
        ]);

        var task = Assert.IsType<Task<(IReadOnlySet<string>, IReadOnlySet<string>)>>(invokeResult);
        var (transgressorTargets, partnerTargets) = await task;

        Assert.Contains("wife-char", transgressorTargets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Ken", partnerTargets, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CandidateOnlyStateRepository(IReadOnlyList<ScenarioCandidateEvaluation> evaluations) : IRolePlayStateRepository
    {
        public Task<RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveAdaptiveStateSemanticFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => Task.FromResult(evaluations);

        public Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);
    }
}
