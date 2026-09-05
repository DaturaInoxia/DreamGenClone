# B-109 — Identity Conditioning Strategy Expansion

**State:** designed — ready for implementation
**Created:** 2026-09-05
**Owner surface:** Model Manager (capability configuration) + Character Identity / Production Studio (per-render strategy selection)

## Why this exists

Investigation (this session) found two concrete gaps in the current face-identity conditioning path:

1. **Angle-aware reference selection was designed but is not implemented.** The domain model
   documents it explicitly (`SceneImageReferenceFaceView` — *"Used by the multi-angle compiler to
   pick the reference nearest the target head angle"*), and a repo-memory handoff note confirms an
   earlier `SceneImageHeadAngleResolver` existed and was dropped during the RunPod Serverless
   migration. Today `IdentityControlledRequestCompiler` always uses the single
   `CanonicalFaceAssetId` fixed at pack-approval time — never any of the other 4 approved views —
   regardless of the target shot's pose.
2. **Only one mechanism is configured, and it's not the strongest available option already in the
   codebase.** The configured mechanism is IP-Adapter with adapter ref `PLUS FACE (portraits)` —
   the CLIP-vision-embedding preset. `SceneImageIdentityMechanism.PuLid` is **already fully
   implemented** in `ComfyUIIdentityConditionedClient` (`PulidModelLoader`, `PulidInsightFaceLoader`,
   `ApplyPulid`) but is not the qualified/configured mechanism.

The user asked for a plan covering all six discussed methods, with recommendations, and explicitly
wants the application to **support more than one simultaneously** rather than pick a single winner.

## Verified infrastructure findings (2026-09-05)

Checked against the live deployment registries and Model Manager export, not just application code:

- **BigLust's serverless endpoint DOES have IP-Adapter installed.** *(Correction: an earlier pass
  in this session wrongly concluded BigLust had no identity capability, based on a stale
  2026-08-31 handoff note describing endpoint `ovwnwol2o30grn`. That endpoint was recreated via
  GitHub Integration on 2026-09-01 as `yhae6ihkabyb0o` — confirmed by
  `run_biglust_identity.py`'s own comment, "2026-09-01: recreated via GitHub Integration (was
  ovwnwol2o30grn)", by the live Model Manager provider notes, *"RunPod Serverless BigLust v1.6
  endpoint img-biglust-serverless (worker-comfyui + IP-Adapter)"*, and by the external
  `specs/image-generator-tests/biglust/run_biglust_identity.py` matrix runner, whose own docstring
  states it "Runs the identity cells against the IP-Adapter-enabled BigLust endpoint." The stale
  note was superseded infrastructure history, not the current state.)*
- **The currently-enabled `bigLust_v16.safetensors` Model Manager row has no identity fields set**
  (`IdentityMechanism`/`IdentityStrength`/`IdentityAdapterRef` are all `null` on that row), even
  though its endpoint supports IP-Adapter. This is a **configuration gap**, not an infrastructure
  gap — `ModelResolutionService.ResolveIdentityImageModelAsync` would fail fast for BigLust today
  purely because those three fields are unpopulated on the active row, not because the mechanism is
  unavailable. Fixable by setting the same known-good values already used on other rows
  (`IpAdapter` / `0.8` / `"PLUS FACE (portraits)"`).
- **PuLID is not installed on any production serverless endpoint.** `PuLID_ComfyUI` was installed
  only on the isolated, non-serverless proof pod (`7i2mutjmry5tkt`) used for the one-time P2-011–016
  evidence matrix. Neither the Juggernaut identity worker's Dockerfile (installs
  `ComfyUI_IPAdapter_plus` only) nor BigLust's provider notes ("+ IP-Adapter" only, never "+
  PuLID") show any PuLID install. No BigLust-specific worker Dockerfile was found in
  `helpers/runpod/serverless/`, so this is stated with the caveat that the exact build definition
  behind the 2026-09-01 recreation was not directly inspected — but every recorded note is
  consistent with IP-Adapter-only.
- **Multi-reference input on the Qwen Edit serverless endpoint (`img-qwen-edit-serverless`,
  `79wkn5jz5d5txx`) is unverified**, not proven false. Its only recorded smoke test used exactly one
  source image. `TextEncodeQwenImageEditPlus` is plausibly a stock ComfyUI node (no custom-node
  install step is recorded for this endpoint, unlike IP-Adapter's explicit
  `comfy-node-install` step), so multi-slot input may already work — but this has never been
  exercised on the deployed endpoint. B-106 (B106-013) and B-108 already require verifying this
  before implementation; this finding confirms that caution was warranted.
