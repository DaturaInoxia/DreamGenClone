# External Research Findings — Consistent Visual Production

**Date:** 2026-09-05
**Status:** Evidence base. Normative for B-111 phases.
**Method:** Primary sources only — peer-reviewed / technical-report papers and first-party
provider documentation. No blog posts, no community folklore, no inference from this repo's own
prior decisions.

> **How to use this document.** Every B-111 phase requirement traces to a finding here. If a future
> change contradicts a finding, either cite a newer primary source and update this document, or do
> not make the change. "It looked better in one render" is not evidence.

---

## Sources

| Ref | Source | Type |
|---|---|---|
| S1 | IP-Adapter: Text Compatible Image Prompt Adapter for Text-to-Image Diffusion Models — arXiv:2308.06721 | Paper |
| S2 | InstantID: Zero-shot Identity-Preserving Generation in Seconds — arXiv:2401.07519 | Paper |
| S3 | PuLID: Pure and Lightning ID Customization via Contrastive Alignment — arXiv:2404.16022 (NeurIPS 2024) | Paper |
| S4 | DreamBooth: Fine Tuning Text-to-Image Diffusion Models for Subject-Driven Generation — arXiv:2208.12242 (CVPR 2023) | Paper |
| S5 | UniPortrait: Unified Framework for Identity-Preserving Single- and Multi-Human Personalization — arXiv:2408.05939 | Paper |
| S6 | OMG: Occlusion-friendly Personalized Multi-concept Generation — arXiv:2403.10983 (ECCV 2024) | Paper |
| S7 | ConsiStory: Training-Free Consistent Text-to-Image Generation — arXiv:2402.03286 (SIGGRAPH 2024 TOG) | Paper |
| S8 | StoryDiffusion: Consistent Self-Attention for Long-Range Image and Video Generation — arXiv:2405.01434 | Paper |
| S9 | ControlNet: Adding Conditional Control to Text-to-Image Diffusion Models — arXiv:2302.05543 | Paper |
| S10 | FLUX.1 Kontext: Flow Matching for In-Context Image Generation and Editing — arXiv:2506.15742 | Paper |
| S11 | Qwen-Image Technical Report — arXiv:2508.02324 | Paper |
| S12 | IDM-VTON: Improving Diffusion Models for Authentic Virtual Try-on in the Wild — arXiv:2403.05139 (ECCV 2024) | Paper |
| S13 | ArcFace: Additive Angular Margin Loss for Deep Face Recognition — arXiv:1801.07698 (TPAMI) | Paper |
| S14 | RunPod — Endpoint settings (`/serverless/endpoints/endpoint-configurations`) | Vendor docs |
| S15 | RunPod — Serverless overview, cold starts (`/serverless/overview`) | Vendor docs |
| S16 | RunPod — Send API requests (`/serverless/endpoints/send-requests`) | Vendor docs |
| S17 | RunPod — Optimize your endpoints (`/serverless/development/optimization`) | Vendor docs |
| S18 | RunPod — Cached models (`/serverless/endpoints/model-caching`) | Vendor docs |
| S19 | RunPod — Build a concurrent handler (`/serverless/workers/concurrent-handler`) | Vendor docs |

---

## F1. There is no single identity technique. There is a technique *portfolio*, and the choice is per-shot.

The literature does not converge on one winner. It converges on a set of methods with **different
and partly opposing trade-offs**, which is why locking the app into one is an architectural error.

