# B-150 — MiniMax H3 local video: application-integration handoff

**Audience:** a planning agent scoping how to wire local MiniMax H3 video generation into
DreamGenClone.
**Status:** local proof complete (2026-10-06). This document is the scoping input; it is NOT a plan
and proposes no code changes.
**Scope:** local H3 video on the ComfyUI host. Cloud video providers are out of scope.

Read alongside:
- [`RESEARCH-minimax-h3-16gb.md`](./RESEARCH-minimax-h3-16gb.md) — full findings, the run record
  (**29 runs; 26 configurations in the catalog, 23 executed**), timings, limits, and the audio
  measurements.
- [`helpers/h3-local-host/README.md`](../../../helpers/h3-local-host/README.md) — the working harness and how to re-run any configuration.
- [`tools/video-audio-inspection/README.md`](../../../tools/video-audio-inspection/README.md) — how a clip's audio is graded.

**Working reference implementation:** the harness is the closest thing to a spec for the client
behaviour, the request shape and the reference handling. Read it before planning the equivalents.

---

## 1. What is already proven (treat as given, do not re-derive)

A working local video pipeline exists **outside the application**, driven by
[`run-h3-ref2va-proof.py`](../../../helpers/h3-local-host/run-h3-ref2va-proof.py): it uploads
reference images to the host over HTTP, converts the official Comfy-Org workflow template to API
format, overrides the model/LoRA/params, submits to `/prompt`, polls `/history`, and saves the
resulting mp4.

