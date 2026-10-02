namespace DreamGenClone.Domain.RolePlay;

/// <summary>What kind of step a composer is presenting. Drives defaults and labels; never behaviour.</summary>
public enum ImageStepKind
{
    Compose = 1,
    IdentityApply = 2,
    PoseRender = 3,
    BodyView = 4,
    LoraCell = 5,
    Edit = 6,
    AssetCreate = 7
}

/// <summary>Where a step's starting image comes from, if it has one.</summary>
public enum ImageStepSourceMode
{
    /// <summary>Text/references only - a generation. No source image.</summary>
    None = 1,

    /// <summary>Start from an already-produced image (a render, an attempt, a composed frame).</summary>
    ProducedImage = 2,

    /// <summary>Start from one attempt the operator selects in the UI.</summary>
    SelectedAttempt = 3
}

/// <summary>Where a step's result is stored. The host decides; the composer never invents one.</summary>
public enum ImageStepPersistenceKind
{
    /// <summary>Nothing is kept (the pose-library test render).</summary>
    Throwaway = 1,

    SceneImage = 2,
    SceneAsset = 3,
    LoraCellAttempt = 4,
    EditRevision = 5
}

/// <summary>
/// What a reference slot supplies. Deliberately NOT the same list as the legacy element keys: this is what the
/// slot MEANS, and the prompt-element plan maps it to the exact payload scopes it replaces.
/// </summary>
public enum ImageStepSlotKind
{
    /// <summary>A character's face. Per-character.</summary>
    Face = 1,

    /// <summary>A character's build / proportions. Per-character.</summary>
    Body = 2,

    /// <summary>A stance, supplied as an OpenPose SKELETON. Scene-level: the skeleton is pose guidance, and a
    /// photoreal person in the slot is reproduced wholesale rather than donating a pose (measured, CASE-03).</summary>
    Pose = 3,

    /// <summary>A room / environment. Scene-level.</summary>
    Location = 4,

    /// <summary>What a character is wearing. Per-character.</summary>
    Wardrobe = 5,

    /// <summary>
    /// A character rendered in a specific pose and wardrobe state - the character pose asset. Per-character, and
    /// it SUBSUMES appearance, clothing and position, because one image carries all three.
    /// </summary>
    CharacterPose = 6
}

/// <summary>Where a slot's reference image may come from. The HOST declares this; it is never inferred.</summary>
public enum ImageStepReferenceSourceKind
{
    /// <summary>An approved <c>SceneAssets</c> image (the only source the legacy bindings ever had).</summary>
    ApprovedSceneAsset = 1,

    /// <summary>An unapproved render produced earlier in this same chain.</summary>
    ScratchImage = 2,

    /// <summary>A skeleton from the pose library.</summary>
    PoseLibrarySkeleton = 3,

    /// <summary>An approved character pose asset (<c>SceneAssetType.CharacterPose</c>).</summary>
    CharacterPoseAsset = 4,

    /// <summary>
    /// An approved image from a character's identity PACK. A distinct source from <see cref="ApprovedSceneAsset"/>,
    /// and not an alias for it: pack images are <c>SceneImageReferenceAsset</c> rows addressed by their own pack id,
    /// which is the id space the LoRA cell's conditioning and the identity renders already use. Folding one into the
    /// other would mean inventing a scene asset id for an image that has none.
    /// </summary>
    IdentityPackAsset = 5
}

/// <summary>How a slot gets its initial value. Pre-fill is a convenience the operator can always change.</summary>
public enum ImageStepSlotPrefill
{
    /// <summary>Starts empty.</summary>
    None = 1,

    /// <summary>The character is in this Moment's visible cast.</summary>
    MomentCast = 2,

    /// <summary>The character's approved pack canonical face.</summary>
    PackCanonicalFace = 3,

    /// <summary>The character's approved pack canonical body, in the state the step's own rule names.</summary>
    PackCanonicalBody = 4,

    /// <summary>The record's own rule (e.g. a LoRA cell's declared reference rule).</summary>
    RecordRule = 5,

    /// <summary>The caller supplies it directly.</summary>
    CallerSupplied = 6
}