| Method | Mechanism | Strength | Documented cost |
|---|---|---|---|
| Text description only | Prompt tokens | Free, universal, no infra, composes with everything | Statistical resemblance only; no instance-level identity |
| **IP-Adapter** (S1) | Decoupled cross-attention for image features, 22M params, base model frozen | Lightweight; **generalises to other fine-tunes of the same base**; **composes with existing controllable tools**; works alongside the text prompt | It is a general *image* prompt, not a face-specialised one — identity fidelity is moderate; face-specialised variants trade editability for fidelity |
| **InstantID** (S2) | IdentityNet: **strong semantic + weak spatial** conditioning, fusing a face image *and a landmark image* with the text prompt | Single reference image, single forward pass, high face fidelity, plug-in for SD1.5/SDXL | The landmark/spatial condition constrains pose and expression — it pulls the render toward the reference's facial geometry; requires face detection to work at all |
| **PuLID** (S3) | Lightning T2I branch + contrastive alignment loss + accurate ID loss | Superior ID fidelity **and** editability; explicitly **minimises disruption to the original model** — background, lighting, composition and style before/after ID insertion are kept as consistent as possible | Extra branch/infra; must be installed and qualified per host |
| **LoRA / DreamBooth** (S4) | Binds a unique identifier to the subject by fine-tuning, with class-specific prior-preservation loss | Highest fidelity; covers **whole subject** (body, not just face); renders the subject in novel poses, views and lighting not present in the references | Per-subject training time and storage; needs a curated dataset; prior-preservation loss is required specifically to prevent language drift and overfitting |
| **In-context edit** (S10, S11) | Sequence concatenation of image + text context (Kontext); dual semantic+reconstructive encoding (Qwen-Image) | Preserves everything not addressed; **designed for multi-turn stability** | Requires an existing image — cannot originate a composition |
| **Shared-attention** (S7, S8) | Share internal activations / self-attention **across images generated together** | Zero training, zero per-character setup, extends to multi-subject and to common objects | **Only works on a set generated as a batch** — not applicable to a one-off request |

**Normative consequence.** The app must model identity as a *strategy* selected per request and
qualified per (model + endpoint), exactly as it already models `ImageProtocol`. Any design that
hard-codes one technique, or silently substitutes one for another, contradicts the evidence.

### F1.1 The PuLID property is the one this app most needs, and it is currently unused

S3's distinguishing claim is not just fidelity — it is that **the image's background, lighting,
composition and style are preserved across ID insertion**. That is precisely the requirement for a
staged pipeline where composition is approved first and identity is applied second. The enum value
exists in this codebase and has never been qualified or exposed.

### F1.2 Shared-attention makes batching a *quality* mechanism, not just a cost mechanism

S7 and S8 both achieve consistency by sharing internal state **between images produced in the same
generation batch**. S7 additionally reports strategies to preserve layout diversity while holding
the subject constant, and extends naturally to multi-subject.

This is a direct architectural finding: **a "production run" that renders all shots of a scene
together can access consistency mechanisms that a sequence of independent one-off requests
structurally cannot.** The batching design demanded by the serverless cold-start constraint and the
batching design demanded by visual consistency are the *same* design. They must not be built twice.

---

## F2. Multi-character identity bleed is solved by routing, not by strength tuning

Both primary sources on multi-subject personalisation solve identity bleed **spatially**, not by
weighting.

- **UniPortrait (S5)** uses two modules: an ID *embedding* module that extracts decoupled editable
  facial features per ID, and an **ID routing module that adaptively distributes those embeddings to
  their respective regions within the synthesised image**. It reports universal compatibility with
  existing generative control tools.
- **OMG (S6)** uses two-stage sampling: stage 1 produces **layout plus visual-comprehension
  information for handling occlusion**; stage 2 performs **noise blending** per concept using that
  layout. It reports that **the denoising timestep at which blending starts is the key control** for
  the identity-vs-layout balance. It composes with single-concept LoRA and InstantID without extra
  tuning.

**Normative consequences.**
1. Per-character region assignment is required for multi-character identity. A single global
   reference strength cannot fix bleed.
2. Regions should be **derived from an actual layout** (a generated composition, or a segmentation of
   it), not hand-entered boxes. A fixed hand-specified mask is a degraded approximation of what both
   papers do.
3. This is independent evidence for a **Composition-first, Identity-second** pipeline: stage 1
   establishes layout and occlusion, stage 2 applies per-region identity. That is the same shape as
   S6's two-stage sampler.
4. Occlusion is a named failure mode, not an edge case. Overlapping/embracing characters are the
   hard case and must be in the acceptance set.

---

## F3. Location consistency has no adapter. It requires three mechanisms composed.

There is no "location adapter" analogous to a face adapter in the primary literature. What exists is
the general machinery, which must be composed deliberately:

1. **Reference conditioning** — a canonical plate of the location used as an image prompt (S1) or as
   in-context reference (S10, S11).
