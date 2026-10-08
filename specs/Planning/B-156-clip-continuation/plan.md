# B-156 - Continue this clip (clip-to-clip continuation): implementation handoff

**Status:** `planned` - design complete, ready to spike. No code has been changed for this item.
**Created:** 2026-10-07
**Depends on:** B-153 (render path, `implemented`), B-152 (compiler contract). **B-155 is NOT a blocker** - its library is `planned`/unbuilt (`VideoCompositionList.razor` and `VideoCompositionLibrary.razor` do not exist), so this item's primary UI lands on the **existing** [VideoStudio.razor](../../../DreamGenClone.Web/Components/Pages/VideoStudio.razor) Queue-tab surfaces (section 6.4). The library/lineage row is a *conditional* integration that rides on B-155 when it lands, never a prerequisite.
**Extends:** B-152 **D-6** (duration policy: "multi-shot chaining remains out of scope") and the composer's origin model (D-3), which has no notion of a previous clip.
**Supersedes nothing.**

---

## 0. How to use this document

1. Read section 2 (verified current state) first. Every claim has a live ComfyUI probe or a file/line behind it. The node inventory in 2.1 is why this feature is cheaper than expected.
2. Section 3 separates what is verified locally from what is **not yet verified** - including the honest quality question. Do not promise a user seamlessness until 3.3 is resolved.
3. Implement in the order in section 8. **C0 is a spike, not production code**: it must prove the guide mechanism on the live host before anything else is built.
4. Section 9 is the acceptance protocol. It requires an A/B against a single longer render, not just "it produced a clip".

---

## 1. Why this exists

Operator request (2026-10-07): *"how do I continue the video from where it ended for another 5 seconds?"* Today the answer is a manual four-step workaround (2.3) that no interface describes. The operator also asked whether the widely-reported quality loss when chaining clips is a genuine model limitation with no workaround.

Two deliverables:

- **A first-class continuation path** - one action on a finished clip that opens a composer already anchored to that clip's final frame (and optionally its audio), with the lineage recorded.
- **An honest, evidence-backed statement** of what continuation can and cannot deliver, so the UI does not over-promise. Section 3.3 tracks that verification.

---

## 2. Verified current state

### 2.1 What the live H3 host actually offers (probed 2026-10-07, `comfy.kenacwood.net`, 1273 node types)

The host has **more H3 machinery than the app uses**. Two nodes matter here and both are already installed:

| Node | Signature (from `object_info`) | Why it matters |
|---|---|---|
| `MiniMaxH3ReferenceToVideo` | in: `clip, prompt, width, height, length, ref_image_size` + optional `vae, audio_vae, ref_images` (0-9 IMAGE), `ref_videos` (0-3 IMAGE), `ref_video_audios` (0-3 AUDIO), `ref_audios` (0-3 AUDIO). **out: `CONDITIONING, LATENT`** (`positive`, `LATENT`). Description: *"`<Picture i>` / `<Video k>` / `<Audio j>` reference conditioning for MiniMax H3. Use the same tags when prompting."* | The graph already produces a **latent**, which is what the guide node consumes; and the node already supports reference **video** and **audio** slots the app never emits. |
| `MiniMaxH3AddGuide` | in: `positive: CONDITIONING`, `latent: LATENT`, `frame_idx: INT` + optional `vae: VAE`, `audio_vae: VAE`, `image: IMAGE`, `audio: AUDIO`. out: `CONDITIONING`. | **The native continuation seam.** It pins an image (optionally with audio) at a chosen frame index of the target clip, directly in the conditioning. |

Consequence: a continuation graph is the **existing** Ref2VA graph plus **one node**:

```
MiniMaxH3ReferenceToVideo(ref_images=..., prompt, width, height, length) -> positive, latent
MiniMaxH3AddGuide(positive, latent, frame_idx=0, image=<previous clip's final frame>, audio=<optional>, vae, audio_vae) -> positive'
BasicGuider(model, positive') -> SamplerCustomAdvanced            (unchanged from today)
```

No pod change is required for the primary mechanism. Also present and relevant:

| Node | Note |
|---|---|
| `MiniMaxH3ImageToVideo` | optional `first_frame` / `last_frame` - H3 has a native first+last-frame mode. A different task from Ref2VA; mixing modes is a scope decision, not part of this item. |
| `EmptyMiniMaxH3LatentAV` | latent source for graphs that do not start from Ref2VA. |
| `ContextWindowsManual` (+ `WanContextWindowsManual`, `LTXVContextWindows`) | ComfyUI's sliding-context-window mechanism (`context_length`, `context_overlap`, `fuse_method`, `causal_window_fix`, ...) - the long-video approach used by other families. **Unverified for H3** (R-4); it would not by itself lift the trained 362-frame ceiling. |
| `MiniMaxH3SigmaShift` | **Forbidden.** `COMPILER-RESEARCH.md` section 17.5 records that sampling the distilled w4a8 stack at a wrong shift shows grid artifacts; the model definition's `shift 12` / `audio_shift 3` is correct and must not be overridden. |
| `ComfyUI-H3-Motion-Context` | **NOT installed.** The community node for >15 s chains ("feeds the previous block's final frame *and* audio forward"). Only relevant if the native guide proves insufficient (C4 fallback, and then it is a documented pod change: pod-registry provision step + `/pre_start.sh` restart-proofing). |

### 2.2 What the app has today

| Fact | Evidence |
|---|---|
| The graph builder emits image references **only** - `ref_images.ref_image_<i>` in operator order. It never emits `ref_videos`, `ref_video_audios` or `ref_audios`, and it has no guide node. | `DreamGenClone.Infrastructure/Models/MiniMaxH3VideoGraph.cs:189` |
| Reference roles are image-shaped: `FrameAnchor`, `SubjectDefinition`, `CharacterSheet`. A frame anchor is labelled `([Shot 1] first frame)` - which is *why* the manual workaround works at all. | `SceneVideoReferenceRole`, compiler label emission |
| There is **no lineage**: `SceneVideos` has no `SourceVideoId`, and `SceneVideoOriginKind` has no previous-clip member (`Standalone`, `SceneImage`, `AssetImage`, `VideoCoveragePlan`). | `SceneVideoRecord.cs:104`, DB schema |
| The composer's reference picker lists only session-gallery images, and reference bytes are read through the **scene-image** storage root. | `VideoStudio.razor:308`, `SceneVideoService.cs:184` |
| ffmpeg is configured per model (`FfmpegPath`) and already invoked for loudness normalization and stream verification, so extracting a frame costs no new dependency. | `SceneVideoAudioProcessor` |
| Reference **audio** slots (`ref_audios`) were explicitly deferred as slice S7. | `SCOPING.md` D-5 |

