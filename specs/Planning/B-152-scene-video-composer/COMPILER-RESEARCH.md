# H3 Ref2VA prompt compiler — deep research

**Status:** research complete (2026-10-06). Input to [`SCOPING.md`](./SCOPING.md) (component C-3).
**Scope:** the prompt compiler for local MiniMax H3 **Ref2VA / full-reference mode** video generation.
This document is the authority for what the compiler must emit and why. It records the official
format rules, the rules the B-150 proof was breaking, and the deterministic validation the compiler
must enforce. It proposes no code.

---

## 0. Why this document exists

B-150 proved the H3 stack runs locally on the 16 GB host, but it also proved that **prompt rules that
are easy to get silently wrong produce measurable defects**: abstract soundscape wording emptied the
sub-100 Hz band, negative phrasing re-registered banned words as content, and a missing style
declaration / short description left conditioning on the table. Video generation is ~25–53 minutes
per clip, so a malformed prompt is an expensive failure that no retry loop can catch automatically.

The compiler is therefore a **governed, deterministic, versioned component** — not a string-building
helper. This document is its rule set.

---

## 1. Sources

| # | Source | What it gives |
|---|---|---|
| S1 | `MiniMaxAI/MiniMax-H3` → `docs/VIDEO_PROMPT_WRITING_GUIDE_ref_en.md` (23.5 KB) | Full-reference output format: six sections, `<Subject>/<Picture>/<Video>/<Audio>` labels, retention markers, full-reference differences |
| S2 | `MiniMaxAI/MiniMax-H3` → `docs/VIDEO_PROMPT_WRITING_GUIDE_base_en.md` (15.8 KB) | Shared rules: shots/cuts, camera motion vocabulary, speakers & `<d>` dialogue, on-screen text, `overall_soundscape`, `non_diegetic_music` |
| S3 | `RESEARCH-minimax-h3-16gb.md` §"Official prompt rules that were being violated", §"AUDIO" | The nine rules the proof broke; measured loudness/spectral behaviour |
| S4 | `helpers/h3-local-host/run-h3-ref2va-proof.py` | Working reference implementation of graph conversion, overrides, upload, poll, fetch, loudness normalization |
| S5 | ComfyUI tutorial for MiniMax H3 (`docs.comfy.org/tutorials/video/minimax/minimax-h3-native`) | Node names, `ref_images` autogrow, `ref_audios` support |
| S6 | `tools/video-audio-inspection/` | The measurement tool used for the audio acceptance checks |
| S7 | Live host: `https://comfy.kenacwood.net/object_info/MiniMaxH3ReferenceToVideo` (fetched 2026-10-06) | The node's real input schema — reference limits, frame-length rules, VAE optionality, and the "use the same tags when prompting" label binding. See §16. |
| S8 | Live host: `https://comfy.kenacwood.net/system_stats` (fetched 2026-10-06) | Host identity: ComfyUI 0.37.1, torch 2.14.0+cu130, RTX 5080 16 GB — resolves the stale-IP conflict |

Both S1/S2 are official MiniMax prompt-writing guides distributed with the weights. They are cited,
not copied; the compiler encodes their rules rather than shipping their text.

**Node/graph facts that the compiler must respect** (S4, verified):

- The graph is the official `video_minimax_h3_r2v.json` template converted UI→API.
- The prompt text enters through a single `PrimitiveStringMultiline` node.
- `MiniMaxH3ReferenceToVideo.ref_images` is a `COMFY_AUTOGROW_V3` input (1..9 images in practice;
  2 is the proven quality configuration).
- `MiniMaxH3ReferenceToVideo` also accepts up to **3 `ref_audios`** plus 3 reference videos with
  paired soundtracks.
- Sampling is `BasicGuider` at **CFG 1.0** with `euler/beta` — there is **no negative prompt
  branch**, which is what makes §9 a hard rule rather than a style preference.
- The sampler emits one joint audio+video latent; `VAEDecode` + `VAEDecodeAudio` + `CreateVideo`
  mux both streams. A graph that drops either decode loses audio silently.
- **Reference inputs are ordered autogrow lists** (verified 2026-10-06 from `/object_info`):
  `ref_images` **0..9**, `ref_videos` **0..3**, `ref_video_audios` **0..3**, `ref_audios` **0..3**.
  Their order is the prompt's `<Picture i>` / `<Video k>` / `<Audio j>` numbering (see §3).