- **`img-biglust-serverless` is missing from the canonical `helpers/runpod/serverless/endpoints.json`
  registry**, despite that file's own rule that every endpoint change must be recorded there. This is
  independently already tracked as a pending item in
  `specs/Planning/B-101-serverless-migration/plan.md` ("Add `img-biglust-serverless` to
  `helpers/runpod/serverless/endpoints.json`") — not new work invented here, just cross-referenced.

### Net effect on the six methods

| Method | Original framing | Corrected, verified status |
|---|---|---|
| 1. Angle-aware selection | App-code fix only | Unchanged |
| 2. PuLID | "Zero new code, just needs qualification" | **Corrected:** needs a new worker build/redeploy with `PuLID_ComfyUI` installed before qualification can even begin — genuine infrastructure work |
| 3. Multi-reference | "Verify before enabling" | Confirmed genuinely unverified on the deployed Qwen Edit endpoint — no change to the plan, this finding just confirms the caution was correct |
| 4/5. FaceID/InstantID | New integration, proof-gated | Unchanged |
| 6. B-106 pixel-edit | Depends on Qwen Edit ordered-reference | Endpoint proven for single-image edits; multi-reference specifically unproven (see above) |
| **BigLust identity (pre-existing feature, not one of the 6 methods)** | *(not addressed)* | Endpoint is capable; the enabled Model Manager row just needs its identity fields populated — a data-entry fix, tracked in tasks.md section B |

## The six methods

| # | Method | Status in this codebase |
|---|---|---|
| 1 | Angle-aware reference selection (pick nearest of N approved views by target head angle) | Designed, not implemented (regression) |
| 2 | PuLID | Fully coded, not configured/qualified |
| 3 | Multi-reference averaging (feed 2-3 approved views into one embedding call) | Not implemented |
| 4 | IP-Adapter FaceID (ArcFace-based, vs. the configured CLIP-vision Plus Face) | Not implemented — new model artifacts |
| 5 | InstantID | Not implemented — new mechanism entirely |
| 6 | Pixel-level reference edit (B-106 Identity stage, Qwen ordered-reference) | Designed (B-106), not yet built |

## The key architectural decision

Methods 1–5 are all **generation-time (T2I) mechanisms** — they compete for the same
`ResolvedIdentityImageModel.Mechanism` slot today, which is why only one can be active. Method 6 is
structurally different: it is a **post-composition edit stage** (B-106's Identity stage), applied
*after* a Composition attempt already exists, not a T2I-time embedding.

This package makes methods 1–5 **independently qualified, per-request-selectable strategies**,
reusing the exact pattern B-032 Phase 2 already built for `ReferenceConditioning`/`Lora`/`Combined`
(FR2-053: *"The application qualifies reference-only, LoRA-only, and combined strategies as
distinct exact cells... No strategy falls back to another"*). Method 6 remains the separate B-106
stage and **coexists** with whichever T2I mechanism is used for Composition — they are not
alternatives to each other, they are different pipeline stages that can both run in the same
production group.

## Recommendation (priority order)

1. **Fix the BigLust Model Manager configuration gap** — populate `IdentityMechanism`/
   `IdentityStrength`/`IdentityAdapterRef` on the enabled `bigLust_v16.safetensors` row with the
   already-proven values. Trivial, unblocks the existing "+Identity" feature for the actual
   production model today, independent of everything else in this package.
2. **Method 1 (angle-aware selection)** — restores documented intent, zero new mechanism, applies
   to every other method automatically. Do this next regardless of what else is chosen.
3. **Method 2 (PuLID)** — requires a new worker build (install `PuLID_ComfyUI`) before
   qualification; still worth doing, but no longer "zero cost."
4. **Method 3 (multi-reference averaging)** — moderate effort; verify the Qwen Edit/IP-Adapter
   node's exact batched-input contract on the deployed endpoint first (confirmed still open).
5. **Method 6 (B-106 pixel edit)** — already scoped separately; proceed on its own timeline.
6. **Method 4 (IP-Adapter FaceID)** and **Method 5 (InstantID)** — genuinely new integrations;
   worth doing, but only after 1–4 are qualified and only via the same isolated-pod proof
   discipline B-032 Phase 2 section C already established (no shortcutting the proof gate).

## Documents

| File | Purpose |
|---|---|
| [spec.md](spec.md) | Goal, non-goals, user stories, functional requirements, acceptance scenarios, exit gate |
| [plan.md](plan.md) | Design decisions, reuse map, per-method implementation notes, phased plan, risks |
| [ui-contract.md](ui-contract.md) | Model Manager qualification surface + per-render strategy selector |
| [tasks.md](tasks.md) | Ordered, checkable implementation tasks |

## Controlling upstream documents

- `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs` — `SceneImageReferenceFaceView`,
  `SceneImageIdentityMechanism` (extended by this package)
- `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/tasks.md` section C/D —
  the proof discipline (isolated pod → dependency manifest → approval → frozen matrix → scored
  gate → decision) that any new mechanism (methods 4, 5) must follow, unchanged
- `specs/Planning/B-106-production-studio-staged-workflow/` — method 6, the coexisting Identity
  stage
- `.github/instructions/qwen-image-edit-2511.instructions.md`,
  `specs/Planning/B-032-scene-image-generator/provider-evidence-matrix.md` — evidence discipline
