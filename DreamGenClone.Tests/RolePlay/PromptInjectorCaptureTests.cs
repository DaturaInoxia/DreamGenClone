using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Injectors;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Structural parity and negative-assertion tests for the SceneDirectionCoordinator
/// pipeline. Verifies that the coordinator + 12 injectors produce the expected
/// behavioral directives, that critical strings are present, and that forbidden
/// patterns (contradictory directives, hardcoded phase-branching) are absent.
/// </summary>
public sealed class PromptInjectorCaptureTests
{
    private static SceneDirectionCoordinator CreateCoordinator(params IPromptInjector[] injectors)
    {
        // Provide all 12 injectors in priority order; coordinator sorts them.
        var allInjectors = new IPromptInjector[]
        {
            new TurnContextInjector(),
            new TimeLocationInjector(),
            new BehavioralFrameInjector(),
            new ThemeContractInjector(),
            new ThemeAIGuidanceInjector(),
            new IntensityContractInjector(),
            new EscalationInjector(),
            new SceneTimeDirectionInjector(),
            new PositionListInjector(),
            new BeatStageInjector(),
            new FinalDirectiveInjector()
        };
        var logger = NullLogger<SceneDirectionCoordinator>.Instance;
        return new SceneDirectionCoordinator(allInjectors, logger);
    }

    private static PromptInjectionContext CreateContext(
        string phase = "Climax",
        PromptIntent intent = PromptIntent.Message,
        int? positionInTurn = null,
        int? turnActorCount = null,
        string actorName = "TestActor",
        RPTheme? activeTheme = null,
        SceneDirection? sceneDirection = null)
    {
        sceneDirection ??= SceneDirectionResolver.Resolve(phase, activeTheme, ClimaxSubPhase.None, intent);
        return new PromptInjectionContext
        {
            Session = CreateMockSession(),
            SceneDirection = sceneDirection,
            Phase = phase,
            Intent = intent,
            PositionInTurn = positionInTurn,
            TurnActorCount = turnActorCount,
            ActorName = actorName,
            ActiveTheme = activeTheme,
            PhaseGuidanceLines = activeTheme is not null
                ? RolePlayAssistantPrompts.GetThemePhaseGuidanceLines(activeTheme, phase)
                : [],
            PhaseDirectiveLines = [],
            AiGuidanceNotes = [],
            ThemeHardConstraintLines = []
        };
    }

    private static RolePlaySession CreateMockSession()
    {
        return new RolePlaySession
        {
            Id = "test-session-" + Guid.NewGuid().ToString("N")[..8],
            BehaviorMode = DreamGenClone.Web.Domain.RolePlay.BehaviorMode.TakeTurns,
            MaxThemeAIGuidanceNotes = 5,
            ThemeAIGuidanceInfluencePercent = 80,
            UseThemeAIGuidanceNotesInPrompt = true
        };
    }

    // ── Structural parity: position 1 gets Time Span Reminder ──

