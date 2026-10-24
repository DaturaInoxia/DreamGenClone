using System.Text;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns the reference images a render will send into the sentence that says WHAT EACH IMAGE IS.
///
/// <para>
/// This is the other half of <see cref="ReferenceBindingPromptRemoval"/>. Removal stops the prompt describing an
/// element an image already supplies; it says nothing about which image supplies it. That is sufficient for ONE
/// reference — the binding is unambiguous and Qwen's own rule is the opposite (for a single image input the prompt
/// must NOT use tags) — but for two or more it leaves the model to guess, and it guesses wrong: a studio-lit identity
/// plate reads as the subject of the picture and a dim, cluttered room reference loses.
/// </para>
///
/// <para>
/// Measured 2026-10-03 on the app's own three reference files, one pinned seed and envelope, only the prompt differing:
/// the same two references scored outer-ring histogram L1 1.574 against the bound shed with no role text and 0.887
/// with it — better than the shed-alone control (0.936) — with mean brightness landing on the shed (61.8 vs 68.5)
/// where the tagless prompt had drifted to 79.9. Naming the references is not cosmetic.
/// </para>
///
/// <para>
/// The tag format is Qwen's own requirement, not this codebase's convention: <c>QwenLM/Qwen-Image-2.1</c>
/// <c>prompt_rewrite/prompts/system_prompt_edit.txt</c> — "For Multi-Image Input (N &gt;= 2), the rewritten
/// instruction MUST use <c>&lt;image1&gt;</c>, <c>&lt;image2&gt;</c>, ... to refer to each input image... This tagging
/// format is mandatory and non-negotiable. For single-image input (N = 1), do NOT use tags." The same file requires
/// each image's ROLE to be stated, which is the rest of this text. ComfyUI's 2.1 encoder passes the prompt through
/// untouched, so nothing else supplies those indices.
/// </para>
///
/// <para>
/// The numbering follows the list it is given, so a caller must hand it images IN SEND ORDER. The render does that by
/// building the two lists together — one role entry per image actually added — instead of deriving one from the other
/// afterwards: a numbering derived from a second source is the one thing that can silently re-point every tag.
/// </para>
/// </summary>
public static class ReferenceRoleClauses
{
    /// <summary>
    /// One reference image that is going to be sent, described by what it IS: the slot kind decides the role
    /// sentence, and the label (the operator's own name for the image) only helps tell two of a kind apart.
    /// </summary>
    /// <param name="SubjectKey">
    /// The participant profile key this reference belongs to, or null for a scene-level slot (a location, a pose).
    ///
    /// <para>
    /// This is the SAME key the compiled brief uses — <c>typedReferences[].subjectKey</c> and the frozen-state
    /// contract's <c>profileKey</c> — which is what lets the clause join a reference to a placed subject. The join is
    /// verified, not assumed: <c>ImageStepBlueprintFactory.RequireActor</c> refuses any actor key that is not "the
    /// profile key the compiled brief uses, not a scenario character id", and
    /// <c>ReferenceSlotPlanner.Plan</c> carries that key straight onto the binding's <c>ActorKey</c>.
    /// </para>
    ///
    /// <para>
    /// A view label alone cannot disambiguate two characters: both packs' fronts are labelled "Front · Clothed", so
    /// without this key the clause names two references identically and the model resolves the collision by keeping
    /// whichever face it noticed first.
    /// </para>
    /// </param>
    /// <param name="ContainerKey">
    /// What this image is a view OF — the asset container it lives in, or null when the caller does not know.
    ///
    /// <para>
    /// It answers one question the clause cannot answer any other way: are several LOCATION references several views of
    /// ONE place, or several different places standing in one frame? Two views of the same shed share a container and
    /// must keep being described as the same room from another angle; two different trailers do not, and describing
    /// each as the whole room — each with its "do not replace it with the setting of any other reference image"
    /// exclusivity — tells the model to render mutually exclusive frames (found 2026-10-12).
    /// </para>
    ///
    /// <para>
    /// Null is deliberately treated as "the same place": an unknown container must never be the evidence that turns a
    /// single location into several structures.
    /// </para>
    /// </param>
    public readonly record struct ReferenceRole(
        ImageStepSlotKind? SlotKind,
        string? Label,
        string? SubjectKey = null,
        string? ContainerKey = null,
        ReferenceDirections? Directions = null);

    /// <summary>The tag Qwen's multi-image rule requires for the reference at position <paramref name="index"/>.</summary>
    /// <remarks>
    /// The INDEX is a position in the images the model receives, not a position in the reference list. On an edit the
    /// source image occupies slot 1, so the first reference is <c>&lt;image2&gt;</c> — see
    /// <see cref="LeadingImageCount.Edit"/>. That offset is stated once, here, because getting it wrong re-points
    /// every tag in the clause without failing anything.
    /// </remarks>
    public static string TagFor(int index) => $"<image{index}>";

    /// <summary>
    /// The label the reference map shows for an image that is sent WITHOUT a number, because the vendor forbids tags
    /// when a single image is supplied.
    /// </summary>
    /// <remarks>
    /// A map row must still say what that image is; showing <c>&lt;image1&gt;</c> there would teach the operator a tag
    /// the model never receives, which is the mistake this label exists to prevent.
    /// </remarks>
    public const string UntaggedImageLabel = "(sent untagged)";

    /// <summary>
    /// The sentence that keeps a preservation clause from overriding the instruction. The reference block is appended
    /// AFTER the instruction, so it is the last thing the image model reads; without this, "keep the face exactly"
    /// beats "her face can not be seen" and the render comes back as a hybrid (SceneImage f6bab9b3, 2026-10-18).
    /// </summary>
    private const string InstructionPrecedence =
        "Where the instruction explicitly changes that person's view, pose, stance or clothing, follow the instruction "
        + "for that change; everything it does not name stays exactly as the reference shows it.";

    /// <summary>
    /// <see cref="InstructionPrecedence"/>, ONLY where there is an instruction for it to defer to.
    /// </summary>
    /// <remarks>
    /// A render's caption IS the prompt: nothing is riding above it that a preservation clause could override, so the
    /// sentence told the image model to obey an instruction that does not exist — and it read as edit wording in a
    /// generate prompt (operator report 2026-10-23). An edit is the path that carries one, and it is also the path whose
    /// source image occupies slot 1, which is exactly what <paramref name="leadingImages"/> distinguishes here.
    /// </remarks>
    private static string PrecedenceFor(int leadingImages)
        => leadingImages > 0 ? InstructionPrecedence : string.Empty;

