# Phase 2 Research - Character Identity

**Date:** 2026-08-24
**Status:** Complete for implementation planning; host proof remains an execution task

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

Persist a provider-neutral identity pack and a resolved conditioning profile. Compare IP-Adapter and
PuLID on the actual pinned ComfyUI/Juggernaut environment with the same references, prompts, masks,
seeds, and scorecard. Select one mechanism from evidence, then integrate only that pinned workflow.

The application contract names capabilities (`FaceIdentity`, `FullBodyReference`, `RegionalMask`)
rather than third-party node names. Provider settings persist the selected mechanism, artifacts,
strengths, node/workflow revision, and checkpoint family.

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
