# Phase 2 - Character Identity

**Status:** Planned after Phase 1B vision-aware editing
**Epic:** B-032 Scene Image Generator
**Prerequisite:** Phase 1B vision-aware editing exit gate

## Goal

Make recurring characters visually recognizable across renders, poses, and camera angles without relying on prose likeness cues alone.

## Implementation Package

- [`research.md`](research.md) - candidate mechanisms, primary evidence, two-character matrix, and
	LoRA decision rule.
- [`spec.md`](spec.md) - requirements, acceptance scenarios, and exit gate.
- [`data-model.md`](data-model.md) - identity packs, assets, assignments, evaluations, and decisions.
- [`contracts.md`](contracts.md) - repositories, resolver/client/job, storage, and host-proof contract.
- [`plan.md`](plan.md) - layered change surface, slices, blast radius, and rollout.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

## Delivery

- Add persisted `CharacterImageIdentityPack` records tied to character profiles.
- Store approved face and full-body references, wardrobe references, consent/provenance, descriptor snapshots, and asset checksums.
- Prove candidate SDXL reference-image conditioning mechanisms first, then pin and integrate the
	one that passes. Do not preselect or silently substitute a backend.
- Prove two recurring characters across at least two poses and two camera angles while preserving identity and clothing assignment.
- Use regional masks or equivalent conditioning for multi-character scenes to prevent identity bleed.
- Decide from measured results whether principal characters require checkpoint-compatible LoRAs.
- If LoRAs are justified, define dataset curation, trigger tokens, training provenance, checkpoint family, versioning, and inference strength as persisted configuration.

## Evidence

Qwen Image Edit 2511 is the selected semantic editing mechanism. The covered non-explicit proof passed 6/6 controlled edits; adult-content editing has been exercised in an exploratory, unscored session (`specs/image-generator-tests/qwen/images/adult-fellatio/`). Proof outputs remain under `specs/image-generator-tests/qwen/`.

## Exit Gate

Two characters pass the identity-preservation matrix across the required poses and POVs, with no silent fallback when references or adapter configuration are missing. The LoRA decision is recorded with evidence.
