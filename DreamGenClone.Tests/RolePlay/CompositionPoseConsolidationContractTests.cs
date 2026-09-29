using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// One pose per Composition, chosen in one place (2026-09-28).
///
/// The composition page used to own a bespoke "Pose reference" section with its own copy of the pose library picker,
/// while every other host got its pose from the shared step's Pose tab. Two ways to choose one pose is how the two
/// drift apart, so the picker moved to the step - the slot below is what makes the tab appear - and the page now reads
/// the pose the binding names.
///
/// Two things deliberately stayed on the page, because the composer has no concept of either: an uploaded skeleton
/// (the step's Pose tab offers the pose LIBRARY, and a DWPose extract is not in it) and the ControlNet strength (a
/// scalar of the sampler route, while a pose carried as a reference image has no strength at all).
/// </summary>
public sealed class CompositionPoseConsolidationContractTests
{
    private static string Page => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));

    /// <summary>
    /// Frame-wide, optional, library-sourced: a pose belongs to the frame rather than to one character, a Composition
    /// renders unposed until one is bound, and what the render conditions on is a pose library skeleton.
    /// </summary>
    [Fact]
    public void TheCompositionStepDeclaresAFrameWidePoseSlot()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);

        var pose = Assert.Single(blueprint.Slots.Where(slot => slot.SlotKind == ImageStepSlotKind.Pose));

        Assert.Null(pose.ActorKey);
        Assert.False(pose.Required);
        Assert.Contains(ImageStepReferenceSourceKind.PoseLibrarySkeleton, pose.AllowedSources);
    }

    /// <summary>The page must not keep a second picker once the step offers the tab.</summary>
    [Fact]
    public void ThePageNoLongerOffersASecondPosePicker()
    {
        Assert.DoesNotContain("<PoseLibraryPicker", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenPosePicker", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose from library", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("_posePickerOpen", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pose comes from the step's binding, and is read BY PRESET ID rather than from the path the binding also
    /// carries, so a stale path cannot make the render read a file other than the pose that was picked.
    /// </summary>
    [Fact]
    public void ThePageTakesItsPoseFromTheStepBinding()
    {
        Assert.Contains("ImageStepPoseBinding.Resolve(bindings)", Page, StringComparison.Ordinal);
        Assert.Contains("PoseCatalog.ReadSkeletonAsync(selected.PresetId)", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The render destination is unchanged - one stored skeleton, whether it came from the library or an upload - so
    /// the consolidation cannot have introduced a second conditioning path.
    /// </summary>
    [Fact]
    public void ThePoseStillTravelsAsOneStoredSkeletonToTheRender()
    {
        Assert.Contains("renderSettings.PoseReference = new SceneImagePoseReference", Page, StringComparison.Ordinal);
        Assert.Contains("StoragePath = _poseStoragePath", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A render takes one pose, so the two producers release each other rather than one silently winning: an upload
    /// clears the thread's pose binding, and unbinding the slot detaches the attached pose.
    /// </summary>
    [Fact]
    public void TheUploadAndTheBindingReleaseEachOther()
    {
        Assert.Contains("_posePresetId = null;", Page, StringComparison.Ordinal);
        Assert.Contains("nameof(ImageStepSlotKind.Pose)", Page, StringComparison.Ordinal);
        Assert.Contains("DetachPose();", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ControlNet strength keeps its control. Removing the bespoke section without keeping it would have left the
    /// strength as a code-only default - a hidden value the operator could not see or change.
    /// </summary>
    [Fact]
    public void TheControlNetStrengthStaysAUiBackedControl()
    {
        Assert.Contains("@bind=\"_poseStrength\"", Page, StringComparison.Ordinal);
        Assert.Contains("_poseStrength.ToString(", Page, StringComparison.Ordinal);
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
