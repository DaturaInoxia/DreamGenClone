# H3 Ref2VA prompt compiler â€” deep research

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
declaration / short description left conditioning on the table. Video generation is ~25â€“53 minutes
per clip, so a malformed prompt is an expensive failure that no retry loop can catch automatically.

The compiler is therefore a **governed, deterministic, versioned component** â€” not a string-building
helper. This document is its rule set.

---

## 1. Sources

| # | Source | What it gives |
|---|---|---|
| S1 | `MiniMaxAI/MiniMax-H3` â†’ `docs/VIDEO_PROMPT_WRITING_GUIDE_ref_en.md` (23.5 KB) | Full-reference output format: six sections, `<Subject>/<Picture>/<Video>/<Audio>` labels, retention markers, full-reference differences |
| S2 | `MiniMaxAI/MiniMax-H3` â†’ `docs/VIDEO_PROMPT_WRITING_GUIDE_base_en.md` (15.8 KB) | Shared rules: shots/cuts, camera motion vocabulary, speakers & `<d>` dialogue, on-screen text, `overall_soundscape`, `non_diegetic_music` |
| S3 | `RESEARCH-minimax-h3-16gb.md` Â§"Official prompt rules that were being violated", Â§"AUDIO" | The nine rules the proof broke; measured loudness/spectral behaviour |
| S4 | `helpers/h3-local-host/run-h3-ref2va-proof.py` | Working reference implementation of graph conversion, overrides, upload, poll, fetch, loudness normalization |
| S5 | ComfyUI tutorial for MiniMax H3 (`docs.comfy.org/tutorials/video/minimax/minimax-h3-native`) | Node names, `ref_images` autogrow, `ref_audios` support |
| S6 | `tools/video-audio-inspection/` | The measurement tool used for the audio acceptance checks |
| S7 | Live host: `https://comfy.kenacwood.net/object_info/MiniMaxH3ReferenceToVideo` (fetched 2026-10-06) | The node's real input schema â€” reference limits, frame-length rules, VAE optionality, and the "use the same tags when prompting" label binding. See Â§16. |
| S8 | Live host: `https://comfy.kenacwood.net/system_stats` (fetched 2026-10-06) | Host identity: ComfyUI 0.37.1, torch 2.14.0+cu130, RTX 5080 16 GB â€” resolves the stale-IP conflict |
| S9 | `docs.comfy.org/tutorials/video/minimax/minimax-h3-prompt-guide` (fetched 2026-10-06) | ComfyUI's official prompt-guide page: general tips, dialogue/speaker rules, reference sheets, audio-reuse markers, embeddings. See Â§17.4. |
| S10 | `docs.comfy.org/tutorials/video/minimax/minimax-h3` (fetched 2026-10-06) | ComfyUI's main H3 tutorial: audio-convergence behaviour, step-count guidance, flow-shift values. See Â§17.5. |
| S11 | `MiniMax-AI/awesome-minimax-h3-integration` README (fetched 2026-10-06) | The community index: prompting recipes, compiler prior art, Ref Patch, corpora, Motion-Context. See Â§17.6. |
| S12 | `MiniMax-AI/MiniMax-H3/skills/h3-prompt-writing` (fetched 2026-10-06) | MiniMax's own packaged prompt-writing skill â€” references byte-identical to S1/S2, plus four distilled tips. See Â§17.7. |
| S13 | The official ComfyUI R2V template's embedded default prompt (extracted from the local template cache, 2026-10-06) | Proof of the free-form dialect and the two-dialect decision. See Â§17.3. |
| S14 | GitHub code search for the section field names (~2,700 results, 2026-10-06) | Evidence the six-section format is the community standard. See Â§17.7. |

Both S1/S2 are official MiniMax prompt-writing guides distributed with the weights. They are cited,
not copied; the compiler encodes their rules rather than shipping their text.

**Node/graph facts that the compiler must respect** (S4, verified):

- The graph is the official `video_minimax_h3_r2v.json` template converted UIâ†’API.
- The prompt text enters through a single `PrimitiveStringMultiline` node.
- `MiniMaxH3ReferenceToVideo.ref_images` is a `COMFY_AUTOGROW_V3` input (1..9 images in practice;
  2 is the proven quality configuration).
- `MiniMaxH3ReferenceToVideo` also accepts up to **3 `ref_audios`** plus 3 reference videos with
  paired soundtracks.
- Sampling is `BasicGuider` at **CFG 1.0** with `euler/beta` â€” there is **no negative prompt
  branch**, which is what makes Â§9 a hard rule rather than a style preference.
- The sampler emits one joint audio+video latent; `VAEDecode` + `VAEDecodeAudio` + `CreateVideo`
  mux both streams. A graph that drops either decode loses audio silently.
- **Reference inputs are ordered autogrow lists** (verified 2026-10-06 from `/object_info`):
  `ref_images` **0..9**, `ref_videos` **0..3**, `ref_video_audios` **0..3**, `ref_audios` **0..3**.
  Their order is the prompt's `<Picture i>` / `<Video k>` / `<Audio j>` numbering (see Â§3).