- `vae` and `audio_vae` are **optional** inputs. The node's own tooltips state that without the video
  VAE *"reference images/videos only condition the text encoder"*, and without the audio VAE
  *"reference audio only conditions the text encoder"* — an omission degrades silently rather than
  failing.
- `ref_image_size`: `match` scales each reference (down only, preserving aspect) to the generation's
  pixel area; `max` uses the reference pipeline's 2048px short edge. Because reference tokens ride
  **every sampling step**, `max` "can be several times slower" — matching the measured ~400 s cost of
  a second reference.

---

## 2. The output contract — six sections, in order

The compiler's output is a single document with exactly these six sections, in this order (S1 §1):

| # | Section | Purpose | Compiler owns the structure? |
|---|---|---|---|
| 1 | `subject_definitions` | Defines referenced content and its labels | Yes — derived from the reference bindings the user supplied |
| 2 | `summary` | Task type, target video, main reference relationships | Yes — assembled from the declared task type |
| 3 | `retention_analysis` | How each referenced item is preserved/transferred/reused | Yes — derived from the same bindings |
| 4 | `detailed_description` | Visuals, actions, shots, sound, dialogue in playback order | Yes — this is where the prompt compiler's main work lives (§5) |
| 5 | `overall_soundscape` | Ambience + physical sounds (1–4 sentences) | Yes — from the operator's audio-intent input (§6) |
| 6 | `non_diegetic_music` | Audience-only score (1–3 sentences) or `N/A` | Yes — from the operator's audio-intent input (§7) |

Write all six sections in English (S1 preamble). The **only** non-English content permitted is:
dialogue/lyrics inside `<d>`, and text visibly present in the scene.

---

## 3. Reference labels (`subject_definitions`)

Four label types (S1 §2):

| Label | Meaning | Compiler rule |
|---|---|---|
| `<Subject N>` | Reusable visible content (person, scene, wardrobe, prop, style, action, pose) | One line per tracked item; state what the label denotes, its reference role, and its main features. Cite the source asset when provenance must be explicit. |
| `<Picture N>` | A reference image used as a concrete frame anchor (first frame, keyframe, last frame, composition anchor) or a storyboard | Only gets its own line when it is a **frame anchor**. If the image only *defines* a character/scene/costume/style, cite it inside the `<Subject N>` line instead. |
| `<Video N>` | Whole-video relationship (edit source, continuation start, camera/rhythm/temporal structure) | Not used by the current slice (no reference video input). Reserved. |
| `<Audio N>` | Standalone audio or a reference video's audio track | Used when `ref_audios` are bound (§10). |

**Label stability is a hard requirement.** Once a label is assigned it keeps the same meaning across
all six sections (S1 §2). The compiler must assign labels from a stable, ordered input list — never
from incidental iteration order — so a regeneration of the same inputs produces the same labels.

**Label numbers must match the node's reference slots (verified 2026-10-06).** The
`MiniMaxH3ReferenceToVideo` node describes itself as *"`<Picture i>` / `<Video k>` / `<Audio j>`
reference conditioning for MiniMax H3. **Use the same tags when prompting.**"* Its reference inputs
are ordered autogrow lists (`ref_images`, `ref_videos`, `ref_audios`, `ref_video_audios`). Therefore
the operator's reference **order is semantic**: `<Picture 1>` must correspond to the first supplied
`ref_images` slot, and reordering references in the UI must renumber the prompt labels. A compiler
that assigns labels from a different ordering than the graph binds the slots will condition on the
wrong image — silently.

**Numbering is independent per category.** `<Video N>` and `<Audio N>` numbers do not pair with each
other (S1 §2.5).

---

## 4. Retention markers — fixed English tokens

These are fixed enum values in the output format, not prose. The compiler must emit them verbatim
from a closed set (S1 §4).

**Visible content** (`<Subject N>`, `<Picture N>`, `<Video N>`):

| Marker | Meaning |
|---|---|
| `fully_preserved` | The defined role of the reference is fully preserved |
| `partially_preserved` | Still used, but some defined characteristics change or are only partly retained |
| `attribute_transfer` | Referenced characteristics transfer to a different identifiable target |
| `weak_reference` | Only broad similarity in style/category/composition/atmosphere is retained |

