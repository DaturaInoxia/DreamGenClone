using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Renders an OpenPose skeleton PNG from keypoints — the C# equivalent of the hand-run
/// <c>helpers/runpod/render-single-pose.py</c> that produced the app's committed skeleton files, so the
/// library can generate its own conditioning images instead of depending on a Python step.
///
/// The conventions are deliberate and inherited: a black canvas, one colour per limb group, and a line drawn
/// only when <b>both</b> endpoints clear the visibility floor. The pair predicate is the audited one — a
/// lenient "at least one endpoint" test renders half-limbs and once produced a false all-clear on the pack.
/// No head silhouette is drawn, matching the OpenPose/DWPose output the ControlNet was trained on.
/// </summary>
public static class PoseSkeletonRenderer
{
    /// <summary>Canvas edge in pixels, matching the committed pose-library skeletons.</summary>
    public const int DefaultCanvas = 1024;

    /// <summary>
    /// Gap kept between the fitted figure and the canvas edge.
    ///
    /// Generous on purpose, and not a cosmetic choice. The skeleton's topmost joint is a nose or an eye — the rig
    /// has no skull — so a figure fitted to the canvas edge leaves a renderer no room to draw the crown and hair
    /// that belong above them, and it draws them anyway, straight off the top of the frame. Measured 2026-09-24:
    /// with a 48 px margin both the SDXL/ControlNet route and Qwen-2.1 cropped the head, which cost three of the
    /// eighteen joints when the result was read back with DWPose. 128 px on a 1024 canvas leaves ~12% headroom and
    /// is pinned by a test that the fitted figure never touches the edge.
    /// </summary>
    public const int Margin = 128;

    private const double BodyLineRadius = 2.0;
    private const double BodyJointRadius = 5.0;
    private const double HandLineRadius = 1.0;
    private const double HandJointRadius = 2.0;

    private static readonly Rgb24 Background = new(0, 0, 0);
    private static readonly Rgb24 JointColor = new(255, 255, 255);
    private static readonly Rgb24 HandLineColor = new(120, 120, 120);
    private static readonly Rgb24 HandJointColor = new(200, 200, 200);

    /// <summary>Body limbs with their per-group colours (face, arms and legs read differently at a glance).</summary>
    /// <remarks>
    /// Public so an on-screen editor can draw the same figure the render will produce, rather than keeping a second
    /// copy of the limb table that can drift out of step with this one.
    /// </remarks>
    public static readonly (int A, int B, Rgb24 Color)[] BodyLinks =
    [
        (1, 0, new Rgb24(0, 0, 255)), (0, 14, new Rgb24(0, 0, 255)), (14, 16, new Rgb24(0, 0, 255)),
        (0, 15, new Rgb24(0, 0, 255)), (15, 17, new Rgb24(0, 0, 255)),
        (1, 2, new Rgb24(0, 255, 255)), (2, 3, new Rgb24(0, 255, 255)), (3, 4, new Rgb24(0, 255, 255)),
        (1, 5, new Rgb24(255, 255, 0)), (5, 6, new Rgb24(255, 255, 0)), (6, 7, new Rgb24(255, 255, 0)),
        (1, 8, new Rgb24(255, 0, 0)), (8, 9, new Rgb24(255, 0, 0)), (9, 10, new Rgb24(255, 0, 0)),
        (1, 11, new Rgb24(255, 0, 255)), (11, 12, new Rgb24(255, 0, 255)), (12, 13, new Rgb24(255, 0, 255))
    ];

    /// <summary>Hand bone chains: the wrist's five fingers, four bones each.</summary>
    private static readonly (int A, int B)[] HandLinks =
    [
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (0, 9), (9, 10), (10, 11), (11, 12),
        (0, 13), (13, 14), (14, 15), (15, 16),
        (0, 17), (17, 18), (18, 19), (19, 20)
    ];

