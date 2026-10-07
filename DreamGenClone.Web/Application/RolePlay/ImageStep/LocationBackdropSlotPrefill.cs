using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns a production group's bound location backdrop into the reference a Composition step starts with.
///
/// <para>
/// A sibling of <see cref="IdentityPackSlotPrefill"/>: the group's per-POV backdrop is the durable record, and the
/// composer's bindings are rebuilt per visit, so this re-seeds the blueprint's declared <c>Location</c> slot from the
/// backdrop on every composer open. A blueprint that declares no location slot is refused by name — the bindings would
/// otherwise be silently dropped, and the render would no longer be the place.
/// </para>
/// </summary>
public static class LocationBackdropSlotPrefill
{
    /// <summary>Whether the blueprint declares a frame-wide <c>Location</c> slot at all.</summary>
    public static bool HasLocationSlot(ImageStepBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return blueprint.Slots.Any(slot => slot.SlotKind == ImageStepSlotKind.Location);
    }

    /// <summary>
    /// The single <c>Location</c> binding the backdrop seeds. Refused by name when the blueprint declares no location
    /// slot, so the caller can surface a notice instead of a silently dropped reference.
    /// </summary>
    public static ReferenceApplicationSelection For(
        ImageStepBlueprint blueprint,
        SceneImageLocationBackdrop backdrop)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(backdrop);

        if (!HasLocationSlot(blueprint))
        {
            throw new InvalidOperationException(
                "This step declares no Location slot, so a location backdrop cannot be pre-filled. Add the slot to the "
                + "blueprint the factory builds for this surface, or leave the location as text-only.");
        }

        return new ReferenceApplicationSelection
        {
            ElementKey = ReferenceStrategyCatalogue.ElementKeyForSlot(ImageStepSlotKind.Location),
            SemanticRole = "location continuity",
            Kind = ImageStepSlotKind.Location.ToString(),
            Source = ImageStepReferenceSourceKind.ApprovedSceneAsset.ToString(),
            Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
            SceneAssetId = backdrop.AssetId,
            SceneAssetImageId = backdrop.ImageId,
            SceneAssetVersion = backdrop.ProductionVersion,
            SceneAssetSha256 = backdrop.Sha256,
            Ordinal = 1
        };
    }
}