Entry forms:

```text
<Subject 1> (appears in [Shot 1], [Shot 3]): fully_preserved - ...
<Picture 2> ([Shot 1] first frame): fully_preserved - ...
```

**Audio** (`<Audio N>`): `fully_copy` | `partially_copy` | `reference` | `weak_reference` (S1 §4.2).

> Do **not** write `(Sx)` speaker IDs in `retention_analysis` (S1 §5.4) — that is a validation rule
> (§13).

---

## 5. `detailed_description` — the main body

### 5.1 Length and structure

- For generation tasks the body is normally **350–500 English words** (S1 §5.2). The B-150 proof was
  ~150 words, costing conditioning. Dialogue-dense content prioritises fitting the complete spoken
  timeline over mechanically hitting the word count.
- A single shot does **not** justify a shorter description; distribute detail across shots by
  information load (S1 §5.2).

### 5.2 Style declaration — full-reference mode difference

| Dimension | T2VA | **Full-reference mode (ours)** |
|---|---|---|
| Main field | `integrated_multimodal_description` | `detailed_description` |
| Style opening | Written after `[Shot 1]` | **Established in one or two English sentences *before* `[Shot 1]`** |
| Reference info | No full-reference labels | `<Subject N>` etc. inserted at first appearance and where their role applies |

Example (S1 §5.2):

```text
The target video is in a cinematic, literary music-video style with soft lighting and a slightly desaturated color palette.
[Shot 1] The scene opens in a crowded urban street...
[Shot 2] At 00:09.000, the shot cuts to an extreme close-up...
```

This is the rule the proof omitted (S3 rule 8).

### 5.3 Shots and cuts (S2 §4.2)

- `[Shot 1]` carries **no timestamp**.
- Later shots are `[Shot N] At MM:SS.mmm, ...` with a **strictly increasing** cut time inside the
  clip duration.
- Cut vocabulary: `the camera cuts to`, `the shot cuts to`, `the shot transitions to`, `the shot
  changes to`, `the shot switches to`. Cross-dissolve/fade/wipe only when explicitly requested.
- A cut must introduce new information (subject, space, state, viewpoint, time). For a distance or
  slight-angle change, use camera motion instead of a cut.
- A `[Shot N]` for N>1 **without** a cut timestamp is malformed (S3 rule 9).

### 5.4 Camera motion — type + amplitude + speed (S2 §4.3)

Motion type is a closed vocabulary: `Zoom In/Out`, `Push In/Pull Out`, `Pan Left/Right`,
`Truck Left/Right`, `Tilt Up/Down`, `Pedestal Up/Down`, `Arc Shot`, `Tracking Shot`, `Static Shot`,
`Shake Slightly/Strongly`, `POV`, `Roll Clockwise/Counterclockwise`.
Amplitude: `with small amplitude` / `with large amplitude`. Speed: `at slow speed` / `at fast speed`.
Medium amplitude and normal speed are usually omitted.

Write it as natural English inside the shot, not as stacked trailing labels:

```text
The camera pushes in with small amplitude at slow speed toward the folded letter in her hands.
```

### 5.5 Speakers, dialogue, singing (S2 §4.4, S1 §5.4)

- Stable IDs `(S1)`, `(S2)` … assigned **once, in order of actual vocal event** in the target video.
  A speaker keeps the same ID across shots. **Characters who never vocalize get no ID.**
- Compound ID `(S1,S2)` when already-numbered speakers vocalize together.
- When a referenced subject speaks, keep **both** the visual label and the speaker ID:
  `<Subject 2> (S1) turns toward the woman and says, <d>[English] ...</d>`.
- Off-screen speech keeps the same form and is marked `off-screen`.
- A speaker's **first appearance** must carry identity information: character type, age, gender,
  on-screen/off, pitch, timbre, speaking rate or accent (S3 rule 5).
- Identifying phrase, ID, action and delivery stay **outside** `<d>`. Inside `<d>`: only the
  language tag and the verbatim spoken content. Never translate or rewrite it.
- Voiceover uses the exact phrase `says in an off-screen voiceover`, and the sentence immediately
  after the `<d>` block must state that the on-screen character's lips remain completely closed.
