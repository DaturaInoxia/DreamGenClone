# Phase 2 Research - Character Identity

**Date:** 2026-08-24; revised 2026-09-03
**Status:** External research complete; synthetic LoRA implementation and qualification remain execution work

## Questions

1. What mechanism should preserve recurring identities without training first?
2. How can two identities remain assigned to the correct actor?
3. How does Asset Manager create and govern a wholly synthetic character LoRA dataset?
4. How should Qwen participate without replacing text-to-image generation?

## Existing Evidence

- Prompt-only Juggernaut generation does not reliably preserve identity assignment or geometry
  across seeds.
- OpenPose is useful for macro limb direction but failed exact-contact gates. It is not an identity
  mechanism.
- Qwen Image Edit 2511 passed six covered non-explicit semantic edits, including position swap and
  multi-person attribute preservation. Adult editing remains untested.
- The app already has strict image generation and Qwen editor resolution paths, immutable render
  records, background jobs, disk storage, and model provenance.

## Candidate Identity Mechanisms

| Candidate | Primary-source finding | Decision |
|---|---|---|
| IP-Adapter SDXL / face variants | Official project supports SDXL, text plus image prompts, ControlNet composition, face-focused variants, and Apache-2.0 code. Identity strength trades against prompt freedom. | Include in frozen host comparison. |
| PuLID v1.1 SDXL | Official project provides an SDXL identity model and documents Juggernaut-XL as a usable base. Apache-2.0 code; ComfyUI integration is community maintained. | Include in frozen host comparison. |
| InstantID | Official project is single-image and tuning-free, but explicitly says multi-person is unsupported and documents research-only checkpoint/face-model constraints. | Exclude from first product slice. |
| Character LoRA | Official and maintained trainers support character/concept LoRA training with per-image captions, trigger tokens, aspect-ratio bucketing, validation prompts/images, checkpoints, and exact base-model binding. | Required product capability. Dataset creation, training, artifact registration, and inference are separately versioned and qualified. |
| Qwen edit | Official 2511 materials describe improved character and multi-person consistency; local proof confirms covered edits. It edits source images rather than supplying generator identity conditioning. | Use for explicit manual or bounded source-image corrections, not as hidden generator fallback. |

## Selected Architecture

Persist provider-neutral identity, body, and wardrobe assets plus exact model/workflow capability
profiles. Qualification attaches to a matrix cell, not a mechanism name. A profile may be qualified
for one near-frontal actor and rejected for angled or interacting actors at the same time.

Generation-first identity conditioning, composition-first multi-reference editing, and LoRA-backed
generation are distinct candidate operations. IP-Adapter, PuLID, Qwen Edit 2511, FLUX.2 editing,
character LoRAs, and specifically qualified combinations are evaluated under the same frozen facts
and scoring rules where capabilities overlap. No client, compiler, or dispatcher substitutes one
operation or identity strategy for another.

The application contract names semantic capabilities (`FaceIdentity`, `BodyReference`,
`WardrobeReference`, `RegionalMask`, `MultipleIdentityReferences`, `SourcePreservation`) while a
qualified profile pins artifacts, workflow/compiler revision, checkpoint/model, reference limits,
and supported cells.

## Two-Character Ownership Strategy

- One approved identity-pack version per actor.
- One non-overlapping region mask per actor for each controlled shot.
- Adapter application is actor scoped; a global blend is invalid for two controlled identities.
- Wardrobe belongs to the visual actor snapshot, not the identity embedding.
- A result fails when faces are individually similar but swapped between actors.

## Evaluation Matrix

Use two adult test characters, two poses, and three views: front three-quarter, side/profile, and a
closer alternate view. Render two frozen seeds per cell, giving 12 outputs per candidate.

Score every output:

- expected cast count;
- identity A likeness and ownership;
- identity B likeness and ownership;
- wardrobe ownership;
- pose/view compliance;
- major anatomy integrity;
- no reference-image leakage or unintended blending.

The selected mechanism must pass at least 10 of 12 outputs overall, both identities in at least 5
of 6 composition cells, and all 12 ownership checks. Any identity swap is a hard failure.

## Synthetic Character LoRA Findings

DreamGenClone characters have no source photographs. Asset Manager therefore owns a synthetic
identity-bootstrap workflow rather than an upload-only training path:

1. Create and approve a canonical identity seed from generated candidates. The seed records its
  generator, model/version, exact request, seed, output checksum, policy, and operator decision.
2. Generate a planned coverage matrix from that seed using an explicitly selected qualified
  reference/edit capability. Coverage records face angle, crop, expression, body framing, pose,
  wardrobe state, lighting, background, and aspect ratio. The generation mechanism is provenance,
  not an assumed identity guarantee.