### 2.3 The manual workaround that exists today (the baseline this item automates)

1. `ffmpeg -sseof -0.1 -i <normalized clip> -frames:v 1 -update 1 last-frame.png` (verified: a 1344x768 PNG extracted from the operator's own clip).
2. Upload the PNG into an asset (Asset Studio -> *Upload image*), which gives it an asset-image id.
3. Open `/roleplay/video-studio/asset/{thatImageId}` - the composer seeds it as `<Picture 1> ([Shot 1] first frame)`, `fully_preserved`.
4. Write the next beat, keep the LoRA stack / canvas / frame length / seed, queue. ~23 min per 5.2 s clip.

**Known limits of the workaround:** motion is not guaranteed to flow on (the anchor pins one frame and H3 restarts sampling); the join can pop; **audio restarts entirely** - the previous room tone and any mid-word speech are lost. Replacing steps 1-3 with one action, and bringing the guide and audio mechanisms to bear, is the point of this item.

---

## 3. What is verified, and what is not

### 3.1 Verified locally

- The two H3 nodes above exist on the host with the signatures quoted (live `object_info` probe).
- Ref2VA emits a latent the guide node accepts, so the continuation seam is additive to the current graph.
- The clip's final frame can be extracted deterministically (done, from the operator's render).
- The app's current reference path cannot express continuation (2.2).

### 3.2 Spike results (RUN 2026-10-07 - C0 is GREEN)

The C0 spike proved all four questions on the live host. Harness (throwaway, git-ignored):
`artifacts/tmp/h3-guide-spike/run-guide-spike.py`; graphs in `.../out/*-graph.json`; results summary in
`.../out/spike-results.json`. Three arms at 1344x768, `length=5`, seed 7, one reference image.

| # | Question | Result | Evidence |
|---|---|---|---|
| 1 | Does `frame_idx=0` + `image` condition the first frame? | **YES** | Frame 0 of the guided clip vs the guide image: **PSNR 18.29 / SSIM 0.727** against the **control's 10.38 / 0.475** (the control carried the same reference image but no guide). +7.9 dB, +0.25 SSIM at ONE step, so the anchor is genuinely influencing frame 0. With audio also carried: 19.58 / 0.765. |
| 2 | Does the guide's `audio` input carry the source audio forward? | **YES** | The guided-with-audio clip's PCM differs from the guide-only clip's PCM (MD5 `3EF3A921...` vs `B5C3810B...`) with seed, prompt, image and step count all identical - the carried audio changes generation rather than being ignored. |
| 3 | Does `length` still respect the node's accepted values with a guide present? | **YES** | All three arms rendered `length=5` (the node's documented minimum) as 1344x768 h264 + AAC mp4s. The guide does not change the accepted envelope. |
| 4 | How are the guide image and audio uploaded, and does the upload land? | **Both land** | `POST /upload/image` with `type=input` accepts a **PNG and a WAV** (multipart `image` field), returning `{"name": ..., "type": "input"}` for each. `LoadAudio`'s combo reads the uploaded wav by name, so audio needs **no** client special-casing. |

**Additional findings that shaped the slices:**

- **The guide graph is structurally valid**: the host executed nodes `1, 40, 41, 10, 11, 2, 3, 13, 12, 14, 4, 30` with no `node_errors` - i.e. `MiniMaxH3AddGuide` consumed the Ref2VA `positive`/`latent` plus `frame_idx`, `vae`, `audio_vae` and `image`, and `BasicGuider` accepted its output. One node is the whole seam, as designed.
- **A guided render costs more than a plain one.** At 4 steps the guided arm was still inside `SamplerCustomAdvanced` after **310 s** of execution, while the whole control run (cold model load included) took 324 s. At 1 step the guided arm completed in 9 s, so the mechanism is cheap when the sampler is. C3's cost note and the composer's estimate must not imply a continuation is as fast as the source render.
- **The host's `Python-urllib/*` user-agent is answered 403 by the proxy in front of it** (no UA and a browser UA both return 200). The app's `HttpClient` sends a .NET UA and is unaffected - this only cost the harness a diagnosis round.
- **A caution recorded honestly:** reading `system_stats.vram_free` is NOT a reliable "is it working" signal - it showed 13.3 GB free while the node was actively sampling, which briefly suggested a stall that did not exist. Do not use it to decide whether to interrupt a render.

### 3.3 External verification (COMPLETED 2026-10-07): the operation must know this

The operator's claim was *"quality degrades when continuing, and there is no fix or workaround - it is a limitation of the models."* Verified verdict: **substantially correct, but wrong in its absolutism.**

| Claim | Verdict | Confidence |
|---|---|---|
| Continuing a clip from its final frame degrades quality | **TRUE** - several independent projects document it; two quantify it | High |
| It affects motion/identity too | **TRUE but unquantified** - "seed lottery", phase advance/lag/repeat, hard-cut degradation; no motion metric published anywhere | Medium |
| "There is no fix or workaround" | **PARTIALLY TRUE** - no method *eliminates* it; several *reduce* it materially; a "no way to hide it at all" reading is false | High both ways |
| It is architectural, not an H3 quirk | **TRUE** - the literature names the mechanism (exposure bias / error accumulation / attribute drift) and every open long-video system carries an anti-drift component | High |

**Measured magnitudes (small-n, project-specific, but two independent projects agree in direction):**

- **Contrast/brightness bloom of roughly +4 % per join** - "by clip 8 the picture is about 40 % more contrasty than clip 1"; by then ~2 % of pixels are pure black and 0.5 % pure white. Crucially: *"the gain happens inside generation, so it cannot be fixed after the fact by adjusting files or settings"* - **post-hoc grading cannot restore clipped detail.**
- **Audio loses about one third of its treble relative to bass per join** - chains "start sounding deeper, muddier, a bit echoey". Not visible in a waveform view; needs a treble/spectral check.
- A different project measures the opposite *sign* for texture: **+13 % fine texture per join** at 736×1280 with anti-drift on, slight under ~4 windows, visible at 7 - and states a practical ceiling of **~4 windows (~30-40 s)**. So drift direction is task- and prompt-dependent, which is exactly why this item must measure rather than assume.
- A third measured an unmitigated **Laplacian-sharpness gen/src ratio falling ~0.81 → 0.30 over 11 links** (single task; its author calls it "task-specific evidence, not a general benchmark").

