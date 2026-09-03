# Phase 2 Implementation Plan - Character Identity

## Summary

Build from the existing identity POC into a clean-session character production system: shared asset
curation, separate identity/body/wardrobe versions, cell-qualified model capabilities,
family-specific deterministic compilers, durable multi-item workloads, immutable attempts, and a
context-preserving Production Studio.

Generation and source editing remain separate operations but share the asset, capability, workload,
attempt, lineage, review, and approval architecture. New sessions are the only runtime baseline;
there is no compatibility path for the existing one-off records.

## Change Surface

### Domain

- Add identity pack, reference asset, evaluation, assignment, synthetic LoRA dataset/training,
  artifact, and request-strategy records under `DreamGenClone.Domain/RolePlay`.
- Add enums for statuses, asset kinds, mechanisms, and decisions.
- Add `ResolvedIdentityImageModel` under `DreamGenClone.Domain/ModelManager`.

### Application

- Add repository and conditioned-client abstractions under
  `DreamGenClone.Application/Abstractions`.
- Add controlled request/result DTOs and validation.

### Infrastructure

- Extend `SqlitePersistence` additively.
- Implement identity repository and safe asset operations.
- Implement one ComfyUI conditioned client only after the host proof selects its workflow.

### Web

- Add identity-pack orchestration and a character-scoped management UI.
- Extend Model Manager with explicit identity settings.
- Add the resolver, compiler, job payload/handler, and Studio controlled-render action.
- Register services and handler in `Program.cs`.

### Tests

- Repository/migration and asset integrity tests.
- Pack approval/versioning tests.
- Resolver no-fallback and compatibility tests.
- Compiler actor-region binding tests.
- Workflow JSON tests using the selected proof fixture.
- Service/handler state and provenance tests.
- Razor diagnostics plus manual identity matrix.

## Slices

1. **Persistence slice:** records, schema, repository, safe files, approval rules.
2. **Curation slice:** character identity-pack UI and provenance/consent workflow.
3. **Production schema slice:** capability cells, intents, workloads, attempts, derivatives, clean
  session-generation gate.
4. **Compiler slice:** Pony, SDXL/Juggernaut/BigLust, FLUX.2, Qwen generation, and Qwen Edit
  registries with exact schema validation.
5. **Dispatch slice:** Together/RunPod adapters, grouping, immediate provider-ID persistence,
  transient-result capture, and restart reconciliation.
6. **Asset Manager slice:** shared catalog/pickers plus identity/body/wardrobe version workflows.
7. **Production Studio slice:** persistent context, workload preparation, queue, attempts,
  comparison, inspector, review, and approval.
8. **Synthetic LoRA slice:** Asset Manager identity bootstrap, coverage generation, curation,
  captioning, immutable dataset versions, configured training, attempt recovery, and artifacts.
9. **Qualification slice:** cell-based matrix UI/report, composition-first candidates,
  reference-only/LoRA-only/combined strategy cells, and application-path reruns.

Each slice receives narrow tests, solution build, and full tests before the next slice.

## Blast Radius

Existing sessions are intentionally unsupported by the production surfaces. The greatest risks are
provider/workflow drift, duplicate submission after interruption, transient provider output loss,
and falsely broad qualification. Mitigate with exact versions/schemas, persisted provider IDs,
owned storage, immutable snapshots, and cell-level gates. The RP generation engine is out of scope.

## Rollout

Create a new session, curate/approve assets, qualify exact capability cells, prepare a workload,
dispatch compatible groups, review attempts, and approve derivatives. Remove the old one-off Studio
path from the new-session workflow when its production replacement is complete; do not preserve it
as a hidden downgrade.
