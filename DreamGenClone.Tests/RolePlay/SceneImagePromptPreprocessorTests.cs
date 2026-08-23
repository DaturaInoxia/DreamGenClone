using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImagePromptPreprocessorTests
{
    private static RolePlaySession MakeSession() => new()
    {
        Id = "s1",
        Title = "Test",
        LastResolvedIntensityLabel = "SensualMature"
    };

    private static RolePlayInteraction MakeInteraction() => new()
    {
        Id = "i1",
        ActorName = "Wife",
        Content = "She stepped closer, her hand sliding along his arm as the rain beat against the window."
    };

    private static AdaptiveScenarioState MakeState() => new()
    {
        CurrentPhase = NarrativePhase.BuildUp,
        CurrentSceneLocation = "living room",
        CurrentTimeOfDay = TimeOfDay.Evening
    };

    private readonly PonySceneImagePromptBuilder _preprocessor = new();

    [Fact]
    public void BuildMessages_SfwPolicy_ClampsExplicitness()
    {
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains(PonySceneImagePromptBuilder.SfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-explicit", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_AdultPolicy_AllowsExplicit()
    {
        var settings = new SceneImageStudioSettings { Style = "anime", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.DoesNotContain(PonySceneImagePromptBuilder.SfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_IncludesSceneContextAndSettings()
    {
        var settings = new SceneImageStudioSettings { Style = "cartoon", ImageSize = "768x768" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains("cartoon", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("living room", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildUp", user);
        Assert.Contains("Wife", user);
        Assert.Contains(MakeInteraction().Content[..40], user);
    }

    [Fact]
    public void BuildMessages_ExcerptOverride_UsedOverInteractionContent()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, "a custom passage", null);

        Assert.Contains("a custom passage", user);
        Assert.DoesNotContain(MakeInteraction().Content[..20], user);
    }

    [Fact]
    public void BuildMessages_RefineInstruction_Included()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, "more atmospheric");

        Assert.Contains("more atmospheric", user);
    }

    [Fact]
    public void BuildMessages_EmptyInteraction_UsesContextFallback()
    {
        var interaction = MakeInteraction();
        interaction.Content = "   ";
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), interaction, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.Contains("empty", user, StringComparison.OrdinalIgnoreCase);
        // Falls back to scene context (setting present) rather than a blank moment.
        Assert.Contains("living room", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_VeryLongInteraction_TruncatesToLimit()
    {
        var interaction = MakeInteraction();
        interaction.Content = new string('x', PonySceneImagePromptBuilder.InputExcerptMaxChars + 500);
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), interaction, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        // The moment line is truncated; the full content block cannot exceed the excerpt cap.
        Assert.DoesNotContain(new string('x', PonySceneImagePromptBuilder.InputExcerptMaxChars + 1), user);
        Assert.True(user.Length < interaction.Content.Length);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_IncludesActorVisualIdentity()
    {
        var characters = new List<Character>
        {
            new()
            {
                Id = "c1",
                Name = "Wife",
                Gender = "Female",
                PhysicalAttributes = new PhysicalAttributes
                {
                    Age = "32",
                    HairColour = "auburn",
                    HairStyle = "shoulder-length",
                    EyeColour = "green",
                    BodyType = "athletic",
                    SkinTone = "fair"
                }
            }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.Contains("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auburn", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("green", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("athletic", user, StringComparison.OrdinalIgnoreCase);
        // The likeness directive tells the pre-processor to keep identity fixed.
        Assert.Contains("CHARACTER LIKENESS", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_ExcludesMeasurementsAndIntimate()
    {
        var characters = new List<Character>
        {
            new()
            {
                Id = "c1",
                Name = "Wife",
                Gender = "Female",
                PhysicalAttributes = new PhysicalAttributes
                {
                    HairColour = "black",
                    EyeColour = "brown",
                    BustSize = "large",
                    EndowmentLength = "large",
                    VaginalTightness = "tight"
                }
            }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        // Visual identity present.
        Assert.Contains("black", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brown", user, StringComparison.OrdinalIgnoreCase);
        // Measurements + intimate fields must NOT leak into the image prompt.
        Assert.DoesNotContain("Bust", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vaginal", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Endowment", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_NoCharacters_NoBlock()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.DoesNotContain("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_NoAppearanceData_NoBlock()
    {
        // Actor has no physical attributes and no description → no appearance block.
        var characters = new List<Character> { new() { Id = "c1", Name = "Wife" } };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.DoesNotContain("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_DescriptionFallback_WhenNoStructuredAttributes()
    {
        var characters = new List<Character>
        {
            new() { Id = "c1", Name = "Wife", Description = "a tall woman with striking blue eyes and long blonde hair" }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.Contains("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blonde", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blue eyes", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_PersonaIncluded_WhenPresentInEncounter()
    {
        var session = MakeSession();
        session.PersonaName = "Ken";
        session.PersonaPhysicalAttributes = new PhysicalAttributes
        {
            Height = "6'1\"",
            HairColour = "dark brown",
            EyeColour = "hazel",
            BodyType = "lean"
        };
        // Ken is actively in the encounter → the persona participates and is included.
        var state = MakeState();
        state.CharacterEncounterStates["Ken"] = new CharacterEncounterState { IsHavingSex = true };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            session, MakeInteraction(), state, settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.Contains("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ken", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hazel", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterAppearance_PersonaExcluded_WhenNotPresent()
    {
        var session = MakeSession();
        session.PersonaName = "Ken";
        session.PersonaPhysicalAttributes = new PhysicalAttributes
        {
            Height = "6'1\"",
            HairColour = "dark brown",
            EyeColour = "hazel",
            BodyType = "lean"
        };
        // Ken is NOT present (no location, no encounter state, not named in text) → excluded.
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            session, MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.DoesNotContain("Ken", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_IncludesSiblingInteractions()
    {
        var selected = MakeInteraction();
        var sibling = new RolePlayInteraction
        {
            Id = "i2",
            ActorName = "Dean",
            Content = "He reached for her, the rain loud on the glass."
        };
        var narrative = new RolePlayInteraction
        {
            Id = "i3",
            ActorName = "Narrative",
            Content = "The room was dim, lit only by the storm outside."
        };
        var fullTurn = new FullTurnContext
        {
            Interactions = [selected, sibling, narrative],
            SelectedInteraction = selected,
            NarrativeInteraction = narrative
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        // The full-turn context block includes the sibling + narrative interactions.
        Assert.Contains("FULL TURN CONTEXT", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dean", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("storm", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_NoSiblings_NoContextBlock()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext
        {
            Interactions = [selected],
            SelectedInteraction = selected,
            NarrativeInteraction = null
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.DoesNotContain("FULL TURN CONTEXT", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_PovFraming_Included()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext
        {
            Interactions = [selected],
            SelectedInteraction = selected,
            NarrativeInteraction = null
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var beat = new SceneImageBeat
        {
            SchemaVersion = 3,
            BeatId = "b1",
            Label = "Watching",
            VisualDescription = "Becky stands by the window while Dean watches.",
            Location = "bedroom",
            TimeOfDay = "evening",
            Lighting = "amber window light",
            Environment = "bedroom beside the kitchen",
            Mood = "tense",
            Characters =
            [
                new SceneImageBeatCharacter { Name = "Becky", Involvement = "active", PhysicalLocation = "bedroom", Position = "bedroom window", ActionOrObservation = "stands by the window", Sightline = "toward the kitchen", VisibleCharacterNames = ["Dean"], Clothing = "yellow dress" },
                new SceneImageBeatCharacter { Name = "Dean", Involvement = "observer", PhysicalLocation = "kitchen", Position = "kitchen counter", ActionOrObservation = "watches Becky", Sightline = "through the partly open bedroom door", VisibleCharacterNames = ["Becky"], Clothing = "casual clothing" }
            ]
        };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null, beat, pov: "Dean");

        Assert.Contains("AUTHORITATIVE RENDER BRIEF", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partly open bedroom door", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VISIBLE CAST", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Becky [active]", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dean [observer]", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_CharacterPov_RemoteObserverIdentityExcluded()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext { Interactions = [selected], SelectedInteraction = selected };
        var beat = MakeThreeCharacterBeat();
        var characters = MakeThreeCharactersWithDistinctAppearances();
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters, beat, pov: "Dean");

        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("Becky: Appearance", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hair: auburn", user, StringComparison.OrdinalIgnoreCase);
        // Dean is a remote observer (doorway ≠ living room) → off-camera, not in the depicted cast.
        Assert.DoesNotContain("Dean: Appearance", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_CharacterPov_ActiveParticipantOwnBodyIncluded()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext { Interactions = [selected], SelectedInteraction = selected };
        var beat = MakeThreeCharacterBeat() with
        {
            Location = "doorway",
            Characters = MakeThreeCharacterBeat().Characters
                .Select(character => character with { PhysicalLocation = "doorway" })
                .ToList()
        };
        var characters = MakeThreeCharactersWithDistinctAppearances();
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters, beat, pov: "Dean");

        Assert.Contains("Dean: Appearance", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FullTurn_Omniscient_ExcludesRemoteObserverIdentity()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext { Interactions = [selected], SelectedInteraction = selected };
        var beat = MakeThreeCharacterBeat();
        var characters = MakeThreeCharactersWithDistinctAppearances();
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters, beat, pov: SceneImagePovFramer.Omniscient);

        Assert.Contains("Becky: Appearance", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dean: Appearance", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ken: Appearance", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hair: auburn", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REMOTE OBSERVER CUES", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anonymous", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_VisibleIdentityAndWardrobe_AreIdenticalAcrossPovs()
    {
        var beat = MakeThreeCharacterBeat();
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", AspectRatio = "16:9" };

        var deanPov = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), beat, "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            MakeThreeCharactersWithDistinctAppearances());
        var kenPov = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), beat, "Ken", settings, ImageContentPolicy.AdultAllowed, null,
            MakeThreeCharactersWithDistinctAppearances());

        var deanPrompt = deanPov.ToLowerInvariant();
        var kenPrompt = kenPov.ToLowerInvariant();
        Assert.Contains("becky: appearance", deanPrompt, StringComparison.Ordinal);
        Assert.Contains("becky: appearance", kenPrompt, StringComparison.Ordinal);
        // Adult-allowed deterministic prompts force nudity over profile clothing.
        Assert.Contains("naked", deanPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_CharacterPov_PositiveSectionsContainOnlyVisibleCast()
    {
        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), "Ken",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());

        Assert.Contains("Becky: Appearance", prompt, StringComparison.Ordinal);
        Assert.Contains("naked", prompt, StringComparison.Ordinal);
        // The POV character (Ken) is not a subject here; their identity is not emitted as a cast line.
        Assert.DoesNotContain("Ken: Appearance", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_CharacterPov_IncludesOwnBody()
    {
        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), "Dean",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());

        // First-person: Dean's own body is visible in frame.
        Assert.Contains("Dean's own body", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_Omniscient_RemoteObserverIsAnonymousOccludedCue()
    {
        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), SceneImagePovFramer.Omniscient,
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());

        Assert.Contains("Becky: Appearance", prompt, StringComparison.Ordinal);
        // Ken is the remote observer: anonymous/occluded, not a detailed cast identity.
        Assert.DoesNotContain("Ken: Appearance", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wardrobe: gray shirt", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_CharacterPov_UsesOnlyPrimaryActiveSetting()
    {
        var beat = MakeThreeCharacterBeat() with
        {
            Location = "trailer bedroom",
            Environment = "bed, open window, moonlight through blinds"
        };

        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), beat, "Ken",
            new SceneImageStudioSettings { Style = "cartoon", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());

        Assert.Contains("trailer bedroom", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bed, open window", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shed", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_InjectsOptionPlaceholders_NotBakedValues()
    {
        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), "Ken",
            new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());

        // Style/size are injectable placeholders, substituted at render time.
        Assert.Contains("{{style}}", prompt, StringComparison.Ordinal);
        Assert.Contains("{{size}}", prompt, StringComparison.Ordinal);
        // Omniscient camera angle is a placeholder too.
        var omni = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), SceneImagePovFramer.Omniscient,
            new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", OmniscientAngle = "High wide angle" },
            ImageContentPolicy.AdultAllowed, null, MakeThreeCharactersWithDistinctAppearances());
        Assert.Contains("{{angle}}", omni, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_ExplicitParticipantPov_StatesPenetrationAndOmitsRemoteObserver()
    {
        var beat = new SceneImageBeat
        {
            SchemaVersion = 3,
            BeatId = "b1",
            Label = "Sex",
            VisualDescription = "Becky on all fours, Dean kneeling behind her, thrusting, his cock inside her. Ken watches through the blinds.",
            Location = "bedroom",
            TimeOfDay = "night",
            Lighting = "moonlight",
            Environment = "bedroom bed",
            Mood = "intense",
            Characters =
            [
                new SceneImageBeatCharacter { Name = "Becky", Involvement = "active", PhysicalLocation = "bedroom", Position = "on all fours on the bed", ActionOrObservation = "back arched", Sightline = "gazing forward", VisibleCharacterNames = ["Dean"], Clothing = "naked" },
                new SceneImageBeatCharacter { Name = "Dean", Involvement = "active", PhysicalLocation = "bedroom", Position = "kneeling behind Becky", ActionOrObservation = "thrusting into her", Sightline = "looking at her back", VisibleCharacterNames = ["Becky"], Clothing = "naked" },
                new SceneImageBeatCharacter { Name = "Ken", Involvement = "observer", PhysicalLocation = "porch", Position = "outside", ActionOrObservation = "watching through window", Sightline = "looking in", VisibleCharacterNames = ["Becky","Dean"], Clothing = "shorts" }
            ]
        };
        var characters = MakeThreeCharactersWithDistinctAppearances();

        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), beat, "Dean",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null, characters);

        // Explicit penetration anatomy is stated.
        Assert.Contains("penetrating Becky", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("penis visible entering her vagina", prompt, StringComparison.OrdinalIgnoreCase);
        // Remote observer (Ken) detail does NOT leak into Dean's participant POV.
        Assert.DoesNotContain("Ken", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("porch", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_MissingPovCharacter_FailsExplicitly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), "Absent",
            new SceneImageStudioSettings(), ImageContentPolicy.AdultAllowed, null,
            MakeThreeCharactersWithDistinctAppearances()));

        Assert.Contains("not associated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeterministicBeatPrompt_VisualIdentity_DoesNotDuplicateBodyType()
    {
        var characters = MakeThreeCharactersWithDistinctAppearances().ToList();
        characters.Single(character => character.Name == "Becky").PhysicalAttributes!.BodyType = "athletic";

        var prompt = _preprocessor.BuildDeterministicBeatPrompt(
            MakeSession(), MakeThreeCharacterBeat(), "Ken",
            new SceneImageStudioSettings(), ImageContentPolicy.AdultAllowed, null, characters);

        Assert.Equal(1, CountOccurrences(prompt, "Body type"));
    }

    [Fact]
    public void BuildMessages_FullTurn_NoPov_NoFramingLine()
    {
        var selected = MakeInteraction();
        var fullTurn = new FullTurnContext
        {
            Interactions = [selected],
            SelectedInteraction = selected,
            NarrativeInteraction = null
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), fullTurn, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.DoesNotContain("POV FRAMING", user, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneImageBeat MakeThreeCharacterBeat() => new()
    {
        SchemaVersion = 3,
        BeatId = "b1",
        Label = "Shared moment",
        VisualDescription = "Becky stands in the room while Dean and Ken observe.",
        Location = "living room",
        TimeOfDay = "evening",
        Lighting = "warm light",
        Environment = "small living room",
        Mood = "tense",
        Characters =
        [
            new SceneImageBeatCharacter { Name = "Becky", Involvement = "active", PhysicalLocation = "living room", Position = "center", ActionOrObservation = "stands", Sightline = "toward Dean", VisibleCharacterNames = ["Dean"], Clothing = "yellow dress" },
            new SceneImageBeatCharacter { Name = "Dean", Involvement = "observer", PhysicalLocation = "doorway", Position = "threshold", ActionOrObservation = "watches", Sightline = "toward Becky and Ken", VisibleCharacterNames = ["Becky", "Ken"], Clothing = "black shirt" },
            new SceneImageBeatCharacter { Name = "Ken", Involvement = "observer", PhysicalLocation = "window alcove", Position = "window", ActionOrObservation = "watches", Sightline = "toward Becky", VisibleCharacterNames = ["Becky"], Clothing = "gray shirt" }
        ]
    };

    private static IReadOnlyList<Character> MakeThreeCharactersWithDistinctAppearances() =>
    [
        new Character { Id = "becky", Name = "Becky", PhysicalAttributes = new PhysicalAttributes { HairColour = "auburn" } },
        new Character { Id = "dean", Name = "Dean", PhysicalAttributes = new PhysicalAttributes { HairColour = "black" } },
        new Character { Id = "ken", Name = "Ken", PhysicalAttributes = new PhysicalAttributes { HairColour = "silver" } }
    ];

    private static int CountOccurrences(string value, string search)
        => (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) / search.Length;

    [Fact]
    public void BuildMessages_CharacterClothing_FromProfileClothingStyle()
    {
        var characters = new List<Character>
        {
            new()
            {
                Id = "c1",
                Name = "Wife",
                PhysicalAttributes = new PhysicalAttributes
                {
                    ClothingStyle = "elegant black dress"
                }
            }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.Contains("CHARACTER CLOTHING", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elegant black dress", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterClothing_FallsBackToDefaultClothing()
    {
        var characters = new List<Character>
        {
            new()
            {
                Id = "c1",
                Name = "Wife",
                PhysicalAttributes = new PhysicalAttributes
                {
                    DefaultClothing = "a simple white blouse and jeans"
                }
            }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.Contains("CHARACTER CLOTHING", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("white blouse", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterClothing_TurnDataWinsOverProfile()
    {
        var characters = new List<Character>
        {
            new()
            {
                Id = "c1",
                Name = "Wife",
                PhysicalAttributes = new PhysicalAttributes
                {
                    ClothingStyle = "elegant black dress"
                }
            }
        };
        // The turn text describes the Wife wearing a red dress → turn data wins.
        var interaction = MakeInteraction();
        interaction.Content = "The Wife was wearing a red silk dress as she stepped closer.";
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), interaction, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.Contains("CHARACTER CLOTHING", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("red silk dress", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("elegant black dress", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_CharacterClothing_NoClothing_NoBlock()
    {
        var characters = new List<Character>
        {
            new() { Id = "c1", Name = "Wife" }
        };
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, characters);

        Assert.DoesNotContain("CHARACTER CLOTHING", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_JsonEnvelope_ReturnsPromptAndExcerpt()
    {
        var result = _preprocessor.ParseOutput("{\"prompt\":\"a dramatic scene\",\"excerpt\":\"the moment\"}");
        Assert.Equal("a dramatic scene", result.Prompt);
        Assert.Equal("the moment", result.Excerpt);
    }

    [Fact]
    public void ParseOutput_PlainText_UsesWholeOutput()
    {
        var result = _preprocessor.ParseOutput("a cinematic, dramatic scene");
        Assert.Equal("a cinematic, dramatic scene", result.Prompt);
        Assert.Equal(string.Empty, result.Excerpt);
    }

    [Fact]
    public void ParseOutput_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput("   "));
    }

    [Fact]
    public void ParseOutput_Overlong_Throws()
    {
        var longPrompt = new string('a', PonySceneImagePromptBuilder.OutputPromptMaxChars + 1);
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput(longPrompt));
    }
}
