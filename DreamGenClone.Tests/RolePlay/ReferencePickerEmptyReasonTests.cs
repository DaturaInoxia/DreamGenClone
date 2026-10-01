using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// An empty reference picker has to say WHY it is empty, because the two causes have different remedies: nothing of that
/// kind exists yet, or something does and no image of it is approved for production. A dropdown listing only "Text only /
/// no asset reference" states neither, and reads as a broken control.
///
/// Reported live 2026-09-30: "i picked a character pack, the tabs show but nothing shows in the face, body drop downs."
/// The character's faces and builds live in an identity PACK, so the approved-scene-asset list was legitimately empty -
/// and the operator had no way to tell that from a picker that had failed.
/// </summary>
public sealed class ReferencePickerEmptyReasonTests
{
    [Fact]
    public void Build_WithNothingOfThatKind_SaysNothingExistsAndWhereToCreateOne()
    {
        var reason = ReferencePickerEmptyReason.Build(SceneAssetType.CharacterFace, "p-becky", assetCount: 0);

        Assert.NotNull(reason);
        Assert.Contains("No approved face reference exists for this character yet", reason, StringComparison.Ordinal);
        Assert.Contains("Create and approve one in the Asset Manager", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second cause is NOT the first one: items exist and none is usable. Reporting the count is what separates
    /// "your library is empty" from "your library is unapproved", which are different pieces of work for the operator.
    /// </summary>
    [Fact]
    public void Build_WithItemsButNoApprovedImage_ReportsThemAsUnapprovedRatherThanMissing()
    {
        var reason = ReferencePickerEmptyReason.Build(SceneAssetType.CharacterBody, "p-becky", assetCount: 3);

        Assert.NotNull(reason);
        Assert.Contains("3 approved build references exist for this character", reason, StringComparison.Ordinal);
        Assert.Contains("none carries an image approved for production yet", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("exists for this character yet", reason, StringComparison.Ordinal);
    }

    /// <summary>A single item must not read as a plural one.</summary>
    [Fact]
    public void Build_WithOneUnapprovedItem_UsesTheSingularNoun()
    {
        var reason = ReferencePickerEmptyReason.Build(SceneAssetType.CharacterFace, "p-becky", assetCount: 1);

        Assert.NotNull(reason);
        Assert.Contains("1 approved face reference exists for this character", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("references exist", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A face or a build can be filled from the character's identity pack, so an empty approved-asset list points at the
    /// working alternative. Without this the operator's only reading of the empty control is that identity is broken.
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.CharacterFace)]
    [InlineData(SceneAssetType.CharacterBody)]
    public void Build_ForAFaceOrABuild_OffersTheIdentityPackAsTheAlternative(SceneAssetType assetType)
    {
        var reason = ReferencePickerEmptyReason.Build(assetType, "p-becky", assetCount: 0);

        Assert.NotNull(reason);
        Assert.Contains(
            "Or bind one of the character's approved identity-pack references, above.",
            reason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A pack carries faces and bodies and nothing else, so a wardrobe or location picker must not send the operator to
    /// a control that has nothing for them.
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.Wardrobe)]
    [InlineData(SceneAssetType.Location)]
    [InlineData(SceneAssetType.Prop)]
    public void Build_ForAnElementAPackCannotCarry_DoesNotMentionThePack(SceneAssetType assetType)
    {
        var reason = ReferencePickerEmptyReason.Build(assetType, "p-becky", assetCount: 0);

        Assert.NotNull(reason);
        Assert.DoesNotContain("identity-pack", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A wardrobe is a shared library, so its list is never scoped to a character - and the reason must not claim it was,
    /// because that would send the operator looking for a per-character wardrobe that does not exist.
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.Wardrobe)]
    [InlineData(SceneAssetType.Location)]
    public void Build_ForASharedElement_NeverClaimsTheListWasScopedToACharacter(SceneAssetType assetType)
    {
        var reason = ReferencePickerEmptyReason.Build(assetType, "p-becky", assetCount: 0);

        Assert.NotNull(reason);
        Assert.DoesNotContain("for this character", reason, StringComparison.Ordinal);
    }

    /// <summary>A character-owned element with no character in force is unscoped, and says so by omission rather than by lying.</summary>
    [Fact]
    public void Build_ForACharacterElementWithNoCharacterInForce_DropsTheScopePhrase()
    {
        var reason = ReferencePickerEmptyReason.Build(SceneAssetType.CharacterFace, characterProfileId: null, assetCount: 0);

        Assert.NotNull(reason);
        Assert.DoesNotContain("for this character", reason, StringComparison.Ordinal);
        Assert.Contains("No approved face reference exists yet", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which asset types belong to ONE character. A character element addresses somebody; a wardrobe item and a location
    /// are shared, and scoping them to a character is what made one dress unreachable for every other character
    /// (found 2026-09-29).
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.CharacterFace, true)]
    [InlineData(SceneAssetType.CharacterBody, true)]
    [InlineData(SceneAssetType.CharacterPose, true)]
    [InlineData(SceneAssetType.Wardrobe, false)]
    [InlineData(SceneAssetType.Location, false)]
    [InlineData(SceneAssetType.Prop, false)]
    [InlineData(SceneAssetType.Style, false)]
    [InlineData(SceneAssetType.Playground, false)]
    public void IsCharacterOwned_AnswersPerAssetType(SceneAssetType assetType, bool expected)
    {
        Assert.Equal(expected, ReferencePickerEmptyReason.IsCharacterOwned(assetType));
    }

    /// <summary>A negative count is not a state the store can be in, so it is refused rather than described.</summary>
    [Fact]
    public void Build_WithANegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReferencePickerEmptyReason.Build(SceneAssetType.CharacterFace, "p-becky", assetCount: -1));
    }
}
