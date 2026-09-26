using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The planner is where a blueprint becomes the ordered bindings a render actually consumes, so its rules are what
/// stand between "the operator asked for a pose" and "the render quietly did something else".
/// </summary>
public sealed class ReferenceSlotPlannerTests
{
    private static ImageStepBlueprint Blueprint(params ImageStepSlotBlueprint[] slots) =>
        new(ImageStepKind.Compose, "Step", ImageStepSourceMode.None, slots, ImageStepPersistenceKind.SceneImage);

    private static ImageStepSlotSource Source(
        ImageStepReferenceSourceKind kind = ImageStepReferenceSourceKind.ApprovedSceneAsset,
        string strategy = "NativeMultiReference") =>
        new(kind, strategy, SceneAssetId: "asset-1", SceneAssetImageId: "image-1", SceneAssetVersion: 1, SceneAssetSha256: "SHA");

    private static ImageStepSlotBlueprint FaceSlot(string actorKey = "p-becky", bool required = false) =>
        new(ImageStepSlotKind.Face, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.ApprovedSceneAsset], actorKey, required);

    private static ImageStepSlotBlueprint LocationSlot(bool required = false) =>
        new(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.ApprovedSceneAsset], null, required);

    private static ImageStepSlotBlueprint PoseSlot() =>
        new(ImageStepSlotKind.Pose, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.PoseLibrarySkeleton], null);

    [Fact]
    public void Plan_OrdersBindingsByTheBlueprintsSlotOrder()
    {
        var blueprint = Blueprint(LocationSlot(), FaceSlot());
        var bindings = ReferenceSlotPlanner.Plan(
            blueprint,
            [
                // Deliberately supplied face-first: the STEP's declared order is what decides placement, because a
                // placement that changed with the caller's list order would be invisible in the UI.
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, Source())
            ],
            maxReferences: 10);

        Assert.Equal(2, bindings.Count);
        Assert.Equal("Location", bindings[0].ElementKey);
        Assert.Equal(1, bindings[0].Ordinal);
        Assert.Null(bindings[0].ActorKey);
        Assert.Equal("Identity", bindings[1].ElementKey);
        Assert.Equal(2, bindings[1].Ordinal);
        Assert.Equal("p-becky", bindings[1].ActorKey);
    }

    [Fact]
    public void Plan_RecordsKindSourceAndStrategyOnEachBinding()
    {
        var bindings = ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot()),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source())],
            maxReferences: 10);

        var binding = Assert.Single(bindings);
        Assert.Equal("Face", binding.Kind);
        Assert.Equal("ApprovedSceneAsset", binding.Source);
        Assert.Equal("NativeMultiReference", binding.Strategy);
        Assert.Equal("character identity", binding.SemanticRole);
        Assert.True(binding.UsesReference);
    }

    [Fact]
    public void Plan_AssignmentForAnUndeclaredSlot_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot()),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, Source())],
            maxReferences: 10));

        Assert.Contains("declares no", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AssignmentForTheWrongActor_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot("p-becky")),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-dean", Source())],
            maxReferences: 10));

        Assert.Contains("p-dean", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host declares which sources a slot accepts, so binding one it did not offer is refused. This is what stops
    /// a skeleton being bound to a slot that meant a photoreal wardrobe reference.
    /// </summary>
    [Fact]
    public void Plan_SourceNotAllowedByTheSlot_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(PoseSlot()),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Pose, null, Source(ImageStepReferenceSourceKind.ApprovedSceneAsset))],
            maxReferences: 10));

        Assert.Contains("does not accept", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RequiredSlotLeftEmpty_ThrowsNamingIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot(required: true), LocationSlot()),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, Source())],
            maxReferences: 10));

        Assert.Contains("required reference slot", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Face", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_OneSlotFilledTwice_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot()),
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source())
            ],
            maxReferences: 10));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_MissingStrategy_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot()),
            [new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source(strategy: "  "))],
            maxReferences: 10));

        Assert.Contains("no strategy", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measured ceiling: with a skeleton bound, more than six references stop being honoured. The message must
    /// name the reason, because "too many references" without it reads as an arbitrary limit.
    /// </summary>
    [Fact]
    public void Plan_PoseCarryingStepAboveTheMeasuredCeiling_ThrowsCitingTheReason()
    {
        var slots = new List<ImageStepSlotBlueprint> { PoseSlot() };
        var assignments = new List<ImageStepSlotAssignment>
        {
            new(ImageStepSlotKind.Pose, null, Source(ImageStepReferenceSourceKind.PoseLibrarySkeleton))
        };
        for (var index = 0; index < 6; index++)
        {
            slots.Add(FaceSlot($"p-{index}"));
            assignments.Add(new ImageStepSlotAssignment(ImageStepSlotKind.Face, $"p-{index}", Source()));
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceSlotPlanner.Plan(Blueprint([.. slots]), assignments, maxReferences: 10));

        Assert.Contains("CASE-22", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pose", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_SevenReferencesWithoutAPose_IsAllowedWhenTheModelAcceptsThem()
    {
        var slots = new List<ImageStepSlotBlueprint>();
        var assignments = new List<ImageStepSlotAssignment>();
        for (var index = 0; index < 7; index++)
        {
            slots.Add(FaceSlot($"p-{index}"));
            assignments.Add(new ImageStepSlotAssignment(ImageStepSlotKind.Face, $"p-{index}", Source()));
        }

        var bindings = ReferenceSlotPlanner.Plan(Blueprint([.. slots]), assignments, maxReferences: 10);

        Assert.Equal(7, bindings.Count);
    }

    [Fact]
    public void EffectiveBudget_WithAPoseSlot_IsCappedAtTheMeasuredCeiling()
    {
        Assert.Equal(6, ReferenceSlotPlanner.EffectiveBudget(10, [ImageStepSlotKind.Face, ImageStepSlotKind.Pose]));
    }

    [Fact]
    public void EffectiveBudget_WithoutAPoseSlot_UsesTheModelsOwnLimit()
    {
        Assert.Equal(10, ReferenceSlotPlanner.EffectiveBudget(10, [ImageStepSlotKind.Face, ImageStepSlotKind.Location]));
    }

    [Fact]
    public void EffectiveBudget_NeverExceedsTheModelsOwnLimit()
    {
        // A model that only accepts 4 must not be handed 6 just because the step carries a pose.
        Assert.Equal(4, ReferenceSlotPlanner.EffectiveBudget(4, [ImageStepSlotKind.Pose]));
    }

    [Fact]
    public void EffectiveBudget_NonPositiveModelLimit_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ReferenceSlotPlanner.EffectiveBudget(0, [ImageStepSlotKind.Face]));

        Assert.Contains("MaxReferences", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_InheritsTheBlueprintsOwnValidation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(FaceSlot(), FaceSlot()),
            [],
            maxReferences: 10));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }
}
