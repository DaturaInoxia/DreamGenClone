using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns an identity pack into the references a step starts with.
///
/// <para>
/// The pack is the SOURCE; the step's slots are the mechanism. A host that knows the character picks the pack and asks
/// for the shape the step needs - <b>face only, body only, or face and body</b> - and this fills exactly the slots the
/// blueprint declares, addressed to that character. Nothing is invented: a requested element with no declared slot is a
/// host bug and is refused by name rather than dropped, because a dropped reference is invisible and the render would
/// simply not be the character.
/// </para>
///
/// <para>
/// Face and body are independent on purpose. A close-up carries a face and no build; a build reference on its own is a
/// legitimate step ("just body"); a full frame carries both. Both are matched to the angle the step depicts, and the
/// body to the wardrobe STATE it depicts, through <see cref="IdentityPackReferenceResolver"/> - the same decision a
/// render makes, so what the operator sees on the step is what conditions the image.
/// </para>
/// </summary>
public static class IdentityPackSlotPrefill
{
    /// <summary>
    /// What the host asks for. The angle values are the step's own: a LoRA cell names its cell's angle and state, a
    /// canonical compose step names the frontal face and the front body.
    /// </summary>
    /// <param name="Actor">The character the pack and the slots belong to.</param>
    /// <param name="FaceView">Which face angle the step depicts.</param>
    /// <param name="BodyView">Which body angle the step depicts.</param>
    /// <param name="BodyState">Which wardrobe state the step depicts. Never inferred.</param>
    /// <param name="IncludeFace">Whether this step carries a face reference at all.</param>
    /// <param name="IncludeBody">Whether this step carries a build reference at all.</param>
    public sealed record Request(
        ImageStepActor Actor,
        SceneImageReferenceFaceView FaceView,
        SceneImageReferenceBodyView BodyView,
        SceneImageReferenceBodyState BodyState,
        bool IncludeFace,
        bool IncludeBody)
    {
        /// <summary>A face reference and nothing else - a close-up that carries no build.</summary>
        public static Request FaceOnly(ImageStepActor actor, SceneImageReferenceFaceView faceView) =>
            new(actor, faceView, SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed,
                IncludeFace: true, IncludeBody: false);

        /// <summary>A build reference and nothing else - a step whose business is the body.</summary>
        public static Request BodyOnly(
            ImageStepActor actor,
            SceneImageReferenceBodyView bodyView,
            SceneImageReferenceBodyState bodyState) =>
            new(actor, SceneImageReferenceFaceView.Front, bodyView, bodyState,
                IncludeFace: false, IncludeBody: true);

        /// <summary>Both, which is what a full-frame step carries.</summary>
        public static Request FaceAndBody(
            ImageStepActor actor,
            SceneImageReferenceFaceView faceView,
            SceneImageReferenceBodyView bodyView,
            SceneImageReferenceBodyState bodyState) =>
            new(actor, faceView, bodyView, bodyState, IncludeFace: true, IncludeBody: true);
    }

    /// <summary>
    /// The bindings the step starts with, in frame order: the face first, because the first reference anchors the frame,
    /// then the build.
    /// </summary>
    public static IReadOnlyList<ReferenceApplicationSelection> For(
        ImageStepBlueprint blueprint,
        string identityPackId,
        IReadOnlyList<SceneImageReferenceAsset> packAssets,
        Request request)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(packAssets);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(identityPackId))
        {
            throw new InvalidOperationException(
                "A step cannot be pre-filled from an identity pack without naming one: the pack is what the references "
                + "come from, and there is nothing to choose between without it.");
        }

        var packId = identityPackId.Trim();
        var bindings = new List<ReferenceApplicationSelection>();

        if (request.IncludeFace)
        {
            RequireDeclaredSlot(blueprint, ImageStepSlotKind.Face, request.Actor, "face");
            var face = IdentityPackReferenceResolver.ResolveFace(packAssets, request.FaceView)
                ?? throw new InvalidOperationException(
                    $"Identity pack '{packId}' has no approved {request.FaceView} face reference, so a step at that angle "
                    + "cannot be rendered as this character. Shoot or approve that view on the Faces tab first.");
            bindings.Add(BindingFor(request.Actor, ImageStepSlotKind.Face, "character identity", packId, face.Id,
                bindings.Count + 1));
        }

        if (request.IncludeBody)
        {
            RequireDeclaredSlot(blueprint, ImageStepSlotKind.Body, request.Actor, "build");
            var body = IdentityPackReferenceResolver.ResolveBody(packAssets, request.BodyView, request.BodyState)
                ?? throw new InvalidOperationException(
                    $"Identity pack '{packId}' has no approved {request.BodyView}/{request.BodyState} body reference, so "
                    + "this step cannot be rendered on the character's build. Shoot or approve that body slot in that "
                    + "state on the Body tab first.");
            bindings.Add(BindingFor(request.Actor, ImageStepSlotKind.Body, "character body", packId, body.Id,
                bindings.Count + 1));
        }

        if (bindings.Count == 0)
        {
            throw new InvalidOperationException(
                "A step pre-fill was asked for neither a face nor a build reference, so it would produce no bindings at "
                + "all. Ask for the shape the step actually carries.");
        }

        return bindings;
    }

    /// <summary>
    /// A requested element the blueprint does not declare is refused rather than dropped: the planner drops undeclared
    /// assignments, so the reference would vanish silently and the render would not be the character.
    /// </summary>
    private static void RequireDeclaredSlot(
        ImageStepBlueprint blueprint, ImageStepSlotKind slotKind, ImageStepActor actor, string element)
    {
        var declared = blueprint.Slots.Any(slot =>
            slot.SlotKind == slotKind
            && string.Equals(slot.ActorKey ?? string.Empty, actor.ActorKey, StringComparison.Ordinal));

        if (!declared)
        {
            throw new InvalidOperationException(
                $"This step declares no {slotKind} slot for character '{actor.ActorKey}', so its {element} reference "
                + $"cannot be pre-filled. Add the slot to the blueprint the factory builds for this surface.");
        }
    }

    private static ReferenceApplicationSelection BindingFor(
        ImageStepActor actor, ImageStepSlotKind slotKind, string semanticRole, string packId, string assetId, int ordinal) =>
        new()
        {
            ElementKey = ReferenceStrategyCatalogue.ElementKeyForSlot(slotKind),
            SemanticRole = semanticRole,
            Kind = slotKind.ToString(),
            ActorKey = actor.ActorKey,
            Source = ImageStepReferenceSourceKind.IdentityPackAsset.ToString(),
            // The binding says WHICH image; the render path still resolves HOW the selected model carries it.
            Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
            IdentityPackId = packId,
            ReferenceAssetId = assetId,
            Ordinal = ordinal
        };
}
