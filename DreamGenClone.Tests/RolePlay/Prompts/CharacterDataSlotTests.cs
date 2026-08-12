using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// B-078: CharacterDataSlot seduction-archetype injection contract tests.
/// Verifies the "Seduction style:" line is emitted only for OtherMan characters
/// with non-empty SeductionArchetypes, and falls back to role intent otherwise.
/// </summary>
public sealed class CharacterDataSlotTests
{
    private static readonly List<ScenarioCharacter> Characters = 
    [
        new("c1", "Becky", "Wife"),
        new("c2", "Ken", "Husband"),
        new("c3", "Dean", "OtherMan"),
    ];

    private static PromptBuildContext CreateContext(
        PromptVariant variant = PromptVariant.Character,
        ActorProfileKind actorKind = ActorProfileKind.Narrative,
        List<ScenarioCharacter>? characters = null)
    {
        var roster = characters ?? Characters;
        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            ScenarioId = "test-scenario",
            PersonaName = "You",
            PersonaDescription = "A brave adventurer.",
            PersonaRole = "Hero",
            MaxPromptChars = 35000,
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = NarrativePhase.BuildUp,
                CurrentSceneLocation = "The Living Room",
                ObservedTurnCount = 1,
            },
        };

        var actorProfile = new ActorProfile
        {
            Kind = actorKind,
            ActorName = "narrator",
            ActorRole = "narrator",
            PerspectiveMode = CharacterPerspectiveMode.ThirdPersonExternalOnly,
            PresentCharacterIds = roster.Select(c => c.Id).ToList(),
            AllCharacterIds = roster.Select(c => c.Id).ToList(),
        };

        return new PromptBuildContext
        {
            Session = session,
            ActorProfile = actorProfile,
            Variant = variant,
            Phase = "BuildUp",
            TurnIndex = 3,
            PositionInTurn = 2,
            TurnActorCount = 3,
            PromptText = "Continue naturally.",
            MaxPromptChars = 35000,
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
                Characters = roster,
                Locations = [],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = null,
                OpeningGuidanceText = null,
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
            RecentInteractions = [],
            PinnedInteractions = [],
            StagedInteractions = [],
            CharacterDetails = null,
        };
    }

    private static async Task<string> WriteCharacterDataSlotAsync(PromptBuildContext context)
    {
        var slot = new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance);
        return await slot.WriteAsync(context, CancellationToken.None);
    }

    // ── Archetype injection for OtherMan ─────────────────────────────────

    [Fact]
    public async Task OtherMan_WithArchetypes_EmitSeductionStyleLine()
    {
        var roster = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "Wife"),
            new("c2", "Ken", "Husband"),
            new("c3", "Dean", "OtherMan", new List<string> { "Competent", "Confidante" }),
        };
        var context = CreateContext(characters: roster);

        var text = await WriteCharacterDataSlotAsync(context);

        Assert.Contains("Dean (OtherMan)", text);
        Assert.Contains("Seduction style:", text);
        Assert.Contains("The Competent / Capable Man", text);
        Assert.Contains("The Confidante / Emotional Connection", text);
    }

    [Fact]
    public async Task OtherMan_NoArchetypes_NoSeductionStyleLine()
    {
        var context = CreateContext(); // Dean has no archetypes

        var text = await WriteCharacterDataSlotAsync(context);

        Assert.Contains("Dean (OtherMan)", text);
        Assert.DoesNotContain("Seduction style:", text);
    }

    [Fact]
    public async Task NonOtherMan_WithArchetypes_NoSeductionStyleLine()
    {
        var roster = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "Wife", new List<string> { "Charmer" }),
            new("c2", "Ken", "Husband", new List<string> { "Dominant" }),
            new("c3", "Dean", "OtherMan"),
        };
        var context = CreateContext(characters: roster);

        var text = await WriteCharacterDataSlotAsync(context);

        // Wife/Husband with archetypes must NOT get seduction style injection.
        Assert.Contains("Becky (Wife)", text);
        Assert.Contains("Ken (Husband)", text);
        Assert.DoesNotContain("Seduction style:", text);
    }

    [Fact]
    public async Task MultipleOtherMen_EachGetsIndependentGuidance()
    {
        var roster = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "Wife"),
            new("c2", "Dean", "OtherMan", new List<string> { "Charmer" }),
            new("c4", "Marco", "OtherMan", new List<string> { "Dominant" }),
        };
        var context = CreateContext(characters: roster);

        var text = await WriteCharacterDataSlotAsync(context);

        // Both OtherMen get their own Seduction style line.
        Assert.Contains("Seduction style: The Charmer / Smooth Talker", text);
        Assert.Contains("Seduction style: The Dominant / Assertive", text);
    }

    [Fact]
    public async Task UnknownArchetypeId_NoSeductionStyleLine()
    {
        var roster = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "Wife"),
            new("c3", "Dean", "OtherMan", new List<string> { "nonexistent" }),
        };
        var context = CreateContext(characters: roster);

        var text = await WriteCharacterDataSlotAsync(context);

        Assert.DoesNotContain("Seduction style:", text);
    }

    [Fact]
    public async Task NarrativeVariant_WithArchetypes_EmitSeductionStyleLine()
    {
        var roster = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "Wife"),
            new("c3", "Dean", "OtherMan", new List<string> { "Tease" }),
        };
        var context = CreateContext(variant: PromptVariant.Narrative, characters: roster);

        var text = await WriteCharacterDataSlotAsync(context);

        Assert.Contains("Seduction style: The Tease / Playful Provocateur", text);
    }
}
