using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-012 — the FREE prompt layer.
///
/// <para>
/// Two of these tests matter more than the rest. The first is the dialect check: Pony tags drafted into a
/// natural-language prompt is a defect that actually shipped (reported 2026-09-24), and it is exactly the kind of
/// thing a cell should catch for free rather than for a GPU spend. The second is that an UNDECLARED tolerance fails
/// rather than passes — a missing tolerance is a gap in the cell, and letting it skip the comparison would make every
/// sloppy cell look green.
/// </para>
/// </summary>
public sealed class ImagePromptConformanceEvaluatorTests
{
    private static ImageCompilerProfile SdxlProfile(int minChars = 10, int maxChars = 600, int maxTokens = 100) => new()
    {
        Id = "p-sdxl",
        CheckpointIdentifier = "juggernautXL_ragnarok.safetensors",
        DisplayName = "Juggernaut XL Ragnarok",
        Family = SceneImageModelFamily.Sdxl,
        PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        MinChars = minChars,
        MaxChars = maxChars,
        MaxTokens = maxTokens,
        PoseInText = ImagePoseInText.Forbidden,
        Negative = string.Empty,
        RequiredComponentsJson = """["subject","framing","lighting"]""",
        ForbiddenTokensJson = """["story-name","pony-tag"]""",
    };

    private static ImageCompilerProfile PonyProfile() => new()
    {
        Id = "p-pony",
        CheckpointIdentifier = "ponyDiffusionV6XL_v6.safetensors",
        DisplayName = "Pony V6 XL",
        Family = SceneImageModelFamily.Pony,
        PromptDialect = SceneImagePromptDialect.PonyV6Tags,
        MinChars = 10,
        MaxChars = 600,
        MaxTokens = 100,
        PoseInText = ImagePoseInText.SimpleOnly,
        Negative = "lowres, bad anatomy",
        NegativeSource = "pony-v6-prompting.instructions.md rules 8-9",
        RequiredComponentsJson = """["quality-tag-string"]""",
        ForbiddenTokensJson = """["natural-language-prose"]""",
    };

    private static ImagePromptCheck Check(ImagePromptConformanceResult result, string name) =>
        result.Checks.Single(check => check.Name == name);

    // ---- the defect that actually shipped ----------------------------------------------------------------

    [Fact]
    public void PonyVocabularyInANaturalLanguagePromptIsAFailure()
    {
        var result = ImagePromptConformanceEvaluator.Evaluate(
            "score_9, score_8_up, rating_explicit, 1girl, a woman lying on a bed",
            "a woman lying on a bed",
            0.1,
            SdxlProfile());

        var check = Check(result, "dialect-no-foreign-vocabulary");

        Assert.Equal(ImagePromptCheckOutcome.Fail, check.Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public void APonyPromptMustCarryItsQualityString()
    {
        var withoutQualityString = ImagePromptConformanceEvaluator.Evaluate(
            "rating_explicit, 1girl, a woman standing, front view, eye level", "a woman standing", 0.1, PonyProfile());

        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(withoutQualityString, "dialect-required-tags").Outcome);

        var withQualityString = ImagePromptConformanceEvaluator.Evaluate(
            "score_9, score_8_up, score_7_up, rating_explicit, 1girl, a woman standing, front view, eye level",
            "a woman standing", 0.1, PonyProfile());

        Assert.Equal(ImagePromptCheckOutcome.Pass, Check(withQualityString, "dialect-required-tags").Outcome);
    }

    // ---- universal rules ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("a woman on a bed\nsecond line", "single-paragraph")]
    [InlineData("```a woman on a bed```", "no-markdown-fence")]
    [InlineData("a woman on a bed, 1024x1024", "no-resolution-in-prompt")]
    [InlineData("a woman on a bed, 16:9 framing", "no-resolution-in-prompt")]
    [InlineData("a masterpiece photograph of a woman", "no-quality-boosters")]
    [InlineData("a woman on a bed with no clothing", "no-negation-phrasing")]
    public void TheUniversalRulesFailThePrompt(string compiled, string expectedFailingCheck)
    {
        var result = ImagePromptConformanceEvaluator.Evaluate(compiled, compiled, 0.1, SdxlProfile());

        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(result, expectedFailingCheck).Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public void ACleanPromptPassesEveryMechanicalCheck()
    {
        const string prompt =
            "a photograph of a woman lying on a bed, one hand resting on her thigh, warm low lamp light from the left, "
            + "shallow depth of field, 35mm";

        var result = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, 0.9, SdxlProfile());

        Assert.Empty(result.Failures);
        Assert.True(result.Passed);
    }

    // ---- budget ------------------------------------------------------------------------------------------

    [Fact]
    public void TheBudgetComesFromTheProfile_BothEnds()
    {
        const string prompt = "a photograph of a woman lying on a bed, warm low lamp light";

        var tooShort = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, 0.1, SdxlProfile(minChars: 500));
        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(tooShort, "budget-characters").Outcome);

