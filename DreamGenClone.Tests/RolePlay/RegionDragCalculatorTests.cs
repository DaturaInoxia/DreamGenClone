using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The drag arithmetic behind the region selector. It is tested here rather than through the component because these
/// are the numbers that end up on the run: a rectangle that is moved, clamped or inverted wrongly still renders, and
/// the picture that comes back looks entirely plausible with the wrong area confined.
/// </summary>
public sealed class RegionDragCalculatorTests
{
    private static readonly MediaEditRegionOperation Region = new(20, 30, 25, 40, GrowMaskBy: 6, FeatherPixels: 3);

    [Fact]
    public void Draw_AnchorsTheRectangleWhereTheDragStarted()
    {
        var region = RegionDragCalculator.Draw(10, 20, 40, 60, growMaskBy: 8, featherPixels: 4);

        Assert.NotNull(region);
        Assert.Equal(10d, region!.LeftPercent);
        Assert.Equal(20d, region.TopPercent);
        Assert.Equal(30d, region.WidthPercent);
        Assert.Equal(40d, region.HeightPercent);
        Assert.Equal(8, region.GrowMaskBy);
        Assert.Equal(4, region.FeatherPixels);
    }

    [Theory]
    [InlineData(10, 20, 40, 60)]
    [InlineData(40, 60, 10, 20)]
    [InlineData(40, 20, 10, 60)]
    [InlineData(10, 60, 40, 20)]
    public void Draw_IgnoresWhichCornerTheDragStartedFrom(double startX, double startY, double endX, double endY)
    {
        // A drag up and to the left is the same selection as one down and to the right: the operator drew a box, and
        // which corner their finger began in says nothing about which corners they meant.
        var region = RegionDragCalculator.Draw(startX, startY, endX, endY, growMaskBy: 0, featherPixels: 0);

        Assert.NotNull(region);
        Assert.Equal(10d, region!.LeftPercent);
        Assert.Equal(20d, region.TopPercent);
        Assert.Equal(30d, region.WidthPercent);
        Assert.Equal(40d, region.HeightPercent);
    }

    [Fact]
    public void Draw_StopsAtTheFrameEdge()
    {
        // A drag that leaves the stage at the top left is a selection that begins at the frame's own edge, not one
        // with a negative origin.
        var region = RegionDragCalculator.Draw(-15, -25, 30, 35, growMaskBy: 0, featherPixels: 0);

        Assert.NotNull(region);
        Assert.Equal(0d, region!.LeftPercent);
        Assert.Equal(0d, region.TopPercent);
        Assert.Equal(30d, region.WidthPercent);
        Assert.Equal(35d, region.HeightPercent);
    }

    [Theory]
    [InlineData(10, 20, 11, 21)]
    [InlineData(10, 20, 10, 20)]
    [InlineData(90, 90, 91, 95)]
    public void Draw_ReportsNothingWhenTheDragIsTooSmallToBeARegion(double startX, double startY, double endX, double endY)
    {
        // A stray click is not a selection. It must leave the "draw a region" reason on screen instead of queueing an
        // edit confined to a few pixels that the pipeline would then refuse.
        Assert.Null(RegionDragCalculator.Draw(startX, startY, endX, endY, growMaskBy: 0, featherPixels: 0));
    }

    [Fact]
    public void Move_KeepsTheSizeWhenItReachesTheEdge()
    {
        // Pushing a selection into the edge keeps its size: it stops, rather than shrinking under the pointer.
        var moved = RegionDragCalculator.Move(new MediaEditRegionOperation(70, 60, 25, 30, 0, 0), 40, 40);

        Assert.Equal(75d, moved.LeftPercent);
        Assert.Equal(70d, moved.TopPercent);
        Assert.Equal(25d, moved.WidthPercent);
        Assert.Equal(30d, moved.HeightPercent);
    }

    [Fact]
    public void Move_PreservesGrowAndFeather()
    {
        var moved = RegionDragCalculator.Move(Region, 5, -5);

        Assert.Equal(25d, moved.LeftPercent);
        Assert.Equal(25d, moved.TopPercent);
        Assert.Equal(Region.WidthPercent, moved.WidthPercent);
        Assert.Equal(Region.HeightPercent, moved.HeightPercent);
        Assert.Equal(Region.GrowMaskBy, moved.GrowMaskBy);
        Assert.Equal(Region.FeatherPixels, moved.FeatherPixels);
    }

    [Fact]
    public void Resize_MovesOnlyTheGrabbedCorner()
    {
        var resized = RegionDragCalculator.Resize(Region, RegionDragCalculator.BottomRightHandle, 10, 5);

        Assert.Equal(20d, resized.LeftPercent);
        Assert.Equal(30d, resized.TopPercent);
        Assert.Equal(35d, resized.WidthPercent);
        Assert.Equal(45d, resized.HeightPercent);
        Assert.Equal(Region.GrowMaskBy, resized.GrowMaskBy);
        Assert.Equal(Region.FeatherPixels, resized.FeatherPixels);
    }

    [Fact]
    public void Resize_RefusesToTurnTheSelectionInsideOut()
    {
        // Dragged far past its opposite corner, a selection keeps a usable size instead of becoming a negative-width
        // rectangle that covers the area the operator was trying to exclude.
        var resized = RegionDragCalculator.Resize(Region, RegionDragCalculator.TopLeftHandle, 100, 100);

        Assert.Equal(43d, resized.LeftPercent);
        Assert.Equal(68d, resized.TopPercent);
        Assert.Equal(RegionDragCalculator.MinimumExtentPercent, resized.WidthPercent);
        Assert.Equal(RegionDragCalculator.MinimumExtentPercent, resized.HeightPercent);
    }

    [Theory]
    [InlineData("tl", true)]
    [InlineData("tr", true)]
    [InlineData("bl", true)]
    [InlineData("br", true)]
    [InlineData("", false)]
    [InlineData("move", false)]
    public void IsResizeHandle_NamesOnlyTheCorners(string handle, bool expected)
    {
        // "move" must NOT be a resize handle: the window body carries it, and a body drag moves the selection.
        Assert.Equal(expected, RegionDragCalculator.IsResizeHandle(handle));
    }

    [Theory]
    [InlineData(30, 40)]
    [InlineData(20, 30)]
    [InlineData(45, 70)]
    [InlineData(32.5, 50)]
    public void IsInside_AcceptsTheBoxAndItsEdges(double x, double y)
    {
        // The frame's CSS passes the pointer through to the image, so this is the ONLY thing that tells a
        // move-the-box press apart from a draw-a-new-box one. A press on the edge belongs to the box.
        Assert.True(RegionDragCalculator.IsInside(Region, x, y));
    }

    [Theory]
    [InlineData(19.9, 40)]
    [InlineData(45.1, 40)]
    [InlineData(30, 29.9)]
    [InlineData(30, 70.1)]
    [InlineData(0, 0)]
    [InlineData(99, 99)]
    public void IsInside_RejectsEverythingOutsideIt(double x, double y)
    {
        // Everything outside draws a new box instead, which is how a region gets drawn at all: if a press outside
        // were read as a move, the only way to make a box would be to grab a corner handle that is not there yet.
        Assert.False(RegionDragCalculator.IsInside(Region, x, y));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Resize_RequiresTheCornerThatWasGrabbed(string handle)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => RegionDragCalculator.Resize(Region, handle, 1, 1));

        Assert.Contains("corner handle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resize_RefusesAHandleItCannotHonour()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => RegionDragCalculator.Resize(Region, "middle", 1, 1));

        Assert.Contains("Unsupported region resize handle", error.Message, StringComparison.Ordinal);
    }
}