- Dialogue crossing a cut uses `<scenetrans>` at both connection points plus an explicit continuity
  statement; speech truncated by the end uses `<cutoff>`.
- Speech that is only a cue inside directly reused BGM uses `<Audio N>` and does **not** invent an
  `(Sx)`.

### 5.6 Visible on-screen text (S2 §4.5)

Put banner/sign/label/subtitle/neon text in **English double quotation marks**, preserving the
original text and punctuation verbatim, untranslated:

```text
A red neon sign reading "营业中" glows above the doorway.
```

---

## 6. `overall_soundscape` (S2 §4.6, S3)

- **1–4 English sentences**, one continuous paragraph.
- Covers **ambient sound, physical action sounds, and non-verbal human sounds** — wind, rain,
  traffic, footsteps, fabric movement, impacts, breathing, laughter, panting.
- **Dialogue, singing and diegetic music must NOT be repeated here** (they belong in
  `detailed_description`).
- `N/A` **only** when complete silence is explicitly requested.

**The measured lesson (S3 §"Why the body sounds are missing"):** name a **concrete object plus a
physical action**. The guide's vocabulary is concrete ("wet footsteps", "the soft scrape of a
chair", "trays clink softly inside the bakery"). The proof wrote abstract qualities ("a wet, heavy,
low impact sound") and the model rendered no such event — measured `sub<100 Hz` = 0.1–2.4 %.
The revised wording names *the bed frame knocks against the wall*, *the mattress compresses and
creaks*, *the sheets bunch and drag under her hands*, *skin slaps against skin*.

**Compiler consequence:** the soundscape input must be authored as **concrete object + physical
action** statements. The compiler validates for abstraction-only phrasing and for the required
`N/A` sentinel; it must not synthesize soundscape text from mood words.

---

## 7. `non_diegetic_music` (S2 §4.7, S3 rule 1)

- **1–3 English sentences** describing music only the audience hears.
- Focus on **instrumentation, speed, rhythm, and dynamic changes**.
- **No abstract mood words**, and no explanation of the score's emotional function.
- Music the characters can hear (radio, TV, phone, live instruments, singing) is **diegetic** and
  belongs in `detailed_description` instead.
- **`N/A` is the required sentinel when there is no non-diegetic music.** The proof wrote `None.`,
  which is not the documented token (S3 rule 1).

---

## 8. The nine violated rules — the compiler's regression list

Recorded in S3 from the ComfyUI H3 guide + S1/S2. Every one of these was present in the B-150 proof
prompts before `prompt-v4-audio.txt` fixed them. Each becomes a compiler validation (§13).

| # | Rule | Failure mode if broken |
|---|---|---|
| 1 | `non_diegetic_music` must be `N/A` when there is none | Undocumented sentinel → music hallucinated/absent unpredictably |
| 2 | Dialogue does not belong in `overall_soundscape` | Field is for ambience/physical/non-verbal sound only |
| 3 | Dialogue wrapped in `<d>` with a language tag; everything else outside | Voice content not parsed as speech |
| 4 | Speaker IDs `(Sx)` assigned once by vocal-event order, reused everywhere | Voice identity drifts between lines |
| 5 | A speaker's first appearance needs identity info | Unstable/unanchored voice |
| 6 | **Bans must be written as positives** | With CFG 1 the banned words are *added* as content |
| 7 | `detailed_description` 350–500 words | Under-conditioned generation |
| 8 | Overall style declared at the very start of the description (before `[Shot 1]`) | Style inferred rather than directed |
| 9 | `[Shot N]` for N>1 requires a strictly increasing cut timestamp | Malformed shot structure |

Also from the same guides (S1/S2): soundscape 1–4 sentences; `non_diegetic_music` 1–3 sentences with
instrumentation/tempo/rhythm/dynamics; `<cutoff>` for truncated lines; `<scenetrans>` across cuts;
R2V audio references need a retention marker matching what the audio actually does.

---

## 9. Negative phrasing is a content bug, not a filter miss

The graph runs `BasicGuider` at **CFG 1.0** (S4 `apply_overrides`: sampler `euler`, scheduler `beta`,
no negative conditioning, no negative prompt input anywhere in the R2V template).

