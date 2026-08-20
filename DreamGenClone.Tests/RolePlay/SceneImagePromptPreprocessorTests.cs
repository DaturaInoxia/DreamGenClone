using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;

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
