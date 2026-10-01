using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The two things a pose's metadata produces: the prompt that describes it and the reference images it needs.
///
/// Both are pinned because both are read by a render: the wording carries the pose's stance, direction and camera
/// (the three facts a skeleton cannot state), and the plan decides WHICH of a character's approved references is
/// loaded. A silent change to either is a different render, not a different sentence.
/// </summary>
public sealed class PoseMetadataPromptTests
{
    [Fact]
    public void AnNsfwStandingFrontPoseGetsTheNakedSubjectAndTheFacing()
    {
        var prompt = PoseMetadataPrompt.Compose(Metadata(
            PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Equal(
            "A full-body photograph of a naked woman standing facing the camera, natural skin texture, "
            + "photorealistic, plain studio background, 85mm.",
            prompt);
    }

    [Fact]
    public void ALyingPoseStatesWhichWayUpItLiesAndWhereTheCameraIs()
    {
        // The two facts that fix the recorded failure where a lying pose was rendered standing: the skeleton is
        // ambiguous between the two, so the words have to say it.
        var prompt = PoseMetadataPrompt.Compose(Metadata(
            PoseStance.Lying, PoseFacingDirection.Back, PoseCameraAngle.FromAbove, PoseContentRating.Nsfw));

        Assert.Contains("lying face down", prompt, StringComparison.Ordinal);
        Assert.Contains("seen from behind", prompt, StringComparison.Ordinal);
        Assert.Contains("viewed from above", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ASfwPoseAnchorsClothingInsteadOfNudity()
    {
        var prompt = PoseMetadataPrompt.Compose(Metadata(
            PoseStance.Kneeling, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Sfw));

        Assert.Contains("a woman, fully clothed", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("naked", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnratedPoseGetsNoPromptAtAll()
    {
        // The subject clause depends on a rating. Choosing one would be the app deciding what a pack contains.
        var prompt = PoseMetadataPrompt.Compose(Metadata(
            PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Unrated));

        Assert.Equal(string.Empty, prompt);
    }

    [Fact]
    public void AnUndeclaredStanceOrCameraAddsNoClauseInsteadOfAGuess()
    {
        var prompt = PoseMetadataPrompt.Compose(Metadata(
            PoseStance.Unknown, PoseFacingDirection.Front, PoseCameraAngle.Unknown, PoseContentRating.Nsfw));

        Assert.Equal(
            "A full-body photograph of a naked woman facing the camera, natural skin texture, photorealistic, "
            + "plain studio background, 85mm.",
            prompt);
    }

    [Theory]
    [InlineData(PoseFacingDirection.Front, SceneImageReferenceFaceView.Front, SceneImageReferenceBodyView.Front)]
    [InlineData(PoseFacingDirection.ThreeQuarterLeft, SceneImageReferenceFaceView.ThreeQuarterLeft, SceneImageReferenceBodyView.ThreeQuarterLeft)]
    [InlineData(PoseFacingDirection.ThreeQuarterRight, SceneImageReferenceFaceView.ThreeQuarterRight, SceneImageReferenceBodyView.ThreeQuarterRight)]
    [InlineData(PoseFacingDirection.ProfileLeft, SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceBodyView.ProfileLeft)]
    [InlineData(PoseFacingDirection.ProfileRight, SceneImageReferenceFaceView.ProfileRight, SceneImageReferenceBodyView.ProfileRight)]
    public void TheDirectionDecidesBothReferenceAngles(
        PoseFacingDirection direction, SceneImageReferenceFaceView face, SceneImageReferenceBodyView body)
    {
        var plan = PoseMetadataPrompt.ReferencePlan(
            Metadata(PoseStance.Standing, direction, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Equal(face, plan.FaceView);
        Assert.Equal(body, plan.BodyView);
        Assert.Equal(SceneImageReferenceBodyState.Unclothed, plan.BodyState);
    }

    [Fact]
    public void ABackFacingPoseNeedsABackBodyAndNoFace()
    {
        // Not "unknown": the pose genuinely shows no face, so a face reference would put a face on a pose that has
        // none. The body angle is a real answer, which is why the two nulls mean different things.
        var plan = PoseMetadataPrompt.ReferencePlan(
            Metadata(PoseStance.Standing, PoseFacingDirection.Back, PoseCameraAngle.EyeLevel, PoseContentRating.Sfw));

        Assert.Null(plan.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.Back, plan.BodyView);
        Assert.Equal(SceneImageReferenceBodyState.Clothed, plan.BodyState);
        Assert.Contains("no face reference", plan.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUndeclaredDirectionResolvesNothingAndSaysWhatToFix()
    {
        var plan = PoseMetadataPrompt.ReferencePlan(
            Metadata(PoseStance.Standing, PoseFacingDirection.Unknown, PoseCameraAngle.EyeLevel, PoseContentRating.Nsfw));

        Assert.Null(plan.FaceView);
        Assert.Null(plan.BodyView);
        Assert.Null(plan.BodyState);
        Assert.Contains("pack.json", plan.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnratedPoseWithAKnownDirectionStillHasNoBodyState()
    {
        var plan = PoseMetadataPrompt.ReferencePlan(
            Metadata(PoseStance.Standing, PoseFacingDirection.Front, PoseCameraAngle.EyeLevel, PoseContentRating.Unrated));

        Assert.Equal(SceneImageReferenceFaceView.Front, plan.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.Front, plan.BodyView);
        Assert.Null(plan.BodyState);
        Assert.Contains("rating", plan.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanForAStoredPresetReadsThePresetsOwnColumns()
    {
        var preset = new PosePreset
        {
            Id = "p",
            Name = "n",
            Category = "c",
            LibraryId = "l",
            Stance = PoseStance.Kneeling,
            Direction = PoseFacingDirection.ProfileLeft,
            CameraAngle = PoseCameraAngle.FromAbove,
            ContentRating = PoseContentRating.Nsfw,
            MetadataPrompt = "stored prompt"
        };

        var plan = PoseMetadataPrompt.ReferencePlan(preset);

        Assert.Equal(SceneImageReferenceFaceView.ProfileLeft, plan.FaceView);
        Assert.Equal(SceneImageReferenceBodyView.ProfileLeft, plan.BodyView);
        Assert.Equal(SceneImageReferenceBodyState.Unclothed, plan.BodyState);
    }

    private static PoseMetadata Metadata(
        PoseStance stance, PoseFacingDirection direction, PoseCameraAngle camera, PoseContentRating rating) =>
        new(stance, direction, camera, rating, Prompt: string.Empty);
}
