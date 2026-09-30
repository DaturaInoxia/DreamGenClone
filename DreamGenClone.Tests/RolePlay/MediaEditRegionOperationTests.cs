using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The region an operator draws is REQUEST DATA (CASE-21 / CASE-23). These tests are about refusing geometry the graph
/// cannot honour, loudly: a rectangle that runs off the frame or a grow value the host node rejects must be reported,
/// because a quietly clamped region edits a different area than the one that was drawn - and the render still looks
/// like a success.
/// </summary>
public sealed class MediaEditRegionOperationTests
{
    [Fact]
    public void ARegionInsideTheFrameWithGrowAndFeatherIsAccepted()
    {
        var region = new MediaEditRegionOperation(32, 55, 35, 33, GrowMaskBy: 12, FeatherPixels: 24);

        region.Validate();

        Assert.Equal("region rect=32,55 35x33% grow=12 feather=24", region.Describe());
    }

    /// <summary>A region flush to the frame edge is legal - that is exactly the panorama/outpaint shape.</summary>
    [Fact]
    public void ARegionFlushWithTwoEdgesIsAccepted()
    {
        var region = new MediaEditRegionOperation(0, 0, 100, 40, GrowMaskBy: 0, FeatherPixels: 0);

        region.Validate();
    }

    [Theory]
    [InlineData(-1, 0, 10, 10, "non-negative edges")]
    [InlineData(0, -5, 10, 10, "non-negative edges")]
    [InlineData(10, 10, 0, 10, "positive size")]
    [InlineData(10, 10, 10, 0, "positive size")]
    [InlineData(80, 10, 30, 10, "inside the frame")]
    [InlineData(10, 80, 10, 30, "inside the frame")]
    public void GeometryTheGraphCannotHonourIsRefused(double left, double top, double width, double height, string expected)
    {
        var region = new MediaEditRegionOperation(left, top, width, height, GrowMaskBy: 0, FeatherPixels: 0);

        var exception = Assert.Throws<InvalidOperationException>(region.Validate);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 64 is the host node's own maximum and 0 its minimum; one past either end is refused rather than clamped, because
    /// the clamp would happen on the far side of the wire where nobody would see it.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void AGrowValueOutsideTheHostsOwnBoundIsRefused(int growMaskBy)
    {
        var region = new MediaEditRegionOperation(10, 10, 10, 10, growMaskBy, FeatherPixels: 0);

        var exception = Assert.Throws<InvalidOperationException>(region.Validate);

        Assert.Contains("between 0 and 64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeFeatherIsRefused()
    {
        var region = new MediaEditRegionOperation(10, 10, 10, 10, GrowMaskBy: 0, FeatherPixels: -1);

        var exception = Assert.Throws<InvalidOperationException>(region.Validate);

        Assert.Contains("feather", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
