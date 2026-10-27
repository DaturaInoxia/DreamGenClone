using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The trailing camera statement is worded per MODEL FAMILY. The operator chooses from the same controls either way;
/// what changes is whether the choice is stated as a demand (the families whose vendors document shot-size terms) or as
/// a description of the viewpoint (Krea 2, measured to render a framing demand badly).
/// </summary>
public sealed class SceneCameraStatementTests
{
    private static SceneBlocking Frame(string shotSize, string? angle, string? height) => new(
        "1",
        [],
        new SceneBlockingCamera(angle ?? string.Empty, shotSize, null, null, height),
        [],
        "rationale");

    /// <summary>
    /// The live case (SceneImage 57fcc461, 2026-10-25): Krea 2, "tightly framed shot", operator height "above".
    /// </summary>
    [Fact]
    public void For_Krea2_DescribesTheViewpointInsteadOfDemandingAShot()
    {
        var line = SceneCameraStatement.For(
            SceneImageModelFamily.Krea2,
            Frame("tightly framed shot", "high angle, viewed from above", CameraHeights.Above));

        Assert.Equal(
            "CAMERA — AUTHORITATIVE: the picture is taken from above the subject's eye line, looking down. "
            + FrameInclusion.Sentence + " The subject fills most of the frame.",
            line);
        // The two things Krea 2 is measured to punish: a shot-size term and a demand about how much of the subject to
        // include. INCLUSION is not a demand — it says what the picture contains, which is the fact that stops a caption
        // from inventing a crop.
        Assert.DoesNotContain("tightly framed", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" shot", line, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CameraHeights.Floor, "from floor level, looking up")]
    [InlineData(CameraHeights.Waist, "from waist height, level with the subject")]
    [InlineData(CameraHeights.Eye, "from eye level")]
    [InlineData(CameraHeights.Above, "from above the subject's eye line, looking down")]
    [InlineData(CameraHeights.Overhead, "from directly overhead, looking straight down")]
    public void For_Krea2_TranslatesEveryHeightTheDropdownOffers(string height, string expected)
    {
        // The operator still chooses from the SAME five positions; the selection is translated, never removed.
        var line = SceneCameraStatement.For(
            SceneImageModelFamily.Krea2,
            Frame("medium shot", null, height));

        Assert.Contains($"the picture is taken {expected}.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void For_Krea2_StatesTheScaleOfAWideFrameAsWhatThePictureShows()
    {
        var line = SceneCameraStatement.For(
            SceneImageModelFamily.Krea2,
            Frame("wide establishing shot", null, CameraHeights.Eye));

        Assert.Contains("The setting fills the frame", line, StringComparison.Ordinal);
        Assert.Contains("scaled by how far they are from the camera", line, StringComparison.Ordinal);
        Assert.DoesNotContain("wide establishing shot", line, StringComparison.Ordinal);
    }

    [Fact]
    public void For_Krea2_WithNoRecordedHeightKeepsTheCoverageFactOnly()
    {
        var line = SceneCameraStatement.For(SceneImageModelFamily.Krea2, Frame("tightly framed shot", null, null));

        Assert.Equal($"CAMERA — AUTHORITATIVE: {FrameInclusion.Sentence} The subject fills most of the frame.", line);
    }

    [Fact]
    public void For_Krea2_ClaimsNothingWhenNeitherHeightNorShotSizeIsRecorded()
    {
        Assert.Empty(SceneCameraStatement.For(SceneImageModelFamily.Krea2, Frame("", null, null)));
        Assert.Empty(SceneCameraStatement.For(SceneImageModelFamily.Krea2, null));
    }

    /// <summary>
    /// The documented-terms families keep the demand form they were measured against — a fix for Krea must not silently
    /// change what SDXL/Flux/Qwen are told.
    /// </summary>
    [Fact]
    public void For_TheDocumentedTermsFamilies_KeepsTheDemandForm()
    {
        var frame = Frame("medium shot", "eye level", CameraHeights.Eye);

        foreach (var family in new[]
        {
            SceneImageModelFamily.Sdxl,
            SceneImageModelFamily.Flux,
            SceneImageModelFamily.QwenImage21,
            SceneImageModelFamily.Pony,
            SceneImageModelFamily.Api
        })
        {
            var line = SceneCameraStatement.For(family, frame);
            Assert.Equal(
                $"CAMERA — AUTHORITATIVE: this frame is a medium shot, eye level perspective. {FrameInclusion.Sentence} The subject fills the frame.",
                line);
        }
    }

    /// <summary>
    /// The angle and the height come from the SAME pitch when the operator picks a height and no angle, so stating both
    /// printed "high angle" twice: "at high angle, viewed from above, high angle, camera just above eye level, looking
    /// down" (reported live 2026-10-25).
    /// </summary>
    [Theory]
    [InlineData(CameraHeights.Above, "high angle, viewed from above")]
    [InlineData(CameraHeights.Floor, "low angle, viewed from below")]
    [InlineData(CameraHeights.Eye, "eye level")]
    public void TheDemandForm_SaysTheAnglePitchOnce(string height, string derivedAngle)
    {
        var line = SceneFramingClause.LineFor(Frame("medium shot", derivedAngle, height));

        var pitchWord = height == CameraHeights.Above
            ? "high angle"
            : height == CameraHeights.Floor ? "low angle" : "eye level";
        Assert.Equal(1, CountOccurrences(line, pitchWord));
    }

    [Fact]
    public void TheDemandForm_KeepsAnAngleTheHeightWordingDoesNotAlreadyState()
    {
        var line = SceneFramingClause.LineFor(Frame("medium shot", "three-quarter angle", CameraHeights.Eye));

        Assert.Contains("three-quarter angle", line, StringComparison.Ordinal);
        Assert.Contains("eye level perspective", line, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        for (var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = text.IndexOf(term, index + term.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }
}
