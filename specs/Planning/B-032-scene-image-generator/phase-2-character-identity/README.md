# Phase 2 - Character Identity

**Status:** Planned, next implementation slice
**Epic:** B-032 Scene Image Generator

## Goal

Make recurring characters visually recognizable across renders, poses, and camera angles without relying on prose likeness cues alone.

## Delivery

- Add persisted `CharacterImageIdentityPack` records tied to character profiles.
- Store approved face and full-body references, wardrobe references, consent/provenance, descriptor snapshots, and asset checksums.
- Add reference-image conditioning as the first implementation path.
- Prove two recurring characters across at least two poses and two camera angles while preserving identity and clothing assignment.
- Use regional masks or equivalent conditioning for multi-character scenes to prevent identity bleed.
- Decide from measured results whether principal characters require checkpoint-compatible LoRAs.
- If LoRAs are justified, define dataset curation, trigger tokens, training provenance, checkpoint family, versioning, and inference strength as persisted configuration.

## Evidence

Qwen Image Edit 2511 is the selected semantic editing mechanism. The covered non-explicit proof passed 6/6 controlled edits; adult-content editing has been exercised in an exploratory, unscored session (`specs/image-generator-tests/qwen/images/adult-fellatio/`). Proof outputs remain under `specs/image-generator-tests/qwen/`.

## Exit Gate

Two characters pass the identity-preservation matrix across the required poses and POVs, with no silent fallback when references or adapter configuration are missing. The LoRA decision is recorded with evidence.