- `vae` and `audio_vae` are **optional** inputs. The node's own tooltips state that without the video
  VAE *"reference images/videos only condition the text encoder"*, and without the audio VAE
  *"reference audio only conditions the text encoder"* â€” an omission degrades silently rather than
  failing.
- `ref_image_size`: `match` scales each reference (down only, preserving aspect) to the generation's
  pixel area; `max` uses the reference pipeline's 2048px short edge. Because reference tokens ride
  **every sampling step**, `max` "can be several times slower" â€” matching the measured ~400 s cost of
  a second reference.

---

## 2. The output contract â€” six sections, in order

The compiler's output is a single document with exactly these six sections, in this order (S1 Â§1):

| # | Section | Purpose | Compiler owns the structure? |
|---|---|---|---|
| 1 | `subject_definitions` | Defines referenced content and its labels | Yes â€” derived from the reference bindings the user supplied |
| 2 | `summary` | Task type, target video, main reference relationships | Yes â€” assembled from the declared task type |
| 3 | `retention_analysis` | How each referenced item is preserved/transferred/reused | Yes â€” derived from the same bindings |
| 4 | `detailed_description` | Visuals, actions, shots, sound, dialogue in playback order | Yes â€” this is where the prompt compiler's main work lives (Â§5) |
| 5 | `overall_soundscape` | Ambience + physical sounds (1â€“4 sentences) | Yes â€” from the operator's audio-intent input (Â§6) |
| 6 | `non_diegetic_music` | Audience-only score (1â€“3 sentences) or `N/A` | Yes â€” from the operator's audio-intent input (Â§7) |

Write all six sections in English (S1 preamble). The **only** non-English content permitted is:
dialogue/lyrics inside `<d>`, and text visibly present in the scene.

---

## 3. Reference labels (`subject_definitions`)

Four label types (S1 Â§2):

| Label | Meaning | Compiler rule |
|---|---|---|
| `<Subject N>` | Reusable visible content (person, scene, wardrobe, prop, style, action, pose) | One line per tracked item; state what the label denotes, its reference role, and its main features. Cite the source asset when provenance must be explicit. |
| `<Picture N>` | A reference image used as a concrete frame anchor (first frame, keyframe, last frame, composition anchor) or a storyboard | Only gets its own line when it is a **frame anchor**. If the image only *defines* a character/scene/costume/style, cite it inside the `<Subject N>` line instead. |
| `<Video N>` | Whole-video relationship (edit source, continuation start, camera/rhythm/temporal structure) | Not used by the current slice (no reference video input). Reserved. |
| `<Audio N>` | Standalone audio or a reference video's audio track | Used when `ref_audios` are bound (Â§10). |

**Label stability is a hard requirement.** Once a label is assigned it keeps the same meaning across
all six sections (S1 Â§2). The compiler must assign labels from a stable, ordered input list â€” never
from incidental iteration order â€” so a regeneration of the same inputs produces the same labels.

**Label numbers must match the node's reference slots (verified 2026-10-06).** The
`MiniMaxH3ReferenceToVideo` node describes itself as *"`<Picture i>` / `<Video k>` / `<Audio j>`
reference conditioning for MiniMax H3. **Use the same tags when prompting.**"* Its reference inputs
are ordered autogrow lists (`ref_images`, `ref_videos`, `ref_audios`, `ref_video_audios`). Therefore
the operator's reference **order is semantic**: `<Picture 1>` must correspond to the first supplied
`ref_images` slot, and reordering references in the UI must renumber the prompt labels. A compiler
that assigns labels from a different ordering than the graph binds the slots will condition on the
wrong image â€” silently.

**Numbering is independent per category.** `<Video N>` and `<Audio N>` numbers do not pair with each
other (S1 Â§2.5).

---

## 4. Retention markers â€” fixed English tokens

These are fixed enum values in the output format, not prose. The compiler must emit them verbatim
from a closed set (S1 Â§4).

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

**Audio** (`<Audio N>`): `fully_copy` | `partially_copy` | `reference` | `weak_reference` (S1 Â§4.2).

> Do **not** write `(Sx)` speaker IDs in `retention_analysis` (S1 Â§5.4) â€” that is a validation rule
> (Â§13).

---

## 5. `detailed_description` â€” the main body

### 5.1 Length and structure

- For generation tasks the body is normally **350â€“500 English words** (S1 Â§5.2). The B-150 proof was
  ~150 words, costing conditioning. Dialogue-dense content prioritises fitting the complete spoken
  timeline over mechanically hitting the word count.
- A single shot does **not** justify a shorter description; distribute detail across shots by
  information load (S1 Â§5.2).

### 5.2 Style declaration â€” full-reference mode difference

| Dimension | T2VA | **Full-reference mode (ours)** |
|---|---|---|
| Main field | `integrated_multimodal_description` | `detailed_description` |
| Style opening | Written after `[Shot 1]` | **Established in one or two English sentences *before* `[Shot 1]`** |
| Reference info | No full-reference labels | `<Subject N>` etc. inserted at first appearance and where their role applies |

Example (S1 Â§5.2):

```text
The target video is in a cinematic, literary music-video style with soft lighting and a slightly desaturated color palette.
[Shot 1] The scene opens in a crowded urban street...
[Shot 2] At 00:09.000, the shot cuts to an extreme close-up...
```

