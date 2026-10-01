using DreamGenClone.Web.Application.RolePlay;

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

    [Fact]
    public void Validate_WithAnElementKeyDeclaredTwice_Refuses()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReferenceApplicationSelectionValidation.Validate(
                [PackBinding(), PackBinding(referenceAssetId: "body-back-clothed")]));

        Assert.Contains("must be unique", exception.Message, StringComparison.Ordinal);
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