/// <summary>
/// Whether a slot must, must not, or may name a character.
/// </summary>
/// <remarks>
/// Three values rather than a boolean because a pose slot is genuinely either: a skeleton can steer one
/// character's stance (<c>character:{key}.position</c>) or the frame as a whole (the Moment's visible action).
/// Collapsing that to "per-character or not" is how the frame-wide case ends up writing the scope
/// <c>character:.position</c>, which addresses nobody.
/// </remarks>
public enum ImageStepActorKeyRequirement
{
    /// <summary>The slot is meaningless without a character (a face, a body, a wardrobe, a character pose).</summary>
    Required = 1,

    /// <summary>The slot addresses the scene and must NOT name a character (a location).</summary>
    Forbidden = 2,

    /// <summary>The slot addresses a character when one is named, and the frame otherwise (a pose skeleton).</summary>
    Optional = 3
}

/// <summary>
/// One character a step's per-character slots can address.
/// </summary>
/// <param name="ActorKey">
/// The character's payload key — the PROFILE KEY the compiled brief uses, not a scenario character id. The
/// prompt-element plan addresses characters by profile key, so an id here would silently remove nothing.
/// </param>
/// <param name="DisplayName">The character's name, as the operator sees it.</param>
public sealed record ImageStepActor(string ActorKey, string DisplayName);

/// <summary>
/// One reference slot a step offers.
/// </summary>
/// <param name="SlotKind">What the slot supplies.</param>
/// <param name="Prefill">How it is pre-filled. Never a lock: the operator can change or clear it.</param>
/// <param name="AllowedSources">
/// Which reference sources this slot may bind. Must be non-empty: a slot with no allowed source is a control the
/// operator can never satisfy, so it is refused rather than rendered dead.
/// </param>
/// <param name="ActorKey">Which character the slot belongs to, where <see cref="ActorKeyRequirementFor"/> allows one.</param>
/// <param name="Required">Whether the step cannot be created without it.</param>
/// <param name="AllowsMultiple">
/// Whether this slot may carry MORE THAN ONE reference image. Defaults to false, and that default is load-bearing:
/// for every other slot a second binding is refused loudly, because a render that used the first and dropped the
/// rest would be invisible in the UI. A wardrobe opts in, because a look genuinely can be two garments (a dress and
/// the shoes that go with it) and each is its own reference image.
/// </param>
/// <param name="ActorDisplayName">
/// The name of <see cref="ActorKey"/>'s character, as the operator sees it — carried here because the composer shows
/// the slot's owner and a slot that renders the raw profile key makes the operator read a GUID to find out whose face
/// they are binding. Supplied by the host through <see cref="ImageStepActor"/>, which has carried the name all along;
/// this field is what stopped it being dropped on the way to the screen. Null falls back to the key.
/// </param>
public sealed record ImageStepSlotBlueprint(
    ImageStepSlotKind SlotKind,
    ImageStepSlotPrefill Prefill,
    IReadOnlyList<ImageStepReferenceSourceKind> AllowedSources,
    string? ActorKey = null,
    bool Required = false,
    bool AllowsMultiple = false,
    string? ActorDisplayName = null)
{
    /// <summary>
    /// Whether this slot names a character, names the scene, or may name either. This is also what decides the
    /// prompt-element scope, so the classification and the prompt mapping cannot drift apart silently.
    /// </summary>
    public static ImageStepActorKeyRequirement ActorKeyRequirementFor(ImageStepSlotKind slotKind) => slotKind switch
    {
        ImageStepSlotKind.Face => ImageStepActorKeyRequirement.Required,
        ImageStepSlotKind.Body => ImageStepActorKeyRequirement.Required,
        ImageStepSlotKind.Wardrobe => ImageStepActorKeyRequirement.Required,
        ImageStepSlotKind.CharacterPose => ImageStepActorKeyRequirement.Required,
        ImageStepSlotKind.Location => ImageStepActorKeyRequirement.Forbidden,
        ImageStepSlotKind.Pose => ImageStepActorKeyRequirement.Optional,
        _ => throw new InvalidOperationException(
            $"Reference slot kind '{slotKind}' has no actor-key classification. Add it deliberately: the answer "
            + "decides whether its prompt elements are addressed per character or scene-wide.")
    };

    /// <summary>
    /// The prose THIS element currently contributes to the step's text, as the host resolves it for this step.
    /// </summary>
    /// <remarks>
    /// It exists so "what does this element actually say?" is answerable ON SCREEN, in the element's own card, rather
    /// than only inside the composed prompt - and so the replacement is visible: once a reference image supplies the
    /// element, that same text is shown struck through, because the image carries it now and the prompt no longer
    /// describes it (D4). Null means the host declares no text for this element, which is a real answer for a slot
    /// whose element the step's text never described.
    /// </remarks>
    public string? ElementText { get; init; }

    /// <summary>
    /// When this slot holds a binding, the element is supplied by THAT slot's reference image rather than by any text
    /// of its own — so it is left out of the prompt and shown as supplied, exactly as if this slot were bound.
    /// </summary>
    /// <remarks>
    /// The LoRA cell is why this exists (2026-09-27). A cell's body reference is always STATE-MATCHED, so a clothed
    /// cell's reference is a clothed full-body image: the garment is IN that image. The prompt was nonetheless also
    /// told to wear the coverage plan's outfit phrase ("wearing a plain t-shirt and jeans"), which the reference then
    /// contradicted — and the wardrobe had no reference of its own, so nothing could express the overlap.
    ///
    /// Declared as DATA rather than decided in the composer, the same way requiredness is: whether one reference image
    /// carries another element is a fact about the host's pipeline, and a component that guessed it would be wrong on
    /// the next host. Null means the ordinary rule — the slot is supplied only by its own binding.
    /// </remarks>
    public ImageStepSlotKind? SuppliedBySlotKind { get; init; }

    public void Validate()
    {
        if (AllowedSources is null || AllowedSources.Count == 0)
        {
            throw new InvalidOperationException(
                $"Reference slot '{SlotKind}'"
                + (string.IsNullOrWhiteSpace(ActorKey) ? string.Empty : $" for actor '{ActorKey}'")
                + " allows no reference source, so the operator could never fill it. Declare at least one source.");
        }

        var requirement = ActorKeyRequirementFor(SlotKind);
        var namesActor = !string.IsNullOrWhiteSpace(ActorKey);
        if (requirement == ImageStepActorKeyRequirement.Required && !namesActor)
        {
            throw new InvalidOperationException(
                $"Reference slot '{SlotKind}' is per-character but names no actor key, so its prompt elements could "
                + "not be addressed. Name the character it belongs to.");
        }

        if (requirement == ImageStepActorKeyRequirement.Forbidden && namesActor)
        {
            throw new InvalidOperationException(
                $"Reference slot '{SlotKind}' is scene-level but names actor '{ActorKey}'. A scene-level slot "
                + "(for example a location) does not belong to one character.");
        }
    }
}

