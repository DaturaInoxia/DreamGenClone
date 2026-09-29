using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The authoring panel must turn a FACE pose with the SAME controls it turns a body pose with.
///
/// The defect this pins, reported by the operator on 2026-09-28 as "body poses move, face poses do nothing": the Body
/// step's arrows wrote the rig's VIEW, while <see cref="PoseFaceProxy.Project"/> reads the HEAD and takes no view at
/// all. So on exactly the poses the head path exists for, every control in the step the panel OPENS ON did nothing —
/// and did it silently, because the readout printed the view and got a permanent 0°.
///
/// WHY THIS IS A SOURCE TEST. The geometry behind the routing is pinned behaviourally in
/// <see cref="PoseFaceProxyTests"/> (a named target must actually change the face). What no behavioural test can see is
/// whether the PANEL reaches for that geometry, because the file is markup plus event wiring. This repo already guards
/// its markup this way — see <see cref="PoseTestCharacterConditioningContractTests"/> — and the regression here is a
/// wiring regression, so the wiring is what is asserted.
/// </summary>
public sealed class PoseAuthorPanelHeadRoutingContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Panel => File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "PoseAuthorPanel.razor"));

    /// <summary>
    /// ONE place decides that the head is what turns, and it asks the head path's own test. Asking anything else — or
    /// asking it in two places — is how the two answers drift apart.
    /// </summary>
    [Fact]
    public void ThePanelDecidesTheHeadPathWithTheProxysOwnTest()
    {
        Assert.Contains("private bool TurningTheHead", Panel, StringComparison.Ordinal);
        Assert.Contains("PoseFaceProxy.IsHeadPose(pose)", Panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 5° press reaches the HEAD on a face pose. This is the exact line the operator's complaint is about: without it
    /// the arrows step the rig's view, which the face proxy never reads, so the picture cannot move.
    /// </summary>
    [Fact]
    public void APressOnTheArrows_StepsTheHead_OnAFacePose()
    {
        Assert.Contains(
            "PoseHeadRotation.Step(_head, axis, steps, StudioOptions.Value)", Panel, StringComparison.Ordinal);

        // And the named targets land there too, so all five work rather than a partial set that looks like a fluke.
        Assert.Contains("PoseFaceProxy.AsHeadRotation(view)", Panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The readout names the angle that actually CHANGED. Reading the view on a face pose printed a permanent 0° while
    /// the head turned underneath it, which makes a working control read as a dead one.
    /// </summary>
    [Fact]
    public void TheReadoutNamesTheAngleThatTurned()
    {
        Assert.Contains("@TurnYaw.ToString", Panel, StringComparison.Ordinal);

        // The view must no longer be printed directly: the accessors are the only readers of it.
        Assert.DoesNotContain("@_view.YawDegrees.ToString", Panel, StringComparison.Ordinal);
        Assert.DoesNotContain("@_view.PitchDegrees.ToString", Panel, StringComparison.Ordinal);
        Assert.DoesNotContain("@_view.RollDegrees.ToString", Panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Head step is handed the loaded FACE, so both steps show the pose being edited — rather than one of them
    /// showing a mannequin the operator never loaded while their own face turned in the other.
    /// </summary>
    [Fact]
    public void TheHeadStepIsGivenTheFace_NotJustTheRig()
    {
        Assert.Contains("Face=\"@(TurningTheHead ? _storedPose : null)\"", Panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Saving a LOADED pose carries its own keypoints. The gate used to ask for rig rotations, and a face pose has
    /// none, so saving one re-projected the rig and wrote a standing MANNEQUIN in place of the face just turned.
    /// </summary>
    [Fact]
    public void SavingALoadedPoseCarriesItsOwnKeypoints()
    {
        Assert.Contains(
            "_storedPose is null && _draggedPose is null ? null : PoseOnScreen()", Panel, StringComparison.Ordinal);

        // Named, so the keywords and the provenance record what made it instead of defaulting to "dragged".
        Assert.Contains("Origin: TurningTheHead ? \"head-turned\" : null", Panel, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
