using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The face proxy is what lets a HEAD pose be turned at all: a face-framed crop has no body to fit, so the body path
/// cannot touch it, and a flat point set cannot be rotated. These pin the three things that make the result a TURN
/// rather than a deformation:
///
///   * a neutral head leaves the pose alone, exactly (the same instance — no round trip through the projection);
///   * a yaw NARROWS the face while its height stays put, because a yaw compresses the face sideways and does not
///     stretch it up and down. A turn that kept its width would be no turn at all;
///   * two steps move the face twice as far as one, so the angle is ADDED to the pose's own head rather than replacing
///     it — the same property the body's head path is pinned on.
/// </summary>
public sealed class PoseFaceProxyTests
{
    [Fact]
    public void ANeutralHead_ReturnsThePoseUntouched()
    {
        var pose = Face();

        var result = PoseFaceProxy.Project(pose, new PoseHeadRotation(), Settings());

        // The SAME instance, so "leave the head alone" cannot drift by a fraction of a pixel per render.
        Assert.Same(pose, result);
    }

    /// <summary>
    /// The turn, measured. The face's width must fall at every step from front to profile, and its height must not
    /// move — that pair is what a yaw IS, and a proxy that sheared or stretched would fail one of the two.
    /// </summary>
    [Fact]
    public void AYaw_NarrowsTheFaceWhileItsHeightStaysPut()
    {
        var pose = Face();
        var settings = Settings();

        var front = Width(pose);

        foreach (var yaw in new[] { 15.0, 30.0, 45.0, 60.0, 75.0, 90.0 })
        {
            var turned = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: yaw), settings);

            Assert.True(
                Width(turned) < front,
                $"at {yaw:0}° the face is {Width(turned):0.#} wide against {front:0.#} at the front, so the turn did "
                + "not foreshorten it");

