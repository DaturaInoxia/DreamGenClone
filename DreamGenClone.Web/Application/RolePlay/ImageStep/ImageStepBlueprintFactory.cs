using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Builds the blueprint for each surface. This is the ONLY place host knowledge lives.
/// </summary>
/// <remarks>
/// The rule that keeps the composer honest: if <c>ImageStepComposer.razor</c> would need to branch on which surface it
/// is serving, the difference belongs here as data instead. One composer, seven blueprints - rather than seven
/// layouts that slowly disagree about what a reference is.
///
/// Every blueprint is validated before it is returned, so a malformed one fails at the source rather than rendering a
/// control the operator can never satisfy.
/// </remarks>
public static class ImageStepBlueprintFactory
{
    /// <summary>The Production Studio's composition step: a new frame for a Moment with a known cast.</summary>
    public static ImageStepBlueprint ForProductionStudio(IReadOnlyList<ImageStepActor> cast)
    {
        var slots = new List<ImageStepSlotBlueprint>();
        foreach (var actor in RequireCast(cast))
        {
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.MomentCast,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage], actor.ActorKey,
                ActorDisplayName: actor.DisplayName));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.PackCanonicalBody,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], actor.ActorKey, ActorDisplayName: actor.DisplayName));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.RecordRule,
            [ImageStepReferenceSourceKind.ApprovedSceneAsset]));
        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.None,
            [ImageStepReferenceSourceKind.PoseLibrarySkeleton]));

        return Build(new ImageStepBlueprint(
            ImageStepKind.Compose, "Composition", ImageStepSourceMode.None, slots, ImageStepPersistenceKind.SceneImage));
    }

    /// <summary>
    /// The LoRA coverage cell. Face and body are the cell's own rule - and they are PRE-FILLED, not fixed, which is
    /// the difference between this component and the read-only badges the surface shipped with.
    /// </summary>
    /// <param name="elementText">
    /// The prose each element currently contributes, so the step can show it and show it REPLACED once a reference
    /// image supplies the element.
    /// </param>
    /// <param name="faceIsRequired">
    /// Whether this cell shows a face. The render REFUSES a cell that shows a face but carries no face reference, and
    /// refuses a face reference on a cell that shows none, so the step declares the same fact: the control said
    /// "binding one is optional unless marked required" while the render demanded it, which is the mismatch this
    /// parameter removes. A text-only face is deliberately NOT available here - this is a training set - though it is
    /// for other hosts, which is why requiredness is data on the blueprint rather than behaviour in the component.
    /// </param>
    public static ImageStepBlueprint ForLoraCell(
        ImageStepActor actor,
        IReadOnlyDictionary<ImageStepSlotKind, string>? elementText = null,
        bool faceIsRequired = true)
    {
        var cellActor = RequireActor(actor);
        var actorKey = cellActor.ActorKey;
        var actorName = cellActor.DisplayName;

        // The ACCEPTED PACK IS THIS CELL'S FACE AND BODY SOURCE. A pack image is an
        // `SceneImageReferenceAsset` addressed by its own pack id, so it is NOT an approved scene asset, and the cell
        // pre-fills these two slots FROM the pack. Declaring only `ApprovedSceneAsset` here - as this did - meant the
        // pack picker never rendered and the operator had NO CONTROL AT ALL to add the face or body reference
        // (reported live 2026-09-26: "How do I add the Face Reference?").
        ImageStepReferenceSourceKind[] faceAndBodySources =
        [
            ImageStepReferenceSourceKind.ApprovedSceneAsset,
            ImageStepReferenceSourceKind.IdentityPackAsset
        ];

        return Build(new ImageStepBlueprint(
            ImageStepKind.LoraCell,
            "Shoot this cell",
            ImageStepSourceMode.None,
            [
                WithElementText(new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.RecordRule,
                    faceAndBodySources, actorKey, Required: faceIsRequired, ActorDisplayName: actorName), ImageStepSlotKind.Face, elementText),
                // The build is always required: a cell rendered without it is a training image of an unverified body.
                WithElementText(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.RecordRule,
                    faceAndBodySources, actorKey, Required: true, ActorDisplayName: actorName), ImageStepSlotKind.Body, elementText),
                // Wardrobe is NOT required: the render does not demand it, so the step must not either. It is SUPPLIED BY
                // THE BODY REFERENCE (2026-09-27): that reference is state-matched, so a clothed cell's is a clothed
                // full-body image and the garment is in it. Describing an outfit as well made the prompt contradict the
                // image it was conditioned on, so the element is now left out of the prompt and shown as supplied.
                WithElementText(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.RecordRule,
                    [ImageStepReferenceSourceKind.ApprovedSceneAsset], actorKey, AllowsMultiple: true,
                    ActorDisplayName: actorName)
                    { SuppliedBySlotKind = ImageStepSlotKind.Body },
                    ImageStepSlotKind.Wardrobe, elementText),
                // The POSE is bindable, and it COMPOSES with the face and body references (restored 2026-09-27).
                // RenderIdentityConditionedAsync adds the skeleton as one more native reference beside them, and a host
                // proof landed identity, build and pose together on 2026-09-23. NOT required: the cell's pose phrase
                // already describes the pose, so a skeleton pins it rather than being the only statement of it.
                // CallerSupplied because the operator picks it; no actor key, mirroring ForProductionStudio - a pose is
                // not a person's property, and an actor key would re-scope the element's prompt address.
                WithElementText(new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.CallerSupplied,
                    [ImageStepReferenceSourceKind.PoseLibrarySkeleton]), ImageStepSlotKind.Pose, elementText)
            ],
            ImageStepPersistenceKind.LoraCellAttempt,
            // No batch, by operator rule: the training set is judged frame by frame, and a sweep is how a set of
            // near-duplicates gets made. The flag is data because the character pose library DOES batch - the component
            // must not impose either.
            AllowsBatch: false));
    }

    /// <summary>Attaches an element's current text, when the host declares one for it.</summary>
    private static ImageStepSlotBlueprint WithElementText(
        ImageStepSlotBlueprint slot,
        ImageStepSlotKind slotKind,
        IReadOnlyDictionary<ImageStepSlotKind, string>? elementText)
        => elementText is not null
            && elementText.TryGetValue(slotKind, out var text)
            && !string.IsNullOrWhiteSpace(text)
                ? slot with { ElementText = text }
                : slot;

    /// <summary>
    /// The pose library's try-a-pose step. It has no persistence at all today, which is exactly why a render
    /// made here could never become a reference for anything else.
    /// </summary>
    /// <param name="character">
    /// The character the pose is tested ON, or null when the operator has picked none. This is what makes the step
    /// re-form: with a character, the step declares its face and build elements too, because a character HAS them; with
    /// none, it declares only the pose, because there is nothing else to say. A face or build slot on a step with no
    /// character would be a control with nothing behind it.
    /// </param>
    /// <param name="elementText">The prose each element currently contributes, when the host declares one.</param>
    public static ImageStepBlueprint ForPoseLibraryTest(
        ImageStepActor? character = null,
        IReadOnlyDictionary<ImageStepSlotKind, string>? elementText = null)
    {
        var slots = new List<ImageStepSlotBlueprint>();

        // Face and build come from the character's APPROVED PACK, the same source the identity renders use, so what the
        // test conditions on is what a real render would condition on.
        ImageStepReferenceSourceKind[] packSources =
            [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.IdentityPackAsset];

        var resolvedCharacter = character is null ? null : RequireActor(character);
        var actorKey = resolvedCharacter?.ActorKey;
        var actorName = resolvedCharacter?.DisplayName;
        if (actorKey is not null)
        {
            slots.Add(WithElementText(new ImageStepSlotBlueprint(
                ImageStepSlotKind.Face, ImageStepSlotPrefill.None, packSources, actorKey, ActorDisplayName: actorName),
                ImageStepSlotKind.Face, elementText));
            slots.Add(WithElementText(new ImageStepSlotBlueprint(
                ImageStepSlotKind.Body, ImageStepSlotPrefill.None, packSources, actorKey, ActorDisplayName: actorName),
                ImageStepSlotKind.Body, elementText));
            slots.Add(WithElementText(new ImageStepSlotBlueprint(
                ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], actorKey, AllowsMultiple: true,
                ActorDisplayName: actorName),
                ImageStepSlotKind.Wardrobe, elementText));
        }

        slots.Add(WithElementText(new ImageStepSlotBlueprint(
            ImageStepSlotKind.Pose, ImageStepSlotPrefill.CallerSupplied,
            [ImageStepReferenceSourceKind.PoseLibrarySkeleton], Required: true),
            ImageStepSlotKind.Pose, elementText));

        return Build(new ImageStepBlueprint(
            ImageStepKind.PoseRender,
            "Test a library pose",
            ImageStepSourceMode.None,
            slots,
            ImageStepPersistenceKind.Throwaway));
    }

    /// <summary>
    /// The asset creator: a standalone reference image, saved as an asset.
    ///
    /// <paramref name="subject"/> is the character the asset belongs to, when it has one. The surface this replaces
    /// offered face, body, wardrobe and location elements with no owner on any of them; a character element that names
    /// no character is refused by the model (<see cref="ImageStepSlotBlueprint.ActorKeyRequirementFor"/>), so the
    /// per-character slots are declared only when the subject is known rather than being silently dropped. The frame-
    /// wide location slot is always declared.
    /// </summary>
    public static ImageStepBlueprint ForAssetCreate(ImageStepActor? subject = null)
    {
        // The elements the CHARACTER owns are filled from either store: an approved scene asset, or an image out of the
        // character's approved identity PACK. A pack image is a SceneImageReferenceAsset addressed by its own pack id,
        // so it is NOT an approved scene asset and the two are not aliases. Declaring only ApprovedSceneAsset here - as
        // this did - meant the pack picker never rendered, leaving the Face and Body tabs with a single dropdown that
        // lists nothing, while the pack is exactly where the character's approved faces and builds actually live
        // (measured 2026-09-30: two characters hold approved packs carrying 5 faces and up to 12 bodies, while ZERO
        // character-owned face/body scene assets carry an approved usable image). This is the same defect the LoRA cell
        // had - see ForLoraCell.
        ImageStepReferenceSourceKind[] characterSources =
        [
            ImageStepReferenceSourceKind.ApprovedSceneAsset,
            ImageStepReferenceSourceKind.IdentityPackAsset,
            ImageStepReferenceSourceKind.ScratchImage
        ];

        // A wardrobe item and a location are SHARED library entries, never pack images: a pack carries faces and
        // bodies. Offering the pack on either would be a control the operator can never satisfy.
        ImageStepReferenceSourceKind[] sharedSources =
            [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage];

        var slots = new List<ImageStepSlotBlueprint>();
        if (subject is { } actor)
        {
            var resolved = RequireActor(actor);
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.None, characterSources, resolved.ActorKey, ActorDisplayName: resolved.DisplayName));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, characterSources, resolved.ActorKey, ActorDisplayName: resolved.DisplayName));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sharedSources, resolved.ActorKey, AllowsMultiple: true, ActorDisplayName: resolved.DisplayName));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, sharedSources));

        // The pose comes from the pose library's own skeletons, never from an asset: the skeleton IS the reference the
        // model conditions on, and a preset is addressed by ID so a stale path cannot make the render read a different
        // file. Declared frame-wide because a pose is a fact about the frame, not about one character in it.
        //
        // Declared unconditionally, and SHOWN only when the selected model can carry it: the composer hides a slot's
        // tab for a model that can offer it no strategy (D3), because a preset is honoured only on the
        // native-reference route and a pose-conditioned ControlNet model cannot take one at all.
        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.None,
            [ImageStepReferenceSourceKind.PoseLibrarySkeleton]));

        return Build(new ImageStepBlueprint(
            ImageStepKind.AssetCreate, "Generate Image", ImageStepSourceMode.None, slots,
            ImageStepPersistenceKind.SceneAsset));
    }

    /// <summary>A source-image edit. The source is the image being changed, not the subject of the references.</summary>
    public static ImageStepBlueprint ForEdit(IReadOnlyList<ImageStepActor> cast)
    {
        var slots = new List<ImageStepSlotBlueprint>();
        foreach (var actor in RequireCast(cast))
        {
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.MomentCast,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage], actor.ActorKey,
                ActorDisplayName: actor.DisplayName));
        }

        return Build(new ImageStepBlueprint(
            ImageStepKind.Edit, "Describe the change", ImageStepSourceMode.ProducedImage, slots,
            ImageStepPersistenceKind.EditRevision));
    }

    /// <summary>
    /// A Composition whose character identity travels as approved identity PACKS - the pack channel the Composition
    /// Composer already uses, which the render resolves into face references itself.
    ///
    /// No face slot is declared, and that is the point: the pack IS the identity mechanism here, so a face slot would
    /// give each character two mechanisms in one render. What this step adds is the elements the pack cannot carry -
    /// a pack binds FACES only (see how the native-reference identity bindings are written) - addressed per character,
    /// because a body or wardrobe reference must name whose it is, plus the frame-wide location slot.
    /// </summary>
    public static ImageStepBlueprint ForPackIdentityComposition(IReadOnlyList<ImageStepActor>? cast)
    {
        ImageStepReferenceSourceKind[] sources =
            [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage];

        var slots = new List<ImageStepSlotBlueprint>();
        foreach (var actor in cast ?? [])
        {
            if (string.IsNullOrWhiteSpace(actor?.ActorKey))
            {
                continue;
            }

            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, sources, actor.ActorKey, ActorDisplayName: actor.DisplayName));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sources, actor.ActorKey, AllowsMultiple: true, ActorDisplayName: actor.DisplayName));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, sources));

        // The pose is FRAME-WIDE and comes from the pose library's own skeletons, never from an asset: what the render
        // conditions on is an OpenPose skeleton, and a library preset is the artifact the pose proofs used. NOT
        // required - a Composition renders unposed until the operator binds one.
        //
        // Declared here (2026-09-28) because the composition page was the last host that had to offer its own pose
        // picker: the slot is what makes the shared step show a Pose tab, so the page stopped duplicating it.
        slots.Add(new ImageStepSlotBlueprint(
            ImageStepSlotKind.Pose, ImageStepSlotPrefill.CallerSupplied,
            [ImageStepReferenceSourceKind.PoseLibrarySkeleton]));

        return Build(new ImageStepBlueprint(
            ImageStepKind.Compose, "Composition", ImageStepSourceMode.None, slots, ImageStepPersistenceKind.SceneImage));
    }

    /// <summary>
    /// The edit WORKSPACE's step: one body slot and one wardrobe slot per character the operator has identified, plus
    /// the frame-wide location slot.
    ///
    /// The faces are deliberately absent. That workspace picks one face per detected person through its own target flow,
    /// so a face slot here would give each person two mechanisms in one render - the duplicate path the reference rules
    /// forbid. The per-character slots ARE addressed, because the model requires an owner for them: an unaddressed
    /// "body" reference is how a frame-wide scope ends up written as <c>character:.body</c>, which addresses nobody.
    ///
    /// An empty cast is not an error here, unlike <see cref="ForEdit"/>: an edit of an image whose people the operator
    /// has not identified yet still has its frame-wide elements, and refusing the step outright would remove the very
    /// slot that lets them say what the image contains.
    /// </summary>
    public static ImageStepBlueprint ForEditElements(IReadOnlyList<ImageStepActor>? cast)
    {
        ImageStepReferenceSourceKind[] sources =
            [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage];

        var slots = new List<ImageStepSlotBlueprint>();
        foreach (var actor in cast ?? [])
        {
            if (string.IsNullOrWhiteSpace(actor?.ActorKey))
            {
                continue;
            }

            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, sources, actor.ActorKey, ActorDisplayName: actor.DisplayName));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sources, actor.ActorKey, AllowsMultiple: true, ActorDisplayName: actor.DisplayName));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, sources));

        return Build(new ImageStepBlueprint(
            ImageStepKind.Edit, "Describe the change", ImageStepSourceMode.ProducedImage, slots,
            ImageStepPersistenceKind.EditRevision));
    }

    /// <summary>
    /// Building one character's pose library: a pose asset per pose, in a wardrobe state, rendered from that
    /// character's face and body. THIS one batches - the deliberate opposite of the LoRA cell above.
    /// </summary>
    public static ImageStepBlueprint ForCharacterPoseLibrary(ImageStepActor actor)
    {
        var resolved = RequireActor(actor);
        return Build(new ImageStepBlueprint(
            ImageStepKind.AssetCreate,
            $"Build {resolved.DisplayName}'s pose library",
            ImageStepSourceMode.None,
            [
                new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.PackCanonicalFace,
                    [ImageStepReferenceSourceKind.ApprovedSceneAsset], resolved.ActorKey, Required: true, ActorDisplayName: resolved.DisplayName),
                new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.PackCanonicalBody,
                    [ImageStepReferenceSourceKind.ApprovedSceneAsset], resolved.ActorKey, Required: true, ActorDisplayName: resolved.DisplayName),
                new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.CallerSupplied,
                    [ImageStepReferenceSourceKind.PoseLibrarySkeleton], Required: true)
            ],
            ImageStepPersistenceKind.SceneAsset,
            AllowsBatch: true));
    }

    private static IReadOnlyList<ImageStepActor> RequireCast(IReadOnlyList<ImageStepActor> cast)
    {
        if (cast is null || cast.Count == 0)
        {
            throw new InvalidOperationException(
                "A multi-character step needs the Moment's cast: each per-character reference slot must name the "
                + "character it addresses, and the prompt plan writes its elements under that character's profile key.");
        }

        foreach (var actor in cast)
        {
            RequireActor(actor);
        }

        return cast;
    }

    private static ImageStepActor RequireActor(ImageStepActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.ActorKey))
        {
            throw new InvalidOperationException(
                $"Character '{actor.DisplayName}' has no profile key, so its reference slots could not be mapped to "
                + "prompt elements. Supply the profile key the compiled brief uses, not a scenario character id.");
        }

        return actor;
    }

    private static ImageStepBlueprint Build(ImageStepBlueprint blueprint)
    {
        blueprint.Validate();
        return blueprint;
    }
}