    /// <summary>
    /// Whether this many REFERENCE images are NUMBERED with <c>&lt;imageN&gt;</c> on a surface that sends
    /// <paramref name="leadingImages"/> images before them.
    ///
    /// <para>
    /// THE one place this rule is decided. The vendor's own requirement is "For Multi-Image Input (N &gt;= 2) ... this
    /// tagging format is mandatory ... For single-image input (N = 1), do NOT use tags", and the rule used to be
    /// re-derived as a bare count comparison in six places — which is how "may not be numbered" silently became "may
    /// not be described at all", leaving a step with one reference (a location-only composition is the reported case,
    /// 2026-10-13) unable to state anything about the image it sends. Numbering and describing are two decisions and
    /// only the first is the vendor's.
    /// </para>
    ///
    /// <para>
    /// <b>The count is the IMAGES THE MODEL RECEIVES, not the references.</b> An edit sends its source image first, so
    /// one reference beside it is already TWO images and the vendor rule already bites — yet counting references alone
    /// meant a one-reference edit emitted no tag at all, leaving a face reference that the prompt never named (reported
    /// live 2026-10-15: a bound pack face was composited into the frame as a second face). <see cref="TagFor"/>'s own
    /// numbering always followed the model's slots; the threshold now does too. A render passes no leading images, so
    /// every generate path is byte-identical to before.
    /// </para>
    /// </summary>
    public static bool SendsTags(int referenceCount, int leadingImages = LeadingImageCount.Generate)
        => referenceCount + leadingImages >= 2;

    /// <summary>
    /// How many of these references a job may record as TAGGED — the same rule as <see cref="SendsTags"/>, said as the
    /// number the render's own telemetry carries: the references themselves when they are numbered, and none when they
    /// are not. A handler that compared the counts itself would be a second copy of the tag rule.
    /// </summary>
    public static int TaggedReferenceCount(int referenceCount, int leadingImages = LeadingImageCount.Generate)
        => SendsTags(referenceCount, leadingImages) ? referenceCount : 0;

    /// <summary>
    /// Whether this reference has something STATED about it — the operator's placement or note, or a location
    /// analysis's reading.
    /// </summary>
    /// <remarks>
    /// This is what a single, unnumbered reference needs in order to be described at all: with two or more images the
    /// role sentence is worth saying on its own, but with one image the reference IS the whole request, and adding an
    /// automatic sentence nobody asked for would change every single-reference render in the app. A statement is the
    /// evidence that a human (or an analysis they triggered) had something to say about this image, so the untagged
    /// clause is emitted only then.
    /// </remarks>
    public static bool CarriesStatement(ReferenceRole role) => role.Directions is { IsEmpty: false };

