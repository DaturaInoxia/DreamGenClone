# B-152 â€” Scene Video Studio: scoping

**Status:** scoping complete (2026-10-06); decisions **settled in the 2026-10-06 walkthrough**. This
document settles the open decisions and defines the work; **it is not an implementation** and changes
no code.
**Verified against the live host 2026-10-06:** `comfy.kenacwood.net` (the app's own configured
provider) reports ComfyUI **0.37.1**, torch 2.14.0+cu130, RTX 5080 16 GB, and exposes
`MiniMaxH3ReferenceToVideo`. The node's real input schema replaced several inferred values â€” see Â§6.3
and [`COMPILER-RESEARCH.md`](./COMPILER-RESEARCH.md) Â§16.
**Inputs:** [`INTEGRATION-HANDOFF.md`](../B-150-minimax-h3-local-investigation/INTEGRATION-HANDOFF.md)
(D-1â€¦D-10), [`RESEARCH-minimax-h3-16gb.md`](../B-150-minimax-h3-local-investigation/RESEARCH-minimax-h3-16gb.md)
(the proof), [`COMPILER-RESEARCH.md`](./COMPILER-RESEARCH.md) (the compiler rule set).
**Scope:** local MiniMax H3 video on the ComfyUI host, surfaced as a first-class **Video Studio**
page. Cloud video providers and the Wan 2.2 track are out of scope.

---

## 1. What the operator asked for

Restated as verifiable requirements. Every one of these is in scope for this item.

| # | Requirement |
|---|---|
| R1 | A **video generation page** that opens **independently** on its own (no session/interaction required) |
| R2 | **All inputs can be entered manually**, then a video generation job is started |
| R3 | It uses the **durable job** infrastructure; jobs can be **queued** (started later) **or started immediately** |
| R4 | It can be opened **from any image**, from the **review deck(s)**, and from the **image attempts lists** in Asset Manager and Production Studio â€” carrying that image in as the **starting reference image** |
| R5 | **Tabs** separate and group the options and settings, similar to the image composer and the image editor |
| R6 | **User intent inputs** (what the video should be) |
| R7 | A **prompt compiler** turns the intent into the model's prompt format â€” *deep research required* â†’ [COMPILER-RESEARCH.md](./COMPILER-RESEARCH.md) |
| R8 | The **same for audio**: user input compiled into the audio portion of the prompt |
| R9 | A tab of **all options, using prebuilt dropdowns** to choose from |
| R10 | **All settings proven in the B-150 sweep** are available |
| R11 | **LoRA selection and removal** (add / remove / reorder / strength) |
| R12 | Full ability to generate with **different reference images, different LoRAs, and setting option groups** |

---

## 2. Settled decisions

| # | Decision | Settled as | Rationale |
|---|---|---|---|
| **D-1** | Which pipeline | **Option C â€” hybrid seam** (confirmed 2026-10-06). Build the H3 client, prompt compiler and the full configuration/registration surface behind `Application` interfaces; deliver through the **live scene-image path**; leave the production-media substrate adoptable without rewriting the client. Because the composer **binds to a B-100 `SceneVideoCoveragePlan` when one is supplied** (see D-3), the deviation is **execution-only** â€” video *intent* still originates in B-100 as the roadmap requires. | The production-media substrate is the roadmap's canonical owner but has **0 rows in every table**. The scene-image path is proven in daily use (747 scene images, 1,543 asset images, 3,694 durable jobs) and the **B-100 video-intent lane is itself live** â€” 81 production plans, **37 video coverage plans**, 61 catalogue entries, 73 Moments â€” so only the execution path deviates. Recorded, not silent, per the roadmap's Change-Control rules. |
| **D-2** | `ModelKind.Video` vs a `SceneImageModelFamily` value | **New `ModelKind.Video` member, appended** (never renumbered â€” the integer is persisted and `ModelManagerTransfer` maps enum names by value). | Video is not an image; B-113 D5 independently forbids shoehorning video into the image family enum. |
| **D-3** | Where video attaches in the domain | A new **`SceneVideoRecord`** with an **optional origin** that may be (a) a **B-100 video coverage plan**, (b) a scene image, (c) an asset image, or (d) none â€” standalone. Settled 2026-10-06. | The operator's spec is an independent page that *may* be seeded from an image. Binding to a coverage plan when one exists keeps video intent in B-100's canonical hands and shrinks D-1 to execution-only, while the optional origin preserves the standalone case. `IMultimodalMediaCompiler` already carries `VideoCoveragePlanId`, so the linkage precedent exists. |
| **D-4** | What supplies the reference image(s) | **Operator-controlled, ordered, 1..N, each with an explicit role.** The origin seeds the default: a coverage plan's `VideoFirstFrame` typed reference, or the scene image plus a one-click MediaPipe-derived subject crop (`make-ref-crop.py`). Settled 2026-10-06. | Proven quality config is 2 references (scene + subject crop); `ref_images` autogrows to **9** (verified from the node). Critically, the node requires the prompt's **`<Picture i>` number to match the slot order** ("Use the same tags when prompting"), so reference order is *semantic* and must be operator-controlled and stably renumbered on reorder. |
| **D-5** | Audio | **Native audio kept and normalized.** Prompt-authored soundscape + diegetic/dialogue content; a required **loudness normalization** step to a configured target. `ref_audios` voice pinning is **deferred** (slice S7). Audio is always produced, so the honest production kind is `VideoWithAudio`. | H3 generates audio in the same forward pass; measured âˆ’24 to âˆ’30 LUFS is ~12 dB too quiet to ship. |
| **D-6** | Duration policy | **Trained range only: 124â€“362 frames (5.2â€“15.1 s)**, with **124 and 192 as the proven presets** and a UI warning above 192. Settled 2026-10-06 from the node's own schema. | The node accepts `min 5 / max 3600 / step 17` but documents the **trained range as ~124â€“362**; 56 is accepted yet untrained. Measured cost is ~0.28 min/frame (124 â‰ˆ 23â€“30 min; 192 â‰ˆ 53 min), so 362 â‰ˆ ~100 min is the practical ceiling. Multi-shot chaining remains out of scope. |
| **D-7** | Lease strategy | **Reuse the existing renewing executor.** Add the video lane to `SupportedLanes` and add a **video `AppFunction` default** with concurrency **1** and its own lease/poll/retry bounds. Settled 2026-10-06. | **Correction to the handoff:** lease renewal already exists â€” [`TextAnalysisDurableJobExecutor.RenewLeaseAsync`](../../../DreamGenClone.Web/Application/BackgroundJobs/TextAnalysisDurableJobExecutor.cs:174) renews while a job runs (with an `onLeaseLost` callback) and the worker reclaims expired leases. The `1â€“3600` bound appears **only** in `ValidateSceneBeatAnalyzerConfiguration`, so it is not a global job-duration ceiling. **No new mechanism is needed.** |
| **D-8** | NSFW gating | **None. No content-policy controls and no content limits on video** (operator constraint, 2026-10-06). Video model resolution must **not** demand a content policy, and nothing may block, flag, or cap video content. | Explicit operator requirement â€” do not re-introduce a gate. The local H3 pipeline has no runtime moderation, and the operator has chosen that the app imposes none either. The existing provider-level `ImageContentPolicy` stays **exactly as-is for images** ([`ImageContentPolicy.cs`](../../../DreamGenClone.Domain/ModelManager/ImageContentPolicy.cs), enforced at [`ModelManager.razor:1004`](../../../DreamGenClone.Web/Components/Pages/ModelManager.razor:1004)) and is **not** extended to video. |
| **D-9** | 768p ceiling | **Accept 768p short edge** (1344Ã—768). No upscale pass. | H3-Regenerate-2K is not open-sourced; an external upscaler is a separate item. |
| **D-10** | Backlog/spec structure | B-152 stays the **scoping** item and moves to `designed`. Implementation is tracked as its **own** backlog item (see Â§11). | The backlog entry itself says the scoping deliverable is a design, and implementation should be its own item. |

