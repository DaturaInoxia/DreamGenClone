using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The pose metadata analyzer: what the keypoints can decide, what they must NOT decide, and what happens when the
/// measurement disagrees with the pack's declaration.
///
/// The rules under test are the result of a measurement over all 579 shipped poses (see the review notes in
/// <see cref="PoseMetadataAnalyzer"/>): the shoulder ordering reads front from back on the two packs whose own names
/// state the answer (on_stomach 15/15 back, on_back 0/21 back), it reads nothing at all on an all-fours pose (6/12
/// each way), and it is gated on both the shoulder separation and an upright torso for exactly that reason.
/// </summary>
public sealed class PoseMetadataAnalyzerTests
{
    [Fact]
    public void AnUprightFigureWithTheSubjectsRightOnTheLeftIsReadAsFacingTheCamera()
    {
        var observation = PoseMetadataAnalyzer.Measure(Upright(rightShoulderX: 60, leftShoulderX: 140));

        Assert.Equal(PoseFacingDirection.Front, observation.MeasuredFacing);
        Assert.True(observation.FaceVisible);
    }

    [Fact]
    public void TheSameFigureWithTheShoulderOrderSwappedIsReadAsBackFacing()
    {
        var observation = PoseMetadataAnalyzer.Measure(Upright(rightShoulderX: 140, leftShoulderX: 60));

        Assert.Equal(PoseFacingDirection.Back, observation.MeasuredFacing);
    }

    [Fact]
    public void ShouldersSittingAlmostOnTopOfEachOtherDecideNothing()
    {
        // A profile view and a foreshortened quadruped both put the two shoulders a few percent apart, where the
        // ordering carries no facing at all. Reading one would invent an answer.
        var observation = PoseMetadataAnalyzer.Measure(Upright(rightShoulderX: 95, leftShoulderX: 105));

        Assert.Equal(PoseFacingDirection.Unknown, observation.MeasuredFacing);
    }

    [Fact]
    public void AFoldedTorsoDecidesNothingEvenWhenTheShoulderOrderIsWide()
    {
        // The all-fours case, measured on the shipped pack: the shoulders ARE wide apart and they swap sides between
        // poses (6 of 12 each way), because the ordering only encodes front from back when the shoulder line is
        // roughly horizontal in the world and the camera is level.
        var observation = PoseMetadataAnalyzer.Measure(Folded());

        Assert.True(observation.ShoulderSpanRatio > PoseGeometryObservation.MinimumDecisiveOrderRatio);
        Assert.Equal(PoseFacingDirection.Unknown, observation.MeasuredFacing);
    }