/// <summary>
/// Everything a host must tell the composer to present one image step. The composer holds NO host knowledge of
/// its own: if a behaviour differs between surfaces, it is expressed here rather than in the component.
/// </summary>
/// <param name="StepKind">What kind of step this is (labels and defaults).</param>
/// <param name="Title">The step's title, as the operator sees it.</param>
/// <param name="SourceMode">Where the starting image comes from.</param>
/// <param name="Slots">The reference slots, in the order they are offered.</param>
/// <param name="PersistenceKind">Where the result goes.</param>
/// <param name="AllowsBatch">
/// Whether the host offers a batch. Defaults to false, and that default is load-bearing in both directions: the
/// LoRA studio forbids a sweep ("the set is judged frame by frame"), while the character pose library IS a batch.
/// The component must never impose either.
/// </param>
/// <param name="DefaultModelRole">The configured model role to resolve when the caller pins no model.</param>
public sealed record ImageStepBlueprint(
    ImageStepKind StepKind,
    string Title,
    ImageStepSourceMode SourceMode,
    IReadOnlyList<ImageStepSlotBlueprint> Slots,
    ImageStepPersistenceKind PersistenceKind,
    bool AllowsBatch = false,
    string? DefaultModelRole = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidOperationException("An image step blueprint requires a title.");
        }

        // Two slots of the same kind for the same actor would both write the same prompt scopes, so which one won
        // would depend on iteration order. Refuse it instead of letting the last one silently win.
        var duplicates = Slots
            .GroupBy(slot => (slot.SlotKind, Key: slot.ActorKey ?? string.Empty))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                "An image step blueprint declares the same reference slot more than once: "
                + string.Join(", ", duplicates.Select(duplicate =>
                    string.IsNullOrEmpty(duplicate.Key) ? duplicate.SlotKind.ToString() : $"{duplicate.SlotKind} for '{duplicate.Key}'"))
                + ". Each slot must be declared once, because two slots of one kind would write the same prompt "
                + "elements and which one won would depend on order.");
        }

        foreach (var slot in Slots)
        {
            slot.Validate();
        }
    }
}
