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
    public void Resolve_TurnedLeft_RecommendsThreeQuarterLeft()
    {
        var recommendation = SceneViewRecommender.Resolve("Directed left, away from the others", "Turned to her left");

        Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterLeft, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ThreeQuarterLeft, recommendation.BodyView);
    }

    [Fact]
    public void Resolve_InProfileRight_RecommendsProfileRight()
    {
        var recommendation = SceneViewRecommender.Resolve("Directed right in profile", "Standing side-on");

        Assert.Equal(SceneImageReferenceFaceView.ProfileRight, recommendation.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ProfileRight, recommendation.BodyView);
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
