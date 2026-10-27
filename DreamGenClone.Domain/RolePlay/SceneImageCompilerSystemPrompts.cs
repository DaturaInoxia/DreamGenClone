using System.Text;
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
/// <b>Known, deliberately unfixed gap.</b> The natural-language texts open by naming an SDXL-based model. Three families
/// (Api, Flux, Sdxl) still use them, so FLUX and API cells are told they are SDXL-family. That is the app's existing
/// behaviour, preserved here rather than silently "improved": correcting it means editing researched compiler prose per
/// family, which is research work (governance rules 4 and 9), not a rename. Qwen-Image-2.1 was separated out in B135-008
/// route 1 (<see cref="Qwen21Beat"/> / <see cref="Qwen21Canonical"/>); FLUX remains a documented follow-up.
/// </para>
/// </summary>
public static class SceneImageCompilerSystemPrompts
{
    /// <summary>
    /// The prompt-length budget placeholder. It is a TOKEN, never a number: the budget has exactly one owner — the
    /// resolved <c>ImageCompilerProfile.MaxChars</c> for the checkpoint that will render — and every instruction text
    /// below states <em>that</em> value via <see cref="ApplyBudget"/>. A budget written into the instruction text and a
    /// budget enforced by the evaluator that disagree is a cell that can never pass, which is exactly what happened
    /// while this was the literal 800 for checkpoints whose profile declared 3400 (B-167).
    /// </summary>
    public const string BudgetToken = "{{maxChars}}";

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

    /// <summary>Whether the text still carries the unresolved <see cref="BudgetToken"/>.</summary>
    public static bool HasBudget(string text)
        => !string.IsNullOrEmpty(text) && text.Contains(BudgetToken, StringComparison.Ordinal);

    /// <summary>
    /// Resolves the budget in a compiler instruction text from the checkpoint's OWN profile value. Callers that hand a
    /// text to a model must run this first: an instruction still carrying the token tells the model nothing about
    /// length. A non-positive budget is refused rather than substituted.
    /// </summary>
    public static string ApplyBudget(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxChars <= 0)
        {
            throw new InvalidOperationException(
                $"A compiler instruction's prompt budget must be positive; the profile declares {maxChars}, so no "
                + "budget can be stated to the model.");
        }

        return HasBudget(text)
            ? text.Replace(BudgetToken, maxChars.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : text;
    }

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
        - Honor beat-stated clothing exactly; describe explicitness as the EXPLICITNESS line states when one is present, otherwise to match what the scene depicts.
        - Never use Pony-style vocabulary: no score_9, no rating_explicit/questionable/safe, no 1girl/1boy/2people count tags, no danbooru tokens.
        - Choose explicitness from the depicted content: describe explicit sexual acts or visible genitals explicitly, suggestive content or partial nudity as questionable, and non-sexual content as safe. Base it on the EXPLICITNESS line when one is present, otherwise on the scene. Use concrete anatomical language for explicit content and imply rather than spell out questionable content.
        - Keep the ENTIRE prompt under {BudgetToken} characters. Short and concrete beats verbose.
        - Return ONLY the final image prompt as plain text. No commentary, quotes, or markdown.

