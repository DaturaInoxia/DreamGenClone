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

    private readonly SceneImagePromptPreprocessor _preprocessor = new();

    [Fact]
    public void BuildMessages_SfwPolicy_ClampsExplicitness()
    {
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains(SceneImagePromptPreprocessor.SfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
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

        Assert.DoesNotContain(SceneImagePromptPreprocessor.SfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
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
        interaction.Content = new string('x', SceneImagePromptPreprocessor.InputExcerptMaxChars + 500);
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            MakeSession(), interaction, MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        // The moment line is truncated; the full content block cannot exceed the excerpt cap.
        Assert.DoesNotContain(new string('x', SceneImagePromptPreprocessor.InputExcerptMaxChars + 1), user);
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
    public void BuildMessages_CharacterAppearance_PersonaIncluded_WhenDifferentFromActor()
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
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (_, user) = _preprocessor.BuildMessages(
            session, MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null, null);

        Assert.Contains("CHARACTER APPEARANCE", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ken", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hazel", user, StringComparison.OrdinalIgnoreCase);
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
        var longPrompt = new string('a', SceneImagePromptPreprocessor.OutputPromptMaxChars + 1);
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput(longPrompt));
    }
}