2. **Geometry lock** — ControlNet (S9) adds spatial conditioning from **depth, segmentation, normals
   or edges**, supports **single or multiple conditions**, works **with or without prompts**, and
   trains robustly on both small (<50k) and large (>1m) datasets. This is the mechanism that holds
   architecture, camera geometry and set layout constant across shots and across POVs of one moment.
3. **A frozen text block** — a deterministic, versioned set of invariant facts about the location,
   injected identically in every prompt for that location.

**Normative consequences.**
- A location asset is **not one picture**. Its minimum viable definition is *plate + derived control
  map(s) + frozen text block*, all versioned together.
- Multiple POVs of one moment must derive their control maps from **one shared canonical source**,
  not from per-shot re-invention.
- ControlNet controls **geometry**; identity adapters control **appearance**. They compose (S1
  explicitly notes compatibility with existing controllable tools; S5 and S6 report the same). Do
  not attempt to fix blocking with identity strength, and do not attempt to fix identity with prompt
  wording.

---

## F4. Wardrobe consistency needs image *and* text, and is two different problems

S12 (IDM-VTON) is the strongest primary evidence on garment fidelity. Its findings:

- Prior work adapted exemplar-based inpainting and **failed to preserve garment identity**.
- The winning recipe is **dual encoding of the garment**: high-level semantics from a visual encoder
  fused into **cross-attention**, plus low-level features from a parallel UNet fused into
  **self-attention**.
- Critically for a prompt-driven app: they **provide detailed textual prompts for both the garment
  and the person** to improve authenticity.
- A per-pair customisation step significantly improves fidelity.

**Normative consequences.**
- Separate two problems that this repo currently conflates:
  **(a) same outfit across shots** — solved like a location (frozen text block + reference
  conditioning);
  **(b) putting a specific garment on a character** — the try-on problem, which needs the dual
  encoding above and is a distinct capability.
- **A garment reference image alone under-performs.** A wardrobe asset must carry a mandatory
  descriptive text block alongside the image. Any design that stores only a picture is known-weak.
- Today the repo stores wardrobe as a free-text clothing field on beat analysis, with a
  `SceneAssetType.Wardrobe` enum and no aggregate. Both halves of the evidence-based design are
  missing.

---

## F5. Editing beats regeneration for preservation — and multi-turn stability is the measured axis

- **S10 (FLUX.1 Kontext)** states the problem directly: current editing models **degrade in
  character consistency and stability across multiple turns**; Kontext reports improved preservation
  of objects and characters and **greater robustness in iterative workflows**. Its benchmark
  (KontextBench, 1026 pairs) has an explicit **character reference** category alongside local edit,
  global edit, style reference and text editing. It evaluates **single-turn quality and multi-turn
  consistency as separate axes**.
- **S11 (Qwen-Image)** feeds the source image to **both** a vision-language encoder (semantic) and a
  VAE encoder (reconstructive), so the editing module can **balance semantic consistency against
  visual fidelity** — and adds I2I reconstruction to the training mix specifically to improve editing
  consistency.

**Normative consequences.**
- Encode the rule: **if the composition is approved, edit; if the composition is wrong, regenerate.**
  Never repair composition with an edit; never repair identity with a re-roll.
- **Multi-turn consistency must be measured separately from single-turn quality.** An edit workbench
  that only ever shows the latest result hides the degradation this literature says is the dominant
  failure.
- A geometry-changing edit (pose, camera angle, head turn) necessarily invalidates a previously
  applied identity, because the identity was applied to different pixels/geometry. This is a physical
  consequence of the mechanism, not a policy preference — a `Cosmetic` vs `Geometry` change class is
  evidence-backed.

---

## F6. Validation metrics: measure identity, adherence and diversity *together* or the scorecard lies

The standard evaluation protocol across S2, S3, S4, S5 is a **triple**, not a single number.

| Axis | Metric | Source | Notes |
|---|---|---|---|
| Identity (faces) | Cosine similarity of face-recognition embeddings, ArcFace-class | S13, used as the ID metric by S2/S3/S5 | Must be computed on a **detected + aligned face crop**, not the whole image |
| Subject fidelity (non-face) | **DINO** and **CLIP-I** image similarity | S4 | S4 introduces this protocol; DINO is the more discriminative of the two for *instance*-level identity — CLIP-I readily forgives "a different person of the same type" |
| Prompt adherence | CLIP-T (text–image similarity) | S4 | The counterweight to identity |
| Diversity / layout | Variance across seeds; layout diversity | S7 | S7 explicitly designs *for* layout diversity under subject consistency |