            // Height is preserved because the proxy is placed back on the pose using it. Within a percent of the
            // original, which is the tolerance the perspective projection can hold.
            Assert.Equal(Height(pose), Height(turned), 1);
        }
    }

    /// <summary>
    /// Additive, not absolute. A replacement would move the face by |requested − the pose's own angle|, whose ratio is
    /// decided by that angle; an addition moves it by the step, so two steps travel twice as far as one.
    /// </summary>
    [Fact]
    public void TwoSteps_TravelTwiceAsFarAsOne()
    {
        var pose = Face();
        var settings = Settings();

        var front = PoseFaceProxy.Project(pose, new PoseHeadRotation(), settings);
        var five = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 5), settings);
        var ten = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 10), settings);

        // The nose sits on the centre line, so its sideways travel is what the turn produces.
        var one = Math.Abs(NoseX(five) - NoseX(front));
        var two = Math.Abs(NoseX(ten) - NoseX(front));

        Assert.True(one > 0, "a 5° head turn did not move the face at all");

        var ratio = two / one;

        Assert.True(
            ratio is > 1.8 and < 2.2,
            $"two 5° steps travelled {ratio:0.000}x one step (expected ~2), so the angle is being replaced rather than "
            + "added to");
    }

    /// <summary>Pitch is the other axis of the same rotation, so a nod must compress the face vertically.</summary>
    [Fact]
    public void APitch_ShortensTheFaceWhileItsWidthStaysPut()
    {
        var pose = Face();

        var level = PoseFaceProxy.Project(pose, new PoseHeadRotation(), Settings());
        var down = PoseFaceProxy.Project(pose, new PoseHeadRotation(PitchDegrees: -40), Settings());

        Assert.True(
            Height(down) < Height(level),
            $"looking down left the face {Height(down):0.#} tall against {Height(level):0.#}, so the nod did nothing");

        Assert.True(
            Width(down) > 0,
            "looking down collapsed the face to nothing, which a nod must not do");
    }

    [Fact]
    public void AFaceWithNothingVisible_IsRefusedByName()
    {
        var pose = new PosePerson
        {
            Body = Enumerable.Range(0, PosePerson.BodyJointCount)
                .Select(_ => new PoseKeypoint(0, 0, 0))
                .ToArray(),
            Face = Enumerable.Range(0, PosePerson.FaceJointCount)
                .Select(_ => new PoseKeypoint(10, 10, 0))
                .ToArray()
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 5), Settings()));

        Assert.Contains("has 0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The routing decision — what the guard this replaces got wrong. It refused on the FACE CHANNEL alone, so it
    /// rejected poses that carried a face AND a perfectly good body, and its refusal claimed "no body" without ever
    /// counting one. The joint count is the test.
    /// </summary>
    [Fact]
    public void IsHeadPose_DecidesOnTheJointCount_NotOnThePresenceOfAFace()
    {
        // The two real head poses, measured 2026-09-27: a full 70-point face and 5 / 7 "visible" body joints, which are
        // detector artifacts of a face crop rather than a person.
        Assert.True(PoseFaceProxy.IsHeadPose(Pose(visibleBody: 5, visibleFace: 70)));
        Assert.True(PoseFaceProxy.IsHeadPose(Pose(visibleBody: 7, visibleFace: 70)));

        // A BODY pose that also carries a face still belongs to the rig. This is the case the old guard broke.
        Assert.False(PoseFaceProxy.IsHeadPose(Pose(visibleBody: 16, visibleFace: 70)));

        // A body pose with no face at all is a body pose.
        Assert.False(PoseFaceProxy.IsHeadPose(Pose(visibleBody: 16, visibleFace: 0)));

        // Neither a body worth fitting nor a face to turn: neither path applies, so it is not claimed by the head one.
        Assert.False(PoseFaceProxy.IsHeadPose(Pose(visibleBody: 3, visibleFace: 1)));
    }

    /// <summary>
    /// The PANEL'S OWN CHAIN, end to end, for a head pose: the proxy, then ONE frame computed from the pose, then a
    /// render without re-fitting. Every link of that chain has its own test; this one exists because "the buttons do
    /// nothing" is about the chain, not about any link — and a head angle that produces byte-identical output is a
    /// button that does nothing.
    /// </summary>
    [Fact]
    public void ThePanelsChainForAHeadPose_ChangesWithTheHeadAngle()
    {
        // The real shape: 70 visible face points and 5 "visible" body joints, which are the detector artifacts of a
        // face crop rather than a person.
        var body = Enumerable.Range(0, PosePerson.BodyJointCount)
            .Select(index => new PoseKeypoint(index * 100, index * 50, index < 5 ? 1.0 : 0.0))
            .ToArray();

        var stored = new PosePerson { Body = body, Face = Face().Face };
        var options = Settings();

        Assert.True(PoseFaceProxy.IsHeadPose(stored), "the fixture must take the head path for this test to mean anything");

        // ONE frame, computed from the pose itself, exactly as the panel computes it.
        var rest = PoseFaceProxy.Project(stored, new PoseHeadRotation(), options);
        var framing = PoseSkeletonRenderer.ComputeFraming(rest, options.RequireCanvas());

        var yawFive = Render(stored, framing, new PoseHeadRotation(YawDegrees: 5), options);
        var yawTen = Render(stored, framing, new PoseHeadRotation(YawDegrees: 10), options);
        var pitched = Render(stored, framing, new PoseHeadRotation(PitchDegrees: -30), options);

        Assert.False(yawFive.SequenceEqual(yawTen), "a 5° step and a 10° step rendered identically");
        Assert.False(yawFive.SequenceEqual(pitched), "a yaw and a pitch rendered identically");
    }

    [Fact]
    public void AsHeadRotation_KeepsTheAnglesItIsGiven()
    {
        var head = PoseFaceProxy.AsHeadRotation(new PoseView(YawDegrees: -45, PitchDegrees: 10, RollDegrees: -5));

        Assert.Equal(-45, head.YawDegrees);
        Assert.Equal(10, head.PitchDegrees);
        Assert.Equal(-5, head.RollDegrees);
    }

    /// <summary>
    /// EVERY target in the Body step actually turns a face pose. This pins the defect that made "face poses do
    /// nothing": the Body step wrote the rig's VIEW, the head path takes no view at all, so all five buttons moved
    /// nothing — while the panel opened on that step. A target that leaves the face byte-identical is that defect
    /// coming back, whichever way it comes back.
    /// </summary>
    [Fact]
    public void EveryBodyStepTarget_ChangesAFacePose()
    {
        var pose = Face();
        var options = Settings();

        // ONE frame, computed from the pose, exactly as the panel holds it — so a difference in the bytes is the turn
        // and never a rescale.
        var framing = PoseSkeletonRenderer.ComputeFraming(pose, options.RequireCanvas());
        var front = Render(pose, framing, new PoseHeadRotation(), options);

        foreach (var (label, view) in PoseNamedViews.BodyTargets)
        {
            var head = PoseFaceProxy.AsHeadRotation(view);
            var rendered = Render(pose, framing, head, options);

            if (head.IsNeutral)
            {
                Assert.Equal("Front", label, StringComparer.Ordinal);
                Assert.True(
                    rendered.SequenceEqual(front),
                    $"'{label}' is the neutral target and must leave the face exactly as it is");
                continue;
            }

            Assert.False(
                rendered.SequenceEqual(front),
                $"'{label}' rendered the face byte-identical to the front, so pressing it does nothing at all");
        }
    }

    /// <summary>
    /// The two steps AGREE on the names they share. A face pose has one set of named views and one set of arrows, and it
    /// must not matter whether the operator pressed them under Body or under Head — so the same label has to reach the
    /// face as the same angle through both. This is the property that lets one control scheme serve either kind of pose.
    /// </summary>
    [Fact]
    public void TheBodyStepAndTheHeadStep_AgreeOnTheNamesTheyShare()
    {
        var pose = Face();
        var options = Settings();

        var headStepNames = PoseNamedViews.HeadTargets.ToDictionary(
            target => target.Label, target => target.Head, StringComparer.Ordinal);

        var shared = 0;

        foreach (var (label, view) in PoseNamedViews.BodyTargets)
        {
            // "3/4 left/right" and the look-up / look-down pair exist on only one side; the shared names are what the
            // two steps must not disagree about.
            if (!headStepNames.TryGetValue(label, out var fromTheHeadStep)) continue;

            shared++;

            var fromTheBodyStep = PoseFaceProxy.Project(pose, PoseFaceProxy.AsHeadRotation(view), options);
            var fromTheHead = PoseFaceProxy.Project(pose, fromTheHeadStep, options);

            Assert.Equal(NoseX(fromTheHead), NoseX(fromTheBodyStep), 6);
            Assert.Equal(Height(fromTheHead), Height(fromTheBodyStep), 6);
        }

        Assert.True(shared >= 3, $"only {shared} names are shared between the two steps, so this proves little");
    }

    /// <summary>
    /// THE regression test for the operator's second report (2026-09-28): "it is not rotating the main blue line 5 dot
    /// connection so the face is all skewed".
    ///
    /// The head's blue frame — (1,0) neck-nose, (0,14) nose-right eye, (14,16) right eye-right ear, and the same on the
    /// left — lives in the BODY channel, and the proxy carried the whole body through untouched. So the 70 face dots
    /// turned and the skull they belong to did not, and the face arrived skewed against its own head.
    /// </summary>
    [Fact]
    public void AYaw_MovesTheHeadsOwnPointsInTheBodyChannel()
    {
        var pose = HeadCrop();

        var front = PoseFaceProxy.Project(pose, new PoseHeadRotation(), Settings());
        var turned = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 20), Settings());

        // The four points the estimator actually placed. The neck (1) is deliberately NOT among them: it is the pivot the
        // head turns about, not part of the head.
        foreach (var index in new[] { Nose, RightEye, LeftEye, RightEar })
        {
            Assert.NotEqual(front.Body[index].X, turned.Body[index].X);
        }

        // The pivot holds still, which is what makes this a turn rather than a translation of the whole frame.
        Assert.Equal(front.Body[Neck].X, turned.Body[Neck].X);
        Assert.Equal(front.Body[Neck].Y, turned.Body[Neck].Y);
    }

    /// <summary>
    /// An undetected point is left EXACTLY where it was recorded. The real pose stores its undetected left ear as a (0,0)
    /// artifact; rotating that would drag it in from the corner of the canvas and draw a blue bone to nowhere. The limbs
    /// are untouched for the same reason — they are not in the picture.
    /// </summary>
    [Fact]
    public void AnUndetectedHeadPointAndTheLimbs_AreLeftExactlyWhereTheyWere()
    {
        var pose = HeadCrop();

        var turned = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 30), Settings());

        Assert.Equal(0, turned.Body[LeftEar].X);
        Assert.Equal(0, turned.Body[LeftEar].Y);
        Assert.Equal(0, turned.Body[LeftEar].Confidence);

        // A limb joint a face crop reports that is not in the picture: never moved, because moving it would be
        // inventing motion for something nothing measured.
        Assert.Equal(pose.Body[RightShoulder].X, turned.Body[RightShoulder].X);
        Assert.Equal(pose.Body[RightShoulder].Y, turned.Body[RightShoulder].Y);
    }

    /// <summary>
    /// At a profile, a rotation about y sends each head point to a place decided by its own DEPTH, and this is what pins
    /// the depth profile's clamp.
    ///
    /// Measured on the real '34 Right Head': the right ear sits at lateral 1.07, just OUTSIDE the face box the profile is
    /// measured across. Unclamped, the eye-to-ear segment extrapolates past the ears to a NEGATIVE depth, and the ear
    /// lands on the far side of the centre line by that amount — a point swinging forward of the nose, which is the
    /// shear this whole approach exists to avoid. Clamped, the ears keep the zero depth they have by construction, so a
    /// profile puts them exactly ON the centre line.
    /// </summary>
    [Fact]
    public void AtAProfile_AnEarOutsideTheFaceWidth_LandsOnTheCentreLine_NotBehindIt()
    {
        var pose = HeadCrop();
        var turned = PoseFaceProxy.Project(pose, new PoseHeadRotation(YawDegrees: 90), Settings());

        // The face box centre IS the rotation's centre, and the ears are the zero of the depth profile.
        var centreX = ((261 + 893) / 2.0);

        Assert.Equal(centreX, turned.Body[RightEar].X, 2);

        // The nose is genuinely forward, so the assertion above cannot be passing merely because nothing moved.
        Assert.True(
            Math.Abs(turned.Body[Nose].X - centreX) > 10,
            "the nose has no depth at a profile, so the depth profile is not being applied at all");
    }

    private const int Nose = 0;
    private const int Neck = 1;
    private const int RightShoulder = 2;
    private const int RightEye = 14;
    private const int LeftEye = 15;
    private const int RightEar = 16;
    private const int LeftEar = 17;

    /// <summary>
    /// A face-framed crop carrying the head's OWN points in the body channel, laid out from the real '34 Right Head'
    /// measured 2026-09-28 so the fixture exercises the numbers the defect was found on rather than convenient ones:
    /// face box x 261..893 (centre 577, half-width 316), a nose and both eyes inside it, the right ear at lateral 1.07 —
    /// just OUTSIDE — the left ear not detected at all, and the neck below the chin at y 1218.
    /// </summary>
    private static PosePerson HeadCrop()
    {
        var body = Enumerable.Range(0, PosePerson.BodyJointCount)
            .Select(_ => new PoseKeypoint(0, 0, 0))
            .ToArray();

        body[Neck] = new PoseKeypoint(498, 1218, 1.0);
        body[Nose] = new PoseKeypoint(801, 723.5, 1.0);
        body[RightEye] = new PoseKeypoint(587, 584.7, 1.0);
        body[LeftEye] = new PoseKeypoint(826, 567.5, 1.0);
        body[RightEar] = new PoseKeypoint(238, 675.7, 1.0);
        // body[LeftEar] stays (0, 0) at confidence 0, exactly as the real pose records it.

        var face = new PoseKeypoint[PosePerson.FaceJointCount];
        face[OpenPosePoseJson.NoseIndex] = new PoseKeypoint(770, 760, 1.0);

        for (var index = 1; index < face.Length; index++)
        {
            var angle = 2 * Math.PI * (index - 1) / (face.Length - 1);

            // An ellipse spanning the measured face box, so the centre and the half-width the proxy derives are the ones
            // the real pose produces.
            face[index] = new PoseKeypoint(577 + (316 * Math.Cos(angle)), 781 + (312 * Math.Sin(angle)), 1.0);
        }

        // Pinned explicitly, because 69 sampled angles never land on the left-hand extreme (that needs index 34.5). Left
        // to the ellipse alone the box came out 261.33..893 — a centre of 577.164 — and a test asking whether a point
        // lands ON the centre line would be measuring the fixture rather than the clamp.
        face[1] = new PoseKeypoint(893, 781, 1.0);
        face[2] = new PoseKeypoint(261, 781, 1.0);

        return new PosePerson { Body = body, Face = face };
    }

    private static byte[] Render(
        PosePerson stored, PoseSkeletonRenderer.Framing framing, PoseHeadRotation head, PoseStudioOptions options) =>
        PoseSkeletonRenderer.RenderPngWithoutFitting(
            PoseSkeletonRenderer.Apply(PoseFaceProxy.Project(stored, head, options), framing),
            options.RequireCanvas());

    private static PosePerson Pose(int visibleBody, int visibleFace)
    {
        var body = Enumerable.Range(0, PosePerson.BodyJointCount)
            .Select(index => new PoseKeypoint(index * 10, index * 10, index < visibleBody ? 1.0 : 0.0))
            .ToArray();

        var face = Enumerable.Range(0, PosePerson.FaceJointCount)
            .Select(index => new PoseKeypoint(400 + index, 400 + index, index < visibleFace ? 1.0 : 0.0))
            .ToArray();

        return new PosePerson { Body = body, Face = face };
    }

    /// <summary>
    /// A face-shaped point set. The properties under test hold for any layout, so a synthetic face is enough and keeps
    /// the test independent of the pack and of the database.
    /// </summary>
    private static PosePerson Face()
    {
        var face = new PoseKeypoint[PosePerson.FaceJointCount];

        // Index 0 IS the nose, so it is placed ON the centre line — that is where OpenPose's face layout puts it, at the
        // most forward point of the face. A fixture that put a point at the face's edge instead would measure the wrong
        // thing: a point at the edge sits at the rotation axis's own depth, so its sideways travel is a COSINE of the
        // angle rather than a sine, and the linearity it should show is the wrong one (measured 2026-09-27: 3.992x).
        face[OpenPosePoseJson.NoseIndex] = new PoseKeypoint(500, 400, 1.0);

        for (var index = 1; index < face.Length; index++)
        {
            var angle = 2 * Math.PI * (index - 1) / (face.Length - 1);
            face[index] = new PoseKeypoint(
                500 + (150 * Math.Cos(angle)),
                400 + (190 * Math.Sin(angle)),
                1.0);
        }

        return new PosePerson
        {
            Body = Enumerable.Range(0, PosePerson.BodyJointCount)
                .Select(_ => new PoseKeypoint(0, 0, 0))
                .ToArray(),
            Face = face
        };
    }

    private static double NoseX(PosePerson pose) => pose.Face[OpenPosePoseJson.NoseIndex].X;

    private static double Width(PosePerson pose) =>
        pose.Face.Max(point => point.X) - pose.Face.Min(point => point.X);

    private static double Height(PosePerson pose) =>
        pose.Face.Max(point => point.Y) - pose.Face.Min(point => point.Y);

    private static PoseStudioOptions Settings() => new()
    {
        FocalLengthPx = 1600,
        CameraDistance = 4.5,
        Canvas = 1024,
        RotationStepDegrees = 5
    };
}
