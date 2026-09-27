using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The EXACT prompt each family compiles for one fixed brief (B-122, golden).
///
/// These are deliberately brittle. Every other test in the suite asserts a property ("the age is repeated early",
/// "no negation survives"), which is the right way to pin a rule — but it cannot tell anyone what the operator will
/// actually see, and it would not notice the prompt slowly turning into something nobody wants to look at. One
/// golden pair per family keeps the real output reviewable.
///
/// If you are here because this failed: read the new prompt before updating it. The failure is the question.
/// </summary>
public sealed class BodyReferenceGoldenPromptTests
{
    /// <summary>Becky: 50, curvy with a soft middle, a dress as her default outfit, clothed.</summary>
    private static BodyReferenceBrief Becky()
    {
        var brief = new BodyReferenceBrief
        {
        CharacterTemplateId = "8f2a1c34-0000-4000-8000-00000000be01",
        BodyCardVersion = 4,
        Gender = "Female",
        BodyState = SceneImageReferenceBodyState.Clothed,
        Age = "50",
        HairStyle = "Bun",
        HairColour = "Brown",
        EyeColour = "blue",
        SkinTone = "Fair",
        BodyShape = "average frame, a little soft, slight roundness, fuller rear with a soft belly, bust Full, hips Wide, rear full",
        Axes = new CharacterBodyAxes
        {
            BodyBuild = "average frame",
            Adiposity = "a little soft, slight roundness",
            FatDistribution = "fuller rear with a soft belly",
            BustSize = "Full",
            HipSize = "Wide",
            ButtSize = "full"
        },
        Stance = BodyReferenceStance.Standing,
        Clothing = "plain everyday clothing"
    };

        // The canonical face and body texts, composed the way the brief factory composes them. WITHOUT these the briefs
        // these goldens compile would not be shaped like a real one, and the goldens would be pinning an accident.
        brief.BodyText = BodyReferencePromptCompiler.ComposeBodyText(brief);
        brief.FaceText = BodyReferencePromptCompiler.ComposeFaceText(brief);
        return brief;
    }

    private static ResolvedImageModel Model(SceneImageModelFamily family) => new(
        "http://localhost",
        "/sdapi/v1/txt2img",
        300,
        null,
        family == SceneImageModelFamily.Pony ? "ponyDiffusionV6XL_v6StartWithThisOne" : "juggernautXL_v9",
        ImageContentPolicy.AdultAllowed,
        "Local",
        false,
        family,
        family == SceneImageModelFamily.Pony
            ? SceneImagePromptDialect.PonyV6Tags
            : SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol.ComfyUi);

    /// <summary>
    /// Pony: the full quality string, the rating, the count tag, the repeated key attributes, then the body tokens,
    /// then the view — and the inert picks ("no visible muscle") contribute nothing at all.
    /// </summary>
    [Fact]
    public void Pony_Golden()
    {
        var compiled = BodyReferencePromptCompiler.Compile(Becky(), Model(SceneImageModelFamily.Pony), ["Becky"]);

        Assert.Equal(
            "score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, "
            + "rating_safe, 1girl, mature female, brown hair, hair bun, blue eyes, pale skin, "
            + "chubby, thick thighs, large ass, belly, large breasts, wide hips, "
            + "plain everyday clothing, full body, standing, front view, eye level",
            compiled.Positive);
        Assert.Equal("lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry", compiled.Negative);
        Assert.True(compiled.IsValid);
    }

    /// <summary>
    /// SDXL: the CANONICAL BODY TEXT first (B-132), then this shot's stance and clothing, then the camera and texture
    /// cues — and an EMPTY negative, which is not a defect.
    ///
    /// The medium cue the prompt used to open with ("Full-body photograph of …") is gone: it was the sentence that
    /// could not host an authored text, and the tail below carries the framing cue ("Whole body in frame and
    /// unobstructed, head to feet") and the lens ("35mm photograph"). What the operator now reads first is the same
    /// sentence the LoRA cells and the angle renders paste.
    /// </summary>
    [Fact]
    public void Sdxl_Golden()
    {
        var compiled = BodyReferencePromptCompiler.Compile(Becky(), Model(SceneImageModelFamily.Sdxl), ["Becky"]);

        Assert.Equal(
            "A middle-aged woman, with brown hair, bun hairstyle, blue eyes, fair skin. "
            + "Body: an average frame, slightly soft with a little roundness, a fuller rear with a soft belly, "
            + "a full bust, wide hips, a full rear. "
            + "Standing upright and facing the camera. "
            + "Wearing plain everyday clothing. "
            + "Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp "
            + "focus, 35mm photograph.",
            compiled.Positive);
        Assert.Equal(string.Empty, compiled.Negative);
        Assert.True(compiled.IsValid);
    }
}