**Mechanism (why the manual workaround drifts even though it "pins" a frame):** the pinned frame is *not copied into the next clip* - it is handed to the model as a near-clean reference and the model **regenerates** it. The ComfyUI H3 implementation pins visual conditions at a static 0.999 clean mixture, and *"core has no policy for feeding a generated output back as a nearly clean condition at the next scene's noisiest model call"*. The same convention is inherited by Wan2GP, DiffSynth and SGLang - a family-wide characteristic, not a ComfyUI bug. In other words: the drift lives in the conditioning feedback loop, which is precisely what `MiniMaxH3AddGuide` participates in.

**Counter-evidence, stated honestly:** one maintainer could not reproduce a sharpness loss or seam problem in a 3-clip test and warns that Laplacian-variance numbers vary strongly with framing, motion and subject scale; another reports 3-minute chains with little visible degradation and a 72 s music video that looks fine. Degradation is therefore **conditional**: severe for long chains with the camera holding on one subject, mild or invisible for 3-4 joins and for shot changes/hard cuts.

**What this changes in this plan (not optional):**

| Consequence | Where it lands |
|---|---|
| Never promise seamless or unlimited extension in the UI; say the join is a regeneration and audio starts as a continuation of the previous track | C-11, C-13, 6.3 |
| A **chain-length budget** with a hard reset is a first-class configurable policy (~3-4 continuations before starting a fresh chain and bridging) | C-13, C-14 |
| Per-block **drift instrumentation from day one** (Laplacian-variance ratio, luminance mean/percentiles, HSV saturation, black/white clipping-pixel percentage) plus an **audio treble check** | C-14 |
| Prefer **latent-path carry** (the guide node already operates on the latent) over any decode → resize → re-encode round trip | C-1, 6.2 |
| Do **not** offer "fix it in post" grading as a remedy for clipped detail (it cannot restore it); per-block tone anchoring is the only cheap, honest colour control - and it stays off-by-default until the A/B shows visible tone drift | C-13b |
| Do **not** ship the one prototype aimed at eliminating the drift (dynamic condition-timestep scheduling): its own author says it is unvalidated and must not become the default, and it needs a patched ComfyUI core | C-9 |

Primary sources: the upstream H3 Motion-Context author's quantified breakdown (issue #20), his "no one has solved the issue... all current fixes are simply band-aids" statement (issue #44), the cross-model statement "Wan, LTX, H3, doesn't matter" (issue #27), the maintained fork's "you cannot fully prevent degradation... if you do long extensions" plus its mitigation list (MultiRef #14), the +13 %/+4-window figure (joeygambino Multishot README), the mechanism note (ethanfel `VISUAL_CONTEXT_DRIFT_RESEARCH.md`), the counter-test (tritant #106), and the literature that names the phenomenon (FramePack's "Drift Prevention" paper and its peers: exposure bias / error accumulation).

---

## 4. Decisions proposed for this item

