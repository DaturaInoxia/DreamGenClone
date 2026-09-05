# B-106 — Production Studio Staged Workflow (Identity, Finish, Image Management)

**State:** designed — ready for implementation
**Created:** 2026-09-04
**Owners:** B-100 owns the staged studio workflow (US10); B-032 owns image execution mechanisms
**Blocks:** B-032 Phase 2 exit gate (P2-057 → P2-059) and therefore all of B-032 Phase 3

## Why this package exists

B-032 Phase 2 delivered a complete infrastructure tier (identity packs, appearance versions,
production graph, six model-native compilers, durable workloads, dispatch, reconciliation) but
stopped short of the user-facing workflow it was written to enable. The result is a Production
Studio in which **only the Composition stage does anything**:

| Stage | Designed in | Actual state |
|---|---|---|
| 1 Compose | `production-studio-image-workflow.md` | Implemented |
| 2 Identity | same, FR-055 | **Not wired.** `SceneImageProductionStage.Identity` appears nowhere in `DreamGenClone.Web` |
| 3 Edit / Finish | same, FR-054 | **Not wired.** Studio hard-codes "Unavailable…"; editing exists only as a disconnected page |
| 4 Review / Approve | same, FR-056/057 | Implemented, but flat — no lineage tree, compare, or branch |

Supporting evidence:

- `SceneImageProductionStage.Identity` has zero references in the web project.
- `IdentitySkipReason` / `SkippedByUser` appear in **no** Razor file, so FR-055's mandatory skip
  control was never surfaced.
- `IImageEditingClient.EditAsync` has **no reference-image parameter**, so "apply approved faces to
  an existing composition" is impossible today.
- The `+ Identity (one-pass)` button is identity-conditioned **text-to-image**, which
  `production-studio-image-workflow.md` Stage 2 and the Stage 2/3 handoff §4.1 explicitly forbid as
  a substitute for the identity stage.
- Phase 2 tasks P2-053, P2-054, P2-055, P2-056 and the entire release gate (K) remain open.

## Scope

This package closes the user-facing gap only. It does **not** re-model persistence — the domain,
repositories, and schema required already exist and are reused verbatim.

Delivers:

1. Stage 2 Identity as a real reference-conditioned source-image edit.
2. Stage 3 Finish as production-group-scoped edits with immutable child attempts.
3. Explicit persisted identity skip with reason.
4. Image management: branch-aware lineage tree, side-by-side compare, branch-from-any-attempt.
5. Retirement of the legacy one-off generation path.

## Documents

| File | Purpose |
|---|---|
| [spec.md](spec.md) | Goal, non-goals, user stories, functional requirements, acceptance scenarios, exit gate |
| [plan.md](plan.md) | Design decisions, architecture, file-level change map, risks |
| [ui-contract.md](ui-contract.md) | Layout regions, stage stepper, readiness panel, stage controls, attempt tree, compare mode, approval surface, states, accessibility, state keys |
| [tasks.md](tasks.md) | Ordered, checkable implementation tasks |
| [phase-2-closure-map.md](phase-2-closure-map.md) | Everything still standing between today and a cohesive Phase 3 start, and which part B-106 covers |

## Controlling upstream documents

- `specs/Planning/B-100-progressive-scene-beat-pipeline/spec.md` — User Story 10, FR-052 → FR-059
- `specs/Planning/B-100-progressive-scene-beat-pipeline/production-studio-image-workflow.md` — approved staged design
- `specs/Planning/B-100-progressive-scene-beat-pipeline/STUDIO-STAGE2-3-IDENTITY-FINISH-HANDOFF.md` — scoping input and hard constraints
- `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/spec.md` — FR2-016, FR2-017, FR2-028
- `.github/instructions/qwen-image-edit-2511.instructions.md` — edit mechanism rules
- `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` — compiler governance

## Relationship to the phase ladder

```
Phase 1  ── accepted-in-practice
Phase 1B ── acceptance OPEN (P1B-009/010, P1B-036…047)
Phase 2  ── infrastructure done; USER WORKFLOW OPEN  ◀── B-106 closes this
Phase 3  ── declares "Ready after the Phase 2 production exit gate" (not recorded)
```

Phase 3 must not start before the Phase 2 exit gate is recorded. B-106 is the shortest path to that
gate that also produces the capability the user actually asked for.