**The central warning.** Identity fidelity and editability/prompt adherence move in **opposite
directions** as conditioning strength rises — this is the trade-off S3 claims to improve on and that
S2's spatial condition exhibits. A scorecard that reports only identity similarity will drive
conditioning strength upward until every render is effectively a re-pose of the reference. **Report
identity + prompt adherence + diversity as a triple, always.**

**Thresholds.** No paper's absolute threshold transfers. Thresholds must be **calibrated on this
project's own golden set**, per (model + strategy + strength), and recorded as data — not hard-coded.

**Directly relevant failure mode already observed in this repo:** the B-103 render where the second
character was "dropped/buried" as a distant shadowed figure. A DINO/CLIP-I subject check and a
face-detection presence check on the expected number of subjects would have caught that
automatically. Eyeballing did not.

---

## F7. Serverless cold start: the levers are documented, ranked, and mostly unused here

### F7.0 Cold start is a property of ONE provider class, not of the pipeline

The app already routes on three distinct image protocols
(`DreamGenClone.Domain/ModelManager/ImageProtocol.cs`), and they have **fundamentally different
cost, latency and capability profiles**. Treating them as one thing is a modelling error.

| Class | `ImageProtocol` | Latency profile | Cost profile | Capability ceiling |
|---|---|---|---|---|
| **Hosted inference API** | `OpenAiImages` (e.g. Together AI) | **No cold start.** Latency is request-time only; subject to provider rate limits and 429s | Per-image, no idle cost | **No ComfyUI graph.** No ControlNet, no IP-Adapter/PuLID/InstantID, no LoRA, no `batch_size` control, no arbitrary workflow. Provider content policy applies |
| **Dedicated ComfyUI pod** | `ComfyUi` | No cold start while running; provisioning/recovery is the failure mode | Billed while the pod exists, idle or not | Full ComfyUI graph — all techniques available |
| **Self-built ComfyUI serverless** | `ComfyUiServerless` | **Cold start on first request after idle**; idle timeout returns it to cold | Pay-per-second of execution + idle-timeout tail; near-zero when idle | Full ComfyUI graph — all techniques available |

**Normative consequences.**

1. **Warm-window / staged-run machinery applies only to `ComfyUiServerless`.** Hosted APIs need a
   different discipline entirely: concurrency ceilings, 429 backoff, and per-image cost accounting.
   Dedicated pods need liveness and provisioning discipline. One "Run" abstraction is correct, but
   its execution policy must be **per provider class**, and that policy must be data, not code.
2. **Provider class sets a hard capability ceiling on technique choice.** Every identity method in F1
   except *text-only* requires a ComfyUI graph or a provider-native reference feature. This means
   whole rows of the (strategy × model × endpoint) qualification matrix are **structurally
   impossible**, not merely unqualified — and the app must be able to say which is which.
3. **The obvious "route the one-off to an API while the serverless endpoint is cold" optimisation is
   only valid when the required strategy is supported on both.** Because the capability ceilings
   differ, an automatic reroute would silently downgrade identity conditioning to text-only. Under
   this repo's no-fallback rule that is forbidden: the app must **fail fast and offer the choice**,
   never substitute a weaker strategy to avoid a wait.
3a. **The correct resolution is a cross-class staged pipeline, not a reroute** (user decision,
   2026-09-05). A hosted API cannot apply identity, **but the image it produces can be edited
   afterwards on a ComfyUI endpoint to apply identity.** Therefore each *stage* of a production may
   legitimately target a different provider class:

   | Stage | Natural class | Why |
   |---|---|---|
   | Composition | Hosted API **or** ComfyUI | No cold start, cheap, fast iteration on framing; identity not needed yet |
   | Identity | ComfyUI (pod or serverless) | Requires a graph — reference conditioning or reference-edit |
   | Finish | ComfyUI (pod or serverless) | Requires a graph — source-image edit |

   This is **composition of capabilities, not substitution of one for another**, so it does not
   violate the no-fallback rule. It also directly minimises cold-start exposure: only the stages that
   genuinely need a graph must wait for a warm window. The per-stage target class must be persisted
   and visible, never inferred silently.
