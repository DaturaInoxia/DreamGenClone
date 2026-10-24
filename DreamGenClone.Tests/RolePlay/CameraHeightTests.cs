using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The camera-HEIGHT axis: five recorded positions, one prompt wording each, and one place that decides which wins.
///
/// <para>
/// Research basis (2026-10-22): no official Qwen-Image document defines camera height. The only official perspective
/// keywords are <c>eye level perspective</c>, <c>bird's eye perspective</c>, <c>low angle</c> and
/// <c>aerial perspective</c> (Alibaba Model Studio text-to-image prompt guide, linked from the Qwen-Image API page).
/// Bare <c>worm's eye</c> is documented to under-deliver, and degrees are the weak input — which is why each position
/// pairs a term with an explicit height relation in ONE clause rather than quoting an angle.
/// </para>
/// </summary>
public sealed class CameraHeightTests
{
    [Fact]
    public void EveryRecordedPositionStatesATermAndAHeightInOneClause()
    {
        Assert.Equal(
            ["floor", "waist", "eye", "above", "overhead"],
            CameraHeights.All);

        // A term alone under-delivers at the extremes; the wording always carries the height relation too.
        Assert.Equal("low angle from floor level, looking up", CameraHeights.Wording(CameraHeights.Floor));
        Assert.Equal("waist height, level with the subject", CameraHeights.Wording(CameraHeights.Waist));
        Assert.Equal("eye level perspective", CameraHeights.Wording(CameraHeights.Eye));
        Assert.Equal("high angle, camera just above eye level, looking down", CameraHeights.Wording(CameraHeights.Above));
        Assert.Equal("bird's eye perspective, looking straight down", CameraHeights.Wording(CameraHeights.Overhead));

        // 'aerial' is deliberately absent: its official gloss is a landscape overview, which reframes a photograph of
        // people as scenery.
        Assert.DoesNotContain(CameraHeights.All, height => height.Contains("aerial", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(CameraHeights.Floor, "up")]
    [InlineData(CameraHeights.Waist, "level")]
    [InlineData(CameraHeights.Eye, "level")]
    [InlineData(CameraHeights.Above, "down")]
    [InlineData(CameraHeights.Overhead, "down")]
    public void EachPositionImpliesTheAimThatMatchesIt(string height, string expectedPitch)
        => Assert.Equal(expectedPitch, CameraHeights.PitchOf(height));

    [Fact]
    public void AnUnknownPositionFailsFastInsteadOfGuessing()
    {
        var wording = Assert.Throws<InvalidOperationException>(() => CameraHeights.Wording("ceiling"));
        Assert.Contains("ceiling", wording.Message, StringComparison.Ordinal);

        var required = Assert.Throws<InvalidOperationException>(() => CameraHeights.Require(null, "The framing override"));
        Assert.Contains("The framing override", required.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireNormalisesACaseInsensitiveValue()
    {
        Assert.Equal(CameraHeights.Floor, CameraHeights.Require("  FLOOR ", "source"));
        Assert.False(CameraHeights.IsDefined("   "));
    }

    /// <summary>
    /// The compiled framing line is the ONE camera statement, so the height has to be inside it — a second sentence
    /// would read as a second subject.
    /// </summary>
    [Fact]
    public void TheFramingLineStatesTheHeightInTheSameSentence()
    {
        var blocking = new SceneBlocking(
            "1",
            [],
            new SceneBlockingCamera("eye level", "medium shot", null, null, CameraHeights.Floor),
            [],
            "rationale");

        var line = SceneFramingClause.LineFor(blocking);

        Assert.StartsWith("CAMERA — AUTHORITATIVE: this frame is a medium shot at eye level, low angle from floor level, looking up.", line, StringComparison.Ordinal);
        // One camera statement, not two.
        Assert.Single(line.Split("CAMERA", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void AFramingLineWithNoRecordedHeightClaimsNone()
    {
        var blocking = new SceneBlocking(
            "1", [], new SceneBlockingCamera("eye level", "medium shot", null, null, null), [], "rationale");

        Assert.DoesNotContain("floor level", SceneFramingClause.LineFor(blocking), StringComparison.Ordinal);
        Assert.DoesNotContain("eye level perspective", SceneFramingClause.LineFor(blocking), StringComparison.Ordinal);
    }
}
