namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The parameters of one crop, as chosen in the edit form. Every value is explicit: nothing here
/// invents a default, because a silent default would move the frame without anyone asking for it.
/// </summary>
/// <param name="TargetAspect">Width ÷ height of the crop window. Must be positive.</param>
/// <param name="HeadroomPercent">
/// Vertical placement of the crop window inside the available slack, measured from the top:
/// 0 anchors the window to the top of the image, 50 centres it, 100 anchors it to the bottom. For a
/// portrait this is the knob that keeps or trims space above the head.
/// </param>
/// <param name="HorizontalOffsetPercent">Horizontal placement from the left; 50 centres the window.</param>
public sealed record ImageCropSettings(
    double TargetAspect,
    double HeadroomPercent,
    double HorizontalOffsetPercent)
{
    /// <summary>Rejects anything the calculator would otherwise have to guess at.</summary>
    public void Validate()
    {
        if (TargetAspect <= 0)
            throw new InvalidOperationException($"Crop target aspect must be positive, but was {TargetAspect}.");
        if (HeadroomPercent is < 0 or > 100)
            throw new InvalidOperationException($"Crop headroom percent must be between 0 and 100, but was {HeadroomPercent}.");
        if (HorizontalOffsetPercent is < 0 or > 100)
            throw new InvalidOperationException($"Crop horizontal offset percent must be between 0 and 100, but was {HorizontalOffsetPercent}.");
    }
}

/// <summary>The pixel window a crop keeps. Origin is the top-left of the source image.</summary>
public sealed record ImageCropRect(int X, int Y, int Width, int Height);

/// <summary>
/// How the crop window is chosen. Each mode is an explicit alternative — nothing switches between them on
/// its own. Framing distributes slack by percentage; HeadAware places a framing-sized window by measured
/// head height; HeadFramed wraps the measured head itself; Manual is the window the user dragged.
/// </summary>
public enum ImageCropMode
{
    /// <summary>Percentage of the slack between the window and the image edge.</summary>
    Framing,

    /// <summary>Percentage of the measured head height above the hairline.</summary>
    HeadAware,

    /// <summary>Wraps the measured head (face box + hairline/chin) with a margin, fitted to the aspect.</summary>
    HeadFramed,

    /// <summary>The exact window the user dragged. Requires <c>Rect</c>; nothing is derived.</summary>
    Manual
}

/// <summary>
/// The pixel box of the detected face, from the approved tool's <c>face_box</c> (min/max over all face
/// landmarks). It does not include hair, which is why head framing adds a margin of its own.
/// </summary>
public sealed record ImageCropFaceBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>
/// A measured face in the source image, in pixels, from the approved MediaPipe tool
/// (<c>tools/eye-validation/measure_iris.py</c>), which reports the hairline (landmark 10), the chin (152)
/// and the face box.
/// </summary>
public sealed record ImageCropHeadMeasurement(int ForeheadTopY, int ChinY, ImageCropFaceBox? Face = null)
{
    /// <summary>
    /// Hairline to chin. Slightly under a true skull height: FaceMesh stops at the hairline, so hair
    /// above it is not measured.
    /// </summary>
    public int HeadHeightPx => ChinY - ForeheadTopY;
}

/// <summary>
/// Works out which pixels a crop keeps. Deliberately pure and free of image libraries so the framing
/// rules are testable as arithmetic rather than by eyeballing output.
///
/// It fits the largest window of the requested aspect inside the source, then places that window using
/// the placement percentages. It is a framing crop, NOT a subject-aware one: there is no face detection
/// here, so "headroom" means space above the window's top edge, not space measured above a detected head.
/// </summary>
public static class ImageCropCalculator
{
    public static ImageCropRect Compute(int sourceWidth, int sourceHeight, ImageCropSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException($"A crop needs a positive source size, but got {sourceWidth}x{sourceHeight}.");
        settings.Validate();

        var sourceAspect = (double)sourceWidth / sourceHeight;
        double windowWidth;
        double windowHeight;
        if (sourceAspect > settings.TargetAspect)
        {
            // Source is wider than the target: keep the full height and trim the sides.
            windowHeight = sourceHeight;
            windowWidth = sourceHeight * settings.TargetAspect;
        }
        else
        {
            // Source is taller (or equal): keep the full width and trim top/bottom.
            windowWidth = sourceWidth;
            windowHeight = sourceWidth / settings.TargetAspect;
        }

        var width = Math.Clamp((int)Math.Round(windowWidth), 1, sourceWidth);
        var height = Math.Clamp((int)Math.Round(windowHeight), 1, sourceHeight);

        // Whatever the window does not cover is the slack the placement percentages distribute.
        var slackX = sourceWidth - width;
        var slackY = sourceHeight - height;
        var x = Math.Clamp((int)Math.Round(slackX * (settings.HorizontalOffsetPercent / 100.0)), 0, slackX);
        var y = Math.Clamp((int)Math.Round(slackY * (settings.HeadroomPercent / 100.0)), 0, slackY);

        return new ImageCropRect(x, y, width, height);
    }

