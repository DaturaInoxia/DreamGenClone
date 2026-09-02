# Phase 2 - Character Identity

**Status:** Partially implemented; expanded production scope ready for implementation
**Epic:** B-032 Scene Image Generator
**Baseline:** New sessions created after the Phase 2 production schema is installed
**Architecture:** [`../production-architecture.md`](../production-architecture.md)
**Evidence:** [`../provider-evidence-matrix.md`](../provider-evidence-matrix.md)

## Goal

Deliver an operational character-production system that preserves approved identity, body, and
wardrobe ownership across every explicitly qualified render/edit cell. Unsupported cells fail
readiness instead of falling back.

## Implementation Package

- [`research.md`](research.md) - candidate mechanisms, primary evidence, two-character matrix, and
	LoRA decision rule.
- [`spec.md`](spec.md) - requirements, acceptance scenarios, and exit gate.
- [`data-model.md`](data-model.md) - identity packs, assets, assignments, evaluations, and decisions.
- [`contracts.md`](contracts.md) - repositories, resolver/client/job, storage, and host-proof contract.
- [`plan.md`](plan.md) - layered change surface, slices, blast radius, and rollout.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

Completed P2-001 through P2-026 remain historical implementation evidence. P2-023 and P2-027
through P2-032 remain open. Expanded production work starts at P2-033.

## Delivery

- Add persisted `CharacterImageIdentityPack` records tied to character profiles.
- Store approved face and full-body references, wardrobe references, consent/provenance, descriptor snapshots, and asset checksums.
- Register qualification by exact provider/model/workflow/compiler/reference-layout cell; no
  mechanism receives universal approval.
- Support generation-first and composition-first edit candidates as separate operations.
- Bind each visible actor to exact identity, body, and wardrobe versions and explicit reference
  roles.
- Prepare Moments/shots as durable workloads, dispatch compatible bounded groups, persist every
  provider attempt, and review/approve outputs in one production surface.
- Provide a unified Asset Manager for reference curation, provenance, lineage, approval, and reuse.
- **ControlNet pose/layout control (B-097 re-open 2026-08-25):** add ControlNet OpenPose + Depth
  conditioning remains a candidate pose/layout control. Exact artifacts and strengths are
  configuration and qualification data; this specification does not hardcode a universal range.
- Decide from measured results whether principal characters require checkpoint-compatible LoRAs.
- If LoRAs are justified, define dataset curation, trigger tokens, training provenance, checkpoint family, versioning, and inference strength as persisted configuration.

## Evidence

Qwen Image Edit 2511 is qualified only for the six covered local semantic-edit cases. Its official
multi-image and consistency claims make identity-after-composition a candidate matrix, not an
already-qualified production path. The strict angled two-character IP-Adapter matrix failed;
multi-angle and FACEID-v2 follow-ups did not repair it.

## Exit Gate

The Asset Manager, Production Studio, compiler registry, workload pipeline, attempt lineage, and
approval path operate end to end for new sessions. Every exposed capability cell passes its frozen
matrix, including two-character ownership where claimed. Unsupported cells are blocked. The LoRA
decision is recorded with evidence, and all automated/manual gates pass.
