using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 N6 (CASE-24): the outpaint operation contract. Direction + percent extend the frame one way, and grow/feather
/// soften the strip's inner seam exactly like a region's.
/// </summary>
public sealed class MediaEditOutpaintOperationTests
{
    [Fact]
    public void AValidOutpaintValidatesAndDescribes()
    {
        var operation = new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 50, 8, 32);

        operation.Validate();

        Assert.Contains("Right", operation.Describe(), StringComparison.Ordinal);
        Assert.Contains("50", operation.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void ForOutpaintCarriesTheOutpaintKind()
    {
        var operation = MediaEditOperation.ForOutpaint(
            new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Top, 30, 8, 32));

        operation.Validate();

        Assert.Equal(MediaEditOperationKind.Outpaint, operation.Kind);
        Assert.Equal(MediaEditOutpaintDirection.Top, operation.Outpaint!.Direction);
        Assert.Equal(30, operation.Outpaint.Percent);
    }

    [Fact]
    public void AnOutpaintWithoutParametersIsRefused()
    {
        var operation = new MediaEditOperation(MediaEditOperationKind.Outpaint, null);

        var exception = Assert.Throws<InvalidOperationException>(() => operation.Validate());

        Assert.Contains("outpaint parameters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APercentOutOfRangeIsRefused()
    {
        var operation = new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 250, 8, 32);

        var exception = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("between 0 and 200", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeFeatherIsRefused()
    {
        var operation = new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 50, 0, -1);

        var exception = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("feather", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An outpaint needs a positive feather for the same reason a region does (CASE-25): the host rounds the mask it
    /// confines with to 0/1, so a zero feather leaves a hard line where the source ends. The refusal names the
    /// persisted setting, not a value invented here.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AConfinedOutpaintWithoutAPositiveFeatherIsRefusedByName(int featherPixels)
    {
        var operation = new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 50, 8, featherPixels);

        var exception = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("RegionFeatherPixels", exception.Message, StringComparison.Ordinal);
    }
}
