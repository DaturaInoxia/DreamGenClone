using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The reference-binding rules, in the one place all three persistence paths now read them.
///
/// Those paths each had their own copy, and every copy knew only the legacy scene-asset channel — so a binding that
/// named an identity-PACK reference or a pose skeleton was refused as "requires an approved asset for strategy
/// 'NativeMultiReference'" even though the render supports both. Reported live 2026-09-30, after the pack picker was
/// finally offered on the asset creator: "Asset reference application 'Body' requires an approved asset for strategy
/// 'NativeMultiReference' I selected a body".
/// </summary>
public sealed class ReferenceApplicationSelectionValidationTests
{
    private static ReferenceApplicationSelection PackBinding(
        string elementKey = "Body",
        string? packId = "pack-becky-v9",
        string? referenceAssetId = "body-front-clothed",
        string strategy = "NativeMultiReference") =>
        new()
        {
            ElementKey = elementKey,
            SemanticRole = "character body",
            Kind = "Body",
            Strategy = strategy,
            IdentityPackId = packId,
            ReferenceAssetId = referenceAssetId
        };

    private static ReferenceApplicationSelection SceneAssetBinding() =>
        new()
        {
            ElementKey = "Location",
            SemanticRole = "location continuity",
            Kind = "Location",
            Strategy = "NativeMultiReference",
            SceneAssetId = "asset-pine-clearing",
            SceneAssetImageId = "image-1",
            SceneAssetVersion = 2,
            SceneAssetSha256 = "SHA"
        };

    /// <summary>The reported case: a body bound from the character's approved pack.</summary>
    [Fact]
    public void Validate_WithAnIdentityPackBinding_Accepts()
    {
        ReferenceApplicationSelectionValidation.Validate([PackBinding()]);
    }

    /// <summary>A pose travels as a skeleton and has no version or checksum of its own to pin.</summary>
    [Theory]
    [InlineData("standing-front", null)]
    [InlineData(null, "poses/standing-front.png")]
    public void Validate_WithAPoseBinding_Accepts(string? presetId, string? skeletonPath)
    {
        var binding = new ReferenceApplicationSelection
        {
            ElementKey = "Pose",
            SemanticRole = "stance",
            Kind = "Pose",
            Strategy = "PoseControlNet",
            PosePresetId = presetId,
            SkeletonRelativePath = skeletonPath
        };

        ReferenceApplicationSelectionValidation.Validate([binding]);
    }

    [Fact]
    public void Validate_WithAnApprovedSceneAssetBinding_Accepts()
    {
        ReferenceApplicationSelectionValidation.Validate([SceneAssetBinding()]);
    }

    /// <summary>Text is the one strategy that carries an element without naming an image.</summary>
    [Fact]
    public void Validate_WithATextOnlyBinding_Accepts()
    {
        ReferenceApplicationSelectionValidation.Validate(
        [
            new ReferenceApplicationSelection { ElementKey = "Identity", SemanticRole = "character identity", Strategy = "TextOnly" }
        ]);
    }

    [Fact]
    public void Validate_WithNothingBound_Accepts()
    {
        ReferenceApplicationSelectionValidation.Validate([]);
    }

