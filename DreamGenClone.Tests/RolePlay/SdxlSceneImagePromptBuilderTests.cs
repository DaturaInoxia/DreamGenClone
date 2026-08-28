using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SdxlSceneImagePromptBuilderTests
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

    private static SceneImageBeat MakeThreeCharacterBeat() => new()
    {
        SchemaVersion = SceneImageBeatAnalysisService.CurrentSchemaVersion,
        BeatId = "b1",
        Label = "Encounter",
        VisualDescription = "Becky and Dean embrace in the living room while Ken watches from the porch.",
        Location = "living room",
        TimeOfDay = "evening",
        Lighting = "warm lamp light",
        Environment = "living room near the front door",
        Mood = "tense",
        Characters =
        [
            new SceneImageBeatCharacter { Name = "Becky", Involvement = "active", PhysicalLocation = "living room", Position = "center", ActionOrObservation = "embraces Dean", Sightline = "toward Dean", VisibleCharacterNames = ["Dean"], Clothing = "yellow dress" },
            new SceneImageBeatCharacter { Name = "Dean", Involvement = "active", PhysicalLocation = "living room", Position = "center", ActionOrObservation = "holds Becky", Sightline = "toward Becky", VisibleCharacterNames = ["Becky"], Clothing = "black shirt" },
            new SceneImageBeatCharacter { Name = "Ken", Involvement = "observer", PhysicalLocation = "porch", Position = "outside", ActionOrObservation = "watches through the window", Sightline = "looking in", VisibleCharacterNames = ["Becky", "Dean"], Clothing = "gray shirt" }
        ]
    };

    private readonly SdxlSceneImagePromptBuilder _preprocessor = new();

    [Fact]
    public void BuildMessages_SfwPolicy_ClampsExplicitness()
    {
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-explicit", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_AdultPolicy_AllowsExplicit()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.DoesNotContain(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NarrativePhase.Opening, "safe: fully clothed")]
    [InlineData(NarrativePhase.BuildUp, "safe: fully clothed")]
    [InlineData(NarrativePhase.Committed, "questionable: partially undressed")]
    [InlineData(NarrativePhase.Approaching, "questionable: partially undressed")]
    [InlineData(NarrativePhase.Climax, "explicit: nude bodies")]
    [InlineData(NarrativePhase.Reset, "questionable: partially undressed")]
    public void BuildMessages_ExplicitnessProse_FollowsNarrativePhase(NarrativePhase phase, string expectedProse)
    {
        var state = MakeState();
        state.CurrentPhase = phase;
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), state, settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.Contains(expectedProse, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Explicitness level: {expectedProse}", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_SfwPolicy_ForcesSafe_RegardlessOfClimaxPhase()
    {
        var state = MakeState();
        state.CurrentPhase = NarrativePhase.Climax;
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), state, settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains("safe: fully clothed, wholesome, non-explicit", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit: nude bodies", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_SystemPrompt_IsSdxlExpert_NaturalLanguage_NoPonyTags()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, _) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        // SDXL/Juggernaut expert: natural-language photography brief.
        Assert.Contains("SDXL", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural-language", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("35mm", system, StringComparison.OrdinalIgnoreCase);

        // It teaches the model NOT to use Pony vocabulary ...
        Assert.Contains("no score_9", system, StringComparison.Ordinal);
        Assert.Contains("no rating_explicit", system, StringComparison.Ordinal);

        // ... and never instructs the model to EMIT Pony quality/rating/count tags (unlike Pony).
        Assert.DoesNotContain("ALWAYS start the prompt with the full quality tag string", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Immediately after the quality tags, add the rating tag", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_JsonEnvelope_ExtractsPromptAndExcerpt()
    {
        var raw = JsonSerializer.Serialize(new { prompt = "a photorealistic couple on a beach", excerpt = "the golden hour" });
        var result = _preprocessor.ParseOutput(raw);

        Assert.Equal("a photorealistic couple on a beach", result.Prompt);
        Assert.Equal("the golden hour", result.Excerpt);
    }

    [Fact]
    public void ParseOutput_PlainText_UsesWholeOutput()
    {
        var result = _preprocessor.ParseOutput("photorealistic man and woman on a lakeside beach");

        Assert.Equal("photorealistic man and woman on a lakeside beach", result.Prompt);
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
        var raw = new string('x', SdxlSceneImagePromptBuilder.OutputPromptMaxChars + 1);
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput(raw));
    }

    [Fact]
    public void BuildDeterministicBeatNegativePrompt_UsesSdxlGuardSet()
    {
        var negative = _preprocessor.BuildDeterministicBeatNegativePrompt(MakeThreeCharacterBeat(), SceneImagePovFramer.Omniscient);

        // SDXL needs the heavier guard set (limb/leg artifacts, censored genitals, non-photo styles).
        Assert.Contains("four legs", negative, StringComparison.Ordinal);
        Assert.Contains("fused legs", negative, StringComparison.Ordinal);
        Assert.Contains("blurry genitals", negative, StringComparison.Ordinal);
        Assert.Contains("censored", negative, StringComparison.Ordinal);
        Assert.Contains("cartoon", negative, StringComparison.Ordinal);
        Assert.Contains("bad anatomy", negative, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeterministicBeatNegativePrompt_ExcludesAbsentCharacter()
    {
        // From Becky's POV, Dean is in frame and Ken (porch observer) is not.
        var negative = _preprocessor.BuildDeterministicBeatNegativePrompt(MakeThreeCharacterBeat(), "Becky");

        Assert.Contains("Ken absent from frame", negative, StringComparison.OrdinalIgnoreCase);
        // Visible characters are not excluded.
        Assert.DoesNotContain("Becky absent from frame", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dean absent from frame", negative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SfwClampSuffix_DiffersFromPony()
    {
        // The SDXL prose clamp is intentionally distinct from the Pony clamp suffix.
        Assert.NotEqual(PonySceneImagePromptBuilder.SfwClampSuffix, _preprocessor.SfwClampSuffix);
    }
}
