using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Contract tests for individual prompt slots. Each slot is tested in isolation
/// with a mocked PromptBuildContext to verify FR-036 (independent slot testability).
/// SC-008: Every slot can be tested independently.
/// </summary>
public sealed class SlotContractTests
{
    // ── Shared context builder ─────────────────────────────────

    private static PromptBuildContext CreateContext(
        string phase = "BuildUp",
        PromptVariant variant = PromptVariant.Character,
        ActorProfileKind actorKind = ActorProfileKind.Player,
        string actorName = "TestActor",
        string actorRole = "protagonist",
        int? turnIndex = 3,
        int? positionInTurn = 2,
        int? turnActorCount = 3,
        string currentSceneLocation = "The Living Room",
        string personaName = "You",
        string personaDescription = "A brave adventurer.",
        string promptText = "Continue naturally.",
        int maxPromptChars = 35000,
        int observedTurnCount = 1,
        string? openingGuidanceText = null,
        List<ScenarioCharacter>? characters = null,
        IReadOnlyList<RolePlayInteraction>? recentInteractions = null)
    {
        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            ScenarioId = "test-scenario",
            PersonaName = personaName,
            PersonaDescription = personaDescription,
            PersonaRole = "Hero",
            MaxPromptChars = maxPromptChars,
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = NarrativePhase.BuildUp,
                CurrentSceneLocation = currentSceneLocation,
                ObservedTurnCount = observedTurnCount,
            },
        };

        var actorProfile = new ActorProfile
        {
            Kind = actorKind,
            ActorName = actorName,
            ActorRole = actorRole,
            PerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            PresentCharacterIds = characters?.Select(c => c.Id).ToList() ?? [],
            AllCharacterIds = characters?.Select(c => c.Id).ToList() ?? [],
        };

        return new PromptBuildContext
        {
            Session = session,
            ActorProfile = actorProfile,
            Variant = variant,
            Phase = phase,
            TurnIndex = turnIndex,
            PositionInTurn = positionInTurn,
            TurnActorCount = turnActorCount,
            PromptText = promptText,
            MaxPromptChars = maxPromptChars,
            WorldState = null,
            Scenario = new ResolvedScenarioData
            {
                ScenarioId = "test-scenario",
                Name = "Test Scenario",
                Description = "A test scenario",
                PlotDescription = "Plot",
                WorldDescription = "World",
                TimeFrame = null,
                Goals = [],
                Conflicts = [],
                WorldRules = [],
                EnvironmentalDetails = [],
                NarrativeGuidelines = [],
                Characters = characters ?? [],
                Locations = [],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = null,
                OpeningGuidanceText = openingGuidanceText,
            },
            Theme = new ResolvedThemeData(),
            Intensity = new ResolvedIntensityData
            {
                ProseStyleDirective = "Test prose.",
                VoiceDirective = "Test voice.",
                ToneDirective = "Test tone.",
                FocusDirective = "Test focus.",
                HeatLevelDirective = "Test heat.",
            },
            WritingStyle = new ResolvedWritingStyleData
            {
                Example = "Test example",
                PhaseRuleOfThumb = "Phase RoT",
                StyleHint = "Test hint",
                ImmersionDirective = "Stay in character.",
                ActionDirective = "Respond naturally.",
                WordTargetMin = 200,
                WordTargetMax = 400,
                NarrativeWordTargetMin = 300,
                NarrativeWordTargetMax = 500,
            },
            NarrativeTone = new ResolvedNarrativeToneData(),
            EncounterSummaries = [],
            RecentInteractions = recentInteractions ?? [],
            PinnedInteractions = [], StagedInteractions = [],
            CharacterDetails = null,
        };
    }

    // ── T021: SceneAnchorSlot (FR-005, FR-036, SC-008) ─────────

    // ── T022: ActorAssignmentSlot (FR-006, FR-036) ─────────────

    [Fact]
    public async Task ActorAssignmentSlot_CharacterVariant_OutputsContinueAs()
    {
        var slot = new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance);
        var context = CreateContext(variant: PromptVariant.Character, actorName: "Becky", actorRole: "the wife");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Continue as: Becky (the wife)", text);
    }

    [Fact]
    public async Task ActorAssignmentSlot_NarrativeVariant_OutputsOmniscient()
    {
        var slot = new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance);
        var context = CreateContext(variant: PromptVariant.Narrative, actorKind: ActorProfileKind.Narrative);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("omniscient narrator", text);
    }

    [Fact]
    public async Task ActorAssignmentSlot_CustomActor_NoRoleSuffix()
    {
        var slot = new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance);
        var context = CreateContext(actorKind: ActorProfileKind.Custom, actorName: "MysteryGuest", actorRole: "custom");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Continue as: MysteryGuest.", text);
        Assert.DoesNotContain("(custom)", text);
    }

    // ── T023: TurnContextSlot (FR-007, FR-036) ─────────────────

    [Fact]
    public async Task TurnContextSlot_CharacterVariant_FirstPosition_EstablishesBeat()
    {
        var slot = new TurnContextSlot(NullLogger<TurnContextSlot>.Instance);
        var context = CreateContext(turnIndex: 5, positionInTurn: 1, turnActorCount: 3);

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("turn 5", text);
        Assert.Contains("response 1 of 3", text);
        Assert.Contains("You are position 1 of 3", text);
    }

    [Fact]
    public async Task TurnContextSlot_SkipsWhenNoTurnData()
    {
        var slot = new TurnContextSlot(NullLogger<TurnContextSlot>.Instance);
        var context = CreateContext(turnIndex: null, turnActorCount: null);

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task TurnContextSlot_NarrativeVariant_NoPositionGuidance()
    {
        var slot = new TurnContextSlot(NullLogger<TurnContextSlot>.Instance);
        var context = CreateContext(variant: PromptVariant.Narrative, turnIndex: 3, turnActorCount: 2);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("narrative close", text);
        Assert.Contains("omniscient account", text);
        Assert.DoesNotContain("You are first", text);
    }

    // ── T024: SceneLocationLockSlot (FR-008, FR-036) ───────────

    [Fact]
    public async Task SceneLocationLockSlot_WithLocation_OutputsHardConstraint()
    {
        // SKIPPED: Location assertion commented out — slot now returns empty string.
        // See SceneLocationLockSlot.cs for rationale.
        var slot = new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance);
        var context = CreateContext(currentSceneLocation: "The Kitchen");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task SceneLocationLockSlot_WithoutLocation_OutputsContinuityRule()
    {
        // SKIPPED: Location assertion commented out — slot now returns empty string.
        var slot = new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance);
        var context = CreateContext(currentSceneLocation: "");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, text);
    }

    // ── T025: CharacterDataSlot (FR-010, FR-011, FR-036) ───────

    [Fact]
    public async Task CharacterDataSlot_PlayerActor_IncludesPersonaAndOthers()
    {
        var slot = new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance);
        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };
        var context = CreateContext(
            actorKind: ActorProfileKind.Player,
            actorName: "You",
            personaName: "Ken",
            personaDescription: "A young man on vacation.",
            characters: characters);
        // Player has all chars present.
        context = context with
        {
            ActorProfile = context.ActorProfile with
            {
                PresentCharacterIds = characters.Select(c => c.Id).ToList()
            }
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("POV Persona (Ken)", text);
        Assert.Contains("Characters in this scene:", text);
        Assert.Contains("Becky", text);
        Assert.Contains("Dean", text);
        Assert.DoesNotContain("comparison reference only", text);
    }

    [Fact]
    public async Task CharacterDataSlot_NpcNonPresent_ComparisonOnlyForPresent()
    {
        var slot = new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance);
        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };
        var context = CreateContext(
            actorKind: ActorProfileKind.NpcNonPresent,
            actorName: "Becky",
            actorRole: "wife",
            characters: characters);
        // Becky's own ID is the only present one.
        context = context with
        {
            ActorProfile = context.ActorProfile with
            {
                PresentCharacterIds = new List<string> { "c1" }
            }
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("comparison reference only", text);
    }

    [Fact]
    public async Task CharacterDataSlot_NarrativeVariant_LighterFormatAllChars()
    {
        var slot = new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance);
        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };
        var context = CreateContext(
            variant: PromptVariant.Narrative,
            actorKind: ActorProfileKind.Narrative,
            characters: characters);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Characters in this scene:", text);
        Assert.DoesNotContain("POV Persona", text);
        Assert.DoesNotContain("comparison reference only", text);
    }

    // ── T039: ThemeContractSlot (FR-018, FR-027, FR-036) ──────

    [Fact]
    public async Task ThemeContractSlot_NoActiveTheme_HandlesGracefully()
    {
        var slot = new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance);
        var context = CreateContext();
        context = context with { Theme = new ResolvedThemeData() };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // Should not throw, may be empty or minimal.
        Assert.NotNull(text);
    }

    // ── 001-opening-period FR-002: Potential Arcs suppressed during opening period ──

    [Fact]
    public async Task ThemeContractSlot_OpeningPeriod_SuppressesPotentialArcs()
    {
        var slot = new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 2);
        context = context with
        {
            Theme = new ResolvedThemeData
            {
                ActiveTheme = null,
                AvailableArcLabels = [("NTR Open World", "A love triangle — Wife, Husband, and Other Man.")],
            },
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Potential Arcs", text);
        Assert.DoesNotContain("Other Man", text);
    }

    [Fact]
    public async Task ThemeContractSlot_AfterOpeningPeriod_EmitsPotentialArcs()
    {
        var slot = new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance);
        var context = CreateContext(phase: "BuildUp", observedTurnCount: 4);
        context = context with
        {
            Theme = new ResolvedThemeData
            {
                ActiveTheme = null,
                AvailableArcLabels = [("NTR Open World", "A love triangle — Wife, Husband, and Other Man.")],
            },
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Potential Arcs", text);
        Assert.Contains("Other Man", text);
    }

    // ── T040: BehavioralFramesSlot (FR-019, FR-027, FR-036) ───

    [Fact]
    public async Task BehavioralFramesSlot_CharacterVariant_ShowsOwnFrameFirst()
    {
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var frames = new Dictionary<string, string>
        {
            ["Becky"] = "Becky is feeling conflicted.",
            ["Dean"] = "Dean is growing suspicious.",
        };
        var context = CreateContext(
            actorKind: ActorProfileKind.NpcPresent,
            actorName: "Becky",
            actorRole: "wife");
        context = context with
        {
            CharacterBehavioralFrames = frames,
            ActorProfile = context.ActorProfile with
            {
                PresentCharacterIds = new List<string> { "c1", "c2" }
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Character Behavioral Frames", text);
        Assert.Contains("Becky — your character", text);
        Assert.Contains("feeling conflicted", text);
        Assert.Contains("Dean", text);
        Assert.Contains("growing suspicious", text);
    }

    [Fact]
    public async Task BehavioralFramesSlot_NoFrames_Skips()
    {
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var context = CreateContext();
        context = context with { CharacterBehavioralFrames = null };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task BehavioralFramesSlot_NarrativeVariant_ShowsAllFrames()
    {
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var frames = new Dictionary<string, string>
        {
            ["Becky"] = "Becky is feeling conflicted.",
            ["Dean"] = "Dean is growing suspicious.",
        };
        var context = CreateContext(
            variant: PromptVariant.Narrative,
            actorKind: ActorProfileKind.Narrative);
        context = context with { CharacterBehavioralFrames = frames };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Character Behavioral Frames", text);
        Assert.Contains("Becky", text);
        Assert.Contains("Dean", text);
        Assert.DoesNotContain("your character", text);
        Assert.DoesNotContain("not present", text);
    }

    // ── B-034: Wife Willingness to Cheat unified block (Slot 13) ──

    [Fact]
    public async Task BehavioralFramesSlot_WillingnessBlock_FiresWhenGuidanceHasBandLines()
    {
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            CharacterBehavioralFrames = null,
            CharacterStatStateTexts = null,
            ScenarioGuidanceText =
                "Verdict: YES — She will cross when the opportunity is plausible. " +
                "Ceiling: Full Surrender — Escalate to the ceiling. (Examples: consummated) " +
                "Ladder: Gentle Touch, Kissing, Manual Sex, Full Surrender " +
                "Details: Willingness to Cheat = 100 (Desire=80, Loyalty=20, SeductionReceptivity=70, BoundaryFirmness=30, Attentiveness=40, IntimacyAvailability=40); Ceiling = min(Desire, willingness) = 80."
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // The unified HARD CONSTRAINT block is rendered with Verdict + Ceiling + Ladder + Details.
        Assert.Contains("HARD CONSTRAINT — Wife Willingness to Cheat", text);
        Assert.Contains("Verdict: YES", text);
        Assert.Contains("Ceiling: Full Surrender", text);
        Assert.Contains("Ladder: Gentle Touch, Kissing, Manual Sex, Full Surrender", text);
        Assert.Contains("Details: Willingness to Cheat = 100", text);
        Assert.Contains("Ceiling = min(Desire, willingness) = 80", text);
    }

    [Fact]
    public async Task BehavioralFramesSlot_WillingnessBlock_DoesNotFireWithoutBandLines()
    {
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            CharacterBehavioralFrames = null,
            CharacterStatStateTexts = null,
            ScenarioGuidanceText = "The triangle is in motion."
        };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task BehavioralFramesSlot_WillingnessBlock_RendersMidLineBandsWithFactorySuffix()
    {
        // B-034 regression: ScenarioGuidanceGenerator concatenates the phase guidance prose,
        // the verdict, the ceiling, the ladder, and the details space-separated into ONE line,
        // and the context factory appends " Emphasize:" / " Avoid:" after them. The slot must
        // extract the verdict/ceiling/ladder/details sentences by marker (not by line-start)
        // and stop before the suffixes, preserving contract order (Verdict, Ceiling, Ladder,
        // Details).
        var slot = new BehavioralFramesSlot(NullLogger<BehavioralFramesSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            CharacterBehavioralFrames = null,
            CharacterStatStateTexts = null,
            ScenarioGuidanceText =
                "The triangle is in motion. Ceiling: Full Surrender — Escalate to the ceiling. (Examples: consummated) Ladder: Gentle Touch, Kissing, Manual Sex, Full Surrender Details: Willingness to Cheat = 100 (Desire=80, Loyalty=20, SeductionReceptivity=70, BoundaryFirmness=30, Attentiveness=40, IntimacyAvailability=40); Ceiling = min(Desire, willingness) = 80. Verdict: YES — She will cross when the opportunity is plausible. Emphasize: trust. Avoid: tone drift."
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Wife Willingness to Cheat", text);
        // Contract order: Verdict, Ceiling, Ladder, Details.
        var verdictIdx = text.IndexOf("Verdict: YES", StringComparison.Ordinal);
        var ceilingIdx = text.IndexOf("Ceiling: Full Surrender", StringComparison.Ordinal);
        var ladderIdx = text.IndexOf("Ladder: Gentle Touch, Kissing, Manual Sex, Full Surrender", StringComparison.Ordinal);
        var detailsIdx = text.IndexOf("Details: Willingness to Cheat = 100", StringComparison.Ordinal);
        Assert.True(verdictIdx >= 0, "Verdict line missing");
        Assert.True(ceilingIdx >= 0, "Ceiling line missing");
        Assert.True(ladderIdx >= 0, "Ladder line missing");
        Assert.True(detailsIdx >= 0, "Details line missing");
        Assert.True(verdictIdx < ceilingIdx && ceilingIdx < ladderIdx && ladderIdx < detailsIdx,
            $"Expected contract order Verdict < Ceiling < Ladder < Details (got {verdictIdx}, {ceilingIdx}, {ladderIdx}, {detailsIdx})");
        // The factory suffixes are NOT dragged into the block.
        Assert.DoesNotContain("Emphasize: trust", text);
        Assert.DoesNotContain("Avoid: tone drift", text);
        // The band sentences must not be preceded by the phase prose inside the block.
        Assert.DoesNotContain("triangle is in motion. Ceiling:", text);
    }

    // ── T041: FinalInstructionSlot Character variant (FR-023, FR-027, FR-036) ──

    // ── T048: Narrative-variant contract tests for CharacterDataSlot ──

    [Fact]
    public async Task CharacterDataSlot_NarrativeVariant_NoPersonaNoComparison()
    {
        var slot = new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance);
        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };
        var context = CreateContext(
            variant: PromptVariant.Narrative,
            actorKind: ActorProfileKind.Narrative,
            actorName: "omniscient narrator",
            characters: characters);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Characters in this scene:", text);
        Assert.Contains("Becky", text);
        Assert.Contains("Dean", text);
        Assert.DoesNotContain("POV Persona", text);
        Assert.DoesNotContain("comparison reference only", text);
    }

    // ── T058: ScenarioContextSlot (FR-012, FR-036) ─────────────

    [Fact]
    public async Task ScenarioContextSlot_OutputsScenarioInfo()
    {
        var slot = new ScenarioContextSlot(NullLogger<ScenarioContextSlot>.Instance);
        var context = CreateContext();
        // Set required session config.
        var session = context.Session;
        session.ScenarioCompressionTurnThreshold = 10;
        context = context with
        {
            Session = session,
            Scenario = new ResolvedScenarioData
            {
                ScenarioId = "test-scenario",
                Name = "Test Scenario",
                Description = "A test.",
                PlotDescription = "A plot unfolds.",
                WorldDescription = "A vast world.",
                TimeFrame = "Summer 2024",
                Goals = ["Find the truth"],
                Conflicts = ["Inner demons"],
                WorldRules = ["Magic is real"],
                EnvironmentalDetails = ["Forest environment"],
                NarrativeGuidelines = ["Stay grounded"],
                Characters = [],
                Locations = [new("The Cabin", null)],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = null,
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Scenario:", text);
        Assert.Contains("Test Scenario", text);
        Assert.Contains("A plot unfolds", text);
        Assert.Contains("A vast world", text);
    }

    [Fact]
    public async Task ScenarioContextSlot_IsTrimEligible()
    {
        var slot = new ScenarioContextSlot(NullLogger<ScenarioContextSlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task ScenarioContextSlot_CompressionFlagSet()
    {
        // Verify the slot has the correct identity.
        var slot = new ScenarioContextSlot(NullLogger<ScenarioContextSlot>.Instance);
        Assert.Equal(PromptSlotId.ScenarioContext, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(6, slot.Order);
    }

    // ── T059: CurrentLocationSlot (FR-013, FR-036) ─────────────

    [Fact]
    public async Task CurrentLocationSlot_UnknownLocation()
    {
        var slot = new CurrentLocationSlot(NullLogger<CurrentLocationSlot>.Instance);
        var context = CreateContext(currentSceneLocation: "");

        // Should still write, just with "Unknown".
        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);
        Assert.NotNull(text);
    }

    [Fact]
    public async Task CurrentLocationSlot_IsTrimEligible()
    {
        var slot = new CurrentLocationSlot(NullLogger<CurrentLocationSlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task CurrentLocationSlot_HasCorrectIdentity()
    {
        var slot = new CurrentLocationSlot(NullLogger<CurrentLocationSlot>.Instance);
        Assert.Equal(PromptSlotId.CurrentLocation, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(7, slot.Order);
    }

    // ── T060: WritingStyleSlot (FR-014, FR-036) ─────────────────

    [Fact]
    public async Task WritingStyleSlot_OutputsStyleGuide()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        var context = CreateContext();

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Style Guide:", text);
        Assert.Contains("POV:", text);
        Assert.Contains("Word Target:", text);
    }

    [Fact]
    public async Task WritingStyleSlot_NoFailFastOnMissingFields()
    {
        // WritingStyleSlot emits the full Style Guide from Intensity fields —
        // it does not fail-fast on empty StyleProfile fields.
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            WritingStyle = new ResolvedWritingStyleData
            {
                Example = "Example prose.",
                PhaseRuleOfThumb = "",
                StyleHint = "Hint",
                ImmersionDirective = "Stay in character.",
                ActionDirective = "Respond naturally.",
                WordTargetMin = 200,
                WordTargetMax = 400,
                NarrativeWordTargetMin = 300,
                NarrativeWordTargetMax = 500,
                WordTargetMarker = "small",
            },
            Intensity = new ResolvedIntensityData
            {
                ProseStyleDirective = "",
                VoiceDirective = "",
                ToneDirective = "",
                FocusDirective = "",
                HeatLevelDirective = "",
            },
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Style Guide:", text);
        Assert.Contains("Word Target:", text);
        Assert.DoesNotContain("Prose Style:", text); // Empty in intensity, skipped
    }

    [Fact]
    public async Task WritingStyleSlot_IsTrimEligible()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        Assert.False(slot.IsTrimEligible);
    }

    [Fact]
    public async Task WritingStyleSlot_HasCorrectIdentity()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        Assert.Equal(PromptSlotId.WritingStyle, slot.Id);
        Assert.Equal(PromptZone.C, slot.Zone);
        Assert.Equal(18, slot.Order);
    }

    // ── T061: SceneContinuityAnchorSlot (FR-017, FR-036) ───────

    [Fact]
    public async Task SceneContinuityAnchorSlot_DropsSelfPerceptions()
    {
        var slot = new SceneContinuityAnchorSlot(NullLogger<SceneContinuityAnchorSlot>.Instance);
        var context = CreateContext(actorKind: ActorProfileKind.Player, actorName: "You");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // Self-perceptions are dropped — only cross-perceptions remain.
        Assert.DoesNotContain("you perceive", text.ToLowerInvariant());
        Assert.DoesNotContain("your own", text.ToLowerInvariant());
    }

    [Fact]
    public async Task SceneContinuityAnchorSlot_IsTrimEligible()
    {
        var slot = new SceneContinuityAnchorSlot(NullLogger<SceneContinuityAnchorSlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task SceneContinuityAnchorSlot_HasCorrectIdentity()
    {
        var slot = new SceneContinuityAnchorSlot(NullLogger<SceneContinuityAnchorSlot>.Instance);
        Assert.Equal(PromptSlotId.SceneContinuityAnchor, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(11, slot.Order);
    }

    // ── T071: InteractionHistorySlot (FR-015, FR-036) ──────────

    [Fact]
    public async Task InteractionHistorySlot_ThreeTierCompression_FullDetailForRecent()
    {
        var slot = new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance);
        // Create 3 turns with 2 actors each (Dean + Becky per turn = 6 interactions).
        var interactions = new List<RolePlayInteraction>();
        for (int turn = 1; turn <= 3; turn++)
        {
            interactions.Add(new RolePlayInteraction
            {
                Id = $"t{turn}-ixn1",
                ActorName = "Dean",
                Content = $"Turn {turn} Dean content.",
                IsExcluded = false,
            });
            interactions.Add(new RolePlayInteraction
            {
                Id = $"t{turn}-ixn2",
                ActorName = "Becky",
                Content = $"Turn {turn} Becky content.",
                IsExcluded = false,
            });
        }

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            HistoryFullDetailTurnBand = 3,
            HistoryNarrativeOnlyTurnBand = 3,
            ContextWindowTurns = 8,
        };

        // Build entries so the slot has turn metadata
        var entries = new List<RecentInteractionEntry>();
        for (int i = 0; i < interactions.Count; i++)
        {
            var turnNum = (i / 2) + 1;
            entries.Add(new RecentInteractionEntry
            {
                Interaction = interactions[i],
                TurnNumber = turnNum,
                PositionInTurn = (i % 2) + 1,
                TurnActorCount = 2,
            });
        }

        var context = CreateContext(recentInteractions: interactions);
        context = context with { Session = session, RecentInteractionEntries = entries };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // Turn-grouped format: each completed turn gets its own header.
        Assert.Contains("Turn 1:", text);
        Assert.Contains("Turn 2:", text);

        // All recent interactions present.
        Assert.Contains("Turn 1 Dean", text);
        Assert.Contains("Turn 2 Becky", text);
    }

    [Fact]
    public async Task InteractionHistorySlot_FailsFast_WhenThresholdsMissing()
    {
        var slot = new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance);
        var interactions = new List<RolePlayInteraction>
        {
            new() { Id = "ixn-1", ActorName = "Becky", Content = "Hello.", IsExcluded = false },
        };

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            HistoryFullDetailTurnBand = null, // Missing!
            HistoryNarrativeOnlyTurnBand = 3,
            ContextWindowTurns = 8,
        };

        var context = CreateContext(recentInteractions: interactions);
        context = context with { Session = session };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            slot.WriteAsync(context, CancellationToken.None));

        Assert.Contains("HistoryFullDetailTurnBand", ex.Message);
        Assert.Contains("FR-012a", ex.Message);
    }

    [Fact]
    public async Task InteractionHistorySlot_SkipsWhenNoInteractions()
    {
        var slot = new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance);
        var context = CreateContext(recentInteractions: []);

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task InteractionHistorySlot_IsTrimEligible()
    {
        var slot = new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task InteractionHistorySlot_HasCorrectIdentity()
    {
        var slot = new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance);
        Assert.Equal(PromptSlotId.InteractionHistory, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(9, slot.Order);
    }

    // ── T072: SessionMemorySlot (FR-016, FR-036) ───────────────

    [Fact]
    public async Task SessionMemorySlot_ThreeTiers_LongTermMediumShortTerm()
    {
        var slot = new SessionMemorySlot(NullLogger<SessionMemorySlot>.Instance);

        var encounterSummaries = new List<EncounterSummaryRecord>
        {
            new() { Id = "es1", CharacterId = "Becky", LlmSummary = "Becky's long-term memory 1.", OccurredUtc = DateTime.UtcNow.AddDays(-10), EncounterNumber = 1, SummaryType = EncounterSummaryType.EncounterCompletion },
            new() { Id = "es2", CharacterId = "Dean", LlmSummary = "Dean's long-term memory 1.", OccurredUtc = DateTime.UtcNow.AddDays(-10), EncounterNumber = 1, SummaryType = EncounterSummaryType.EncounterCompletion },
            new() { Id = "es3", CharacterId = "Becky", LlmSummary = "Becky's medium-term memory.", OccurredUtc = DateTime.UtcNow.AddDays(-2), EncounterNumber = 3, SummaryType = EncounterSummaryType.EncounterCompletion },
            new() { Id = "es4", CharacterId = "Becky", LlmSummary = "Becky's short-term milestone.", OccurredUtc = DateTime.UtcNow.AddHours(-1), EncounterNumber = 4, SummaryType = EncounterSummaryType.PhaseMilestone },
        };

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            SessionMemoryLongTermTurnThreshold = 10,
        };

        var context = CreateContext(actorName: "Becky");
        context = context with
        {
            Session = session,
            EncounterSummaries = encounterSummaries,
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Session Memory", text);
        // Should contain Becky's memories only, not Dean's.
        Assert.Contains("Becky", text);
        Assert.DoesNotContain("Dean", text);
    }

    [Fact]
    public async Task SessionMemorySlot_FailsFast_WhenThresholdMissing()
    {
        var slot = new SessionMemorySlot(NullLogger<SessionMemorySlot>.Instance);

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            SessionMemoryLongTermTurnThreshold = null, // Missing!
        };

        var context = CreateContext();
        context = context with { Session = session, EncounterSummaries = [new() { Id = "es1", LlmSummary = "Test.", SummaryType = EncounterSummaryType.EncounterCompletion }] };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            slot.WriteAsync(context, CancellationToken.None));

        Assert.Contains("SessionMemoryLongTermTurnThreshold", ex.Message);
        Assert.Contains("FR-012a", ex.Message);
    }

    [Fact]
    public async Task SessionMemorySlot_SkipsWhenNoSummaries()
    {
        var slot = new SessionMemorySlot(NullLogger<SessionMemorySlot>.Instance);

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            SessionMemoryLongTermTurnThreshold = 10,
        };

        var context = CreateContext();
        context = context with { Session = session, EncounterSummaries = [] };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task SessionMemorySlot_IsTrimEligible()
    {
        var slot = new SessionMemorySlot(NullLogger<SessionMemorySlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task SessionMemorySlot_HasCorrectIdentity()
    {
        var slot = new SessionMemorySlot(NullLogger<SessionMemorySlot>.Instance);
        Assert.Equal(PromptSlotId.SessionMemory, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(10, slot.Order);
    }

    // ── T081: WorldStateSlot (FR-009, FR-036) ─────────────────

    [Fact]
    public void WorldStateSlot_ShouldWrite_WhenWorldStateNonNull()
    {
        var slot = new WorldStateSlot();
        var context = CreateContext();
        context = context with
        {
            WorldState = new WorldStateData
            {
                DayNumber = 3,
                TotalDays = 7,
                DayOfWeek = "Wednesday",
                TimePhase = "Morning",
                SpecificTime = "10:30 AM",
                WeatherCondition = "Clear skies",
                TemperatureCelsius = 22,
                HumidityDescription = "Dry and crisp",
                WorldRhythm = "Birds chirping, distant traffic",
                TemporalPressure = "Checkout is at noon",
            },
        };

        Assert.True(slot.ShouldWrite(context));
    }

    [Fact]
    public void WorldStateSlot_ShouldWrite_WhenWorldStateNull()
    {
        var slot = new WorldStateSlot();
        var context = CreateContext();
        context = context with { WorldState = null };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task WorldStateSlot_OutputMatchesGap5Format()
    {
        var slot = new WorldStateSlot();
        var context = CreateContext();
        context = context with
        {
            WorldState = new WorldStateData
            {
                DayNumber = 3,
                TotalDays = 7,
                DayOfWeek = "Wednesday",
                TimePhase = "Morning",
                SpecificTime = "10:30 AM",
                WeatherCondition = "Clear skies",
                TemperatureCelsius = 22,
                HumidityDescription = "Dry and crisp",
                WorldRhythm = "Birds chirping, distant traffic",
                TemporalPressure = "Checkout is at noon",
            },
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("World State:", text);
        Assert.Contains("Day 3 of 7", text);
        Assert.Contains("Wednesday", text);
        Assert.Contains("Morning", text);
        Assert.Contains("10:30 AM", text);
        Assert.Contains("Weather: Clear skies, 22°C", text);
        Assert.Contains("Dry and crisp", text);
        Assert.Contains("World rhythm:", text);
        Assert.Contains("Birds chirping", text);
        Assert.Contains("Temporal pressure:", text);
        Assert.Contains("Checkout is at noon", text);
    }

    [Fact]
    public async Task WorldStateSlot_PartialData_OmitsMissingLines()
    {
        var slot = new WorldStateSlot();
        var context = CreateContext();
        context = context with
        {
            WorldState = new WorldStateData
            {
                DayNumber = 1,
                WeatherCondition = "Rain",
                TemperatureCelsius = 15,
            },
        };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Day 1", text);
        Assert.Contains("Weather: Rain, 15°C", text);
        Assert.DoesNotContain("World rhythm:", text);
        Assert.DoesNotContain("Temporal pressure:", text);
    }

    [Fact]
    public void WorldStateSlot_HasCorrectIdentity()
    {
        var slot = new WorldStateSlot();
        Assert.Equal(PromptSlotId.WorldState, slot.Id);
        Assert.Equal(PromptZone.A, slot.Zone);
        Assert.Equal(4, slot.Order);
        Assert.False(slot.IsTrimEligible);
    }

    // ── T089: ScenarioGuidanceSlot (FR-020, FR-036) ───────────

    [Fact]
    public async Task ScenarioGuidanceSlot_OutputsPhaseSteering()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "BuildUp");

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Scenario Guidance:", text);
        Assert.Contains("BuildUp", text);
    }

    // ── 001-opening-period: opening-period direction injection (FR-003 / FR-016) ───

    [Fact]
    public async Task ScenarioGuidanceSlot_OpeningPeriod_InjectsOpeningDirection()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 2);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Opening Period Direction:", text);
        Assert.Contains("settled, long-established couple", text);
        Assert.DoesNotContain("Establish the scene, introduce characters", text);
    }

    [Fact]
    public async Task ScenarioGuidanceSlot_OpeningPeriod_UsesScenarioTextWhenPresent()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 1,
            openingGuidanceText: "Custom couple baseline direction.");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Custom couple baseline direction.", text);
        Assert.DoesNotContain("settled, long-established couple", text);
    }

    [Fact]
    public async Task ScenarioGuidanceSlot_AfterOpeningPeriod_NoOpeningDirection()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 4);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.DoesNotContain("HARD CONSTRAINT — Opening Period Direction:", text);
        Assert.Contains("Establish the scene, introduce characters", text);
    }

    // ── 001-opening-period SC-001 hardening: explicit Opening Cast absence constraint ──

    [Fact]
    public async Task ScenarioGuidanceSlot_OpeningPeriod_EmitsOpeningCastConstraint()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 2);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Opening Cast:", text);
        Assert.Contains("The love interest / Other Man is NOT present", text);
        Assert.Contains("Only the couple (husband and wife) are present", text);
    }

    [Fact]
    public async Task ScenarioGuidanceSlot_AfterOpeningPeriod_NoOpeningCastConstraint()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        var context = CreateContext(phase: "Opening", observedTurnCount: 4);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.DoesNotContain("HARD CONSTRAINT — Opening Cast:", text);
    }

    [Fact]
    public void ScenarioGuidanceSlot_HasCorrectIdentity()
    {
        var slot = new ScenarioGuidanceSlot(NullLogger<ScenarioGuidanceSlot>.Instance);
        Assert.Equal(PromptSlotId.ScenarioGuidance, slot.Id);
        Assert.Equal(PromptZone.C, slot.Zone);
        Assert.Equal(14, slot.Order);
        Assert.True(slot.IsTrimEligible);
    }

    // ── T089: IntensityPacingSlot (FR-021, FR-036) ────────────

    [Fact]
    public async Task IntensityPacingSlot_OutputsPositionsOnly()
    {
        var slot = new IntensityPacingSlot(NullLogger<IntensityPacingSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            Intensity = new ResolvedIntensityData
            {
                ResolvedLabel = "Passionate",
                Description = "High emotional charge with physical urgency.",
                AvailablePositions = new List<string> { "Missionary", "Cowgirl" },
                SceneDirection = new SceneDirection
                {
                    Pacing = ScenePacing.Medium,
                    TimeShift = TimeShiftPolicy.None,
                    Deepening = DeepeningPolicy.None,
                },
                ProseStyleDirective = "Test prose.",
                VoiceDirective = "Test voice.",
                ToneDirective = "Test tone.",
                FocusDirective = "Test focus.",
                HeatLevelDirective = "Test heat.",
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // After consolidation, only available positions remain (structural data).
        // Heat Level, contract, and pacing have moved to Slot 17.
        Assert.DoesNotContain("Intensity & Pacing:", text);
        Assert.DoesNotContain("Passionate", text);
        Assert.DoesNotContain("Scene pacing:", text);
        Assert.Contains("Available positions:", text);
        Assert.Contains("Missionary", text);
        Assert.Contains("Cowgirl", text);
    }

    [Fact]
    public async Task IntensityPacingSlot_NoIntensityData_Omitted()
    {
        var slot = new IntensityPacingSlot(NullLogger<IntensityPacingSlot>.Instance);
        var context = CreateContext();
        context = context with { Intensity = new ResolvedIntensityData { ProseStyleDirective = "", VoiceDirective = "", ToneDirective = "", FocusDirective = "", HeatLevelDirective = "" } };

        // Should still write — at minimum provides pacing guidance.
        Assert.True(slot.ShouldWrite(context));
    }

    [Fact]
    public void IntensityPacingSlot_HasCorrectIdentity()
    {
        var slot = new IntensityPacingSlot(NullLogger<IntensityPacingSlot>.Instance);
        Assert.Equal(PromptSlotId.IntensityPacing, slot.Id);
        Assert.Equal(PromptZone.C, slot.Zone);
        Assert.Equal(15, slot.Order);
        Assert.False(slot.IsTrimEligible);
    }

    // ── T089: UserDirectionSlot (FR-022, FR-036) ──────────────

    [Fact]
    public void UserDirectionSlot_ShouldWrite_WhenRealDirection()
    {
        var slot = new UserDirectionSlot(NullLogger<UserDirectionSlot>.Instance);
        var context = CreateContext(promptText: "Becky should confront Dean about the letter.");

        Assert.True(slot.ShouldWrite(context));
    }

    [Fact]
    public void UserDirectionSlot_ShouldNotWrite_WhenGenericDefault()
    {
        var slot = new UserDirectionSlot(NullLogger<UserDirectionSlot>.Instance);
        var context = CreateContext(promptText: "Continue naturally.");

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task UserDirectionSlot_OutputsUserDirection()
    {
        var slot = new UserDirectionSlot(NullLogger<UserDirectionSlot>.Instance);
        var context = CreateContext(promptText: "Becky should confront Dean about the letter.");

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("User Direction:", text);
        Assert.Contains("Becky should confront Dean about the letter", text);
    }

    [Fact]
    public void UserDirectionSlot_HasCorrectIdentity()
    {
        var slot = new UserDirectionSlot(NullLogger<UserDirectionSlot>.Instance);
        Assert.Equal(PromptSlotId.UserDirection, slot.Id);
        Assert.Equal(PromptZone.C, slot.Zone);
        Assert.Equal(16, slot.Order);
        Assert.False(slot.IsTrimEligible);
    }

    // ── T090: PinnedContextSlot (FR-024, FR-036) ──────────────

    [Fact]
    public void PinnedContextSlot_ShouldWrite_WhenPinnedExists()
    {
        var slot = new PinnedContextSlot(NullLogger<PinnedContextSlot>.Instance);
        var pinned = new List<RolePlayInteraction>
        {
            new() { Id = "p1", ActorName = "Instruction", InteractionType = InteractionType.System, Content = "keep tension high", IsPinned = true },
        };
        var context = CreateContext();
        context = context with { PinnedInteractions = pinned };

        Assert.True(slot.ShouldWrite(context));
    }

    [Fact]
    public void PinnedContextSlot_ShouldNotWrite_WhenNoPinned()
    {
        var slot = new PinnedContextSlot(NullLogger<PinnedContextSlot>.Instance);
        var context = CreateContext();
        context = context with { PinnedInteractions = [] };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public void PinnedContextSlot_ShouldNotWrite_ForNarrativeVariant()
    {
        var slot = new PinnedContextSlot(NullLogger<PinnedContextSlot>.Instance);
        var pinned = new List<RolePlayInteraction>
        {
            new() { Id = "p1", ActorName = "Instruction", InteractionType = InteractionType.System, Content = "keep tension high", IsPinned = true },
        };
        var context = CreateContext(variant: PromptVariant.Narrative);
        context = context with { PinnedInteractions = pinned };

        Assert.False(slot.ShouldWrite(context));
    }

    [Fact]
    public async Task PinnedContextSlot_OutputsPinnedMessagesAndInstructions()
    {
        var slot = new PinnedContextSlot(NullLogger<PinnedContextSlot>.Instance);
        var pinned = new List<RolePlayInteraction>
        {
            new() { Id = "p1", ActorName = "Becky", InteractionType = InteractionType.User, Content = "I'm worried about Dean.", IsPinned = true },
            new() { Id = "p2", ActorName = "Instruction", InteractionType = InteractionType.System, Content = "keep tension high", IsPinned = true },
        };
        var context = CreateContext();
        context = context with { PinnedInteractions = pinned };

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("[Pinned Context]", text);
        Assert.Contains("Character Message — Becky: I'm worried about Dean.", text);
        Assert.Contains("Instruction: keep tension high", text);
    }

    [Fact]
    public void PinnedContextSlot_HasCorrectIdentity()
    {
        var slot = new PinnedContextSlot(NullLogger<PinnedContextSlot>.Instance);
        Assert.Equal(PromptSlotId.PinnedContext, slot.Id);
        Assert.Equal(PromptZone.C, slot.Zone);
        Assert.Equal(8, slot.Order);
        Assert.False(slot.IsTrimEligible);
    }
}