4. **Cost and consistency pull in opposite directions across classes.** Hosted APIs are cheapest for
   one-off, low-consistency work; self-built serverless is the only economical route to the
   high-consistency techniques (F1, F2, F3). Run planning must therefore group work by
   **(endpoint, class)** — see F7.7.9.
5. `ProductionDispatchAdapters` currently hard-codes `ImageProtocol.ComfyUiServerless` in three
   dispatch paths and separately hard-codes a `ProviderType.TogetherAI` + `OpenAiImages` check. That
   is provider-class knowledge baked into code rather than expressed as capability data.

Everything below in F7 concerns the **`ComfyUiServerless`** class specifically.

### F7.0.6 Model provisioning is from RunPod storage, not runtime registry pulls (user, 2026-09-05)

The app's self-built ComfyUI serverless workers **load models from RunPod storage** — a persistent
network volume and/or models baked into the worker image — **not** by pulling from an external model
registry at request time. Model/node **sourcing is flexible and provisioning-time only**: when (or
if) a **new model or a new serverless endpoint** is added, its weights/nodes are fetched from
**Civitai or any other suitable source** and placed onto RunPod storage. There is no fixed registry
and no runtime registry dependency.

**Normative consequences.**
1. **External-registry reachability (HF, Civitai) is a build-time concern only.** The Hugging Face
   TLS block on the dev machine (F7.0.5 note / repo env facts) does **not** affect the running
   pipeline, because workers never fetch weights at runtime. It only matters when provisioning a
   worker image or populating a volume — and Civitai is the source there.
2. **Cold start = container boot + model load from local/volume storage**, per F7.2's cached-model /
   baked-image / network-volume levers (S17/S18). This is the fast path RunPod documents; the app is
   already on the right side of the cold-start trade-off for model loading.
3. **P3's new identity worker image** (PuLID/FaceID/InstantID) provisions its models/nodes from
   Civitai + RunPod storage at build time, recorded in the RunPod registry per repo rules — no
   runtime HF/Civitai dependency, and the build step is where registry reachability is verified.
4. Adding/replacing a model on an endpoint is therefore a **provisioning** action (update the volume
   or image, record it) plus a Model Manager data row — consistent with O7 (data + qualification
   record, no caller code change).

### F7.1 Defaults and the shape of latency
- Defaults: **Active workers 0, Max workers 3, Idle timeout 5s, Execution timeout 600s, Job TTL 24h,
  FlashBoot enabled.** (S14)
- Response time splits into **delay time** (waiting for a worker — includes container image
  *initialization* and model **cold start** into GPU memory) and **execution time** (S17).
- **If cold start exceeds 7 minutes the worker is marked unhealthy**; extend with
  `RUNPOD_INIT_TIMEOUT` (seconds). (S17)

### F7.2 Ranked cold-start levers (S17)
1. **Cached models** — for Hugging Face-hosted models; reduces cold start to seconds, and **you are
   not billed for worker time while the model downloads**. Limit: **one cached model per endpoint**;
   all quantisation variants in a repo are downloaded. (S17, S18)
2. **Bake models into the image** — for private models; loads from local NVMe. (S17)
3. **Network-volume cache** — flexible but adds latency and pins the endpoint to one data centre.
   (S17, S14)
4. **Active workers > 0 — eliminates cold starts entirely**, billed continuously including idle.
   Sizing formula given: `active workers = (requests_per_min × request_duration_seconds) / 60`. (S17)
5. **FlashBoot** — retains worker state after spin-down for faster revival; **most effective on
   endpoints with consistent traffic where workers cycle between active and idle**. (S14)

### F7.3 Scaling and queueing controls (S14)
- **Auto-scaling type — Queue delay**: add workers when requests wait longer than a threshold
  (default 4s). **Request count**: `ceil((requestsInQueue + requestsInProgress) / scalerValue)`;
  scaler value 1 = maximum responsiveness; recommended for frequent short requests.
