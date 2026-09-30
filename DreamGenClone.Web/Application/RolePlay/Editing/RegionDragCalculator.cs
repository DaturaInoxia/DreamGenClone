namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Turns a pointer drag on the source image into the rectangle a region edit is confined to.
///
/// <para>
/// It works in PERCENT of the frame - the same units <see cref="MediaEditRegionOperation"/> stores - so a drawn
/// rectangle is stored as the numbers that were drawn, with no pixel conversion to get wrong. The page script only
/// reports pointer positions; every decision about the rectangle is made here, which is why this is a plain static
/// class with no dependencies: the arithmetic can be tested against the same cases the graph runs.
/// </para>
///
/// <para>
/// Nothing is ever INVERTED or CLAMPED into a different shape: a drag that would cross itself keeps a positive size,
/// a drag that would leave the frame stops at its edge, and a drag too small to be a region reports null rather than a
/// sliver that the pipeline would then refuse. A rectangle that silently became something else would edit an area the
/// operator never drew, and the result would look plausible.
/// </para>
/// </summary>
public static class RegionDragCalculator
{
    /// <summary>
    /// The smallest region a drag may produce, as a share of the frame. A rectangle smaller than this is a stray
    /// click or an aborted drag, not a selection: reporting nothing keeps the "draw a region" reason on screen
    /// instead of queueing an edit confined to a few pixels.
    /// </summary>
    public const double MinimumExtentPercent = 2;

    /// <summary>Handle names, matching the corner handles the crop stage already reports.</summary>
    public const string TopLeftHandle = "tl";
    public const string TopRightHandle = "tr";
    public const string BottomLeftHandle = "bl";
    public const string BottomRightHandle = "br";

    /// <summary>
    /// The rectangle a fresh drag describes, anchored where the drag started. Coordinates are percent of the frame
    /// and may be given in any order, so a drag up and to the left produces the same rectangle as one down and to
    /// the right. Returns null when the result is too small to be a region.
    /// </summary>
    public static MediaEditRegionOperation? Draw(
        double anchorXPercent,
        double anchorYPercent,
        double currentXPercent,
        double currentYPercent,
        int growMaskBy,
        int featherPixels)
    {
        var left = Clamp(Math.Min(anchorXPercent, currentXPercent), 0, 100);
        var top = Clamp(Math.Min(anchorYPercent, currentYPercent), 0, 100);
        var right = Clamp(Math.Max(anchorXPercent, currentXPercent), 0, 100);
        var bottom = Clamp(Math.Max(anchorYPercent, currentYPercent), 0, 100);
        return FromEdges(left, top, right, bottom, growMaskBy, featherPixels);
    }

    /// <summary>
    /// The rectangle moved by a drag. The movement stops at the frame edge rather than shrinking or leaving the frame:
    /// dragging a selection into the edge keeps its size, which is what the operator sees under their finger.
    /// </summary>
    public static MediaEditRegionOperation Move(
        MediaEditRegionOperation region, double deltaXPercent, double deltaYPercent)
    {
        ArgumentNullException.ThrowIfNull(region);

        var left = Clamp(region.LeftPercent + deltaXPercent, 0, 100 - region.WidthPercent);
        var top = Clamp(region.TopPercent + deltaYPercent, 0, 100 - region.HeightPercent);
        return region with
        {
            LeftPercent = Round(left),
            TopPercent = Round(top)
        };
    }

    /// <summary>
    /// The rectangle resized by grabbing one of its corners. The grabbed corner follows the pointer and the opposite
    /// corner stays put; an edge is never dragged past its opposite, because a selection that flipped would cover the
    /// area the operator was trying to exclude.
    /// </summary>
    public static MediaEditRegionOperation Resize(
        MediaEditRegionOperation region, string handle, double deltaXPercent, double deltaYPercent)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (string.IsNullOrWhiteSpace(handle))
            throw new InvalidOperationException("A region resize requires the corner handle that was grabbed.");

        var left = region.LeftPercent;
        var top = region.TopPercent;
        var right = region.LeftPercent + region.WidthPercent;
        var bottom = region.TopPercent + region.HeightPercent;

        switch (handle)
        {
            case TopLeftHandle:
                left = Clamp(left + deltaXPercent, 0, right - MinimumExtentPercent);
                top = Clamp(top + deltaYPercent, 0, bottom - MinimumExtentPercent);
                break;

            case TopRightHandle:
                right = Clamp(right + deltaXPercent, left + MinimumExtentPercent, 100);
                top = Clamp(top + deltaYPercent, 0, bottom - MinimumExtentPercent);
                break;

            case BottomLeftHandle:
                left = Clamp(left + deltaXPercent, 0, right - MinimumExtentPercent);
                bottom = Clamp(bottom + deltaYPercent, top + MinimumExtentPercent, 100);
                break;

            case BottomRightHandle:
                right = Clamp(right + deltaXPercent, left + MinimumExtentPercent, 100);
                bottom = Clamp(bottom + deltaYPercent, top + MinimumExtentPercent, 100);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported region resize handle '{handle}'. Expected one of {TopLeftHandle}, {TopRightHandle}, {BottomLeftHandle}, {BottomRightHandle}.");
        }

        return FromEdges(left, top, right, bottom, region.GrowMaskBy, region.FeatherPixels)
            ?? throw new InvalidOperationException(
                "A region resize must leave a region at least as large as the minimum extent.");
    }

    /// <summary>
    /// True when a pointer position lies inside the rectangle. The selector needs it because the frame itself passes
    /// the pointer through to the image underneath, so a press on the image has to be read two ways: inside the box it
    /// moves it, anywhere else it draws a new one.
    /// </summary>
    public static bool IsInside(MediaEditRegionOperation region, double xPercent, double yPercent)
    {
        ArgumentNullException.ThrowIfNull(region);
        return xPercent >= region.LeftPercent
            && xPercent <= region.LeftPercent + region.WidthPercent
            && yPercent >= region.TopPercent
            && yPercent <= region.TopPercent + region.HeightPercent;
    }

    /// <summary>True when the handle names a corner this calculator resizes.</summary>
    public static bool IsResizeHandle(string handle)
        => handle is TopLeftHandle or TopRightHandle or BottomLeftHandle or BottomRightHandle;

    /// <summary>
    /// Builds the operation from edges, refusing a rectangle too small to be a selection. Percent is rounded to three
    /// decimals: enough for a frame of any size this pipeline renders, and short enough that the stored provenance
    /// stays readable.
    /// </summary>
    private static MediaEditRegionOperation? FromEdges(
        double left, double top, double right, double bottom, int growMaskBy, int featherPixels)
    {
        var width = right - left;
        var height = bottom - top;
        if (width < MinimumExtentPercent || height < MinimumExtentPercent)
            return null;

        return new MediaEditRegionOperation(
            Round(left),
            Round(top),
            Round(width),
            Round(height),
            growMaskBy,
            featherPixels);
    }

    private static double Clamp(double value, double minimum, double maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
