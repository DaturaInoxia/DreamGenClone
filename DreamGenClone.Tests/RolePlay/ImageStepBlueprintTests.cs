using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The blueprint is what replaces host-specific logic in the composer, so its rules are the composer's contract:
/// a slot the operator could never fill, two slots that would fight over the same prompt elements, or a
/// per-character slot with no character, are all declared-invalid rather than rendered as dead controls.
/// </summary>
public sealed class ImageStepBlueprintTests
{
    private static ImageStepSlotBlueprint FaceSlot(string actorKey = "p-becky", bool required = false) =>
        new(ImageStepSlotKind.Face, ImageStepSlotPrefill.PackCanonicalFace,
            [ImageStepReferenceSourceKind.ApprovedSceneAsset], actorKey, required);

    [Fact]
    public void Validate_WellFormedBlueprint_Passes()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.LoraCell,
            "Shoot this cell",
            ImageStepSourceMode.None,
            [
                FaceSlot(),
                new(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.ApprovedSceneAsset]),
                new(ImageStepSlotKind.Pose, ImageStepSlotPrefill.None, [ImageStepReferenceSourceKind.PoseLibrarySkeleton])
            ],
            ImageStepPersistenceKind.LoraCellAttempt);

        blueprint.Validate();
    }

    [Fact]
    public void Validate_EmptyTitle_Throws()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "   ", ImageStepSourceMode.None, [FaceSlot()], ImageStepPersistenceKind.SceneImage);

        var exception = Assert.Throws<InvalidOperationException>(blueprint.Validate);
        Assert.Contains("title", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_SameSlotTwice_Throws()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "Two faces", ImageStepSourceMode.None, [FaceSlot(), FaceSlot()],
            ImageStepPersistenceKind.SceneImage);

        // Two face slots for one character would both clear the same appearance line, so which one "won" would
        // depend on iteration order. That is refused rather than left to chance.
        var exception = Assert.Throws<InvalidOperationException>(blueprint.Validate);
        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SameSlotKindForDifferentActors_Passes()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "Two characters", ImageStepSourceMode.None,
            [FaceSlot("p-becky"), FaceSlot("p-dean")],
            ImageStepPersistenceKind.SceneImage);

        blueprint.Validate();
    }

    [Fact]
    public void Validate_SlotWithNoAllowedSource_Throws()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "Dead control", ImageStepSourceMode.None,
            [new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, [])],
            ImageStepPersistenceKind.SceneImage);

        var exception = Assert.Throws<InvalidOperationException>(blueprint.Validate);
        Assert.Contains("allows no reference source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PerCharacterSlotWithoutAnActorKey_Throws()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "Nameless face", ImageStepSourceMode.None,
            [new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.None,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset])],
            ImageStepPersistenceKind.SceneImage);

        var exception = Assert.Throws<InvalidOperationException>(blueprint.Validate);
        Assert.Contains("names no actor key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SceneLevelSlotWithAnActorKey_Throws()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.Compose, "A location for Becky", ImageStepSourceMode.None,
            [new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], ActorKey: "p-becky")],
            ImageStepPersistenceKind.SceneImage);

        var exception = Assert.Throws<InvalidOperationException>(blueprint.Validate);
        Assert.Contains("scene-level", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pose may name a character (steering one stance) or not (steering the frame), so it is the one slot kind
    /// that is allowed either way. Pinned because the alternative - a boolean per-character flag - silently produces
    /// the scope <c>character:.position</c> for the frame-wide case, which addresses nobody.
    /// </summary>
    [Theory]
    [InlineData("p-becky")]
    [InlineData(null)]
    public void Validate_PoseSlotWithOrWithoutAnActorKey_Passes(string? actorKey)
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.PoseRender, "Pose", ImageStepSourceMode.ProducedImage,
            [new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.None,
                [ImageStepReferenceSourceKind.PoseLibrarySkeleton], ActorKey: actorKey)],
            ImageStepPersistenceKind.SceneImage);

        blueprint.Validate();
    }

    /// <summary>
    /// Every slot kind must answer the actor-key question explicitly. A new kind that forgot to would throw here
    /// instead of quietly mapping its prompt elements to the wrong place.
    /// </summary>
    [Fact]
    public void ActorKeyRequirement_CoversEverySlotKind()
    {
        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);
            Assert.True(Enum.IsDefined(requirement), $"Slot kind '{slotKind}' returned an undefined requirement.");
        }
    }

    /// <summary>
    /// Batching is host data, and the default is load-bearing in BOTH directions: the LoRA studio forbids a sweep
    /// ("the training set is judged frame by frame"), while the character pose library IS one.
    /// </summary>
    [Fact]
    public void AllowsBatch_DefaultsToFalse()
    {
        var blueprint = new ImageStepBlueprint(
            ImageStepKind.LoraCell, "Cell", ImageStepSourceMode.None, [FaceSlot()],
            ImageStepPersistenceKind.LoraCellAttempt);

        Assert.False(blueprint.AllowsBatch);
    }
}