    /// <summary>
    /// Scales and centres the person so it fills the canvas, using the visible body keypoints as the bounding
    /// box. The same transform is applied to the hands, so a wrist and its hand stay attached.
    /// </summary>
    /// <param name="frameIndices">
    /// COCO indices to frame on, or null to frame the whole body. A head close-up passes the head joints, because
    /// a body-scale render gives the head a handful of pixels. If none of the named joints are visible the call is
    /// refused — falling back to the whole body would silently produce a body shot labelled as a head shot.
    /// </param>
    public static PosePerson FitToCanvas(
        PosePerson person, int canvas = DefaultCanvas, IReadOnlyCollection<int>? frameIndices = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        RequireCanvas(canvas);

        var visible = person.Body
            .Select((point, index) => (point, index))
            .Where(entry => entry.point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .Where(entry => frameIndices is null || frameIndices.Contains(entry.index))
            .Select(entry => entry.point)
            .ToArray();

        if (visible.Length == 0)
        {
            throw new InvalidOperationException(
                frameIndices is null
                    ? "A pose with no visible body keypoint cannot be fitted to the canvas, so no skeleton can "
                        + "be rendered from it."
                    : $"None of the {frameIndices.Count} requested framing joints are visible, so there is nothing "
                        + "to frame the render on.");
        }

        var minX = visible.Min(p => p.X);
        var maxX = visible.Max(p => p.X);
        var minY = visible.Min(p => p.Y);
        var maxY = visible.Max(p => p.Y);

        var width = Math.Max(maxX - minX, 1.0);
        var height = Math.Max(maxY - minY, 1.0);
        var scale = Math.Min((canvas - (2.0 * Margin)) / width, (canvas - (2.0 * Margin)) / height);
        var offsetX = ((canvas - (width * scale)) / 2.0) - (minX * scale);
        var offsetY = ((canvas - (height * scale)) / 2.0) - (minY * scale);

        IReadOnlyList<PoseKeypoint> Transform(IReadOnlyList<PoseKeypoint> source) =>
            source.Select(point => new PoseKeypoint(
                (point.X * scale) + offsetX,
                (point.Y * scale) + offsetY,
                point.Confidence)).ToArray();

        return new PosePerson
        {
            Body = Transform(person.Body),
            LeftHand = Transform(person.LeftHand),
            RightHand = Transform(person.RightHand)
        };
    }

    /// <summary>Fits the person to the canvas and returns the PNG bytes of the rendered skeleton.</summary>
    public static byte[] RenderPng(
        PosePerson person, int canvas = DefaultCanvas, IReadOnlyCollection<int>? frameIndices = null)
    {
        var fitted = FitToCanvas(person, canvas, frameIndices);

        using var image = new Image<Rgb24>(canvas, canvas, Background);
        Rasterize(image, fitted);
        return EncodePng(image);
    }

    /// <summary>Renders without re-fitting, for callers that already positioned the keypoints.</summary>
    public static byte[] RenderPngWithoutFitting(PosePerson person, int canvas = DefaultCanvas)
    {
        ArgumentNullException.ThrowIfNull(person);
        RequireCanvas(canvas);

        using var image = new Image<Rgb24>(canvas, canvas, Background);
        Rasterize(image, person);
        return EncodePng(image);
    }

    private static void Rasterize(Image<Rgb24> image, PosePerson person)
    {
        var body = person.Body;
        foreach (var (a, b, color) in BodyLinks)
        {
            if (!TryGet(body, a, out var from) || !TryGet(body, b, out var to)) continue;

            // Both endpoints must clear the floor: a half-present limb is not drawn at all.
            if (from.Confidence <= OpenPosePoseJson.VisibilityFloor) continue;
            if (to.Confidence <= OpenPosePoseJson.VisibilityFloor) continue;

            DrawCapsule(image, color, from.X, from.Y, to.X, to.Y, BodyLineRadius);
        }

        for (var i = 0; i < body.Count; i++)
        {
            if (body[i].Confidence <= OpenPosePoseJson.VisibilityFloor) continue;
            FillDisc(image, JointColor, body[i].X, body[i].Y, BodyJointRadius);
        }

        DrawHand(image, person.LeftHand);
        DrawHand(image, person.RightHand);
    }

    private static void DrawHand(Image<Rgb24> image, IReadOnlyList<PoseKeypoint> hand)
    {
        if (hand.Count == 0) return;

        foreach (var (a, b) in HandLinks)
        {
            if (!TryGet(hand, a, out var from) || !TryGet(hand, b, out var to)) continue;
            if (from.Confidence <= OpenPosePoseJson.VisibilityFloor) continue;
            if (to.Confidence <= OpenPosePoseJson.VisibilityFloor) continue;

            DrawCapsule(image, HandLineColor, from.X, from.Y, to.X, to.Y, HandLineRadius);
        }

        for (var i = 0; i < hand.Count; i++)
        {
            if (hand[i].Confidence <= OpenPosePoseJson.VisibilityFloor) continue;
            FillDisc(image, HandJointColor, hand[i].X, hand[i].Y, HandJointRadius);
        }
    }

    private static bool TryGet(IReadOnlyList<PoseKeypoint> keypoints, int index, out PoseKeypoint point)
    {
        if (index >= 0 && index < keypoints.Count)
        {
            point = keypoints[index];
            return true;
        }

        point = default;
        return false;
    }

    /// <summary>
    /// Draws a line of the given half-width by stamping discs along it, so the stroke has round ends and no
    /// gaps at steep angles.
    /// </summary>
    private static void DrawCapsule(Image<Rgb24> image, Rgb24 color, double x0, double y0, double x1, double y1, double radius)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var steps = Math.Max(1, (int)Math.Ceiling(length));

        for (var step = 0; step <= steps; step++)
        {
            var t = (double)step / steps;
            FillDisc(image, color, x0 + (dx * t), y0 + (dy * t), radius);
        }
    }

    private static void FillDisc(Image<Rgb24> image, Rgb24 color, double centerX, double centerY, double radius)
    {
        var minX = (int)Math.Floor(centerX - radius);
        var maxX = (int)Math.Ceiling(centerX + radius);
        var minY = (int)Math.Floor(centerY - radius);
        var maxY = (int)Math.Ceiling(centerY + radius);
        var radiusSquared = radius * radius;

        for (var y = minY; y <= maxY; y++)
        {
            if (y < 0 || y >= image.Height) continue;

            for (var x = minX; x <= maxX; x++)
            {
                if (x < 0 || x >= image.Width) continue;

                var dx = x - centerX;
                var dy = y - centerY;
                if ((dx * dx) + (dy * dy) > radiusSquared) continue;

                image[x, y] = color;
            }
        }
    }

    private static byte[] EncodePng(Image<Rgb24> image)
    {
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static void RequireCanvas(int canvas)
    {
        if (canvas <= 2 * Margin)
        {
            throw new InvalidOperationException(
                $"A pose canvas of {canvas}px leaves no room for a figure with a {Margin}px margin on each side. "
                + $"Use more than {2 * Margin}px.");
        }
    }
}
