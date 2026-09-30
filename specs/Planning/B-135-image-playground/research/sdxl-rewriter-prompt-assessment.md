# Operator-supplied SDXL rewriter prompt — assessment + adapted form

**Provenance:** operator-supplied 2026-09-30. **Not vendor-researched** — it is not from BigLust's or Juggernaut's
authors, so it cannot be recorded as author guidance (compiler-standards governance rule 4: only the model's own
official/Civitai/author guide counts as researched settings). If adopted, `ResearchSource` must say
**"operator-supplied baseline, not author-researched"**.

**Verdict:** adopt the *structure and the restraint*, **reject four clauses** — one of which is factually wrong, and
two of which would break hard rules in this repo.

---

## 1. It is written for a tag model, not SDXL

The strongest single clue is **"Never use underscores in any tags."** Underscores are a Danbooru/Pony convention
(`long_hair`, `blue_eyes`); they are meaningless in SDXL prose. That clause only exists for a tag model.

So does **"Output must be a single-line, comma-separated list of visual tags"**, which is the opposite of what this
repo has established for the SDXL family. `scene-image-prompt-compiler-standards.instructions.md` §2.2 is explicit:
this family reads **natural-language photographic briefs** — *"Tokens or natural sentences both work — this family is
not Pony; prefer natural-language photographic briefs for our use case"* — and `SdxlSceneImagePromptBuilder`'s own
system prompt says *"not comma-tag soup, not danbooru tags, not attribute metadata blocks"*. §4's quick-reference
table exists precisely because **the vocabularies must not be mixed**.

Note the nuance: comma-separated *descriptive clauses* are fine for SDXL (the Juggernaut guide's own examples are
comma-separated clauses). What is not fine is tag vocabulary. The fix is the vocabulary, not the commas.

## 2. Adopt / adapt / reject