**Additional decisions settled in the 2026-10-06 walkthrough:**

| # | Decision | Settled as |
|---|---|---|
| **W-1** | Tab grouping | **Keep all seven tabs** as specced in Â§3.2 â€” Intent, References, Audio, Model & LoRAs, Sampling & Output, Prompt, Queue. Audio and the compiled prompt keep their own tabs. |
| **W-2** | Host (was X-1) | **One host. Closed.** `comfy.kenacwood.net` â†’ ComfyUI 0.37.1 on the RTX 5080 is the same instance the H3 proof used; the `192.168.0.16:8188` in B-113 is a **stale IP**. **No new provider row** â€” the video model hangs off the existing `Local ComfyUI (WOOD-GAME-MAIN 5080)` provider, and the client takes its base URL from that row (never a hardcoded IP, as the harness does). |

---

## 3. The Video Studio

### 3.1 Routes

Origin-explicit routes (verified against the existing editor precedents â€” the asset editor has no
session: `/assets/{assetId}/images/{id}/edit` at [ReviewDeck.razor:508-510](../../../DreamGenClone.Web/Components/Pages/ReviewDeck.razor:508)):

| Route | Use |
|---|---|
| `/roleplay/video-studio` | Standalone (R1) â€” no session, no reference. Landing shows recent videos + "New composition" |
| `/roleplay/video-studio/session/{sessionId}/{interactionId}` | Scoped to a session/interaction |
| `/roleplay/video-studio/session/{sessionId}/{interactionId}/{sourceImageId}` | Seeded from a scene image / production attempt (R4) |
| `/roleplay/video-studio/asset/{assetImageId}` | Seeded from an **Asset Manager image** (R4) â€” no session exists on this path |
| `/roleplay/video-studio/coverage/{coveragePlanId}` | Seeded from a **B-100 video coverage plan** (D-3 origin (a)) â€” resolves the plan's `VideoFirstFrame` typed reference |

Razor note: optional route segments need separate `@page` directives â€” follow
`.github/instructions/razor-editing.instructions.md`.

**Seed-image byte resolution:** one service resolves seed bytes for every origin kind. All image
files live under the one `SceneImageRoot` â€” scene images at `{sessionId}/â€¦`
([SceneImageStorageService.cs:28](../../../DreamGenClone.Infrastructure/Storage/SceneImageStorageService.cs:28)),
asset images at `assets/â€¦` ([SceneAssetStorageService.cs:42](../../../DreamGenClone.Infrastructure/Storage/SceneAssetStorageService.cs:42)),
identity images at `identity/â€¦` ([CharacterImageAssetStorageService.cs:50](../../../DreamGenClone.Infrastructure/Storage/CharacterImageAssetStorageService.cs:50))
â€” so one root-relative resolver serves every entry point.

### 3.2 Tabs (R5, R6, R9, R10, R11)

**Settled (W-1, 2026-10-06): seven tabs, as listed below.** Audio keeps its own tab (it is compiled
intent, but it is a distinct authoring surface), and the compiled prompt keeps its own tab (it is the
verification surface for R7).

The page is a **new page**, not a stage added to
[`SceneImageStudio.razor`](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor)
(5,438 lines already). Tab pattern to mirror: the stage stepper at
[SceneImageStudio.razor:948](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:948)
and the tab nav at [SceneImageStudio.razor:848](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:848).

