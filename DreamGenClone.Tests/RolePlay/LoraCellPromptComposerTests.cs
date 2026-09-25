using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The composer is the last place a prompt can go wrong before it costs a render, so it refuses rather than
/// guesses: an unfilled slot throws naming itself, and a caption can never carry an invariant. Both are
/// cheap to check here and expensive to discover in a training set.
/// </summary>
public sealed class LoraCellPromptComposerTests
{
    private const string RenderTemplate =
        "Photorealistic photograph of {BodyCard}. {Facing}. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}.";

    private const string CaptionTemplate =
        "{TriggerToken}, {Wardrobe}, {Angle}, {Distance}, {Pose}, {Expression}, {Lighting}, {Background}";

    [Fact]
    public void ComposeRenderPrompt_FillsEverySlotFromThePlan()
    {
        var plan = Plan();
        var record = plan.Records[0];

        var prompt = LoraCellPromptComposer.ComposeRenderPrompt(plan, record, BodyCard, RenderTemplate);

        Assert.DoesNotContain("{", prompt, StringComparison.Ordinal);
        Assert.Contains(BodyCard, prompt, StringComparison.Ordinal);
        Assert.Contains("facing the camera straight on", prompt, StringComparison.Ordinal);
        Assert.Contains("wearing a plain t-shirt and jeans", prompt, StringComparison.Ordinal);
        Assert.Contains("standing", prompt, StringComparison.Ordinal);
        Assert.Contains("a plain neutral wall", prompt, StringComparison.Ordinal);
    }

