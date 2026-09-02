# B-032 Provider And Model Evidence Matrix

**Status:** Controlling research ledger for Phase 2 and Phase 3 planning  
**Research cutoff:** 2026-09-02  
**Rule:** Official capability is not product qualification. Every exposed production cell also
requires DreamGenClone proof evidence.

## 1. Evidence Classes

| Class | Meaning | May establish |
|---|---|---|
| `Official` | Provider/model author documentation, API schema, model card, or repository | Candidate capability, legal parameters, limits, licensing, recommended technique. |
| `Community` | Maintainer/community integration, creator workflow, or third-party implementation | Candidate workflow and operational practice; never product qualification alone. |
| `Local` | Frozen DreamGenClone proof, application test, or scored matrix | Qualification for the exact tested tuple and cells. |
| `Hypothesis` | Reasonable but unproven application claim | A task/proof requirement only; never enabled production behavior. |

Marketing claims are recorded as author claims and remain unqualified until local evidence passes.
Download counts, stars, rankings, and popularity never select a mechanism.

## 2. Still And Edit Families

| Family/version | Operation | Official facts | Forbidden assumptions | Current local status |
|---|---|---|---|---|
| Pony V6 XL | Generate | Tag-oriented family with its own quality/rating/count vocabulary and workflow settings. | Do not use SDXL prose or FLUX JSON. Do not infer multi-character ownership from count tags. | Existing builder/proofs remain applicable only to their tested workflow. Requalify with Phase 2 assets/workloads. |
| SDXL 1.0 finetunes: Juggernaut/BigLust | Generate | Natural-language photographic prompt; sampler/settings and limitations are checkpoint/workflow specific. Faces at distance and multi-person attribute binding are known weak areas. | Do not use Pony tags. Prompt clauses do not prove actor ownership or exact geometry. | Prompt-only production exists. Identity/control combinations require cell qualification. |
| FLUX.2 Pro/Max/Flex/Dev | Generate/Edit | BFL documents subject + action + style + context, priority ordering, structured JSON for automation/complex scenes, explicit reference roles, multi-reference editing, exact-color association, and seed support. FLUX.2 has no negative prompt. Limits vary by variant; Pro documents total input+output megapixel constraints. Fixed endpoints are preferred over preview endpoints for reproducibility. | No negative prompt. No universal reference count or megapixel limit across variants/providers. Prompt upsampling is not deterministic application compilation. | Candidate for structured generation and composition-first multi-reference editing. No DreamGenClone production qualification yet. |
| Qwen Image 2512 | Generate | Official project describes improved human realism, texture, text rendering, aspect-ratio recipes, negative prompts, and model-specific enhancement utilities. Apache-2.0 model project. | Official prompt enhancement does not justify sending B-100 facts to DeepSeek. Published examples do not establish DreamGenClone continuity. | Candidate; no production qualification yet. |
| Qwen Image Edit 2511 | Edit | Official project supports multiple images and claims improved character and multi-person consistency; official sample uses `QwenImageEditPlusPipeline` with explicit settings. | Do not use as hidden fallback from generation. Do not infer exact identity ownership for arbitrary angles/compositions. | Local covered semantic edit proof passed 6/6; adult exploratory output is not a scored gate. Multi-reference identity-after-composition requires a new matrix. |

## 3. Identity Mechanisms

