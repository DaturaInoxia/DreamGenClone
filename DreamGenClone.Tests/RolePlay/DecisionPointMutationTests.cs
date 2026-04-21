using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class DecisionPointMutationTests
{
    private readonly DecisionPointService _service = new(NullLogger<DecisionPointService>.Instance);

    [Fact]
    public async Task TriggeredDecisionPoint_GeneratesOptions()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(70, 40, 60);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.InteractionStart);

        Assert.NotNull(point);
        Assert.NotEmpty(point!.OptionIds);
        Assert.Contains("custom", point.OptionIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpPhase_UsesHiddenTransparency()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(45, 72, 40);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;
        state.CurrentPhase = NarrativePhase.BuildUp;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.InteractionStart);

        Assert.NotNull(point);
        Assert.Equal(TransparencyMode.Hidden, point!.TransparencyMode);
    }

    [Fact]
    public async Task HighDesireContext_ChangesOptionSet()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(78, 35, 75);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;
        state.CurrentPhase = NarrativePhase.Approaching;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.SignificantStatChange);

        Assert.NotNull(point);
        Assert.Contains("test-boundary", point!.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("observe", point.OptionIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowDesireContext_FiltersOutLeanInByPrerequisite()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.Committed,
            InteractionCountInPhase = 3,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "wife",
                    Desire = 40,
                    Restraint = 60,
                    Tension = 45,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                }
            ]
        };

        var point = await _service.TryCreateDecisionPointAsync(
            state,
            DecisionTrigger.SignificantStatChange,
            new DecisionGenerationContext
            {
                Phase = state.CurrentPhase,
                Who = "coworker",
                What = "invitation",
                TargetActorId = "wife",
                RelevantActors = state.CharacterSnapshots
            });

        Assert.NotNull(point);
        Assert.DoesNotContain("lean-in", point!.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("custom", point.OptionIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectQuestionTrigger_CreatesDecisionPoint_WithoutActiveScenario()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = null,
            CurrentPhase = NarrativePhase.BuildUp,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "becky",
                    Desire = 55,
                    Restraint = 50,
                    Tension = 45,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                },
                new CharacterStatProfile
                {
                    CharacterId = "ken",
                    Desire = 50,
                    Restraint = 52,
                    Tension = 40,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                }
            ]
        };

        var point = await _service.TryCreateDecisionPointAsync(
            state,
            DecisionTrigger.CharacterDirectQuestion,
            new DecisionGenerationContext
            {
                ScenarioId = "party-night",
                Phase = state.CurrentPhase,
                PromptSnippet = "Dance with me?",
                AskingActorName = "becky",
                TargetActorId = "ken",
                IsDirectQuestionContext = true,
                RelevantActors = state.CharacterSnapshots
            });

        Assert.NotNull(point);
        Assert.Equal("party-night", point!.ScenarioId);
        Assert.Equal(DecisionTrigger.CharacterDirectQuestion.ToString(), point.TriggerSource);
        Assert.Equal("ken", point.TargetActorId);
        Assert.NotEmpty(point.OptionIds);
        Assert.Contains("tempt-answer", point.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("lean-in", point.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hold-back", point.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("observe", point.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.True(
            point.OptionIds.Contains("seek-connection", StringComparer.OrdinalIgnoreCase)
            || point.OptionIds.Contains("redirect", StringComparer.OrdinalIgnoreCase));
        Assert.Contains("custom", point.OptionIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemptAnswerDecision_IncreasesDesire_AndReducesLoyalty()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.BuildUp,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "becky",
                    Desire = 54,
                    Restraint = 70,
                    Tension = 51,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 68,
                    SelfRespect = 52
                }
            ]
        };

        var outcome = await _service.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = "dp-direct-tempt-1",
                OptionId = "tempt-answer",
                ActorName = "dean",
                TargetActorId = "becky"
            },
            targetActorId: "becky");

        Assert.True(outcome.Applied);
        Assert.Equal("becky", outcome.TargetActorId);
        Assert.True(outcome.AppliedStatDeltas.TryGetValue("Desire", out var desireDelta));
        Assert.True(desireDelta > 0);
        Assert.True(outcome.AppliedStatDeltas.TryGetValue("Loyalty", out var loyaltyDelta));
        Assert.True(loyaltyDelta < 0);
        Assert.True(state.CharacterSnapshots[0].Desire > 54);
        Assert.True(state.CharacterSnapshots[0].Loyalty < 68);
    }

    [Fact]
    public async Task ApplyDecision_PersistsStatDeltaMutations()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(50, 50, 50);
        state.ActiveScenarioId = "scenario-1";

        var outcome = await _service.ApplyDecisionAsync(state, new DecisionSubmission
        {
            DecisionPointId = "dp-1",
            OptionId = "lean-in",
            ActorName = "Tester"
        });

        Assert.True(outcome.Applied);
        Assert.True(state.CharacterSnapshots[0].Desire > 50);
        Assert.True(state.CharacterSnapshots[0].Restraint < 50);
    }

    [Fact]
    public async Task CustomResponseFallback_ParsesDeltaMap()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(50, 50, 50);
        state.ActiveScenarioId = "scenario-1";

        var outcome = await _service.ApplyDecisionAsync(state, new DecisionSubmission
        {
            DecisionPointId = "dp-2",
            OptionId = "custom",
            CustomResponseText = "Connection:4,SelfRespect:2",
            ActorName = "Tester"
        });

        Assert.Equal(4, outcome.AppliedStatDeltas["Connection"]);
        Assert.Equal(2, outcome.AppliedStatDeltas["SelfRespect"]);
    }

    [Fact]
    public async Task TargetActor_OnlyMutatesTargetSnapshot()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.Committed,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "wife",
                    Desire = 50,
                    Restraint = 50,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                },
                new CharacterStatProfile
                {
                    CharacterId = "husband",
                    Desire = 50,
                    Restraint = 50,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                }
            ]
        };

        var outcome = await _service.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = "dp-3",
                OptionId = "lean-in",
                ActorName = "wife",
                TargetActorId = "wife"
            },
            targetActorId: "wife");

        Assert.True(outcome.Applied);
        Assert.Equal("wife", outcome.TargetActorId);
        Assert.True(state.CharacterSnapshots[0].Desire > 50);
        Assert.Equal(50, state.CharacterSnapshots[1].Desire);
    }

    [Fact]
    public async Task MultiActorOption_AppliesPerActorDeltas()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.Climax,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "wife",
                    Desire = 60,
                    Restraint = 50,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 60,
                    SelfRespect = 50
                },
                new CharacterStatProfile
                {
                    CharacterId = "husband",
                    Desire = 40,
                    Restraint = 55,
                    Tension = 35,
                    Connection = 55,
                    Dominance = 45,
                    Loyalty = 70,
                    SelfRespect = 50
                }
            ]
        };

        var outcome = await _service.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = "dp-4",
                OptionId = "husband-observes",
                ActorName = "wife",
                TargetActorId = "husband"
            },
            targetActorId: "husband");

        Assert.True(outcome.Applied);
        Assert.True(outcome.PerActorStatDeltas.Count >= 2);
        Assert.True(state.CharacterSnapshots[0].Desire > 60);
        Assert.True(state.CharacterSnapshots[1].Tension > 35);
    }

    [Fact]
    public async Task TransparencyOverride_UsesContextOverride()
    {
        var state = RolePlayAcceptanceFixtureData.BuildBoundaryState(70, 45, 55);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;
        state.CurrentPhase = NarrativePhase.BuildUp;

        var point = await _service.TryCreateDecisionPointAsync(
            state,
            DecisionTrigger.InteractionStart,
            new DecisionGenerationContext
            {
                Phase = state.CurrentPhase,
                TransparencyOverride = TransparencyMode.Explicit,
                RelevantActors = state.CharacterSnapshots
            });

        Assert.NotNull(point);
        Assert.Equal(TransparencyMode.Explicit, point!.TransparencyMode);
    }

    [Fact]
    public async Task EscalationDecision_AppliesHighRestraintDrop()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.Approaching,
            CharacterSnapshots =
            [
                new CharacterStatProfile
                {
                    CharacterId = "becky",
                    Desire = 72,
                    Restraint = 76,
                    Tension = 64,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 52,
                    SelfRespect = 50
                },
                new CharacterStatProfile
                {
                    CharacterId = "alex",
                    Desire = 55,
                    Restraint = 58,
                    Tension = 51,
                    Connection = 48,
                    Dominance = 49,
                    Loyalty = 56,
                    SelfRespect = 52
                }
            ]
        };

        var outcome = await _service.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = "dp-5",
                OptionId = "escalate",
                ActorName = "becky",
                TargetActorId = "becky"
            },
            targetActorId: "becky");

        Assert.True(outcome.Applied);
        Assert.True(outcome.AppliedStatDeltas.TryGetValue("Restraint", out var restraintDelta));
        Assert.True(restraintDelta <= -30);
        Assert.True(state.CharacterSnapshots[0].Restraint <= 49);
    }
}
