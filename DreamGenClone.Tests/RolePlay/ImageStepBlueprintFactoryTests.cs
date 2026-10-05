using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
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
    /// <summary>
    /// Requiredness is the HOST's answer, expressed once as data: mandatory for a LoRA training cell (neither the face
    /// nor the build may come from text alone) and optional wherever prose is enough. It is data rather than component
    /// behaviour precisely because that answer differs per host.
    /// </summary>
    [Fact]
    public void LoraCell_RequiresItsBuildAndItsFaceOnlyWhenTheCellShowsOne()
    {
        var showsFace = ImageStepBlueprintFactory.ForLoraCell(Becky);
        Assert.True(showsFace.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Face).Required);
        Assert.True(showsFace.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Body).Required);
        // The render does not demand wardrobe, so the step must not either.
        Assert.False(showsFace.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Wardrobe).Required);

        // A back view shows no face, so it is not asked for one - the same rule the render applies.
        var backView = ImageStepBlueprintFactory.ForLoraCell(Becky, faceIsRequired: false);
        Assert.False(backView.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Face).Required);
        Assert.True(backView.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Body).Required);
    }

    [Fact]
    public void LoraCell_CarriesTheElementTextTheHostDeclares()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky, new Dictionary<ImageStepSlotKind, string>
        {
            [ImageStepSlotKind.Face] = "dark hair in a Bun, Blue eyes, oval face",
            [ImageStepSlotKind.Body] = "curvy, full bust, fair smooth skin"
        });

        // Face and Body state DIFFERENT text. Showing the build line under the face is what made a face reference
        // look like it had replaced the body, so the two elements must not share one line.
        Assert.Equal(
            "dark hair in a Bun, Blue eyes, oval face",
            blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Face).ElementText);
        Assert.Equal(
            "curvy, full bust, fair smooth skin",
            blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Body).ElementText);

        // A slot the host declares no text for stays null rather than gaining an invented one.
        Assert.Null(blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Wardrobe).ElementText);
    }

    /// <summary>
    /// The pose-library step RE-FORMS by whether a character is picked (2026-09-27): with one it declares the
    /// character's own elements as well as the pose, because a character HAS a face and a build and the test now
    /// conditions on them; with none it declares only the pose, because a face slot with no character behind it is a
    /// control the operator cannot satisfy.
    /// </summary>
    [Fact]
    public void PoseLibraryTest_DeclaresTheCharactersElementsOnlyWhenACharacterIsPicked()
    {
        var poseOnly = ImageStepBlueprintFactory.ForPoseLibraryTest();
        Assert.Equal([ImageStepSlotKind.Pose], poseOnly.Slots.Select(slot => slot.SlotKind));
        Assert.True(poseOnly.Slots.Single().Required);

        var withCharacter = ImageStepBlueprintFactory.ForPoseLibraryTest(Becky);
        Assert.Equal(
            [ImageStepSlotKind.Face, ImageStepSlotKind.Body, ImageStepSlotKind.Wardrobe, ImageStepSlotKind.Pose],
            withCharacter.Slots.Select(slot => slot.SlotKind));

        // The face and build come from the character's APPROVED PACK - the same source the identity renders use - and
        // are addressed to that character, because both are per-character slots.
        foreach (var slotKind in new[] { ImageStepSlotKind.Face, ImageStepSlotKind.Body })
        {
            var slot = withCharacter.Slots.Single(candidate => candidate.SlotKind == slotKind);
            Assert.Contains(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
            Assert.Equal(Becky.ActorKey, slot.ActorKey);
        }

        // Only the pose is required: a character's face and build are the test's context, not its subject.
        Assert.All(
            withCharacter.Slots.Where(slot => slot.SlotKind != ImageStepSlotKind.Pose),
            slot => Assert.False(slot.Required));
    }

    /// <summary>Declaring no element text at all is valid: the step simply shows none.</summary>
    [Fact]
    public void LoraCell_WithoutElementText_DeclaresNone()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky);

        Assert.All(blueprint.Slots, slot => Assert.Null(slot.ElementText));
    }

    /// <summary>
    /// The cell's wardrobe is SUPPLIED BY ITS BODY REFERENCE (2026-09-27). The reference is state-matched, so a clothed
    /// cell's is a clothed full-body image and the garment is in it: describing an outfit as well made the prompt
    /// contradict the image it was conditioned on. Declared as blueprint DATA, and reported through the one omission
    /// source, so the composer's badge, the strike-through and the prompt's slot removal all agree.
    /// </summary>
    [Fact]
    public void LoraCell_DeclaresItsWardrobeSuppliedByTheBodyReference()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky);

        var wardrobe = blueprint.Slots.Single(slot => slot.SlotKind == ImageStepSlotKind.Wardrobe);
        Assert.Equal(ImageStepSlotKind.Body, wardrobe.SuppliedBySlotKind);

        // Nothing else claims to be carried by another element's reference.
        Assert.All(
            blueprint.Slots.Where(slot => slot.SlotKind != ImageStepSlotKind.Wardrobe),
            slot => Assert.Null(slot.SuppliedBySlotKind));
    }

    /// <summary>
    /// A bound body reference reports the wardrobe as supplied as well, and NOT before: an unbound body supplies
    /// nothing, so the outfit must still be described.
    /// </summary>
    [Fact]
    public void LoraCell_WardrobeIsSuppliedOnlyOnceTheBodyReferenceIsBound()
    {
        var blueprint = ImageStepBlueprintFactory.ForLoraCell(Becky);

        Assert.DoesNotContain(
            ImageStepPromptOmission.BoundSlotsFor(blueprint, []),
            entry => entry.SlotKind == ImageStepSlotKind.Wardrobe);

        var bodyBound = new ReferenceApplicationSelection
        {
            Kind = ImageStepSlotKind.Body.ToString(),
            ActorKey = Becky.ActorKey,
            Source = ImageStepReferenceSourceKind.IdentityPackAsset.ToString(),
            ReferenceAssetId = "pack-body-front",
            IdentityPackId = "pack-1"
        };

        var supplied = ImageStepPromptOmission.BoundSlotsFor(blueprint, [bodyBound]);
        Assert.Contains(supplied, entry => entry.SlotKind == ImageStepSlotKind.Body);
        Assert.Contains(supplied, entry => entry.SlotKind == ImageStepSlotKind.Wardrobe);
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
    /// The asset creator's face and build come from the character's APPROVED PACK as well as from an approved scene
    /// asset, because the pack is where a character's curated references actually live.
    ///
    /// Declaring only <c>ApprovedSceneAsset</c> - as this did - meant the pack picker never rendered and the Face and
    /// Build tabs were left with a single dropdown that lists nothing: the operator picked a character, the tabs
    /// appeared, and every control in them was empty (reported live 2026-09-30). Measured on the dev store at that
    /// moment: two characters held approved packs carrying 5 faces and up to 12 bodies, while ZERO character-owned
    /// face/body scene assets carried an approved usable image. Same defect the LoRA cell had.
    /// </summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Face)]
    [InlineData(ImageStepSlotKind.Body)]
    public void AssetCreate_CanBindItsFaceAndBodyFromTheCharacterPack(ImageStepSlotKind slotKind)
    {
        var slot = ImageStepBlueprintFactory.ForAssetCreate(Becky).Slots
            .Single(candidate => candidate.SlotKind == slotKind);

        Assert.Contains(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
        Assert.Equal(Becky.ActorKey, slot.ActorKey);
    }

    /// <summary>
    /// A pack carries faces and bodies and nothing else, so the wardrobe and location slots must not offer it: an
    /// option the source cannot satisfy is a control the operator can never use.
    /// </summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Wardrobe)]
    [InlineData(ImageStepSlotKind.Location)]
    public void AssetCreate_DoesNotOfferThePackForWardrobeOrLocation(ImageStepSlotKind slotKind)
    {
        var slot = ImageStepBlueprintFactory.ForAssetCreate(Becky).Slots
            .Single(candidate => candidate.SlotKind == slotKind);

        Assert.DoesNotContain(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
    }

    /// <summary>
    /// The edit workspace's STEP can bind a character's BUILD from that character's approved identity pack. The pack is
    /// where a character's curated builds actually live, so offering only approved scene assets left the edit surface
    /// unable to condition a build on the character's own pack — reported live 2026-10-03 as "make the edit behave
    /// just the composition … face, body, wardrobe, location".
    ///
    /// <para>
    /// The FACE stays absent from this blueprint ON PURPOSE, and that is not the defect: this workspace binds each
    /// detected person to a pack face through its own Identity tab and runs a face-only correction there, so a face
    /// slot would give each person two mechanisms in one render.
    /// </para>
    /// </summary>
    [Fact]
    public void EditElements_TakesTheBuildFromTheCharactersIdentityPack_AndStillDeclaresNoFaceSlot()
    {
        var blueprint = ImageStepBlueprintFactory.ForEditElements(Cast);

        var bodies = blueprint.Slots.Where(slot => slot.SlotKind == ImageStepSlotKind.Body).ToList();
        Assert.NotEmpty(bodies);
        Assert.All(bodies, body => Assert.Contains(ImageStepReferenceSourceKind.IdentityPackAsset, body.AllowedSources));

        // …and the pack is NOT offered where it cannot supply anything: a pack carries faces and builds only.
        Assert.All(
            blueprint.Slots.Where(slot => slot.SlotKind == ImageStepSlotKind.Wardrobe),
            wardrobe => Assert.DoesNotContain(ImageStepReferenceSourceKind.IdentityPackAsset, wardrobe.AllowedSources));

        // The face slot's absence is deliberate (the workspace's own Identity tab owns face conditioning here).
        Assert.DoesNotContain(blueprint.Slots, slot => slot.SlotKind == ImageStepSlotKind.Face);
    }

    /// <summary>
    /// A location is a container of accepted views (four elevations, an interior), so its slot carries a LIST: binding
    /// Front and Left together is how one building stays the same building across angles. EVERY blueprint that declares
    /// a location slot opts in — a host that offered one view while another offered several would be the drift this
    /// factory exists to prevent.
    /// </summary>
    [Fact]
    public void EveryBlueprintWithALocationSlot_AcceptsMoreThanOneView()
    {
        var blueprints = new (string Name, ImageStepBlueprint Blueprint)[]
        {
            ("ProductionStudio", ImageStepBlueprintFactory.ForProductionStudio(Cast)),
            ("AssetCreate", ImageStepBlueprintFactory.ForAssetCreate(Becky)),
            ("PackIdentityComposition", ImageStepBlueprintFactory.ForPackIdentityComposition(Cast)),
            ("EditElements", ImageStepBlueprintFactory.ForEditElements(Cast))
        };

        foreach (var (name, blueprint) in blueprints)
        {
            var slot = blueprint.Slots.Single(candidate => candidate.SlotKind == ImageStepSlotKind.Location);

            Assert.True(slot.AllowsMultiple, $"{name} declares a single-valued location slot.");
        }
    }

    /// <summary>
    /// With no character picked there is nobody to take a face or build from, so the asset creator declares only the
    /// frame-wide elements. Picking a character is what DECLARES the per-character tabs, which is why the selector sits
    /// above them rather than beside them.
    /// </summary>
    [Fact]
    public void AssetCreate_DeclaresNoPerCharacterSlotsWithoutASubject()
    {
        var blueprint = ImageStepBlueprintFactory.ForAssetCreate();

        Assert.Equal(
            [ImageStepSlotKind.Location, ImageStepSlotKind.Pose],
            blueprint.Slots.Select(slot => slot.SlotKind));
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

    /// <summary>
    /// The Composition step binds each character's face and build from that character's APPROVED identity PACK, which
    /// is where those references actually live: measured in the dev store 2026-10-02, Becky holds an approved
    /// BodyComplete pack with 5 faces and 12 builds, while ZERO character-owned face/body scene assets carry an
    /// approved usable image. Declaring only <c>ApprovedSceneAsset</c> left the Body tab with a dropdown that lists
    /// nothing, and no Face tab at all (reported live 2026-10-02: "2.1 should allow for picking becky face and body
    /// but it says no approved build reference exists for this character yet"). Same defect the LoRA cell and the asset
    /// creator were fixed for; this is the host that was still missing it.
    /// </summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Face)]
    [InlineData(ImageStepSlotKind.Body)]
    public void PackIdentityComposition_CanBindItsFaceAndBodyFromTheCharacterPack(ImageStepSlotKind slotKind)
    {
        var slot = ImageStepBlueprintFactory.ForPackIdentityComposition(Cast).Slots
            .Single(candidate => candidate.SlotKind == slotKind && candidate.ActorKey == Becky.ActorKey);

        Assert.Contains(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
        Assert.Equal(Becky.ActorKey, slot.ActorKey);
    }

    /// <summary>
    /// The face is declared before the build for every character: the ordinal is request data and the first reference
    /// anchors the frame, so the identity is what the model places before the build that follows it.
    /// </summary>
    [Fact]
    public void PackIdentityComposition_DeclaresTheFaceBeforeTheBuild()
    {
        var slots = ImageStepBlueprintFactory.ForPackIdentityComposition(Cast).Slots;

        Assert.Equal(
            [
                ImageStepSlotKind.Face, ImageStepSlotKind.Body, ImageStepSlotKind.Wardrobe,
                ImageStepSlotKind.Face, ImageStepSlotKind.Body, ImageStepSlotKind.Wardrobe,
                ImageStepSlotKind.Location, ImageStepSlotKind.Pose
            ],
            slots.Select(slot => slot.SlotKind));
    }

    /// <summary>A pack carries faces and builds and nothing else, so the shared elements must not offer it.</summary>
    [Theory]
    [InlineData(ImageStepSlotKind.Wardrobe)]
    [InlineData(ImageStepSlotKind.Location)]
    [InlineData(ImageStepSlotKind.Pose)]
    public void PackIdentityComposition_DoesNotOfferThePackForSharedElements(ImageStepSlotKind slotKind)
    {
        var slot = ImageStepBlueprintFactory.ForPackIdentityComposition(Cast).Slots
            .First(candidate => candidate.SlotKind == slotKind);

        Assert.DoesNotContain(ImageStepReferenceSourceKind.IdentityPackAsset, slot.AllowedSources);
    }
}
