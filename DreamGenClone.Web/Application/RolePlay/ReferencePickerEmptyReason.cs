using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Why an approved-reference picker has nothing to list - as prose the operator can act on, or null when it does have
/// something.
///
/// <para>
/// An empty dropdown is a control nobody can read. It lists only "Text only / no asset reference", so it looks like a
/// picker that failed rather than an empty library, and it never says which of the two possible causes it is: nothing of
/// that kind exists yet, or something does and none of its images is approved for production. Those have different
/// remedies, so they are named separately (reported live 2026-09-30: "i picked a character pack, the tabs show but
/// nothing shows in the face, body drop downs" - the character's faces and builds live in an identity PACK, and the
/// approved-asset list was legitimately empty).
/// </para>
///
/// <para>
/// Pure and static for the same reason <see cref="IdentityPackReferenceResolver"/> is: the wording of an empty control
/// is a decision worth proving on its own, and a picker cannot be asked what it would have said.
/// </para>
/// </summary>
public static class ReferencePickerEmptyReason
{
    /// <summary>
    /// Whether an asset type is OWNED by a character. A face, a build and a character pose are (a character element
    /// addresses somebody); a wardrobe item and a location are shared, so filtering them by character hides every item
    /// created from another character's studio (found 2026-09-29).
    /// </summary>
    public static bool IsCharacterOwned(SceneAssetType assetType) => assetType is SceneAssetType.CharacterFace
        or SceneAssetType.CharacterBody
        or SceneAssetType.CharacterPose;

    /// <summary>
    /// The reason this picker is empty, or null when it is not.
    /// </summary>
    /// <param name="assetType">The element kind the picker lists.</param>
    /// <param name="characterProfileId">The character the list is scoped to, when it is scoped at all.</param>
    /// <param name="assetCount">How many assets of that kind the scope holds, before the approval filter is applied.</param>
    public static string? Build(SceneAssetType assetType, string? characterProfileId, int assetCount)
    {
        if (assetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assetCount), assetCount, "An asset count cannot be negative.");
        }

        var element = Label(assetType);
        var scope = IsCharacterOwned(assetType) && !string.IsNullOrWhiteSpace(characterProfileId)
            ? " for this character"
            : string.Empty;

        // The noun AND the verb agree with the count: "1 approved face reference exists" rather than "1 ... exist", which
        // is the kind of wording that makes an operator doubt the number is real.
        var plural = assetCount != 1;
        var reason = assetCount == 0
            ? $"No {element} exists{scope} yet, so there is nothing to bind here."
                + " Create and approve one in the Asset Manager."
            : $"{assetCount} {element}{(plural ? "s" : string.Empty)} {(plural ? "exist" : "exists")}{scope},"
                + " but none carries an image approved for production yet. Approve one in the Asset Manager.";

        // A pack carries faces and bodies, so only those two can be filled from one. Appended to BOTH causes rather than
        // only the second: "nothing exists yet" is precisely the state where the pack is the working alternative.
        return IsPackFillable(assetType)
            ? reason + " Or bind one of the character's approved identity-pack references, above."
            : reason;
    }

    /// <summary>Whether a slot of this element kind can be filled from a character's identity pack.</summary>
    public static bool IsPackFillable(SceneAssetType assetType) =>
        assetType is SceneAssetType.CharacterFace or SceneAssetType.CharacterBody;

    /// <summary>What one element of this kind is called, so the reason reads as prose rather than as an enum name.</summary>
    private static string Label(SceneAssetType assetType) => assetType switch
    {
        SceneAssetType.CharacterFace => "approved face reference",
        SceneAssetType.CharacterBody => "approved build reference",
        SceneAssetType.CharacterPose => "approved character pose",
        SceneAssetType.Wardrobe => "wardrobe item",
        SceneAssetType.Location => "location",
        SceneAssetType.Prop => "prop",
        SceneAssetType.Style => "style",
        SceneAssetType.Character => "character asset",
        _ => $"{assetType} asset"
    };
}