    /// <summary>
    /// Head-aware placement. The window keeps exactly the size <see cref="Compute"/> would choose, so
    /// switching mode only moves the frame — it never resizes it. The top edge is placed
    /// <see cref="ImageCropSettings.HeadroomPercent"/> of the measured head height above the hairline and
    /// then clamped to the image bounds.
    ///
    /// There is no fallback: a missing or unusable measurement throws instead of quietly reverting to
    /// framing, because a silent mode change would move the frame without anyone asking for it.
    /// </summary>
    public static ImageCropRect ComputeHeadAware(
        int sourceWidth,
        int sourceHeight,
        ImageCropSettings settings,
        ImageCropHeadMeasurement? measurement)
    {
        var framing = Compute(sourceWidth, sourceHeight, settings);
        if (measurement is null)
        {
            throw new InvalidOperationException(
                "A head-aware crop needs a measured face, but none was supplied. Measure with " +
                "tools/eye-validation/measure_iris.py, or select the framing mode explicitly.");
        }

        if (measurement.HeadHeightPx <= 0)
        {
            throw new InvalidOperationException(
                "A head-aware crop needs a positive head height, but the measurement gave " +
                $"{measurement.HeadHeightPx}px (forehead {measurement.ForeheadTopY}, chin {measurement.ChinY}).");
        }

        var headroomPx = measurement.HeadHeightPx * (settings.HeadroomPercent / 100.0);
        var desiredTop = measurement.ForeheadTopY - headroomPx;
        var y = Math.Clamp((int)Math.Round(desiredTop), 0, sourceHeight - framing.Height);

        return framing with { Y = y };
    }

    /// <summary>
    /// Wraps the MEASURED HEAD rather than placing a framing-sized window. The head is the face box plus
    /// the hairline/chin extent (FaceMesh stops at the hairline and the box covers no hair), grown by
    /// <see cref="ImageCropSettings.HeadroomPercent"/> of the head height on every side, then expanded
    /// about its own centre until it matches the target aspect, then shifted inside the source.
    ///
    /// Expansion never shrinks a side: a crop that cut into the face would defeat the purpose. There is no
    /// fallback — a measurement without a face box throws, because the head cannot be located without it.
    /// </summary>
    public static ImageCropRect ComputeHeadFramed(
        int sourceWidth,
        int sourceHeight,
        ImageCropSettings settings,
        ImageCropHeadMeasurement? measurement)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException($"A crop needs a positive source size, but got {sourceWidth}x{sourceHeight}.");
        settings.Validate();

        if (measurement is null)
        {
            throw new InvalidOperationException(
                "A head-framed crop needs a measured face, but none was supplied. Measure the image first, " +
                "or select the framing mode explicitly.");
        }

        if (measurement.Face is not { } face)
        {
            throw new InvalidOperationException(
                "A head-framed crop needs the measured face box, but the measurement carried none. Re-measure " +
                "the image with tools/eye-validation/measure_iris.py.");
        }

        if (measurement.HeadHeightPx <= 0)
        {
            throw new InvalidOperationException(
                "A head-framed crop needs a positive head height, but the measurement gave " +
                $"{measurement.HeadHeightPx}px (forehead {measurement.ForeheadTopY}, chin {measurement.ChinY}).");
        }

        // The head, not just the face: the hairline sits above the face box top and the chin below it.
        var top = Math.Min(face.Y, measurement.ForeheadTopY);
        var bottom = Math.Max(face.Bottom, measurement.ChinY);
        var left = face.X;
        var right = face.Right;

        var margin = measurement.HeadHeightPx * (settings.HeadroomPercent / 100.0);
        double windowLeft = left - margin;
        double windowRight = right + margin;
        double windowTop = top - margin;
        double windowBottom = bottom + margin;

