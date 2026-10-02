using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The pose facing measurement, pinned against the rig's own angles.
///
/// The rig is the only source of EXACT ground truth available: it projects the figure at a yaw the code chose, so a
/// pose at -45 degrees IS a three-quarter-left view and nothing about that is a judgement call. Every threshold in
/// the measurement is derived from those readings, and these tests hold them to it.
///
/// The defect being guarded: the shoulder ORDERING can only separate front from back, and it stays negative through a
/// 45-degree turn (measured: -0.67 at yaw 0, -0.47 at +/-45, 0.00 at +/-90). Every 3/4 pose in the library was
/// therefore stored as "front" and no re-run could ever correct it.
/// </summary>
public sealed class PoseFacingMeasurementTests
{
    [Theory]
    [InlineData(0, PoseFacingDirection.Front)]
    [InlineData(-45, PoseFacingDirection.ThreeQuarterLeft)]
    [InlineData(45, PoseFacingDirection.ThreeQuarterRight)]
    [InlineData(-90, PoseFacingDirection.ProfileLeft)]
    [InlineData(90, PoseFacingDirection.ProfileRight)]
    public void TheRigAtAKnownAngleMeasuresAsThatDirection(double yaw, PoseFacingDirection expected)
    {
        using var fixture = new PoseLibraryTestFixture();
        var projected = fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: yaw));

        // Declared FRONT, which is what the packs declare for these categories: the measurement has to be able to
        // contradict it, or a 3/4 pose can never be corrected.
        var metadata = PoseMetadataAnalyzer.Classify(projected, UprightFrontDeclaration);

        Assert.Equal(expected, metadata.Direction);
    }

    [Fact]
    public void TheTurnIsMeasuredEvenThoughTheShoulderOrderingStillReadsFront()
    {
        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: 45));

        var observation = PoseMetadataAnalyzer.Measure(turned);

        // The evidence for why this needed fixing: the ordering is still negative at 45 degrees, so the old
        // front-from-back test called it front, while the shoulder line has already collapsed from 0.67 to 0.47.
        Assert.True(observation.ShoulderOrderRatio < 0, "a 45-degree turn still orders the shoulders as a front view");
        Assert.InRange(observation.ShoulderSpanOverTorso, 0.40, 0.55);
        Assert.True(observation.HeadOffsetOverTorso > 0, "a turn to her right puts the nose at larger x");

        // And the measurement disagrees with the declared front, which is the whole point.
        Assert.Equal(PoseFacingDirection.ThreeQuarterRight, observation.MeasuredTurn(PoseStance.Standing));
    }

    [Fact]
    public void ATurnedShoulderLineWithAHeadFacingTheCameraIsNotGuessedAt()
    {
        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: -45));

        // A slim figure reads "turned" from the shoulders for its own proportions, while its head faces the camera.
        // The two signals then disagree, and the honest answer is to keep what was declared and say so.
        var body = turned.Body.ToArray();
        body[0] = body[0] with { X = body[1].X };

        var metadata = PoseMetadataAnalyzer.Classify(new PosePerson { Body = body }, UprightFrontDeclaration);

        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.True(metadata.NeedsReview);
        Assert.Contains("shoulders read turned", metadata.ReviewNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APoseWhoseTorsoIsNotUprightKeepsItsDeclaredDirection()
    {
        using var fixture = new PoseLibraryTestFixture();

        // The fixture's keypoints are a flat diagonal: the torso is not upright in the image, so the shoulder span
        // cannot be compared with the calibrated values and the declared direction stands.
        fixture.WriteRaw(
            "pack.json",
            """
            { "name": "Test pack", "rating": "sfw",
              "categories": { "on_back": { "stance": "lying", "direction": "back", "camera": "from-above" } } }
            """);

        var pose = await fixture.ImportPoseAsync("on_back", "on_back/512768/on_back017.json");

        Assert.Equal(PoseFacingDirection.Back, pose.Direction);
        Assert.False(pose.MetadataNeedsReview);
    }

    [Fact]
    public void TheComposedPromptSaysWhichWaySheIsTurned()
    {
        using var fixture = new PoseLibraryTestFixture();
        var projected = fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: -45));

        var metadata = PoseMetadataAnalyzer.Classify(projected, UprightFrontDeclaration);

        // The prompt and the reference plan are both derived from the direction, so a corrected direction has to reach
        // the wording as well as the column.
        Assert.Contains("her left side", metadata.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecomputeRepointsStoredPosesButNeverAnOperators()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw(
            "pack.json",
            """
            { "name": "Test pack", "rating": "sfw",
              "categories": { "standing": { "stance": "standing", "direction": "front", "camera": "eye-level" } } }
            """);

        var pose = await fixture.ImportPoseAsync("standing", "standing/512768/standing028.json");
        Assert.Equal(PoseFacingDirection.Front, pose.Direction);

        // The row's keypoints ARE a 45-degree turn, which is the state the old measurement could not detect.
        var turned = fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: 45));
        pose.KeypointsJson = OpenPosePoseJson.Serialize(turned);
        await fixture.Repository.UpsertAsync(pose);

        var result = await fixture.Importer.RecomputeFacingAsync();

        var repaired = await fixture.Repository.GetAsync(pose.Id);
        Assert.NotNull(repaired);
        Assert.Equal(PoseFacingDirection.ThreeQuarterRight, repaired!.Direction);
        Assert.Contains("her right side", repaired.MetadataPrompt, StringComparison.Ordinal);
        Assert.Equal(1, result.Repointed);

        // An operator's own metadata is never recomputed, whatever their keypoints measure.
        repaired.KeypointsJson = OpenPosePoseJson.Serialize(
            fixture.Service.ProjectAuthoredPose(new PoseView(YawDegrees: -90)));
        repaired.MetadataOperatorEdited = true;
        await fixture.Repository.UpsertAsync(repaired);

        var second = await fixture.Importer.RecomputeFacingAsync();

        var kept = await fixture.Repository.GetAsync(pose.Id);
        Assert.NotNull(kept);
        Assert.Equal(PoseFacingDirection.ThreeQuarterRight, kept!.Direction);
        Assert.Equal(0, second.Repointed);
    }

    [Theory]
    // Edge-on shoulder line: a side view, and the head names the side.
    [InlineData(95, 105, 60, PoseFacingDirection.ProfileLeft)]
    [InlineData(95, 105, 140, PoseFacingDirection.ProfileRight)]
    // Shoulder line across the view: pointing at or away from the camera, and the ordering says which.
    [InlineData(60, 140, 100, PoseFacingDirection.Front)]
    [InlineData(140, 60, 100, PoseFacingDirection.Back)]
    public void AQuadrupedIsMeasuredOnItsOwnTerms(
        double rightShoulderX, double leftShoulderX, double noseX, PoseFacingDirection expected)
    {
        using var fixture = new PoseLibraryTestFixture();
        var observation = PoseMetadataAnalyzer.Measure(Quadruped(rightShoulderX, leftShoulderX, noseX));

        // The upright gate can never apply to this figure, which is exactly why all fours needed its own rule.
        Assert.True(observation.TorsoVerticalRatio < PoseGeometryObservation.FoldedTorsoRatio);
        Assert.Equal(expected, observation.MeasuredTurn(PoseStance.AllFours));
    }

    [Fact]
    public void TheUprightRuleWouldGetAQuadrupedWrongWhichIsWhyItHasItsOwn()
    {
        using var fixture = new PoseLibraryTestFixture();

        // A quadruped with its shoulder line across the view and its head far to one side. The upright rule reads the
        // span as a turn MAGNITUDE and calls this a three-quarter view — but the ordering says the body points away
        // from the camera, which is what the operator reported for `all_fours 006` in the shipped pack.
        var observation = PoseMetadataAnalyzer.Measure(Quadruped(140, 60, 190));

        Assert.True(observation.ShoulderSpanOverTorso < PoseGeometryObservation.FrontalSpanCut,
            "the span is in the range the upright rule would read as a 3/4");
        Assert.Equal(PoseFacingDirection.Back, observation.MeasuredQuadrupedFacing());
    }

    [Fact]
    public void AQuadrupedIsNeverCalledAThreeQuarterView()
    {
        using var fixture = new PoseLibraryTestFixture();

        // The span cannot support a turn magnitude on a body whose own torso is shortened by the same turn, so the
        // quadruped rule must not claim a 3/4 at any shoulder span or head offset.
        for (var span = 0.0; span <= 1.0; span += 0.05)
        {
            for (var head = -0.5; head <= 0.5; head += 0.1)
            {
                var measured = PoseMetadataAnalyzer
                    .Measure(Quadruped(100 + (span * 100) / 2, 100 - (span * 100) / 2, 100 + (head * 200)))
                    .MeasuredQuadrupedFacing();

                Assert.NotEqual(PoseFacingDirection.ThreeQuarterLeft, measured);
                Assert.NotEqual(PoseFacingDirection.ThreeQuarterRight, measured);
            }
        }
    }

    [Fact]
    public void AQuadrupedWithAnEdgeOnShoulderLineAndACentredHeadIsNotGuessedAt()
    {
        using var fixture = new PoseLibraryTestFixture();
        var observation = PoseMetadataAnalyzer.Measure(Quadruped(95, 105, 100));

        Assert.Equal(PoseFacingDirection.Unknown, observation.MeasuredTurn(PoseStance.AllFours));
        Assert.True(observation.TurnSignalsDisagree(PoseStance.AllFours));

        var metadata = PoseMetadataAnalyzer.Classify(
            Quadruped(95, 105, 100),
            new PoseMetadataDeclaration(
                PoseStance.AllFours, PoseFacingDirection.Front, PoseCameraAngle.Unknown, PoseContentRating.Nsfw));

        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.True(metadata.NeedsReview);
    }

    /// <summary>
    /// A figure on hands and knees: the spine runs along x from the neck to the hips, so the torso is horizontal by
    /// construction and the upright gate can never apply. The shoulder line is given in x, because that is what
    /// separates an edge-on quadruped (a side view) from one pointing at or away from the camera.
    /// </summary>
    private static PosePerson Quadruped(double rightShoulderX, double leftShoulderX, double noseX) => new()
    {
        Body =
        [
            new PoseKeypoint(noseX, 60, 1),          // nose
            new PoseKeypoint(100, 100, 1),           // neck
            new PoseKeypoint(rightShoulderX, 100, 1),
            new PoseKeypoint(rightShoulderX - 20, 180, 1),
            new PoseKeypoint(rightShoulderX - 40, 260, 1),
            new PoseKeypoint(leftShoulderX, 100, 1),
            new PoseKeypoint(leftShoulderX + 20, 180, 1),
            new PoseKeypoint(leftShoulderX + 40, 260, 1),
            new PoseKeypoint(300, 100, 1),           // right hip
            new PoseKeypoint(360, 180, 1),
            new PoseKeypoint(420, 260, 1),           // right ankle
            new PoseKeypoint(300, 140, 1),           // left hip
            new PoseKeypoint(360, 220, 1),
            new PoseKeypoint(420, 300, 1),           // left ankle
            new PoseKeypoint(noseX - 5, 55, 1),      // right eye
            new PoseKeypoint(noseX + 5, 55, 1),      // left eye
            new PoseKeypoint(noseX - 10, 58, 1),     // right ear
            new PoseKeypoint(noseX + 10, 58, 1)      // left ear
        ]
    };

    /// <summary>What the packs declare for an ordinary upright category: standing, square to the camera, clothed.</summary>
    private static readonly PoseMetadataDeclaration UprightFrontDeclaration = new(
        PoseStance.Standing,
        PoseFacingDirection.Front,
        PoseCameraAngle.EyeLevel,
        PoseContentRating.Sfw);
}
