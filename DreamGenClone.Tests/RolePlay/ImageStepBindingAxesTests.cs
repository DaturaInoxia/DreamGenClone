using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The translation from a step's bound references to the axes a compiler reads. It decides which elements a compiled
/// prompt is allowed to describe, so a wrong answer here is a prompt that fights its own references.
///
/// It exists because the asset creator refused to compile at all when anything was bound ("That translation does not
/// exist yet") while that same panel is how the character's LoRA training images are made - face and build bound, then
/// Generate Prompt. Reported live 2026-09-30: "i used this feature to generate the Lora Images".
/// </summary>
public sealed class ImageStepBindingAxesTests
{
    private static ReferenceApplicationSelection Binding(
        ImageStepSlotKind slotKind,
        string strategy = ReferenceStrategyCatalogue.NativeMultiReference,
        string? packId = null,
        string? referenceAssetId = null,
        string? sceneAssetId = null,
        string? posePresetId = null,
        string? skeletonPath = null,
        int? ordinal = null) =>
        new()
        {
            Kind = slotKind.ToString(),
            ElementKey = ReferenceStrategyCatalogue.ElementKeyForSlot(slotKind),
            Strategy = strategy,
            IdentityPackId = packId,
            ReferenceAssetId = referenceAssetId,
            SceneAssetId = sceneAssetId,
            PosePresetId = posePresetId,
            SkeletonRelativePath = skeletonPath,
            Ordinal = ordinal
        };