    [Fact]
    public void Coordinator_Position1_ContainsTimeSpanReminder()
    {
        var coordinator = CreateCoordinator();
        var context = CreateContext(positionInTurn: 1, turnActorCount: 2);
        var output = coordinator.BuildPrompt(context);

        Assert.Contains("first response this turn", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("establish or shift", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may skip forward in time", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Structural parity: position 2+ gets Location Continuity ──

    [Fact]
    public void Coordinator_Position2_ContainsLocationContinuity()
    {
        var coordinator = CreateCoordinator();
        var context = CreateContext(positionInTurn: 2, turnActorCount: 2);
        var output = coordinator.BuildPrompt(context);

        Assert.Contains("maintain this physical setting", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not silently relocate", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Must-NOT: position 2+ without override markers gets NO Time Span Reminder ──

    [Fact]
    public void Coordinator_Position2_NoOverride_NoContradictoryTimeSpan()
    {
        var coordinator = CreateCoordinator();
        var sceneDirection = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            TimeShift = TimeShiftPolicy.None
        };
        var context = CreateContext(positionInTurn: 2, turnActorCount: 2, sceneDirection: sceneDirection);
        var output = coordinator.BuildPrompt(context);

        // Location Continuity should be present
        Assert.Contains("maintain this physical setting", output, StringComparison.OrdinalIgnoreCase);
        // But position 2 should NOT get the standalone Time Span Reminder
        Assert.DoesNotContain("you may establish or shift the time", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Must-NOT: position 2+ with override markers gets modified time shift ──

    [Fact]
    public void Coordinator_Position2_WithOverride_HasTimeShiftPermission()
    {
        var coordinator = CreateCoordinator();
        var sceneDirection = new SceneDirection
        {
            Pacing = ScenePacing.Fast,
            TimeShift = TimeShiftPolicy.Small
        };
        var context = CreateContext(positionInTurn: 2, turnActorCount: 2, sceneDirection: sceneDirection);
        var output = coordinator.BuildPrompt(context);

        Assert.Contains("maintain this physical setting", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You may also shift time", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Intensity contract ───────────────────────────────────────

    [Fact]
    public void Coordinator_Always_ContainsIntensityContract()
    {
        var coordinator = CreateCoordinator();
        var context = CreateContext();
        var output = coordinator.BuildPrompt(context);

        Assert.Contains("WRITING STYLE", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXPLICITNESS LEVEL", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Turn context ─────────────────────────────────────────────

    [Fact]
    public void Coordinator_Position1_TurnContext_Present()
    {
        var coordinator = CreateCoordinator();
        var context = CreateContext(positionInTurn: 1, turnActorCount: 3);
        var output = coordinator.BuildPrompt(context);

        Assert.Contains("response 1 of 3", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first this turn", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Markerless theme: falls back to phase defaults ───────────

    [Fact]
    public void Coordinator_NoMarkers_ProducesValidOutput()
    {
        var coordinator = CreateCoordinator();
        var context = CreateContext(phase: "BuildUp", activeTheme: null);
        var output = coordinator.BuildPrompt(context);

        // Should produce non-empty output with intensity contract
        Assert.NotEmpty(output);
        Assert.Contains("WRITING STYLE", output, StringComparison.OrdinalIgnoreCase);
        // Should not throw
    }

    // ── Conflicting markers: Deepening overrides pacing ──────────

    [Fact]
    public void Coordinator_DeepeningWithPacing_Position2_GetsDeepening()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance
                {
                    Phase = NarrativePhase.Climax,
                    GuidanceText = "[Pacing:fast] [Deepening:subsequent-actors]"
                }
            ]
        };
        var sceneDirection = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        var coordinator = CreateCoordinator();
        var context = CreateContext(
            phase: "Climax",
            positionInTurn: 2,
            turnActorCount: 2,
            activeTheme: theme,
            sceneDirection: sceneDirection);
        context = context with { ActorStats = new Dictionary<string, int> { ["TestActor"] = 50 } };

        var output = coordinator.BuildPrompt(context);

        // Should contain deepening guidance for position 2+
        Assert.Contains("deepen", output, StringComparison.OrdinalIgnoreCase);
        // Fast pacing should be on scene direction but position 2+ gets deepening
        Assert.Contains("Do NOT advance to a new beat", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── No hardcoded phase branching — theme marker drives text ──

    [Fact]
    public void Coordinator_PacingSlow_ProducesSlowEscalationText()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance
                {
                    Phase = NarrativePhase.BuildUp,
                    GuidanceText = "[Pacing:slow]"
                }
            ]
        };
        var sceneDirection = SceneDirectionResolver.Resolve("BuildUp", theme, ClimaxSubPhase.None, PromptIntent.Message);
        var coordinator = CreateCoordinator();
        var context = CreateContext(
            phase: "BuildUp",
            activeTheme: theme,
            sceneDirection: sceneDirection);
        context = context with { ActorStats = new Dictionary<string, int> { ["TestActor"] = 50 } };

        var output = coordinator.BuildPrompt(context);
        Assert.Contains("Advance within the same beat", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coordinator_PacingFast_ProducesFastEscalationText()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance
                {
                    Phase = NarrativePhase.Climax,
                    GuidanceText = "[Pacing:fast]"
                }
            ]
        };
        var sceneDirection = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        var coordinator = CreateCoordinator();
        var context = CreateContext(
            phase: "Climax",
            activeTheme: theme,
            sceneDirection: sceneDirection);
        context = context with { ActorStats = new Dictionary<string, int> { ["TestActor"] = 50 } };

        var output = coordinator.BuildPrompt(context);
        Assert.Contains("Compress multiple beats", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── ThemeContract injector fires when theme is present ───────

    [Fact]
    public void Coordinator_ActiveTheme_ContainsThemeContract()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            Label = "Test Theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance
                {
                    Phase = NarrativePhase.Climax,
                    GuidanceText = "This is a test phase guidance line."
                }
            ]
        };
        var sceneDirection = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        var coordinator = CreateCoordinator();
        var context = CreateContext(
            phase: "Climax",
            activeTheme: theme,
            sceneDirection: sceneDirection);
        context = context with { ActorStats = new Dictionary<string, int> { ["TestActor"] = 50 } };

        var output = coordinator.BuildPrompt(context);
        Assert.Contains("Test Theme", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test phase guidance line", output, StringComparison.OrdinalIgnoreCase);
    }
}