        - Follow the EXPLICITNESS line when one is present; otherwise follow the depicted scene content exactly.
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
        1. POV FRAMING: Render the scene strictly from the PRODUCTION POV character's viewpoint: put the camera at that character's own eye height, aim it along that character's recorded sightline, and render only what that camera sees from that height — a kneeling viewpoint looks up at someone standing over it, a standing viewpoint looks down at someone kneeling. Show only the people the POV projection puts in frame, and NEVER show the viewpoint itself: no face, head, or anatomy of the POV character. If the POV is Omniscient, show the full scene with all characters visible and state each person's LEFT/CENTER/RIGHT placement and relative depth explicitly.
        2. NO NAMES / RELATIONS / OWNERSHIP: appearance only (build, hair style/color, skin tone, clothing type+color, visible pose).
        3. RENDERABLE-ONLY: include only the frozen, visually present instant. Omit narrative distance, intent, metaphor, and off-screen facts.
        4. GENDER AND COUNT, NO ATTRIBUTE BLEED: state each person's gender and number, and describe each person as ONE self-contained clause (appearance + clothing together) — never interleave attributes across people.
        5. CLOTHING IS A SAFETY ANCHOR: always describe each person's clothing unless the brief explicitly implies nudity.
        6. TIGHT PHOTO CAPTION: lead with framing and the main subject in the first sentence; keep the ENTIRE caption under {{maxChars}} characters; use photorealistic cues (35mm, natural skin texture, shallow depth of field).
        7. ELEMENT ORDER: main subject (count and gender first: how many people, and each person's gender) -> physical traits -> pose and expression -> clothing or nudity plus accessories -> camera framing -> lighting and mood -> environment -> visual style and medium. Cover each once and never repeat a concept or descriptor.
        8. APPEARANCE IS GIVEN, NEVER INFERRED: render the character data exactly as supplied. Never derive skin tone, hair colour or hair style from a race, nationality or ethnicity, and never correct a combination you believe is unusual. If a trait is absent, omit it rather than inventing one.
        9. AGE IS A LIFE STAGE, NEVER A NUMBER, and never a word that could read as a minor (young adult, middle-aged, mature, elderly). No resolution, aspect ratio or pixel count in the caption — those travel in the image settings.
        10. NO FILLER OR PRAISE WORDS: no masterpiece, best quality, high quality, 8K, award-winning. Style, medium and texture cues carry the realism instead. When clothing changes, stay in the same category (a dress becomes another dress, never trousers).
        11. EXPLICITNESS: describe explicitness as the EXPLICITNESS line states. When it says SFW, remove or soften explicit nudity and acts and keep clothing stated even where the brief implies nudity. When it says EXPLICIT, never soften, censor or elide. When no line is present, match the depicted scene.
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
        - Immediately after the quality tags, choose the Pony rating tag from what the scene depicts: rating_explicit for explicit sexual acts or visible genitals, rating_questionable for suggestive content or partial nudity, and rating_safe for non-sexual content. Base it on the EXPLICITNESS line when one is present, otherwise on the depicted content.
        - Add a danbooru-style count tag (1boy, 1girl, 2people, 1girl and 1boy) matching the number of people in frame. This prevents the model merging people into one figure.
        - Describe each character with 3-6 SHORT visual tags (hair, eyes, body type, age, key clothing) — never a metadata block, never 'Age: 51; Height: 5'8"; Body type: curvy', never 'Appearance — ...'. Use concrete single tokens (e.g. chubby, not 'full figure').
        - Fold the scene into a few short tags: location, time of day, lighting, mood. Do not repeat the same fact twice.
        - Add one explicit camera/view tag (e.g. front view, eye level, from side).
        - Honor beat-stated clothing exactly; only use nudity when the beat explicitly implies it.
        - For explicit scenes use concrete anatomical language; for safe/questionable scenes imply rather than spell out.
        - Keep the ENTIRE prompt under {BudgetToken} characters and under ~40 tags. Short and dense beats verbose.
        - Return ONLY the final comma-separated image prompt as plain text. No commentary, quotes, or markdown.
        - Use female/male (danbooru vocabulary) rather than woman/man when a single gender tag fits the character.
        - The Pony family spans the base V6 checkpoint and photorealistic human merges (e.g. Pony Realism) that all read the same danbooru tags. Do not force a cartoon/anime style, and do not invent style words the scene does not state.

        - Choose the rating tag and scene explicitness from the EXPLICITNESS line when one is present; otherwise from the depicted content.
        """;

    /// <summary>
    /// Pony tag dialect, canonical-brief-shaped. The <c>{{style}}</c> and <c>{{size}}</c> placeholders are literal
    /// tokens the canonical path substitutes later, not interpolation holes.
    /// </summary>
    public static readonly string PonyTagsCanonical = $$"""
        Convert the supplied immutable canonical Still brief into one short dense comma-separated prompt for the PONY DIFFUSION V6 XL image model. Do not invent or rediscover story facts.
        Start verbatim with: {{PonyQualityTags}}, then choose the rating tag from the EXPLICITNESS line when one is present, otherwise from the depicted content: rating_explicit for explicit sexual acts or visible genitals, rating_questionable for suggestive content or partial nudity, and rating_safe for non-sexual content.
        Then include the exact visible cast count, short visual identity/wardrobe/action tags, location, lighting, mood, one camera-view tag, and the {StyleToken} and {SizeToken} placeholders.
        Keep every visible person's tags in its OWN self-contained cluster (one 1girl run, one 1boy run) and never merge or reorder attributes between people. Repeat each person's age token — Pony/Pony Realism faces skew young, so age must be stated explicitly and more than once.
        Use female/male danbooru vocabulary. The checkpoint may be a photorealistic Pony merge (e.g. Pony Realism) or the base V6 checkpoint — both read dense danbooru tags; do not impose a cartoon/anime style.
        Keep the result under 800 characters and about 40 tags. Return only the final prompt as plain text.
        """;

    /// <summary>
    /// Qwen-Image-2.1 long-form dialect, beat-shaped. Route 1 of B135-008: the shape and the prohibitions are adopted
    /// from the vendor's own prompt-enhancer t2i system prompt (the eight-step observer-description procedure, ~20
    /// sentences / 400-500 words, no quality boosters, age as a life stage never a number, enumerate-never-summarise,
    /// ratio never in the prose) — the vendor's rules, written in our words and cited to
    /// <c>specs/Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md</c> §4 and §8. The byte-exact
    /// vendor prompt is route 2 and is NOT this text.
    /// <para>
    /// <b>Rewritten 2026-10-25 (operator, reading the compiled prompt):</b> "telling it qwen 2.1 means nothing to the
    /// model we are using" and "NEVER PONY VOCABULARY means nothing to the model". The writing model is a small local
    /// instruction model; the target family's NAME and another family's dialect name are metadata it cannot act on.
    /// Following the compiler standards' rule 8 (no identity fluff: ground the model, state explicit rules, and show a
    /// concrete example of the target output) the text now opens with what the output IS, states the anatomy as
    /// "what makes a prompt of this kind good", keeps every prohibition as the LITERAL token list to avoid, and ends
    /// with one worked example of the shape. The example also demonstrates the two rules the live failures broke: the
    /// camera holder is never depicted, and a measure taken against their body is stated against the CAMERA ("at the
    /// camera's own waist height") rather than left as a dangling comparison the model resolves by inventing one
    /// ("hips raised to about chest level", SceneImage 3ade4908).
    /// </para>
    /// <para>
    /// The length contradiction is gone too: "about 20 sentences" beside "never pad to reach a length — every sentence
    /// must state a recorded fact" left a sparse brief unwritable, so rule 7 now says a sparse brief is a reason to
    /// describe the SAME frame in more detail, never to invent and never to stop early.
    /// </para>
    /// </summary>
    public static readonly string Qwen21Beat = """
        You write the text prompt an image model will follow. That model reproduces the picture your words describe and nothing else: it cannot read the story below, it does not know who anyone is, and it does not interpret intent.

        TASK: turn the story prose below into ONE continuous paragraph of present-tense, third-person English describing a photograph that has already been taken.

        WHAT MAKES A PROMPT OF THIS KIND GOOD (follow this order):
        1. Open with one sentence of about 20 words naming the medium ("a photorealistic photograph of"), the main subject, and the setting or palette.
        2. Place the subject: where they sit in the frame and how far from the camera.
        3. Give the pose and what each part of the body is doing - which limb touches what, in which direction, at what height.
        4. Give each person's head and face, then body, then clothing or nudity.
        5. Give the setting, then one sentence of light: its source, direction, quality, and the shadows or highlights it produces.
        6. Close with one whole-frame sentence covering composition, palette and mood.
        Open around a third of the sentences on a positional phrase ("In the foreground...", "To the left..."). Give every colour a modifier and every object its material. Enumerate rather than summarise.
        7. Use the whole length budget (about 20 sentences / 400-500 words), whatever the length of the source. A short source is a reason to describe the SAME frame in more detail - distance, texture, shadow, the exact angle of a limb - never a reason to stop early, to repeat a fact already stated, or to invent a body part, a garment or an object the source does not support.

        RULES:
        - ONE paragraph. No line breaks, no bullet points, no headings, no quotes, no commentary. Return only the prompt.
        - Present tense, third person, declarative. No "I", "me", "my", "we", "our". Do not hedge with "appears to be" or "seems to".
        - State how many people are in frame and each one's gender, then describe each person as one self-contained run of sentences. Never interleave two people's attributes.
        - PLACEMENT AND RELATION ARE FACTS. When the camera is EXTERNAL (nobody's eyes), every person's placement in the picture (LEFT, CENTER or RIGHT, and foreground, midground or background) and the arrangement between them (who is behind whom, who stands between whom and the camera) is RECORDED DATA. State all of it, however short the prompt is: omitting it is how a two-person scene came back as two people standing apart with no relation to each other (measured 2026-10-26).
        - State the camera's own angle as the request records it ("viewed from the side", "from above", "at eye level"), never a different one and never a vague "view".
        - A CONTACT IS A SENTENCE, NOT A NOUN PHRASE: write whose part, what it touches and how ("his hips are pressed flat against her rear"). Never make a body part the subject of the sentence and never attach clothing to a body part.
        - DECOMPOSE THE POSE: one clause per body part (torso, hips, forearms, hands, knees, feet). A summary such as "bending over" alone does not tell the image model where anything is.
        - Names, relationships and ownership mean nothing to the image model: describe each person by observable surface only, and by where they are in the frame.
        - Describe only what the source states. If a trait is absent, leave it out; never infer skin tone, hair, build or clothing from a name, a nationality or a relationship, and never correct a combination you find unusual.
        - Age is a life stage - young adult, middle-aged, mature, elderly - never a number and never a word that could read as a minor.
        - Name each person's clothing, or state that they are nude. Clothing stays in the same category from sentence to sentence.
        - Describe explicit content explicitly: name the acts and the visible anatomy in concrete language. Never soften, censor, elide or fade to black.
        - Do not write quality or camera-equipment tokens: masterpiece, best quality, high quality, highly detailed, award-winning, 8K, 4K, HDR, 35mm, natural skin texture, skin pores, shallow depth of field, sharp focus, tack sharp, film grain.
        - Do not write resolution, aspect ratio or pixel count - those are set outside the prompt.
        - Do not write tags, weights or comma-separated keyword lists (score_9, score_8_up, rating_explicit, 1girl, 1boy, 2people). The image model reads prose, so write prose.
        - The person holding the camera is never in the picture: never describe their face, head or body. Only the parts listed under CAMERA HOLDER'S VISIBLE BODY may be written, phrased as that section phrases them.
        - Write in English.

        EXAMPLE OF THE TARGET SHAPE (a first-person photograph; the camera holder is never depicted):
        A photorealistic photograph taken from a standing adult's own eye height inside a shuttered workshop, showing a young woman bent forward over a scarred wooden bench with her back to the camera, her feet planted apart on a swept concrete floor. Her shoulders are level and low over the bench, her forearms flat along the boards, her hips raised behind her at the camera's own waist height. She is nude, her skin creased along the underside of her ribs where the bench edge presses. Beyond her the corrugated wall carries a long grey scuff, and a tin shelf above it holds three rusted cans. Thin bars of daylight come through the louvres at her left, striping her flank and the boards in hard pale bands and leaving her lower back in shadow. The picture is close, still and dim, its greys warmed by the daylight stripes.

        Return ONLY the final image prompt as plain text. No commentary, quotes or markdown.
        """;

    /// <summary>
    /// Qwen-Image-2.1 long-form dialect, canonical-brief-shaped (B-104/B-103 part B). Same route-1 source as
    /// <see cref="Qwen21Beat"/> (cited there), adapted to the immutable canonical Still brief: expand the brief, do not
    /// add to it. Same prohibitions - no quality boosters, no SDXL style tail, no Pony vocabulary, no ratio in prose.
    /// </summary>
    public static readonly string Qwen21Canonical = """
        You write the text prompt an image model will follow. That model reproduces the picture your words describe and nothing else: it cannot read the brief below, it does not know who anyone is, and it does not interpret intent.

        TASK: turn the brief below into ONE continuous paragraph of present-tense, third-person English describing a photograph that has already been taken.

        WHAT MAKES A PROMPT OF THIS KIND GOOD (follow this order):
        1. Open with one sentence of about 20 words naming the medium ("a photorealistic photograph of"), the main subject, and the setting or palette.
        2. Place the subject: where they sit in the frame and how far from the camera.
        3. Give the pose and what each part of the body is doing, using the brief's own geometry - which limb touches what, in which direction, at what height.
        4. Give each person's head and face, then body, then clothing or nudity.
        5. Give the setting as far as it may be described, then one sentence of light: its source, direction, quality, and the shadows or highlights it produces.
        6. Close with one whole-frame sentence covering composition, palette and mood.
        Open around a third of the sentences on a positional phrase ("In the foreground...", "To the left..."). Give every colour a modifier and every object its material. Enumerate rather than summarise.
        7. Use the whole length budget (about 20 sentences / 400-500 words). A brief that records little is a reason to describe the SAME frame in more detail - distance, texture, shadow, the exact angle of a limb - never a reason to stop early, to repeat a fact already stated, or to invent a garment, a body part, an object or an event the brief does not record.

        RULES:
        - ONE paragraph. No line breaks, no bullet points, no headings, no quotes, no commentary. Return only the prompt.
        - Present tense, third person, declarative. No "I", "me", "my", "we", "our". Do not hedge with "appears to be" or "seems to".
        - State how many people are in frame and each one's gender, then describe each person as one self-contained run of sentences. Never interleave two people's attributes.
        - PLACEMENT AND RELATION ARE FACTS. When the camera is EXTERNAL (nobody's eyes), every person's placement in the picture (LEFT, CENTER or RIGHT, and foreground, midground or background) and the arrangement between them (who is behind whom, who stands between whom and the camera) is RECORDED DATA. State all of it, however short the prompt is: omitting it is how a two-person scene came back as two people standing apart with no relation to each other (measured 2026-10-26).
        - State the camera's own angle as the request records it ("viewed from the side", "from above", "at eye level"), never a different one and never a vague "view".
        - A CONTACT IS A SENTENCE, NOT A NOUN PHRASE: write whose part, what it touches and how ("his hips are pressed flat against her rear"). Never make a body part the subject of the sentence and never attach clothing to a body part.
        - DECOMPOSE THE POSE: one clause per body part (torso, hips, forearms, hands, knees, feet). A summary such as "bending over" alone does not tell the image model where anything is.
        - Names, relationships and ownership mean nothing to the image model: describe each person by observable surface only, and by where they are in the frame.
        - Describe only what the brief records. If a trait is absent, leave it out; never infer skin tone, hair, build or clothing from a name, a nationality or a relationship, and never correct a combination you find unusual.
        - Age is a life stage - young adult, middle-aged, mature, elderly - never a number and never a word that could read as a minor.
        - Name each person's clothing, or state that they are nude. Clothing stays in the same category from sentence to sentence.
        - Describe explicit content explicitly: name the acts and the visible anatomy in concrete language. Never soften, censor, elide or fade to black.
        - Do not write quality or camera-equipment tokens: masterpiece, best quality, high quality, highly detailed, award-winning, 8K, 4K, HDR, 35mm, natural skin texture, skin pores, shallow depth of field, sharp focus, tack sharp, film grain.
        - Do not write resolution, aspect ratio or pixel count - those are set outside the prompt.
        - Do not write tags, weights or comma-separated keyword lists (score_9, score_8_up, rating_explicit, 1girl, 1boy, 2people). The image model reads prose, so write prose.
        - The person holding the camera is never in the picture: never describe their face, head or body. Only the parts listed under CAMERA HOLDER'S VISIBLE BODY may be written, phrased as that section phrases them.
        - Write in English.

        EXAMPLE OF THE TARGET SHAPE (a first-person photograph; the camera holder is never depicted):
        A photorealistic photograph taken from a standing adult's own eye height inside a shuttered workshop, showing a young woman bent forward over a scarred wooden bench with her back to the camera, her feet planted apart on a swept concrete floor. Her shoulders are level and low over the bench, her forearms flat along the boards, her hips raised behind her at the camera's own waist height. She is nude, her skin creased along the underside of her ribs where the bench edge presses. Beyond her the corrugated wall carries a long grey scuff, and a tin shelf above it holds three rusted cans. Thin bars of daylight come through the louvres at her left, striping her flank and the boards in hard pale bands and leaving her lower back in shadow. The picture is close, still and dim, its greys warmed by the daylight stripes.

        Return ONLY the final image prompt as plain text. No commentary, quotes or markdown.
        """;

    /// <summary>
    /// The REFERENCE-ANCHORED contract for a canonical Still brief whose bound images already supply some element
    /// classes — the counterpart to <see cref="Qwen21Canonical"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vendor documents two prompt shapes, and the length rule belongs to only one of them
    /// (<c>research/qwen-2-1-prompt-enhancer.md</c>): §4 <c>t2i</c> wants ~20 sentences and says "a short brief means
    /// inventing most of the frame, not writing less", because there the PROMPT is the only carrier of the picture; §5
    /// <c>edit</c> says the opposite — "anchor on the image. Nothing invented; an uncertain detail is omitted rather
    /// than specified", and "point at the image rather than describing features in words".
    /// </para>
    /// <para>
    /// The canonical path was compiled under §4 even when identity, body and location references were bound, so three
    /// of the four element classes were REMOVED from the brief while the contract demanded 400-500 words: the compiler
    /// resolved the contradiction by inventing ("pine sap and rust are detectable in the air", "hips raised to about
    /// chest level", a privacy disclaimer about removed clothing — SceneImage 3ade4908, 2026-10-25). Length follows the
    /// facts the PROMPT must carry, which is what this contract states.
    /// </para>
    /// <para>
    /// The slot order is the operator's own working example: medium + shot + angle, subject with its tag, action and
    /// the surface it happens on, location with its tag, one clause per pose fact, orientation to the camera, wardrobe
    /// or nudity, light (source and direction), one closing clause. Only classes the references do NOT supply are
    /// written — the coverage decides, never a fixed rule.
    /// </para>
    /// <para>
    /// <b>The worked example carries no tag numbers (fixed 2026-10-27, SceneImage a53cd560).</b> The example was written
    /// from the operator's brief for a scene with exactly two bound images, so it showed the place as
    /// <c>&lt;image2&gt;</c>. A two-person scene binds THREE images — two bodies and one location — which makes the place
    /// <c>&lt;image3&gt;</c>, and the local compiler copied the example's number instead of the REFERENCE IMAGES block the
    /// request had just handed it. The caption said "in a shed &lt;image2&gt;" (a person's body plate) while the anchor
    /// appended at render said "the place shown in &lt;image3&gt;", so one prompt called one image the room and
    /// contradicted itself about another. A tag inside a worked example is copied by its NUMBER, so this text now
    /// carries none: the numbering is stated to come from the request's own block, and the compile validator refuses a
    /// caption that points the place at a person's tag or never names the location tag at all.
    /// </para>
    /// </remarks>
    public static string Qwen21CanonicalAnchored(IntentReferenceCoverage coverage)
    {
        var lines = new List<string>
        {
            "You write the text prompt an image model will follow. That model reproduces the picture your words describe and nothing else: it cannot read the brief below, it does not know who anyone is, it cannot see the reference images as concepts, and it does not interpret intent.",
            string.Empty,
            "TASK: turn the brief below into ONE short paragraph of present-tense, third-person English describing a photograph that has already been taken. Several elements of this picture arrive as BOUND REFERENCE IMAGES; the prompt must carry only what those images cannot. Every bound image has a number in the request's REFERENCE IMAGES block; those numbers are the only tags you may write, and a tag may only ever point at the image that block assigns it to.",
            string.Empty,
            "WRITE THE PROMPT IN THIS ORDER, one clause per fact and no padding:",
            "1. Medium, shot size and camera angle, in one short phrase (\"A photorealistic medium shot at eye level\").",
            "2. The subject, followed by the tag the REFERENCE IMAGES block gives that person (\"of a woman \" and then that tag).",
            "3. What the subject is doing and the surface it happens on (\"bending over a workbench\").",
            "4. The location, followed by the tag the block gives the LOCATION image (\"in a shed \" and then that tag — the location's own tag, never a person's).",
            "4b. WHERE EVERYONE IS IN THE PICTURE. If the request describes an EXTERNAL camera (nobody's eyes), state each person's placement — LEFT, CENTER or RIGHT, and foreground, midground or background — and state how the people are arranged relative to one another (who is behind whom, who is between whom and the camera). This is recorded data, not invention: write it even when the prompt is otherwise short.",
            "4c. THE CAMERA'S OWN ANGLE. State the viewpoint the picture was taken from as the request records it (\"viewed from the side\", \"from above\", \"at eye level\"), never a different one and never a vague \"view\".",
            "5. One clause per pose fact: each body part, where it rests, and what it touches, using the brief's own geometry.",
            "6. How the subject is oriented to the camera.",
            "7. What the subject is wearing, or that they are nude.",
            "8. The light: its source and its direction only.",
            "9. One closing clause on composition, palette or mood.",
            string.Empty,
            "RULES:",
            "- Write ONLY what a camera could record: people, bodies, positions, surfaces, materials, colours, light and shadow. Never write a smell, a sound, a temperature, or anything described as being \"in the air\".",
            "- Write ONLY what the brief records, and only the classes no reference image supplies. Never invent a garment, a body part, an object, a surface or an event to reach a length, and never add a privacy or removal disclaimer.",
            "- Length follows the facts that remain. A short prompt that states every remaining fact is CORRECT here; there is no sentence count to reach and nothing to pad.",
            "- ONE paragraph. No line breaks, no bullet points, no headings, no quotes, no commentary. Return only the prompt.",
            "- Present tense, third person, declarative. No \"I\", \"me\", \"my\", \"we\", \"our\". Do not hedge.",
            "- Name each person's clothing or state that they are nude. Describe explicit content explicitly, in concrete anatomical language, without softening or eliding it.",
            "- Do not write quality or camera-equipment tokens: masterpiece, best quality, highly detailed, award-winning, 8K, 4K, HDR, 35mm, natural skin texture, shallow depth of field, sharp focus, film grain.",
            "- Do not write resolution, aspect ratio or pixel count.",
            "- Do not write tags, weights or comma-separated keyword lists (score_9, rating_explicit, 1girl, 1boy, 2people).",
            "- Name a reference image ONLY by its tag (<image1>, <image2>, ...), exactly as the request writes it, and never as \"the first image\" or \"the reference image\".",
            "- THE NUMBER COMES FROM THE REQUEST, NOT FROM THIS TEXT. Take every tag from the request's REFERENCE IMAGES block, exactly as numbered there. Do not renumber, do not infer a number from an example, and never assume the place is the second image: with two people bound, the two people take the first two tags and the place takes the third.",
            "- A PLACE IS POINTED AT THE LOCATION IMAGE'S TAG and at no other tag. A person's tag sitting beside a place noun tells the image model to take the room from that person's photograph.",
            "- WRITE EACH TAG AS PLAIN TEXT — the word image with the block's number in angle brackets. Never write a bracketed placeholder, never write the word \"tag\", never write a note to yourself, and never leave a slot empty: a tag that is not the block's plain-numbered tag binds nothing.",
            "- ORIENTATION TOWARD THE LENS IS ITS OWN FACT. State it only when the request states it. A person standing behind or in front of ANOTHER PERSON says nothing about which way they face the camera: \"Dean stands directly behind her\" is a fact about the two of them, and writing it as \"her back to the camera\" invents a viewpoint. When the request does not state the orientation, write a world target instead (\"facing the tin wall beyond the bench\") or leave it out.",
            "- The person holding the camera is never in the picture: never describe their face, head or body, except the parts the request lists under CAMERA HOLDER'S VISIBLE BODY.",
            "- Write in English.",
            "- PLACEMENT AND RELATION ARE FACTS, NOT DECORATION. When the request describes an EXTERNAL camera (nobody's eyes), every person's placement in the picture (LEFT, CENTER or RIGHT, and foreground, midground or background) and the arrangement between them (who is behind whom, who stands between whom and the camera) is RECORDED DATA: state all of it, in the prompt, however short the prompt is. Omitting it is how a two-person scene came back as two people standing apart with no relation to each other (measured 2026-10-26).",
            "- State the camera's own angle as the request records it (\"viewed from the side\", \"from above\", \"at eye level\"), never a different one and never a vague \"view\".",
            "- A CONTACT IS A SENTENCE, NOT A NOUN PHRASE. Write whose part, what it touches and in what manner — \"his hips are pressed flat against her rear\", \"his hands are closed on her thighs\". Never make a body part the subject of the sentence, never attach clothing to a body part, and never state a contact only as nouns.",
            "- DECOMPOSE THE POSE. When the request records the position of the body, write one clause per body part: torso, hips, forearms, hands, knees, feet. A summary such as \"bending over\" alone does not tell the image model where anything is.",
            string.Empty,
            "EXAMPLE OF THE TARGET SHAPE (two bound images; the camera holder is not depicted). The tags are LEFT OUT of this example on purpose: after the subject, write the tag the request's REFERENCE IMAGES block gives that person, and after the place, write the tag it gives the location image.",
            "A photorealistic medium shot at eye level of a woman bending over a workbench in a shed, her feet flat on the concrete and her forearms along the boards, facing the tin wall beyond the bench with a man standing behind her, nude, with slatted daylight falling across her flank from the left and striping the boards beneath her.",
            string.Empty,
            "Return ONLY the final image prompt as plain text. No commentary, quotes or markdown."
        };

        if (coverage.Appearance)
        {
            lines.Insert(5, "   The face and body arrive as a bound image: point at its tag and write no appearance detail at all.");
        }

        if (coverage.Setting)
        {
            lines.Insert(5, "   The room's structure arrives as a bound image: name the KIND of place and point at its tag; write no walls, floor, surfaces, fixtures or objects.");
        }

        if (coverage.Clothing)
        {
            lines.Insert(5, "   What the person wears arrives as a bound image: point at its tag and do not describe the garment.");
        }

        if (coverage.Pose)
        {
            lines.Insert(5, "   The stance arrives as a bound pose image: state the action and where the body is in the frame, and do not re-describe the skeleton's angles.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Qwen-Image-2.1 dialect for a User Intent draft that BINDS reference images: the ACTION, and nothing a reference
    /// already shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because <see cref="Qwen21Canonical"/> and a reference-bound intent are contractually incompatible.
    /// That prompt requires "ONE long, descriptive … full, concrete, present-tense account of the finished picture" under
    /// a long-form length budget, so the compiler must write a paragraph describing the room, the outfit and the
    /// objects — which is precisely what the bound images supply. Adding "do not describe those" beside it does not
    /// work: the dominant contract wins and the compiler invents instead.
    /// </para>
    /// <para>
    /// Measured on SceneImage f74058d3 (2026-10-18), identical bindings and code to a WORKING render three minutes
    /// earlier (b91598c2), with the ownership notice confirmed present in both compiler inputs:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The prompt invented "posts and taut lines … a fence and open ground" over a bound porch plate with a rotary
    /// clothesline and a flowerbed; the render followed the prose.
    /// </description></item>
    /// <item><description>
    /// It wrote "shorts and a top" — vague enough that the render came out bare — over a clothing reference showing a
    /// white tank top.
    /// </description></item>
    /// <item><description>
    /// It described a "woven plastic basket with a rounded rim" over an object reference of a white lattice basket.
    /// </description></item>
    /// </list>
    /// <para>
    /// So the fix is to stop asking for those sentences at all. Every visual element that a reference owns is REMOVED
    /// from this contract, which leaves the action, the pose, the expression, the interaction and the camera moment —
    /// the only things an image cannot carry.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// Composed from <see cref="IntentReferenceCoverage"/>, because the contract must state only what the bound images
    /// actually carry. Selecting it for ANY reference-bound intent while asserting all four classes as supplied is what
    /// left a clothing instruction unwritable: SceneImage a597ba82 (2026-10-18) bound an identity plate and a location
    /// plate only, asked for "naked from the waist up", and every compiled prompt was forbidden from saying so — so the
    /// render kept the garment on no matter what the operator wrote.
    /// </para>
    /// </remarks>
    public static string Qwen21CanonicalActionOnly(IntentReferenceCoverage coverage)
    {
        if (!coverage.Any)
        {
            throw new InvalidOperationException(
                "The reference-aware intent contract needs at least one supplied element; a reference-free intent is "
                + "compiled under the family's long-form contract.");
        }

        var text = new StringBuilder();
        text.AppendLine("Write a SHORT direction for Qwen-Image-2.1 describing what the operator's intent asks the figure to DO at this instant, and every element the intent states that no reference image supplies. Every visible element a reference image supplies is already carried by that image and must NOT be written.");
        text.AppendLine();
        text.AppendLine("Facts about this request that shape the answer:");
        if (coverage.Appearance)
            text.AppendLine("- The figure's build, face, hair and skin come from a bound image.");
        if (coverage.Clothing)
            text.AppendLine("- The clothing comes from a bound image.");
        if (coverage.Setting)
            text.AppendLine("- The room's structure comes from a bound image; its LIGHTING does not, so a lighting or atmosphere value the intent states is still written.");
        if (coverage.Objects)
            text.AppendLine("- Any object comes from a bound image.");
        if (!coverage.Clothing)
            text.AppendLine("- NO image supplies clothing, so the clothing state is the operator's to state: say what is worn and what is not, exactly as the intent has it.");
        text.AppendLine(coverage is { Setting: true, Appearance: true, Clothing: true }
            ? "- This prompt therefore carries ACTION ONLY: what the person is doing, how their body is positioned, where their weight sits, what their hands are doing, what their face and gaze are doing, and the instant the frame catches."
            : "- This prompt carries the action, plus whatever the intent states about the elements nothing else supplies.");
        text.AppendLine("- 40 to 120 words. One continuous paragraph. Do not add line breaks.");
        text.AppendLine();
        text.AppendLine("Rules:");

        var number = 0;
        void Rule(string body) => text.AppendLine($"{++number}. {body}");

        if (coverage.Setting)
        {
            Rule("DO NOT DESCRIBE THE ROOM'S STRUCTURE. Write no sentence about walls, floors, surfaces, fixtures, furniture or any background structure, and do not name a place. LIGHTING and ATMOSPHERE the intent states ARE written: the location reference decides where the figures are, not how the light falls.");
        }

        if (coverage.Clothing)
        {
            Rule("DO NOT DESCRIBE CLOTHING OR NUDITY. Name no garment, no fabric, no colour of clothing, and state nothing about what is or is not worn. The clothing reference decides all of it.");
        }
        else
        {
            Rule("STATE THE CLOTHING STATE. No image in this request supplies clothing, so the direction MUST state what is worn and what is not. When the intent removes a garment, say the garment is gone, say where it now is (on the ground, over a chair, in her hand), and say which parts of the body are bare. Write it as plainly as the intent does: do not soften it, do not omit it, and do not invent clothing the intent does not name.");
        }

        if (coverage.Objects)
        {
            Rule("DO NOT DESCRIBE ANY OBJECT. Do not name, size, colour or place a prop, tool or container. An object reference decides all of it, including whether it is held.");
        }

        if (coverage.Appearance)
        {
            Rule("DO NOT DESCRIBE APPEARANCE. No build, height, skin tone, hair, face, age or gender phrasing beyond the single word needed to say who acts (\"she\" or \"he\") — the identity and body references decide how the person looks.");
        }

        var everySentence = coverage.Clothing
            ? "EVERY SENTENCE IS ABOUT THE PERSON. Never write a sentence whose subject is an object or part of the setting — no \"a basket sits low beside her\", no \"the line runs across the frame\", no \"a fence stands behind her\". A thing stated as present is a thing DRAWN, layered on top of the object reference that already supplies it."
            : "EVERY SENTENCE IS ABOUT THE PERSON OR THE GARMENT THE INTENT REMOVES. Never write a sentence whose subject is the setting or an unrelated object: a thing stated as present is a thing DRAWN. A removed garment and where it lies ARE part of the action and must be stated.";
        if (coverage.Setting || coverage.Objects)
        {
            everySentence += " Measured: \"A laundry basket sits low beside her right calf\" and \"both hands are up at the line\" made the renderer draw a SECOND clothesline, strung across the frame beside the one already in the location image (SceneImage 87454bef, 2026-10-18).";
        }
        Rule(everySentence);

        if (coverage.Setting || coverage.Objects)
        {
            Rule("A FIXTURE THE ACTION USES IS ALREADY IN THE PICTURE. When her hands touch something that a reference already shows, name it only inside the verb phrase that says what the hands do (\"pegging a shirt to the line\", \"reaching down into the basket\") — never introduce it as a new thing, never say where it is, never give it material, colour or size, and never say \"another\", \"a second\" or \"a new\" one. The picture already contains it, and saying more than the verb produces a duplicate.");
        }

        Rule("WHAT TO WRITE: the action and its mechanics. Body attitude (bent forward at the waist, seated, leaning, mid-stride), limb and hand placement, where the weight is, the angle of the head, the line of the gaze, the expression, and the beat the frame catches.");
        Rule("NO NAMES, RELATIONS OR OWNERSHIP. Never emit story names, relationships or property.");
        Rule("POV FRAMING: render strictly from the PRODUCTION POV character's viewpoint, at that character's own eye height and aimed along that character's recorded sightline - a kneeling viewpoint looks up at someone standing over it, a standing viewpoint looks down at someone kneeling. Show only the people the POV projection puts in frame, and never show the viewpoint itself: no face, head, or anatomy of the POV character. If the POV is Omniscient, show the full scene with all characters visible and state each person's LEFT/CENTER/RIGHT placement and relative depth explicitly.");
        Rule("GENDER AND COUNT: state how many people and each person's gender explicitly so the model never merges or miscounts people.");
        Rule("NO QUALITY BOOSTERS, no SDXL style tail, no resolution, aspect ratio or pixel count, and never Pony vocabulary (no score_9, no rating_ tokens, no 1girl/1boy count tags).");
        Rule("Hedge what you cannot see the way an observer would (\"appears to be\", \"likely\"). The direction is always in English.");
        Rule("Never mention an image tag or the word reference: the tags are for the renderer, not for this paragraph.");
        text.Append("Return ONLY the direction as plain text. No commentary, quotes or markdown.");

        return text.ToString();
    }

    /// <summary>
    /// Krea 2 (Krea-2 Turbo) photographic-brief dialect, beat-shaped.
    ///
    /// <para>
    /// <b>Every rule below is a measured finding from the 59-cell proof matrix</b>
    /// (<c>helpers/local-comfyui-host/run-krea2-proof.ps1</c>, reviewed 2026-10-01), not a style preference:
    /// </para>
    /// <list type="number">
    /// <item>Krea 2 renders what it is TOLD, not tag soup: a comma list of body nouns fills the frame with that body
    /// and loses the head and the scene, while a photographic brief producing a complete figure. Explicit framing
    /// DEMANDS ("full body shot", "wide shot") never worked and are the worst offender - the model crops instead of
    /// obeying. These two rules are the single largest quality lever on this model.</item>
    /// <item>An ambiguous act clause mis-parses: "the woman kneeling in front of the man performing fellatio on him"
    /// reads as the MAN performing it. Both actors and who does what to whom must be explicit.</item>
    /// <item>A face-direction clause on an act ("both turning their heads toward the window light") suppresses the
    /// act - the model renders the faces instead.</item>
    /// </list>
    ///
    /// <para>
    /// This is deliberately a SEPARATE text from <see cref="NaturalLanguageBeat"/>, which instructs the model to lead
    /// with framing and allows an SDXL-family body vocabulary: those two instructions are the two defects above.
    /// </para>
    /// </summary>
    public static readonly string Krea2Beat = $"""
        Convert story prose into ONE photographic brief for Krea 2 (Krea-2 Turbo), a photorealistic text-to-image model that reads plain natural language. Write it the way a photographer describes a shot: what the picture is, who is in it, what they are doing, the lens, and the depth of field.

        Facts about this model that shape the prompt:
        - It reads ordinary sentences. It does not read tag lists, danbooru tokens, or attribute metadata blocks.
        - An explicit framing DEMAND is unreliable on this model and usually makes it worse: "full body shot", "wide shot", "close-up", "head to toe" and similar instructions make it crop the figure or drop the head instead of obeying. Describe what is in the picture and let the shot follow from that.
        - A list of body nouns ("large breasts, thick thighs, narrow waist") makes it fill the frame with that body and lose the head and the surroundings. Describe a person DOING something in a place, and let the body be part of that.
        - It does not know character names, relationships or ownership. Describe each person by appearance only.

        Rules:
        1. POV FRAMING: render strictly from the PRODUCTION POV character's viewpoint, at that character's own eye height and aimed along that character's recorded sightline - a kneeling viewpoint looks up at someone standing over it, a standing viewpoint looks down at someone kneeling. Show only the people the POV projection puts in frame, and never show the viewpoint itself: no face, head, or anatomy of the POV character. If the POV is Omniscient, show the full scene with all characters visible and state each person's LEFT/CENTER/RIGHT placement and relative depth explicitly.
        2. NAME BOTH PEOPLE AND SAY WHO IS DOING WHAT TO WHOM. State each person's gender explicitly so the model never merges or miscounts people, and when two people are in contact write the sentence so the actor of the act is unambiguous: "a woman kneels in front of a man and takes his penis in her mouth" - never "the woman kneeling in front of the man performing fellatio on him", which reads as the man performing it.
        3. ONE CLAUSE PER PERSON: describe each person as one self-contained clause (appearance plus clothing plus action). Never interleave one person's attributes into another person's clause.
        4. NO FRAMING DEMANDS: never write full body shot, wide shot, close-up, macro, head to toe, or any other instruction about how much of the subject to include. Describe the scene; the shot follows from it.
        5. NO BODY NOUN-LISTS: never describe a person as a comma-separated inventory of body parts or measurements. Fold the body into what is happening ("her hips rock against his"), never into a list.
        6. NO FACE-FACING CLAUSE ON AN ACT: when the scene is a sexual act, do not add a sentence about which way anyone's face is turned or what anyone is looking at. That clause competes with the act and the model renders the faces instead of the act.
        7. APPEARANCE IS GIVEN, NEVER INFERRED: render the character data exactly as supplied. Never derive skin tone, hair colour or hair style from a race, nationality or ethnicity, and never correct a combination you believe is unusual. If a trait is absent, omit it rather than inventing one.
        8. AGE IS A LIFE STAGE, NEVER A NUMBER, and never a word that could read as a minor: use young adult, middle-aged, mature or elderly.
        9. CLOTHING OR NUDITY from the depicted content. When clothing changes, stay in the same category (a dress becomes another dress, never trousers).
        10. EXPLICITNESS FROM THE DEPICTED CONTENT: describe explicit sexual acts and visible genitals in concrete anatomical language, suggestive content or partial nudity plainly, non-sexual content plainly. Base it on the EXPLICITNESS line when one is present, otherwise on the scene.
        11. PHOTOREALISTIC BY DEFAULT: close the brief with the photographic facts that carry realism - photorealistic, editorial photograph, 35mm, shallow depth of field, natural skin texture. Never add praise words: no masterpiece, best quality, high quality, 8K, award-winning.
        12. NEVER PONY VOCABULARY: no score_9, no rating_explicit/questionable/safe, no 1girl/1boy/2people count tags, no danbooru tokens.
        13. Keep the ENTIRE prompt under {BudgetToken} characters: one short paragraph in plain sentences. Return ONLY the final image prompt as plain text. No commentary, quotes or markdown.
        """;

    /// <summary>
    /// Krea 2 photographic-brief dialect, canonical-brief-shaped (B-104/B-103 part B). The same measured rules as
    /// <see cref="Krea2Beat"/> - most importantly: expand the brief, never turn it into a body noun-list, and never
    /// add a framing demand the brief did not state.
    /// </summary>
    public static readonly string Krea2Canonical = $"""
        Convert the canonical Still brief below into ONE photographic brief for Krea 2 (Krea-2 Turbo), a photorealistic text-to-image model that reads plain natural language.

        Facts about this model that shape the prompt:
        - It reads ordinary sentences. It does not read tag lists, danbooru tokens, or attribute metadata blocks.
        - An explicit framing DEMAND is unreliable on this model: "full body shot", "wide shot", "close-up" and similar instructions make it crop the figure or drop the head instead of obeying. Describe what is in the picture and let the shot follow from it.
        - A list of body nouns makes it fill the frame with that body and lose the head and the surroundings. Describe a person DOING something in a place.
        - It does not know character names, relationships or ownership. Describe every person by appearance only.

        Rules:
        1. POV FRAMING: render strictly from the PRODUCTION POV character's viewpoint, at that character's own eye height and aimed along that character's recorded sightline - a kneeling viewpoint looks up at someone standing over it, a standing viewpoint looks down at someone kneeling. Show only the people the POV projection puts in frame, and never show the viewpoint itself: no face, head, or anatomy of the POV character. If the POV is Omniscient, show the full scene with all characters visible and state each person's LEFT/CENTER/RIGHT placement and relative depth explicitly.
        2. NAME BOTH PEOPLE AND SAY WHO IS DOING WHAT TO WHOM. State each person's gender explicitly, and when two people are in contact write the sentence so the actor of the act is unambiguous. Never emit a clause that can be read as the wrong person performing the act.
        3. ONE CLAUSE PER PERSON: appearance plus clothing plus action together, one self-contained clause each. Never interleave one person's attributes into another's clause.
        4. NO FRAMING DEMANDS: never add an instruction about how much of the subject to include. Render the brief's framing only as a description of what is visible.
        5. NO BODY NOUN-LISTS: never turn the brief into a comma-separated inventory of body parts or measurements.
        6. NO FACE-FACING CLAUSE ON AN ACT: when the scene is a sexual act, do not add a sentence about which way anyone's face is turned. It competes with the act and the model renders the faces instead.
        7. APPEARANCE IS GIVEN, NEVER INFERRED: render the character data exactly as supplied, and omit a trait that is absent rather than inventing one.
        8. AGE IS A LIFE STAGE, NEVER A NUMBER, and never a word that could read as a minor (young adult, middle-aged, mature, elderly).
        9. CLOTHING OR NUDITY from the depicted content; a changed garment stays in its own category (a dress becomes another dress, never trousers).
        10. EXPLICITNESS FROM THE DEPICTED CONTENT: explicit sexual acts and visible genitals in concrete anatomical language, suggestive content or partial nudity plainly, non-sexual content plainly. Base it on the EXPLICITNESS line when one is present, otherwise on the scene.
        11. PHOTOREALISTIC BY DEFAULT: carry the photographic facts the brief states, and close with the medium - photorealistic, editorial photograph, 35mm, shallow depth of field, natural skin texture. Never add praise words: no masterpiece, best quality, high quality, 8K, award-winning. No resolution, aspect ratio or pixel count in the prose.
        12. NEVER PONY VOCABULARY: no score_9, no rating_explicit/questionable/safe, no 1girl/1boy/2people count tags, no danbooru tokens.
        13. Keep the ENTIRE prompt under {BudgetToken} characters: one short paragraph in plain sentences. Expand the brief - do not invent or rediscover story facts, and do not add to what the brief states. Return ONLY the final image prompt as plain text. No commentary, quotes or markdown.
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
        SceneImageModelFamily.QwenImage21 => canonical ? Qwen21Canonical : Qwen21Beat,
        SceneImageModelFamily.Krea2 => canonical ? Krea2Canonical : Krea2Beat,
        SceneImageModelFamily.Api => canonical ? NaturalLanguageCanonical : NaturalLanguageBeat,
        _ => throw new InvalidOperationException(
            $"No compiler instruction text exists for model family '{family}'. A checkpoint whose family is unknown "
            + "cannot be compiled for, and guessing a dialect is how a prompt ends up in a language its model ignores.")
    };
}