| Clause | Call | Why |
|---|---|---|
| "Only consider the current input. Do not retain past prompts or context." | **ADOPT** | Matches our compiler's statelessness. But see §3 — it contradicts two of this prompt's own rules. |
| Element order: subject → physical traits → pose/expression → clothing/accessories → camera → lighting/mood → environment → style/medium | **ADOPT (with one addition)** | Lines up with the Juggernaut 17-component anatomy and canon §2.1 ("subject at the START", camera named, style anchored). **Addition required:** canon §2.6 makes *count + gender first* mandatory for multi-person frames, or SDXL draws one person. The supplied list omits this. |
| "Preserve core identity traits… Preserve existing pose, perspective, or key body parts if mentioned." | **ADAPT** | Preservation is right, **but bounded by the profile's `PoseInText` capability** (B-135 D18). Complex or multi-person pose in text is `Forbidden` on BigLust/Juggernaut — the canon records plain-text multi-person separation working only "some of the time (~75%)". So: preserve pose **only to the extent the profile permits**, otherwise refuse and name the missing structural source. |
| "Do not include filler terms like 'masterpiece' or 'high quality'." | **ADOPT** | Independently corroborated: the official Qwen-Image-2.1 Prompt Enhancer forbids quality boosters by name ("masterpiece", "8K", "highly detailed", "award-winning"). Style/medium/texture anchors (`photorealistic`, `35mm`, `natural skin texture`) are canon §2.1 components and stay — they are not boosters. |
| "Do not repeat the same concept or descriptor more than once." | **ADOPT** | Matches the existing SDXL builder rule ("Do not repeat the same fact twice"). |
| "If the original prompt includes N-S-F-W or sensual elements, maintain that same level… If not, do not introduce N-S-F-W content." | **ADOPT** | Exactly our canon: *"describe explicitness to match what the scene depicts, not a user toggle or narrative phase."* Level parity, both directions. |
| "When modifying clothing, stay within the same category." | **ADOPT** | Good, cheap, and prevents the drift that makes a re-render unreproducible. |
| "Replace vague elements with creative, coherent alternatives." | **ADAPT** | Bound it to **visual** detail. Unbounded "creative" invites unrenderable narrative, which canon §2.3 rule 3 forbids ("Renderable-only… omit narrative distance, intent, metaphor"). |
| "Never use numeric ages." | **ADOPT** | Matches canon, and the official Qwen PE independently: *"Age is a life stage or a decade — never a number of years."* |
| **"Do not go older than middle-aged unless specified."** | **REJECT** | An unjustified clamp. `PhysicalAttributes.Age` is **authored character data**; a rewriter does not get to age a character down. |
| **The youth descriptors "young", "teenager"** | **REJECT for adult content** | In an adult-image pipeline these are not acceptable age words. Canon Pony rule: *"repeat mature-age tokens because the model's faces skew young."* Use life-stage bands that cannot read as a minor (young adult / middle-aged / mature / elderly). |
| **"use realistic combinations based on race or nationality… 'dark black skin, red hair' is not [acceptable]… For Mexican or Latina characters, use natural hair colors and light to medium brown skin tones unless context clearly suggests otherwise"** | **REJECT** | Two reasons. **(a) It is factually wrong** — naturally red hair occurs in people of African descent; this is a stereotype, not a realism rule. **(b) It breaks a hard repo rule**: the copilot instructions forbid guessed substitute values (`No Fallbacks`), and the compiler must render each character's **authored** appearance. `CharacterBodyCard`/`PhysicalAttributes` (`SkinTone`, `HairColour`, `HairStyle`, `EyeColour`, `Ethnicity`) is authoritative, and the body-card work established that authored appearance **outranks** attribute-derived text. Inferring skin tone and hair colour from a nationality is exactly the guessed-value path this repo bans. Appearance comes from the character record; if a field is absent it is left absent, never invented from race. |
| **"Wrap the final prompt in triple backticks (```)… Do not include any other output."** | **REJECT** | That is a chat-UI delimiter for a human copy-paste workflow. Our output goes into a DB record and then into a provider request; markdown fences must never reach either. The app already has a parse contract — `Validate: JSON envelope {prompt, excerpt} OR plain text, failing fast on empty/overlong` — and output must resolve to a **single** prompt string. Keep ours. |
| "Never output multiple prompts, alternate versions, or explanations." | **ADOPT** | Same shape as our single-prompt contract. |
| "Never include narrative text, summaries, or explanations before or after." | **ADOPT** | Matches §2.3 rule 3 and the existing builder. |
| "If a race or nationality is specified, do not change it or generalize it." | **ADOPT** | Consistent with "preserve authored traits". (The prompt's own violation of this spirit is the inference clause above.) |

## 3. Two internal contradictions (both resolved in our favour)

1. **"Only consider the current input. Do not retain past prompts or context."** vs
   **"If repeating prompts, vary what you change — rotate features…"**, **"If a trait was previously exaggerated… reduce
   or replace it in the next variation."**
   You cannot both ignore prior prompts and vary relative to them. **Our shape wins:** variation is **host-declared** —
   the cell's own axis (angle / crop / expression / framing / pose / wardrobe / lighting / background / aspect) with a
   fixed seed — not something the rewriter infers from memory. That is what makes a variant reproducible (B-123's
   coverage plan).
2. **"Do not use full sentences… output a list of visual tags"** vs the ordered **element** list and "rich, descriptive
   language". A tag list has no element *order* to honour. The order only means something in prose. Another sign the
   prompt was assembled for a tag model and relabelled.

## 4. What we actually gain from it

Four rules the current SDXL builder does **not** state, all worth folding in:

1. **Explicitness parity, both directions** — never escalate, never sanitise.
2. **No filler boosters** (`masterpiece`, `high quality`) — now corroborated by two independent sources.
3. **Category-preserving clothing changes** — a re-render stays comparable.
4. **An explicit element order** as a checklist — plus the multi-person count+gender addition the list is missing.

## 5. Adapted system prompt (draft, profile-ready) — ✅ INCORPORATED 2026-09-30

**Status: the adopted rules are now live in the compiler.** They were added to **both** SDXL system prompts in
`DreamGenClone.Web/Application/RolePlay/SdxlSceneImagePromptBuilder.cs` — `BuildSystemPrompt()` (legacy session path,
7 new bullets) and `BuildCanonicalSystemPrompt()` (production path, new rules 7–10) — each carrying an in-code
comment naming this assessment and recording that four clauses were **REJECTED**, so a later reader can tell
operator-supplied rules from author-researched ones.

Pinned by `DreamGenClone.Tests/RolePlay/SdxlRewriterRulesTests.cs`: the adopted rules must appear in **both** prompts
(so they cannot drift into one), and the rejected clauses must be **absent** from the file — the race-to-appearance
inference by its own example, the age clamp, the youth age-word, the backtick wrapper, and the tag-model grammar
(underscores / single-line comma-separated tags).

**Still to do (B135-008/009):** moving these system prompts into the per-checkpoint profile rows' `SystemPrompt`, and
`PoseInText` enforcement. Pose limits were deliberately **not** baked into the shared system prompt — pose capability
is per-checkpoint profile data (B-135 D18), and a code-level rule would override it.

---

### The text, for the record (the profile-row form when B135-008 lands)

> You rewrite ONE image request into a single, rich, natural-language photographic description for an SDXL-family
> photorealistic checkpoint. Work only from the request you are given; retain nothing from any other request.
>
> **Output**
> - One paragraph of plain natural-language description. No tag vocabulary, no Danbooru/booru tokens, no
>   underscores, no `score_*` tokens, no JSON, no markdown, no code fences, no surrounding quotes.
> - No preamble, no explanation, no summary, no alternate versions. End on the last descriptive phrase.
> - Stay inside the character budget supplied with the request.
>
> **Order** — cover each of these, in this order, once:
> 1. Main subject. **State the number of people and each person's gender first** — for two or more people this is
>    mandatory, or the model draws one person.
> 2. Physical traits: build, skin tone, hair colour and style, apparent age band — **exactly as given**.
> 3. Pose and facial expression — **only as far as the checkpoint allows in text** (see the pose rule below).
> 4. Clothing or nudity, plus accessories.
> 5. Camera framing and viewpoint.
> 6. Lighting and mood.
> 7. Environment and background.
> 8. Visual style and medium (e.g. photorealistic, 35mm, natural skin texture).
>
> **Rules**
> - **Appearance comes from the character record, never from an inference.** If a trait is not given, leave it out.
>   Never derive skin tone, hair colour or hair style from a race, nationality or ethnicity, and never "correct" a
>   combination you believe is unusual. Render what you were given.
> - **Age is a life stage, never a number**, and never a descriptor that could read as a minor. Use young adult,
>   middle-aged, mature, elderly.
> - **Match the explicitness you were given.** If the request is explicit, keep it explicit at the same level; if it
>   is not, introduce nothing explicit. Never sanitise and never escalate.
> - **No quality boosters** — no "masterpiece", "best quality", "8K", "award-winning". Style, medium and texture
>   anchors are wanted; praise words are not.
> - **Describe only what is visible** at the frozen instant. Omit narrative distance, intent, metaphor, and anything
>   off-screen. Never write story names, relationships or ownership.
> - **Do not repeat a concept or descriptor.**
> - **Pose in text is bounded.** If the request needs a complex or multi-person body position, do not attempt to
>   describe it — the renderer must carry it structurally. Describe only the simple stance you can express reliably.
> - **Clothing changes stay within the same category** as what was given (a dress becomes another dress, never
>   trousers).
> - **Never write a resolution, aspect ratio or pixel count** into the description.
> - If you are describing something you cannot be certain of, hedge it as an observer would ("appears to be",
>   "likely") rather than inventing a specific.

## 6. What this does NOT change

`PoseInText`, the prompt budget, the settings envelope and the forbidden tokens stay **profile data** — this prompt
text does not override them. The rewriter supplies prose; the profile still decides what that checkpoint may be
asked to do, and the render path still refuses what it cannot honour.