| # | Decision | Proposed as | Why |
|---|---|---|---|
| **C-1** | Continuity mechanism | **Primary: `MiniMaxH3AddGuide`** (native, installed, additive to the current graph). Reference-video slots are a **secondary** enrichment, never the primary mechanism. The carry must stay on the **latent path** (the guide node operates on the latent) - never a decode → resize → re-encode round trip. | The guide conditions the latent at a frame index; a reference video is only semantic conditioning. The guide is one node and one image upload, and it avoids one documented error source (3.3). |
| **C-2** | A continuation is a **new** composition, not a mutation | It creates a new record with `SourceVideoId` = the clip continued. The source is never rewritten. | Renders are immutable history (B-155 V-3); a chain is a lineage, not an edit. |
| **C-3** | Lineage is explicit and additive | New nullable columns on `SceneVideos` (`SourceVideoId`, `ContinuationKind`, `GuideFrameIndex`, `SourceFrameRelativePath`, `SourceFrameSha256`) + an index on `SourceVideoId`. **No unique constraint** - several continuations of one clip are allowed. | Matches the additive-migration idiom; no renumbering, no backfill; existing rows are simply not continuations. |
| **C-4** | Where the extracted frame lives | Beside the clip: `SceneVideoRoot/<recordId>/frames/last.png`, served by the existing `/scene-videos` mapping. The reference **byte reader** needs an **explicit root selector** (scene-image vs scene-video) - never a silent search across roots. | The frame belongs to the clip, not to a session gallery; an explicit root keeps a single decision path (no-fallback rule). |
| **C-5** | Extraction is deterministic and recorded | Extraction runs at continuation time (not at render time), is idempotent, and stores the frame's **path + sha256** on the continuation record so the anchor is reproducible. | A continuation must be reproducible from stored data, like every other record. |
| **C-6** | Audio continuity is opt-in and visible | `ContinuationKind` distinguishes `FrameOnly` from `FrameAndAudio`; the latter passes the source clip's stored audio into the guide's `audio` input. If that audio is missing or unreadable, **fail fast** naming the input - never silently continue without it once the operator chose audio continuation. | No-fallback rule. |
| **C-7** | The length ceiling is unchanged | A continuation clip is still 124-362 frames; a guide does not lift the trained range. The UI states the chain's total duration and never implies a longer single-shot window. | Measured model envelope; do not invent headroom. |
| **C-8** | No new pod dependency for the primary path | C0-C3 need **no** pod change. `ComfyUI-H3-Motion-Context` is only a C4 decision, and if taken it is a documented pod change (registry provision step, `/pre_start.sh` self-heal, restart-proof verification). | RunPod pod rule; also keeps the feature shippable today. |
| **C-9** | Forbidden nodes stay forbidden | Never emit `MiniMaxH3SigmaShift`; never override the definition's shift. Do not emit `ContextWindowsManual` until R-4 is answered. | `COMPILER-RESEARCH.md` section 17.5. |
| **C-10** | Compiler impact is scoped, not improvised | The primary path needs **no** new label tags (the guide is a graph input, not a reference label). Only the secondary reference-video path introduces `<Video k>` / `<Audio j>` tags, which need compiler rules and validator changes and therefore their own confirmed plan. | The 33-rule contract is the compiler's acceptance checklist; a new tag family must not sneak in. |
| **C-11** | The UI tells the truth about the seam | The continue action states that the join is a **regeneration** of the previous frame (not a copy), that drift accumulates across joins, that audio continues the previous track rather than matching it, and that post-processing cannot restore clipped detail. Plus the source citation in the tooltip/hint. | Verified in 3.3: the operator must not discover the limitation after a 23-minute render, and must not be told "grade it afterwards". |
| **C-12** | The A/B is part of "done" | Acceptance requires a real chain **and** a single longer render of the same content as the control, with the seam and drift recorded as evidence, plus the operator's verdict. | "It rendered" is not acceptance for a quality-sensitive feature. |
| **C-13** | **Chain-length budget with a hard reset** (essential) | Configurable policy: at most **N continuations** per chain (default 3) before `PrepareContinuationAsync` refuses with a named policy message. N lives in the model's qualification (`MiniMaxH3Refs.MaxContinuationChainLength`, resolved fail-fast like the word band) - a single configured source, UI-backed via Model Manager, no code literal. | 3.3: drift accumulates per join; ~3-4 windows is the documented comfortable zone, and "starting a fresh chain from a new clip resets it". The reset must be a policy the app enforces, not folklore. |
| **C-13b** | **Per-block tone anchoring** (optional, deferred to the A/B) | Only if 9.3 shows visible tone drift: a post-normalization ffmpeg step that histogram-matches the continuation's luminance+saturation to the source's last frame, applied **before** the loudness normalize. It is a band-aid (R-11: it cannot restore clipped detail) - ship it only as a configured, off-by-default step the metrics justify. | The published drift is tone (contrast/colour), but the evidence also says post-hoc grading cannot recover detail; anchor tone, never promise detail recovery. |
| **C-14** | **Per-block drift instrumentation from day one** | Every continuation records, per block: Laplacian-variance ratio (gen vs source), luminance mean/percentiles, HSV saturation mean, black/white clipping-pixel percentage, and an audio treble/bass ratio - computed by the existing ffmpeg path after normalization, stored in the new `DriftMetricsJson`, shown in the composer's completed-record block and (later) the library. | 3.3: published magnitudes are small-n and partly contradictory (contrast bloom vs texture ratchet); one maintainer warns single sharpness metrics are framing-sensitive - so measure, do not assume. |
| **C-15** | **Audio continuity semantics** | `FrameAndAudio` pins the source's audio window so it **ends at the join** (the model continues the track) rather than restarting. Per-block treble loss is monitored (C-14) because the documented failure (~1/3 of treble per join) is inaudible in a waveform and only shows up as "deeper and muddier". | 3.3: audio drifts independently of the picture and at a different rate. |
| **C-16** | Route + open sequence | New route `/roleplay/video-studio/continue/{SourceVideoId}` (+ `?kind=frame\|audio`), two new `[Parameter]`s, and an `OnInitializedAsync` branch to `LoadContinuationAsync()` that calls `PrepareContinuationAsync` and hydrates the tabs (6.3, 6.4). The continuation is an ordinary seeded composition once loaded, so the existing compile/stage/start buttons are reused unchanged. | One code path into the composer; no new page; the queue buttons already work. |
| **C-17** | The frame is **both** an anchor reference and the guide | `PrepareContinuationAsync` returns the extracted frame as a `FrameAnchor` `SceneVideoReference` (prompt label `<Picture 1> ([Shot 1] first frame)` + a `ref_images` slot - the proven workaround) **and** the graph emits `MiniMaxH3AddGuide(frame_idx=0, image=<same frame>)`. One stored frame, two graph roles; never two files. | The guide pins frame 0 in the latent; the reference pins scene identity in the prompt. Dropping either loses what made the manual workaround work. |
| **C-18** | Drift storage and config source | `DriftMetricsJson` (new column, 5.1) stores C-14's metrics; the chain budget reads `MiniMaxH3Refs.MaxContinuationChainLength` from the model qualification (same single-source pattern as `LoudnessTargetLufs` and the B-155 word band). Missing/unparseable metrics do not fail the render - they are recorded as `null` with a verification note (measurement is evidence, not a correctness gate). | Metrics are observational; the only *configured* policy (the budget) must fail fast when absent. |
| **C-19** | Guide upload determinism | The guide frame uploads under `frame-<sha8>.png` (sha-derived, stable across retries), the guide node id + inputs derive from the record, and the audio input (when present) uses a sha-derived name too. | The same record must produce the same graph bytes - and the ComfyUI execution cache stays warm across a relaunch, which the operator already benefits from. |

---

## 5. Data model

### 5.1 `SceneVideos` additions

```sql
ALTER TABLE SceneVideos ADD COLUMN SourceVideoId TEXT NULL;        -- the clip this one continues
ALTER TABLE SceneVideos ADD COLUMN ContinuationKind INTEGER NULL;   -- SceneVideoContinuationKind: 1 = FrameOnly, 2 = FrameAndAudio
ALTER TABLE SceneVideos ADD COLUMN GuideFrameIndex INTEGER NULL;   -- the frame index the guide pinned (0 until C0 says otherwise)
ALTER TABLE SceneVideos ADD COLUMN SourceFrameRelativePath TEXT NULL;
ALTER TABLE SceneVideos ADD COLUMN SourceFrameSha256 TEXT NULL;
ALTER TABLE SceneVideos ADD COLUMN DriftMetricsJson TEXT NULL;     -- per-block drift instrumentation (C-14); NULL when not measured

CREATE INDEX IF NOT EXISTS IX_SceneVideos_SourceVideo ON SceneVideos (SourceVideoId);
```

Rules: add to the `CREATE TABLE` text **and** as guarded ALTERs (`SceneVideoRepository.SchemaSql` plus the `pragma_table_info` idiom). A row is a continuation iff `SourceVideoId IS NOT NULL`. A stale `SourceVideoId` (source deleted) must surface as broken lineage, never be treated as a standalone clip. **`ContinuationKind` is stored as INTEGER** like `Status` and `OriginKind` (the other enum columns in this table), validated on read and fail-fast on an unknown value - the earlier "stored as text" note was inconsistent and is corrected here. `DriftMetricsJson` is written only after a render completes, so a draft or in-flight continuation has it NULL.