Consequence: a phrase like *"no text and no watermark"* **registers "text" and "watermark" as content
to render** (S3 rule 6). The compiler must therefore:

1. emit **no negated phrasing** anywhere in the six sections;
2. express exclusions as positive descriptions of the desired state;
3. fail validation when a banned negation pattern appears (a content check, unit-testable with no
   ComfyUI running).

This is a **content rule the compiler enforces**, because no downstream filter can undo it.

---

## 10. Reference audio (`ref_audios`) and its retention markers

`MiniMaxH3ReferenceToVideo` accepts up to **3 standalone reference audio clips** through the same
autogrow mechanism as images, and references can lock "a character's identity, a style, a motion, a
camera move, **or a voice**" (S5, S3 §"Voice reference").

- Number independently as `<Audio 1>`, `<Audio 2>`, … . If an `<Audio N>` maps to a target speaker,
  reuse that speaker's global `(Sx)` in its definition — e.g.
  `<Audio 1> is the voice-timbre reference for <Subject 1> (S1).` — and never assign a new ID (S1 §2.4).
- Choose the marker from the closed audio set: `fully_copy`, `partially_copy`, `reference`,
  `weak_reference` (S1 §4.2).
- State copy/reference relationships in the section matching the audible layer: ambience/SFX in
  `overall_soundscape`, audience-only score in `non_diegetic_music` (S1 §6).
- **Unused lever.** The B-150 harness never exercised `ref_audios`; it is the strongest lever for
  consistent per-character voices and is deliberately deferred (§15).

---

## 11. Frame length and duration

The `length` input is in frames at **24 fps**. **Verified 2026-10-06 from the node's own schema** on
the live host (`/object_info/MiniMaxH3ReferenceToVideo`):

```json
"length": ["INT", {"tooltip": "Frame count at 24 fps, (124 = ~5s, trained range is ~124-362)",
                   "default": 124, "min": 5, "max": 3600, "step": 17}]
```

Reading that precisely:

- **`step: 17` from `min: 5`** means the node accepts only `length ≡ 5 (mod 17)` — 5, 22, 39, 56, 73,
  … , 124, …, 192, … . This **confirms** the rule previously derived from the run values
  (56/124/192 all satisfy it), so rule 26 is correct as stated.
- **But acceptance is not validity.** The tooltip states the **trained range is ~124–362**. 56 is
  accepted by the node and yet outside what the model was trained on; the node's `max: 3600`
  (150 s) is far beyond the trained range too.
- The composer therefore offers the **trained range 124–362 (5.2–15.1 s)**, with **124 and 192 as the
  proven presets** and a warning above 192 (D-6).

Measured wall-clock (2 references, 40 steps) — roughly **0.28 min/frame**:

| Frames | Duration | Status |
|---|---|---|
| 56 | ~2.3 s | accepted by the node, **outside the trained range** — not offered |
| 124 | 5.2 s | proven, ~23–30 min |
| 192 | 8.0 s | proven, ~49–53 min |
| 362 | 15.1 s | trained-range ceiling, ~100 min estimated — unmeasured |

The compiler does **not** choose the length; it receives it as configured input and must **reject an
invalid or untrained length before submission** rather than let the node fail — or worse, silently
underperform — after a long queue wait.

---

## 12. Compiler input contract — user intent → sections

This is the mapping the Video Composer's intent inputs feed. All of it is operator-supplied or comes
from a bound reference; **none of it is invented by the compiler**.

| Operator intent input | Lands in | Notes |
|---|---|---|
| Task type (`reference_generation`, `keyframe completion`, …) | `summary` prefix | Square-bracketed prefix; chosen from the actual reference roles |
| Reference bindings (image → role: frame anchor vs subject definition; audio → role) | `subject_definitions`, `retention_analysis` | Determines `<Subject N>` vs standalone `<Picture N>` and the retention marker |
| Subject descriptions (who/what is in each reference) | `subject_definitions` | One line per tracked item |
| Scene/action description (what happens, in order) | `detailed_description` | Body of the document |
| Style declaration | First 1–2 sentences of `detailed_description` | Must precede `[Shot 1]` |
| Shots + cut times | `[Shot N] At MM:SS.mmm` | Strictly increasing; `[Shot 1]` untimed |
| Camera intent per shot | `detailed_description` | Closed motion vocabulary + amplitude + speed |
| Speakers (name, identity info, voice description, on/off-screen) | `detailed_description` | First appearance carries identity info; `(Sx)` from vocal-event order |
| Dialogue lines (verbatim text + language) | `<d>[Language] …</d>` | Verbatim; never translated |
| On-screen text | `detailed_description` | English double quotes, verbatim |
| Ambience / physical-action sounds (concrete object + action) | `overall_soundscape` | 1–4 sentences; `N/A` only for total silence |
| Non-diegetic music (instrumentation/tempo/dynamics) | `non_diegetic_music` | 1–3 sentences; `N/A` when none |
| Ban list / things to avoid | **nowhere** | Must be re-expressed as positives before compilation (CFG 1) |

