using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The researched per-family compiler texts, in one place (B-135 B135-008 / D13).
///
/// <para>
/// <b>None of this text is new.</b> Every string here is moved verbatim out of the prompt builders that already used
/// it (<c>SdxlSceneImagePromptBuilder</c> and <c>PonySceneImagePromptBuilder</c>), so the per-checkpoint profile rows
/// can be seeded from the SAME source the beat path compiles with. That is what makes "the profile's SystemPrompt" a
/// relocation rather than a rewrite: no model facts, rules or examples were invented for this file, and the provenance
/// comments below record where each text's authority comes from.
/// </para>
///
/// <para>
/// They live in the Domain layer because two layers need them and only one may own them: the profile seed (Infrastructure)
/// and the prompt builders (Web). Duplicating them would mean the profile row and the compiler could disagree about what
/// the checkpoint's instructions are, which is exactly the class of divergence these profiles exist to remove.
/// </para>
///
/// <para>
/// <b>Known, deliberately unfixed gap.</b> The natural-language texts open by naming an SDXL-based model. Four families
/// (Api, Flux, QwenImage21, Sdxl) currently use them, so FLUX and Qwen cells are told they are SDXL-family. That is the
/// app's existing behaviour, preserved here rather than silently "improved": correcting it means editing researched
/// compiler prose per family, which is research work (governance rules 4 and 9), not a rename.
/// </para>
/// </summary>
public static class SceneImageCompilerSystemPrompts
{
    /// <summary>
    /// The effective prompt-length target both families state to the model. One source, because a budget written into
    /// the instruction text and a budget enforced by the evaluator that disagree is a cell that can never pass.
    /// </summary>
    public const int OutputTargetChars = 800;

    /// <summary>The Pony quality string. Its presence is a requirement, not a preference: Pony's author documents the short form as much weaker.</summary>
    public const string PonyQualityTags = "score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up";

    /// <summary>
    /// The literal option tokens the render path substitutes after generation. They are constants rather than text
    /// inside the interpolated literal below because a doubled brace cannot appear as CONTENT in a raw string with a
    /// single <c>$</c> (the compiler reads it as an interpolation), and the tokens must reach the model's instructions
    /// verbatim: <c>SceneImageRenderingJobHandler</c> replaces exactly these strings.
    /// </summary>
    private const string StyleToken = "{{style}}";

    /// <inheritdoc cref="StyleToken"/>
    private const string SizeToken = "{{size}}";

    /// <summary>
    /// Natural-language, beat-shaped instruction. Source: the canonical compiler standards plus the adopted rules from
    /// the operator-supplied SDXL rewriter prompt (assessment record:
    /// <c>specs/Planning/B-135-image-playground/research/sdxl-rewriter-prompt-assessment.md</c>). That prompt's tag-list
    /// grammar, backtick wrapper, numeric-age clamp and race-&gt;appearance inference were REJECTED and must not be
    /// reintroduced here.
    /// </summary>
    public static readonly string NaturalLanguageBeat = $"""
        Convert story prose into a short, NATURAL-LANGUAGE image prompt for an SDXL-based photorealistic model (SDXL base 1.0, Juggernaut XL, Big Lust) — not comma-tag soup, not danbooru tags, not attribute metadata blocks.
        Rules:
        - Write 2-4 short natural sentences or phrases that describe the scene like a photography brief. The result must look realistic and photographic.
        - Describe each character's appearance (hair, eyes, body type, age) so the same character is recognizable every time. State each person's gender explicitly (e.g. 'a middle-aged man and a middle-aged woman') so the model never merges or miscounts people.
        - Fold in location, time of day, lighting, and mood in plain words. Do not repeat the same fact twice.
        - Use this element order, and cover each once: main subject (state the count and gender first: how many people, and each person's gender) -> physical traits (build, skin tone, hair colour and style, age band) -> pose and expression -> clothing or nudity plus accessories -> camera framing -> lighting and mood -> environment -> visual style and medium. Never repeat a concept or descriptor.
        - Appearance comes from the character data you are given, never from inference: never derive skin tone, hair colour or hair style from a race, nationality or ethnicity, and never correct a combination you believe is unusual. If a trait is not given, leave it out rather than inventing one.
        - Age is a life stage, never a number of years, and never a word that could read as a minor. Use young adult, middle-aged, mature or elderly.
        - No filler or praise words: no masterpiece, best quality, high quality, 8K, award-winning. Style, medium and texture cues carry the realism instead.
        - Never write a resolution, aspect ratio or pixel count into the description; those travel in the image settings.
        - When you change clothing, stay in the same category: a dress becomes another dress, never trousers.
        - Describe uncertainty the way an observer would (appears to be, likely) instead of inventing a specific you cannot see.
        - Include photographic style cues such as photorealistic, 35mm, natural skin texture, sharp focus.
        - Honor beat-stated clothing exactly; describe explicitness to match what the scene depicts, not a user toggle or narrative phase.
        - Never use Pony-style vocabulary: no score_9, no rating_explicit/questionable/safe, no 1girl/1boy/2people count tags, no danbooru tokens.
        - Choose explicitness from the depicted content: describe explicit sexual acts or visible genitals explicitly, suggestive content or partial nudity as questionable, and non-sexual content as safe. Base it on the scene, not narrative phase or a user toggle. Use concrete anatomical language for explicit content and imply rather than spell out questionable content.
        - Keep the ENTIRE prompt under {OutputTargetChars} characters. Short and concrete beats verbose.
        - Return ONLY the final image prompt as plain text. No commentary, quotes, or markdown.

        - Follow the depicted scene content exactly; do not add or remove explicitness based on phase or settings.
        """;

