using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The pose library's test render can now condition on a CHARACTER (2026-09-27): the operator picks one, and the
/// character's approved face and build travel beside the skeleton, so a test result is what the app would really
/// render rather than a pose on a stranger.
/// </summary>
public sealed class PoseTestCharacterConditioningContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Service => Read("DreamGenClone.Web", "Application", "RolePlay", "PoseTestRenderService.cs");

    private static string Page => Read("DreamGenClone.Web", "Components", "Pages", "PoseLibraryPage.razor");

    /// <summary>
    /// Face, body, skeleton — the order the render path documents (the face anchors the person, the body the build, the
    /// skeleton goes LAST). A different order is a different placement, and placement is request data.
    /// </summary>
    [Fact]
    public void TheReferencesTravelInTheRenderPathsOrder()
    {
        var faceIndex = Service.IndexOf("approved identity face", StringComparison.Ordinal);
        var bodyIndex = Service.IndexOf("approved body build reference", StringComparison.Ordinal);
        var skeletonIndex = Service.IndexOf("pose reference (OpenPose skeleton)", StringComparison.Ordinal);

        Assert.True(faceIndex > 0 && bodyIndex > faceIndex && skeletonIndex > bodyIndex,
            "The references must be built face, then body, then skeleton.");
    }

    /// <summary>
    /// A ControlNet graph conditions the sampler and has no reference-image slot, so character conditioning there would
    /// be DROPPED. Refused by name instead — a render that looks conditioned and is not is the failure this page
    /// exists to make impossible.
    /// </summary>
    [Fact]
    public void CharacterConditioningIsRefusedOnTheControlNetRoute()
    {
        Assert.Contains("cannot be combined with the ControlNet pose route", Service, StringComparison.Ordinal);
        Assert.Contains("request.FaceReference is not null || request.BodyReference is not null", Service, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page resolves the character's references itself, from the APPROVED pack, at the angles THE POSE asks for and
    /// refuses a character who cannot serve them rather than testing with a substitute.
    ///
    /// Both the angle and the state come from the pose's metadata now (2026-09-30): 472 of the 579 poses in this
    /// library are NSFW, so a fixed clothed-front reference conditioned a naked pose on a clothed build. The refusal
    /// therefore has to NAME the angle and the state it wanted — that is what tells the operator which reference to
    /// shoot — which is why these assertions pin the interpolated source text rather than a bare phrase.
    /// </summary>
    [Fact]
    public void ThePageResolvesApprovedReferencesAndRefusesAnIncompleteCharacter()
    {
        Assert.Contains("ListPackOwnersAsync", Page, StringComparison.Ordinal);
        Assert.Contains("IdentityPackReferenceResolver.ResolveFace", Page, StringComparison.Ordinal);
        Assert.Contains("IdentityPackReferenceResolver.ResolveBody", Page, StringComparison.Ordinal);
        Assert.Contains("SceneImageReferenceBodyState.Clothed", Page, StringComparison.Ordinal);

        // The state is not a constant on the page: it comes from the pose's rating through the plan.
        Assert.Contains("plan.BodyState is not { } bodyState", Page, StringComparison.Ordinal);

        Assert.Contains(
            "has no APPROVED {PoseMetadataLabels.Direction(pose.Direction)} face",
            Page,
            StringComparison.Ordinal);
        Assert.Contains(
            "has no APPROVED {state} {PoseMetadataLabels.Direction(pose.Direction)} body",
            Page,
            StringComparison.Ordinal);

        // And the test refuses to run until those resolved, rather than running unconditioned.
        Assert.Contains("CharacterIsReady", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bytes come from the identity service, never from the filesystem here: a consumer outside the asset store has
    /// no file path to trust, and the storage layer stays behind the service that owns the asset.
    /// </summary>
    [Fact]
    public void ThePageReadsAssetBytesThroughTheIdentityService()
    {
        Assert.Contains("IdentityService.ReadAssetBytesAsync", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllBytes", Page, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (holding DreamGenClone.sln) was not found.");
    }
}
