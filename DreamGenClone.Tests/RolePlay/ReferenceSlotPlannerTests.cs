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

    private static ImageStepSlotBlueprint LocationSlot(bool required = false, bool allowsMultiple = true) =>
        new(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.ApprovedSceneAsset],
            null, required, AllowsMultiple: allowsMultiple);

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

    /// <summary>
    /// An identity-pack face bound on the ASSET CREATOR plans through, and the pack travels on the binding.
    ///
    /// This is the chain that was broken. The composer offers the pack picker only when the slot DECLARES
    /// <c>IdentityPackAsset</c>, so with the source missing the control never rendered at all (reported live
    /// 2026-09-30: "i picked a character pack, the tabs show but nothing shows in the face, body drop downs"), and the
    /// planner refuses the source outright as well - so the host's translation of the binding into the render's identity
    /// conditioning had nothing to consume either.
    /// </summary>
    [Fact]
    public void Plan_IdentityPackFaceOnTheAssetCreator_CarriesThePackThrough()
    {
        var bindings = ReferenceSlotPlanner.Plan(
            ImageStepBlueprintFactory.ForAssetCreate(new ImageStepActor("p-becky", "Becky")),
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", new ImageStepSlotSource(
                    ImageStepReferenceSourceKind.IdentityPackAsset,
                    "IdentityNativeMultiReference",
                    IdentityPackId: "pack-9",
                    ReferenceAssetId: "ref-front"))
            ],
            maxReferences: 10);

        var binding = Assert.Single(bindings);
        Assert.Equal("Face", binding.Kind);
        Assert.Equal("p-becky", binding.ActorKey);
        Assert.Equal("IdentityPackAsset", binding.Source);
        Assert.Equal("pack-9", binding.IdentityPackId);
        Assert.Equal("ref-front", binding.ReferenceAssetId);
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

    private static ImageStepSlotBlueprint WardrobeSlot(string actorKey = "p-becky", bool allowsMultiple = true) =>
        new(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.ApprovedSceneAsset],
            actorKey, Required: false, AllowsMultiple: allowsMultiple);

    /// <summary>
    /// A wardrobe is the one slot that carries SEVERAL references: a dress and the shoes that go with it are two
    /// garments, so the operator binds two images and both must reach the render. Position decides placement, so the
    /// order they bound them in is the order the model sees.
    /// </summary>
    [Fact]
    public void Plan_MultiSlot_KeepsEveryBindingInTheOrderTheHostSuppliedThem()
    {
        var bindings = ReferenceSlotPlanner.Plan(
            Blueprint(WardrobeSlot()),
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source())
            ],
            maxReferences: 10);

        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, binding => Assert.Equal("Wardrobe", binding.ElementKey));
        Assert.All(bindings, binding => Assert.Equal("p-becky", binding.ActorKey));
        Assert.Equal([1, 2], bindings.Select(binding => binding.Ordinal));
    }

    /// <summary>
    /// The opt-in is what makes the difference: the SAME two assignments against a slot that does not allow multiple
    /// are refused, so "second reference silently dropped" cannot become the behaviour of every other slot.
    /// </summary>
    [Fact]
    public void Plan_MultiSlotNotAllowed_StillRefusesTheSecondBinding()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            Blueprint(WardrobeSlot(allowsMultiple: false)),
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source())
            ],
            maxReferences: 10));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Several wardrobe images count against the SAME budget as everything else, and the measured pose ceiling still
    /// applies: three garments beside a face, a body and a skeleton is seven references, which stops the skeleton
    /// being honoured.
    /// </summary>
    [Fact]
    public void Plan_MultiSlot_CountsEveryBindingAgainstTheBudget()
    {
        var blueprint = Blueprint(
            FaceSlot("p-becky"),
            WardrobeSlot(),
            PoseSlot());

        var exception = Assert.Throws<InvalidOperationException>(() => ReferenceSlotPlanner.Plan(
            blueprint,
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Wardrobe, "p-becky", Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Pose, null, Source(ImageStepReferenceSourceKind.PoseLibrarySkeleton))
            ],
            maxReferences: 10));

        Assert.Contains("CASE-22", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A location is a CONTAINER of accepted views — four elevations and an interior of one shed — so its slot carries
    /// a list as well. Binding Front AND Left together is how the same building reads as the same building from two
    /// angles, and the order the operator bound them in is the order the model sees.
    /// </summary>
    [Fact]
    public void Plan_LocationKeepsEveryViewInOrder()
    {
        var bindings = ReferenceSlotPlanner.Plan(
            Blueprint(LocationSlot()),
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, Source()),
                new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, Source())
            ],
            maxReferences: 10);

        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, binding => Assert.Equal("Location", binding.ElementKey));
        Assert.All(bindings, binding => Assert.Null(binding.ActorKey));
        Assert.Equal([1, 2], bindings.Select(binding => binding.Ordinal));
    }
}
