using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The character-pose slot (B-130 §D8/§019): an approved pose asset is a reference IMAGE, so it travels through the
/// same ordered binding channel as every other element - not a second mechanism. These tests hold the whole chain,
/// because each link was already capable of the slot and nothing proved they agreed:
///
/// <list type="number">
/// <item><description>a blueprint may declare a CharacterPose slot whose source is an approved pose ASSET;</description></item>
/// <item><description>the planner accepts that pairing and emits one ordered binding;</description></item>
/// <item><description>the prompt plan treats the pose as ONE image that already carries appearance, clothing and
/// position - which is why filling the slot removes all three from the prompt rather than leaving two of them to be
/// described twice.</description></item>
/// </list>
/// </summary>
public sealed class CharacterPoseSlotTests
{
    private static readonly ImageStepActor Becky = new("becky", "Becky");

    private static ImageStepBlueprint PoseBlueprint()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.AssetCreate,
            "Render this pose",
            ImageStepSourceMode.None,
            [
                new ImageStepSlotBlueprint(
                    ImageStepSlotKind.CharacterPose,
                    ImageStepSlotPrefill.None,
                    [ImageStepReferenceSourceKind.CharacterPoseAsset],
                    Becky.ActorKey,
                    Required: true)
            ],
            ImageStepPersistenceKind.SceneAsset);

        blueprint.Validate();
        return blueprint;
    }

    private static ImageStepSlotAssignment PoseAssignment() =>
        new(ImageStepSlotKind.CharacterPose, Becky.ActorKey,
            new ImageStepSlotSource(
                ImageStepReferenceSourceKind.CharacterPoseAsset,
                ReferenceStrategyResolver.IdentityNativeMultiReference,
                SceneAssetId: "pose-asset-1",
                SceneAssetImageId: "pose-image-1",
                SceneAssetVersion: 3,
                SceneAssetSha256: new string('A', 64)));

    [Fact]
    public void Planner_AcceptsAnApprovedPoseAssetForAPoseSlot_AndOrdersIt()
    {
        var bindings = ReferenceSlotPlanner.Plan(PoseBlueprint(), [PoseAssignment()], maxReferences: 4);

        var binding = Assert.Single(bindings);
        Assert.Equal(nameof(ImageStepSlotKind.CharacterPose), binding.Kind);
        Assert.Equal(ImageStepReferenceSourceKind.CharacterPoseAsset.ToString(), binding.Source);
        Assert.Equal("CharacterPose", binding.ElementKey);
        Assert.Equal("character pose", binding.SemanticRole);
        Assert.Equal(Becky.ActorKey, binding.ActorKey);
        Assert.Equal("pose-asset-1", binding.SceneAssetId);
        Assert.Equal("pose-image-1", binding.SceneAssetImageId);
        Assert.Equal(1, binding.Ordinal ?? 0);
        Assert.True(binding.SuppliesImage);
    }

    /// <summary>
    /// A pose slot that is filled supplies appearance, clothing AND position, because one pose image carries all three.
    /// Leaving any of them in the prompt would describe the same element twice.
    /// </summary>
    [Fact]
    public void PromptPlan_RemovesAppearanceClothingAndPosition_ForAFilledPoseSlot()
    {
        var bindings = ReferenceSlotPlanner.Plan(PoseBlueprint(), [PoseAssignment()], maxReferences: 4);

        var scopes = StepPromptElementPlan.Build(
            bindings.Select(binding => (Enum.Parse<ImageStepSlotKind>(binding.Kind!, ignoreCase: true), binding.ActorKey)));

        Assert.Contains("character:becky.appearance", scopes);
        Assert.Contains("character:becky.clothing", scopes);
        Assert.Contains("character:becky.position", scopes);
    }

    /// <summary>
    /// A pose slot refuses a source the blueprint did not declare. The source set is the host's declaration of what a
    /// slot may bind, so accepting a scratch image here would mean the blueprint is not the contract it claims to be.
    /// </summary>
    [Fact]
    public void Planner_RefusesASourceTheSlotDoesNotAllow()
    {
        var wrong = new ImageStepSlotAssignment(
            ImageStepSlotKind.CharacterPose, Becky.ActorKey,
            new ImageStepSlotSource(
                ImageStepReferenceSourceKind.ScratchImage,
                ReferenceStrategyResolver.IdentityNativeMultiReference,
                SceneAssetId: "scratch-asset",
                SceneAssetImageId: "scratch-image"));

        Assert.Throws<InvalidOperationException>(
            () => ReferenceSlotPlanner.Plan(PoseBlueprint(), [wrong], maxReferences: 4));
    }

    /// <summary>
    /// A pose slot must name its character: the pose is worn by someone, so an unaddressed one writes the scope
    /// <c>character:.position</c>, which addresses nobody.
    /// </summary>
    [Fact]
    public void Blueprint_RefusesAPoseSlotThatNamesNobody()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.AssetCreate,
            "Render this pose",
            ImageStepSourceMode.None,
            [
                new ImageStepSlotBlueprint(
                    ImageStepSlotKind.CharacterPose,
                    ImageStepSlotPrefill.None,
                    [ImageStepReferenceSourceKind.CharacterPoseAsset])
            ],
            ImageStepPersistenceKind.SceneAsset);

        Assert.Throws<InvalidOperationException>(blueprint.Validate);
    }

    /// <summary>
    /// The slot's approved-asset picker asks for the pose ASSET TYPE, which is what makes the picker offer approved pose
    /// assets instead of (say) faces. Asserted on the composer's own mapping so the two cannot drift.
    /// </summary>
    [Fact]
    public void Composer_MapsThePoseSlotToThePoseAssetType()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor"));

        Assert.Contains("ImageStepSlotKind.CharacterPose => SceneAssetType.CharacterPose", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln"))
                && File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the DreamGenClone repository root from '{AppContext.BaseDirectory}'.");
    }
}