        // Fit the target aspect by growing the short side about the centre — never by trimming a side.
        var width = windowRight - windowLeft;
        var height = windowBottom - windowTop;
        var centreX = (windowLeft + windowRight) / 2.0;
        var centreY = (windowTop + windowBottom) / 2.0;
        if (width / height < settings.TargetAspect)
        {
            width = height * settings.TargetAspect;
        }
        else
        {
            height = width / settings.TargetAspect;
        }

        var windowWidth = Math.Clamp((int)Math.Round(width), 1, sourceWidth);
        var windowHeight = Math.Clamp((int)Math.Round(height), 1, sourceHeight);

        // Centre on the head, then move the window inside the source if the head sits near an edge.
        var x = Math.Clamp((int)Math.Round(centreX - windowWidth / 2.0), 0, sourceWidth - windowWidth);
        var y = Math.Clamp((int)Math.Round(centreY - windowHeight / 2.0), 0, sourceHeight - windowHeight);

        return new ImageCropRect(x, y, windowWidth, windowHeight);
    }

    /// <summary>
    /// The exact window the user dragged. It is used as-is, but must sit inside the source: a dragged
    /// window that runs off the image is refused rather than quietly clipped, because clipping would crop
    /// something other than what the box showed.
    /// </summary>
    public static ImageCropRect ComputeManual(int sourceWidth, int sourceHeight, ImageCropRect? rect)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException($"A crop needs a positive source size, but got {sourceWidth}x{sourceHeight}.");

        if (rect is not { } window)
        {
            throw new InvalidOperationException(
                "A manual crop requires the window that was dragged, but none was supplied.");
        }

        if (window.Width <= 0 || window.Height <= 0)
        {
            throw new InvalidOperationException(
                $"A manual crop needs a positive window size, but got {window.Width}x{window.Height}.");
        }

        if (window.X < 0 || window.Y < 0 || window.X + window.Width > sourceWidth || window.Y + window.Height > sourceHeight)
        {
            throw new InvalidOperationException(
                $"The dragged crop window ({window.X}, {window.Y}, {window.Width}x{window.Height}) must stay inside " +
                $"the {sourceWidth}x{sourceHeight} source image.");
        }

        return window;
    }

    /// <summary>
    /// Moves a window by a pixel delta, keeping its size and keeping it inside the source: a dragged box
    /// slides along the edge instead of leaving the image.
    /// </summary>
    public static ImageCropRect Translate(
        ImageCropRect rect, int deltaX, int deltaY, int sourceWidth, int sourceHeight)
    {
        RequireSourceAndRect(rect, sourceWidth, sourceHeight);

        return new ImageCropRect(
            Math.Clamp(rect.X + deltaX, 0, sourceWidth - rect.Width),
            Math.Clamp(rect.Y + deltaY, 0, sourceHeight - rect.Height),
            rect.Width,
            rect.Height);
    }

    /// <summary>
    /// Resizes a window by dragging one corner handle, keeping the opposite corner anchored. The window can
    /// never go below <paramref name="minimumSize"/> on a side, and never leaves the source.
    /// </summary>
    public static ImageCropRect Resize(
        ImageCropRect rect,
        string handle,
        int deltaX,
        int deltaY,
        int sourceWidth,
        int sourceHeight,
        int minimumSize)
    {
        RequireSourceAndRect(rect, sourceWidth, sourceHeight);
        if (minimumSize < 1)
            throw new InvalidOperationException($"A crop window needs a positive minimum size, but got {minimumSize}.");
        if (handle is not ("tl" or "tr" or "bl" or "br"))
            throw new InvalidOperationException($"Unsupported crop handle '{handle}'.");

        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;

        if (handle is "tl" or "bl")
            left = Math.Clamp(left + deltaX, 0, right - minimumSize);
        else
            right = Math.Clamp(right + deltaX, left + minimumSize, sourceWidth);

        if (handle is "tl" or "tr")
            top = Math.Clamp(top + deltaY, 0, bottom - minimumSize);
        else
            bottom = Math.Clamp(bottom + deltaY, top + minimumSize, sourceHeight);

        return new ImageCropRect(left, top, right - left, bottom - top);
    }

    private static void RequireSourceAndRect(ImageCropRect rect, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException($"A crop needs a positive source size, but got {sourceWidth}x{sourceHeight}.");
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException($"A crop window needs a positive size, but got {rect.Width}x{rect.Height}.");
        if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > sourceWidth || rect.Y + rect.Height > sourceHeight)
            throw new InvalidOperationException("The crop window must stay inside the source image.");
    }
}
