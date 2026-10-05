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
    public readonly record struct ReferenceRole(ImageStepSlotKind? SlotKind, string? Label);

    /// <summary>The tag Qwen's multi-image rule requires for the reference at position <paramref name="index"/>.</summary>
    /// <remarks>
    /// The INDEX is a position in the images the model receives, not a position in the reference list. On an edit the
    /// source image occupies slot 1, so the first reference is <c>&lt;image2&gt;</c> — see
    /// <see cref="LeadingImageCount.Edit"/>. That offset is stated once, here, because getting it wrong re-points
    /// every tag in the clause without failing anything.
    /// </remarks>
    public static string TagFor(int index) => $"<image{index}>";

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
    /// The roles for a set of bindings, in the order the render sends them, or nothing when they supply fewer than
    /// two images. A binding that carries no image (a <c>TextOnly</c> strategy) is not a reference and is never
    /// numbered — numbering it would shift every tag after it onto the wrong image.
    /// </summary>
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

            roles.Add(new ReferenceRole(ReferenceBindingShape.SlotKindOf(binding), LabelFor(binding)));
        }

        return roles.Count < 2 ? [] : roles;
    }

    /// <summary>
    /// The numbered block, for images ALREADY in send order. Empty when fewer than two are sent: with one reference
    /// the app issues no tags at all, which is Qwen's rule for N = 1 and also what every single-reference render has
    /// always done.
    /// </summary>
    /// <param name="leadingImages">
    /// How many images the model receives BEFORE these references, which decides the first tag —
    /// <see cref="LeadingImageCount.Generate"/> for a render, <see cref="LeadingImageCount.Edit"/> for an edit whose
    /// source image is slot 1.
    /// </param>
    public static string BlockFor(
        IReadOnlyList<ReferenceRole>? rolesInSendOrder,
        int leadingImages = LeadingImageCount.Generate)
    {
        if (rolesInSendOrder is not { Count: > 1 })
        {
            return string.Empty;
        }

        if (leadingImages < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadingImages), leadingImages,
                "A negative leading-image count would number the first reference before <image1>.");
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            "REFERENCE IMAGES — AUTHORITATIVE (each is an input to this render, numbered in the order sent; there are "
            + $"{rolesInSendOrder.Count}):");
        for (var index = 0; index < rolesInSendOrder.Count; index++)
        {
            sb.AppendLine(LineFor(index + 1 + leadingImages, rolesInSendOrder[index]));
        }

        if (rolesInSendOrder.Any(role => role.SlotKind == ImageStepSlotKind.Location))
        {
            sb.AppendLine("Frame the shot inside that room: the room supplies the setting and its own lighting.");
        }

        sb.AppendLine("Take each <imageN> as that image, not as a description of it.");
        return sb.ToString();
    }

    /// <summary>
    /// Appends <see cref="BlockFor"/> to a finished image prompt, returning it unchanged when fewer than two images
    /// are sent.
    /// </summary>
    /// <param name="leadingImages">See <see cref="BlockFor"/>: the source image of an edit counts as 1.</param>
    public static string AppendToImagePrompt(
        string imagePrompt,
        IReadOnlyList<ReferenceRole>? rolesInSendOrder,
        int leadingImages = LeadingImageCount.Generate)
    {
        ArgumentNullException.ThrowIfNull(imagePrompt);

        var block = BlockFor(rolesInSendOrder, leadingImages);
        return block.Length == 0
            ? imagePrompt
            : $"{imagePrompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{block}";
    }

    /// <summary>
    /// Appends the role block to an EDIT instruction, numbering from the image AFTER the source. Returns the
    /// instruction unchanged when fewer than two references are attached to it.
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
        IReadOnlyList<(ImageStepSlotKind? SlotKind, string Description)> referencesInSendOrder)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(referencesInSendOrder);

        return AppendToImagePrompt(
            instruction,
            [.. referencesInSendOrder.Select(reference => new ReferenceRole(reference.SlotKind, reference.Description))],
            LeadingImageCount.Edit);
    }

    /// <summary>
    /// The clause for the message the image-prompt PRE-PROCESSOR receives, so the prompt it writes can name the
    /// references instead of describing them for the first time. Empty when fewer than two images are sent.
    /// </summary>
    public static string ForPreprocessor(IReadOnlyList<ReferenceApplicationSelection>? bindingsInSendOrder)
    {
        var roles = RolesFor(bindingsInSendOrder);
        if (roles.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("REFERENCE IMAGES — AUTHORITATIVE (this render is conditioned on the numbered images below):");
        for (var index = 0; index < roles.Count; index++)
        {
            sb.AppendLine(LineFor(index + 1, roles[index]));
        }

        sb.AppendLine(
            "Refer to these images in the image prompt ONLY by those tags, exactly as written (<image1>, <image2>, ...). "
            + "Do not call them \"the first image\", \"the reference image\", or \"image A\": the tag is what binds a "
            + "description to an image, and the model matches them by position. Say what each image supplies and what "
            + "must NOT be taken from it, and never repeat an image's content as a description of something the brief "
            + "already supplies.");
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

    private static string LineFor(int index, ReferenceRole role)
    {
        var tag = TagFor(index);
        var subject = string.IsNullOrWhiteSpace(role.Label) ? tag : $"{tag} ({role.Label.Trim()})";

        var sentence = role.SlotKind switch
        {
            ImageStepSlotKind.Face =>
                "is the FACE reference: the person's face, features, hair and skin tone come from this image. Do not "
                + "carry over its plain studio background or its studio lighting.",
            ImageStepSlotKind.Body =>
                "is the BODY reference: the person's build, body shape, proportions and skin tone come from this "
                + "image. Do not carry over its plain studio background or its studio lighting.",
            ImageStepSlotKind.Wardrobe =>
                "is the WARDROBE reference: the garment, its cut, colour, material and state come from this image. Do "
                + "not carry over the pose or the setting it was photographed in.",
            ImageStepSlotKind.Location =>
                "is the LOCATION reference: this image IS the room. Reproduce its structure, surfaces, objects and its "
                + "own lighting. Do not invent a different room, and do not replace it with the setting of any other "
                + "reference image.",
            ImageStepSlotKind.Pose =>
                "is the POSE reference: a skeleton supplying the stance, limb positions and camera-facing orientation. "
                + "It is a conditioning map, not a person — never render it.",
            ImageStepSlotKind.CharacterPose =>
                "is the CHARACTER POSE reference: the person's appearance, clothing and stance all come from this "
                + "image.",
            _ => "is a reference image supplied for this render: take its subject as shown and do not carry over its "
                + "background or lighting."
        };

        return $"{subject} {sentence}";
    }
}
