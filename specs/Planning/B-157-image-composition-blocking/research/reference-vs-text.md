# B-157 — Research: reference images vs. text description (who owns the appearance)

Independent, source-verified research on the question the operator raised on 2026-10-12:

> *"the body image does include the face … either way the body image has the face so the text prompt should not have
> the face description … my understanding is having both the image reference and the duplicate text description will
> confuse the model … keep in mind for models like krea 2 where there are no image reference ability, the full text
> needs to be included."*

Conducted 2026-10-12 by three independent research passes (web + GitHub + academic). No internal note was treated as
authoritative; every claim below is quoted from an external source. Community-evidence capture was hampered by search
infrastructure (DuckDuckGo/Bing/Brave/Mojeek blocked or rate-limited from the research environment, Reddit
unreachable), which is noted per finding — absence of community evidence there is not evidence of absence.

## 1. The headline: the operator's premise is OFFICIAL for reference-native models

`QwenLM/Qwen-Image-2.1:prompt_rewrite/prompts/system_prompt_edit.txt` (the model's own prompt-enhancer) —

> "**Identity is the hardest invariant.** … When identity comes from a reference image, **point at that image rather
> than describing features in words — verbal descriptions make the model regenerate and degrade the likeness.**"

> "**Say what stays, without repainting it.** Name the untargeted content by type, position and role rather than
> describing its appearance … **A preservation description reads to the model as a generation instruction: the more
> concretely you describe something you meant to keep, the more likely it drifts.**"

> "**Leakage** — touching what the user did not name (… a style change that **drifts a face** …)."

`Runway` is the only *provider* that states the anti-redundancy rule as directly:

> "Reiterating elements that exist within the image in high detail can lead to reduced motion or unexpected results."
> — <https://help.runwayml.com/hc/en-us/articles/39789879462419-Gen-4-Video-Prompting-Guide>

**The canonical official pattern** (`Qwen-Image-2.1:prompt_rewrite/data/edit_example.jsonl`, the multi-portrait row):

> "Place the person in `<image1>` and the person in `<image2>` into a modern live-stream room scene; generate a group
> photo of the two sitting side-by-side behind the table, both facing camera. **Keep both persons' facial features,
> hairstyle, and clothing completely unchanged.**"

Note the nuance: it names the invariant **categories** and supplies **no values** — no eye colour, hair colour, skin
tone or build. This is the wording the app's blanket preservation clause is modelled on.

## 2. The blanket version of the premise is NOT established

| Claim | Verdict | Evidence |
|---|---|---|
| Duplicated description is **harmful per se** | **Not established** | Verified negative searches: **no paper varies prompt redundancy and measures identity fidelity.** `arXiv abs:"text-image conflict"` → 0 results |
| Duplicated description is **endorsed** anywhere | **No** | No source endorses it; the design literature is built around *reducing* free-text identity tokens |
| **Conflict** (text contradicting the image) is a real, named failure mode | **Yes, HIGH confidence** | `InstantID` §3.2: *"we eliminate the text prompts and use ID embedding as conditions for cross-attention layers in the ControlNet… not being influenced by generalized descriptions of faces and backgrounds"*; `PhotoMaker` §4.1: *"delayed subject conditioning to solve the conflicts between text and ID conditions"*; `PuLID`: *"when we provide a prompt that is in conflict or misaligned with the ID condition… these methods tend to fail"*; `InfiniteYou`: *"injecting text and identity information at the same positions… may bring potential entanglement and conflict"* |
| Text can **win over** the image (so redundant text is dangerous, not merely inert) | **Yes, HIGH confidence** | `FastComposer` §4.3/§5.4: *"using the augmented text representation directly often leads to images that closely resemble the subjects while ignoring the textual directives… image layout forms at the early phases"*; `From Competition to Synergy` §5.2: early steps prioritize prompt following, later steps ID preservation |
| Reference **adapter strength** is the documented knob for conflict | **Yes, HIGH confidence** | `InstantID` model card tip 3: *"If you find that text control is not as expected, decrease Adapter strength"*; `PuLID-FLUX` docs: earlier ID insertion = higher fidelity, lower editability |

**Synthesis:** for a reference-native model, redundant appearance text is *inert at best* and *harmful by the vendor's
own statement*; the mechanism is not "confusion" but **drift and incomplete preservation** (the model re-generates what
it is told you meant to keep).

## 3. The model-capability boundary is official and the app must branch on it

`Google` Nano Banana prompting guide:

> "your prompt structure **depends entirely on whether you are using reference images** or relying solely on text."

with two published formulas — text-only: `[Subject] + [Action] + [Location/context] + [Composition] + [Style]`; with
references: `[Reference images] + [Relationship instruction] + [New scenario]`.

`Seedream 4.0` requires naming a **"Reference Target"** per image plus a **"Generated Scene Description"**.
`Ideogram`: *"With references attached, your prompt works as an edit instruction, so say what should change and keep
the rest of the prompt short."* `Bria/Fibo-Edit` ships an official template that branches literally:
*"**Do not describe the reference image itself; describe the new scene.**"*

**How to structure it as ONE decision** — `InvokeAI` is the architectural precedent:

> `invokeai/backend/architectures/facets/features.py` — "The UI has to know … **how many reference images to accept**…
> None of it is derivable from a model file… **This is where those answers live now.**" Fields:
> `max_reference_images: int = 0`, `reference_images_require_variant: str | None`.
> `capabilities.py` — "**One row per architecture… A client fetches this once and joins it against model records
> locally.**"

> Template for this app: **one backend-declared capability record + one predicate consumed by picker, validator and
> prompt builder. Do not let the prompt builder carry its own copy of the rule.**

Negative result worth keeping: **no public repo was found that branches the *prompt text* (appearance block on/off) on
reference capability** — only capability *plumbing*. This app would be doing something the field has not standardised;
confidence in that negative: medium (GitHub code search was rate-limited in one session).

## 4. "The body reference already contains the face" — factually true of the asset, not attested for the model

Explicitly **UNKNOWN**: no official statement in the 2.1/2509/2511 model cards, READMEs, enhancer prompts, Alibaba
Cloud API references (EN + ZH) or HF discussions says a full-body reference carries the face or that a face crop is
redundant. Closest signals:

- Qwen's enhancer has a **face-image-plus-body-image** ratio rule for face/head swap, so the pairing is anticipated.
- `Qwen-Image-Edit-2509` model card: *"**Optimal performance is currently achieved with 1 to 3 input images**"*.
- A practitioner report (`Qwen-Image-Edit-2511` discussion #30) uses 3 refs (face close-up + body front + body back)
  and reports drift/artifacts at 3 refs against *"1 reference (face only) → always clean"*.
- `Runway`: *"**Front-facing or 3/4 angle captures features most effectively**"*; *"a single high-quality reference image
  works well for most projects."*
- LoRA dataset guidance (`Character-Dataset-for-LoRA-Training`): *"Include a good number of full-body shots, not just
  face crops… crucial for teaching the character's overall build, proportions."*

**Design consequence taken:** the premise is a property of *the pack* (its body views are composed at the same angles as
its face views), not of the model. So the element model treats "appearance" as **one element per character** — either a
face or a body reference clears it — rather than splitting face and body into two elements. This is also what the
element plan already derived.

## 5. Auto-picking default references (split out as its own item)

- **No vendor prescribes a default set** (verified absence — a real gap in official guidance).
- Convergent signals: one reference suffices for most shots; front or 3/4 captures features best; a character sheet
  wants front / three-quarter / profile; keep identity and style references in **distinct roles**.
- **VERIFIED ABSENCE (high):** no official or community source states "condition the shot on the angle-closest
  reference", and no ComfyUI node was found that auto-selects a reference by pose. The documented principle is the
  opposite — **decouple identity from pose** (`InstantID` conditions on five facial keypoints only; its maintainer:
  *"For novel-view synthesis, you can provide a different pose by mean of another image"*). CLIP-embedding adapters are
  angle-weak: *"I only seem to get output images with the face pointing at the camera"* (`ComfyUI_IPAdapter_plus` issue
  #101, answered with "use the face model and any controlnet you want… maybe a pose controlnet"). Reference-native
  models claim viewpoint robustness (`FLUX.1 Kontext`, `Gemini 2.5 Flash Image`).

So any angle-matching default in this app is **our heuristic**, to be documented as such and paired with an explicit
angle instruction rather than presented as best practice.

## 6. Prompt-length guidance for the text-only path

- `BFL` FLUX.2 guide: *"Medium (30-80 words): Usually ideal for most projects"*; *"**Word order matters** — FLUX.2 pays
  more attention to what comes first"* (subject → action → style → context → details).
- `Civitai` prompt-crafting guide: *"keeping similar tokens grouped together increases the chances of them being
  included"* → emit the appearance description as one contiguous block.
- SD/SDXL tokenisation (`AUTOMATIC1111` wiki): 75-**token** chunks (`CLIPTokenizer.model_max_length = 77`); the folk
  claim "75 words then truncated" is a unit error, and chunking means text is not discarded wholesale.
- Pony V6 XL model card: no maximum-length guidance (a commonly repeated "Pony needs short prompts" claim is refuted).

## 7. What this research changed in the implementation

| Finding | Change |
|---|---|
| Qwen: point at the image; a preservation description reads as a generation instruction | The removal derived from a binding is now actually applied (the key-space fix), so a bound reference **drops** that character's appearance prose |
| Qwen's example names categories, never values | `ReferenceRoleClauses.BlockFor` emits **one blanket preservation clause** naming face/hair/skin tone/build/clothing and no values |
| Google: prompt structure depends on whether references are used; InvokeAI: one capability record, one predicate, never a copy in the builder | The removal is derived only for references that will be **sent**; a binding the resolved model cannot carry is refused by name instead of silently costing the prompt its description |
| Text-only models need the full description | Verifiable in one prompt: a character with a bound reference loses their appearance line, a character without one keeps it (measured 2026-10-12) |
| No source supports angle-matched reference defaulting | Deferred to its own item, to be documented as this app's heuristic |
