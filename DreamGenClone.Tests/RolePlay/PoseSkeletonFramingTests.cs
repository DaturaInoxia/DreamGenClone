using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// How a pose is framed onto its canvas.
///
/// A head pose that carries a face channel must be framed on its FACE, not on its body. The body is the wrong box for
/// one, and measurably so: a face crop makes the estimator report body joints that are not in the picture — the
/// reference plate this feature was measured against came back with 11 "visible" body joints for a picture of a face,
/// including a hip well below the crop. Framing on those squeezed the head into the top of an otherwise empty canvas,
/// which reads as a broken pose rather than as a framing choice.
/// </summary>
public sealed class PoseSkeletonFramingTests
{
    private const int Canvas = 1024;

    /// <summary>FitToCanvas keeps a margin, so a framed extent lands at canvas minus twice it.</summary>
    private const double ExpectedFramedExtent = Canvas - (2 * 128);

    [Fact]
    public void AHeadPose_IsFramedOnItsFace_NotOnAStrayBodyJoint()
    {
        // A believable face cluster high in the frame, and the kind of stray joint a face crop produces: a "hip" far
        // below it. Framed on the body, the face would come out a speck.
        //
        // The cluster is 8 wide by 9 tall on purpose: the fit preserves aspect ratio and scales the LARGER extent to
        // the canvas, so a fixture that is wider than it is tall could not fill the height and would test nothing.
        var face = Enumerable.Range(0, 70)
            .Select(index => new PoseKeypoint(500 + (index % 8), 300 + (index / 8), 1.0))
            .ToArray();

        var body = Enumerable.Range(0, 18)
            .Select(index => new PoseKeypoint(500, 300, index is 0 or 14 or 15 or 16 or 17 ? 1.0 : 0.0))
            .ToArray();
        body[9] = new PoseKeypoint(520, 1400, 1.0);

        var fitted = PoseSkeletonRenderer.FitToCanvas(
            new PosePerson { Body = body, Face = face }, Canvas);

        var faceHeight = fitted.Face.Max(point => point.Y) - fitted.Face.Min(point => point.Y);

        Assert.True(
            faceHeight > ExpectedFramedExtent * 0.9,
            $"The face should have been framed to fill the canvas but spans {faceHeight:0} of {Canvas}.");
    }

    [Fact]
    public void ABodyPose_IsStillFramedOnItsBody()
    {
        // No face channel, so the body is the only thing there is to frame on — and that must not have changed.
        var body = Enumerable.Range(0, 18)
            .Select(index => new PoseKeypoint(400 + (index % 3 * 100), 200 + (index * 30), 1.0))
            .ToArray();

        var fitted = PoseSkeletonRenderer.FitToCanvas(new PosePerson { Body = body }, Canvas);

        var bodyHeight = fitted.Body.Max(point => point.Y) - fitted.Body.Min(point => point.Y);

        Assert.True(
            bodyHeight > ExpectedFramedExtent * 0.9,
            $"A body pose should still be framed on its body but spans {bodyHeight:0} of {Canvas}.");
    }
}