| Mechanism | Evidence | Constraint/tradeoff | Qualification decision |
|---|---|---|---|
| IP-Adapter / face variants | Official project supports SD/SDXL image prompting, face variants, multimodal text+image conditioning, and ControlNet composition. It documents scale tradeoffs and center-crop loss for non-square inputs. Apache-2.0 code. | Higher image adherence reduces diversity/text freedom. Crop/preprocessing can discard identity information. Generic/global image conditioning does not establish per-person ownership. | Local single/near-frontal cells are evidence. Angled and strict two-actor cells failed. Only exact passing cells may remain qualified. |
| PuLID v1.1 SDXL | Official project reports improved compatibility, editability, facial naturalness, and similarity over v1; Juggernaut-XL is a documented base option. Apache-2.0 code; ComfyUI implementation is community maintained. | Exact checkpoint/node/workflow compatibility must be pinned. Project’s FLUX table reports variant-specific fidelity gaps. | Candidate, not selected by reputation. Must run the same frozen matrix. |
| PuLID FLUX 0.9.x | Official project supports FLUX and reports 0.9.0 male-fidelity weakness and improved 0.9.1 similarity. | Not equivalent to PuLID SDXL; checkpoint and workflow differ. Multi-person route cited by project is a third-party regional implementation. | Separate candidate profile and matrix. |
| InstantID | Official project is tuning-free, single-reference, SDXL-based identity generation. It explicitly states multi-person is unsupported and uses only the largest detected face. Checkpoints/InsightFace assets have research-use constraints documented by the project. | Not suitable for initial multi-person commercial product cells without license resolution and a different mechanism. | Excluded from Phase 2 production candidates. May be reconsidered only by a new evidence decision. |
| Character LoRA | Model-family/community practice supports recurring concepts when trained and invoked against compatible bases. | Requires consent/licensing, curated dataset, training provenance, trigger governance, exact base compatibility, and inference qualification. It cannot repair pose, contact, or location geometry. | Conditional branch only after a qualified reference/edit route fails identity cells for a principal character. |

## 4. Provider Transport And Retention

| Provider | Official transport facts | Architecture consequence |
|---|---|---|
| Together Images | `/v1/images/generations`; legal fields vary by exact model. Image API supports `n` from 1 to 4, seeds, response URL/base64, selected reference fields, and model-specific dimensions/settings. Its compatibility table says FLUX.2 does not accept `negative_prompt`. | Capability profile must whitelist fields per model. Variations in one request remain separate attempts. Prefer base64 or immediate URL capture for application-owned storage. |
| Together Batch | Documented JSONL asynchronous Batch API supports named eligible endpoint families; current evidence does not establish images-generation support. | Do not route image workloads through JSONL Batch. Revisit only after exact official endpoint support is documented and qualified. |
| BFL API | Submit then poll. BFL documents fixed and preview endpoints; signed result URLs are short lived. | Persist request/polling ID immediately and download result immediately. Pin fixed endpoints for production profiles. |
| RunPod Serverless | Queue-based custom worker execution with provider job IDs/status and finite provider result retention. Worker image, network volume, endpoint, and concurrency are deployment concerns. | Durable local workload/attempt state is authoritative. Persist IDs before polling and copy outputs into owned storage. B-102 owns deployment/transport, not request semantics. |
| ComfyUI API | Workflow JSON, asynchronous queue, selective graph execution/caching, and workflow/seed recovery from generated media are documented features. | Pin workflow JSON and node/model revisions. Store submitted workflow and seed. Do not depend on mutable UI graph state. |

## 5. Model-Native Compiler Rules

### 5.1 FLUX.2

- Order: primary subject, key action, critical style, essential context, secondary detail.
- Use natural language for simple work and structured JSON for automation, multiple subjects, and
  independently editable production fields.
- Bind each reference by ordinal and role.
- Associate precise colors with exact subjects/objects.
- Describe desired positive state; do not generate a negative prompt.
- Apply variant-specific dimensions, reference count, guidance, steps, and megapixel validation.

### 5.2 Qwen generation/edit

- Generation and editing use different pipelines and compiler identities.
- Editing receives ordered image references and an explicit transformation/preservation contract.
- The exact Qwen version, pipeline, settings, negative, and input list are snapshotted.
- Official Qwen prompt-enhancement tools are research evidence, not the runtime architecture.
  DreamGenClone compiles from validated structured facts without generic LLM polishing.

### 5.3 SDXL/Juggernaut/BigLust