    /// <summary>
    /// The <c>&lt;imageN&gt;</c> tag each reference will actually be SENT as, keyed by the binding instance — the map a
    /// surface needs to tell the operator "this is image 2" at the point where they bind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives here because the NUMBERING lives here: a host that counted the bindings itself would be a second rule
    /// for the same question, and the two would drift the moment the send order or the tag rule changed. The order
    /// comes from <see cref="ReferenceBindingShape.InSendOrder"/> and the tags from
    /// <see cref="TagFor"/> — the same two inputs <see cref="BlockFor"/> uses, so what the operator is shown cannot
    /// disagree with what the model receives.
    /// </para>
    ///
    /// <para>
    /// EMPTY when fewer than two images are sent IN TOTAL — the references plus any <paramref name="leadingImages"/>
    /// the surface sends ahead of them — because tags are forbidden for a single image (Qwen's own rule). Showing a
    /// number the model never receives would be teaching the operator a tag that does not exist. An unnumbered
    /// reference is still DESCRIBED when something is stated about it — see <see cref="BlockFor"/>: the tag and the
    /// description are separate decisions.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<ReferenceApplicationSelection, string> TagsFor(
        IReadOnlyList<ReferenceApplicationSelection>? bindingsInSendOrder,
        int leadingImages = LeadingImageCount.Generate)
    {
        var numbered = new Dictionary<ReferenceApplicationSelection, string>();
        if (bindingsInSendOrder is not { Count: > 0 })
        {
            return numbered;
        }

        if (leadingImages < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadingImages), leadingImages,
                "A negative leading-image count would number the first reference before <image1>.");
        }

        if (!SendsTags(RolesFor(bindingsInSendOrder).Count, leadingImages))
        {
            return numbered;
        }

        var index = 0;
        foreach (var binding in ReferenceBindingShape.InSendOrder(bindingsInSendOrder))
        {
            // The SAME filter the clause numbers by, so a text-only binding cannot shift every tag after it onto an
            // image that is not there.
            if (!binding.SuppliesImage
                || string.Equals(binding.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            numbered[binding] = TagFor(index + 1 + leadingImages);
            index++;
        }

        return numbered;
    }

    /// <summary>
    /// The images that precede the references in the model's own numbering, per surface.
    ///
    /// <para>
    /// A generation sends only its references, so the first reference is image 1. An EDIT sends the source image
    /// first — the 2.1 edit graph wires it to <c>images.image_1</c> and reference *i* to <c>image_{i+2}</c> — so the
    /// first reference is image 2. The tags must follow the model's numbering, not the reference list's.
    /// </para>
    /// </summary>
    public static class LeadingImageCount
    {
        /// <summary>Prompt-to-image: nothing precedes the references.</summary>
        public const int Generate = 0;

        /// <summary>Source-image edit: the source image is slot 1.</summary>
        public const int Edit = 1;
    }

    /// <summary>
    /// The roles for a set of bindings, in the order the render sends them. A binding that carries no image (a
    /// <c>TextOnly</c> strategy) is not a reference and is never numbered — numbering it would shift every tag after it
    /// onto the wrong image.
    /// </summary>
    /// <remarks>
    /// The list is returned for ONE reference too. Whether that reference may be TAGGED is a different question, owned
    /// by <see cref="SendsTags"/>: collapsing this list to nothing below two images is what made "no tags" mean "no
    /// description at all" (see <see cref="SendsTags"/>), and a caller that needs the numbered roles asks for the tag
    /// rule rather than for a shorter list.
    /// </remarks>
    public static IReadOnlyList<ReferenceRole> RolesFor(
        IReadOnlyList<ReferenceApplicationSelection>? bindingsInSendOrder)
    {
        if (bindingsInSendOrder is not { Count: > 0 })
        {
            return [];
        }

        var roles = new List<ReferenceRole>(bindingsInSendOrder.Count);
        foreach (var binding in ReferenceBindingShape.InSendOrder(bindingsInSendOrder))
        {
            if (!binding.SuppliesImage
                || string.Equals(binding.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            roles.Add(new ReferenceRole(
                ReferenceBindingShape.SlotKindOf(binding),
                LabelFor(binding),
                SubjectKeyOf(binding),
                ContainerKeyOf(binding),
                binding.Directions));
        }

        return roles;
    }

    /// <summary>
    /// The numbered block, for images ALREADY in send order. A reference is TAGGED only when the model receives at
    /// least TWO images in total — the references plus <paramref name="leadingImages"/> — which is why one reference on
    /// an EDIT is numbered (<c>&lt;image2&gt;</c>, beside the source in slot 1) while one reference on a RENDER is not;
    /// an untagged one is described only when something is STATED about it, in the same words minus the number — see
    /// <see cref="UntaggedStatementBlock"/>.
    /// </summary>
    /// <param name="leadingImages">
    /// How many images the model receives BEFORE these references, which decides the first tag —
    /// <see cref="LeadingImageCount.Generate"/> for a render, <see cref="LeadingImageCount.Edit"/> for an edit whose
    /// source image is slot 1.
    /// </param>
    /// <param name="blocking">
    /// Where each subject sits in the frame (B-157), or null.
    ///
    /// <para>
    /// When supplied, a reference that carries a <see cref="ReferenceRole.SubjectKey"/> naming one of the blocking
    /// subjects is described as that PERSON in that POSITION ("&lt;image1&gt; is the person on the LEFT"), instead of
    /// by its pack view label. When null the block is byte-identical to what this class has always emitted — the
    /// framing is additive, so a caller that has not derived one cannot accidentally change a render.
    /// </para>
    ///
    /// <para>
    /// A scene-level reference (a location, a pose) carries no subject key and keeps its existing sentence either way:
    /// it is not a person, and describing a room as "the person on the RIGHT" is the kind of confident nonsense that
    /// makes an entire clause untrustworthy.
    /// </para>
    /// </param>
    public static string BlockFor(
        IReadOnlyList<ReferenceRole>? rolesInSendOrder,
        int leadingImages = LeadingImageCount.Generate,
        SceneBlocking? blocking = null)
    {
        if (rolesInSendOrder is not { Count: > 0 })
        {
            return string.Empty;
        }

        if (leadingImages < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadingImages), leadingImages,
                "A negative leading-image count would number the first reference before <image1>.");
        }

        if (!SendsTags(rolesInSendOrder.Count, leadingImages))
        {
            // The model receives ONE image in total, so no tag is allowed. It is described only when something is
            // stated about it.
            return UntaggedStatementBlock(rolesInSendOrder[0], blocking, leadingImages);
        }

        var introducers = IntroducerIndexes(rolesInSendOrder);
        var severalPlaces = CarriesSeveralPlaces(rolesInSendOrder);
        var sb = new StringBuilder();
        sb.AppendLine(
            "REFERENCE IMAGES — AUTHORITATIVE (each is an input to this render, numbered in the order sent; there are "
            + $"{rolesInSendOrder.Count}):");
        for (var index = 0; index < rolesInSendOrder.Count; index++)
        {
            sb.AppendLine(LineFor(index + 1 + leadingImages, rolesInSendOrder[index], blocking, introducers.Contains(index), severalPlaces));
        }

        // A SINGLE place needs no extra line: its role clause already says the image IS the room and the shot happens
        // inside it. The old sentence here ("Name the setting only as its kind of place … do not describe its
        // structure, surfaces, fixtures or objects") was an instruction to the CAPTION WRITER and had no meaning for an
        // image model — it was building advice leaking into the picture prompt (operator, 2026-10-22).
        if (severalPlaces)
        {
            sb.AppendLine(
                "Every place named above stands in the SAME frame: they are separate places in one shot, and the "
                + "instruction says how they sit relative to each other. All of them must be visible in the result. "
                + "Do not merge them into one building and do not render only one of them.");
        }

        // One BLANKET preservation clause, naming the invariant CATEGORIES and supplying no values. Qwen's own worked
        // multi-reference example does exactly this ("keep the two people's facial features, hairstyle and clothing
        // appearance completely unchanged") and its prompt-enhancer states why the categories and not the values:
        // "the more concretely you describe something you meant to keep, the more likely it drifts". The appearance
        // prose a reference supplies is REMOVED from the brief for the same reason, so a concrete value here would
        // contradict that removal and reintroduce the duplicate description it exists to prevent (B-157, 2026-10-12).
        //
        // It DEFERS to the instruction. This block is appended after the instruction, so it is the last thing the image
        // model reads, and an absolute "keep the face exactly" overrode an edit whose whole point was to turn the
        // subject away: SceneImage f6bab9b3 (2026-10-18) asked for "her back and ass facing the camera, her face can
        // not be seen", and the render kept a front-facing head and chest on rotated hips — a hybrid that follows the
        // preservation clause and ignores the change. The instruction is the operator's request; preservation names
        // what the request does NOT touch.
        if (rolesInSendOrder.Any(role => role.SlotKind is ImageStepSlotKind.Face
            or ImageStepSlotKind.Body
            or ImageStepSlotKind.Wardrobe
            or ImageStepSlotKind.CharacterPose))
        {
            // A WARDROBE reference OWNS the clothing. Bundling clothing into the "reference that carries the person"
            // sentence handed it back to the body plate, which is itself a clothed photograph — measured 2026-10-17 on
            // SceneImages 9b36a8c7: a 'Jean Shorts' wardrobe binding rendered the body plate's T-shirt and jeans.
            var wardrobeBound = rolesInSendOrder.Any(role => role.SlotKind == ImageStepSlotKind.Wardrobe);
            sb.AppendLine(wardrobeBound
                ? "Keep each person's face, hair, skin tone and build exactly as the reference image that carries them "
                    + "shows it, and keep the clothing exactly as the WARDROBE reference shows it — the garment in the "
                    + "person reference is not the outfit in this frame and must not be carried over. "
                    + PrecedenceFor(leadingImages) + " They are not described in words anywhere here, and nothing may be "
                    + "re-derived from another description."
                : "Keep each person's face, hair, skin tone, build and clothing exactly as the reference image that "
                    + "carries them shows it. " + PrecedenceFor(leadingImages) + " They are not described in words anywhere "
                    + "here, and nothing may be re-derived from another description.");
        }

        sb.AppendLine("Take each <imageN> as that image, not as a description of it.");
        return sb.ToString();
    }

    /// <summary>
    /// Appends <see cref="BlockFor"/> to a finished image prompt, returning it unchanged when the block is empty — no
    /// references at all, or a single one about which nothing is stated.
    /// </summary>
    /// <param name="leadingImages">See <see cref="BlockFor"/>: the source image of an edit counts as 1.</param>
    public static string AppendToImagePrompt(
        string imagePrompt,
        IReadOnlyList<ReferenceRole>? rolesInSendOrder,
        int leadingImages = LeadingImageCount.Generate,
        SceneBlocking? blocking = null)
    {
        ArgumentNullException.ThrowIfNull(imagePrompt);

        var block = BlockFor(rolesInSendOrder, leadingImages, blocking);
        if (block.Length == 0)
        {
            return imagePrompt;
        }

        // A LOCATION reference anchors the FRAME, and it has to be said EARLY.
        //
        // The block below already states "this image IS the room", and it was not enough. Measured on SceneImage
        // 1cc55900 (2026-10-18): a User Intent draft under the action-only contract compiled to 613 characters of pure
        // body action — correct, it named no setting because a reference owns it — and the render came back on the
        // identity plate's plain grey STUDIO backdrop with the location image ignored, even though it was sent as
        // <image3> and named in the block as the room. A full-figure person reference is reproduced wholesale, so with
        // nothing early in the prompt placing her anywhere, the person plate's own backdrop won. The two renders that
        // worked (b91598c2, 87454bef) both happened to mention the surroundings in the prose.
        //
        // So the anchor is written HERE, deterministically, instead of hoping the compiler writes one: no invented
        // detail, just the assignment to the tag the block already numbers. Generation only — an EDIT starts from a
        // source image which already carries its own setting, and prepending this there would contradict the frame
        // being edited.
        var anchor = LocationAnchorFor(rolesInSendOrder, leadingImages);
        return anchor is null
            ? $"{imagePrompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{block}"
            : $"{anchor}{Environment.NewLine}{Environment.NewLine}{imagePrompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{block}";
    }

    /// <summary>
    /// The sentence that places the frame in the LOCATION reference, or null when this prompt has no location image or
    /// the surface is an edit.
    /// </summary>
    private static string? LocationAnchorFor(
        IReadOnlyList<ReferenceRole>? rolesInSendOrder,
        int leadingImages)
    {
        if (leadingImages != LeadingImageCount.Generate || rolesInSendOrder is not { Count: > 0 })
        {
            return null;
        }

        var index = -1;
        for (var position = 0; position < rolesInSendOrder.Count; position++)
        {
            if (rolesInSendOrder[position].SlotKind == ImageStepSlotKind.Location)
            {
                index = position;
                break;
            }
        }

        if (index < 0 || !SendsTags(rolesInSendOrder.Count, leadingImages))
        {
            return null;
        }

        // The same numbering BlockFor uses, so the anchor and the block cannot name different images.
        var tag = TagFor(index + 1 + leadingImages);
        return $"The setting of this picture is the place shown in {tag}: that place supplies the background of the "
            + "frame, and no person reference supplies the background.";
    }

    /// <summary>
    /// Appends the role block to an EDIT instruction, numbering from the image AFTER the source. Returns the
    /// instruction unchanged when the block is empty — no references, or a single one about which nothing is stated.
    ///
    /// <para>
    /// The edit path has no binding-derived removals to apply — an edit's instruction is an operator-authored intent,
    /// not a brief-driven image prompt, so there is no element vocabulary to remove from. The role clause is the half
    /// that does apply, and it is the half that carries the <c>&lt;imageN&gt;</c> tags the 2.1 graph requires once
    /// two or more reference images ride beside the source.
    /// </para>
    /// </summary>
    public static string AppendToEditInstruction(
        string instruction,
        IReadOnlyList<(ImageStepSlotKind? SlotKind, string Description, string? ContainerKey, ReferenceDirections? Directions)> referencesInSendOrder)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(referencesInSendOrder);

        return AppendToImagePrompt(
            instruction,
            [.. referencesInSendOrder.Select(reference => new ReferenceRole(
                reference.SlotKind, reference.Description, ContainerKey: reference.ContainerKey,
                Directions: reference.Directions))],
            LeadingImageCount.Edit);
    }

    /// <summary>
    /// The clause for the message the image-prompt PRE-PROCESSOR receives, so the prompt it writes can name the
    /// references instead of describing them for the first time. A single reference is described without a tag, and
    /// only when something is stated about it; nothing is emitted otherwise.
    /// </summary>
    /// <param name="blocking">
    /// The brief's frame-space blocking, or null (B-157 T5). Supplying it makes the clause the pre-processor reads use
    /// the SAME placement vocabulary as the block appended to the finished prompt, so the prose and the role clause
    /// cannot describe the frame differently — the brief's snapshot already carries the blocking, so this is the same
    /// value, not a second derivation.
    /// </param>
    public static string ForPreprocessor(
        IReadOnlyList<ReferenceApplicationSelection>? bindingsInSendOrder,
        SceneBlocking? blocking = null)
    {
        var roles = RolesFor(bindingsInSendOrder);
        if (roles.Count == 0)
        {
            return string.Empty;
        }

        if (!SendsTags(roles.Count))
        {
            // One image: the pre-processor is told about it without a tag, and only when something is stated about it.
            return UntaggedStatementPreprocessorClause(roles[0], blocking);
        }

        var introducers = IntroducerIndexes(roles);
        var severalPlaces = CarriesSeveralPlaces(roles);
        var sb = new StringBuilder();
        sb.AppendLine("REFERENCE IMAGES — AUTHORITATIVE (this render is conditioned on the numbered images below):");
        for (var index = 0; index < roles.Count; index++)
        {
            sb.AppendLine(PreprocessorLineFor(index + 1, roles[index], blocking, introducers.Contains(index), severalPlaces));
        }

        sb.AppendLine(
            "Refer to these images in the image prompt ONLY by those tags, exactly as written (<image1>, <image2>, ...). "
            + "Do not call them \"the first image\", \"the reference image\", or \"image A\": the tag is what binds a "
            + "description to an image, and the model matches them by position. Say what each image supplies and what "
            + "must NOT be taken from it, and NEVER restate what an image supplies — not its structure, surfaces, "
            + "fixtures, objects, lighting, garment, features or stance — whether or not the brief also states it.");
        return sb.ToString();
    }

    /// <summary>
    /// What the operator calls this image — the name they typed on the asset, which is what a reference picker shows.
    /// Falls back to the binding's own semantic role, and then to nothing: an invented label would be worse than no
    /// label, because the tag is the part that binds and the label is only there to help the model tell two person
    /// references apart.
    /// </summary>
    /// <summary>
    /// The label a binding contributes to its numbered line: the operator's own name for the image when there is one,
    /// then <paramref name="fallback"/> when the caller resolved a richer description (the asset path's resolver
    /// folds the image's display name into it), then the binding's semantic role. Public because the render path
    /// numbers references it resolved itself and must not invent a second labelling rule.
    /// </summary>
    public static string? LabelFor(ReferenceApplicationSelection binding, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(binding.ReferenceLabel))
        {
            return binding.ReferenceLabel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return string.IsNullOrWhiteSpace(binding.SemanticRole) ? null : binding.SemanticRole.Trim();
    }

    /// <summary>
    /// The subject key a binding contributes, or null for a scene-level slot.
    ///
    /// <para>
    /// The binding's <c>ActorKey</c> IS the participant profile key: <c>ImageStepBlueprintFactory.RequireActor</c>
    /// refuses any other value ("the profile key the compiled brief uses, not a scenario character id") and
    /// <c>ReferenceSlotPlanner.Plan</c> copies it straight onto the binding. Location and pose slots declare no actor,
    /// so they contribute null and keep their scene-level sentence.
    /// </para>
    /// </summary>
    public static string? SubjectKeyOf(ReferenceApplicationSelection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return string.IsNullOrWhiteSpace(binding.ActorKey) ? null : binding.ActorKey.Trim();
    }

    /// <summary>
    /// The indexes whose role INTRODUCES a subject, so the first reference of each person carries the position and the
    /// rest refer back to it ("that same person's build"). A later reference does not repeat the framing: two
    /// sentences claiming one subject holds two positions would be a contradiction inside the prompt itself.
    /// </summary>
    private static HashSet<int> IntroducerIndexes(IReadOnlyList<ReferenceRole> roles)
    {
        var introduced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var introducers = new HashSet<int>();
        for (var index = 0; index < roles.Count; index++)
        {
            var key = roles[index].SubjectKey;
            if (!string.IsNullOrWhiteSpace(key) && introduced.Add(key!))
            {
                introducers.Add(index);
            }
        }

        return introducers;
    }

    private static string LineFor(int index, ReferenceRole role, SceneBlocking? blocking, bool introducesSubject, bool severalPlaces)
        => $"{TagFor(index)}{BodyFor(role, blocking, introducesSubject, severalPlaces)}";

    /// <summary>
    /// The same reference described for the PRE-PROCESSOR, whose reader is a language model WRITING the caption rather
    /// than an image model READING it. The instruction is inverted on purpose: <see cref="BodyFor"/> says "reproduce its
    /// structure, surfaces, objects and its own lighting", which a caption writer obeys by copying the room into the
    /// prose.
    /// </summary>
    /// <remarks>
    /// Measured live 2026-10-20 (production group 449e9148, Becky POV): the pre-processor was handed the image-model
    /// sentence and the location analysis note, and wrote the analysis back out — "corrugated metal walls … wooden
    /// floor showing cracks … natural light streaming through multiple windows on the left wall" — spending the whole
    /// caption on the room an image already supplies.
    /// </remarks>
    private static string PreprocessorBodyFor(
        ReferenceRole role,
        SceneBlocking? blocking,
        bool introducesSubject,
        bool severalPlaces)
    {
        var label = string.IsNullOrWhiteSpace(role.Label) ? string.Empty : $" ({role.Label.Trim()})";
        var placed = blocking is null ? null : SubjectFor(blocking, role.SubjectKey);
        var person = placed is null
            ? null
            : introducesSubject ? placed.RoleText : "that same person";
        var owner = person is null ? string.Empty : $" for {person}";

        var sentence = role.SlotKind switch
        {
            ImageStepSlotKind.Face =>
                $"is the FACE reference{owner}. Do NOT describe that person's facial features, hair or skin tone — the "
                + "image carries them. Do not let the plate's plain studio background or its studio lighting stand in "
                + "for the setting.",
            ImageStepSlotKind.Body =>
                $"is the BODY reference{owner}. Do NOT describe that person's height, body shape or proportions — the "
                + $"image carries them. Do not let the plate's plain studio background or its studio lighting stand in "
                + $"for the setting.{BodyViewStatement(role.Label)}",
            // The wardrobe clause keeps NAMING the garment: a clothing image is a photograph of clothes, and silence
            // about it dresses nobody (measured 2026-10-17/18, SceneImage c0d77b66).
            ImageStepSlotKind.Wardrobe =>
                $"is the WARDROBE reference{owner}. NAME the garment this image shows and dress the person in it. Do not "
                + "invent a different outfit, and do not leave them with nothing on.",
            ImageStepSlotKind.Location => severalPlaces
                ? "is one of the PLACES in this frame: it supplies that place. Name each place only by its KIND (a "
                    + "trailer, a shed, a yard) and do NOT describe their structure, surfaces, fixtures, objects or "
                    + "lighting. Do not merge them into one and do not leave one out."
                : "is the LOCATION reference: it supplies the setting. Name the place only by its KIND (a shed, a yard, "
                    + "a beach) and do NOT describe its structure, surfaces, fixtures, objects or lighting.",
            ImageStepSlotKind.Pose =>
                $"is the POSE reference{owner}: it supplies the stance. Do NOT describe limb positions or body angles.",
            ImageStepSlotKind.CharacterPose =>
                $"is the CHARACTER POSE reference{owner}: it supplies that person's appearance, clothing and stance. Do "
                + "NOT describe any of them.",
            ImageStepSlotKind.Prop =>
                $"is an OBJECT{owner}: name it and place it where the request puts it. Do not restyle it, substitute a "
                + "different object or leave it out.",
            _ => "is a reference image supplied for this render: say what it supplies and do NOT describe it."
        };

        return $"{label} {sentence}{DirectionStatementForPreprocessor(role.Directions)}";
    }

    /// <summary>
    /// The operator's direction, for the PRE-PROCESSOR only: which side it sits on, which way it faces, and the
    /// operator's OWN words — but never an ANALYSIS reading.
    /// </summary>
    /// <remarks>
    /// A note sourced from <see cref="ReferenceDirectionSource.Analysis"/> is a machine description of what the image
    /// contains (B-160 reads the location plate). Handing that to the caption writer is what made it reproduce the
    /// analysis verbatim — measured live 2026-10-20, production group 449e9148, where the compiled prompt repeated
    /// "corrugated metal walls … wooden floor showing cracks … natural light streaming through multiple windows on the
    /// left wall" including the analysis's own anchor inventory. The operator's own statement is a different thing: it
    /// is an instruction about this image, so it still reaches the writer.
    /// </remarks>
    private static string DirectionStatementForPreprocessor(ReferenceDirections? directions)
    {
        if (directions is null) return string.Empty;

        var clauses = new List<string>();
        if (directions.Side is { } side)
        {
            clauses.Add(side == FrameHorizontal.Center
                ? "in the CENTER of the frame"
                : $"on the {side.ToString().ToUpperInvariant()} of the frame");
        }

        if (directions.Facing is { } facing and not ReferenceFacing.Unknown)
        {
            clauses.Add(facing switch
            {
                ReferenceFacing.TowardViewer => "facing the camera",
                ReferenceFacing.AwayFromViewer => "facing away from the camera",
                _ => $"facing {facing.ToString().ToUpperInvariant()}"
            });
        }

        var sb = new StringBuilder();
        if (clauses.Count > 0)
        {
            sb.Append($" The operator places it {string.Join(", ", clauses)}.");
        }

        if (directions.Source != ReferenceDirectionSource.Analysis
            && !string.IsNullOrWhiteSpace(directions.Note))
        {
            sb.Append($" The operator says of it: \"{directions.Note.Trim()}\".");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The tag form of a reference line for the PRE-PROCESSOR. One numbering rule, two descriptions: the tag is decided
    /// here for both readers, and only the sentence after it differs.
    /// </summary>
    private static string PreprocessorLineFor(
        int index,
        ReferenceRole role,
        SceneBlocking? blocking,
        bool introducesSubject,
        bool severalPlaces)
        => $"{TagFor(index)}{PreprocessorBodyFor(role, blocking, introducesSubject, severalPlaces)}";

    private static string UntaggedPreprocessorLineFor(
        ReferenceRole role,
        SceneBlocking? blocking,
        bool introducesSubject,
        bool severalPlaces)
        => $"The reference image{PreprocessorBodyFor(role, blocking, introducesSubject, severalPlaces)}";

    /// <summary>
    /// The same sentence for an image sent WITHOUT a number, so it is named "the reference image" instead of by a tag
    /// the vendor forbids at N = 1. Everything after the subject is shared with <see cref="LineFor"/>: the only
    /// difference between a numbered and an unnumbered reference is how the prompt points AT it.
    /// </summary>
    private static string UntaggedLineFor(ReferenceRole role, SceneBlocking? blocking, bool introducesSubject, bool severalPlaces)
        => $"The reference image{BodyFor(role, blocking, introducesSubject, severalPlaces)}";

    private static string BodyFor(ReferenceRole role, SceneBlocking? blocking, bool introducesSubject, bool severalPlaces)
    {
        var placed = blocking is null ? null : SubjectFor(blocking, role.SubjectKey);
        if (placed is not null)
        {
            // Starts with a space, so it reads "The reference image is the person on the LEFT ..." once prefixed.
            return SubjectSentence(role.SlotKind, placed, introducesSubject);
        }

        var label = string.IsNullOrWhiteSpace(role.Label) ? string.Empty : $" ({role.Label.Trim()})";
        var ownedByPerson = !string.IsNullOrWhiteSpace(role.SubjectKey);

        var sentence = role.SlotKind switch
        {
            ImageStepSlotKind.Face =>
                "is the FACE reference: the person's face, features, hair and skin tone come from this image. Do not "
                + "carry over its plain studio background or its studio lighting.",
            ImageStepSlotKind.Body =>
                "is the BODY reference: the person's build, body shape, proportions and skin tone come from this "
                + $"image. Do not carry over its plain studio background or its studio lighting.{BodyViewStatement(role.Label)}",
            ImageStepSlotKind.Wardrobe =>
                "is the WARDROBE reference: the garment, its cut, colour, material and state come from this image. Do "
                + "not carry over the pose or the setting it was photographed in.",
            ImageStepSlotKind.Location => severalPlaces
                ? "is a SEPARATE PLACE in this frame: reproduce this place's own form, materials, proportions and "
                    + "the light falling on it. It is NOT the whole setting on its own — every place named in this "
                    + "clause stands in one frame together, and the instruction states how they sit relative to each "
                    + "other. Do not merge it into another place and do not drop it."
                // IMAGE-MODEL clause. "Name the setting only as its kind of place / do not describe its structure"
                // belongs to the CAPTION WRITER (PreprocessorBodyFor) — an image model does not name anything, it
                // renders, so that instruction was noise in the picture prompt (operator, 2026-10-22).
                : "is the LOCATION reference: this image IS the room, and the shot takes place inside it — take the "
                    + "room's structure, surfaces, objects and its own lighting from this image. Do not substitute a "
                    + "different room, and do not replace it with the setting of any other reference image.",
            ImageStepSlotKind.Pose =>
                "is the POSE reference: a skeleton supplying the stance, limb positions and camera-facing orientation. "
                + "It is a conditioning map, not a person — never render it.",
            ImageStepSlotKind.CharacterPose =>
                "is the CHARACTER POSE reference: the person's appearance, clothing and stance all come from this "
                + "image.",
            ImageStepSlotKind.Prop => ownedByPerson
                // B-166 §6: a prop may belong to a person. The actor key says WHOSE it is; the operator's direction
                // note says WHERE it goes. Nothing about either is inferred from the image.
                ? "is the OBJECT this person is holding or wearing: its shape, material, colour and state come from "
                    + "this image. Place it in that person's hand or on their person as the instruction describes. Do "
                    + "not substitute a different object and do not omit it."
                : "is the OBJECT in this frame: this object is present in the picture — its shape, material, colour and "
                    + "state come from this image. Place it in the scene wherever the instruction puts it. Do not "
                    + "substitute a different object and do not omit it.",
            _ => "is a reference image supplied for this render: take its subject as shown and do not carry over its "
                + "background or lighting."
        };

        return $"{label} {sentence}{DirectionStatement(role.Directions)}";
    }

    /// <summary>
    /// The facing a BODY reference's pack view label ("Back", "Front", "3/4 Left"…) states, as a sentence the model
    /// can act on. A body image does not just carry build and skin — a "Back" view also says which way the person
    /// faces, and without this the orientation was decided by the ambiguous prose alone (the "straight-on view" flip,
    /// measured 2026-10-22). Null for an unlabelled reference, which states nothing rather than guessing.
    /// </summary>
    private static string? BodyViewStatement(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var view = label.Split('·')[0].Trim();
        return view.ToLowerInvariant() switch
        {
            "back" => " The person is viewed from BEHIND — their back faces the camera.",
            "front" => " The person is viewed from the FRONT — their face is toward the camera.",
            "3/4 left" or "3/4 right" or "profile left" or "profile right" => " The person is viewed from the SIDE.",
            _ => null
        };
    }

    /// <summary>
    /// The clause for a step that sends exactly ONE reference image and has something stated about it.
    ///
    /// <para>
    /// Nothing is emitted when nothing is stated: with one image the reference is the whole request, and an automatic
    /// sentence nobody asked for would change every single-reference render in the app — including the ones that have
    /// always worked. A statement is the evidence that a human, or an analysis they triggered, wants this image
    /// described; without one the render keeps the behaviour it has always had (B-160 follow-up, 2026-10-13).
    /// </para>
    ///
    /// <para>
    /// The block carries NO <c>&lt;imageN&gt;</c> tag anywhere, because the vendor forbids tags when a single image is
    /// supplied; both the opening line and the closing line say so, so the model is never left to guess a number that
    /// was never sent.
    /// </para>
    /// </summary>
    private static string UntaggedStatementBlock(ReferenceRole role, SceneBlocking? blocking, int leadingImages)
    {
        if (!CarriesStatement(role))
        {
            return string.Empty;
        }

        var severalPlaces = CarriesSeveralPlaces([role]);
        var sb = new StringBuilder();
        sb.AppendLine(
            "REFERENCE IMAGE — AUTHORITATIVE (this render is conditioned on ONE reference image, and a single image is "
            + "NOT numbered, so no numbered image tag appears anywhere in this prompt):");
        sb.AppendLine(UntaggedLineFor(role, blocking, introducesSubject: true, severalPlaces));

        // A SINGLE place needs no extra line: its role clause already says the image IS the room and the shot happens
        // inside it. The writer-oriented wording ("name the setting only as its kind of place") lives in the
        // PRE-PROCESSOR clause only — an image model does not name things, it renders them (operator, 2026-10-22).
        if (severalPlaces)
        {
            sb.AppendLine(
                "Every place named above stands in the SAME frame: they are separate places in one shot, and the "
                + "instruction says how they sit relative to each other. All of them must be visible in the result. "
                + "Do not merge them into one building and do not render only one of them.");
        }

        if (role.SlotKind is ImageStepSlotKind.Face
            or ImageStepSlotKind.Body
            or ImageStepSlotKind.Wardrobe
            or ImageStepSlotKind.CharacterPose)
        {
            sb.AppendLine(
                "Keep this person's face, hair, skin tone, build and clothing exactly as the reference image shows it. "
                + PrecedenceFor(leadingImages) + " They are not described in words anywhere here, and nothing may be "
                + "re-derived from another description.");
        }

        sb.AppendLine("Take the reference image as that image, not as a description of it.");
        return sb.ToString();
    }

    /// <summary>
    /// The single-reference clause for the PRE-PROCESSOR, whose reader is a language model writing the prompt rather
    /// than the image model reading it: it is told the image's role, and told explicitly that it may not tag it.
    /// Empty when nothing is stated about the reference, for the same reason as
    /// <see cref="UntaggedStatementBlock"/>.
    /// </summary>
    private static string UntaggedStatementPreprocessorClause(ReferenceRole role, SceneBlocking? blocking)
    {
        if (!CarriesStatement(role))
        {
            return string.Empty;
        }

        var severalPlaces = CarriesSeveralPlaces([role]);
        var sb = new StringBuilder();
        sb.AppendLine(
            "REFERENCE IMAGE — AUTHORITATIVE (this render is conditioned on ONE image, and a single image must NOT be "
            + "tagged):");
        sb.AppendLine(UntaggedPreprocessorLineFor(role, blocking, introducesSubject: true, severalPlaces));
        sb.AppendLine(
            "This image carries what it names. Do NOT restate any of it in the caption — not its structure, surfaces, "
            + "fixtures, objects, lighting, garment, features or stance — whether or not the brief also states it. Name "
            + "only what KIND of thing it is, and spend the caption on the people, what they are doing and where they "
            + "are. Do NOT use numbered image tags: a single image is never numbered.");
        return sb.ToString();
    }

    /// <summary>
    /// The operator's own statement about a reference, appended AFTER the automatic role sentence and never replacing
    /// it (B-159, operator's decision: "add alongside").
    /// </summary>
    /// <remarks>
    /// <para>
    /// It sits beside the automatic sentence rather than over it because that sentence encodes a MEASURED failure it
    /// prevents — a studio identity plate donating its own background over the room, a location reference replaced by
    /// another reference's setting. An operator overriding that from a text box would be undoing a fix they cannot see.
    /// </para>
    ///
    /// <para>
    /// The wording is the OPERATOR'S ("on the left, facing right"), stated as a fact about this image's subject. It is
    /// emitted here, at render, where the <c>&lt;imageN&gt;</c> is assigned — so it is anchored to the reference and
    /// cannot be re-pointed by a later renumbering, and it survives the prompt compiler because the compiler never sees
    /// this block.
    /// </para>
    ///
    /// <para>
    /// Empty when the operator said nothing, which is every binding's starting state.
    /// </para>
    /// </remarks>
    private static string DirectionStatement(ReferenceDirections? directions)
    {
        if (directions is null || directions.IsEmpty)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        if (directions.Side is { } side)
        {
            clauses.Add(side == FrameHorizontal.Center ? "in the CENTER of the frame" : $"on the {side.ToString().ToUpperInvariant()}");
        }

        if (directions.Facing is { } facing and not ReferenceFacing.Unknown)
        {
            clauses.Add(facing switch
            {
                ReferenceFacing.TowardViewer => "facing the camera",
                ReferenceFacing.AwayFromViewer => "facing away from the camera",
                _ => $"facing {facing.ToString().ToUpperInvariant()}"
            });
        }

        // The ANALYSIS reading is never emitted. It is a machine description of what the image contains — the operator
        // reads it in the picker and the model is given the image itself — so stating it in prose hands the model a
        // second, competing description of something it can already see. That is the text the compiled prompt copied
        // verbatim ("corrugated metal walls … wooden floor showing cracks", measured 2026-10-20/21).
        var sb = new StringBuilder();
        if (clauses.Count > 0)
        {
            // An analysis proposes no side or facing (see ReferenceDirectionsPrefill): a place holds many features, so
            // a single side would be a guess. This branch therefore only ever fires for operator-authored axes.
            sb.Append($" The operator places it {string.Join(", ", clauses)}.");
        }

        if (directions.Source != ReferenceDirectionSource.Analysis && !string.IsNullOrWhiteSpace(directions.Note))
        {
            sb.Append($" The operator says of it: \"{directions.Note.Trim()}\".");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Whether this block carries more than one DIFFERENT PLACE — several structures sharing one frame — as opposed to
    /// several views of the same one.
    ///
    /// <para>
    /// One location IS the setting, and saying so is what stops a person reference donating its own studio background
    /// over the room. Several views of that one place are still one place: the shed's front and its back are the same
    /// building seen twice, and the clause keeps describing each as the room ("keeps the same building the same
    /// building from every angle" is why the Location slot is multi-valued at all). But LOCATION references drawn from
    /// different containers are different places, and describing each as the whole room — with its "do not replace it
    /// with the setting of any other reference image" exclusivity — states mutually exclusive facts, once per image
    /// (found 2026-10-12, when the operator bound two trailer fronts in order to show the trailers side by side facing
    /// each other).
    /// </para>
    ///
    /// <para>
    /// The container is the evidence, and an unknown one counts as the SAME place: absence of a container must never
    /// be what turns a single location into several structures.
    /// </para>
    /// </summary>
    private static bool CarriesSeveralPlaces(IReadOnlyList<ReferenceRole> roles)
        => roles
            .Where(role => role.SlotKind == ImageStepSlotKind.Location)
            .Select(role => role.ContainerKey?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

    /// <summary>
    /// What this image is a view OF — the asset container it belongs to, or null when the binding names none.
    /// </summary>
    private static string? ContainerKeyOf(ReferenceApplicationSelection binding)
        => string.IsNullOrWhiteSpace(binding.SceneAssetId) ? null : binding.SceneAssetId.Trim();

    /// <summary>The placed subject a key refers to, or null when the key names no subject this frame places.</summary>
    private static SceneBlockingSubject? SubjectFor(SceneBlocking blocking, string? subjectKey)
    {
        if (string.IsNullOrWhiteSpace(subjectKey))
        {
            return null;
        }

        foreach (var subject in blocking.Subjects)
        {
            if (string.Equals(subject.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase))
            {
                return subject;
            }
        }

        return null;
    }

    /// <summary>
    /// What a subject's reference supplies, said in terms of the PERSON and their POSITION rather than the pack view
    /// the image happened to come from — the change that stops two characters' faces reading identically.
    /// </summary>
    private static string SubjectSentence(ImageStepSlotKind? slotKind, SceneBlockingSubject subject, bool introduces)
    {
        return slotKind switch
        {
            ImageStepSlotKind.Face => introduces
                ? $" is {subject.RoleText} — their face, features, hair and skin tone come from this image. Do not "
                  + "carry over its plain studio background or its studio lighting."
                : " supplies that same person's face, features, hair and skin tone.",
            ImageStepSlotKind.Body => introduces
                ? $" is {subject.RoleText} — their build, body shape, proportions and skin tone come from this image. "
                  + "Do not carry over its plain studio background or its studio lighting."
                : " supplies that same person's build, body shape and proportions.",
            ImageStepSlotKind.Wardrobe => introduces
                ? $" is {subject.RoleText} — the garment, its cut, colour, material and state come from this image. Do "
                  + "not carry over the pose or the setting it was photographed in."
                : " supplies that same person's clothing — its cut, colour, material and state.",
            ImageStepSlotKind.CharacterPose => introduces
                ? $" is {subject.RoleText} — their appearance, clothing and stance all come from this image."
                : " supplies that same person's appearance, clothing and stance.",
            ImageStepSlotKind.Prop => introduces
                ? $" is {subject.RoleText} — this object is present in the frame, and its shape, material and colour come "
                  + "from this image. Do not carry over its background."
                : " supplies this object's shape, material and colour.",
            _ => $" supplies the reference for {subject.RoleText}."
        };
    }
}
