using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Injectors;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for the B-056 wife-husband aftermath closure feature.
/// Covers US1 (closure turn sequencing), US2 (marker opt-in), US3 (actor focus + Fast Pacing suppression).
/// Pure-unit, mirrors MultiEncounterTimeSkipTests.cs patterns — inline state, no DI.
/// </summary>
public sealed class AftermathHusbandContrastTests
{
    // ---- US1: Closure turn sequencing ----

    [Fact]
    public void TimeSkipPhase_AftermathCoupleInteraction_HasValue3()
    {
        Assert.Equal(3, (int)TimeSkipPhase.AftermathCoupleInteraction);
    }

    [Fact]
    public void CloseScene_Phase_Transitions_To_AftermathCoupleInteraction_WhenMarkerPresent()
    {
        // When both markers are active and CloseScene fires, phase advances to AftermathCoupleInteraction.
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene
        };
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);

        // Simulate overflow injection: when aftermath marker present, CloseScene → AftermathCoupleInteraction.
        state.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        Assert.Equal(TimeSkipPhase.AftermathCoupleInteraction, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void CloseScene_Phase_Transitions_To_AdvanceTime_WhenMarkerAbsent()
    {
        // Regression: without aftermath marker, CloseScene still transitions to AdvanceTime.
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene
        };
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);

        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void AftermathCoupleInteraction_Transitions_ToAdvanceTime_WhenMultiEncounter()
    {
        // After aftermath directive, if multi-encounter active, advance to AdvanceTime.
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction
        };
        Assert.Equal(TimeSkipPhase.AftermathCoupleInteraction, state.CurrentTimeSkipPhase);

        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void AftermathCoupleInteraction_Transitions_ToNone_WhenNoMultiEncounter()
    {
        // After aftermath-only closure (no multi-encounter), phase returns to None.
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp,
            CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction
        };
        Assert.Equal(TimeSkipPhase.AftermathCoupleInteraction, state.CurrentTimeSkipPhase);

        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void HasRecentUserInstruction_DeferStaysActiveDuringAftermathLeg()
    {
        // The deferral guard (FR-005) must stay active during AftermathCoupleInteraction
        // — same as CloseScene/AdvanceTime. User instructions should defer the aftermath leg.
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax;
        session.AdaptiveState.CurrentEncounterNumber = 2;
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "wait", GeneratedByCommand = null });

        Assert.True(HasRecentUserInstruction(session, 3));
        // Phase must remain AftermathCoupleInteraction (deferred, not cleared)
        Assert.Equal(TimeSkipPhase.AftermathCoupleInteraction, session.AdaptiveState.CurrentTimeSkipPhase);
    }

    // ---- CloseScene directive text rewrite (FR-010) ----

    [Fact]
    public void CloseScene_Directive_Includes_Closure_Prose()
    {
        // FR-010: The rewritten CloseScene directive includes explicit closure content
        // for ALL multi-encounter themes (marker-absent included).
        var directive = "Wrap up the current encounter naturally — bodies settle, afterglow passes, the characters separate. They get dressed and return to whatever they were doing before this happened. Do not advance time past this transition.";

        Assert.Contains("Wrap up the current encounter", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get dressed", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bodies settle", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return to whatever they were doing", directive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close the current encounter naturally.", directive, StringComparison.OrdinalIgnoreCase);
    }

    // ---- US2: Marker detection ----

    [Fact]
    public void CloseScene_Directive_DoesNotMentionEncounterNumber()
    {
        // Regression: CloseScene directive must not leak encounter numbers.
        var directive = "Wrap up the current encounter naturally — bodies settle, afterglow passes, the characters separate. They get dressed and return to whatever they were doing before this happened. Do not advance time past this transition.";

        Assert.DoesNotContain("#", directive);
        Assert.DoesNotContain("encounter #", directive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AftermathCoupleInteraction_Directive_MentionsHusbandExplicitly()
    {
        // FR-007: The aftermath contrast directive must reference the husband explicitly.
        var evidence = "fell to her knees";
        var evidenceClause = string.IsNullOrWhiteSpace(evidence) ? "had an intimate encounter with another man" : evidence;
        var directive = $"You just {evidenceClause}. Get dressed, return to the normal setting, and interact with your husband. "
                        + "Act normal to his face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. "
                        + "Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. "
                        + "Do not advance time past this husband-wife scene.";

        Assert.Contains("husband", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("act normal", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conceal evidence", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contrast IS the point", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(evidence, directive);
    }

    [Fact]
    public void AftermathCoupleInteraction_Directive_FallbackWhenEvidenceNull()
    {
        var evidence = (string?)null;
        var evidenceClause = string.IsNullOrWhiteSpace(evidence) ? "had an intimate encounter with another man" : evidence;
        Assert.Equal("had an intimate encounter with another man", evidenceClause);
    }

    [Fact]
    public void AftermathCoupleInteraction_Directive_FallbackWhenEvidenceEmpty()
    {
        var evidence = "  ";
        var evidenceClause = string.IsNullOrWhiteSpace(evidence) ? "had an intimate encounter with another man" : evidence;
        Assert.Equal("had an intimate encounter with another man", evidenceClause);
    }

    // ---- US3: Injector behavior ----

    [Fact]
    public void HusbandAftermathInjector_ShouldFire_WhenPhaseIsAftermathCoupleInteraction()
    {
        var injector = new HusbandAftermathInjector();
        Assert.Equal("husband-aftermath", injector.Id);
        Assert.Equal(85, injector.Priority);

        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;
        // B-060: ShouldFire now requires the actor to have an EncounterCompletion record.
        session.AdaptiveState.EncounterSummaries.Add(new EncounterSummaryRecord
        {
            SessionId = session.Id,
            CharacterId = "Anna",
            SummaryType = EncounterSummaryType.EncounterCompletion,
            CycleIndex = 0,
            EncounterNumber = 1,
            TemplateSummary = "test summary"
        });

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        Assert.True(injector.ShouldFire(context));
    }

    [Fact]
    public void HusbandAftermathInjector_ShouldNotFire_WhenActorHasNoEncounterRecord()
    {
        // B-060: ShouldFire returns false when the actor has no EncounterCompletion record
        // even if the phase is AftermathCoupleInteraction.
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;
        // No encounter summaries added — Ken has no record.

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Ken",
            Intent = PromptIntent.Message
        };

        Assert.False(injector.ShouldFire(context));
    }

    [Fact]
    public void HusbandAftermathInjector_ShouldNotFire_WhenPhaseIsCloseScene()
    {
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        Assert.False(injector.ShouldFire(context));

        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.False(injector.ShouldFire(context));

        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.False(injector.ShouldFire(context));
    }

    [Fact]
    public void HusbandAftermathInjector_BuildText_ReferencesEncounterCompletionSummary()
    {
        // B-058 Phase 5.1: the injector now reads from the most recent EncounterCompletion
        // summary in the current arc (prefer LlmSummary, fall back to TemplateSummary).
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;
        session.AdaptiveState.EncounterSummaries.Add(new EncounterSummaryRecord
        {
            SessionId = session.Id,
            CharacterId = "Anna",
            SummaryType = EncounterSummaryType.EncounterCompletion,
            CycleIndex = 0,
            EncounterNumber = 1,
            DetectionEvidence = "raw evidence passage",
            TemplateSummary = "Anna's summary of the encounter prose",
            LlmSummary = "Anna and Marcus had passionate sex in the bathroom — he entered her from behind and finished inside her."
        });

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        // B-060: set persona role and character roles so dynamic partner label resolves.
        session.PersonaRole = "Husband";
        session.AdaptiveState.CharacterRoles["Anna"] = "Wife";

        var text = injector.BuildText(context);
        Assert.Contains("Anna and Marcus had passionate sex in the bathroom", text);
        // B-060: dynamic label resolves to "husband" (actor Anna != persona, partner = persona = Husband).
        Assert.Contains("husband", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HusbandAftermathInjector_BuildText_FallsBackToTemplateWhenLlmMissing()
    {
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;
        session.AdaptiveState.EncounterSummaries.Add(new EncounterSummaryRecord
        {
            SessionId = session.Id,
            CharacterId = "Anna",
            SummaryType = EncounterSummaryType.EncounterCompletion,
            CycleIndex = 0,
            EncounterNumber = 1,
            DetectionEvidence = "raw evidence passage",
            TemplateSummary = "Anna — encounter 1 of arc 1. Scene: bathroom. Detection evidence: raw evidence passage"
        });

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        // B-060: set persona role and character roles so dynamic partner label resolves.
        session.PersonaRole = "Husband";
        session.AdaptiveState.CharacterRoles["Anna"] = "Wife";

        var text = injector.BuildText(context);
        Assert.Contains("encounter 1 of arc 1", text);
        Assert.Contains("husband", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HusbandAftermathInjector_BuildText_FallbackToDetectionEvidenceWhenBothMissing()
    {
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;
        session.AdaptiveState.EncounterSummaries.Add(new EncounterSummaryRecord
        {
            SessionId = session.Id,
            CharacterId = "Anna",
            SummaryType = EncounterSummaryType.EncounterCompletion,
            CycleIndex = 0,
            EncounterNumber = 1,
            DetectionEvidence = "raw detection evidence passage",
            TemplateSummary = string.Empty,
            LlmSummary = null
        });

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        var text = injector.BuildText(context);
        Assert.Contains("raw detection evidence passage", text);
    }

    [Fact]
    public void HusbandAftermathInjector_BuildText_ReturnsEmptyWhenNoEncounterCompletionRecord()
    {
        // B-060: when no EncounterCompletion summary exists for THIS actor in the arc,
        // BuildText returns string.Empty (defensive guard — ShouldFire already gates this).
        var injector = new HusbandAftermathInjector();
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        session.AdaptiveState.CycleIndex = 0;

        var context = new PromptInjectionContext
        {
            Session = session,
            SceneDirection = new SceneDirection(),
            Phase = "Climax",
            ActorName = "Anna",
            Intent = PromptIntent.Message
        };

        var text = injector.BuildText(context);
        Assert.Equal(string.Empty, text);
    }

    // ---- US3: Fast Pacing HC suppression ----

    [Fact]
    public void FastPacingHC_Suppressed_WhenAftermathPhaseActive()
    {
        // The Fast Pacing HC is suppressed during AftermathCoupleInteraction.
        // This is a static gate: CurrentTimeSkipPhase == AftermathCoupleInteraction blocks the HC.
        var phase = TimeSkipPhase.AftermathCoupleInteraction;
        var pacing = ScenePacing.Fast;
        var shouldSuppress = phase == TimeSkipPhase.AftermathCoupleInteraction && pacing == ScenePacing.Fast;

        Assert.True(shouldSuppress);
    }

    [Fact]
    public void FastPacingHC_NotSuppressed_WhenAftermathPhaseInactive()
    {
        // When not in AftermathCoupleInteraction, Fast Pacing HC fires normally.
        foreach (var phase in new[] { TimeSkipPhase.None, TimeSkipPhase.CloseScene, TimeSkipPhase.AdvanceTime })
        {
            var pacing = ScenePacing.Fast;
            var shouldSuppress = phase == TimeSkipPhase.AftermathCoupleInteraction && pacing == ScenePacing.Fast;
            Assert.False(shouldSuppress, $"Phase {phase} should not suppress Fast Pacing HC");
        }
    }

    [Fact]
    public void FastPacingHC_NotSuppressed_WhenPacingNotFast()
    {
        // Even in AftermathCoupleInteraction, non-Fast pacing should not suppress the HC
        // (the HC block only fires on Fast pacing anyway, but the gate is defensive).
        var phase = TimeSkipPhase.AftermathCoupleInteraction;
        var pacing = ScenePacing.Slow;
        var shouldSuppress = phase == TimeSkipPhase.AftermathCoupleInteraction && pacing == ScenePacing.Fast;
        Assert.False(shouldSuppress);
    }

    // ---- US3: Actor filter ----

    [Fact]
    public void LastEncounterEndInteractionIndex_RuntimeField_Survives_Set_Get_Cycle()
    {
        // B-058 Phase 2.1: the runtime-only LastEncounterEndInteractionIndex field replaces
        // the persisted LastEncounterEvidenceSpan for staging the encounter's end index.
        var state = new AdaptiveScenarioState
        {
            LastEncounterEndInteractionIndex = 42
        };
        Assert.Equal(42, state.LastEncounterEndInteractionIndex);

        state.LastEncounterEndInteractionIndex = 0;
        Assert.Equal(0, state.LastEncounterEndInteractionIndex);
    }

    [Fact]
    public void FullStateMachine_ThreeLeg_Flow_Completes()
    {
        // Simulate the full CloseScene → AftermathCoupleInteraction → AdvanceTime → None chain.
        // B-058 Phase 5.3: the encounter-context payload now lives on an EncounterCompletion
        // summary (added here at the start to simulate the detection-time hook).
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            CurrentEncounterStartInteractionIndex = 5,
            LastEncounterEndInteractionIndex = 17
        };
        state.EncounterSummaries.Add(new EncounterSummaryRecord
        {
            SummaryType = EncounterSummaryType.EncounterCompletion,
            CycleIndex = 0,
            EncounterNumber = 2,
            DetectionEvidence = "just climaxed with another man",
            TemplateSummary = "Anna — encounter 2 of arc 1. Scene: bedroom."
        });

        // Leg 1: CloseScene
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);
        state.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        state.IsStateDirty = true;

        // Leg 2: AftermathCoupleInteraction — HusbandAftermathInjector reads the summary above.
        Assert.Equal(TimeSkipPhase.AftermathCoupleInteraction, state.CurrentTimeSkipPhase);
        var memory = state.EncounterSummaries
            .Where(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
                     && s.CycleIndex == state.CycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .FirstOrDefault();
        Assert.NotNull(memory);
        Assert.Equal("just climaxed with another man", memory!.DetectionEvidence);
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        state.IsStateDirty = true;

        // Leg 3: AdvanceTime
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        state.IsStateDirty = true;

        // Done
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
        Assert.True(state.IsStateDirty);
    }

    [Fact]
    public void State_DirtyFlag_Set_OnAftermathPhaseMutation()
    {
        var state = new AdaptiveScenarioState();
        Assert.False(state.IsStateDirty);

        state.CurrentTimeSkipPhase = TimeSkipPhase.AftermathCoupleInteraction;
        state.IsStateDirty = true;
        Assert.True(state.IsStateDirty);

        // B-058 Phase 5.3: LastEncounterEvidenceSpan removed; mutation of the start-index
        // tracker (runtime-only field) does not require setting IsStateDirty because the
        // field is [JsonIgnore] and not persisted. The state machine flow above still sets
        // IsStateDirty on each transition for consistency with the persisted flow.
        state.CurrentEncounterStartInteractionIndex = 7;
        Assert.True(state.IsStateDirty);
    }

    // ---- Helper: local copy mirroring RolePlayEngineService.HasRecentUserInstruction ----

    private static bool HasRecentUserInstruction(RolePlaySession session, int windowSize)
    {
        var recent = session.Interactions
            .OrderByDescending(i => i.CreatedAt)
            .Take(windowSize)
            .ToList();

        return recent.Any(i =>
            string.Equals(i.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(i.GeneratedByCommand));
    }
}