- Use concise natural-language photographic briefs grounded in the canonical prompt standards.
- Keep each actor’s physical appearance and clothing in one self-contained clause.
- Put framing and required visible subjects early.
- Keep negative and sampling settings within the exact qualified profile.
- Treat regional prompts, identity adapters, and ControlNet as separately qualified controls rather
  than prompt-text improvements.

### 5.4 Pony V6 XL

- Use the existing Pony-specific tag compiler and validated quality/rating/count grammar.
- Do not import SDXL prose conventions, FLUX JSON, or unrelated generic negative advice.
- Requalify exact identity/control workflow combinations; prompt compliance is not identity proof.

## 6. Future Audio And Video Compiler Evidence

These findings constrain the shared architecture but are not Phase 2/3 implementation scope.

| Modality/provider | Official findings | Required future compiler fields |
|---|---|---|
| ElevenLabs TTS | Voice selection is primary. Model/version changes pause, pronunciation, tag, and normalization behavior. Eleven v3 uses audio tags/punctuation and does not support SSML break tags; other models differ. | Original display text, spoken text, voice profile, model, language/locale, delivery, allowed tags, punctuation/emphasis, pronunciation dictionary, normalization, speed/stability, continuity. |
| ElevenLabs SFX v2 | Request includes text, optional 0.5–30 second duration, loop, prompt influence, model, and output format. | Event order, source/material/space, duration, loop, influence, output format, policy. |
| ElevenLabs Music | Official guidance calls out genre, mood, instrumentation, tempo, production era, studio vocabulary, arrangement order, key, vocal policy, duration, sections, exclusions, and optional owned audio reference. | Those fields remain typed until deterministic music compilation. |
| Google Veo | Official anatomy includes subject, action, scene/context, camera angle/movement, lens/optics, lighting/style/ambience, temporal development, audio, cinematic terms, and provider-specific negative form. First/last/reference frame workflows are documented separately. | Typed video intent, frame references, duration, temporal/action sequence, camera/optics, native-audio policy, negative list, exact model capability. |

## 7. Creator Workflow Evidence

| Product/practice | Evidence class | Relevant pattern | DreamGenClone adoption |
|---|---|---|---|
| Midjourney Edit Model | Official product documentation | Reuse uploaded assets; pin references; combine up to four references; targeted in/outpainting; perspective changes; warns about attribute mixing. | Persistent reference selection, explicit reference roles, source-edit workspace, and visible ownership warnings. Provider limits do not transfer to other models. |
| ComfyUI | Official repository/docs | Reusable workflows/subgraphs, queues/history, partial execution, masks/compositing, metadata and seed recovery. | Immutable workflow snapshots, partial branch repair, queue/history UI, lineage inspector. |
| InvokeAI boards/queue | Community/product practice pending refreshed official capture | Board-like organization and queued generation are useful candidate UX patterns. | Do not cite as a controlling fact until source is refreshed; Asset Manager design stands on application requirements independently. |

## 8. Local Evidence Ledger

| Evidence | Class | Result | Permitted conclusion |
|---|---|---|---|
| Phase 2 two-character IP-Adapter matrix, 2026-08-26 | Local | Strict gate failed; Becky held, Dean failed angled C2/C3 cells; ownership remained clean. | Do not qualify strict angled two-character production. Near-frontal guard candidate may proceed only under exact constraints and further application proof. |
| Multi-angle IP-Adapter follow-up, 2026-08-27 | Local | Angle-matched references improved structure but did not preserve one identity across angles. | Multi-angle reference selection is not accepted as the identity solution. |
| FACEID v2 probe | Local | Degraded consistency and did not rescue angled cells. | Rejected for this exact tested workflow. |
| Qwen Edit 2511 six-edit proof | Local | Six covered non-explicit semantic edits passed. | Qualifies only covered edit behaviors; not arbitrary identity-after-composition or adult production. |
| OpenPose/contact proof | Local | Macro placement changed causally; exact contact gates failed. | Pose control may guide macro geometry. It does not prove contact, anatomy, or semantic ownership. |
| Phase 1 image pipeline | Local | Prompt/render/edit storage and jobs exist with recorded historical test counts. | Reusable code evidence only. New clean-session production architecture still requires new tests and current reruns. |