- **Max workers** should be set ~20% above expected peak concurrency (S17).
- **GPU priority**: up to three GPU types in priority order; for endpoints with ≥5 workers RunPod
  distributes across them, reducing throttling. Directly relevant to this project's recurring
  "no GPU available" problem.
- **Idle timeout**: how long a worker stays warm after a request. **Billed during idle, but the
  worker stays warm.** Default 5s.

### F7.4 Job control surface the app is not using (S16)
| Operation | Purpose |
|---|---|
| `/run` | Async submit; **results retained 30 minutes** |
| `/runsync` | Sync; results retained 1 minute (5 max) |
| `/status` | Status + execution details + result |
| `/stream` | Incremental results |
| `/cancel` | Stop a queued or running job |
| `/retry` | Requeue a failed/timed-out job with the same job ID and input |
| `/purge-queue` | Clear all pending jobs |
| `/health` | **Endpoint status including worker and job statistics** |

- **Per-request execution policy**: `executionTimeout` (min 5s, max 7 days), `ttl` (min 10s, max 7
  days), and **`lowPriority` — "when true, job won't trigger worker scaling"**.
- **`ttl` is a hard limit measured from submission and covers queue time.** If it expires mid-run the
  job is deleted and `/status` returns 404.
- **Webhooks**: a `webhook` URL per request, called on completion; retried up to 2 more times with a
  10-second delay on non-200.
- Rate limits: `/run` 1000 per 10s (200 concurrent); `/status` 2000 per 10s (400 concurrent);
  `/cancel` 100 per 10s; `/purge-queue` **2 per 10s**.

### F7.5 Worker-side concurrency (S19)
A single worker can process multiple jobs concurrently via an async handler plus a
`concurrency_modifier`. Individual jobs can be expired/timed-out/cancelled **without affecting
sibling jobs running on the same worker**; the handler catches `asyncio.CancelledError` to release
resources.

### F7.6 Operational hazard: silent idle scale-down (S14)
> After **3 days** with no requests, the endpoint's max workers is reduced to **2** and an email is
> sent. After **7 days** with no requests, max workers is set to **0**. Once scaled down this way it
> **stays at the reduced value until raised manually**. Any incoming request resets the timer.

An endpoint left unused for a week becomes permanently max-workers-0 until a human intervenes.
Requests then queue indefinitely with no obvious error. This must be surfaced in the app, not
rediscovered as a mystery outage.

### F7.7 Normative consequences for this codebase
The current client submits one job, sleeps a fixed 5 seconds in a loop, and gives up at a timeout.
It has no warm-up, no keep-alive, no batching, no cancel, no health read, no priority control, and
hard-codes `batch_size = 1`. Against the documented surface:

1. A **Run** (staged set of requests) maps exactly onto `/run` + queue + drain. Queueing on RunPod is
   the intended mechanism — the app does not need to invent its own throttle.
2. **Warm-up before a run** is achievable two ways, both data-driven: a cheap sentinel job, or
   raising active workers to 1 for the run's duration then back to 0. The policy must be persisted
   per endpoint, not hard-coded.
3. **Priority lane** is a documented per-request flag: run jobs carry `lowPriority: true`, the
   interactive one-off does not — so the one-off gets the warm worker and the run does not fight it
   for scaling.
