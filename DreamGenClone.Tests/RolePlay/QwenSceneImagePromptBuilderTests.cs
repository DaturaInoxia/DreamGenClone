using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-008 route 1 (N1) — Qwen-Image-2.1 gets its own long-form compiler text instead of the SDXL-branded
/// natural-language brief. These tests pin the acceptance criteria: a Qwen compile must never emit Pony tags, a
/// quality booster, or the SDXL style tail, and the profile seed must hand the Qwen checkpoint the Qwen text.
/// </summary>
public sealed class QwenSceneImagePromptBuilderTests
{
    private static readonly string[] RequiredMarkers = ["Qwen-Image-2.1", "natural-language"];

    [Fact]
    public void BuildMessages_BeatPath_UsesTheQwenLongFormSystemPrompt()
    {
        var builder = new QwenSceneImagePromptBuilder();
        var session = new RolePlaySession { Id = "s1", Title = "Test", LastResolvedIntensityLabel = "SensualMature" };
        var interaction = new RolePlayInteraction
        {
            Id = "i1",
            ActorName = "Wife",
            Content = "She stepped closer, her hand sliding along his arm as the rain beat against the window."
        };
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = NarrativePhase.BuildUp,
            CurrentSceneLocation = "living room",
            CurrentTimeOfDay = TimeOfDay.Evening
        };
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024" };

        var (system, _) = builder.BuildMessages(
            session, interaction, state, settings, ImageContentPolicy.AdultAllowed, null, null);

        // The system prompt is the Qwen text itself, not a paraphrase and not the SDXL brief.
        Assert.Equal(SceneImageCompilerSystemPrompts.Qwen21Beat, system);
        Assert.NotEqual(SceneImageCompilerSystemPrompts.NaturalLanguageBeat, system);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Qwen21Prose_IsQwenLongForm_NotPony_NotSdxlBrief(bool canonical)
    {
        var text = SceneImageCompilerSystemPrompts.For(SceneImageModelFamily.QwenImage21, canonical);

        // It is the Qwen text, not the SDXL natural-language brief and not the Pony tag text.
        Assert.NotEqual(SceneImageCompilerSystemPrompts.NaturalLanguageBeat, text);
        Assert.NotEqual(SceneImageCompilerSystemPrompts.NaturalLanguageCanonical, text);
        Assert.NotEqual(SceneImageCompilerSystemPrompts.PonyTagsBeat, text);

        // The prohibitions that make a Qwen compile Pony-free, booster-free and free of the SDXL style tail are
        // stated in the text (they appear as prohibitions, not as cues to emit - so these are Contains, not
        // DoesNotContain).
        Assert.Contains("NEVER PONY VOCABULARY", text, StringComparison.Ordinal);
        Assert.Contains("no score_9", text, StringComparison.Ordinal);
        Assert.Contains("NO QUALITY BOOSTERS", text, StringComparison.Ordinal);
        Assert.Contains("never write masterpiece", text, StringComparison.Ordinal);
        Assert.Contains("natural skin texture", text, StringComparison.Ordinal);

        foreach (var marker in RequiredMarkers)
        {
            Assert.Contains(marker, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void For_QwenImage21_ReturnsTheQwenTexts_NotTheNaturalLanguageText()
    {
        // Route 1: Qwen-Image-2.1 is separated out from the shared natural-language text. The other three natural
        // language families (Sdxl, Flux, Api) still share the SDXL-branded brief - a documented follow-up for FLUX/API.
        Assert.Same(SceneImageCompilerSystemPrompts.Qwen21Beat, SceneImageCompilerSystemPrompts.For(SceneImageModelFamily.QwenImage21, canonical: false));
        Assert.Same(SceneImageCompilerSystemPrompts.Qwen21Canonical, SceneImageCompilerSystemPrompts.For(SceneImageModelFamily.QwenImage21, canonical: true));

        Assert.NotSame(SceneImageCompilerSystemPrompts.NaturalLanguageBeat, SceneImageCompilerSystemPrompts.For(SceneImageModelFamily.QwenImage21, canonical: false));
        Assert.NotSame(SceneImageCompilerSystemPrompts.NaturalLanguageCanonical, SceneImageCompilerSystemPrompts.For(SceneImageModelFamily.QwenImage21, canonical: true));
    }

    [Fact]
    public void Qwen21Beat_StatesTheLongFormBudget()
    {
        // The instruction mirrors the Qwen profile's envelope (MinChars 300 / MaxChars 1600). A budget written into the
        // instruction text and one enforced by the evaluator that disagree is a cell that can never pass.
        var text = SceneImageCompilerSystemPrompts.Qwen21Beat;

        Assert.Contains("300 and 1600 characters", text, StringComparison.Ordinal);
        Assert.Contains("20 sentences", text, StringComparison.Ordinal);
    }
}