3. Curate candidates in Asset Manager. Reject identity drift, duplicate/near-duplicate frames,
  malformed anatomy, leakage, inconsistent permanent traits, and unplanned style/background
  repetition. Every accepted member references an immutable shared `SceneAsset` version.
4. Caption each accepted image with its trigger plus only visible, changeable attributes that the
  trainer should disentangle from identity. Captions and operator edits are versioned. A trainer
  may use one instance prompt only when the selected recipe explicitly requires it.
5. Freeze train and validation membership before training. Validation members and prompts are not
  silently moved into training, and any membership/caption change creates a new dataset version.
6. Train with an exact family recipe and base-model checksum. Persist all configured values,
  trainer/version, environment, checkpoints, logs, samples, and final artifact checksum.
7. Qualify the artifact at explicit inference strengths against frozen prompts/seeds and held-out
  compositions. Score identity, ownership, prompt compliance, diversity, anatomy, wardrobe and
  background leakage, and comparison with reference-only and qualified combined strategies.

Primary implementations establish the supported controls, not universal values. Diffusers and
maintained training tools support custom per-image captions, instance/class prompts, optional prior
preservation, repeats, validation prompts/images or held-out subsets, periodic samples/checkpoints,
and model-family-specific resolution/bucketing. AI Toolkit documents paired image/text captions,
`[trigger]` replacement, automatic downscaling and aspect-ratio buckets, while sd-scripts exposes
explicit validation splits and SDXL high-resolution bucketing. These facts require the application
to persist those choices; they do not justify hidden defaults for image count, rank, learning rate,
steps, caption dropout, prior preservation, or inference strength.

Synthetic origin increases correlated-error risk: a generator can repeat one face, wardrobe,
lighting, background, or defect until the LoRA learns that correlation as identity. Consequently,
dataset approval requires operator-visible coverage and duplicate/drift findings. Augmentation is
not a substitute for genuinely distinct approved views, and a generated image is never accepted
only because it came from the canonical seed workflow.

LoRA cannot by itself prove pose, contact, location geometry, or per-actor ownership. Those remain
separate capability axes. A character may have LoRA artifacts for multiple base families, and each
request explicitly selects reference conditioning, LoRA, or a specifically qualified combination.
There is no product-wide identity-mechanism selection and no runtime fallback between strategies.

## Sources Consulted

- `https://github.com/tencent-ailab/IP-Adapter`
- `https://github.com/ToTheBeginning/PuLID`
- `https://github.com/InstantID/InstantID`
- `https://github.com/QwenLM/Qwen-Image`
- `https://github.com/Comfy-Org/ComfyUI`
- `https://github.com/huggingface/diffusers/tree/main/examples/dreambooth`
- `https://github.com/huggingface/diffusers/blob/main/examples/dreambooth/train_dreambooth_lora_sdxl.py`
- `https://github.com/huggingface/diffusers/blob/main/examples/dreambooth/train_dreambooth_lora_flux.py`
- `https://github.com/huggingface/diffusers/blob/main/examples/dreambooth/train_dreambooth_lora_flux2.py`
- `https://github.com/huggingface/diffusers/blob/main/examples/dreambooth/train_dreambooth_lora_flux2_klein.py`
- `https://github.com/ostris/ai-toolkit`
- `https://github.com/kohya-ss/sd-scripts`
- `https://docs.bfl.ai/flux_2/flux2_overview`
- Local Qwen proof manifest and `phase-0-architecture-and-evidence/controlnet-touch-proof.md`

## 2026-09-02 External Challenge And Local Reconciliation

The dated provider/model ledger is [`../provider-evidence-matrix.md`](../provider-evidence-matrix.md).
Its controlling conclusions are:

- FLUX.2 and Qwen Edit multi-reference consistency are candidate capabilities requiring local
  composition-first identity matrices.
- Together supports model-specific image references/variations, but current evidence does not
  establish image generation through its JSONL Batch API.
- InstantID explicitly lacks multi-person support and has unresolved production licensing limits.
- IP-Adapter documents adherence/editability and center-crop tradeoffs. The local C2/C3 Dean
  failures are capability limits, not prompt defects to hide.
- PuLID SDXL and FLUX variants have different artifacts and fidelity evidence and require separate
  profiles/matrices.
- The near-frontal IP-Adapter cells remain candidates only. Multi-angle references and FACEID-v2
  did not rescue the failed angled cells.

Users select approved references and semantic intent; deterministic model-family compilers build
provider requests. DeepSeek is not a media-prompt polishing stage. Workloads and attempts snapshot
exact inputs, compiler/profile/model/workflow versions, provider IDs, and owned outputs. New sessions
are the baseline; no legacy production-row migration is required.