        var tooLong = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, 0.1, SdxlProfile(maxChars: 20));
        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(tooLong, "budget-characters").Outcome);

        var inside = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, 0.1, SdxlProfile());
        Assert.Equal(ImagePromptCheckOutcome.Pass, Check(inside, "budget-characters").Outcome);
    }

    [Fact]
    public void TheTokenBudgetIsEnforcedAsAnApproximateProxy()
    {
        var prompt = string.Join(' ', Enumerable.Repeat("woman", 40));

        var result = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, 0.1, SdxlProfile(maxTokens: 10));

        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(result, "budget-tokens").Outcome);
    }

    // ---- similarity and the tolerance rule ---------------------------------------------------------------

    [Fact]
    public void AnUndeclaredToleranceFailsRatherThanSkippingTheComparison()
    {
        var profile = SdxlProfile();
        const string prompt = "a photograph of a woman lying on a bed";

        var result = ImagePromptConformanceEvaluator.Evaluate(prompt, prompt, requiredTolerance: null, profile);

        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(result, "similarity-to-expected").Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public void TheToleranceIsComparedAgainstTheMeasuredSimilarity()
    {
        const string compiled = "a photograph of a woman lying on a bed, warm lamp light";
        const string expected = "a photograph of a woman lying on a bed, cool window light";

        var strict = ImagePromptConformanceEvaluator.Evaluate(compiled, expected, 0.99, SdxlProfile());
        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(strict, "similarity-to-expected").Outcome);

        var lenient = ImagePromptConformanceEvaluator.Evaluate(compiled, expected, 0.3, SdxlProfile());
        Assert.Equal(ImagePromptCheckOutcome.Pass, Check(lenient, "similarity-to-expected").Outcome);
    }

    [Fact]
    public void SimilarityIsDeterministicAndSymmetricInRange()
    {
        const string a = "a woman lying on a bed, warm lamp light";
        const string b = "a woman lying on a bed";

        var first = ImagePromptConformanceEvaluator.Similarity(a, b);
        var second = ImagePromptConformanceEvaluator.Similarity(a, b);

        Assert.Equal(first, second);
        Assert.InRange(first, 0.0, 1.0);
        Assert.Equal(1.0, ImagePromptConformanceEvaluator.Similarity(a, a));
        Assert.Equal(0.0, ImagePromptConformanceEvaluator.Similarity(a, string.Empty));
    }

    // ---- the honest gap ----------------------------------------------------------------------------------

    [Fact]
    public void CategoryListsAreReportedAsUnverifiable_NotSilentlyPassed()
    {
        var result = ImagePromptConformanceEvaluator.Evaluate(
            "a photograph of a woman lying on a bed, warm lamp light", "a photograph of a woman lying on a bed, warm lamp light", 0.9, SdxlProfile());

        // They do not block a pass...
        Assert.True(result.Passed);

        // ...but they are NAMED, so the parts of a cell still judged by eye stay visible instead of implied.
        Assert.Contains(result.Unverifiable, check => check.Name == "required-components");
        Assert.Contains(result.Unverifiable, check => check.Name == "forbidden-tokens");
        Assert.Contains("subject", Check(result, "required-components").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPromptFailsEverythingItCan()
    {
        var result = ImagePromptConformanceEvaluator.Evaluate(string.Empty, "a woman lying on a bed", 0.1, SdxlProfile());

        Assert.Equal(ImagePromptCheckOutcome.Fail, Check(result, "non-empty").Outcome);
        Assert.False(result.Passed);
    }
}