### 5.2 Domain

`SceneVideoRecord` gains the six nullable members above; `SceneVideoContinuationKind` is a new append-only enum (`FrameOnly = 1`, `FrameAndAudio = 2`) - never renumber. A continuation request is expressed as a service input (6.1), not as loose parameters.

`SceneVideoReference` gains one **optional** member so the extracted frame can be read from the scene-video root without a hidden heuristic:

```csharp
public enum SceneVideoReferenceStorageRoot { SceneImage = 0, SceneVideo = 1 }

public sealed record SceneVideoReference(
    SceneVideoReferenceRole Role,
    string FileRelativePath,
    string FileName,
    string? SubjectDescription = null,
    string? RetentionMarker = null,
    int? DerivedFromReferenceIndex = null,
    SceneVideoReferenceStorageRoot StorageRoot = SceneVideoReferenceStorageRoot.SceneImage);
```

Defaulting to `SceneImage` keeps every existing `ReferencesJson` row valid (the field simply deserialises absent). The XML doc on `FileRelativePath` ("relative path under SceneImageRoot") is updated to name both roots. The continuation's auto-added anchor frame carries `StorageRoot = SceneVideo`.

---

## 6. Service, client and UI

### 6.1 Service surface (`ISceneVideoService`)

```csharp
/// <summary>Prepares a continuation composition from a finished clip: extracts its final frame, records lineage.</summary>
Task<SceneVideoContinuationSeed> PrepareContinuationAsync(
    string sourceVideoId, SceneVideoContinuationKind kind, CancellationToken cancellationToken = default);

/// <summary>Lists the clips that continue this one (lineage view; also powers the composer banner).</summary>
Task<IReadOnlyList<SceneVideoRecord>> ListContinuationsAsync(
    string sourceVideoId, CancellationToken cancellationToken = default);
```

`PrepareContinuationAsync` must:

1. Refuse a source that is not `Complete` (fail fast, naming the status); refuse a source with no normalized file.
2. **Walk the chain** from `SourceVideoId` links to compute the chain depth and the chain's first block (for the C-13 budget and tone anchoring). A cycle in the links fails fast.
3. Refuse a source whose chain is already at the configured `MaxContinuationChainLength` with a message naming the policy and the budget ("chain of 3 reached - start a fresh clip and bridge") - never a silent block.
4. Extract the final frame idempotently (`SceneVideoRoot/<sourceId>/frames/last.png`), compute its sha256, and return a seed carrying the source record, the frame path + sha, the chain depth, and the settings to clone (LoRA stack, canvas, frame length, model, seed policy, refImageSize).
5. Return the frame **both** as a `FrameAnchor` `SceneVideoReference` (so the compiled prompt emits `<Picture 1> ([Shot 1] first frame)` and the graph binds a `ref_images` slot - exactly the proven workaround) **and** as the guide input (C-17). One stored frame, two graph roles.

It must **not** create a record - creation happens when the operator queues (B-155 V-4 promotion) or saves a draft.

### 6.2 Graph and client

- **Request shape:** `SceneVideoGenerationRequest` (in `DreamGenClone.Application/Abstractions/IVideoGenerationClient.cs`) gains optional continuation members - `GuideImage` (bytes + name), `GuideAudio` (bytes + name), `GuideFrameIndex` - defaulting to absent. Non-continuation callers are unchanged.
- **Graph builder:** `MiniMaxH3VideoGraph.Build` gains an optional guide section. When present it emits `MiniMaxH3AddGuide` wired `positive` / `latent` from the Ref2VA node, `frame_idx` from the record, `image` from an uploaded `LoadImage` of the extracted frame, and - for `FrameAndAudio` - `audio` from an uploaded audio input, plus `vae` / `audio_vae` (the same configured VAEs the Ref2VA node already uses). The builder must **fail fast** when a continuation is requested but the frame file, the sha match, the audio input (for `FrameAndAudio`), or the VAEs are missing - never emit a guide-less graph that looks like a continuation.
- **Client:** `MiniMaxH3VideoClient` uploads the guide PNG and, for `FrameAndAudio`, an audio file into the host input directory. The PNG reuses `ComfyUiWorkflowTransport.UploadImageAsync` (already used for references); the audio upload path is a C0 question (VHS-style nodes post audio through the same `/upload/image` endpoint - if it does not work, that is a client change, not a pod change). Every upload is verified after the fact, and the graph is refused when an upload did not land.
- **Determinism:** the guide upload name derives from the sha (`frame-<sha8>.png`), and the guide node id + inputs derive from the record - the same record produces the same graph bytes. This also keeps the ComfyUI execution cache warm across a relaunch (as the operator already saw).

### 6.3 UI - the continuation flow (concrete)

**Route** (new, no collision with `/asset/{id}`, `/coverage/{planId}` or `/session/...`):

```
@page "/roleplay/video-studio/continue/{SourceVideoId}"     + optional ?kind=frame|audio (default frame)
```

New `[Parameter] public string? SourceVideoId` and `[Parameter] public string? ContinueKind`. When `SourceVideoId` is set, `OnInitializedAsync` branches to a new `LoadContinuationAsync()` instead of `LoadSeedAsync()` (see 6.4). `kind=audio` maps to `SceneVideoContinuationKind.FrameAndAudio`; anything else maps to `FrameOnly`.

**Entry points (all on the EXISTING Queue tab - no new page or list is required for the MVP):**

1. The completed-record player block (`_record.Status == SceneVideoStatus.Complete`, the block that already shows the video + stream badges): two buttons under the verification badges -
   **Continue (frame only)** → `/continue/{_record.Id}?kind=frame` and **Continue (frame + audio)** → `/continue/{_record.Id}?kind=audio`.
2. The **Latest finished clip** block (shown when the composer has no output of its own): the same two buttons against `LatestCompleted.Id`.
3. Each **Recent compositions** row with `Status == Complete && NormalizedFileRelativePath != null`: a **Continue** link beside **Open** → `/continue/{recent.Id}?kind=frame`.

**Honest warning (C-11), shown once under any of the entry points, verbatim intent:**

> The next clip starts from this clip's final frame, but the model *regenerates* it - it is not a copy. Quality drifts a little at each join (contrast, texture and treble), and grading afterwards cannot restore detail that is already clipped. Keep chains to ~3 joins, or render one longer clip instead.

