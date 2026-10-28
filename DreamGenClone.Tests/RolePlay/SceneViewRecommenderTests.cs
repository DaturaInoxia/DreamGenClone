using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-166 D8 — which reference view a moment shows. The recommendation is GUIDANCE derived from recorded facts; with
/// no recorded angle it returns nothing rather than guessing, because a wrong suggestion is worse than none.
/// </summary>
public sealed class SceneViewRecommenderTests
{
    [Fact]
    public void Resolve_FacingTheCamera_RecommendsFront()
    {
        var recommendation = SceneViewRecommender.Resolve("Directed forward toward the camera", "Standing upright");

        Assert.Equal(SceneImageReferenceFaceView.Front, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.Front, recommendation.BodyView);
    }

    [Fact]
    public void Resolve_GazeLeft_RecommendsThreeQuarterLeft()
    {
        // The sightline states the FACING direction: "left" = nose to the LEFT of the frame = LEFT side shown.
        var recommendation = SceneViewRecommender.Resolve("Directed left, away from the others", "Standing upright");

        Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterLeft, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ThreeQuarterLeft, recommendation.BodyView);
    }

    [Fact]
    public void Resolve_GazeRightInProfile_RecommendsProfileRight()
    {
        // The sightline states the FACING direction: "right" = nose to the RIGHT of the frame = RIGHT side shown.
        var recommendation = SceneViewRecommender.Resolve("Directed right in profile", "Standing side-on");

        Assert.Equal(SceneImageReferenceFaceView.ProfileRight, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ProfileRight, recommendation.BodyView);
    }

    [Fact]
    public void Resolve_PlacedLeftOfFrame_RecommendsThreeQuarterRight()
    {
        // The position states PLACEMENT: a character on the LEFT is turned toward the centre, so the camera sees their
        // RIGHT side — the opposite of the word.
        var recommendation = SceneViewRecommender.Resolve("Watching Dean", "Standing to the left of Dean");

        Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterRight, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ThreeQuarterRight, recommendation.BodyView);
    }

    [Fact]
    public void Resolve_PlacedRightOfFrame_RecommendsThreeQuarterLeft()
    {
        var recommendation = SceneViewRecommender.Resolve("Watching Dean", "Standing to the right of Dean");

        Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterLeft, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ThreeQuarterLeft, recommendation.BodyView);
    }

    /// <summary>A back view shows NO face, so recommending a face view would tell the operator to bind an image the
    /// scene cannot use. The face stays null and the basis says why.</summary>
    [Fact]
    public void Resolve_FacingAway_RecommendsTheBackBodyAndNoFace()
    {
        var recommendation = SceneViewRecommender.Resolve("Away from the camera", "Standing with her back to the camera");

        Assert.Null(recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.Back, recommendation.BodyView);
        Assert.Contains("away from the camera", recommendation.Basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WithNoRecordedAngle_ReturnsNothingRatherThanAGuess()
    {
        var recommendation = SceneViewRecommender.Resolve(null, null);

        Assert.True(recommendation.IsEmpty);
        Assert.Contains("no angle is recorded", recommendation.Basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WithAnAngleFreeSentence_ReturnsNothing()
    {
        var recommendation = SceneViewRecommender.Resolve("Watching the fire", "Sitting quietly");

        Assert.True(recommendation.IsEmpty);
    }
}
