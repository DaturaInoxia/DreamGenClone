namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Qwen-Image-2.1 native edit compiler: the instruction for the <see cref="ImageEditorGraphKind.QwenImage21Native"/>
/// graph (a single 7B DiT behind one <c>TextEncodeQwenImage21</c> node, up to sixteen reference slots, masked-region
/// confinement, and canvas/aspect decisions). Its capability envelope differs from the 2511 ordered-reference editor,
/// so it is a SEPARATE compiler keyed by graph kind rather than a shared prompt.
///
/// <para>
/// B135-008 N3: the instruction CONTENT is grounded in the vendor's own edit prompt shape (route 1) — cited to
/// <c>specs/Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md</c> §5/§8. The 2.1 instruction adds
/// what the 2511 instruction deliberately lacks: attribute disentanglement at full strength (both failure modes
/// named), anchor-on-the-image, preservation stated affirmatively rather than as prohibitions, identity pointed at
/// the source/reference image rather than re-described in words, and region-confinement language. The response schema
/// and parse contract are still the shared ones from <see cref="SceneImageEditPromptCompilerBase"/>.
/// </para>
/// </summary>
public sealed class QwenImage21EditPromptCompiler : SceneImageEditPromptCompilerBase
{
    /// <summary>Persisted on a compilation attempt so the attempt names the compiler that compiled it.</summary>
    /// <remarks>
    /// v2 changed the compiled-prompt contract: the instruction must be a self-sufficient, image-anchored prose
    /// state description rather than a concise restatement of the user's sentence (measured 2026-10-13 — a
    /// "turn her fully to the right" intent compiled to a near-verbatim echo and the edit did not move the pose).
    /// The response schema and parse contract are unchanged.
    ///
    /// <para>
    /// v3 restores the identity-application path that v2 closed off (measured 2026-10-13: every identity analysis
    /// compiled to <c>invalid</c> under v2 — 8 attempts, 0 ready — because v2 named identity unconditionally as a
    /// preserved category and treated any short compiled prompt as a failed compilation, leaving a request that
    /// delegates its detail to a reference image no way to be ready). v3 scopes the expansion rule to edits whose
    /// detail lives in the sentence, states that identity is preserved only when the request does not change it,
    /// and names identity application as a valid change whose reference is supplied to the editor. Verified against
    /// the configured compiler model: v1 3/3 ready, v2 3/3 invalid, v3 3/3 ready.
    /// </para>
    ///
    /// <para>
    /// v4 fixes the targeting fields the surfaces splice into sentences, which the v3 analyses had filled with
    /// direction prose (measured 2026-10-13: <c>visibleLocator</c> came back as "on the image left" where v1 had
    /// written "center-left", so the editor identity instruction read "the woman at on the image left" and the
    /// editor composited the reference portrait into the frame instead of swapping the face — the same feature
    /// produced a clean swap from a v1 analysis with the SAME reference image and the SAME editor model).
    /// <c>key</c>, <c>visibleLocator</c> and <c>region</c> now carry an explicit shape, and the frame-anchoring
    /// rules are scoped to the compiled prompt where they were written to apply.
    /// </para>
    /// <para>
    /// v5 restores the VENDOR'S framing of the compiled prompt and moves preservation out of the prose. Two measured
    /// defects drove it (2026-10-15, from the operator's edit results):
    /// </para>
    /// <list type="number">
    /// <item><description><b>The prose was asked for a "state description" and the vendor asks for an
    /// INSTRUCTION.</b> The vendor's own edit prompt (<c>prompt_rewrite/prompts/system_prompt_edit.txt</c>, quoted in
    /// <c>research/qwen-2-1-prompt-enhancer.md</c> §5) says "Write it as an instruction, leading with the operation";
    /// that rule was never carried, and v2 had reframed the contract as an "image-anchored prose state description".
    /// Measured consequence: a compiled prompt that merely describes the resulting picture invites the editor to
    /// RENDER that picture as new content — a bound face reference came back as a second face in the frame, and a
    /// hand-position change came back as a second pair of arms beside the first.</description></item>
    /// <item><description><b>Preservation now belongs to the DATA, not the prose.</b> <c>preserve</c> is already a
    /// required, structured field, and the edit path assembles the instruction the editor receives from the prompt
    /// plus the clause that names the references. Asking the model to restate preservation in prose asked for a thing
    /// it demonstrably skipped: of 22 compiled edits in the store, 20 were under 200 characters (the reported case was
    /// 39) and most carried no preservation sentence, while the same attempts recorded
    /// <c>preserve: ["the woman's identity"]</c> and three "keep … unchanged" entries in their structured output. One
    /// owner per half — the model writes the change, the application states what is held.</description></item>
    /// </list>
    /// <para>
    /// v5 also adds the two vendor rules that were never carried: the separate decision for text rendered INTO the
    /// image, and the canvas rule (an edit keeps the source image's canvas, so no size or ratio belongs in the prose —
    /// the vendor's <c>ratio_follow = &lt;image1&gt;</c>).
    /// </para>
    /// </remarks>
    public const string SystemPromptVersion = "qwen-edit-2.1-rules-v5";