| Tab | Contents | Requirement |
|---|---|---|
| **Intent** | Scene/action description, style declaration, shots + cut times, camera intent, on-screen text, content register | R6 |
| **References** | Ordered reference images with a per-image role (frame anchor vs subject definition), subject descriptions, add/remove/reorder, "derive subject crop" action, `ref_image_size` | R4, R12 |
| **Audio** | Soundscape (concrete object + action), non-diegetic music (`N/A` sentinel), dialogue lines + speakers + identity info, loudness target | R8 |
| **Model & LoRAs** | Video model selection, ordered **LoRA stack** with add / remove / reorder / strength / trigger token / capability | R11, R12 |
| **Sampling & Output** | Steps, sampler, scheduler, denoise, widthÃ—height, frame length, fps, seed, CFG (fixed by the distilled model) | R10 |
| **Prompt** | Compiled prompt preview, per-rule validation results, manual edit + recompile, compiler key/version | R7 |
| **Queue** | "Start now" vs "Stage for later", the job list with Start/Cancel, attempt history, output preview + playback | R3 |

All dropdown-backed fields (R9) come from **persisted configuration** â€” the closed vocabularies in
[COMPILER-RESEARCH.md](./COMPILER-RESEARCH.md) Â§5.4 (camera motion), Â§4 (retention markers),
Â§11 (frame length), and the provider/model/LoRA catalogs. Nothing is a free-text substitute for an
enum, and nothing is a code-only default.

#### 3.2.1 Intent tab

| Field | Control | Source / validation |
|---|---|---|
| Composition title | text | Stored on the record; shown in Queue history and the recent-videos list |
| Style declaration | textarea (1â€“2 sentences) | **Required** â€” lands *before* `[Shot 1]` (rule 10); hint text says so |
| Scene / action description | textarea with **live word counter** | Target band 350â€“500 words (rule 11); counter warns outside the band; dialogue-dense content may prioritise the spoken timeline (G-5) |
| Shots | list: `[Shot 1]` fixed, untimed; "Add shot" appends a row | Each added shot requires a cut time `MM:SS.mmm`; **strictly increasing** and within duration (rule 9); removing a shot renumbers |
| Camera intent (per shot) | 3 dropdowns: motion type Â· amplitude Â· speed | Closed vocabulary (COMPILER-RESEARCH Â§5.4); amplitude/speed include "(medium/normal â€” omitted)" |
| On-screen text entries | list of text inputs | Rendered verbatim in English double quotes (rule 13) |
| Content register | dropdown (clinical / explicit) | **Configured choice, not a compiler default** (G-8); the NSFW adapters are trained on explicit vocabulary |
| Task type | dropdown (`reference generation` / `keyframe completion` / â€¦) | Auto-derived from the reference roles (References tab); read-only display with override |

#### 3.2.2 References tab

Ordered list, 1..9 rows. **Row order is semantic** â€” it is the `<Picture i>` numbering (rule 27) â€” so
each row displays its live label (`<Picture 1>`, `<Picture 2>`, â€¦) and reordering renumbers.

| Per-row field | Control | Source / validation |
|---|---|---|
| Thumbnail + origin | image | Seed image, upload, or derived crop |
| Role | dropdown: **frame anchor** (standalone `<Picture N>`) / **subject definition** (cited inside `<Subject N>`) / **character sheet** (own `<Picture N>` entry naming its numbered panels; shots point at a panel) | Rule 7 + rule 30 â€” a definition-only image never gets a standalone line; a sheet always does |
| Subject description | textarea | Required when role = subject definition (feeds `subject_definitions`) |
| Retention marker | dropdown: `fully_preserved` / `partially_preserved` / `attribute_transfer` / `weak_reference` | Closed set (COMPILER-RESEARCH Â§4) |
| Actions | remove Â· move up Â· move down | Removal = the stack shrinks; labels renumber |

List-level: **"Add reference"** (upload / pick from the session gallery / pick from asset library),
**"Derive subject crop"** (one click â€” MediaPipe FaceMesh, the proven second reference;
[`make-ref-crop.py`](../../../helpers/h3-local-host/make-ref-crop.py) is the reference
implementation), and `ref_image_size` dropdown `match`/`max` with the *"several times slower"*
warning on `max`.

#### 3.2.3 Audio tab

| Field | Control | Source / validation |
|---|---|---|
| Soundscape | textarea, 1â€“4 sentences, live sentence counter | **Concrete object + physical action** wording (rule 21 + the measured sub-100 Hz lesson); placeholder shows a worked example; `N/A` toggle only for deliberate silence |
| Non-diegetic music | textarea, 1â€“3 sentences, **or the `N/A` sentinel toggle** | Instrumentation/tempo/rhythm/dynamics only â€” no mood words (rule 22); `N/A` is the documented token, never `None.` |
| Dialogue lines | list: speaker Â· line text Â· language Â· shot/time Â· delivery | Line text is **verbatim** inside `<d>[Language] â€¦</d>` (rule 14); never translated |
| Speakers | sub-list per speaker: identity info (type, age, gender, on/off-screen, pitch, timbre, rate, accent) | Required at first appearance (rule 17); **speaker IDs `(Sx)` are shown read-only, auto-assigned by vocal-event order** (rule 16) â€” the operator never types an ID |
| Loudness target | number (LUFS) | Defaults from the model config; drives the mandatory normalization (C-8) |

#### 3.2.4 Model & LoRAs tab