    /// <summary>
    /// The reported case: a pack face and a pack build bound together. One entry per axis is the rule, and the axis
    /// vocabulary has no Body, so the two merge into Identity - which is also the truth about what they carry.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithAFaceAndABuild_DeclaresOneIdentityAxis()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            Binding(ImageStepSlotKind.Face, packId: "pack-becky", referenceAssetId: "face-3ql", ordinal: 1),
            Binding(ImageStepSlotKind.Body, packId: "pack-becky", referenceAssetId: "body-front", ordinal: 2)
        ]);

        var axis = Assert.Single(axes);
        Assert.Equal(ImageBindingAxis.Identity, axis.Axis);
        Assert.Equal(ImageBindingMode.Reference, axis.Mode);
        Assert.Equal("pack-becky", axis.Value);
    }

    /// <summary>Each remaining slot kind maps to the part of the frame it carries.</summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Wardrobe, ImageBindingAxis.Wardrobe)]
    [InlineData(ImageStepSlotKind.Location, ImageBindingAxis.Location)]
    [InlineData(ImageStepSlotKind.Pose, ImageBindingAxis.Pose)]
    [InlineData(ImageStepSlotKind.CharacterPose, ImageBindingAxis.Pose)]
    public void ToCellBindings_MapsEachSlotKindToItsAxis(ImageStepSlotKind slotKind, ImageBindingAxis expected)
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
            [Binding(slotKind, sceneAssetId: "asset-1")]);

        Assert.Equal(expected, Assert.Single(axes).Axis);
    }

    /// <summary>
    /// A text-only binding is reported as Text rather than dropped: "nothing is declared" and "this element is the
    /// prompt's job" are different facts, and the compiler says which one it was told.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithATextOnlyBinding_DeclaresTheAxisAsTextAndCarriesNoSource()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
            [Binding(ImageStepSlotKind.Wardrobe, ReferenceStrategyCatalogue.TextOnly)]);

        var axis = Assert.Single(axes);
        Assert.Equal(ImageBindingAxis.Wardrobe, axis.Axis);
        Assert.Equal(ImageBindingMode.Text, axis.Mode);
        Assert.Null(axis.Value);
        Assert.Null(axis.Strategy);
    }

    /// <summary>
    /// A pose skeleton through the ControlNet graph is an OpenPose map - the one adapter that can be named from the
    /// binding itself.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithAPoseControlNet_NamesTheOpenPoseAdapter()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            Binding(ImageStepSlotKind.Pose, ReferenceStrategyResolver.PoseControlNet,
                posePresetId: "standing-front", skeletonPath: "poses/standing-front.png")
        ]);

        var axis = Assert.Single(axes);
        Assert.Equal(ImageBindingAxis.Pose, axis.Axis);
        Assert.Equal(ImageBindingMode.Adapter, axis.Mode);
        Assert.Equal("OpenPose", axis.Strategy);
        Assert.Equal("standing-front", axis.Value);
    }

    /// <summary>
    /// A structural carrier absorbs a text-only sibling on the same axis: an axis a reference supplies must not also be
    /// described in words, so the merge cannot let the text binding switch it back.
    /// </summary>
    [Fact]
    public void ToCellBindings_WhenOneAxisIsBothBoundAndTextOnly_TheStructuralCarrierWins()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            Binding(ImageStepSlotKind.Face, ReferenceStrategyCatalogue.TextOnly, ordinal: 1),
            Binding(ImageStepSlotKind.Body, packId: "pack-becky", referenceAssetId: "body-front", ordinal: 2)
        ]);

        var axis = Assert.Single(axes);
        Assert.Equal(ImageBindingAxis.Identity, axis.Axis);
        Assert.Equal(ImageBindingMode.Reference, axis.Mode);
        Assert.Equal("pack-becky", axis.Value);
    }

    /// <summary>Frame order decides which entry survives a merge, so the step's own order is preserved.</summary>
    [Fact]
    public void ToCellBindings_OrdersTheAxesByTheStepsOwnOrdinals()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            Binding(ImageStepSlotKind.Location, sceneAssetId: "asset-loc", ordinal: 1),
            Binding(ImageStepSlotKind.Face, packId: "pack-becky", referenceAssetId: "face", ordinal: 2)
        ]);

        Assert.Equal([ImageBindingAxis.Location, ImageBindingAxis.Identity], axes.Select(axis => axis.Axis));
    }

    /// <summary>
    /// An element key with no slot kind (a binding persisted before slots existed) still translates: the keys are the
    /// same names the planner writes.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithALegacyElementKeyOnly_StillResolvesTheAxis()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            new ReferenceApplicationSelection
            {
                ElementKey = "Identity",
                Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
                SceneAssetId = "asset-face"
            }
        ]);

        var axis = Assert.Single(axes);
        Assert.Equal(ImageBindingAxis.Identity, axis.Axis);
        Assert.Equal("asset-face", axis.Value);
    }

    /// <summary>Nothing bound is nothing declared - the compiler then describes only what the direction states.</summary>
    [Fact]
    public void ToCellBindings_WithNothingBound_DeclaresNothing()
    {
        Assert.Empty(ImageStepBindingAxes.ToCellBindings([]));
    }

    /// <summary>
    /// A LoRA strategy is refused by name rather than declared: a trained artifact is not an image, and the chain the
    /// render applies comes from the LoRA selection that rides with the request.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithALoraStrategy_RefusesNamingWhyItCannotBeDeclared()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBindingAxes.ToCellBindings(
                [Binding(ImageStepSlotKind.Face, ReferenceStrategyCatalogue.Lora, packId: "pack-becky")]));

        Assert.Contains("trained artifact", exception.Message, StringComparison.Ordinal);
        Assert.Contains("LoRA selection", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>An unknown strategy is refused by name - never silently declared as something else.</summary>
    [Fact]
    public void ToCellBindings_WithAnUnknownStrategy_RefusesNamingIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBindingAxes.ToCellBindings(
                [Binding(ImageStepSlotKind.Face, "PhotorealMagic", packId: "pack-becky")]));

        Assert.Contains("PhotorealMagic", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A structural binding that names no source is refused: the prompt would be told to leave out an element that
    /// nothing supplies, so the image would come back missing the face the operator bound.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithAStructuralBindingThatNamesNothing_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBindingAxes.ToCellBindings([Binding(ImageStepSlotKind.Face)]));

        Assert.Contains("names nothing", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ControlNet binding on a slot that is not a pose cannot name its map, and a guessed adapter is a graph the
    /// operator did not choose.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithAControlNetLocation_RefusesRatherThanGuessingTheAdapter()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBindingAxes.ToCellBindings(
                [Binding(ImageStepSlotKind.Location, ReferenceStrategyCatalogue.ControlNet, sceneAssetId: "asset-loc")]));

        Assert.Contains("does not say which control map", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every declaration this translation produces must be VALID under the one definition of a valid declaration, so
    /// nothing here can reach a render with an axis set the render's own rules reject.
    /// </summary>
    [Fact]
    public void ToCellBindings_WithEverySlotKindBoundAtOnce_ProducesADeclarationTheValidatorAccepts()
    {
        var axes = ImageStepBindingAxes.ToCellBindings(
        [
            Binding(ImageStepSlotKind.Face, packId: "pack-becky", referenceAssetId: "face", ordinal: 1),
            Binding(ImageStepSlotKind.Body, packId: "pack-becky", referenceAssetId: "body", ordinal: 2),
            Binding(ImageStepSlotKind.Wardrobe, sceneAssetId: "asset-dress", ordinal: 3),
            Binding(ImageStepSlotKind.Location, sceneAssetId: "asset-loc", ordinal: 4),
            Binding(ImageStepSlotKind.Pose, posePresetId: "standing", skeletonPath: "poses/standing.png", ordinal: 5)
        ]);

        ImageCellBindings.Validate(axes);
        Assert.Equal(
            [
                ImageBindingAxis.Identity,
                ImageBindingAxis.Wardrobe,
                ImageBindingAxis.Location,
                ImageBindingAxis.Pose
            ],
            axes.Select(axis => axis.Axis));
    }
}
