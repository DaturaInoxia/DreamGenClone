using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The joiner between a BOUND pose slot and the render's pose option (2026-09-27).
///
/// It exists because the render path could already carry a pose skeleton beside an identity face and a body build —
/// <c>SceneAssetGenerationJobHandler.RenderIdentityConditionedAsync</c> adds it as one more native reference — while NO
/// code in the app ever set the option. A bound pose was therefore dropped and every Pose control was decorative, on
/// every host. The capability was never missing; this translation was.
/// </summary>
public sealed class ImageStepPoseBindingTests
{
    private static ReferenceApplicationSelection PoseBinding(string? presetId, string? skeletonPath = "/poses/a.png") => new()
    {
        Kind = ImageStepSlotKind.Pose.ToString(),
        Source = ImageStepReferenceSourceKind.PoseLibrarySkeleton.ToString(),
        SkeletonRelativePath = skeletonPath,
        PosePresetId = presetId
    };

    [Fact]
    public void ABoundPose_BecomesThePresetsIdAndItsArtifact()
    {
        var pose = ImageStepPoseBinding.Resolve([PoseBinding("preset-42")]);

        Assert.NotNull(pose);
        Assert.Equal("preset-42", pose!.PresetId);
        Assert.Equal("/poses/a.png", pose.SkeletonRelativePath);
    }

    /// <summary>
    /// The skeleton is read BY PRESET ID at render time, so a binding that names only a path cannot be honoured. It is
    /// refused loudly rather than dropped: a dropped pose is a render that looks successful and contains no pose, which
    /// is the exact failure this class was written to remove.
    /// </summary>
    [Fact]
    public void APoseBindingWithNoPresetId_IsRefusedByName()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageStepPoseBinding.Resolve([PoseBinding(presetId: null)]));

        Assert.Contains("names no pose library preset", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Face, body and wardrobe bindings name no pose, so a step with them renders as it always did.</summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Face)]
    [InlineData(ImageStepSlotKind.Body)]
    [InlineData(ImageStepSlotKind.Wardrobe)]
    public void ABindingThatIsNotAPose_YieldsNoPose(ImageStepSlotKind slotKind)
    {
        var binding = new ReferenceApplicationSelection
        {
            Kind = slotKind.ToString(),
            Source = ImageStepReferenceSourceKind.IdentityPackAsset.ToString(),
            IdentityPackId = "pack-1",
            ReferenceAssetId = "asset-1"
        };

        Assert.Null(ImageStepPoseBinding.Resolve([binding]));
    }

    [Fact]
    public void NoBindings_YieldNoPose() => Assert.Null(ImageStepPoseBinding.Resolve([]));

    /// <summary>
    /// A pose slot bound to an EMPTY selection supplies no image, so it is not a pose: the operator clearing the slot
    /// must leave the render with no pose rather than sending one whose skeleton cannot be read.
    /// </summary>
    [Fact]
    public void APoseSlotWithNothingBoundSuppliesNoImage()
    {
        Assert.Null(ImageStepPoseBinding.Resolve(
            [new ReferenceApplicationSelection { Kind = ImageStepSlotKind.Pose.ToString() }]));
    }
}