| Field | Control | Source / validation |
|---|---|---|
| Video model | dropdown | `ListSceneVideoModelsAsync` (a new video-filtered sibling of [`ModelResolutionService.cs:392`](../../../DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs:392)) â€” `ModelKind.Video` models only |
| Resolved config display | read-only panel | DiT / TE + `device=cpu` / video VAE / audio VAE / envelope, from `CapabilityQualificationsJson` via `MiniMaxH3ModelSettings` â€” the operator sees exactly what will run |
| Provider | read-only | From the model row (W-2 â€” the existing `Local ComfyUI (WOOD-GAME-MAIN 5080)` row) |
| LoRA stack | **the existing shared `SceneLoraPicker`** ([SceneImageStudio.razor:1837](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:1837)), `Family` = the H3 value | Ordered selections with strengths; trigger tokens shown from the new `SceneLoras.TriggerToken` column; empty selection = no loader node (R11 "removal") |
| Test connection | button | **Lightweight probe, never a generation** â€” a render is ~25 min; probe `/system_stats` + `/object_info/MiniMaxH3ReferenceToVideo` + artifact presence instead (new video path in `ProviderTestService`; today a video model would wrongly fall through to the chat health check, [`ProviderTestService.cs:111`](../../../DreamGenClone.Web/Application/ModelManager/ProviderTestService.cs:111)) |

#### 3.2.5 Sampling & Output tab

| Field | Control | Source / validation |
|---|---|---|
| Steps | number (default 40) | 50 measured as poor value; **audio converges later than the picture â€” speech/voice timbre is weakest at 8 steps and usable only from ~12 up** (Â§17.5), so low-step/turbo builds are blocked for speech-bearing clips |
| Sampler / scheduler | **locked display** `euler` / `beta` | Required by the AfterMidnight LoRAs â€” other schedulers break audio; shown read-only with the reason, not editable |
| CFG / denoise | **locked display** 1.0 / 1.0 | Distilled model â€” **no negative prompt exists**; read-only with the reason |
| Width Ã— height | dropdown | 1344Ã—768 (proven); portrait 768Ã—1344 offered but marked unproven |
| Frame length | dropdown: presets **124** / **192**, plus a trained-range stepper 124â€“362 | Warning above 192 (~0.28 min/frame â†’ ~100 min at 362); values outside `â‰¡ 5 (mod 17)` impossible by construction |
| FPS | locked display 24 | Node-fixed |
| Seed | number + randomize | Persisted on the attempt â€” seed variance is large, so reproducibility matters |

#### 3.2.6 Prompt tab

| Element | Behaviour |
|---|---|
| Compiled preview | Read-only render of the six sections, monospace, with the compiler key + version |
| Validation results | The **33 rules as a checklist** â€” pass/fail per rule with the offending section and text; failing rules block queueing (no silent submit) |
| Manual override | "Edit compiled prompt" unlocks a textarea; the edit is **persisted and flagged** on the attempt for provenance; "Recompile" returns to compiler output |
| Word count | Live counter against the 350â€“500 band |

#### 3.2.7 Queue tab

| Element | Behaviour |
|---|---|
| Start now / Stage for later | Two buttons â€” `Queued` vs `Staged` via the existing `requestedStatus` pattern ([`SceneImageService.EnqueueDurableAsync:1601`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageService.cs:1601)); a staged job gets the existing **Start** action ([RunTray.razor:69](../../../DreamGenClone.Web/Components/Shared/RunTray.razor:69) pattern) |
| Job list | This record's jobs â€” `ListRecentAsync` + client-side match on the dedupe key (the existing RunTray pattern, [RunTray.razor:162](../../../DreamGenClone.Web/Components/Shared/RunTray.razor:162)); status badge, attempts, lease, error, payload details |
| Attempt history | Per attempt: config snapshot, seed, LoRA stack, wall-clock, output |
| Output player | `<video controls>` from `/scene-videos/â€¦` â€” **seekable because StaticFiles serves Range requests natively** (the `/scene-images` mapping at [Program.cs:823-829](../../../DreamGenClone.Web/Program.cs:823) is the pattern; no custom endpoint needed) |
| Verification badges | Streams present (video + audio) Â· measured LUFS vs target â€” a silent clip is a **visible failure**, not a surprise |
| Recent videos | On the standalone landing route: recent records + "New composition" |

**Record lifecycle:** the `SceneVideoRecord` row is created on the first **Stage/Start** (with the
full config snapshot) â€” abandoned visits leave no junk rows; the Queue tab then operates on that
record.

### 3.3 Entry points (R4)

Add an "Open in Video Studio" action beside the existing "Open in image editor" affordances. Every
site below is verified today:

| Source | Location |
|---|---|
| Any scene image (gallery) | [SceneImageGallery.razor:214](../../../DreamGenClone.Web/Components/Pages/SceneImageGallery.razor:214) |
| Production attempt rows | [SceneImageStudio.razor:1554](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:1554), [:1628](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:1628), [:5268](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:5268) |
| **Review deck** (all route kinds) | [ReviewDeck.razor:1-2](../../../DreamGenClone.Web/Components/Pages/ReviewDeck.razor:1) routes; [GetEditUrl:508-510](../../../DreamGenClone.Web/Components/Pages/ReviewDeck.razor:508) |
| Production Studio composition attempts | [CompositionComposer.razor:404-417](../../../DreamGenClone.Web/Components/Pages/CompositionComposer.razor:404) |
| Asset Manager **image cards** | [AssetStudioView.razor:192-196](../../../DreamGenClone.Web/Components/Pages/AssetStudioView.razor:192) — the per-image `card-footer` action row (Edit · Review · Batch · Download · Load-into-generator) gains the **"Open in Video Studio"** button, gated on `image.Status == SceneAssetStatus.Complete` exactly like the existing footer actions (a pending/failed row has no picture to pass) |
| Asset Manager library/inspector rows | [AssetStudio.razor:249](../../../DreamGenClone.Web/Components/Pages/AssetStudio.razor:249) (selected-asset inspector), [:669](../../../DreamGenClone.Web/Components/Pages/AssetStudio.razor:669) (library rows) |

The seeded image must resolve to a real file the host can fetch â€” the `/scene-images` static mapping
(`Program.cs:829`) and `PersistenceOptions.SceneImageRoot` (`Program.cs:823`) are already the source of
truth for that.

---

## 4. Components