    /// <summary>
    /// Natural-language, canonical-brief-shaped instruction (B-104 / B-103 part B). Rationale and research:
    /// <c>.github/instructions/scene-image-prompt-compiler-standards.instructions.md</c> and the B-103 failure analysis
    /// (Becky dropped from a wide, distant, shadowed shot).
    /// </summary>
    public static readonly string NaturalLanguageCanonical = """
        Convert the canonical Still brief below into ONE photographic text-to-image prompt for an SDXL-family photorealistic checkpoint (SDXL 1.0 / Juggernaut XL / Big Lust).
        Facts about these models that shape the prompt:
        - They read natural-language photographic descriptions and map only what is visually renderable: visible people, clothing, hair, pose, location, objects, lighting, camera.
        - They do not know character names, relationships, or ownership ("Dean", "Becky", "Ken's shirt" carry no visual meaning). Describe people by appearance only.
        - They cannot render faces at a distance; a far or shadowed subject is dropped — never rely on a distant figure the model will drop. Keep every required person near, clearly lit, and in focus.
        - They are trained on NSFW data; describing each person's clothing anchors the output and prevents accidental nudity.
        - They merge or miscount people, and confuse which attribute belongs to whom (hair, clothing), unless gender and number are stated and each person is described as one self-contained clause.
        Rules:
        1. POV FRAMING: Render the scene strictly from the PRODUCTION POV character's viewpoint — show only what that character sees and NEVER include the POV character in the frame. If the POV is Omniscient, show the full scene with all characters visible.
        2. NO NAMES / RELATIONS / OWNERSHIP: appearance only (build, hair style/color, skin tone, clothing type+color, visible pose).
        3. RENDERABLE-ONLY: include only the frozen, visually present instant. Omit narrative distance, intent, metaphor, and off-screen facts.
        4. GENDER AND COUNT, NO ATTRIBUTE BLEED: state each person's gender and number, and describe each person as ONE self-contained clause (appearance + clothing together) — never interleave attributes across people.
        5. CLOTHING IS A SAFETY ANCHOR: always describe each person's clothing unless the brief explicitly implies nudity.
        6. TIGHT PHOTO CAPTION: lead with framing and the main subject in the first sentence; keep the ENTIRE caption under 800 characters; use photorealistic cues (35mm, natural skin texture, shallow depth of field).
        7. ELEMENT ORDER: main subject (count and gender first: how many people, and each person's gender) -> physical traits -> pose and expression -> clothing or nudity plus accessories -> camera framing -> lighting and mood -> environment -> visual style and medium. Cover each once and never repeat a concept or descriptor.
        8. APPEARANCE IS GIVEN, NEVER INFERRED: render the character data exactly as supplied. Never derive skin tone, hair colour or hair style from a race, nationality or ethnicity, and never correct a combination you believe is unusual. If a trait is absent, omit it rather than inventing one.
        9. AGE IS A LIFE STAGE, NEVER A NUMBER, and never a word that could read as a minor (young adult, middle-aged, mature, elderly). No resolution, aspect ratio or pixel count in the caption — those travel in the image settings.
        10. NO FILLER OR PRAISE WORDS: no masterpiece, best quality, high quality, 8K, award-winning. Style, medium and texture cues carry the realism instead. When clothing changes, stay in the same category (a dress becomes another dress, never trousers).
        Examples — target shape only, do not reuse their content:
        - General caption shape (SDXL/Juggernaut prompting guides): "Young woman reading a book in a cozy coffee shop, brunette hair cascading over her shoulders, style candid photography, mood relaxed, lighting natural light streaming through a nearby window, perspective over-the-shoulder, texture soft wool sweater and glossy wooden table."
        - POV-scene shape (project reference, follows the same anatomy): "a photorealistic view from twenty feet away across the grass at night: a woman with dark hair in a loose bun stands at the wooden deck railing of a silver trailer, wearing an unbuttoned pale-blue camp shirt, bare-legged, one hand resting beside a glass on the rail, her face turned toward the dark pines. She is lit only by thin strips of blue television light leaking through warped blinds. 35mm, shallow depth of field, natural skin texture." — the POV character is never in frame and people are described by appearance only.
        Never emit Pony vocabulary, score tags, rating tags, danbooru tokens, or count tags.
        Return ONLY the final image prompt as plain text. No commentary, quotes, or markdown.
        """;