This is the rule the proof omitted (S3 rule 8).

### 5.3 Shots and cuts (S2 Â§4.2)

- `[Shot 1]` carries **no timestamp**.
- Later shots are `[Shot N] At MM:SS.mmm, ...` with a **strictly increasing** cut time inside the
  clip duration.
- Cut vocabulary: `the camera cuts to`, `the shot cuts to`, `the shot transitions to`, `the shot
  changes to`, `the shot switches to`. Cross-dissolve/fade/wipe only when explicitly requested.
- A cut must introduce new information (subject, space, state, viewpoint, time). For a distance or
  slight-angle change, use camera motion instead of a cut.
- A `[Shot N]` for N>1 **without** a cut timestamp is malformed (S3 rule 9).

### 5.4 Camera motion â€” type + amplitude + speed (S2 Â§4.3)

Motion type is a closed vocabulary: `Zoom In/Out`, `Push In/Pull Out`, `Pan Left/Right`,
`Truck Left/Right`, `Tilt Up/Down`, `Pedestal Up/Down`, `Arc Shot`, `Tracking Shot`, `Static Shot`,
`Shake Slightly/Strongly`, `POV`, `Roll Clockwise/Counterclockwise`.
Amplitude: `with small amplitude` / `with large amplitude`. Speed: `at slow speed` / `at fast speed`.
Medium amplitude and normal speed are usually omitted.

Write it as natural English inside the shot, not as stacked trailing labels:

```text
The camera pushes in with small amplitude at slow speed toward the folded letter in her hands.
```

### 5.5 Speakers, dialogue, singing (S2 Â§4.4, S1 Â§5.4)

- Stable IDs `(S1)`, `(S2)` â€¦ assigned **once, in order of actual vocal event** in the target video.
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

### 5.6 Visible on-screen text (S2 Â§4.5)

Put banner/sign/label/subtitle/neon text in **English double quotation marks**, preserving the
original text and punctuation verbatim, untranslated:

```text
A red neon sign reading "è¥ä¸šä¸­" glows above the doorway.
```

---

## 6. `overall_soundscape` (S2 Â§4.6, S3)

- **1â€“4 English sentences**, one continuous paragraph.
- Covers **ambient sound, physical action sounds, and non-verbal human sounds** â€” wind, rain,
  traffic, footsteps, fabric movement, impacts, breathing, laughter, panting.
- **Dialogue, singing and diegetic music must NOT be repeated here** (they belong in
  `detailed_description`).
- `N/A` **only** when complete silence is explicitly requested.

**The measured lesson (S3 Â§"Why the body sounds are missing"):** name a **concrete object plus a
physical action**. The guide's vocabulary is concrete ("wet footsteps", "the soft scrape of a
chair", "trays clink softly inside the bakery"). The proof wrote abstract qualities ("a wet, heavy,
low impact sound") and the model rendered no such event â€” measured `sub<100 Hz` = 0.1â€“2.4 %.
The revised wording names *the bed frame knocks against the wall*, *the mattress compresses and
creaks*, *the sheets bunch and drag under her hands*, *skin slaps against skin*.

**Compiler consequence:** the soundscape input must be authored as **concrete object + physical
action** statements. The compiler validates for abstraction-only phrasing and for the required
`N/A` sentinel; it must not synthesize soundscape text from mood words.

---

## 7. `non_diegetic_music` (S2 Â§4.7, S3 rule 1)

- **1â€“3 English sentences** describing music only the audience hears.
- Focus on **instrumentation, speed, rhythm, and dynamic changes**.
- **No abstract mood words**, and no explanation of the score's emotional function.
- Music the characters can hear (radio, TV, phone, live instruments, singing) is **diegetic** and
  belongs in `detailed_description` instead.
- **`N/A` is the required sentinel when there is no non-diegetic music.** The proof wrote `None.`,
  which is not the documented token (S3 rule 1).

---

## 8. The nine violated rules â€” the compiler's regression list

Recorded in S3 from the ComfyUI H3 guide + S1/S2. Every one of these was present in the B-150 proof
prompts before `prompt-v4-audio.txt` fixed them. Each becomes a compiler validation (Â§13).

| # | Rule | Failure mode if broken |
|---|---|---|
| 1 | `non_diegetic_music` must be `N/A` when there is none | Undocumented sentinel â†’ music hallucinated/absent unpredictably |
| 2 | Dialogue does not belong in `overall_soundscape` | Field is for ambience/physical/non-verbal sound only |
| 3 | Dialogue wrapped in `<d>` with a language tag; everything else outside | Voice content not parsed as speech |
| 4 | Speaker IDs `(Sx)` assigned once by vocal-event order, reused everywhere | Voice identity drifts between lines |
| 5 | A speaker's first appearance needs identity info | Unstable/unanchored voice |
| 6 | **Bans must be written as positives** | With CFG 1 the banned words are *added* as content |
| 7 | `detailed_description` 350â€“500 words | Under-conditioned generation |
| 8 | Overall style declared at the very start of the description (before `[Shot 1]`) | Style inferred rather than directed |
| 9 | `[Shot N]` for N>1 requires a strictly increasing cut timestamp | Malformed shot structure |