Historical test counts are not current validation and must be rerun during implementation.

## 9. Required Qualification Matrices

### 9.1 Phase 2 matrix axes

- model/provider/workflow/compiler version;
- mechanism and strength/configuration;
- one/two/three visible characters where claimed;
- front, three-quarter, profile, elevated/low head angles;
- close, medium, full-body, and distant/establishing crops;
- neutral, asymmetric, occluded, interacting, and crossing compositions;
- identity, body, and wardrobe reference combinations;
- source-generation versus composition-first edit;
- pose/depth/region/mask combinations;
- fixed seeds and unfavourable-seed reporting.

Score cast, per-actor likeness, ownership, body, wardrobe, pose, viewpoint, anatomy, leakage,
location preservation, and compiler/request validity. Any identity swap is a hard failure.

### 9.2 Phase 3 matrix axes

- location profile and state variant;
- wide, medium, close/reaction, over-the-shoulder, reverse, and character POV shots;
- actor/blocking and screen direction;
- landmark identity and relative placement;
- prop ownership and occlusion;
- lighting/time variant;
- qualified identity workflow;
- pose/depth/region/semantic control combination;
- still and prospective first/last video-keyframe suitability.

Score the invariant facts shared by the shot family and the shot-specific facts. A favourable image
cannot hide a failed family invariant.

## 10. Open Hypotheses

These are implementation/proof tasks, not accepted requirements of model behavior:

- FLUX.2 or Qwen Edit 2511 multi-reference editing can preserve two DreamGenClone identities after
  a composition-first base render across the required angles.
- A character LoRA can improve failed angled identity cells without reducing ownership or prompt
  compliance.
- Depth plus actor regions can preserve Phase 3 location/blocking better than depth alone.
- Approved stills are sufficiently consistent to serve as future video first/last frames.
- Warm-window grouping materially lowers RunPod cost/latency for the selected worker images.

Each hypothesis needs frozen inputs, exact versions, scored output, rejected-result retention, and a
written qualification decision.

## 11. Sources

Official/primary sources consulted on or before 2026-09-02:

- BFL FLUX.2 prompting: https://docs.bfl.ai/guides/prompting_guide_flux2
- BFL FLUX.2 editing: https://docs.bfl.ai/flux_2/flux2_image_editing
- BFL FLUX.2 text-to-image: https://docs.bfl.ai/flux_2/flux2_text_to_image
- Together image parameters: https://docs.together.ai/docs/inference/images/parameters
- Together reference images: https://docs.together.ai/docs/inference/images/reference-images
- Qwen Image official repository: https://github.com/QwenLM/Qwen-Image
- IP-Adapter official repository: https://github.com/tencent-ailab/IP-Adapter
- PuLID official repository: https://github.com/ToTheBeginning/PuLID
- InstantID official repository: https://github.com/instantX-research/InstantID
- ComfyUI official repository: https://github.com/Comfy-Org/ComfyUI
- ComfyUI partial execution: https://docs.comfy.org/interface/features/partial-execution
- ElevenLabs TTS best practices: https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices
- ElevenLabs music best practices: https://elevenlabs.io/docs/overview/capabilities/music/best-practices
- ElevenLabs SFX API: https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert
- Google Veo prompt guide: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/video/video-gen-prompt-guide
- Midjourney Edit Model: https://docs.midjourney.com/hc/en-us/articles/48495453462797-Edit-Model

The model-family instruction files remain the practical source for already validated exact current
strings/settings. FLUX.2 and Qwen generation are not implementation-ready compiler families until
their exact researched envelopes/rules are added to the canonical compiler standards and dedicated
family instructions. A compiler implementation must reconcile this ledger, those instructions, and
a current official API schema before coding.