    /// <summary>
    /// Pony tag dialect, beat-shaped. Source: <c>.github/instructions/pony-v6-prompting.instructions.md</c> and the
    /// Pony author's guidance that the full quality string is a training quirk that matters.
    /// </summary>
    public static readonly string PonyTagsBeat = $"""
        Convert story prose into a dense comma-separated tag prompt for the PONY DIFFUSION V6 XL image model (a Stable Diffusion XL finetune).
        Pony reads DENSE, COMMA-SEPARATED TAGS — not prose, not sentences, not attribute metadata. Short prompts work; long ones degrade output into garbage.
        Rules:
        - ALWAYS start the prompt with the full quality tag string: {PonyQualityTags}
        - Immediately after the quality tags, choose the Pony rating tag from what the scene depicts: rating_explicit for explicit sexual acts or visible genitals, rating_questionable for suggestive content or partial nudity, and rating_safe for non-sexual content. Base it on the depicted content, not on narrative phase.
        - Add a danbooru-style count tag (1boy, 1girl, 2people, 1girl and 1boy) matching the number of people in frame. This prevents the model merging people into one figure.
        - Describe each character with 3-6 SHORT visual tags (hair, eyes, body type, age, key clothing) — never a metadata block, never 'Age: 51; Height: 5'8"; Body type: curvy', never 'Appearance — ...'. Use concrete single tokens (e.g. chubby, not 'full figure').
        - Fold the scene into a few short tags: location, time of day, lighting, mood. Do not repeat the same fact twice.
        - Add one explicit camera/view tag (e.g. front view, eye level, from side).
        - Honor beat-stated clothing exactly; only use nudity when the beat explicitly implies it.
        - For explicit scenes use concrete anatomical language; for safe/questionable scenes imply rather than spell out.
        - Keep the ENTIRE prompt under {OutputTargetChars} characters and under ~40 tags. Short and dense beats verbose.
        - Return ONLY the final comma-separated image prompt as plain text. No commentary, quotes, or markdown.
        - Use female/male (danbooru vocabulary) rather than woman/man when a single gender tag fits the character.
        - The Pony family spans the base V6 checkpoint and photorealistic human merges (e.g. Pony Realism) that all read the same danbooru tags. Do not force a cartoon/anime style, and do not invent style words the scene does not state.

        - Choose the rating tag and scene explicitness from the depicted content; do not use narrative phase or a user setting as a substitute for reading the scene.
        """;

    /// <summary>
    /// Pony tag dialect, canonical-brief-shaped. The <c>{{style}}</c> and <c>{{size}}</c> placeholders are literal
    /// tokens the canonical path substitutes later, not interpolation holes.
    /// </summary>
    public static readonly string PonyTagsCanonical = $$"""
        Convert the supplied immutable canonical Still brief into one short dense comma-separated prompt for the PONY DIFFUSION V6 XL image model. Do not invent or rediscover story facts.
        Start verbatim with: {{PonyQualityTags}}, then choose the rating tag from the depicted content: rating_explicit for explicit sexual acts or visible genitals, rating_questionable for suggestive content or partial nudity, and rating_safe for non-sexual content.
        Then include the exact visible cast count, short visual identity/wardrobe/action tags, location, lighting, mood, one camera-view tag, and the {StyleToken} and {SizeToken} placeholders.
        Keep every visible person's tags in its OWN self-contained cluster (one 1girl run, one 1boy run) and never merge or reorder attributes between people. Repeat each person's age token — Pony/Pony Realism faces skew young, so age must be stated explicitly and more than once.
        Use female/male danbooru vocabulary. The checkpoint may be a photorealistic Pony merge (e.g. Pony Realism) or the base V6 checkpoint — both read dense danbooru tags; do not impose a cartoon/anime style.
        Keep the result under 800 characters and about 40 tags. Return only the final prompt as plain text.
        """;

    /// <summary>
    /// The compiler instruction text for a checkpoint's family. Both builders and the profile seed read through here,
    /// so a profile row's <c>SystemPrompt</c> and the beat path's system prompt cannot drift apart.
    /// </summary>
    /// <param name="family">The checkpoint's family, as declared on its profile.</param>
    /// <param name="canonical">
    /// True for the canonical-brief path (B-104/B-103), false for the beat path. These are different shapes and are
    /// not interchangeable: the canonical text assumes an immutable brief, and the beat text assumes story prose.
    /// </param>
    public static string For(SceneImageModelFamily family, bool canonical) => family switch
    {
        SceneImageModelFamily.Pony => canonical ? PonyTagsCanonical : PonyTagsBeat,
        SceneImageModelFamily.Sdxl => canonical ? NaturalLanguageCanonical : NaturalLanguageBeat,
        SceneImageModelFamily.Flux => canonical ? NaturalLanguageCanonical : NaturalLanguageBeat,
        SceneImageModelFamily.QwenImage21 => canonical ? NaturalLanguageCanonical : NaturalLanguageBeat,
        SceneImageModelFamily.Api => canonical ? NaturalLanguageCanonical : NaturalLanguageBeat,
        _ => throw new InvalidOperationException(
            $"No compiler instruction text exists for model family '{family}'. A checkpoint whose family is unknown "
            + "cannot be compiled for, and guessing a dialect is how a prompt ends up in a language its model ignores.")
    };
}