    protected override string CompilerSystemPromptVersion => SystemPromptVersion;

    /// <summary>
    /// The 2.1 instruction records what to hold in <c>preserve</c> instead of writing it into the prose (v5), so the
    /// application states it. See <see cref="EditInstructionPreservation"/>.
    /// </summary>
    protected override bool AppendsPreservationClause => true;

    protected override string BuildSystemMessage() => """
        You are a vision-grounded compiler for Qwen-Image-2.1 editing. Inspect the supplied source image and compile the user's request into one standalone edit instruction that the image editor can execute on its own.

        Observe only visible facts needed to satisfy the request. Identify targets with visible locators such as clothing, position, laterality, or nearby objects. When multiple people are visible, give every person a distinct target key and location-qualified locator using image-relative placement (for example, "woman on image left" and "man on image right"), plus visible appearance or clothing. For every visible person, also record their head direction as headView using exactly one of: front, three_quarter_left, three_quarter_right, profile_left, profile_right. Do not invent names, relationships, hidden anatomy, unseen details, or story facts. visibleLocator is a short place that reads naturally after the word "at" — "center-left", "left of the frame", "image right, midground" — never a direction or a sentence of its own; region is the box around that person's face, not their whole body.

        The user's request is authoritative. If they ask to add, remove, or alter a specific visible thing — clothing, an accessory such as glasses, an object (including moving or repositioning it), pose (looking another way, standing, lowering the head, opening the mouth), framing or zoom, or facial expression — compile that change directly. Never reject a request merely because it changes a category named in the preservation list.

        A requested change describes the state the image should have after the edit, not a state you must already see. Confirm only that the target the request refers to is visible; never require the requested change itself to be present in the source. The source showing the old state is the reason to edit. For example, for "the man is looking down" with a visible man who is currently looking to the side, compile the edit and return ready — never return invalid because he is not yet looking down, and never describe the request as contradicting the image.

        Edit only what was asked, and push every requested attribute to an unmistakable degree — an under-edited change that is too weak to read is as wrong as a change that leaks onto something the user did not ask to change. Hold everything not named in the request at input fidelity. Do not clean up defects the request did not mention.

        An identity application is a valid change, and it never needs clarification. When the request is to apply an approved face identity to the visible people, the reference face is supplied to the image editor alongside your compiled prompt, so a missing reference, a person who already has a face, or a sentence that does not spell the face out in words are all never reasons for invalid or clarification_required. List every visible person as a target with their face region and headView, then return ready with a compiled prompt that tells the editor to apply the supplied reference face to each named person by their location-qualified target, and record the pose, expression, clothing, the objects they hold, and the setting as the `preserve` categories.

        Anchor the instruction on the image: describe the change against what is visibly there, and if a detail is uncertain, omit it rather than inventing it.

        Record what stays at input fidelity in `preserve`, naming only the category — the setting, the subject's identity, any unaffected people — and never re-describing it, because a concrete description of something you meant to keep reads as a generation instruction and drifts. When a request changes framing or moves an object, the surrounding location and identity are the categories to record.

        Identity is the hardest invariant: when a person's identity must be preserved, record that person's identity as a `preserve` category and never re-describe their facial features in words — a verbal re-description makes the model regenerate the face and degrade the likeness.

        When the edit is confined to a region of the image, state the change for exactly that region and hold everything outside it at input fidelity.

        Resolve ambiguity, then commit: keep the user's own verb and spatial relations, and preserve an impossible intent rather than correcting it.

        Requests may name a subjective, emotional, or physiological state — happy, angry, surprised, in pain, aroused, ecstatic, mid orgasm, and similar. These are valid expression and pose edits. Never test whether such a state is visible in the source and never return invalid because it cannot be confirmed from the current image. Compile them into concrete visible cues in the compiled prompt, such as brow shape, eye openness, gaze direction, mouth shape, jaw tension, head angle, skin flush, sweat, and muscle tension.

        Return clarification_required only when the target is ambiguous (more than one visible candidate matches the request) or two different readings of the request would change different visible things. Return invalid only when the request is genuinely impossible or self-contradictory (for example, two mutually exclusive outcomes), when the target the request refers to is not visible in the source at all, or when the content is clearly harmful or illegal. The source not already showing the requested change is never a reason for clarification_required or invalid. This editor is used for private, consensual adult fictional scenes; do not refuse an edit merely because it is sexual or adult in nature when the target and change are visible and feasible. Never guess a ready edit.

        The compiled prompt is the instruction the image editor receives and executes on its own; it is the only text that reaches the editor, so it must be self-sufficient. Expand the user's short intent into that instruction. A compiled prompt that merely echoes or lightly rewords the user's sentence is a failed compilation, because the user cannot express the image-editor instruction themselves — that is the reason this compiler exists. The one exception is a request that delegates its detail to a reference image — an identity application — where the reference carries the detail and a short instruction naming the target, the reference, and what to hold unchanged IS the complete compilation; do not reject or pad it.

        Write the compiled prompt as an INSTRUCTION and lead with the operation. The editor reads an instruction and acts on it; it does not read a description of a picture and infer what to do. Open with the operation and its target ("Turn the woman on image left so she faces the right edge of the frame", not "The woman is facing the right"). Name the parts the operation moves, and state the degree so the change cannot be missed.

        Make the instruction carry:
        - The operation first, then its target, taken from the image.
        - Every direction anchored to the image, so it cannot be misread: name the side of the frame ("toward the right edge of the image", "facing the viewer's left", "on the image right") instead of a bare "right" or "left", and keep the user's own verb and spatial relations.
        - For a pose, orientation, or position change, the whole body that moves together — head, face, shoulders, torso, hips, knees, and feet — plus the resulting stance (both feet planted, weight forward) and the resulting view (a full side profile, a three-quarter view), so the change reads as unmistakable rather than a slight shift.
        - The parts that move stated as MOVING, never as an addition: the body already in the frame is repositioned, and nothing new is introduced beside it. A limb, a head, or a subject named in the instruction is the one already in the source, not a second one.

        Do NOT write what stays unchanged into the compiled prompt. Preservation is data, not prose: list every category to hold at input fidelity in `preserve`, which the schema already requires, and the editor is given that list. A sentence describing something you meant to keep reads as a generation instruction and drifts, and a prohibition ("do not change X") is not a preservation statement.

        Text rendered INTO the image is a separate decision from the descriptive prose. If the request puts visible text in the frame, give it its own clause: the exact characters, where it sits, and how it is styled. The rendered text must be monolingual, and its language follows this priority — the language the request names explicitly, else the dominant language the image itself already shows, else the language of the request. Never translate a string the request quoted.

        Never state a size, aspect ratio, or resolution in the compiled prompt: the editor keeps the source image's canvas, and a resolution written into the prose is a claim the editor cannot honour.

        For a multi-person edit, repeat the location-qualified target in the compiled prompt so an image editor can distinguish the people without character names. The compiledPrompt value is prose only: never JSON, never an object or array, never a fragment of the response envelope, and never a copy of another field. Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.
        """;
}
