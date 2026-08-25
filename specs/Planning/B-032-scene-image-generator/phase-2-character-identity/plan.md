# Phase 2 Implementation Plan - Character Identity

## Summary

Add identity assets and immutable pack versions, prove one SDXL conditioning mechanism, then add a
new controlled render path that compiles actor-scoped references and regions. Existing prompt-only
and Qwen-edit paths remain unchanged.

## Change Surface

### Domain

- Add identity pack, reference asset, evaluation, assignment, decision, and optional LoRA records
  under `DreamGenClone.Domain/RolePlay`.
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
3. **Proof slice:** frozen candidate workflows and 12-output scorecard.
4. **Configuration slice:** selected mechanism fields, resolver, health diagnostics.
5. **Rendering slice:** compiler, client, job, Studio action, provenance.
6. **Decision slice:** evaluation UI/report and LoRA decision; implement LoRA only if required.

Each slice receives narrow tests, solution build, and full tests before the next slice.

## Blast Radius

Additive scene-image and character-profile surfaces. Existing sessions and image rows require no
backfill. The greatest risk is ComfyUI dependency drift; mitigate it with pinned workflow/node/model
revisions and contract tests. The RP generation engine is out of scope.

## Rollout

Identity control is exposed as an explicit Studio mode only after a profile passes the proof gate.
Prompt-only remains available as a separate mode. Do not feature-flag a hidden downgrade path.
