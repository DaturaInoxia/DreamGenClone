using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>Why a bound backdrop was or was not seeded into the step's <c>Location</c> slot.</summary>
public enum LocationBackdropSeedOutcome
{
    Seeded = 0,

    /// <summary>The blueprint names no <c>Location</c> slot, so a binding would be silently dropped.</summary>
    NoLocationSlot = 1,

    /// <summary>Nothing is selected to prove the capability against yet.</summary>
    NoModelSelected = 2,

    /// <summary>The selected model does not carry a reference image.</summary>
    ModelCannotCarry = 3
}

/// <summary>
/// The outcome of the one decision about seeding a bound backdrop, plus the reason when it was not seeded. The reason
/// is the strategy resolver's OWN words for the capability case, so the operator reads the same diagnostic the render
/// would have raised.
/// </summary>
public sealed record LocationBackdropSeed(
    LocationBackdropSeedOutcome Outcome,
    ReferenceApplicationSelection? Binding,
    string Reason)
{
    public bool IsSeeded => Outcome == LocationBackdropSeedOutcome.Seeded;
}

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
    /// Whether a bound backdrop may be seeded into the step's <c>Location</c> slot, decided in ONE place for every host.
    ///
    /// <para>
    /// A declared slot is NOT enough, and trusting it was the defect (measured 2026-10-13): the Composition blueprint
    /// declares its Location slot for EVERY model, so the only guard that existed answered a question about the
    /// blueprint and a Krea 2 render — which declares <c>["TextOnly","Lora"]</c> and cannot carry a reference image at
    /// all — was handed a <c>NativeMultiReference</c> binding and died at render time with "does not declare support
    /// for 'NativeMultiReference' in Model Manager". The capability question is the strategy resolver's, asked here
    /// rather than re-derived, so the composer's answer and the render's answer cannot disagree.
    /// </para>
    /// </summary>
    public static async Task<LocationBackdropSeed> ResolveAsync(
        IReferenceStrategyResolver strategies,
        string? registeredModelId,
        ImageStepBlueprint blueprint,
        SceneImageLocationBackdrop backdrop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(backdrop);

        if (!HasLocationSlot(blueprint))
        {
            return new LocationBackdropSeed(
                LocationBackdropSeedOutcome.NoLocationSlot,
                null,
                "This step declares no Location slot, so a location backdrop cannot be applied to it.");
        }

        if (string.IsNullOrWhiteSpace(registeredModelId))
        {
            return new LocationBackdropSeed(
                LocationBackdropSeedOutcome.NoModelSelected,
                null,
                "No image model is selected, so the bound backdrop was not applied.");
        }

        var resolution = await strategies.ResolveAsync(
            registeredModelId, ReferenceStrategyCatalogue.NativeMultiReference, cancellationToken);
        if (!resolution.IsAvailable)
        {
            return new LocationBackdropSeed(
                LocationBackdropSeedOutcome.ModelCannotCarry, null, resolution.Reason);
        }

        return new LocationBackdropSeed(
            LocationBackdropSeedOutcome.Seeded, For(blueprint, backdrop), string.Empty);
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
            // The operator's own NAME for the image, carried exactly as the pickers carry it (reported 2026-10-15:
            // the seeded Location showed its image GUID while a hand-picked one showed the name). The backdrop
            // REQUIRES this label, so there is nothing to guess and nothing to look up: dropping it here made the
            // binding degrade to its id in the composer and to the generic role "location continuity" in the prompt
            // block, purely because it was seeded rather than picked.
            ReferenceLabel = backdrop.Label,
            Ordinal = 1
        };
    }
}