Also from the same guides (S1/S2): soundscape 1â€“4 sentences; `non_diegetic_music` 1â€“3 sentences with
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
camera move, **or a voice**" (S5, S3 Â§"Voice reference").

- Number independently as `<Audio 1>`, `<Audio 2>`, â€¦ . If an `<Audio N>` maps to a target speaker,
  reuse that speaker's global `(Sx)` in its definition â€” e.g.
  `<Audio 1> is the voice-timbre reference for <Subject 1> (S1).` â€” and never assign a new ID (S1 Â§2.4).
- Choose the marker from the closed audio set: `fully_copy`, `partially_copy`, `reference`,
  `weak_reference` (S1 Â§4.2).
- State copy/reference relationships in the section matching the audible layer: ambience/SFX in
  `overall_soundscape`, audience-only score in `non_diegetic_music` (S1 Â§6).
- **Reference-clip quality (S11):** *"a clean, clearly spoken 10-second clip is picked up more
  reliably than a noisy one."*
- **Known failure mode with multiple speakers (S9, Â§17.4.4):** H3 can hand an audio reference to
  the **wrong speaker** even when the prompt text and the reference connection order are both
  correct. Mitigations: keep each shot to the references that shot needs, and **tie a line to a
  visible event** (*"When the phone is at his ear, the man in the coat speaks"*) rather than to an
  absolute timecode. Documented escape hatch: generate the line with a voice tool and supply it as
  that speaker's audio reference with the matching marker (`fully_copy` when the clip becomes the
  complete final audio track, `partially_copy` when it covers part of the timeline).
- **Unused lever.** The B-150 harness never exercised `ref_audios`; it is the strongest lever for
  consistent per-character voices and is deliberately deferred (Â§15).

---

## 11. Frame length and duration

The `length` input is in frames at **24 fps**. **Verified 2026-10-06 from the node's own schema** on
the live host (`/object_info/MiniMaxH3ReferenceToVideo`):

```json
"length": ["INT", {"tooltip": "Frame count at 24 fps, (124 = ~5s, trained range is ~124-362)",
                   "default": 124, "min": 5, "max": 3600, "step": 17}]
```

Reading that precisely:

- **`step: 17` from `min: 5`** means the node accepts only `length â‰¡ 5 (mod 17)` â€” 5, 22, 39, 56, 73,
  â€¦ , 124, â€¦, 192, â€¦ . This **confirms** the rule previously derived from the run values
  (56/124/192 all satisfy it), so rule 26 is correct as stated.
- **But acceptance is not validity.** The tooltip states the **trained range is ~124â€“362**. 56 is
  accepted by the node and yet outside what the model was trained on; the node's `max: 3600`
  (150 s) is far beyond the trained range too.
- The composer therefore offers the **trained range 124â€“362 (5.2â€“15.1 s)**, with **124 and 192 as the
  proven presets** and a warning above 192 (D-6).

Measured wall-clock (2 references, 40 steps) â€” roughly **0.28 min/frame**:

| Frames | Duration | Status |
|---|---|---|
| 56 | ~2.3 s | accepted by the node, **outside the trained range** â€” not offered |
| 124 | 5.2 s | proven, ~23â€“30 min |
| 192 | 8.0 s | proven, ~49â€“53 min |
| 362 | 15.1 s | trained-range ceiling, ~100 min estimated â€” unmeasured |

The compiler does **not** choose the length; it receives it as configured input and must **reject an
invalid or untrained length before submission** rather than let the node fail â€” or worse, silently
underperform â€” after a long queue wait.

---

## 12. Compiler input contract â€” user intent â†’ sections

This is the mapping the Video Studio's intent inputs feed. All of it is operator-supplied or comes
from a bound reference; **none of it is invented by the compiler**.

| Operator intent input | Lands in | Notes |
|---|---|---|
| Task type (`reference generation`, `keyframe completion`, â€¦) | `summary` prefix | **Bracketed, space-separated, `+`-joined** â€” the official example writes `[reference generation + audio reference]` (Â§17.1); chosen from the actual reference roles |
| Reference bindings (image â†’ role: frame anchor vs subject definition vs **character sheet**; audio â†’ role) | `subject_definitions`, `retention_analysis` | Determines `<Subject N>` vs standalone `<Picture N>` and the retention marker; a **sheet** gets its own `<Picture N>` entry naming its numbered panels, and shots point at a panel (Â§17.4.1) |
| Subject descriptions (who/what is in each reference) | `subject_definitions` | One line per tracked item |
| Scene/action description (what happens, in order) | `detailed_description` | Body of the document |
| Style declaration | First 1â€“2 sentences of `detailed_description` | Must precede `[Shot 1]` |
| Shots + cut times | `[Shot N] At MM:SS.mmm` | Strictly increasing; `[Shot 1]` untimed |
| Camera intent per shot | `detailed_description` | Closed motion vocabulary + amplitude + speed |
| Speakers (name, identity info, voice description, on/off-screen) | `detailed_description` | First appearance carries identity info; `(Sx)` from vocal-event order |
| Dialogue lines (verbatim text + language) | `<d>[Language] â€¦</d>` | Verbatim; never translated |
| On-screen text | `detailed_description` | English double quotes, verbatim |
| Ambience / physical-action sounds (concrete object + action) | `overall_soundscape` | 1â€“4 sentences; `N/A` only for total silence |
| Non-diegetic music (instrumentation/tempo/dynamics) | `non_diegetic_music` | 1â€“3 sentences; `N/A` when none |
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
4. Label numbering is contiguous and stable for a given input set (same inputs â‡’ same labels).
5. Every retention marker is from the closed set for its category.
6. No `(Sx)` appears in `retention_analysis`.
7. A standalone `<Picture N>` line exists only for a frame anchor; a definition-only image is cited
   inside its `<Subject N>` line.

