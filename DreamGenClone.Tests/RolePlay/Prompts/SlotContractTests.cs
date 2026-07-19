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
            },
        };

        var actorProfile = new ActorProfile
        {
            Kind = actorKind,
            ActorName = actorName,
            ActorRole = actorRole,
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
            },
            Theme = new ResolvedThemeData(),
            Intensity = new ResolvedIntensityData(),
            WritingStyle = new ResolvedWritingStyleData
            {
                Description = "Test style",
                Example = "Test example",
                ProfileDefaultRuleOfThumb = "Default RoT",
                PhaseRuleOfThumb = "Phase RoT",
                StyleHint = "Test hint",
            },
            EncounterSummaries = [],
            RecentInteractions = recentInteractions ?? [],
            CharacterDetails = null,
        };
    }

    // ── T021: SceneAnchorSlot (FR-005, FR-036, SC-008) ─────────

    [Fact]
    public async Task SceneAnchorSlot_OutputsLocationAndPhase_NoLegacyHeader()
    {
        var slot = new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance);
        var context = CreateContext(phase: "Climax", currentSceneLocation: "The Bedroom");

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Current scene: The Bedroom", text);
        Assert.Contains("Climax phase", text);
        Assert.DoesNotContain("You are continuing", text);
        Assert.DoesNotContain("interactive role-play", text);
    }

    [Fact]
    public async Task SceneAnchorSlot_HandlesMissingLocation()
    {
        var slot = new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance);
        var context = CreateContext(currentSceneLocation: "");

        var text = await slot.WriteAsync(context, CancellationToken.None);
        Assert.Contains("Unknown location", text);
    }

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
        Assert.Contains("You are first this turn", text);
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
    public async Task ThemeContractSlot_WithActiveTheme_OutputsThemeAndGuidance()
    {
        var slot = new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            Theme = new ResolvedThemeData
            {
                ActiveTheme = new RPTheme
                {
                    Id = "t1",
                    Label = "Temptation",
                    Description = "The lure of forbidden desire.",
                },
                PhaseGuidanceLines = new List<string> { "Build tension through proximity." },
                PhaseDirectiveLines = new List<string> { "Focus on unspoken attraction." },
                AiGuidanceNotes = new List<RPThemeAIGuidanceNote>
                {
                    new() { Section = RPThemeAIGuidanceSection.KeyScenarioElement, Text = "Eye contact is critical." }
                },
                HardConstraintLines = new List<string> { "No violence." },
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Theme Contract:", text);
        Assert.Contains("Temptation", text);
        // Phase Guidance moved to FinalInstructionSlot — no longer in ThemeContractSlot
        Assert.DoesNotContain("Phase Guidance:", text);
        Assert.DoesNotContain("Build tension through proximity", text);
        Assert.Contains("Theme Directives:", text);
        Assert.Contains("Focus on unspoken attraction", text);
        Assert.Contains("AI Guidance Notes:", text);
        Assert.Contains("[Key Element]", text);
        Assert.Contains("Eye contact is critical", text);
        Assert.Contains("Hard Constraints:", text);
        Assert.Contains("- No violence", text);
    }

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

    // ── T041: FinalInstructionSlot Character variant (FR-023, FR-027, FR-036) ──

    [Fact]
    public async Task FinalInstructionSlot_CharacterVariant_FirstPersonPOV()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(
            variant: PromptVariant.Character,
            actorName: "Becky");

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Writing Instruction:", text);
        Assert.Contains("first-person from Becky", text);
        Assert.Contains("200-400 words", text);
        Assert.DoesNotContain("omniscient", text);
        Assert.DoesNotContain("Zero dialogue", text);
    }

    [Fact]
    public async Task FinalInstructionSlot_NarrativeVariant_OmniscientZeroDialogue()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(
            variant: PromptVariant.Narrative,
            actorKind: ActorProfileKind.Narrative);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("third-person omniscient", text);
        Assert.Contains("HARD CONSTRAINT: Zero dialogue", text);
        Assert.Contains("300-500 words", text);
        Assert.Contains("Physical Detail Checklist", text);
        Assert.Contains("Body positions", text);
        Assert.Contains("Physical contact", text);
        Assert.Contains("Sensory details", text);
        Assert.Contains("Rhythm and pacing", text);
        Assert.Contains("Environmental atmosphere", text);
        Assert.DoesNotContain("first-person", text);
    }

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
    public async Task CurrentLocationSlot_OutputsCurrentSceneLocation()
    {
        var slot = new CurrentLocationSlot(NullLogger<CurrentLocationSlot>.Instance);
        var context = CreateContext(currentSceneLocation: "The Bedroom");

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Current Location:", text);
        Assert.Contains("The Bedroom", text);
    }

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
    public async Task WritingStyleSlot_OutputsTimelessDescriptionAndExample()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            WritingStyle = new ResolvedWritingStyleData
            {
                Description = "A timeless prose style.",
                Example = "She walked through the door.",
                ProfileDefaultRuleOfThumb = "Default RoT text",
                PhaseRuleOfThumb = "Phase RoT text for BuildUp",
                StyleHint = "Tone: warm and intimate",
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Writing Style:", text);
        Assert.Contains("timeless prose style", text);
        Assert.Contains("She walked through the door", text);
        Assert.Contains("Phase Rule of Thumb", text);
        Assert.Contains("Phase RoT text for BuildUp", text);
        Assert.Contains("Profile Default", text);
        Assert.Contains("Default RoT text", text);
        Assert.Contains("Style Hint", text);
        Assert.Contains("warm and intimate", text);
    }

    [Fact]
    public async Task WritingStyleSlot_FailsFast_OnMissingPhaseRuleOfThumb()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            WritingStyle = new ResolvedWritingStyleData
            {
                Description = "Timeless prose.",
                Example = "Example prose.",
                ProfileDefaultRuleOfThumb = "Default RoT",
                PhaseRuleOfThumb = "", // Empty — should fail fast
                StyleHint = "Hint",
            },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            slot.WriteAsync(context, CancellationToken.None));

        Assert.Contains("PhaseRuleOfThumb", ex.Message);
        Assert.Contains("FR-014", ex.Message);
    }

    [Fact]
    public async Task WritingStyleSlot_FailsFast_OnMissingProfileDefault()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        var context = CreateContext();
        context = context with
        {
            WritingStyle = new ResolvedWritingStyleData
            {
                Description = "Timeless prose.",
                Example = "Example prose.",
                ProfileDefaultRuleOfThumb = "", // Empty — should fail fast
                PhaseRuleOfThumb = "Phase RoT",
                StyleHint = "Hint",
            },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            slot.WriteAsync(context, CancellationToken.None));

        Assert.Contains("ProfileDefaultRuleOfThumb", ex.Message);
        Assert.Contains("FR-014", ex.Message);
    }

    [Fact]
    public async Task WritingStyleSlot_IsTrimEligible()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        Assert.True(slot.IsTrimEligible);
    }

    [Fact]
    public async Task WritingStyleSlot_HasCorrectIdentity()
    {
        var slot = new WritingStyleSlot(NullLogger<WritingStyleSlot>.Instance);
        Assert.Equal(PromptSlotId.WritingStyle, slot.Id);
        Assert.Equal(PromptZone.B, slot.Zone);
        Assert.Equal(8, slot.Order);
    }

    // ── T061: SceneContinuityAnchorSlot (FR-017, FR-036) ───────

    [Fact]
    public async Task SceneContinuityAnchorSlot_OutputsCrossPerceptions()
    {
        var slot = new SceneContinuityAnchorSlot(NullLogger<SceneContinuityAnchorSlot>.Instance);
        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };
        var context = CreateContext(
            actorKind: ActorProfileKind.Player,
            actorName: "You",
            personaName: "Ken",
            characters: characters);

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Scene Continuity", text);
        Assert.Contains("what other characters perceive", text);
    }

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
        var interactions = new List<RolePlayInteraction>();
        for (int i = 1; i <= 10; i++)
        {
            interactions.Add(new RolePlayInteraction
            {
                Id = $"ixn-{i}",
                ActorName = i == 6 ? "Narrative" : (i % 2 == 0 ? "Becky" : "Dean"),
                Content = i == 6
                    ? "The scene settled into a quiet tension, the weight of earlier words still hanging between them like humidity before a storm."
                    : $"Interaction {i} content. Some detailed text here for testing.",
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

        var context = CreateContext(recentInteractions: interactions);
        context = context with { Session = session };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        // Layer 1 (full detail): last 3 interactions should be present in full.
        Assert.Contains("Recent Interactions", text);
        Assert.Contains("Interaction 8", text);
        Assert.Contains("Interaction 9", text);
        Assert.Contains("Interaction 10", text);

        // Layer 2 removed — Narrative fragments delegated to Session Memory (Slot 10).
        Assert.DoesNotContain("Earlier Interactions", text);
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
    public async Task IntensityPacingSlot_OutputsMergedBlock()
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
            },
        };

        Assert.True(slot.ShouldWrite(context));

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Intensity & Pacing:", text);
        Assert.Contains("Passionate", text);
        Assert.Contains("High emotional charge", text);
        Assert.Contains("Scene pacing:", text);
        Assert.Contains("Medium pace", text);
        Assert.Contains("Available positions:", text);
    }

    [Fact]
    public async Task IntensityPacingSlot_NoIntensityData_Omitted()
    {
        var slot = new IntensityPacingSlot(NullLogger<IntensityPacingSlot>.Instance);
        var context = CreateContext();
        context = context with { Intensity = new ResolvedIntensityData() };

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
}

