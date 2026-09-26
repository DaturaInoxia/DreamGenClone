using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The factory is the single place host knowledge lives, so these tests are the contract that the seven surfaces
/// really are one component: every blueprint is valid, and every slot can be filled by a source the picker offers.
/// </summary>
public sealed class ImageStepBlueprintFactoryTests
{
    private static readonly ImageStepActor Becky = new("p-becky", "Becky");
    private static readonly ImageStepActor Dean = new("p-dean", "Dean");
    private static readonly ImageStepActor[] Cast = [Becky, Dean];

    /// <summary>
    /// The LoRA cell is shot for a character whose identity lives in an APPROVED PACK, and its face and body come
    /// from that pack. Declaring only <c>ApprovedSceneAsset</c> meant the pack picker never rendered, so the operator
    /// had no control at all to add either reference (reported live 2026-09-26: "How do I add the Face Reference?").
    /// </summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Face)]
    [InlineData(ImageStepSlotKind.Body)]
    public void LoraCell_CanBindItsFaceAndBodyFromTheCharacterPack(ImageStepSlotKind slotKind)
    {
        var slot = ImageStepBlueprintFactory.ForLoraCell(Becky).Slots
            .Single(candidate => candidate.SlotKind == slotKind);

        Assert.Contains(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
    }

    /// <summary>A pack holds faces and bodies and nothing else, so the wardrobe slot must not offer it.</summary>
    [Fact]
    public void LoraCell_DoesNotOfferThePackForWardrobe()
    {
        var slot = ImageStepBlueprintFactory.ForLoraCell(Becky).Slots
            .Single(candidate => candidate.SlotKind == ImageStepSlotKind.Wardrobe);

        Assert.DoesNotContain(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
    }

    /// <summary>
    /// An element's current text travels on the blueprint, so the step can show what the element says and show it
    /// replaced once a reference image supplies it.
    /// </summary>
    [Fact]
    public void LoraCell_CarriesTheElementTextTheHostDeclares()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky, new Dictionary<ImageStepSlotKind, string>
        {
            [ImageStepSlotKind.Face] = "curvy, full bust, fair smooth skin",
            [ImageStepSlotKind.Body] = "curvy, full bust, fair smooth skin"
        });

        Assert.Equal(
            "curvy, full bust, fair smooth skin",
            blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Face).ElementText);
        Assert.Equal(
            "curvy, full bust, fair smooth skin",
            blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Body).ElementText);

        // A slot the host declares no text for stays null rather than gaining an invented one.
        Assert.Null(blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Wardrobe).ElementText);
    }

    /// <summary>Declaring no element text at all is valid: the step simply shows none.</summary>
    [Fact]
    public void LoraCell_WithoutElementText_DeclaresNone()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky);

        Assert.All(blueprint.Slots, slot => Assert.Null(slot.ElementText));
    }

    public static TheoryData<ImageStepBlueprint> AllBlueprints()
    {
        var data = new TheoryData<ImageStepBlueprint>();
        data.Add(ImageStepBlueprintFactory.ForProductionStudio(Cast));
        data.Add(ImageStepBlueprintFactory.ForProductionStudio([Becky]));
        data.Add(ImageStepBlueprintFactory.ForLoraCell(Becky));
        data.Add(ImageStepBlueprintFactory.ForPoseLibraryTest());
        data.Add(ImageStepBlueprintFactory.ForAssetCreate());
        data.Add(ImageStepBlueprintFactory.ForEdit(Cast));
        data.Add(ImageStepBlueprintFactory.ForCharacterPoseLibrary(Becky));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllBlueprints))]
    public void EveryBlueprint_IsValid(ImageStepBlueprint blueprint)
    {
        // The factory validates on build, so a malformed blueprint would already have thrown. Asserting again here
        // pins that the guarantee is the factory's, not a caller's.
        blueprint.Validate();
        Assert.False(string.IsNullOrWhiteSpace(blueprint.Title));
    }

    [Theory]
    [MemberData(nameof(AllBlueprints))]
    public void EverySlot_OffersAtLeastOneSource(ImageStepBlueprint blueprint)
    {
        // A slot with no source is a control the operator can never satisfy, which is worse than no control.
        Assert.All(blueprint.Slots, slot => Assert.NotEmpty(slot.AllowedSources));
    }

    [Theory]
    [MemberData(nameof(AllBlueprints))]
    public void EveryPerCharacterSlot_NamesItsActor(ImageStepBlueprint blueprint)
    {
        // A per-character slot must name its character. The prompt plan writes character:{key}.appearance, so a slot
        // with no actor key would remove the wrong character's prose or none at all - which is exactly why the factory
        // takes the cast instead of resolving it per render.
        Assert.All(blueprint.Slots, slot =>
        {
            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slot.SlotKind);
            if (requirement == ImageStepActorKeyRequirement.Required)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(slot.ActorKey),
                    $"Slot '{slot.SlotKind}' is per-character but names no actor.");
            }

            if (requirement == ImageStepActorKeyRequirement.Forbidden)
            {
                Assert.True(
                    string.IsNullOrWhiteSpace(slot.ActorKey),
                    $"Scene-level slot '{slot.SlotKind}' names actor '{slot.ActorKey}'.");
            }
        });
    }

    /// <summary>A two-character Moment gets a slot PER character, so each one binds its own face.</summary>
    [Fact]
    public void ProductionStudio_TwoActors_DeclareAFaceSlotEach()
    {
        var blueprint = ImageStepBlueprintFactory.ForProductionStudio(Cast);
        var faceSlots = blueprint.Slots.Where(slot => slot.SlotKind == ImageStepSlotKind.Face).ToArray();

        Assert.Equal(2, faceSlots.Length);
        Assert.Equal(["p-becky", "p-dean"], faceSlots.Select(slot => slot.ActorKey));
    }

    /// <summary>
    /// A cast member without a profile key cannot be addressed, so the blueprint is refused at its source rather than
    /// producing slots whose prompt elements would silently go nowhere.
    /// </summary>
    [Fact]
    public void ActorWithoutAProfileKey_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBlueprintFactory.ForLoraCell(new ImageStepActor(string.Empty, "Becky")));

        Assert.Contains("profile key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyCast_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ImageStepBlueprintFactory.ForProductionStudio([]));

        Assert.Contains("cast", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The two batching surfaces are opposites on purpose, and the flag is data precisely because of that.
    /// </summary>
    [Fact]
    public void LoraCell_DoesNotBatch_ButThePoseLibraryDoes()
    {
        Assert.False(ImageStepBlueprintFactory.ForLoraCell(Becky).AllowsBatch);
        Assert.True(ImageStepBlueprintFactory.ForCharacterPoseLibrary(Becky).AllowsBatch);
    }

    /// <summary>
    /// The pose library's batch render needs a face, a body AND a pose; anything less produces an asset that is not
    /// the character, which is what makes those three required rather than pre-filled.
    /// </summary>
    [Fact]
    public void PoseLibrary_RequiresFaceBodyAndPose()
    {
        var blueprint = ImageStepBlueprintFactory.ForCharacterPoseLibrary(Becky);

        Assert.Equal(3, blueprint.Slots.Count);
        Assert.All(blueprint.Slots, slot => Assert.True(slot.Required));
        Assert.Equal(
            [ImageStepSlotKind.Face, ImageStepSlotKind.Body, ImageStepSlotKind.Pose],
            blueprint.Slots.Select(slot => slot.SlotKind));
        Assert.Equal("p-becky", blueprint.Slots[0].ActorKey);
    }

    /// <summary>
    /// The pose-library test render keeps nothing today, which is why a render made there could never become a
    /// reference for a later step. Pinned so that is a visible decision rather than an accident.
    /// </summary>
    [Fact]
    public void PoseLibraryTest_PersistsNothing()
    {
        Assert.Equal(ImageStepPersistenceKind.Throwaway, ImageStepBlueprintFactory.ForPoseLibraryTest().PersistenceKind);
    }

    /// <summary>
    /// Every slot kind a blueprint can declare must be fillable by the composer, so the picker's asset-type mapping
    /// has to cover it. A slot kind with no mapping would render an unfillable control.
    /// </summary>
    [Fact]
    public void EverySlotKindUsedByAFactory_HasAPickerAssetType()
    {
        foreach (var blueprint in AllBlueprints())
        {
            foreach (var slot in blueprint.Slots.Where(slot =>
                slot.AllowedSources.Contains(ImageStepReferenceSourceKind.ApprovedSceneAsset)))
            {
                var assetType = slot.SlotKind switch
                {
                    ImageStepSlotKind.Face => SceneAssetType.CharacterFace,
                    ImageStepSlotKind.Body => SceneAssetType.CharacterBody,
                    ImageStepSlotKind.Wardrobe => SceneAssetType.Wardrobe,
                    ImageStepSlotKind.Location => SceneAssetType.Location,
                    ImageStepSlotKind.CharacterPose => SceneAssetType.CharacterPose,
                    _ => (SceneAssetType?)null
                };

                Assert.True(assetType.HasValue, $"Slot kind '{slot.SlotKind}' offers an approved asset but has no picker type.");
            }
        }
    }

    /// <summary>A pose asset is a SceneAsset, so the new type must be APPENDED - existing rows persist the name.</summary>
    [Fact]
    public void CharacterPoseAssetType_IsAppended()
    {
        Assert.Equal(9, (int)SceneAssetType.CharacterPose);
        Assert.Equal(8, (int)SceneAssetType.Character);
    }
}
