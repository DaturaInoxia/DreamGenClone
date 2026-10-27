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
    /// <summary>
    /// The canonical path must choose its contract by what the bound images SUPPLY: the vendor documents a long
    /// "describe the whole picture" shape for a text-only request and an anchored "nothing invented, point at the image"
    /// shape when images carry part of it. One contract for both regimes is what made the compiler invent.
    /// </summary>
    [Fact]
    public void AnchoredContract_IsTheShortSlotOrderedForm_NotNullTheLongFormEssay()
    {
        var coverage = new IntentReferenceCoverage(
            Setting: true, Appearance: true, Clothing: false, Pose: false, Objects: false);

        var text = SceneImageCompilerSystemPrompts.Qwen21CanonicalAnchored(coverage);

        // The operator's slot order, and the reference tags the model binds by position.
        Assert.Contains("WRITE THE PROMPT IN THIS ORDER", text, StringComparison.Ordinal);
        Assert.Contains("<image1>", text, StringComparison.Ordinal);
        Assert.Contains("<image2>", text, StringComparison.Ordinal);
        // Length follows the remaining facts - the long-form sentence count must not appear.
        Assert.Contains("Length follows the facts that remain", text, StringComparison.Ordinal);
        Assert.DoesNotContain("400-500 words", text, StringComparison.Ordinal);
        // Only photographable facts.
        Assert.Contains("Never write a smell", text, StringComparison.Ordinal);
        // The classes an image supplies are stated as supplied; a class it does not supply is not.
        Assert.Contains("The room's structure arrives as a bound image", text, StringComparison.Ordinal);
        Assert.Contains("The face and body arrive as a bound image", text, StringComparison.Ordinal);
        Assert.DoesNotContain("What the person wears arrives as a bound image", text, StringComparison.Ordinal);
        // And the worked example, which is what the writing model actually imitates.
        Assert.Contains("EXAMPLE OF THE TARGET SHAPE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnchoredContract_WithNoCoverage_StatesNoSuppliedClass()
    {
        var text = SceneImageCompilerSystemPrompts.Qwen21CanonicalAnchored(IntentReferenceCoverage.None);

        Assert.DoesNotContain("arrives as a bound image", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The worked example must not carry tag NUMBERS. Measured 2026-10-27 (SceneImage a53cd560): the example was written
    /// for a one-person scene, where the place really is &lt;image2&gt;, and a two-person scene binds three images with the
    /// place at &lt;image3&gt;. The local compiler copied the example's number instead of its own REFERENCE IMAGES block,
    /// so the caption pointed the shed at a person's body plate while the appended anchor pointed it at &lt;image3&gt;.
    /// </summary>
    [Fact]
    public void AnchoredContract_DoesNotPutTagNumbersInItsWorkedExample()
    {
        var coverage = new IntentReferenceCoverage(
            Setting: true, Appearance: true, Clothing: false, Pose: false, Objects: false);

        var text = SceneImageCompilerSystemPrompts.Qwen21CanonicalAnchored(coverage);
        var example = text[text.IndexOf("EXAMPLE OF THE TARGET SHAPE", StringComparison.Ordinal)..];

        // No number in the example can be copied, because the example carries none.
        Assert.DoesNotContain("<image", example, StringComparison.Ordinal);
        Assert.DoesNotContain("shed <image", text, StringComparison.Ordinal);
        // No bracketed placeholder either: the compiler echoed "[the person's tag]" into a live caption
        // (SceneImage b5ea6fb4), which bound no image at all.
        Assert.DoesNotContain("[the person's tag]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("shed [", text, StringComparison.Ordinal);
        // The numbering is stated to come from the request instead, including the two-person case that broke.
        Assert.Contains("THE NUMBER COMES FROM THE REQUEST", text, StringComparison.Ordinal);
        Assert.Contains("with two people bound", text, StringComparison.Ordinal);
        // And the tags must be written as plain text, not left as a note to self.
        Assert.Contains("WRITE EACH TAG AS PLAIN TEXT", text, StringComparison.Ordinal);
    }

    private static readonly string[] RequiredMarkers = ["ONE continuous paragraph", "third-person"];

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

        // The prohibitions are stated as literal tokens to avoid, not as a dialect NAME: "no Pony vocabulary" tells the
        // writing model nothing it can act on, while the token list does.
        Assert.Contains("comma-separated keyword lists", text, StringComparison.Ordinal);
        Assert.Contains("score_9", text, StringComparison.Ordinal);
        Assert.Contains("Do not write quality", text, StringComparison.Ordinal);
        Assert.Contains("masterpiece, best quality", text, StringComparison.Ordinal);
        Assert.Contains("natural skin texture", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pony", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SDXL", text, StringComparison.Ordinal);

        // What the writer model actually needs: the target SHAPE, worked once, and the camera-holder exclusion rule.
        Assert.Contains("WHAT MAKES A PROMPT OF THIS KIND GOOD", text, StringComparison.Ordinal);
        Assert.Contains("EXAMPLE OF THE TARGET SHAPE", text, StringComparison.Ordinal);
        Assert.Contains("CAMERA HOLDER'S VISIBLE BODY", text, StringComparison.Ordinal);
        // The target model's name is engineering metadata; it is not an instruction the writing model can act on.
        Assert.DoesNotContain("Qwen-Image-2.1", text, StringComparison.Ordinal);

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
        // The LENGTH the vendor's dialect asks for, and the SHAPE it asks for. The character range lives on the target
        // checkpoint's profile (MinChars/MaxChars), not in this text: a range written here that disagrees with the
        // profile is a contract no output can satisfy, and the model resolves it by writing a thin caption that drops
        // the operator's own element text (measured 2026-10-23).
        var text = SceneImageCompilerSystemPrompts.Qwen21Beat;

        Assert.Contains("20 sentences", text, StringComparison.Ordinal);
        Assert.Contains("400-500 words", text, StringComparison.Ordinal);
        Assert.Contains("ONE continuous paragraph", text, StringComparison.Ordinal);
        Assert.DoesNotContain("characters, written as one continuous paragraph", text, StringComparison.Ordinal);
    }
}
