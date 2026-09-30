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

    /// <summary>
    /// A region edit is an EDIT that also carries a region, so its kind records WHICH pixels were allowed to change -
    /// the provenance a later reader needs to tell "the whole frame was edited" from "this rectangle was".
    /// </summary>
    [Fact]
    public void ARegionEditCarriesItsRegionAndDescribesIt()
    {
        var operation = MediaEditOperation.ForMaskedRegion(new MediaEditRegionOperation(10, 20, 30, 40, GrowMaskBy: 2, FeatherPixels: 3));

        operation.Validate();

        Assert.Equal(MediaEditOperationKind.MaskedRegion, operation.Kind);
        Assert.Equal("region rect=10,20 30x40% grow=2 feather=3", operation.Describe());
    }

    /// <summary>A region kind with no region is a wiring mistake, not a whole-frame edit.</summary>
    [Fact]
    public void ARegionEditWithoutItsRegionIsRefused()
    {
        var operation = new MediaEditOperation(MediaEditOperationKind.MaskedRegion, null);

        var exception = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("requires its region parameters", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Region parameters on any other kind are refused, the same way crop and enhance parameters are.</summary>
    [Fact]
    public void RegionParametersOnAnotherKindAreRefused()
    {
        var operation = new MediaEditOperation(
            MediaEditOperationKind.Mirror, null, null, new MediaEditRegionOperation(10, 10, 10, 10, GrowMaskBy: 0, FeatherPixels: 0));

        var exception = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("must not carry region parameters", exception.Message, StringComparison.Ordinal);
    }
}