    /// <summary>The invariant card is pasted verbatim — never paraphrased, never reworded by the app.</summary>
    [Fact]
    public void ComposeRenderPrompt_PastesTheBodyCardVerbatim()
    {
        var plan = Plan();
        var record = plan.Records[0];

        var prompt = LoraCellPromptComposer.ComposeRenderPrompt(plan, record, BodyCard, RenderTemplate);

        Assert.Contains(BodyCard, prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A template whose slot nobody fills is a template bug. Sending the prompt anyway would ship the literal
    /// text "{Facing}" to the model, which is worse than not rendering.
    /// </summary>
    [Fact]
    public void ComposeRenderPrompt_RefusesToLeaveASlotUnfilled()
    {
        var plan = Plan();
        var record = plan.Records[0];

        var error = Assert.Throws<InvalidOperationException>(() => LoraCellPromptComposer.ComposeRenderPrompt(
            plan, record, BodyCard, RenderTemplate + " {PoseClause}"));

        Assert.Contains("{PoseClause}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeRenderPrompt_RequiresTheBodyCard()
    {
        var plan = Plan();

        var error = Assert.Throws<InvalidOperationException>(() => LoraCellPromptComposer.ComposeRenderPrompt(
            plan, plan.Records[0], "   ", RenderTemplate));

        Assert.Contains("body card", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The caption starts with the trigger token, and the token is the only word that carries identity.</summary>
    [Fact]
    public void ComposeCaption_PutsTheTriggerTokenFirst()
    {
        var plan = Plan();

        var caption = LoraCellPromptComposer.ComposeCaption(plan, plan.Records[0], CaptionTemplate);

        Assert.Equal(
            "ohwx-becky, clothed, front view, close-up, standing, a neutral relaxed expression, "
            + "dim indoor lighting with soft shadows, a plain neutral wall",
            caption);
        Assert.DoesNotContain("{", caption, StringComparison.Ordinal);
    }

    /// <summary>Every tag is comma-separated, because a trainer can only shuffle a caption that is tagged.</summary>
    [Fact]
    public void ComposeCaption_IsCommaSeparatedTags()
    {
        var plan = Plan();

        var caption = LoraCellPromptComposer.ComposeCaption(plan, plan.Records[0], CaptionTemplate);

        Assert.Equal(8, caption.Split(", ", StringSplitOptions.None).Length);
    }

    /// <summary>
    /// The caption describes the axes that vary and nothing else. No invariant feature may appear, because a
    /// described invariant stops binding to the token and starts being optional.
    /// </summary>
    [Theory]
    [InlineData("body shape")]
    [InlineData("proportions")]
    [InlineData("skin")]
    [InlineData("body hair")]
    [InlineData("pubic")]
    [InlineData("tattoo")]
    [InlineData("scar")]
    [InlineData("piercing")]
    public void ComposeCaption_NeverCarriesAnInvariant(string invariant)
    {
        var plan = Plan();

        foreach (var record in plan.Records)
        {
            var caption = LoraCellPromptComposer.ComposeCaption(plan, record, CaptionTemplate);
            Assert.DoesNotContain(invariant, caption, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The render prompt and the caption read the same axes, so a cell cannot claim two different states.</summary>
    [Fact]
    public void Compose_KeepsTheRenderAndTheCaptionInAgreement()
    {
        var plan = Plan();
        var unclothed = plan.Records.First(record => record.WardrobeState == LoraCoverageWardrobeState.Unclothed);

        var caption = LoraCellPromptComposer.ComposeCaption(plan, unclothed, CaptionTemplate);
        var prompt = LoraCellPromptComposer.ComposeRenderPrompt(plan, unclothed, BodyCard, RenderTemplate);

        Assert.Contains("nude", caption, StringComparison.Ordinal);
        Assert.Contains("completely unclothed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderTemplateKey_FollowsTheAngleAndTheDistance()
    {
        var plan = Plan();

        Assert.Equal(
            "lora.cell.render.front.close",
            LoraCellPromptComposer.RenderTemplateKey(plan.Records[0]));

        var full = plan.Records.First(record =>
            record.AngleFamily == LoraCoverageAngleFamily.Profile && record.Distance == LoraCoverageDistance.FullBody);
        Assert.Equal("lora.cell.render.profile.full", LoraCellPromptComposer.RenderTemplateKey(full));
    }

    private const string BodyCard =
        "50-year-old woman, 5'8\", curvy, full bust, soft waist, wide hips, fair smooth skin";

    private static CoveragePlan Plan()
    {
        var plan = new CoveragePlan
        {
            CharacterProfileId = "character-1",
            IdentityPackId = "pack-1",
            IdentityPackVersion = 9,
            TriggerToken = "ohwx-becky",
            TargetModelFamily = "biglust",
            SeedRangeStart = 41000,
            GeneratedUtc = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
            Vocabulary = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LoraCellWorkflowKeys.VocabularyFacingCamera] = "facing the camera straight on",
                [LoraCellWorkflowKeys.VocabularyFacingAway] = "with the back of the head toward the camera",
                [LoraCellWorkflowKeys.VocabularyOutfitCasual] = "wearing a plain t-shirt and jeans",
                [LoraCellWorkflowKeys.VocabularyOutfitUnclothed] =
                    "completely unclothed, with no clothing at all and nothing covering the body",
                [LoraCellWorkflowKeys.VocabularyPoseStanding] = "standing",
                [LoraCellWorkflowKeys.VocabularyPoseSitting] = "sitting",
                [LoraCellWorkflowKeys.VocabularyExpressionNeutral] = "a neutral relaxed expression",
                [LoraCellWorkflowKeys.VocabularyLightingIndoorDim] = "dim indoor lighting with soft shadows",
                [LoraCellWorkflowKeys.VocabularyBackgroundPlainWall] = "a plain neutral wall",
                [LoraCellWorkflowKeys.VocabularyWardrobeClothed] = "clothed",
                [LoraCellWorkflowKeys.VocabularyWardrobeUnclothed] = "nude",
                [LoraCellWorkflowKeys.VocabularyAngleFront] = "front view",
                [LoraCellWorkflowKeys.VocabularyAngleProfile] = "profile view",
                [LoraCellWorkflowKeys.VocabularyDistanceClose] = "close-up",
                [LoraCellWorkflowKeys.VocabularyDistanceHalf] = "half-body",
                [LoraCellWorkflowKeys.VocabularyDistanceFull] = "full-body"
            }
        };

        plan.Records.Add(new CoverageRecord
        {
            Key = "core.front.cu.1",
            Role = LoraCoverageCellRole.Core,
            AngleFamily = LoraCoverageAngleFamily.Front,
            AngleYawDeg = 0,
            FaceVisible = true,
            FaceCanonicalSlot = SceneImageReferenceFaceView.Front,
            BodyCanonicalSlot = SceneImageReferenceBodyView.Front,
            BodyState = SceneImageReferenceBodyState.Clothed,
            Distance = LoraCoverageDistance.CloseUp,
            WardrobeState = LoraCoverageWardrobeState.Clothed,
            PoseClass = LoraCoveragePoseClass.Standing,
            ExpressionKey = LoraCellWorkflowKeys.VocabularyExpressionNeutral,
            LightingKey = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
            BackgroundKey = LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
            OutfitKey = LoraCellWorkflowKeys.VocabularyOutfitCasual,
            Aspect = "1024x1024",
            Seed = 41000,
            Split = CharacterLoraDatasetSplit.Train
        });

        plan.Records.Add(new CoverageRecord
        {
            Key = "core.front.hb.1",
            Role = LoraCoverageCellRole.Core,
            AngleFamily = LoraCoverageAngleFamily.Front,
            AngleYawDeg = 0,
            FaceVisible = true,
            FaceCanonicalSlot = SceneImageReferenceFaceView.Front,
            BodyCanonicalSlot = SceneImageReferenceBodyView.Front,
            BodyState = SceneImageReferenceBodyState.Unclothed,
            Distance = LoraCoverageDistance.HalfBody,
            WardrobeState = LoraCoverageWardrobeState.Unclothed,
            PoseClass = LoraCoveragePoseClass.Sitting,
            ExpressionKey = LoraCellWorkflowKeys.VocabularyExpressionNeutral,
            LightingKey = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
            BackgroundKey = LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
            OutfitKey = LoraCellWorkflowKeys.VocabularyOutfitUnclothed,
            Aspect = "832x1216",
            Seed = 41001,
            Split = CharacterLoraDatasetSplit.Train
        });

        plan.Records.Add(new CoverageRecord
        {
            Key = "variation.behind",
            Role = LoraCoverageCellRole.Variation,
            AngleFamily = LoraCoverageAngleFamily.Profile,
            AngleYawDeg = -90,
            FaceVisible = true,
            FaceCanonicalSlot = SceneImageReferenceFaceView.ProfileLeft,
            BodyCanonicalSlot = SceneImageReferenceBodyView.ProfileLeft,
            BodyState = SceneImageReferenceBodyState.Clothed,
            Distance = LoraCoverageDistance.FullBody,
            WardrobeState = LoraCoverageWardrobeState.Clothed,
            PoseClass = LoraCoveragePoseClass.Standing,
            ExpressionKey = LoraCellWorkflowKeys.VocabularyExpressionNeutral,
            LightingKey = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
            BackgroundKey = LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
            OutfitKey = LoraCellWorkflowKeys.VocabularyOutfitCasual,
            Aspect = "832x1216",
            Seed = 41002,
            Split = CharacterLoraDatasetSplit.Validation
        });

        plan.Validate();
        return plan;
    }
}