4. `executionTimeout` and `ttl` must be **per request**, derived from the work, and `ttl` must cover
   expected queue time (a staged run's tail can queue for a long time — a default TTL will delete it).
5. **`/health` is the honest source** for cold/warming/warm state in the UI. Guessing is unnecessary.
6. **Abort** requires `/cancel` per job plus `/purge-queue` for the run (respecting the 2-per-10s
   limit on purge).
7. **Webhooks remove the fixed 5s poll**, cutting both latency and `/status` request volume.
8. **`batch_size` > 1 inside one job** is an independent, cheaper axis: N candidates for one cold
   start and one model load. It is also the axis that unlocks the F1.2 shared-attention consistency
   family.
9. Cold-start cost is per **endpoint**, so the number of distinct endpoints a single run touches is
   itself a first-class cost. A run that needs generation + edit + identity on three separate
   endpoints pays three cold starts. **Run planning should group by endpoint**, and by provider class
   (F7.0) — work targeting a hosted API carries no cold-start cost and should not be forced to wait
   behind a warm window.

All facts in F7.1–F7.6 are from S14–S19 (RunPod first-party docs).

---

## F8. Cross-cutting conclusions

1. **Consistency is a property of a set, not of an image.** Every mechanism that actually works —
   shared attention (S7/S8), shared control maps (S9), shared references (S1/S2/S3), shared LoRA
   (S4) — is about holding something constant across a *group*. The unit of work must therefore be a
   set, not a single render.
2. **The cost architecture and the quality architecture are the same architecture.** Batching for
   cold-start economics and batching for visual consistency are one mechanism (F1.2, F7.7).
3. **Geometry and appearance are separate control channels** and must never be traded off against
   each other (F3).
4. **Every technique must be qualified per (model + endpoint + strength) on this project's own golden
   set** — no paper threshold transfers, and no technique is universally best (F1, F6).
5. **Reference assets are the precondition for all of it.** Every method above except pure text
   requires a canonical reference to exist first. A workflow to *create* references for wholly
   fictional characters, outfits and locations is not an optional feature — it is the floor.
6. **Text never stops mattering.** IP-Adapter is explicitly designed to work *with* the text prompt
   (S1); IDM-VTON requires garment *and* person text prompts (S12); ControlNet works with or without
   prompts but composes with them (S9). Reference conditioning augments the text block; it does not
   replace it.
7. **Provider class is a capability boundary, not just a transport choice** (F7.0). Which
   consistency techniques are even reachable depends on it, so the technique portfolio (F1) and the
   endpoint inventory must be modelled together — and a cheaper or warmer endpoint must never be
   allowed to silently reduce the identity strategy actually applied. Where classes differ, compose
   them across stages (F7.0.3a) rather than substituting one for another.
8. **Content capability is a property of the model, not a mode of the pipeline** (user decision,
   2026-09-05). There is **one** production flow and **one** golden set, and **every golden-set case
   is explicit**. A model either supports the content or it does not. There is no parallel "safe
   path", no SFW variant of a case, and no separate acceptance set — a second flow doubles the
   surface being validated while measuring the case that is not actually used.

   **A refusal is a measurement, not an error to be worked around.** When a model refuses,
   sanitises, or is filtered:
   - the attempt is **recorded as a failure against that (model, endpoint) cell**, with the refusal
     mode captured;
   - the failure is **surfaced clearly to the user**;
   - the user then **manually picks a different model and retries**. The app performs **no automatic
     model substitution and no ordered-list advance** — it never selects a model on the user's behalf
     (user decision 2026-09-05). Model choice stays entirely with the user.

   **Why this is even stricter than the no-fallback rule:** the app never picks a model for you — not
   from a default, not from a list. Detection is still required so a refusal is a *visible* failure,
   never a silent success.

   **Detection is the hard part, and must be designed rather than assumed.** Three distinct refusal
   modes exist and only the first is easy:
   | Mode | Signal | Detection |
   |---|---|---|
   | Explicit refusal / policy error | HTTP error or error payload | Trivial — provider tells you |
   | Empty or absent output | No image returned | Trivial |
   | **Silent sanitisation** — an image *is* returned, but clothed, cropped, blurred or otherwise altered | None | **Requires content inspection of the output.** This is the dangerous mode: it looks like success, it silently corrupts the scorecard, and it is invisible without a check |

   The third mode is why refusal handling belongs to the scorer as well as the transport (see
   FR-C6-08). Deprecated by this decision: the deterministic SFW clamp
   (`ImageContentPolicy.SfwOnly` → model-family suffix appended in `SceneImageRenderingJobHandler`)
   is exactly the second flow this principle forbids and is removed, not reconciled.
9. **The human verdict is authoritative; the automated score is advisory and regression-detecting**
   (user decision, 2026-09-05). The metrics in F6 are proxies built for other domains. Their job is
   to catch drift cheaply, to make failures like the dropped-character case visible without manual
   inspection, and to prevent the identity-strength trap. They do not overrule the eye. Where the
   scorer and the human verdict disagree, **the human verdict stands and the threshold is
   recalibrated as data** — the metric is corrected to match observed judgement, never the reverse.