**Continuation banner** (Intent tab top, when opened via `/continue/...`): *"Continuing {sourceId} · {source duration} · {kind} · chain {n} of {max}"*. The chain counter comes from `PrepareContinuationAsync`'s depth and makes the C-13 budget visible before the operator writes a word. When `kind` is `FrameAndAudio`, the banner adds: *"audio continues the previous track (it will not match exactly)"*.

**References tab:** the extracted frame appears as a read-only first row labelled `anchor (final frame of {sourceId})` with its sha256, `StorageRoot = SceneVideo`, and `fully_preserved`. It is **not** editable or removable; the normal "add a reference" controls remain for the operator's subject references. No new picker or component is created.

**Sampling tab:** cloned from the source record (canvas, frame length, steps, seed, refImageSize, LoRA stack). The frame-length dropdown is unchanged - a continuation is still 124-362 frames (C-7), and the banner states the chain's total duration as "this clip + the previous clip(s)".

**Scene/action description:** starts empty with the hint *"Describe the next beat only - not a repeat of the previous clip's action."* (R-8: the compiler assumes `[Shot 1]` starts the action.)

### 6.4 UI fit - exact touch points in the existing [VideoStudio.razor](../../../DreamGenClone.Web/Components/Pages/VideoStudio.razor)

The page already contains every surface this item needs. The changes are additive and local:

| Touch point | Today | Change |
|---|---|---|
| `@page` directives (lines 1-5) | 5 routes | add `/roleplay/video-studio/continue/{SourceVideoId}` |
| `[Parameter]` block (lines ~811-815) | `SessionId`, `InteractionId`, `SourceImageId`, `AssetImageId`, `CoveragePlanId` | add `SourceVideoId`, `ContinueKind` |
| `OnInitializedAsync` (~line 855) | `LoadSeedAsync()` → candidates → `CompileAsync()` → `RefreshQueueAsync()` | branch: `if (SourceVideoId is not null) await LoadContinuationAsync(); else ...existing...` |
| Header buttons (~line 42) | Recompile / Stage / Start | unchanged - the continuation is just a seeded composition, so the existing queue buttons already apply |
| Intent tab (~line 90) | form fields | prepend the continuation banner (6.3) |
| References tab (~line 270) | seeded reference list | prepend the read-only anchor row when continuing |
| Sampling tab | fields | pre-filled from the source record via `LoadContinuationAsync` |
| Queue tab, completed-record block (~line 712) | badges + `<video>` | add the two **Continue** buttons |
| Queue tab, Latest-finished-clip block (~line 742) | header + `<video>` | add the two **Continue** buttons |
| Queue tab, Recent compositions rows (~line 758) | **Open** link | add a **Continue** link for `Complete` rows |

No new Razor component is needed for the MVP. Lineage **display** in a library is deferred to B-155's `VideoCompositionList.razor` (its "continued by N" / "continues X" column) - this item only *stores and queries* the lineage (`ListContinuationsAsync` + the six columns), which is exactly what B-155 will render later. Until then lineage is visible in the composer banner and via `DbQuery`, not in a list.

---

## 7. Compliance

- **RP-engine plan gate.** This touches `Domain/RolePlay/**`, `Infrastructure/RolePlay/**` and `Web/Application/RolePlay/**`, plus the compiler's file set only if C-10's secondary path is taken. The approved plan is the approval unit; a scope change comes back for confirmation.
- **No fallbacks.** A missing source frame, a sha mismatch, unreadable source audio, absent VAEs, or a host that rejects the guide graph all **fail fast with a diagnostic naming the missing input**. There is no silent "continue without the guide" path - that would hand the operator a clip they believe is continuous when it is not.
- **No hidden defaults.** `GuideFrameIndex`, the extraction seek offset, audio-continuation behaviour and the reference-video window size (if taken) are configured, UI-backed values; nothing hardcoded in code decides behaviour.
- **Forbidden patterns.** No `MiniMaxH3SigmaShift` emission, no shift override, no context-window node until R-4 is resolved.
- **Reuse.** The continuation flow reuses the existing [VideoStudio.razor](../../../DreamGenClone.Web/Components/Pages/VideoStudio.razor) tabs and buttons (6.4) - no new page or picker. Lineage *display* extends B-155's shared list component when it lands; no second list, card or status vocabulary.
- **Pods.** C0-C3 add nothing to a pod. Any C4 pod change follows the RunPod rule: `helpers/runpod/pod-registry.json` provision step, deployment manifest update, `/pre_start.sh` self-heal, restart-proof verification, and `POD-PERSISTENCE.md` compliance.
- **Tests green.** Baseline: the 27 pre-existing `CharacterLora*` failures are unrelated; this item may not add a failure.
- **Razor rules** per `.github/instructions/razor-editing.instructions.md`.
- **Database rules**: `DreamGenClone.DbQuery` only, dev DB only, never commit a `.db`.

---

## 8. Slices

### C0 - Spike: prove the guide mechanism on the live host (no production code)

A throwaway harness (the S1 pattern: dump the emitted graph, submit it, read `/history`) rendering **5 frames at 1 step** from the existing w4a8 stack with `MiniMaxH3AddGuide(frame_idx=0, image=<extracted final frame of the operator's clip>)`. It answers 3.2's four questions. Evidence: the graph dump, `node_errors: {}`, the mp4, and frame 0 compared against the guide image.

