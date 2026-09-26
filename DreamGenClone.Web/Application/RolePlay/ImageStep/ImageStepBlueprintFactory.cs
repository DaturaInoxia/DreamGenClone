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
                [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage], actor.ActorKey));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.PackCanonicalBody,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], actor.ActorKey));
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
    public static ImageStepBlueprint ForLoraCell(ImageStepActor actor) => Build(new ImageStepBlueprint(
        ImageStepKind.LoraCell,
        "Shoot this cell",
        ImageStepSourceMode.None,
        [
            new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.RecordRule,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], RequireActor(actor).ActorKey),
            new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.RecordRule,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], RequireActor(actor).ActorKey),
            new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.RecordRule,
                [ImageStepReferenceSourceKind.ApprovedSceneAsset], RequireActor(actor).ActorKey)
        ],
        ImageStepPersistenceKind.LoraCellAttempt,
        // No batch, by operator rule: the training set is judged frame by frame, and a sweep is how a set of
        // near-duplicates gets made. The flag is data because the character pose library DOES batch - the component
        // must not impose either.
        AllowsBatch: false));

    /// <summary>The pose library's try-a-pose step. It has no persistence at all today, which is exactly why a render
    /// made here could never become a reference for anything else.</summary>
    public static ImageStepBlueprint ForPoseLibraryTest() => Build(new ImageStepBlueprint(
        ImageStepKind.PoseRender,
        "Test a library pose",
        ImageStepSourceMode.None,
        [
            new ImageStepSlotBlueprint(ImageStepSlotKind.Pose, ImageStepSlotPrefill.CallerSupplied,
                [ImageStepReferenceSourceKind.PoseLibrarySkeleton], Required: true)
        ],
        ImageStepPersistenceKind.Throwaway));

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
        ImageStepReferenceSourceKind[] sources =
            [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage];

        var slots = new List<ImageStepSlotBlueprint>();
        if (subject is { } actor)
        {
            var resolved = RequireActor(actor);
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Face, ImageStepSlotPrefill.None, sources, resolved.ActorKey));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, sources, resolved.ActorKey));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sources, resolved.ActorKey));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, sources));

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
                [ImageStepReferenceSourceKind.ApprovedSceneAsset, ImageStepReferenceSourceKind.ScratchImage], actor.ActorKey));
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

            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, sources, actor.ActorKey));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sources, actor.ActorKey));
        }

        slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Location, ImageStepSlotPrefill.None, sources));

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

            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.None, sources, actor.ActorKey));
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Wardrobe, ImageStepSlotPrefill.None, sources, actor.ActorKey));
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
                    [ImageStepReferenceSourceKind.ApprovedSceneAsset], resolved.ActorKey, Required: true),
                new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.PackCanonicalBody,
                    [ImageStepReferenceSourceKind.ApprovedSceneAsset], resolved.ActorKey, Required: true),
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