| # | Component | Where it plugs in |
|---|---|---|
| **C-1** | **Document the host artifacts** | The 4 model files + 4 LoRAs go into the `helpers/h3-local-host/` documentation. This is a **local Windows ComfyUI host, not a RunPod pod** â€” do **not** touch `helpers/runpod/pod-registry.json` or the deployment manifests (B-113 Â§3 is explicit). If H3 is ever deployed to a pod, the registry/restart-proof rules then apply. |
| **C-2** | **Video model registration** | `ModelKind.Video` ([ModelKind.cs](../../../DreamGenClone.Domain/ModelManager/ModelKind.cs)); the H3 artifacts + envelope in the model's existing `CapabilityQualificationsJson` column resolved by a `MiniMaxH3ModelSettings.Resolve` sibling of [`Krea2ModelSettings.cs:27`](../../../DreamGenClone.Web/Application/ModelManager/Krea2ModelSettings.cs:27) â€” **no `ALTER TABLE`, no new columns** (the `Krea2Refs`/`QwenImage21Refs` precedent); the H3 family value appended to `SceneImageModelFamily` + a `MiniMaxH3Ref2VA` dialect + `SceneImagePromptMetadata.IsCompatible` arm; and **LoRA selection reuses the existing `SceneLora` catalog + `SceneLoraPicker` + `ISceneLoraResolver`** (family-keyed, ordered, strength-carrying, UI-backed â€” [SceneLora.cs](../../../DreamGenClone.Domain/ModelManager/SceneLora.cs), [SceneImageStudio.razor:1837](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:1837)). The **only schema delta in S2** is the additive `SceneLoras.TriggerToken` column (Â§6.2). |
| **C-3** | **Video prompt compiler** | New deterministic, versioned compiler under `Web/Application/RolePlay/`, sibling to the scene-image prompt compilers. Full rule set in [COMPILER-RESEARCH.md](./COMPILER-RESEARCH.md). Must be unit-testable with no ComfyUI running. **Must bind `<Picture i>` / `<Video k>` / `<Audio j>` to the node's reference slot order** â€” the node documents "use the same tags when prompting", so reference order is semantic and reordering must renumber the labels. |
| **C-4** | **H3 video client** | `DreamGenClone.Infrastructure` behind an interface in `DreamGenClone.Application`, modelled on [`ComfyUIImageClient.cs:20`](../../../DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs:20) (`GenerateAsync:516`, `GenerateWithReferencesAsync:808`). Same ComfyUI HTTP cycle (upload â†’ `/prompt` â†’ `/history` â†’ `/view`) â€” **no new provider protocol** (`ImageProtocol.ComfyUi` is sufficient). **Reuses the existing `Local ComfyUI (WOOD-GAME-MAIN 5080)` provider row (W-2); the base URL is resolved from that row, never hardcoded.** The reference implementation is [`run-h3-ref2va-proof.py`](../../../helpers/h3-local-host/run-h3-ref2va-proof.py) (`apply_overrides:108`, `upload_image:197`, `api:216`, `normalize_loudness:231`, `main:289`) â€” **but its hardcoded `COMFY` IP must not be copied**. |
| **C-5** | **Graph builder** | Turns config + references into the API-format graph. Must be testable without ComfyUI, and **must not be able to emit a graph that drops the audio decode/mux** (`VAEDecode` + `VAEDecodeAudio` + `CreateVideo`). Also validated: `vae` and `audio_vae` are **optional inputs** on the node whose absence *silently degrades references to text-encoder-only conditioning* â€” so the builder must refuse a graph that binds reference images without a video VAE, or reference audio without an audio VAE. |
| **C-6** | **Durable lane + handlers** | `DurableJobLane.VideoRender = 5` ([DurableBackgroundJob.cs:3-9](../../../DreamGenClone.Domain/Processing/DurableBackgroundJob.cs:3)); `BackgroundJobTypes` constants ([BackgroundJobTypes.cs:11-14](../../../DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs:11)); the lane added to [`TextAnalysisDurableWorker.SupportedLanes:9-15`](../../../DreamGenClone.Web/Application/BackgroundJobs/TextAnalysisDurableWorker.cs:9); handlers registered like [Program.cs:285-290](../../../DreamGenClone.Web/Program.cs:285) and [:432-433](../../../DreamGenClone.Web/Program.cs:432). Staged-vs-queued uses the existing pattern: [`SceneImageService.EnqueueDurableAsync:1601-1625`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageService.cs:1601) with `requestedStatus`, surfaced in [RunTray.razor:69](../../../DreamGenClone.Web/Components/Shared/RunTray.razor:69). |
| **C-7** | **Storage, serving, record** | mp4 storage beside scene images (`data/scene-videos`), served at `/scene-videos` **with HTTP Range support** (seeking a `<video>` element needs it), plus a `SceneVideos` record with stream/loudness verification fields (D-3). Pattern: `ISceneImageStorageService` / `SceneImageStorageService` and [SceneImageRecord.cs](../../../DreamGenClone.Domain/RolePlay/SceneImageRecord.cs). |
| **C-8** | **Audio post-process** | Mandatory loudness normalization to a **configured** target (the harness already does this: [`normalize_loudness:231`](../../../helpers/h3-local-host/run-h3-ref2va-proof.py:231)); plus the two acceptance checks in Â§7. |
| **C-9** | **Video Studio page** | New page + tabs (Â§3.2) and the entry-point actions (Â§3.3). |
| **C-10** | **Provenance and review** | Compiler key + version, config snapshot, seed, reference manifest, and measured audio stats stored with the record; review/playback in the Queue tab. |
| **C-11** | **Fail-fast resolver** | A video counterpart of [`ImageEditorModelResolver.cs:53-56`](../../../DreamGenClone.Web/Application/ModelManager/ImageEditorModelResolver.cs:53) â€” refuses a video model with any missing required setting, **naming the setting**. |
| **C-12** | **No content gate** (D-8) | **Deliberately not built.** Video imposes **no content-policy controls and no content limits**; video model resolution must not require a policy and must not block, flag or cap content. The existing image-only provider policy is untouched. This row exists so a future contributor does not "restore the missing gate". |
| **C-13** | **Model Manager surface** (found in the 2026-10-06 readiness audit) | The video block in [ModelManager.razor](../../../DreamGenClone.Web/Components/Pages/ModelManager.razor) parallel to the image gate at `:440` (the ModelKind dropdown at `:362` is enum-bound, so `Video` appears automatically); a video family/dialect validation parallel to `ValidateImagePromptMetadata:1600`; the function-default model picker filter at `:1448` extended for the video function; **`AppFunction.RolePlaySceneVideo`** appended ([AppFunction.cs](../../../DreamGenClone.Domain/ModelManager/AppFunction.cs)) + a `FunctionModelDefault` video validation method (concurrency 1â€“16, lease, poll, retry bounds â€” the `ValidateSceneBeatAnalyzerConfiguration` pattern); `ListSceneVideoModelsAsync` in `ModelResolutionService`; a **lightweight video test-connection probe** in `ProviderTestService` (never a generation â€” a render is ~25 min; today a video model would wrongly fall through to the chat health check at `:111`); the **`ModelManagerTransfer` ModelKind name array** ([ModelManagerTransfer.cs:148](../../../DreamGenClone.DbQuery/ModelManagerTransfer.cs:148) â€” `"Video"` must be appended or transfers mis-map); and a **DbQuery `h3-video-configure` seeding command** (the `local-comfyui-configure` / `b137-krea2-configure` precedent) that seeds the H3 model row + `CapabilityQualificationsJson` + the LoRA catalog rows. |
| **C-14** | **Loudness normalization execution** (found in the 2026-10-06 readiness audit) | The harness normalizes via an **ffmpeg binary** ([`normalize_loudness:231`](../../../helpers/h3-local-host/run-h3-ref2va-proof.py:231), `loudnorm=I=â€¦:TP=-1.5`, `-ar` pinned). The app is .NET and has **no audio path today**, so: normalization + LUFS measurement execute by shelling out to a **configured ffmpeg path** â€” a persisted, UI-backed setting that **fails fast when normalization is required and ffmpeg is not configured** (no silent skip, no guessed path). Pure-C# EBU R128 measurement is out of scope. |