**Description**

8. `[Shot 1]` has no timestamp.
9. Every `[Shot N]` for N>1 has a cut timestamp; timestamps strictly increase and fall inside the
   configured duration.
10. Style is declared in one or two sentences **before** `[Shot 1]`.
11. Word count is within the configured band (default 350â€“500 for generation tasks), unless the
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

21. `overall_soundscape` is 1â€“4 sentences and contains no dialogue/singing/diegetic music.
22. `non_diegetic_music` is `N/A` or 1â€“3 sentences of instrumentation/tempo/rhythm/dynamics â€” no
    abstract mood words.
23. Diegetic music appears in `detailed_description`, not in `non_diegetic_music`.
24. Audio copy/reference relationships appear in the section matching the audible layer.

**Content**

25. **No negative phrasing** anywhere (CFG 1) â€” pattern-checked.
26. Frame length satisfies `length â‰¡ 5 (mod 17)` â€” the node's `min 5 / step 17` acceptance rule.

**Reference binding** (added 2026-10-06 from the verified node schema)

27. `<Picture i>` / `<Video k>` / `<Audio j>` numbers match the node's reference **slot order**, and
    reordering references renumbers the labels consistently.
28. The graph binds a video VAE whenever reference images/videos are supplied, and an audio VAE
    whenever reference audio is supplied â€” both node inputs are **optional**, and omitting them
    silently degrades references to text-encoder-only conditioning.
29. Frame length is inside the **trained range (124â€“362)**, not merely inside the node's accepted
    range (5â€“3600); an untrained-but-accepted length is a validation failure the user must override
    deliberately.

**Structure and content quality** (added 2026-10-06 from the external-research pass, Â§17)

30. A **reference sheet** (one image holding several views/panels) gets its own `<Picture N>` entry
    that names and numbers its panels, and shots point at a panel â€” never an unnumbered sheet
    (Â§17.4.1).
31. The description's **total timeline matches the requested duration** (4â€“15 s) â€” not merely cut
    times inside it; a description whose arc outruns or underruns the clip is a validation failure
    (Â§17.7).
32. **No abstract-only descriptors** â€” words like "cinematic" or "beautiful" must be backed by
    concrete visual/audio detail in the same passage; this generalizes the soundscape
    concrete-object lesson to the whole description (Â§17.7).
