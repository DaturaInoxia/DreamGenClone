using DreamGenClone.Web.Application.RolePlay;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The renderer is the C# replacement for the hand-run Python that produced the committed skeletons, so these
/// tests pin the two things that are easy to get quietly wrong: the canvas the render lands on, and the pair
/// predicate that decides whether a limb exists at all.
/// </summary>
public sealed class PoseSkeletonRendererTests
{
    private static readonly Rgb24 LeftArmColour = new(255, 255, 0);

    /// <summary>
    /// The fitted figure leaves headroom, and this is not cosmetic. The skeleton's topmost joint is a nose or an
    /// eye — the rig has no skull — so a figure fitted tight to the canvas leaves a renderer nowhere to put the
    /// crown and hair it will draw above them, and it draws them anyway, straight off the top of the frame.
    /// Measured 2026-09-24: at a 48 px margin both the SDL/ControlNet route and Qwen-2.1 cropped the head, which
    /// cost three of the eighteen joints when the result was read back with DWPose.
    /// </summary>
    [Fact]
    public void FitToCanvas_LeavesHeadroomSoARenderCannotCropTheHead()
    {
        const int canvas = 1024;
        var person = TallPerson();

        var fitted = PoseSkeletonRenderer.FitToCanvas(person, canvas);

        var visible = fitted.Body
            .Where(point => point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .ToArray();

        Assert.True(
            visible.Min(point => point.Y) >= PoseSkeletonRenderer.Margin,
            $"the top of the fitted figure is {visible.Min(point => point.Y):0.0} but needs at least "
            + $"{PoseSkeletonRenderer.Margin} of headroom");
        Assert.True(
            visible.Max(point => point.Y) <= canvas - PoseSkeletonRenderer.Margin,
            $"the bottom of the fitted figure is {visible.Max(point => point.Y):0.0} but needs at least "
            + $"{PoseSkeletonRenderer.Margin} of clearance");
    }

    /// <summary>A tall narrow figure, so the fit is bound by height and the margins are what is being measured.</summary>
    private static PosePerson TallPerson()
    {
        var body = new PoseKeypoint[PoseMannequin.CocoJointCount];
        for (var index = 0; index < body.Length; index++)
        {
            // 1.8 units tall and 0.2 wide: any uniform fit of this has to be driven by the height.
            body[index] = new PoseKeypoint(500 + (index % 3), 100 + (index * 90), 1.0);
        }

        return new PosePerson { Body = body };
    }

    [Fact]
    public void RenderPng_UsesTheConfiguredCanvas()
    {
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "render sample");

        var bytes = PoseSkeletonRenderer.RenderPng(person);

        using var image = Image.Load<Rgb24>(bytes);
        Assert.Equal(PoseSkeletonRenderer.DefaultCanvas, image.Width);
        Assert.Equal(PoseSkeletonRenderer.DefaultCanvas, image.Height);
    }

    [Fact]
    public void RenderPng_DrawsALimbOnlyWhenBothEndpointsAreVisible()
    {
        // Leave the neck (1) and the left shoulder (5) as the only visible joints. The single link (1,5) is then
        // the only body line that can be drawn, so every yellow pixel in the render comes from that one pair.
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "pair sample");

        using var both = RenderFor(person, [1, 5]);
        Assert.True(CountPixels(both, LeftArmColour) > 0, "the (1,5) link should be drawn when both of its joints are visible");

        // Drop one endpoint and the line disappears entirely — a lenient renderer would draw it to the origin.
        using var one = RenderFor(person, [1]);
        Assert.Equal(0, CountPixels(one, LeftArmColour));
    }

    /// <summary>Renders the pose with only the listed joints left visible, so a single link can be isolated.</summary>
    private static Image<Rgb24> RenderFor(PosePerson person, int[] visibleJoints)
    {
        var visible = visibleJoints.ToHashSet();
        var masked = new PosePerson
        {
            Body = person.Body
                .Select((point, index) => visible.Contains(index)
                    ? point
                    : new PoseKeypoint(point.X, point.Y, 0.0))
                .ToArray()
        };

        return Image.Load<Rgb24>(PoseSkeletonRenderer.RenderPng(masked));
    }

    [Fact]
    public void FitToCanvas_PreservesBoneLengthRatios()
    {
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "fit sample");

        var fitted = PoseSkeletonRenderer.FitToCanvas(person);

        var neckToHipBefore = Distance(person.Body[1], person.Body[8]);
        var hipToAnkleBefore = Distance(person.Body[8], person.Body[10]);
        var neckToHipAfter = Distance(fitted.Body[1], fitted.Body[8]);
        var hipToAnkleAfter = Distance(fitted.Body[8], fitted.Body[10]);

        Assert.Equal(neckToHipBefore / hipToAnkleBefore, neckToHipAfter / hipToAnkleAfter, 6);
        Assert.True(neckToHipAfter > neckToHipBefore, "the figure should be scaled up to fill the canvas");
    }

    [Fact]
    public void FitToCanvas_KeepsTheHandsAttachedToTheWrists()
    {
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "fit sample");
        var withHand = new PosePerson
        {
            Body = person.Body,
            LeftHand = Enumerable.Range(0, PosePerson.HandJointCount)
                .Select(i => new PoseKeypoint(person.Body[7].X + i, person.Body[7].Y + i, 1.0))
                .ToArray()
        };

        var fitted = PoseSkeletonRenderer.FitToCanvas(withHand);

        // The hand's wrist joint (index 0) tracks the body's left wrist through the same transform.
        Assert.Equal(fitted.Body[7].X, fitted.LeftHand[0].X, 6);
        Assert.Equal(fitted.Body[7].Y, fitted.LeftHand[0].Y, 6);
    }

    [Fact]
    public void RenderPng_ACanvasWithNoRoomForTheFigure_IsRefused()
    {
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "tiny canvas");

        var error = Assert.Throws<InvalidOperationException>(() => PoseSkeletonRenderer.RenderPng(person, 64));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FitToCanvas_APoseWithNoVisibleBody_IsRefused()
    {
        var person = OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "empty sample");
        var invisible = new PosePerson
        {
            Body = person.Body.Select(point => new PoseKeypoint(point.X, point.Y, 0.0)).ToArray()
        };

        var error = Assert.Throws<InvalidOperationException>(() => PoseSkeletonRenderer.FitToCanvas(invisible));

        Assert.Contains("no visible body keypoint", error.Message, StringComparison.Ordinal);
    }

    private static double Distance(PoseKeypoint a, PoseKeypoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static int CountPixels(Image<Rgb24> image, Rgb24 colour)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y].Equals(colour)) count++;
            }
        }

        return count;
    }
}