---

## 5. What is reused, not rebuilt

| Need | Already served by |
|---|---|
| ComfyUI HTTP provider | `ImageProtocol.ComfyUi` on the existing `Local ComfyUI (WOOD-GAME-MAIN 5080)` `Providers` row â€” **already reaches the H3 host** (W-2); no new provider row |
| Submit â†’ poll â†’ fetch â†’ persist | `ComfyUIImageClient` (same shape, longer poll, mp4 instead of png) |
| Durable job infrastructure | `DurableBackgroundJob`, lane worker, lease/retry/poll options, startup recovery, stagedâ†’start â€” **including lease renewal**, which already exists (D-7) |
| Staged vs immediate start | `DurableBackgroundJobStatus.Staged` + the existing Start action |
| Per-function concurrency/lease config | `FunctionModelDefault.MaxConcurrentJobs` / `DurableJobLeaseSeconds` / `DurableJobPollIntervalMilliseconds`, already UI-backed |
| Model configuration UI | [ModelDetailsEditor.razor](../../../DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor) + [ModelManager.razor](../../../DreamGenClone.Web/Components/Pages/ModelManager.razor) |
| Local-first execution | The local ComfyUI host satisfies the constitution by construction |
| Audio measurement | [`tools/video-audio-inspection/`](../../../tools/video-audio-inspection/) |
| Reference-crop derivation | [`make-ref-crop.py`](../../../helpers/h3-local-host/make-ref-crop.py) (MediaPipe FaceMesh) |

---

## 6. Configuration surface

Every row is **persisted, UI-backed, and fails fast when missing** â€” no hardcoded runtime defaults,
no fallback branches (`.github/instructions/roleplay-engine-no-fallback.instructions.md`).

### 6.1 Model artifacts and graph

`VideoGraphKind` Â· `VideoDiffusionModel` Â· `VideoTextEncoder` + **device (`cpu` required)** Â·
`VideoVae` Â· `VideoAudioVae` Â· `VideoPromptDialect` Â· `VideoMaxReferenceImages` (**node cap 9**) Â·
`VideoMaxReferenceAudios` (**node cap 3**) Â· `VideoMaxReferenceVideos` (**node cap 3**).

Both VAEs are **required configuration even though the node marks them optional** â€” omitting them
does not fail loudly, it silently reduces references to text-encoder-only conditioning.

### 6.2 LoRA stack â€” the one structural change

Today `RegisteredModel` carries a **single** `ImageEditorLoraName` / `ImageEditorLoraStrength` pair
([RegisteredModel.cs:52,56](../../../DreamGenClone.Domain/ModelManager/RegisteredModel.cs:52)). H3
quality depends on stacking 2â€“3 LoRAs **in a defined order** (`sexytime@0.8` â†’ `realism@1.0` â†’
optional `facial@0.6`), where order is the model-chain order in the graph.