---

## 13. Validation rules the compiler must enforce

Deterministic, unit-testable without ComfyUI, fail-fast with the offending section and rule named.
This list is the acceptance checklist for C-3.

**Structure**

1. Exactly six sections, present, in order.
2. All six written in English (except `<d>` content and visible on-screen text).

**Labels and retention**

3. Every `<Subject N>` / `<Picture N>` / `<Audio N>` referenced later is defined in
   `subject_definitions`.
4. Label numbering is contiguous and stable for a given input set (same inputs ⇒ same labels).
5. Every retention marker is from the closed set for its category.
6. No `(Sx)` appears in `retention_analysis`.
7. A standalone `<Picture N>` line exists only for a frame anchor; a definition-only image is cited
   inside its `<Subject N>` line.

**Description**

8. `[Shot 1]` has no timestamp.
9. Every `[Shot N]` for N>1 has a cut timestamp; timestamps strictly increase and fall inside the
   configured duration.
10. Style is declared in one or two sentences **before** `[Shot 1]`.
11. Word count is within the configured band (default 350–500 for generation tasks), unless the
    dialogue timeline legitimately dominates.
12. Every camera motion uses the closed vocabulary; amplitude/speed tokens only from the closed set.
13. On-screen text is in English double quotes.

**Speech**

14. Every `<d>` block is `<d>[Language] content</d>` with a non-empty language tag.
15. Every `<d>` block is preceded by an identifying phrase and a `(Sx)` (or an `<Audio N>` source).
16. Speaker IDs are assigned once, in vocal-event order, and reused; compound IDs are
    `(S1,S2)`-shaped.
17. Every speaker's first appearance carries identity information.
18. Characters with no vocal event have no `(Sx)`.
19. Voiceover uses the exact phrase and the lips-closed statement.
20. `<scenetrans>` / `<cutoff>` are used where continuity/truncation applies.

**Audio sections**

21. `overall_soundscape` is 1–4 sentences and contains no dialogue/singing/diegetic music.
22. `non_diegetic_music` is `N/A` or 1–3 sentences of instrumentation/tempo/rhythm/dynamics — no
    abstract mood words.
23. Diegetic music appears in `detailed_description`, not in `non_diegetic_music`.
24. Audio copy/reference relationships appear in the section matching the audible layer.

**Content**

25. **No negative phrasing** anywhere (CFG 1) — pattern-checked.
26. Frame length satisfies `length ≡ 5 (mod 17)` — the node's `min 5 / step 17` acceptance rule.

**Reference binding** (added 2026-10-06 from the verified node schema)

27. `<Picture i>` / `<Video k>` / `<Audio j>` numbers match the node's reference **slot order**, and
    reordering references renumbers the labels consistently.
28. The graph binds a video VAE whenever reference images/videos are supplied, and an audio VAE
    whenever reference audio is supplied — both node inputs are **optional**, and omitting them
    silently degrades references to text-encoder-only conditioning.
29. Frame length is inside the **trained range (124–362)**, not merely inside the node's accepted
    range (5–3600); an untrained-but-accepted length is a validation failure the user must override
    deliberately.

---

## 14. Determinism and speaker-ID stability

Speaker IDs are assigned by **order of actual vocal event**, not by subject order (S1 §5.4). Two
consequences for the compiler:

1. The ID assignment must run over the *compiled dialogue timeline* (sorted by the vocal event's
   position), not over the subject list. A subject introduced first but speaking second is `(S2)`.