33. `detailed_description` must not reduce to a **plot summary** â€” every sentence should carry
    composition, subject state, environment, action, camera, sound, or a reference-appearance point
    (Â§17.7; the ref guide's preamble states the same).

---

## 14. Determinism and speaker-ID stability

Speaker IDs are assigned by **order of actual vocal event**, not by subject order (S1 Â§5.4). Two
consequences for the compiler:

1. The ID assignment must run over the *compiled dialogue timeline* (sorted by the vocal event's
   position), not over the subject list. A subject introduced first but speaking second is `(S2)`.
2. Regenerating the same inputs â€” including a re-run at a different seed â€” must produce the **same**
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
| G-5 | **Word-count target under dialogue density** | S1 Â§5.2 allows dialogue-dense content to prioritise the spoken timeline. The compiler needs a configured policy, not a hard 350 minimum. |
| G-6 | **`ref_audios` voice pinning** | Deferred; the strongest lever for consistent voices and untested in the harness. |
| G-7 | **Multi-shot realism** | Every proven run was a single continuous `[Shot 1]`. Multi-cut prompting validates cleanly but is unmeasured â€” a cut costs part of a very short clip. Needs its own proof before the UI encourages it. |
| G-8 | **Explicit-content dialect** | The NSFW adapters are trained on explicit vocabulary; a "clinical" register measurably underperforms. The register must be a configured, UI-backed choice, not a compiler default. |
| G-9 | ~~**Length validity source**~~ **CLOSED 2026-10-06.** | Confirmed against the node's own schema: `min 5 / max 3600 / step 17`, **trained range ~124â€“362**. The rule is exact, and the trained range (D-6) is now known. |

---

## 16. Verified node schema (2026-10-06)

Fetched live from the host the app already uses â€” `https://comfy.kenacwood.net/object_info/MiniMaxH3ReferenceToVideo`
(ComfyUI **0.37.1**, torch 2.14.0+cu130, RTX 5080 16 GB). This replaces several values that were
previously inferred, and it is the authority for the compiler's input contract.

| Input | Type | Constraint |
|---|---|---|
| `clip` | CLIP | required |
| `prompt` | STRING, multiline | required â€” the compiled six-section document |
| `width` | INT | default **1344**, min 32, max 16384, step 32 |
| `height` | INT | default **768**, min 32, max 16384, step 32 |
| `length` | INT | default **124**, **min 5, max 3600, step 17**, *trained range ~124â€“362* |
| `ref_image_size` | COMBO | `match` (default) \| `max` |
| `vae` | VAE | **optional** â€” without it, reference images/videos only condition the text encoder |
| `audio_vae` | VAE | **optional** â€” without it, reference audio only conditions the text encoder |
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

---

## 17. External research (2026-10-06): official examples, ComfyUI guidance, ecosystem

Deep-research pass over the official guides (completed in full), the official ComfyUI template's
embedded prompt, the ComfyUI prompt-guide and tutorial pages, MiniMax's own packaged
`h3-prompt-writing` skill, and the `awesome-minimax-h3-integration` index. Everything below is
**source-cited**; the compiler and its unit tests should treat Â§17.1 as the canonical fixture.

### 17.1 The canonical worked example (official, complete)

The full-reference guide's own complete example â€” the single best fixture for compiler unit tests.
Four subjects from four references, one audio reference, three shots, two speakers, retention
analysis, and the `N/A` sentinel, all in one document:

```text
subject_definitions:
<Subject 1> is the coffee-shop environment in <Picture 1>, featuring an exposed brick wall, an orange tufted sofa with patterned pillows, a neon sign, and a wooden coffee table.
<Subject 2> is the fluffy white Samoyed in <Picture 2>, <Picture 3>, and <Picture 4>, with thick white fur, pointed ears, a dark nose, and a curved tail.
<Subject 3> is the young blonde woman in <Video 1>, with long blonde hair and a light-pink button-down shirt with rolled-up sleeves.
<Subject 4> is the young man in <Video 2>, with short wavy brown hair and a dark-grey hoodie with drawstrings.
<Audio 1> is the voice-timbre reference for <Subject 3> (S1), containing a spoken English vocal layer.

summary:
[reference generation + audio reference] The target video shows <Subject 3> eating a cookie in <Subject 1>. <Subject 4> enters with <Subject 2>, which lunges toward the cookie. The three-shot exchange uses <Audio 1> as the voice-timbre reference for <Subject 3> and ends with a canned audience laugh.

retention_analysis:
<Subject 1> (appears in [Shot 1], [Shot 2], [Shot 3]): fully_preserved - the exposed brick wall, orange tufted sofa, patterned pillows, neon sign, and wooden coffee table are retained.
<Subject 2> (appears in [Shot 1], [Shot 2]): fully_preserved - the Samoyed's thick white fur, pointed ears, dark nose, and curved tail are retained.
<Subject 3> (appears in [Shot 1], [Shot 2], [Shot 3]): fully_preserved - the blonde woman's identity, long hair, and light-pink shirt are retained.
<Subject 4> (appears in [Shot 1], [Shot 2]): fully_preserved - the young man's short wavy brown hair and dark-grey hoodie are retained.
<Audio 1>: reference - its vocal timbre guides the dialogue delivery of <Subject 3> without copying the original signal.

detailed_description:
The target video uses a realistic multi-camera sitcom style with warm indoor lighting.
[Shot 1] A medium shot establishes <Subject 1>, the coffee shop with its exposed brick wall, orange tufted sofa, patterned pillows, neon sign, and wooden coffee table. <Subject 3> (S1), the young woman with long blonde hair and a light-pink button-down shirt with rolled-up sleeves, sits on the sofa holding a chocolate-chip cookie. From the left, <Subject 4>, the young man with short wavy brown hair and a dark-grey hoodie with drawstrings, enters holding the leash of <Subject 2>, the thick-furred white Samoyed with pointed ears, a dark nose, and a curved tail. The dog lunges toward the cookie and pulls the leash taut. <Subject 3> (S1) jerks her hand back and, using the clear youthful voice timbre referenced from <Audio 1>, exclaims with light annoyance, <d>[English] Hey! Watch your dog!</d> She closes her lips and guards the cookie while <Subject 4> pulls the dog back.
[Shot 2] At 00:03.000, the shot cuts to a close-up of <Subject 4> (S2), the young man in the dark-grey hoodie from Shot 1, sitting beside <Subject 3> on the sofa and holding <Subject 2> securely in his arms. <Subject 4> (S2) says in a casual young male voice with a playful tone and an easy conversational pace, <d>[English] He just likes cookies more than me.</d> He closes his mouth into an apologetic smile and strokes the dog's thick white fur.
[Shot 3] At 00:05.000, the shot cuts to a close-up of <Subject 3> (S1), the blonde woman in the light-pink shirt from Shot 1. Her annoyance softens as she looks toward the Samoyed. <Subject 3> (S1) replies in the same clear youthful voice referenced from <Audio 1> with an amused cadence, <d>[English] Well, he has good taste at least.</d> She smiles and raises the cookie in a small toast-like gesture. A classic canned audience laugh begins immediately after the line and continues through the final frame.

overall_soundscape:
Soft indoor coffee-shop room tone continues throughout the scene.

non_diegetic_music:
N/A
```

**What this example pins that the prose rules do not:**

- The `summary` task-type prefix is **bracketed, space-separated, `+`-joined**:
  `[reference generation + audio reference]` â€” not `reference_generation` underscores.
- `retention_analysis` entries cite the shots each subject appears in:
  `<Subject 1> (appears in [Shot 1], [Shot 2], [Shot 3]): fully_preserved - ...`.
- An `<Audio N>` bound to a speaker is defined as
  *"`<Audio 1> is the voice-timbre reference for <Subject 3> (S1), containing a spoken English vocal layer."*
  and is cited **at the vocal event** in the description (*"using the clear youthful voice timbre
  referenced from <Audio 1>"*).
- A speaker's identity info rides **inside the shot prose** at first appearance, and the ID is
  repeated at every later vocal event (`<Subject 3> (S1)` in Shots 1 and 3).
- Lip-closure statements follow dialogue (*"She closes her lips and guards the cookie"*).
- A diegetic audience laugh is described **in `detailed_description`**, not in the audio sections.
- `overall_soundscape` may legitimately be a **single sentence**.

### 17.2 The base-mode family (context â€” R2V uses none of these)

The base guide defines the other four modes with **fixed first-line instructions** (then one blank
line, then the three core fields). Our mode (R2V / full-reference) has **no instruction line** â€” the
compiler must not emit one:

| Mode | Fixed first line |
|---|---|
| T2VA | *(none â€” starts directly with the fields)* |
| I2VA | `For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.` |
| FL2VA | `How the reference pictures align with the target video â€” Picture 1 (from Shot 1) aligns with the 0.00-second mark of the target video; Picture 2 (from Shot N) aligns with the S.SS-second mark of the target video.` |
| L2VA | `How the reference pictures align with the target video â€” <Picture 1> (from Shot N) aligns with the S.SS-second mark of the target video.` |

`S.SS` is the effective duration to exactly two decimals; `N` is the actual final shot. The base
field is `integrated_multimodal_description` (vs `detailed_description` in full-reference mode).
FL2VA **favors a single shot** for continuous interpolation. The guide ships four complete case
prompts (baker T2VA, train-window I2VA, umbrella FL2VA, falling-glass L2VA) â€” useful as secondary
fixtures and as the format reference if a base mode is ever added.

### 17.3 Two documented dialects â€” and the decision

**The official ComfyUI R2V template's own default prompt is loose free-form prose**, not the
six-section format (extracted from the template's `PrimitiveStringMultiline` widget): a comic-book
scene written as `CUT 1:` / `TRANSITION:` / `CUT 2:` blocks with inline `<Picture 1/2>` and
`<Audio 1>` mentions and no section fields at all.

So both dialects demonstrably work. The decision stands as **structured six-section**, because:

1. Every audio rule is defined in terms of the structured fields (`overall_soundscape`,
   `non_diegetic_music`, `<d>` in `detailed_description`) â€” a free-form prompt has nowhere to put
   the `N/A` sentinel or the soundscape vocabulary, which is exactly how the B-150 proof's audio
   defects happened.
2. The B-150 sweep measured better conditioning with the structured dialect (the v2/v3 six-section
   prompts vs the earlier loose default).
3. The structured format is machine-validatable (Â§13) â€” the free-form dialect is not.

The free-form dialect remains an **escape hatch** for manual prompt edits (the Prompt tab's manual
override), not a compiler target.

### 17.4 ComfyUI prompt-guide findings (user guidance)

From `docs.comfy.org/tutorials/video/minimax/minimax-h3-prompt-guide`:

1. **Reference sheets (R2V).** One reference image may hold several views of a subject or the
   panels of a storyboard. The sheet gets **its own `<Picture N>` entry** (a picture cited only
   inside a `<Subject N>` definition is *not* used as a separate reference), the entry describes
   the panels (*"`<Picture 2> is a character sheet with three panels: a portrait, a full front view,
   and a full back view of the woman`"*), panels are **numbered or labeled** so shots can point at
   them (*"[Shot 2] the shot's keyframe corresponds to the full front view panel of `<Picture 2>`"*),
   and the sheet's printed text is kept to those labels. A sheet keeps several views inside the
   9-reference limit. â†’ New References-tab role + rules 30.
2. **Hallucinated speech has a documented fix:** *"If a quiet shot comes back with speech you did
   not ask for, write out both fields explicitly and regenerate."* â†’ the Audio tab should surface
   this hint; the compiler always writes both fields explicitly (never omits them).
3. **Bans as positives, with the worked example:** `no subtitles and no on-screen text` â†’
   *"the sign above the door is blank"*. In R2V, scope exclusions through the retention markers
   (what each reference contributes) rather than negated prose.
4. **Multi-speaker + audio-reference failure mode (S7):** *"H3 can hand an audio reference to the
   wrong speaker even when the prompt text and the reference connection order are both correct."*
   Mitigations: keep each shot to the references that shot needs; **tie a line to a visible event**
   (*"When the phone is at his ear, the man in the coat speaks"*) rather than to an absolute
   timecode. Escape hatch: generate the line with a voice tool and supply it as that speaker's
   audio reference with the matching marker (`fully_copy` / `partially_copy`).
5. **Length formula, exact:** the `length` input *"snaps up to the model's 17-frame block grid
   (17k+5)"* â€” confirming rule 26's derivation.
6. **Resolution:** 768px short edge = **1344Ã—768 at 16:9**, rounded to multiples of 32.
7. **Prompt embeddings:** ComfyUI's standard `embedding:` syntax works in H3 prompts
   (`ComfyUI/models/embeddings/`, referenced by name). The 10 published style embeddings are
   **community-contributed (silveroxides), unofficial, and visual-only** (B-150 already measured
   that none is an audio lever). The compiler must not emit `embedding:` tokens unless they are
   configured input.

### 17.5 Technical findings from the ComfyUI tutorial

From `docs.comfy.org/tutorials/video/minimax/minimax-h3`:

1. **Audio converges later than the picture.** Video and audio denoise together in one pass, and
   *"the audio track keeps improving at step counts where the picture has stopped changing. Speech
   and voice timbre converge last: at 8 steps they are the weakest part of the output, and 12 steps
   and above keep the track usable."* â†’ **validates the 40-step default** and hard-warns against
   turbo/low-step builds for any speech-bearing clip (the 4-step turbo LoRA would wreck speech).
2. **Steps:** *"most of the gain by step 16. Beyond about 50 the difference is hard to see"* â€”
   matching the B-150 measurements (40 good, 50 poor value).
3. **Flow-shift is a model-definition property:** ComfyUI's H3 definition carries `shift` `12` and
   `audio_shift` `3`, and every workflow except FastH3 reads them from the definition (no shift
   node in our template). FastH3 ships `ModelSamplingMiniMaxH3` at `shift_video` `10` /
   `shift_audio` `3`; sampling a distilled build at the wrong shift shows **grid artifacts**. â†’
   Graph-builder note: never emit a shift node against the w4a8 stack; the definition's 12/3 is
   correct for it.

### 17.6 Ecosystem findings (`awesome-minimax-h3-integration`)

1. **Reference-audio quality guidance (S7):** *"a clean, clearly spoken 10-second clip is picked up
   more reliably than a noisy one."*
2. **FL2VA vs Ref2VA base quality:** *"FL2VA, trained only with keyframes, typically yields better
   raw output. Ref2VA accepts more reference material but has lower base quality; the Ref Patch can
   partially bridge this gap."* The **Ref Patch** (148 MiB, Apache-2.0,
   `lihaoyun6/MiniMax-H3-Ref-Patch`) diffs the 112 keys shared between the ref2va and fl2va weights
   and lets the lighter FL2VA checkpoint partially mimic Ref2VA behaviour. â†’ an honest-limitation
   note for our R2V choice and a **future quality lever** (out of scope for B-153; record only).
3. **Compiler prior art** â€” three community compilers with architectures directly relevant to ours:
   - `ComfyUI-MiniMax-H3-Promptor` â€” embeds `<Picture X>` **inline into the narrative action line**
     ("zero-hallucination inline annotation"), decoupling visual analysis from text structuring.
   - `ComfyUI-MiniMax-H3-Guide` â€” **"Typed Plan v2"**: splits identity, keyframes, motion, edit
     source, voice, and score into explicit typed roles, then compiles them into valid H3 prose â€”
     the same shape as our intent-inputs â†’ compiler design.
   - `OpenH3-IR` â€” **write-then-validate**: writes the document, then checks it and fixes what is
     wrong, following the Context-IR format MiniMax published â€” the same shape as our
     compiler + 33-rule validator.
4. **Prompt corpora for test fixtures:** `awesome-minimax-h3-prompts` (categorized corpus with WebM
   examples: story, action/fantasy, ad/product, music performance, vlog) and
   `minimax-h3-1000-prompts` (curated 1K T2V prompts with a 3-field structure anatomy). â†’ sources
   for compiler golden fixtures beyond the official example.
5. **Long/multi-shot:** `ComfyUI-H3-Motion-Context` feeds the previous block's final frame **and
   audio** forward for >15 s chains â€” the documented path if D-6's ceiling is ever raised.

### 17.7 MiniMax's own packaged skill + format-usage evidence

MiniMax publishes an installable **`h3-prompt-writing` agent skill**
(`MiniMax-AI/MiniMax-H3/skills/h3-prompt-writing`) whose references are **byte-identical** to the
two guides (15,773 / 23,553 bytes) â€” so the guides are the complete rule set; the skill adds four
distilled tips, all now encoded as rules or notes:

- *"Always match the total duration of the description to the requested video length (4â€“15
  seconds)"* â†’ rule 31.
- *"Prefer concrete visual and audio details over abstract words like 'cinematic' or 'beautiful'"*
  â†’ rule 32 (generalizes the soundscape anti-abstraction lesson to the whole description).
- *"Avoid plot summaries, unresolved reference labels, and timing that does not match the requested
  duration"* â†’ rules 3, 9, 31, 33.
- *"Preserve the exact field names, section order, labels, and timing notation"* â†’ rules 1, 8.

**Format-usage evidence:** GitHub code search for the three audio/section field names returns
**~2,700 results** â€” the six-section format is the community standard for H3 prompting (heavily used
in Chinese ComfyUI wikis), not an obscure corner of the spec.