**Exit:** a written answer to 3.2 with the graph JSON and the output file. If the guide cannot condition frame 0 as expected, stop and re-plan (the fallback is C4's node, which is a pod change).

### C1 - Lineage + final-frame extraction + budget

The six columns and additive DDL, domain members (record + `SceneVideoReference.StorageRoot`), the extraction step (idempotent, sha-recorded), the explicit-root reference reader, the chain walk, the budget refusal (C-13), `PrepareContinuationAsync`, `ListContinuationsAsync`, and the `MiniMaxH3Refs.MaxContinuationChainLength` config field (seeded into the qualification; resolved fail-fast).

**Tests:** extraction is deterministic and idempotent; a sha mismatch fails fast; a non-`Complete` source is refused with a named status; a stale `SourceVideoId` surfaces as broken lineage; the reference reader refuses an ambiguous or unknown root and resolves the scene-video root explicitly; a chain at the configured budget is refused with a message naming the policy; a link cycle fails fast; the budget is missing from the qualification -> `Resolve` fails fast naming the setting.

**Exit:** from the operator's existing clip, `PrepareContinuationAsync` yields a seed with an extracted, hashed frame and the correct chain depth - verified by DB row and the file on disk.

### C2 - Composer continuation flow (existing UI)

The route `/continue/{SourceVideoId}` (+ `?kind`), the `OnInitializedAsync` branch to `LoadContinuationAsync()`, the continuation banner, the cloned Sampling settings, the read-only anchor row in the References tab, the empty next-beat description hint, and the three **Continue** entry points in the Queue tab (6.3, 6.4).

**Exit:** one click from a finished clip to a queueable continuation composition on the existing page, with the chain counter visible in the banner. The lineage is stored and queryable even though no library renders it yet.

### C3 - Graph + client guide support

Optional guide emission, PNG and audio upload, fail-fast validation, determinism test.

**Tests:** graph JSON for `FrameOnly` and `FrameAndAudio` (assert the guide node, its wiring, `frame_idx` and the VAEs); refusal when the frame file, audio or VAEs are missing; identical record produces an identical graph; the upload verifier rejects a failed upload.

**Exit:** a real continuation render completes through the durable lane with the guide node present (the first end-to-end proof).

### C4 - Optional enrichments (each needs its own confirmation)

(a) reference-video and reference-audio conditioning (`ref_videos`, `ref_video_audios`), including the compiler's `<Video k>` / `<Audio j>` tags and validator rules - an RP-engine *and* compiler change, so it comes back for a plan and confirmation; (b) `ComfyUI-H3-Motion-Context` provisioning (a pod change) if the native guide proves insufficient; (c) context windows (R-4) only if a documented H3-compatible configuration exists; (d) **per-block tone anchoring (C-13b)** - the ffmpeg histogram-match step, only if the A/B shows visible tone drift.

### C5 - Docs, acceptance evidence, honest wording

Update `SCOPING.md` (D-6 no longer "multi-shot chaining remains out of scope"; record the continuation decision), `COMPILER-RESEARCH.md` (section 17.5 gains the verified guide facts), the backlog, and the UI warning wording from 3.3. Record the A/B evidence.

**Exit:** the acceptance protocol in section 9 has been run and its evidence is attached.

### C6 - Chain policy and drift instrumentation (C-13, C-14, C-15, C-18)

The chain budget enforcement in `PrepareContinuationAsync`; the per-block drift metrics (Laplacian-variance ratio, luminance statistics, HSV saturation, clipping-pixel percentage) and the audio treble/bass ratio, computed by the existing ffmpeg path and stored in `DriftMetricsJson`; the composer's completed-record block shows the metrics as a small `drift` line. **Tone anchoring (C-13b) is NOT built here** - it is a deferred, A/B-gated optional with its own confirmation.

**Tests:** the chain budget refuses an (N+1)th continuation with a named policy message (not a silent block); metrics are computed deterministically for a fixture clip and stored; a chain's depth is derived correctly from `SourceVideoId` links; missing/unparseable metrics are recorded as `null` with a verification note rather than failing the render.

**Exit:** on the operator's own content, the app reports per-block drift for a 3-block chain, and the safe budget is a configured value the UI enforces.

---

## 9. Verification protocol

### 9.1 Unit and build

```
dotnet build DreamGenClone.Web\DreamGenClone.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\
dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\ --filter 'FullyQualifiedName~SceneVideo'
dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\
```

The operator's app locks `bin\Debug`, so build to the temp path; never kill their process.

### 9.2 Live mechanism proof (C0)

Dump the graph, submit it, confirm `node_errors: {}`, output under the `images` key, and that frame 0 matches the guide image. Record the graph JSON in the session workspace.

### 9.3 Acceptance A/B (C5, required by C-12)

| Arm | Content | Purpose |
|---|---|---|
| **A - chain** | Clip 1 (already rendered) + a ~5.2 s continuation using the guide, `FrameOnly` | Is the join usable? |
| **A' - chain with audio** | The same, `FrameAndAudio` | Does audio carry forward, and does it help or hurt? |
| **B - single longer render** | The same two beats as **one** 243-frame (10.1 s) clip | The control: no seam, one audio pass, ~46 min |

Score each arm on: first-frame match against the guide, motion continuity across the join, identity consistency, anatomy at the join, audio continuity, and the operator's verdict. Record the measured per-frame cost of both approaches. **Do not skip arm B** - it is the only way to know whether chaining earns its seam, and it costs about the same wall clock.

Additionally, run the C-14 metrics on every arm and on a 3-block chain, and report: measured per-join drift for this content versus the published figures in 3.3 (+4 % contrast and 1/3 treble loss per join, +13 % texture per join on a different project). The point of the A/B is to establish **this** pipeline's own curve, since the published numbers disagree in sign and are all small-n.

### 9.4 What "done" means

A user can continue a finished clip in one action; the lineage is visible and queryable; the graph provably contains the guide; the A/B evidence exists; the UI wording matches the verified limitations; and nothing in the chain path can silently degrade to a plain render.

---

## 10. Risks and open items

| # | Item | Note |
|---|---|---|
| R-1 | **RESOLVED 2026-10-07 (3.3): degradation is real, architectural, and not eliminated by any known method; it is reduced by several, and it is conditional** | The plan now carries a chain budget (C-13), per-block instrumentation (C-14) and audio semantics (C-15). The remaining risk is the *magnitude on this content*, which only 9.3 can answer. |
| R-9 | Published magnitudes are small-n and partly contradictory | Contrast bloom vs texture ratchet point in different directions; no peer-reviewed H3 measurement exists; one maintainer disputes a reported sharpness loss and warns the metric is framing-sensitive. Treat every published number as a hypothesis, and the A/B as the measurement. |
| R-10 | Audio drift is inaudible in the obvious places | ~1/3 treble loss per join reads as "deeper and muddier", not as an artefact. Without the C-14 treble check, a chain can pass a listening test by a non-expert ear and still be degrading. |
| R-11 | "Fix it in post" is a trap | Clipped highlight/shadow detail is unrecoverable once generation has produced it; grading the finished chain cannot bring it back. The UI must not imply otherwise, and per-block tone anchoring is the only honest colour control (and it is a band-aid by the upstream author's own taxonomy). |
| R-12 | The drift-elimination prototype must stay out | The one approach with a documented causal basis (dynamic condition-timestep scheduling) is unvalidated by its own author, requires a patched ComfyUI core, and must not become a default. If it is ever tried, it is an opt-in experiment behind a flag, never the shipped policy. |
| R-2 | A reference video is an **IMAGE batch** | `ref_videos` takes frames, so a frame window means **N image uploads** (124 frames of 1344x768 PNG is ~130 MB). If C4(a) is taken, use a short configured window of the most recent frames and measure the upload cost; the guide's single image is why C-1 prefers it. |
| R-3 | Audio continuity is not audio *coherence* | Carrying the previous track forward may still step in level or timbre, and loudness normalization runs per clip, so a chain can step in loudness. The A/B must include a listen, not just a measurement. |
| R-4 | Context windows for H3 are unverified | `ContextWindowsManual` exists on the host, but its applicability to the H3 latent/AV pair is unknown, and it does not lift the trained 362-frame ceiling. Treat as research, not as a plan. |
| R-5 | Chain cost | Each block costs the full render price (~23 min per 5.2 s), so a 4-block chain costs ~90 min for 21 s. Chains are the only way past the 15.1 s trained maximum; they are never cheaper. |
| R-6 | Stale lineage | Deleting a source clip must not orphan a continuation silently; surface it as broken lineage wherever lineage is shown (the composer banner today, B-155's library later). |
| R-7 | `frame_idx` semantics unproven | Whether `frame_idx` is the target clip's frame index, and what happens at non-zero indices (mid-clip guidance), is part of C0. Do not build a "pin a mid-clip pose" feature on an assumption. |
| R-8 | Prompt must describe the next beat | Compiler rules R08/R09 assume `[Shot 1]` starts the action, so a continuation prompt that re-describes the source's beat reads as a repetition. Decide the wording or composer hint in C2, and keep it out of the validator until confirmed. |

---

## 11. Handoff checklist

1. Read sections 2 and 3 - **3.3 is the external verification and it changes what the UI may claim**. Re-probe `object_info` for the two H3 nodes at HEAD (the pod can be re-provisioned independently of this repo).
2. Run **C0** and answer 3.2 in writing. Do not start C1 before C0 is green.
3. Implement C1 -> C2 -> C3, running the SceneVideo filter after each. C2 lands entirely on the existing [VideoStudio.razor](../../../DreamGenClone.Web/Components/Pages/VideoStudio.razor) - no B-155 dependency.
4. Implement **C6** (chain budget + drift instrumentation) before shipping any chain policy to the operator: without the measurements, "3-4 joins" is borrowed folklore and the UI cannot honestly say what it is doing. **C-13b tone anchoring stays out** unless the A/B shows visible tone drift.
5. Decide C4 (reference video/audio conditioning, Motion-Context, context windows, or tone anchoring) only with the operator, and only if the A/B shows the native guide is not enough.
6. Run 9.3's A/B, attach the evidence, then finish C5's docs and wording.
7. Report: the graph JSON, the DB rows showing lineage, both A/B clips with the operator's verdict, the measured per-join drift for this content, and the still-open items (R-4, R-7, R-9).

**Suggested PR boundaries:** (1) C0 spike evidence + C1, (2) C2, (3) C3 + C6, (4) C5 docs.

---

## Appendix A - evidence commands

```
# the two nodes this item depends on (live, read-only)
(Invoke-RestMethod 'https://comfy.kenacwood.net/object_info/MiniMaxH3ReferenceToVideo').MiniMaxH3ReferenceToVideo
(Invoke-RestMethod 'https://comfy.kenacwood.net/object_info').MiniMaxH3AddGuide

# is the community chain node present? (it is not, as of 2026-10-07)
(Invoke-RestMethod 'https://comfy.kenacwood.net/object_info').PSObject.Properties.Name -match 'Motion|Context'

# extract the final frame of a finished clip (verified working)
.venv\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe -hide_banner -y -sseof -0.1 -i <clip>.mp4 -frames:v 1 -update 1 last-frame.png
```

## Appendix B - file map

**Changed (existing):** `DreamGenClone.Domain/RolePlay/SceneVideoRecord.cs`; `DreamGenClone.Domain/ModelManager/MiniMaxH3Refs.cs`; `DreamGenClone.Web/Application/ModelManager/MiniMaxH3ModelSettings.cs`; `DreamGenClone.Application/RolePlay/ISceneVideoRepository.cs`; `DreamGenClone.Infrastructure/RolePlay/SceneVideoRepository.cs`; `DreamGenClone.Application/Abstractions/IVideoGenerationClient.cs`; `DreamGenClone.Infrastructure/Models/MiniMaxH3VideoGraph.cs`; `DreamGenClone.Infrastructure/Models/MiniMaxH3VideoClient.cs`; `DreamGenClone.Web/Application/RolePlay/ISceneVideoService.cs`; `DreamGenClone.Web/Application/RolePlay/SceneVideoService.cs`; `DreamGenClone.Web/Application/RolePlay/SceneVideoRenderingJobHandler.cs`; `DreamGenClone.Web/Application/RolePlay/SceneVideoAudioProcessor.cs` (drift metrics); `DreamGenClone.Web/Components/Pages/VideoStudio.razor`; `specs/Planning/B-152-scene-video-composer/SCOPING.md`; `specs/Planning/backlog.md`.

**New:** `DreamGenClone.Domain/RolePlay/SceneVideoContinuation.cs` (the seed + `SceneVideoContinuationKind` + the drift-metrics record); `DreamGenClone.Tests/RolePlay/SceneVideoContinuationTests.cs`; `DreamGenClone.Tests/RolePlay/MiniMaxH3GuideGraphTests.cs`.

**NOT part of this item:** `DreamGenClone.Web/Components/Shared/VideoCompositionList.razor` and `DreamGenClone.Web/Components/Pages/VideoCompositionLibrary.razor` are **B-155** (unbuilt). This item only stores and queries lineage; the library renders it later as a B-155 follow-on, not as part of B-156's required work.

**Compiler files only if C4(a) is taken:** `MiniMaxH3SceneVideoPromptCompiler.cs`, `SceneVideoCompilation.cs`, and `COMPILER-RESEARCH.md` (rule text) - each behind its own confirmation.
