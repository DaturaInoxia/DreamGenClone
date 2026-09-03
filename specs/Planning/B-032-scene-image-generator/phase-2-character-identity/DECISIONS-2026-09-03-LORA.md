# Phase 2 LoRA Architecture Decision (2026-09-03)

## Decision

DreamGenClone must support character LoRA as a first-class capability. Asset Manager must generate,
curate, caption, version, and approve the training images because fictional characters have no
assumed source photographs.

Identity is not one application-wide mechanism. Each production request explicitly selects one
qualified strategy:

- reference conditioning;
- LoRA;
- a specifically qualified combination of references and LoRA.

A missing or rejected strategy cell blocks readiness. The application never substitutes another
strategy, model, artifact, provider, or operation.

## Research Basis

Primary and maintained trainer implementations support the controls required for a governed product
workflow: per-image or instance captions, trigger tokens, aspect-ratio buckets, repeats, optional
prior preservation, validation prompts/images or held-out splits, periodic samples/checkpoints, and
exact base-model binding. Relevant sources are listed in `research.md` and the provider evidence
matrix.

Those implementations do not define a trustworthy synthetic fictional-identity bootstrap. The
application therefore owns that workflow: approve a canonical generated identity seed, expand a
persisted coverage matrix, curate drift/duplicates/defects/leakage, freeze immutable shared-asset
membership and captions, train with an exact configured recipe, and qualify the resulting artifact
on held-out prompts and compositions.

No image-count, rank, learning-rate, step, dropout, prior-preservation, checkpoint, precision, or
inference-strength value becomes a hidden application default. Each value belongs to an explicit,
UI-backed, exact-family training or capability profile.

## Superseded Conclusions

The 2026-08-26 IP-Adapter and FACEID results remain valid local evidence for their exact tested
cells. They do not decide whether LoRA exists in the product. The following conclusions are
superseded wherever they appear as active architecture:

- train LoRA only after reference conditioning fails;
- record `NotRequired`, `Required`, or `Deferred` as the product LoRA decision;
- choose one global identity mechanism for a registered model or the application;
- use a FLUX/Qwen proof to lock all requests to one identity path.

## Implementation Consequences

- Add versioned synthetic dataset, member, training job/attempt, and artifact aggregates.
- Reuse `SceneAsset` as the canonical image byte/metadata catalog.
- Keep dataset membership, captions, coverage, curation findings, and splits immutable after freeze.
- Persist trainer/base/environment/recipe/checkpoint/sample/output provenance without secrets.
- Replace global mechanism selection with capability declarations and per-request strategy bindings.
- Qualify LoRA-only and combined cells independently for artifact version, strength, actor count,
  angle, crop, composition, and other required axes.
- Preserve historical proof files and rejected outputs without relabeling them.