**Verified stack** (present on the host at `D:\ComfyUI\models\`):

| Role | Artifact | Size |
|---|---|---|
| DiT (Ref2VA) | `diffusion_models/minimax_h3_ref2va_pruned_w4a8_mixed.safetensors` (Kijai) | 10.96 GB |
| Text encoder | `text_encoders/qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` (Comfy-Org), `device=cpu` | 14.6 GB |
| Video VAE | `vae/minimax_h3_video_vae_int8_convrot.safetensors` (Comfy-Org) | ~3 GB |
| Audio VAE | `vae/minimax_h3_audio_vae_fp32.safetensors` (Comfy-Org) | ~0.6 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` | 1.11 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_softer_rank64_v1.safetensors` | 1.11 GB |
| Realism LoRA | `loras/h3-realism-people.safetensors` (fal, trigger `r34l1sm`) | 131 MB |
| Realism LoRA | `loras/h3-facial-realism-closeup.safetensors` (prithivMLmods) | 75 MB |

**Verified request shape:**

- Graph = official `video_minimax_h3_r2v.json` template converted UI→API. Node types:
  `UNETLoader` → chained `LoraLoaderModelOnly` → `ComfySwitchNode` → `BasicGuider`;
  `MiniMaxH3ReferenceToVideo` (its `ref_images` is an **autogrow** input, so 1..N references);
  `CLIPLoader` (`device=cpu`); 2× `VAELoader`; `KSamplerSelect`; `BasicScheduler`; `RandomNoise`;
  `SaveVideo`.
- **Audio path:** the sampler emits one **joint audio+video latent**; it feeds both `VAEDecode`
  (video VAE) and `VAEDecodeAudio` (audio VAE) — each pulls its own half out of the packed latent —
  and `CreateVideo` muxes the two into a single MP4. Any video graph that omits the audio decode or
  the mux loses the audio; this is a real integration risk, not a detail.
- Sampling: **euler / beta**, CFG **1.0** (CFG-distilled — no negative prompt), 40 steps, denoise 1.0.
- Output: 1344×768, 24 fps, frame lengths **124 (5.2 s)** and **192 (8.0 s)** both verified, with a
  stereo AAC 32 kHz audio track of the same duration.
- Prompt: a **6-section structured Ref2VA** document (`subject_definitions`, `summary`,
  `retention_analysis`, `detailed_description`, `overall_soundscape`, `non_diegetic_music`).
- Cost: **~23–30 min** per 5.2 s clip; **~49–53 min** per 8.0 s clip, single GPU. Same config varied
  19–30 min on seed alone.

**Best observed configuration so far** (not established as optimal — the operator intends further
testing): w4a8 DiT · `sexytime@0.8` + `realism@1.0` stacked · **two references** (scene image +
MediaPipe-derived subject crop) · explicit 6-section prompt · 40 steps · 1344×768 · len 124.

**Honest limits** (see research doc §"Honest limits"): both faces in frame simultaneously is
bounded by the input reference; contact-point anatomy still warps; 768p is the ceiling because
H3-Base is open-weights at 768p and the 2K path (H3-Regenerate-2K) is not open-sourced.

**Audio is native and always generated — do not treat it as a later phase.** H3 generates video and
stereo audio **jointly in one forward pass** and muxes both into the same MP4. Measured: every clip
carries a stereo AAC 32 kHz track matching the video duration, with harmonicity ≈ 0.85 and 42–76 % of
energy in the vocal-formant band, i.e. **voiced vocalisation (moans) is present and prominent**. Two
measured defects to plan for: the **body/impact band** is largely absent (`sub<100 Hz` = 0.1–2.4 %),
so the track reads as moans over little else; and the audio is **~12 dB too quiet** (native
−24 to −30 LUFS) and needs loudness normalization before delivery. Spoken lines are supported and
follow a strict format. See research doc §"AUDIO".

> **Review clips in a real player (VLC), not the VS Code preview** — VS Code's Electron build lacks an
> AAC decoder and plays these files silently. That is a tooling limitation, not a pipeline defect.

---

## 2. What the repository already has (verified 2026-10-06)

This is the most important section for scoping: **video *intent* is already modelled, video
*rendering* is not.**

### 2.1 Video concepts that already exist

| Thing | Location | Note |
|---|---|---|
| `MediaProductionKind.Video = 5`, `VideoWithAudio = 6`, `LipSyncPerformance = 7` | `DreamGenClone.Domain/RolePlay/CompiledMediaBrief.cs` | Video is already a first-class production kind. |
| `MediaCompilerCapability.VideoKeyStates / VideoActionArc / VideoCameraMotion / ExternalAudioReferences / NativeVideoAudio / LipSyncWindows` | same file | The capability vocabulary for video is already declared. |
| `DeterministicMultimodalMediaCompiler.BuildVideo(...)` | `DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs:177` | Already compiles a video brief (coverage, key state, action arc, continuity, audio ownership). Registered for `Video` and `VideoWithAudio` in `Program.cs:384-404`. |
| `IMultimodalMediaCompiler` / `...Registry` / `...CompilationService` | `DreamGenClone.Application/RolePlay/IMultimodalMediaCompiler.cs` | Extensible compiler registry — the natural home for an H3 video prompt compiler. |
| Production-media substrate: `MediaCapabilityProfile`, `MediaCapabilityCell`, `ProductionIntentSnapshot`, `CompiledMediaRequest`, `ProductionWorkload`, `ProductionWorkloadItem`, `ProductionAttempt`, `ProductionReviewDecision`, `ProductionDerivative` | `DreamGenClone.Domain/RolePlay/ProductionMediaModels.cs`, `DreamGenClone.Application/RolePlay/IProductionMediaRepository.cs` | Includes provider submission/poll/result recording with output path, sha, metadata, cost snapshot. |
| `IProductionDispatchAdapter` (+ `RunPod…`, `Together…` adapters) | `DreamGenClone.Application/RolePlay/IProductionWorkloadService.cs:143`; registered `Program.cs:547-549` | The provider plug point: `SubmitAsync` / `PollAsync` / `CancelAsync`. |
| `ProductionWorkloadService`, `ProductionReconciliationService` | `DreamGenClone.Web/Application/RolePlay/` | Execute and reconcile workloads against adapters. |
| UI: `ProductionDashboard.razor`, `ProductionWorkspace.razor` | `DreamGenClone.Web/Components/Pages/` | Existing production-review surfaces. |

### 2.2 ⚠️ Critical caveat — that subsystem is currently unused

Direct DB query of `dreamgenclone.dev.db`:

| Table | Rows |
|---|---|
| `MediaCapabilityProfiles` / `MediaCapabilityCells` | **0 / 0** |
| `ProductionWorkloads` / `ProductionAttempts` / `ProductionReviewDecisions` | **0 / 0 / 0** |

versus the pipeline that is actually in daily use:

| Table | Rows |
|---|---|
| `SceneImages` | 747 |
| `SceneAssetImages` | 1,543 |
| `DurableBackgroundJobs` | 3,650 |
| Providers with an image protocol | 14 |

**So: the production-media substrate is well-built but has never carried a single row.** The live
image path is the scene-image pipeline. A planning decision is required (§9, D-1).

### 2.3 The live image pipeline (the proven path)

| Concern | Location |
|---|---|
| Model kind | `ModelKind { Text = 0, Image = 1 }` — `DreamGenClone.Domain/ModelManager/ModelKind.cs` (**no Video member**) |
| Provider protocol | `ImageProtocol { OpenAiImages = 0, ComfyUi = 1, ComfyUiServerless = 2 }` — `Domain/ModelManager/ImageProtocol.cs` |
| Model registration | `RegisteredModel` — `Domain/ModelManager/RegisteredModel.cs` (image-editor block: graph kind, diffusion/TE/VAE, steps/CFG/sampler/scheduler/denoise/shift, **one** LoRA name+strength+capability) |
| Model family / dialect | `SceneImageModelFamily`, `SceneImagePromptDialect` — `Domain/ModelManager/SceneImageModelFamily.cs` |
| Schema + migrations | `Infrastructure/Persistence/SqlitePersistence.cs` (RegisteredModels `CREATE TABLE` ≈ line 740; additive `ALTER TABLE` migration list ≈ line 1504) |
| Durable jobs | `Domain/Processing/DurableBackgroundJob.cs` — `DurableJobLane { TextAnalysis = 1, PromptCompilation = 2, ImageRender = 3, ImageEdit = 4 }` (**no video lane**); handler contract `Application/Processing/IDurableBackgroundJobHandler.cs` (`JobType` + `HandleAsync`) |
| Job handlers | `Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` (implements both `IBackgroundJobHandler` and `IDurableBackgroundJobHandler`); registered `Program.cs:286, 432` |
| ComfyUI client | `Infrastructure/Models/ComfyUIImageClient.cs:20` implements `IImageGenerationClient`, `IReferenceConditionedImageClient`; `GenerateAsync:516`, `GenerateWithReferencesAsync:808`; dispatchers registered `Program.cs:314-315` |
| Result storage | `Application/Abstractions/ISceneImageStorageService.cs` → `Infrastructure/Storage/SceneImageStorageService.cs`; root `PersistenceOptions.SceneImageRoot = "data/scene-images"`; served at `/scene-images` (`Program.cs:829`) |
| Result record | `Domain/RolePlay/SceneImageRecord.cs` (`SceneImageStatus`, `SceneImageOperation`, `SceneImageRenderMode`, `FileRelativePath` = `"{sessionId}/{imageId}.png"`) |
| Model config UI | `Web/Components/Shared/ModelDetailsEditor.razor` (Editor Graph at :136, Editor LoRA Capability at :195) and `Web/Components/Pages/ModelManager.razor:469` |
| Image UI surfaces | `SceneImageStudio.razor`, `SceneImageCompose.razor`, `SceneImageEditor.razor`, `SceneImageFinish.razor`, `SceneImageIdentity.razor`, `SceneImageGallery.razor` |
| Lease/concurrency config | `Domain/ModelManager/FunctionModelDefault.cs` — `MaxConcurrentJobs` (1–16), `DurableJobLeaseSeconds` (**1–3600**), `DurableJobPollIntervalMilliseconds`, retry counts. Per-function, UI-backed. |

**Notable gap:** the lease ceiling is **3600 s (60 min)**. An 8 s clip can take ~53 min, leaving
almost no headroom, and a longer clip would exceed it.

---

## 3. What does NOT need building (scope control)

A planner will be tempted to over-scope. These already exist and are sufficient:

| Need | Already served by |
|---|---|
| A ComfyUI HTTP provider | `ImageProtocol.ComfyUi` on the `Providers` row — no new protocol needed. H3 is reached over the same `/prompt`, `/history`, `/view`, `/upload/image` endpoints the image client already uses. |
| Submit → poll → fetch → persist | `ComfyUIImageClient` implements exactly this cycle; a video client is the same shape with a longer poll and an mp4 instead of a png. |
| Durable job infrastructure | `DurableBackgroundJob`, the lane worker, lease/retry/poll options, startup recovery — all present. Only a lane/`JobType` and a handler are added. |
| Rejecting an unconfigured model | The resolver fail-fast pattern (`ImageEditorModelResolver`) is already the house style; copy it rather than inventing validation. |
| Per-model capability qualification | `MediaCapabilityProfile` / `MediaCapabilityCell` + statuses exist if the production-media path is taken. |
| Result storage + checksum + provenance | `ISceneImageStorageService` pattern, `SceneImageRecord`, and the production-media attempt fields. |
| Model configuration UI | `ModelDetailsEditor.razor` already renders a model-settings block; the video block is an addition, not a new page. |
| Local-first execution | The Constitution requires local execution and no cloud dependency — local H3 satisfies it by construction. |

---

## 4. Suggested slicing (minimum viable slice first)

Nothing in this document requires the whole surface at once. A defensible first slice:

1. **Prove the request from inside the app.** A single injected service that takes (prompt, references,
   settings) and returns mp4 bytes against the configured ComfyUI provider. No UI, no job, no assets.
   This validates the graph conversion, the CPU text-encoder offload, and the audio decode+mux path —
   the three genuinely new things.
2. **Model registration.** `ModelKind.Video` + the video block + migration + resolver fail-fast. Now
   the stack is configured data rather than hardcoded.
3. **Durable job + storage.** Lane, handler, mp4 storage, asset record.
4. **Prompt compiler.** The 6-section R2V document including the audio lanes and dialogue.
5. **UI.** Model config block, generation trigger, review/playback.
6. **Voice pinning** (`ref_audios`) — only if per-character voice consistency is wanted. Independent
   of the rest.

Steps 1–3 produce a working, configurable video producer. Steps 4–6 make it usable.

---

## 5. Required components

Each item: purpose → where it plugs in → configuration it needs.

### C-1. Document the host artifacts (mandatory housekeeping)

The four model files and four LoRAs above must be recorded so they are reproducible from scratch.
If H3 is ever deployed to a RunPod pod this is **mandatory** (`helpers/runpod/pod-registry.json` as
an idempotent `provision` step, plus the deployment manifest and restart-proofing per
`helpers/runpod/POD-PERSISTENCE.md`). For the current LAN host, record the artifact set alongside
the existing `helpers/h3-local-host/` documentation.

### C-2. Video model registration

- Add a **`ModelKind.Video`** member (appended, never renumbered — the integer is persisted and the
  DbQuery transfer table indexes names by value).
- Add a **video block** to `RegisteredModel` + the `RegisteredModels` table via the existing
  additive-migration pattern.
- Add a **video prompt dialect / family** value (H3's structured Ref2VA document) alongside the
  existing `SceneImagePromptDialect` values.

**Configuration (see §6):** graph kind, DiT, text encoder, video VAE, audio VAE, **ordered LoRA
list**, sampler envelope, resolution, frame length, fps, reference-image sizing, max references.

### C-3. Video prompt compiler (H3 Ref2VA dialect)

Turns the app's scene/intent data into an H3 6-section Ref2VA document. Deterministic, versioned.
Plugs into the existing compiler-registry pattern (`IMultimodalMediaCompiler` /
`IProductionMediaCompiler`, or a sibling of the scene-image prompt compilers under
`Web/Application/RolePlay/`). Must carry a compiler key + version for provenance, and must fail
fast when required inputs are missing.

**This compiler owns the audio lanes and must follow H3's fixed rules** (details in research doc
§"Official prompt rules"). It is not a text-only compiler:

- `overall_soundscape` carries ambient sound, physical action sounds and **non-verbal** human
  sounds (moans, gasps, breathing) — 1–4 sentences; `N/A` only if the clip should be silent.
- `non_diegetic_music` carries audience-only score — 1–3 sentences; **`N/A`** when there is none.
- **Spoken dialogue lives in `detailed_description`**, wrapped in `<d>` tags with a language tag,
  preceded by the speaker's identifying phrase and a **stable speaker ID `(Sx)`**. Speaker IDs are
  assigned once in order of actual vocal event across the whole clip and reused at every vocal
  event; they number *speakers*, not subjects.
- **Never emit negative phrasing.** The graph runs a `BasicGuider` at CFG 1 with no negative branch,
  so `"no text, no watermark"` *adds* those words as content. Bans must be rewritten as positives
  (e.g. "the sign above the door is blank").
- `detailed_description` should normally run 350–500 English words; dialogue-dense content may
  prioritise covering the spoken timeline instead.
- If audio references are ever bound, each needs a retention marker (`fully_copy` / `partially_copy`
  / `reference` / `weak_reference`) in `retention_analysis`, matching what the audio actually does.

**Character voices are a real, unused lever.** The node takes up to **3 standalone reference audio
clips** (`ref_audios`) alongside up to **9 reference images** — so a character's voice can be pinned
the same way their face is. The app's `CharacterLora`/identity assets may already be able to supply
that audio; scope it rather than assuming text-only dialogue. A workflow exists to hand a line to
the wrong speaker when several speakers have audio references, so tie lines to visible events rather
than to timecodes.

> There is an existing hard rule in this area: read
> `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` before writing any
> prompt compiler.

### C-4. H3 video client over ComfyUI

Submits the converted workflow, polls `/history`, fetches the mp4 via `/view`. Either extended from
`ComfyUIImageClient` or a new `IVideoGenerationClient` + dispatcher registered in `Program.cs`.
Must record the exact submitted workflow payload for diagnostics, as the image path does
(`SceneImageRenderingJobHandler.cs:204`).

### C-5. Durable job lane + handler

- Add a video lane (`DurableJobLane.VideoRender`) or a distinct `JobType` on an existing lane.
- Add a handler implementing `IDurableBackgroundJobHandler`, registered in `Program.cs`.
- **Lease/concurrency:** must be configurable to cover a ~30–55 min render. Either raise the
  `DurableJobLeaseSeconds` ceiling in `FunctionModelDefault` (1–3600 today) or use a
  heartbeat/renewal pattern. Video concurrency on a single GPU is **1**.

### C-6. Video output storage, serving and asset type

- Store mp4 (and the audio stream it carries).
- Serve with **HTTP range requests** — video playback and seeking need them; the current static
  `/scene-images` mapping is image-oriented.
- Extend the media-type/asset handling so a video is a first-class asset with checksum, byte length
  and metadata.
- Decide whether video reuses the scene-image root or gets its own (e.g. `data/scene-videos`).

### C-7. Reference-image planning (the quality lever)

H3's fidelity comes from 1..N reference images. The proven improvement is feeding a **scene image
plus a deterministic crop** of the same scene. Port the harness logic
([`make-ref-crop.py`](../../../helpers/h3-local-host/make-ref-crop.py), MediaPipe FaceMesh — the same
detector family as the repo's canonical `tools/eye-validation/measure_iris.py`) into an app service,
and record which references were bound (the app already has an ordered-reference-binding concept in
`OrderedMediaReferenceBinding`).

### C-8. UI surfaces

- Model Manager: a **video block** in `ModelDetailsEditor.razor` (mirroring the image-editor block),
  including the multi-LoRA editor.
- A trigger/review surface for video generation, and playback in the review deck / gallery.

### C-9. Provenance and review

Record the compiled prompt, the resolved model settings, the reference bindings, the workflow
snapshot, and the output hash — consistent with the existing image provenance
(`SceneImageRecord`, `ApprovedSceneFrameDecision`) and the production-media attempt fields.

### C-10. Audio verification

Because the audio is generated jointly, **a clip's audio must be graded alongside its frames** —
frame inspection cannot detect silence, a missing body/impact band, or wrong/unintelligible
dialogue. Use the approved tool
[`tools/video-audio-inspection/`](../../../tools/video-audio-inspection/README.md), which reports
stream presence, level envelope, harmonicity, and per-band energy, and names the likely problem.
Note it measures the *signal*, not intelligibility: confirming spoken names actually land needs
listening or an ASR pass.

---

## 6. Required configuration surface

Per repository rules, **every** one of these must be persisted, UI-backed and fail fast when
missing — no hardcoded runtime defaults, no fallback branches.

### 6.1 Model artifacts and graph

| Setting | Notes |
|---|---|
| `VideoGraphKind` | Must select the H3 Ref2VA graph shape. Never guessed. |
| `VideoDiffusionModel` | `minimax_h3_ref2va_pruned_w4a8_mixed.safetensors` |
| `VideoTextEncoder` + device | `qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors`, **must support `device=cpu`** (it does not fit VRAM alongside the DiT) |
| `VideoVae` + `VideoAudioVae` | int8 convrot video VAE + fp32 audio VAE |
| `VideoPromptDialect` | H3 structured Ref2VA (6 sections) |

### 6.2 LoRA stack — **this is the one structural change**

The existing `RegisteredModel` carries a **single** `ImageEditorLoraName` / `ImageEditorLoraStrength`
pair. H3 quality depends on **stacking 2–3 LoRAs** in a defined order (e.g. `sexytime@0.8` then
`realism@1.0`). The video block therefore needs an **ordered list** of `{ name, strength }`, applied
in order, each with an explicit positive strength (no defaults).

Also needed: an optional per-LoRA **trigger token** (the fal realism LoRA needs `r34l1sm` to open
the prompt) and, following the existing `ImageEditorLoraCapability` precedent, a **capability**
declaration so capability-gated features can require a specific LoRA.

### 6.3 Sampling and output

| Setting | Proven value |
|---|---|
| Steps | 40 (50 tested; poor value) || CFG | 1.0 — fixed by the distilled model; **there is no negative prompt** |
| Sampler / scheduler | `euler` / `beta` (required by the AfterMidnight LoRAs; other schedulers break audio) |
| Denoise | 1.0 |
| Width × height | 1344×768 |
| Frame length | 124 or 192 — must satisfy H3's validity rule (`round(duration × 24)` adjusted ≡ 5 mod 17) |
| FPS | 24 |
| `ref_image_size` | `match` (default) or `max` |
| Max reference images | ≥ 3 verified; node accepts up to **9** |
| Max reference audio clips | up to **3** (`ref_audios`) — per-character voice pinning |
| Seed | Persisted for reproducibility; seed matters more than several parameter levers |

### 6.4 Audio

The audio track is generated, not optional, so its controls are configuration too:

| Setting | Notes |
|---|---|
| Audio enabled / kind | `Video` vs `VideoWithAudio` — H3 always produces audio, so the honest kind is `VideoWithAudio` unless deliberately discarded |
| **Target loudness** | **Required.** H3 emits −24 to −30 LUFS (≈12 dB below normal media) and must be normalized before delivery (e.g. `loudnorm=I=-16:TP=-1.5`). Persisted and UI-backed, not hardcoded |
| `overall_soundscape` text | Non-verbal/ambient/action sound. Must be authored, not defaulted |
| `non_diegetic_music` text | `N/A` sentinel when none |
| Dialogue lines | Spring from scenario/RP data; need speaker IDs and a language tag |
| Speaker ID assignment | Ordered by vocal event; must be deterministic and stable across a regeneration |
| Reference audio bindings | Optional per-character `ref_audios` + their retention marker |
| Audio mux/export | Must survive to the stored asset (mp4 carries both streams) |

**Two audio acceptance checks, both cheap to automate:**

1. **Stream presence** — the stored asset must carry **both** a video and an audio stream. A silent
   clip must be a detectable failure, not a surprise at review time.
2. **Loudness in range** — measure integrated LUFS and fail (or normalize) outside the configured
   target. `loudnorm` reports this directly; the approved
   [`tools/video-audio-inspection/`](../../../tools/video-audio-inspection/README.md) covers presence,
   level and spectral character.

Per repository rules the **soundscape/dialogue content and the loudness target must be UI-backed and
persisted**, not hardcoded in the compiler.

### 6.5 Job behaviour

Durable job lease seconds, poll interval, retry counts, max attempts, and video lane concurrency
(1 on a single GPU) — configured per function in `FunctionModelDefault`.

---

## 7. Non-negotiable repository constraints that apply

- **No fallbacks anywhere in the RP engine.** Configured values only; missing required configuration
  must fail fast with explicit diagnostics. No hidden default branches, no guessed substitutions.
- **All RP behaviour controls must be UI-backed persisted data**, not code-only defaults.
- **RP engine code changes require a plan + explicit confirmation before any code change** (see
  `.github/copilot-instructions.md`). Touching prompt slots / `RolePlayEngineService` /
  `RolePlayContinuationService` triggers this.
- **All tests must pass**; no skipped or disabled tests.
- **Never use `git restore` / `git checkout --` / `git reset --hard`.**
- **DB access only through** `DreamGenClone.DbQuery` (permanent dispatcher; do not create ad-hoc
  query projects). Never commit `dreamgenclone.dev.db`; only `dreamgenclone.snapshot.db` is tracked.
- **Razor edits** must follow `.github/instructions/razor-editing.instructions.md`.
- **Prompt-compiler changes** must follow
  `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`.
- **RunPod pod changes must be documented** in `helpers/runpod/pod-registry.json` and restart-proofed.

### 7.1 Constitution constraints that shape the design

From `.specify/memory/constitution.md` (these are binding, not advisory):

- **Local-first.** All runtime generation MUST execute on the local Windows machine, and core
  generation MUST NOT depend on cloud services; data, state and logs stay local by default. Local H3
  satisfies this by construction — a useful contrast with the cloud dispatch adapters.
- **Adapters are swappable.** Provider-specific concerns MUST live in adapter modules behind an
  abstraction, and .NET boundaries MUST be separate projects. So: the H3 client belongs in
  `DreamGenClone.Infrastructure` behind an interface in `DreamGenClone.Application`, never called
  directly from a page or the engine.
- **Fail fast, never degrade silently.** Invalid payloads MUST fail with explicit errors and MUST NOT
  degrade into untyped output — the same contract the RP no-fallback rules enforce.
- **Testable without live model calls.** Domain logic, prompt builders and schema validators MUST be
  testable without live model invocation. So the video prompt compiler and the graph builder must be
  unit-testable with no ComfyUI running.
- **SQLite for all persisted data** unless a feature spec explicitly says otherwise.
- **Serilog across all layers**, Information level minimum at persistence operations, adapter calls
  and error boundaries.

### 7.2 Verification expectations for "done"

- The resolver must refuse a video model with missing required configuration, **with an explicit
  message naming the missing setting** — and a test must pin that behaviour.
- The prompt compiler must be unit-tested against the H3 rules that are easy to get silently wrong:
  `non_diegetic_music: N/A`, `<d>` tag wrapping, stable speaker IDs, and **the absence of negative
  phrasing** (a ban is content, not a filter, at CFG 1).
- The graph builder must be testable without ComfyUI, and must not be able to emit a graph that drops
  the audio decode/mux.
- The output must be verified as **an mp4 carrying both a video stream and an audio stream** — not
  merely as "a file was produced". A silent clip must be a detectable failure.
- The output must be verified as **audible at a configured target loudness** (H3's native −24 to
  −30 LUFS is ~12 dB too quiet to ship as-is).
- Review/verification tooling must not rely on an editor preview: VS Code's Electron build has no AAC
  decoder and plays these files silently. Verify with a real player or by measurement.
- Any pacing/RP-engine-adjacent change follows the extra plan-and-confirm rule in §7.

---

## 8. Licensing and policy (must be decided before commercial use)

- **MiniMax H3 Community License** excludes **EU, UK, Republic of Korea and the United States** from
  the free grant, and requires prior written MiniMax authorization above **US$20M/yr** revenue.
- Comfy is the only official reseller of a **commercial local** license — from **$5,000/month**.
- The AfterMidnight / PinkCherry NSFW adapters are Apache-2.0 tagged but derive from the H3 base, so
  the base license still applies.
- **Local H3 has no runtime moderation** (the hosted H3-Context-IR moderation layer is not
  open-sourced). The local pipeline is uncensored-capable. The application's own content-policy
  surface is therefore the only gate, and it must be an explicit, configured decision.
- The harness keeps committed proof artifacts in an implied/softcore register; that convention should
  be carried into whatever the app stores and displays.

---

## 9. Open decisions for the planner

| # | Decision | Notes |
|---|---|---|
| **D-1** | **Which pipeline: extend the live scene-image path, or adopt the unused production-media substrate?** | The production-media subsystem (capability profiles/cells, intents, workloads, attempts, dispatch adapters) is designed for exactly this and has **zero rows**. The scene-image path is proven and live (747 images). Recommend: build on the scene-image path for delivery, and treat production-media convergence as a separate, later decision — but this must be decided explicitly, not by default. |
| **D-2** | Is video a new `ModelKind.Video`, or a `SceneImageModelFamily` value? | `ModelKind.Video` is cleaner; video is not an image. Either way the enum integer is persisted — append, never renumber. |
| **D-3** | Where does video attach in the domain? | Candidates: a scene beat moment, an approved frame, or a new video asset kind. The production-media intent layer already has `VideoCoveragePlanId` and video coverage concepts, but they are unused. |
| **D-4** | What supplies the reference image(s)? | The proven config needs the scene image plus a derived crop. Is the source an approved scene-image attempt, an asset, or an operator upload? |
| **D-5** | **Audio is confirmed wanted** (sounds, moans, spoken names). Remaining choice: how far to drive it. | H3 always generates audio — decide whether to keep it, and how. Levers: prompt-authored soundscape + dialogue (immediate), and `ref_audios` voice pinning (unused, and the strongest lever for consistent character voices). Also decide whether the app ever needs `Video` *without* audio. |
| **D-6** | Duration policy. | 5.2 s (~25 min) and 8.0 s (~50 min) are proven. Longer clips may need `ComfyUI-H3-Motion-Context` (multi-shot chaining) and would exceed the current 3600 s lease ceiling. |
| **D-7** | Lease strategy. | Raise `DurableJobLeaseSeconds` above 3600, or implement lease renewal/heartbeat? |
| **D-8** | Where is the NSFW capability gated? | The pipeline is uncensored-capable and the model has no runtime moderation, so the app-level policy surface is the only gate. Must be configured, not hardcoded. |
| **D-9** | 768p ceiling. | Accept 768p short edge, or budget an external upscale pass? The native 2K path is not open-sourced. |
| **D-10** | Backlog/spec structure. | B-150 is a `new` investigation item. Implementation likely warrants its own backlog item and spec rather than extending the investigation. The repo has a spec-kit workflow (`.specify/`) with a binding constitution, and the constitution requires that spec artifacts are the authoritative design source and that deviations are reconciled by **updating specs first**. |

---

## 10. Out of scope

- Wan 2.2 / any other video family (evaluated separately, in parallel — B-151 / B-113). Do not
  combine the tracks.
- Cloud video providers (the existing `RunPod` / `Together` dispatch adapters) except as the
  comparison point in D-1.
- **Separate audio post-production.** H3's audio is generated inside the same forward pass and needs
  no TTS/SFX layering stage. If a line still lands on the wrong speaker, the documented escape hatch
  is to generate that line externally and supply it as that speaker's `ref_audios` reference — but
  that is a fallback, not the plan.
- Retuning quality. The configuration catalog is deliberately kept complete
  (`helpers/h3-local-host/sweep-config.json`, 26 configurations, re-runnable by tag) because the
  best configuration is **not** considered settled.

---

## 11. Where to look first

1. `helpers/h3-local-host/` — the working harness; the closest thing to a reference implementation
   of the client, the request shape and the reference-image handling.
2. `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` — the closest existing
   analogue of the job handler that must be written.
3. `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs` — the existing ComfyUI HTTP client.
4. `DreamGenClone.Domain/ModelManager/RegisteredModel.cs` +
   `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — the model-registration and
   additive-migration pattern.
5. `DreamGenClone.Domain/RolePlay/CompiledMediaBrief.cs` — the video kinds and capabilities that
   already exist.