    /// <summary>
    /// A structural binding that names nothing is refused, and the message says what it COULD name — the three channels
    /// — rather than only the one the old copies knew.
    /// </summary>
    [Fact]
    public void Validate_WithAStructuralBindingThatNamesNothing_RefusesAndListsTheChannels()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
            [
                new ReferenceApplicationSelection
                {
                    ElementKey = "Body",
                    SemanticRole = "character body",
                    Kind = "Body",
                    Strategy = "NativeMultiReference"
                }
            ]));

        Assert.Contains("'Body'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("approved scene asset image", exception.Message, StringComparison.Ordinal);
        Assert.Contains("identity-pack reference", exception.Message, StringComparison.Ordinal);
        Assert.Contains("or a pose", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A pack id alone is a container, not an image: the reference asset inside it is what is conditioned on.</summary>
    [Fact]
    public void Validate_WithAPackButNoReferenceAsset_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate([PackBinding(referenceAssetId: null)]));

        Assert.Contains("no reference asset inside it", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>An approved asset pinned without its version or checksum names an image that could be swapped later.</summary>
    [Fact]
    public void Validate_WithASceneAssetMissingItsVersion_Refuses()
    {
        var binding = SceneAssetBinding();
        binding.SceneAssetVersion = null;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate([binding]));

        Assert.Contains("exact approved asset version or checksum", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A skeleton without a preset id is still exact only if it names a path; neither is refused.</summary>
    [Fact]
    public void Validate_WithAPoseThatNamesNeitherPresetNorPath_Refuses()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
            [
                new ReferenceApplicationSelection
                {
                    ElementKey = "Pose",
                    SemanticRole = "stance",
                    Kind = "Pose",
                    Strategy = "PoseControlNet"
                }
            ]));
    }

    /// <summary>
    /// Two references at ONE address are refused: same element, same character, same position. That is the shape a
    /// silent duplicate takes, and it is still caught after the rule was corrected (see below).
    /// </summary>
    [Fact]
    public void Validate_WithAnElementKeyDeclaredTwiceAtOneAddress_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
                [PackBinding(), PackBinding(referenceAssetId: "body-back-clothed")]));

        Assert.Contains("must be unique", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A frame with TWO characters binds the same element once per character, which is the normal case for a
    /// composition - the face and the build of each person in it. Requiring the element key alone to be unique refused
    /// exactly this (reported live 2026-10-02: "Reference application element keys must be unique.").
    /// </summary>
    [Fact]
    public void Validate_WithTheSameElementForTwoCharacters_Accepts()
    {
        var becky = PackBinding();
        becky.ActorKey = "character-becky";
        becky.Ordinal = 1;

        var dean = PackBinding(referenceAssetId: "body-front-unclothed");
        dean.ActorKey = "character-dean";
        dean.Ordinal = 3;

        ReferenceApplicationSelectionValidation.Validate([becky, dean]);
    }

    /// <summary>
    /// An element that carries SEVERAL images at once - a wardrobe's dress and shoes - repeats the element key and the
    /// character, and is told apart by its position. The planner is what refuses a second image on a slot that does not
    /// declare <c>AllowsMultiple</c>.
    /// </summary>
    [Fact]
    public void Validate_WithTwoImagesOnOneMultiElement_Accepts()
    {
        var dress = PackBinding(elementKey: "Wardrobe", referenceAssetId: "dress-front");
        dress.Kind = "Wardrobe";
        dress.ActorKey = "character-becky";
        dress.Ordinal = 1;

        var shoes = PackBinding(elementKey: "Wardrobe", referenceAssetId: "shoes-front");
        shoes.Kind = "Wardrobe";
        shoes.ActorKey = "character-becky";
        shoes.Ordinal = 2;

        ReferenceApplicationSelectionValidation.Validate([dress, shoes]);
    }

    [Theory]
    [InlineData("", "role", "NativeMultiReference")]
    [InlineData("Body", "", "NativeMultiReference")]
    [InlineData("Body", "character body", "")]
    public void Validate_WithAMissingElementKeySemanticRoleOrStrategy_Refuses(string elementKey, string role, string strategy)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
            [
                new ReferenceApplicationSelection
                {
                    ElementKey = elementKey,
                    SemanticRole = role,
                    Strategy = strategy,
                    IdentityPackId = "pack-becky-v9",
                    ReferenceAssetId = "body-front-clothed"
                }
            ]));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_WithAStrengthOutsideZeroToOne_Refuses(decimal strength)
    {
        var binding = PackBinding();
        binding.Strength = strength;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate([binding]));

        Assert.Contains("strength must be between 0 and 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The REPORTED case, produced by the Composition step's own planner rather than by hand: a cast of two, each
    /// character binding their face and their build from their pack. This is the request that actually reaches the
    /// queue, and it is what refused to enqueue while the element key alone had to be unique.
    /// </summary>
    [Fact]
    public void Validate_WithTheCompositionPlannersTwoCharacterBindings_Accepts()
    {
        ImageStepActor[] cast = [new("character-becky", "Becky"), new("character-dean", "Dean")];
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition(cast);
        var assignments = cast
            .SelectMany(actor => new[]
            {
                new ImageStepSlotAssignment(ImageStepSlotKind.Face, actor.ActorKey, new ImageStepSlotSource(
                    ImageStepReferenceSourceKind.IdentityPackAsset,
                    ReferenceStrategyResolver.IdentityNativeMultiReference,
                    IdentityPackId: "pack-1", ReferenceAssetId: $"face-{actor.ActorKey}")),
                new ImageStepSlotAssignment(ImageStepSlotKind.Body, actor.ActorKey, new ImageStepSlotSource(
                    ImageStepReferenceSourceKind.IdentityPackAsset,
                    ReferenceStrategyResolver.IdentityNativeMultiReference,
                    IdentityPackId: "pack-1", ReferenceAssetId: $"body-{actor.ActorKey}"))
            })
            .ToList();

        var bindings = ReferenceSlotPlanner.Plan(blueprint, assignments, maxReferences: 10);

        Assert.Equal(4, bindings.Count);
        ReferenceApplicationSelectionValidation.Validate(bindings);
    }

    /// <summary>The label is the caller's, so each path still names itself in the message without a second copy of the rule.</summary>
    [Fact]
    public void Validate_WithALabel_NamesTheCallersOwnKindOfBinding()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
            [
                new ReferenceApplicationSelection
                {
                    ElementKey = "Body",
                    SemanticRole = "character body",
                    Strategy = "NativeMultiReference"
                }
            ],
            "Asset reference"));

        Assert.Contains("Asset reference application 'Body'", exception.Message, StringComparison.Ordinal);
    }
}
