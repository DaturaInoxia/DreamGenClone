# Phase 2 Research - Character Identity

**Date:** 2026-08-24; revised 2026-09-02
**Status:** External research complete; expanded qualification remains execution work

## Questions

1. What mechanism should preserve recurring identities without training first?
2. How can two identities remain assigned to the correct actor?
3. What evidence justifies LoRA training?
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
| Character LoRA | Strong recurring concept mechanism but needs curated data, training provenance, versioning, and checkpoint compatibility. | Conditional Phase 2 branch only after matrix failure. |
| Qwen edit | Official 2511 materials describe improved character and multi-person consistency; local proof confirms covered edits. It edits source images rather than supplying generator identity conditioning. | Use for explicit manual or bounded source-image corrections, not as hidden generator fallback. |

## Selected Architecture

Persist provider-neutral identity, body, and wardrobe assets plus exact model/workflow capability
profiles. Qualification attaches to a matrix cell, not a mechanism name. A profile may be qualified
for one near-frontal actor and rejected for angled or interacting actors at the same time.

Generation-first identity conditioning and composition-first multi-reference editing are distinct
candidate operations. IP-Adapter, PuLID, Qwen Edit 2511, FLUX.2 editing, and conditional character
LoRAs are evaluated under the same frozen facts and scoring rules where capabilities overlap. No
client, compiler, or dispatcher substitutes one operation for another.

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

## LoRA Decision

Do not train a LoRA when the selected reference path passes the matrix. Consider character LoRAs
only when the reference path fails identity likeness while ownership, composition, and workflow
stability are otherwise acceptable. Do not use LoRA training to repair pose, contact, clothing, or
location failures.

The training branch requires a separate approved dataset manifest, licensing/consent review,
checkpoint family, trigger token, training recipe, artifact checksum, and the same evaluation
matrix. A LoRA is accepted only if it improves the failed identity cells without reducing ownership
or prompt compliance.

## Sources Consulted

- `https://github.com/tencent-ailab/IP-Adapter`
- `https://github.com/ToTheBeginning/PuLID`
- `https://github.com/InstantID/InstantID`
- `https://github.com/QwenLM/Qwen-Image`
- `https://github.com/Comfy-Org/ComfyUI`
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