    [Fact]
    public void ADeclarationIsUsedAsItStandsAndIsNotFlaggedWhenTheMeasurementAgrees()
    {
        var metadata = PoseMetadataAnalyzer.Classify(
            Upright(rightShoulderX: 60, leftShoulderX: 140),
            Declared(PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Equal(PoseStance.Standing, metadata.Stance);
        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.False(metadata.NeedsReview);
        Assert.Equal(string.Empty, metadata.ReviewNote);
    }

    [Fact]
    public void ADeclarationContradictedByTheKeypointsIsKeptAndFlaggedWithItsEvidence()
    {
        // Declared front-facing, measured back-facing. The DECLARED value is still what a render uses — the pack knows
        // what its poses are and the keypoints only have one reading of the shoulder order — but the disagreement is
        // recorded rather than averaged away.
        var metadata = PoseMetadataAnalyzer.Classify(
            Upright(rightShoulderX: 140, leftShoulderX: 60),
            Declared(PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.True(metadata.NeedsReview);
        Assert.Contains("declared front", metadata.ReviewNote, StringComparison.Ordinal);
        Assert.Contains("shoulders read back", metadata.ReviewNote, StringComparison.Ordinal);
        Assert.Contains("shoulder-order", metadata.ReviewNote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMeasurementDoesNotContradictAFoldedDeclarationTheOrderingCannotRead()
    {
        // No flag, because the measurement is not decisive here: flagging would put half of the all-fours category
        // into review over noise that has nothing to do with facing.
        var metadata = PoseMetadataAnalyzer.Classify(
            Folded(),
            Declared(PoseStance.AllFours, PoseFacingDirection.Front, PoseCameraAngle.Unknown, PoseContentRating.Sfw));

        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.False(metadata.NeedsReview);
    }

    [Fact]
    public void APackThatDeclaresNothingStillGetsTheFacingItsKeypointsCanDecide()
    {
        var metadata = PoseMetadataAnalyzer.Classify(
            Upright(rightShoulderX: 60, leftShoulderX: 140),
            PoseMetadataDeclaration.None);

        Assert.Equal(PoseFacingDirection.Front, metadata.Direction);
        Assert.Equal(PoseStance.Unknown, metadata.Stance);
        Assert.Equal(PoseContentRating.Unrated, metadata.Rating);
        Assert.False(metadata.NeedsReview);

        // And no prompt, because the subject clause depends on a rating the pack never stated.
        Assert.Equal(string.Empty, metadata.Prompt);
    }

    [Fact]
    public void AnUprightDeclarationWithAFoldedTorsoIsFlagged()
    {
        var metadata = PoseMetadataAnalyzer.Classify(
            Folded(),
            Declared(PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.True(metadata.NeedsReview);
        Assert.Contains("torso is folded", metadata.ReviewNote, StringComparison.Ordinal);
    }

    [Fact]
    public void APoseWithNoVisibleShouldersIsNotMeasurableAndDecidesNothing()
    {
        var body = Upright(rightShoulderX: 60, leftShoulderX: 140).Body.ToArray();
        body[OpenPosePoseJson.RightShoulderIndex] = body[OpenPosePoseJson.RightShoulderIndex] with { Confidence = 0 };

        var observation = PoseMetadataAnalyzer.Measure(new PosePerson { Body = body });

        Assert.False(observation.IsMeasurable);
        Assert.Equal(PoseFacingDirection.Unknown, observation.MeasuredFacing);
    }

    [Fact]
    public void TheClassifiedMetadataCarriesTheComposedPrompt()
    {
        var metadata = PoseMetadataAnalyzer.Classify(
            Upright(rightShoulderX: 60, leftShoulderX: 140),
            Declared(PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Contains("a naked woman", metadata.Prompt, StringComparison.Ordinal);
        Assert.Contains("standing", metadata.Prompt, StringComparison.Ordinal);
        Assert.Contains("facing the camera", metadata.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasureRefusesAPoseThatDoesNotCarryEighteenBodyKeypoints()
    {
        var person = new PosePerson { Body = Enumerable.Range(0, 5).Select(i => new PoseKeypoint(i, i, 1)).ToArray() };

        var error = Assert.Throws<InvalidOperationException>(() => PoseMetadataAnalyzer.Measure(person));

        Assert.Contains("18 body keypoints", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An upright figure with a full COCO-18 body. The default shoulder positions are far enough apart to be
    /// decisive (|order| = 0.186 of the figure's height, above the 0.08 floor).
    /// </summary>
    private static PosePerson Upright(double rightShoulderX, double leftShoulderX) => new()
    {
        Body =
        [
            new PoseKeypoint(100, 80, 1),            // nose
            new PoseKeypoint(100, 100, 1),           // neck
            new PoseKeypoint(rightShoulderX, 110, 1),
            new PoseKeypoint(rightShoulderX - 5, 180, 1),
            new PoseKeypoint(rightShoulderX - 10, 250, 1),
            new PoseKeypoint(leftShoulderX, 110, 1),
            new PoseKeypoint(leftShoulderX + 5, 180, 1),
            new PoseKeypoint(leftShoulderX + 10, 250, 1),
            new PoseKeypoint(85, 300, 1),            // right hip
            new PoseKeypoint(85, 400, 1),
            new PoseKeypoint(85, 500, 1),            // right ankle
            new PoseKeypoint(115, 300, 1),           // left hip
            new PoseKeypoint(115, 400, 1),
            new PoseKeypoint(115, 500, 1),           // left ankle
            new PoseKeypoint(95, 70, 1),             // right eye
            new PoseKeypoint(105, 70, 1),            // left eye
            new PoseKeypoint(85, 75, 1),             // right ear
            new PoseKeypoint(115, 75, 1)             // left ear
        ]
    };

    /// <summary>
    /// The same figure with the torso folded out to the side, which is what an all-fours pose measures like: the
    /// shoulders stay wide apart but the shoulder line is no longer horizontal in the world.
    /// </summary>
    private static PosePerson Folded() => new()
    {
        Body =
        [
            new PoseKeypoint(100, 80, 1),
            new PoseKeypoint(100, 100, 1),
            new PoseKeypoint(60, 110, 1),
            new PoseKeypoint(40, 180, 1),
            new PoseKeypoint(20, 250, 1),
            new PoseKeypoint(140, 110, 1),
            new PoseKeypoint(160, 180, 1),
            new PoseKeypoint(180, 250, 1),
            new PoseKeypoint(360, 300, 1),
            new PoseKeypoint(400, 400, 1),
            new PoseKeypoint(440, 500, 1),
            new PoseKeypoint(390, 300, 1),
            new PoseKeypoint(430, 400, 1),
            new PoseKeypoint(470, 500, 1),
            new PoseKeypoint(95, 70, 1),
            new PoseKeypoint(105, 70, 1),
            new PoseKeypoint(85, 75, 1),
            new PoseKeypoint(115, 75, 1)
        ]
    };

    private static PoseMetadataDeclaration Declared(
        PoseStance stance, PoseFacingDirection direction, PoseCameraAngle camera, PoseContentRating rating) =>
        new(stance, direction, camera, rating);
}
