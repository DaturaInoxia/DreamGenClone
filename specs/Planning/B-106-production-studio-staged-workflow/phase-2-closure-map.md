# Phase 2 Closure And Phase 3 Readiness Map

**Purpose:** state exactly what remains between today and a cohesive start on B-032 Phase 3, and
which part of it B-106 covers. B-106 is necessary but **not sufficient** to close Phase 2.

**Verified:** 2026-09-05 against the live task ledgers. B-106 Group A implementation is complete;
the final provider-backed acceptance run and user approval remain intentionally open.

## 1. Where the blockage actually is

Phase 3 declares two gates it does not control:

- `phase-3-location-and-multi-pov/spec.md` — *"Status: Ready after the Phase 2 production exit gate"*
  and *"Depends on: Qualified Phase 2 cells, approved character assets, B-100 frozen Moments"*.
- `phase-3-location-and-multi-pov/tasks.md` — *"Prerequisite: Phase 2 exit gate is recorded."*

That exit gate is P2-059, which is open. Behind it sit 13 open Phase 2 tasks and, upstream of those,
14 open Phase 1B tasks that Phase 1B's own README declares as blocking Phase 2.

## 2. Open work, grouped by what closes it

### Group A — closed by B-106 (user-facing workflow)

| Task | Status | Title | B-106 coverage |
|---|---|---|---|
| P2-053 | Complete | Production Studio context rail / media pool / canvas-inspector / attempt strip / queue workspace with stable switching | Sections B, D, E + `ui-contract.md` |
| P2-054 | Complete | Semantic intent editing, reference-role selection, prepare/submit/cancel/retry/review/approve, exact request inspection | Sections B, C, D, E |
| P2-055 | Complete | Remove the old one-off generation action; no fallback | Section F |
| P2-056 | Complete through approval boundary | Service/component tests, Razor diagnostics, accessibility, Playwright desktop/mobile | Section G (extended below) |

### Group B — NOT covered by B-106; qualification and evidence work

These are execution/evidence tasks, not UI. They need their own effort after or alongside B-106.

| Task | Title | Why it is still open |
|---|---|---|
| P2-029 | Execute the application path against all frozen identity cases; compare provenance to the standalone proof | Never run through the app |
| P2-032 | Affected tests, build, full suite, record the manual exit gate for section F | Not recorded |
| P2-044 | Freeze and execute composition-first Qwen Edit / FLUX identity matrices against the failed angled cells | Qwen ran 6/6 on 2026-09-02, but per-cell gate review and the FLUX matrix are outstanding |
| P2-045 | Record matrix outcomes per exact identity strategy and capability cell | Depends on P2-044 |

**Deferred out of this group (user scope amendment, 2026-09-04):** P2-068 and P2-069 — LoRA
qualification and its validation run. The LoRA subsystem stays build-complete but inactive; its
activation is tracked as **B-107** and is out of scope for Phase 2 and Phase 3. See
§6 for the exact standing contract.

### Group C — the release gate itself

| Task | Title | Precondition |
|---|---|---|
| P2-057 | Run all historical and new qualification cells through the application path without reclassifying failures | Groups A and B |
| P2-058 | Tests, build, full suite, Razor diagnostics, provider smoke tests, restart recovery, security/retention | Groups A and B. Partially recorded 2026-09-03 (build green, 1742/1742 suite) but provider smoke and security/retention checks are explicitly not claimed |
| P2-059 | Record the Phase 2 release decision, qualified/rejected cells, residual risks, cost observations, and Phase 3 handoff | P2-057 and P2-058 |

### Group D — upstream Phase 1B dependency

`phase-1b-vision-aware-image-editing/README.md` states **"Blocks: Phase 2 character identity"**.
Fourteen tasks remain open: P1B-009, P1B-010, and P1B-036 through P1B-047 — corpus quality/latency
acceptance, deployment manifests, the Qwen VL pod, endpoint configuration, per-capability smoke
tests, staged cutover, application corpus comparison, the six accepted intents end to end, the adult
analysis decision, single-source verification, and the exit run.

Phase 2 was started before this gate. The roadmap already records that as
*"Partially implemented before prerequisite exit."* It must be resolved or formally waived with a
recorded decision — not silently ignored.

## 3. Recommended sequence

```
1. B-106                      ← user-facing workflow; makes the product usable
2. Group B (P2-029/032/044/045)          ← qualification + evidence
3. Group D decision           ← close Phase 1B, or record an explicit scoped waiver
4. Group C (P2-057 → P2-059)  ← Phase 2 release gate
5. B-032 Phase 3              ← now legitimately unblocked
```

Steps 1 and 2 can overlap: B-106 is UI/mechanism work, Group B is qualification work, and they touch
different surfaces. Step 4 must not begin until 1, 2, and 3 are done.

## 4. Phase 3 scope advice

Once the gate is recorded, Phase 3 should be re-scoped before execution rather than run as written:

- **P3-012 → P3-018 (Three.js blocking editor)** is the single largest item in the phase and is not
  required for the workflows the user has asked for. Recommend splitting it into its own package and
  deferring it behind the location-continuity slice.
- **P3-001 → P3-011 (location profiles and canonical visual plans)** deliver the reusable continuity
  the user does want and should lead the phase.
- Phase 3 sections G and H repeat the same UI-last structure that caused this situation. Recommend
  applying the B-106 lesson: land at least one usable end-to-end user path per section instead of
  completing an entire infrastructure tier before any UI.

## 5. Honest answer to "is Phase 2 done after B-106?"

No. After B-106 the **product** is usable and the workflow gap is closed, but Phase 2 is not
*accepted*: Groups B, C, and D remain. B-106 task B106-040 already forbids marking Phase 2 accepted
on B-106 completion alone.

## 6. LoRA standing contract (scope amendment, 2026-09-04)

User decision: **LoRA must be ready to implement, but full integration is out of scope for Phase 2
and Phase 3.**

What that means concretely:

- **Built and staying built.** P2-060 → P2-067 are complete: domain records, repositories and
  additive schema, qualified training profiles, identity-seed and coverage-batch generation,
  curation and dataset freeze, durable training dispatch/reconciliation/artifact registration, and
  per-request `ReferenceConditioning` / `Lora` / `Combined` strategy bindings. None of this is
  removed, reverted, or hidden.
- **Not activated.** P2-068 (cell qualification) and P2-069 (its validation run) are deferred to
  **B-107** and no longer gate P2-057 → P2-059.
- **Fails closed, never falls back.** No qualified LoRA cell is exposed, so selecting a `Lora` or
  `Combined` strategy must fail explicitly as unqualified configuration. FR2-053 already forbids one
  strategy falling back to another, and `ProductionMediaCompilationService` already validates the
  exact qualified cell before persistence. Do not add a convenience path that silently substitutes
  reference conditioning.
- **Reference conditioning remains the only active identity strategy** through Phase 2 and Phase 3,
  subject to its documented near-frontal guardrail.
- **Phase 3 inherits the exclusion.** Its non-goals now name LoRA explicitly, so no Phase 3 control,
  compiler, or capability tuple may depend on a LoRA artifact.

Activation work, when it happens, is B-107: qualify LoRA-only and Combined cells at explicit
artifact versions and strengths against frozen prompts, seeds, held-out compositions, and
leakage/diversity gates, using a real fictional character. Qualification rows are never seeded or
fabricated.