2. Regenerating the same inputs — including a re-run at a different seed — must produce the **same**
   IDs and the same labels, or a regeneration cannot be compared to its predecessor. The compiler
   therefore takes an explicit, ordered input list and must not depend on dictionary/collection
   iteration order.

A compiler key + version travels with the output for provenance (SCOPING C-3/C-10).

---

## 15. Known gaps and open questions

| # | Gap | Why it matters |
|---|---|---|
| G-1 | **Where does dialogue come from?** | The user's spec says the video page has manual intent inputs; the RP session/scenario is the other candidate source. The compiler contract is the same either way, but the UI and the speaker-identity source differ. |
| G-2 | **Who supplies speaker identity info?** (S3 rule 5) | Needed for voice stability. Candidates: RP character data, a manual voice-description field, or `ref_audios` pinning. |
| G-3 | **Style declaration source** | Must be derived from the reference image's style and/or operator input; the proof simply omitted it. |
| G-4 | **Soundscape authoring help** | The concrete-object rule is the single highest-value audio fix and the easiest to get wrong. Needs a UI affordance (placeholder/examples) or a guided field, not a free-text box with no guidance. |
| G-5 | **Word-count target under dialogue density** | S1 §5.2 allows dialogue-dense content to prioritise the spoken timeline. The compiler needs a configured policy, not a hard 350 minimum. |
| G-6 | **`ref_audios` voice pinning** | Deferred; the strongest lever for consistent voices and untested in the harness. |
| G-7 | **Multi-shot realism** | Every proven run was a single continuous `[Shot 1]`. Multi-cut prompting validates cleanly but is unmeasured — a cut costs part of a very short clip. Needs its own proof before the UI encourages it. |
| G-8 | **Explicit-content dialect** | The NSFW adapters are trained on explicit vocabulary; a "clinical" register measurably underperforms. The register must be a configured, UI-backed choice, not a compiler default. |
| G-9 | ~~**Length validity source**~~ **CLOSED 2026-10-06.** | Confirmed against the node's own schema: `min 5 / max 3600 / step 17`, **trained range ~124–362**. The rule is exact, and the trained range (D-6) is now known. |

---

## 16. Verified node schema (2026-10-06)

Fetched live from the host the app already uses — `https://comfy.kenacwood.net/object_info/MiniMaxH3ReferenceToVideo`
(ComfyUI **0.37.1**, torch 2.14.0+cu130, RTX 5080 16 GB). This replaces several values that were
previously inferred, and it is the authority for the compiler's input contract.

| Input | Type | Constraint |
|---|---|---|
| `clip` | CLIP | required |
| `prompt` | STRING, multiline | required — the compiled six-section document |
| `width` | INT | default **1344**, min 32, max 16384, step 32 |
| `height` | INT | default **768**, min 32, max 16384, step 32 |
| `length` | INT | default **124**, **min 5, max 3600, step 17**, *trained range ~124–362* |
| `ref_image_size` | COMBO | `match` (default) \| `max` |
| `vae` | VAE | **optional** — without it, reference images/videos only condition the text encoder |
| `audio_vae` | VAE | **optional** — without it, reference audio only conditions the text encoder |
| `ref_images` | COMFY_AUTOGROW_V3 | **0..9**, `ref_image_<i>` |
| `ref_videos` | COMFY_AUTOGROW_V3 | **0..3**, `ref_video_<k>` |
| `ref_video_audios` | COMFY_AUTOGROW_V3 | **0..3**, paired with the same-numbered reference video |
| `ref_audios` | COMFY_AUTOGROW_V3 | **0..3**, standalone |

Node identity: `MiniMaxH3ReferenceToVideo`, module `comfy_extras.nodes_minimax_h3`, category
`model/conditioning/minimax`, outputs `CONDITIONING` + `LATENT`.

Self-description: *"`<Picture i>` / `<Video k>` / `<Audio j>` reference conditioning for MiniMax H3.
Use the same tags when prompting."*

**The host is the app's existing `Local ComfyUI (WOOD-GAME-MAIN 5080)` provider row**, reached via
the hostname `comfy.kenacwood.net`. No new provider is required, and the client must resolve the base
URL from that row rather than hardcoding an IP (the harness's `http://192.168.0.11:8188` is the
fragile artifact, not the model of how to reach it).