**Settled (2026-10-06 audit): reuse the existing scene-LoRA catalog â€” do not build a video LoRA
table.** The ordered, family-filtered, strength-carrying, UI-backed LoRA stack **already exists**:
the [`SceneLora`](../../../DreamGenClone.Domain/ModelManager/SceneLora.cs) catalog ("the storage spot
for â€¦ per family, for every other family's" LoRAs), the ordered
[`ResolvedSceneLora`](../../../DreamGenClone.Domain/ModelManager/ResolvedSceneLora.cs) record,
`ISceneLoraRepository`/`ISceneLoraResolver` ([Program.cs:235,284](../../../DreamGenClone.Web/Program.cs:235)),
and the shared **`SceneLoraPicker`** component ([SceneImageStudio.razor:1837](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor:1837),
`Family`-filtered, `Scope`-parameterized). Appending the H3 family value makes all of it serve video
with zero duplication â€” which the reuse-shared-components rule requires. "LoRA removal" (R11) is an
empty selection: the graph then emits no loader node, exactly as the harness's `--no-lora` does.

**The one schema delta:** the catalog has **no trigger-token field** (Id/FileName/DisplayName/Family/
Category/DefaultStrength/IsEnabled/Notes), and the fal realism LoRA needs its `r34l1sm` trigger
carried into the prompt. Add an **additive nullable `SceneLoras.TriggerToken` column** (the trigger is
a property of the LoRA, so the catalog is its home â€” not a per-render field), surfaced in the picker
row and consumed by the compiler. This replaces the earlier `RegisteredModelVideoLoras` child-table
proposal, which duplicated what the catalog already does.

### 6.3 Sampling and output

| Setting | Proven value |
|---|---|
| Steps | 40 (50 tested; poor value) |
| CFG | 1.0 â€” fixed by the distilled model; **no negative prompt exists** |
| Sampler / scheduler | `euler` / `beta` (required by the AfterMidnight LoRAs; other schedulers break audio) |
| Denoise | 1.0 |
| Width Ã— height | 1344Ã—768 |
| Frame length | **124â€“362 only** (trained range, 5.2â€“15.1 s). The node accepts `min 5 / max 3600 / step 17`, so 56 is *valid but untrained*. Presets 124 and 192; warn above 192 |
| FPS | 24 |
| `ref_image_size` | `match` (default) or `max` â€” `max` uses a 2048px short edge and is *several times slower*, because reference tokens ride every sampling step |
| Reference images | 2 proven (scene + subject crop); node accepts **0..9**; **order is semantic** (it is the `<Picture i>` numbering) |
| Reference video / audio | up to **3** each (`ref_videos`, `ref_video_audios`, `ref_audios`) â€” deferred (S7) |
| Seed | Persisted; seed variance is large, so reproducibility matters |

### 6.4 Audio

Audio enabled/kind (`Video` vs `VideoWithAudio`) Â· **target loudness (required)** Â· soundscape text Â·
non-diegetic music text (`N/A` sentinel) Â· dialogue lines + speakers + language tags Â· speaker-ID
assignment policy Â· reference-audio bindings (deferred) Â· audio mux/export Â· **the configured ffmpeg
path that executes normalization and LUFS measurement** (C-14 â€” fail fast when missing, never a
silent skip).

### 6.5 Job behaviour

Video lane concurrency (**1** on a single GPU), lease seconds, poll interval, retry counts, max
attempts â€” all per function in `FunctionModelDefault`, with the video `AppFunction` carrying its own
bounds (D-7). **Lease renewal is not new work** â€” the executor already renews while a job runs, so a
~100-minute trained-range render is protected without a ceiling change.

---

## 7. Verification expectations for "done"

| Check | How |
|---|---|
| Resolver refuses a video model with missing config, **naming the setting** | Unit test, pinning the message |
| Compiler obeys every rule | Unit tests for all **33** rules in [COMPILER-RESEARCH.md Â§13](./COMPILER-RESEARCH.md) â€” no ComfyUI required; the official worked example (Â§17.1) is the canonical golden fixture |
| Graph builder cannot drop audio decode/mux | Unit test on the emitted graph, asserting all three nodes and their wiring |
| Graph builder cannot bind references without their VAE | Unit test: reference images require `vae`, reference audio requires `audio_vae` â€” the node marks both optional, so omission degrades silently instead of failing |
| Frame length validated **before** submission | Unit test over the valid/invalid lengths, including the untrained-but-accepted values (e.g. 56) and the trained-range bounds (124â€“362) |
| `<Picture i>` / `<Video k>` / `<Audio j>` match the node's slot order | Unit test: reordering references renumbers the labels consistently |
| Stored asset carries **both** a video and an audio stream | Automated check via [`tools/video-audio-inspection/`](../../../tools/video-audio-inspection/); a silent clip must be a **detectable failure**, not a review-time surprise |
| Stored audio is at the configured loudness | Measured integrated LUFS within the configured target band |
| Real generation works end-to-end | **Acceptance gate, not a unit test** (~25 min/clip): one 5.2 s clip from a scene image + subject crop |
| Review playback works | Verify in a **real player or by measurement** â€” VS Code's Electron build has no AAC decoder and plays these files silently |

---

## 8. Delivery slices

| Slice | Content | Exit |
|---|---|---|
| **S1** | H3 client spike: one injected service, (prompt, references, settings) â†’ mp4 bytes. No UI, no job, no assets. | Unit tests green with no ComfyUI; **one clip produced against the live provider** (operator-run test host call â€” graph conversion, CPU text-encoder offload, and audio mux proven) |
| **S2** | Model registration: `ModelKind.Video` + H3 family/dialect values, `MiniMaxH3Refs` + `MiniMaxH3ModelSettings` resolver fail-fast, `SceneLoras.TriggerToken` column, **Model Manager video block + `RolePlaySceneVideo` function default + its validation**, `ListSceneVideoModelsAsync`, video test-connection probe, `ModelManagerTransfer` name array, **DbQuery `h3-video-configure` seeding command** | A video model is configured as data end-to-end (seeded via DbQuery, visible in Model Manager, resolver refuses incomplete config naming the setting) |
| **S3** | Durable video lane + handler + staged/queued + `PersistenceOptions.SceneVideoRoot` + `/scene-videos` static mapping + `SceneVideos` table/repository + queue listing | A job can be staged, started, cancelled, retried; output is served and seekable |
| **S4** | Prompt compiler + audio compiler with the full validation set and unit tests | Compiler passes all rule tests with no ComfyUI running |
| **S5** | Video Studio page: tabs (Â§3.2), dropdowns, entry-point actions, manual inputs | Operator can compose and queue a clip end-to-end from the UI |
| **S6** | Verification + review: stream/loudness checks (ffmpeg loudnorm path), provenance, playback | Acceptance gate passes on a real clip |
| **S7** *(optional)* | `ref_audios` voice pinning | Per-character voice consistency proven |

S1â€“S3 produce a working, configurable video producer; S4â€“S6 make it usable; S7 is independent.

---

## 9. Constraints and compliance

- **No fallbacks:** configured values only, fail fast with explicit diagnostics, every behaviour
  control UI-backed (`.github/instructions/roleplay-engine-no-fallback.instructions.md`).
- **No content-policy controls or limits on video** (D-8, operator constraint). Do not add a video
  content gate; do not extend the image-only provider policy to video.
- **RP engine plan-and-confirm:** the Web `Application/RolePlay/**` files this touches are RP-engine
  files. The approved plan is the approval unit â€” this document is that plan's input, and
  implementation must be confirmed before code changes.
- **Adapters in `Infrastructure` behind an `Application` interface** (constitution); the composer and
  the engine must never call the client directly.
- **Testable without live model calls** (constitution) â€” the compiler, graph builder and validators.
- **All tests green**; never `git restore` / `git checkout --` / `git reset --hard`.
- **DB access only through `DreamGenClone.DbQuery`**; never commit `dreamgenclone.dev.db`.
- **Razor edits** follow `.github/instructions/razor-editing.instructions.md`.
- **Local host, not a pod:** no `pod-registry.json` / deployment-manifest changes for the H3 model
  files (B-113 Â§3), unless H3 is deployed to RunPod.

---

## 10. Risks and open items

| # | Risk / open item | Note |
|---|---|---|
| **X-1** | ~~Host identity is inconsistent in the sources~~ **CLOSED 2026-10-06.** | One host. `comfy.kenacwood.net` (the app's configured provider) reports ComfyUI **0.37.1**, torch 2.14.0+cu130, RTX 5080 16 GB â€” the same instance the H3 proof used at `192.168.0.11:8188`. The `192.168.0.16:8188` in B-113 is a stale IP. **No new provider row needed**; resolve the base URL from the provider row and never hardcode an IP. |
| **X-2** | ~~Lease ceiling~~ **DOWNGRADED 2026-10-06.** | Lease **renewal already exists** ([`TextAnalysisDurableJobExecutor.RenewLeaseAsync`](../../../DreamGenClone.Web/Application/BackgroundJobs/TextAnalysisDurableJobExecutor.cs:174)), and the `1â€“3600` bound is analyzer-specific, not global. No ceiling fix is required â€” only the video lane registration and its own function-default bounds (D-7, W-2). |
| **X-3** | Roadmap ownership deviation (D-1) | **Reduced to execution-only.** The roadmap assigns video execution to the production-media substrate and requires compilation from B-100 records; since the composer binds to a `SceneVideoCoveragePlan` **(37 live rows)**, intent still originates in B-100. Only the execution path deviates. Convergence stays a later decision. |
| **X-8** | Reference VAE omission degrades silently | `vae`/`audio_vae` are optional node inputs; without them references condition the text encoder only. Must be an explicit graph-builder validation, not a hope. |
| **X-4** | Multi-shot is unproven | Every proven run was a single continuous `[Shot 1]` (COMPILER-RESEARCH G-7). The Intent tab must not encourage cuts until proven. |
| **X-5** | Duration/size growth | Longer clips (15 s â‰ˆ 35â€“40 min at 20 steps) and higher resolution collide with the 3600 s ceiling and single-GPU lane. |
| **X-6** | B-113 overlaps | B-113 covers the **Wan 2.2** track (I2V/FLF2V) and reached different decisions about model family. Keep the tracks separate, but reconcile the shared pieces (video model registration, storage, lane) so two video paths do not appear at once. |
| **X-7** | Reference quality bounds output | The proof's weak spot is both faces in frame; that is bounded by the input image, not by settings (RESEARCH Â§"Honest limits"). The References tab should say so where the operator picks references. |

---

## 11. Backlog / spec structure (D-10)

- **B-152** â†’ state `designed`, linking this document. It remains the scoping item.
- **New implementation item** (proposed next free number) for the Video Studio, referencing the
  slices in Â§8 â€” so the scoping record stays readable as a decision log rather than becoming a task
  list.
- The compiler rule set ([COMPILER-RESEARCH.md](./COMPILER-RESEARCH.md)) is the authoritative input
  for any future compiler change; deviations are reconciled by updating it **first** (constitution).

---

## 12. Where to look first

1. [`COMPILER-RESEARCH.md`](./COMPILER-RESEARCH.md) â€” the compiler contract and validation rules.
2. [`INTEGRATION-HANDOFF.md`](../B-150-minimax-h3-local-investigation/INTEGRATION-HANDOFF.md) â€” proven
   stack, component list, configuration surface.
3. [`run-h3-ref2va-proof.py`](../../../helpers/h3-local-host/run-h3-ref2va-proof.py) â€” the working
   reference implementation of the client and graph overrides.
4. [`SceneImageRenderingJobHandler.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)
   â€” the closest analogue of the handler to write.
5. [`ComfyUIImageClient.cs`](../../../DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs) â€” the
   existing ComfyUI HTTP client.
6. [`RegisteredModel.cs`](../../../DreamGenClone.Domain/ModelManager/RegisteredModel.cs) +
   [`SqlitePersistence.cs`](../../../DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs) â€”
   registration and additive-migration pattern.
