# B-111 — Consistent Visual Production Program

**State:** implemented — production-surface scope completed 2026-09-06
**Created:** 2026-09-05
**Scope:** image generation + editing, visual consistency (character / wardrobe / location),
reference bootstrap, and provider-class execution including serverless cold-start management.
**Out of scope:** B-100 (Beat/Moment production) and B-101 (Story Presentation) — read contract only.

> **Completion boundary (2026-09-06):** B-111 delivers the integrated production surfaces, durable
> lifecycle/provenance, reusable controls, and Model Manager strategy qualification boundary. Graph
> techniques and scoring infrastructure without a real provider endpoint, executable workflow, and
> passing evidence are retained as unavailable activation seams. They are not silently substituted
> with `TextOnly`, and their eventual delivery is separate endpoint/evaluator integration work.

---

## Read in this order

| # | Document | What it is |
|---|---|---|
| 1 | [`research/external-research-findings.md`](research/external-research-findings.md) | **Normative evidence base.** 19 primary sources. Every requirement traces here. |
| 2 | [`spec.md`](spec.md) | Outcomes, the **seven** capability contracts, cross-cutting principles, and the governance rules |
| 3 | [`ui-contract.md`](ui-contract.md) | **Contract C7** — UI/interaction: many small reusable forms that flow, data-volume discipline, no black boxes. A gate condition, not a finishing touch |
| 4 | [`plan.md`](plan.md) | Seven phases, each with scope, a backend exit gate, and a UI gate |
| 5 | [`tasks/README.md`](tasks/README.md) | Task decomposition index + the coordinator/implementer dispatch protocol |
| 6 | [`superseded-map.md`](superseded-map.md) | Where each consolidated package went, and the conflicts resolved |

---

## The problem this program solves

Previous plans were implemented faithfully and still did not produce the wanted outcome, so each one
spawned follow-up plans. The cause was structural, not a discipline failure:

- Plans specified **mechanisms**, not outcomes — so work completed while nothing became true.
- Capabilities were **built and never activated** (5 face views stored, 1 used; PuLID coded and
  unreachable; LoRA trained and never applied).
- **Exit gates were declared then bypassed**, creating contradiction debt.
- **Two packages owned one concern** with opposite rules, both marked ready.
- The **floor was never poured** — nothing could create a reference for a fictional character.
- The **serverless cold-start constraint had no representation** in any design.

## The three findings that reshaped the plan

1. **Consistency is a property of a *set*, not an image.** Every working mechanism holds something
   constant across a group. The unit of work must be a Run.
2. **The cost architecture and the quality architecture are the same architecture.** Batching for
   cold-start economics and batching for consistency are one mechanism — build it once.
3. **Multi-character bleed is solved by spatial *routing*, not strength tuning** — independent
   evidence for Composition-first / Identity-second, with regions derived from the composition.

## Two decisions that shaped it further

- **Provider class is a capability ceiling.** Hosted inference APIs have no graph, so IP-Adapter,
  PuLID, InstantID, ControlNet, LoRA and `batch_size` are *structurally impossible* there — not
  merely unqualified. Only self-built ComfyUI serverless endpoints have cold starts.
- **Compose classes across stages; never substitute.** An API cannot apply identity, but the image
  it produces can be edited afterwards on a ComfyUI endpoint to apply it. So Composition can be fast
  and cheap while only Identity/Finish pay for a warm window. That is composition, not fallback.

---

## Outcomes

| # | Outcome |
|---|---|
| O1 | A fictional character, outfit and location each get an approved canonical reference **entirely in-app** — no photo, no LoRA |
| O2 | Same face across many images, including off-frontal angles |
| O3 | Same outfit across every shot in a scene |
| O4 | Same location across moments and across POVs |
| O5 | Two characters in frame each keep their own identity, under contact and occlusion |
| O6 | A staged batch costs **one** cold start per endpoint; one-offs stay interactive during a batch |
| O7 | Adding a model / endpoint / technique is a **data** change plus a qualification record |
| O8 | No phase closes on "code complete" — only on measured evidence |

---

## Phases

| Phase | Absorbs | Outcome gate |
|---|---|---|
| **P0** Baseline and hygiene | — | Golden set + scorer exist; baseline recorded; conflicts resolved |
| **P1** Run engine, provider-class execution | B-102 remainder | 8-image run = one cold start; one-off stays interactive; abort is clean |
| **P2** Reference Bootstrap | B-108 | Fictional character + outfit + location reach approved references, no LoRA, no photo |
| **P3** Identity strategy contract and activation | B-107, B-109, B-032 P2 | ≥2 strategies qualified; unqualified fails fast; angle-aware selection measurably helps; no bleed |
| **P4** Staged Studio | B-103, B-105, B-106, B-110 | **O2 + O3 + O4 met**, with adherence and diversity in bounds |
| **P5** Location geometry and multi-POV | B-097, B-032 P3 | Three POVs share geometry and pass scoring |
| **P6** Validation and bounded repair | B-032 P4 | Automated scoring in-pipeline; approved-frame gating |

Order confirmed 2026-09-05: the Run engine lands **before** Reference Bootstrap, so Bootstrap becomes
its first real consumer and validates it.

---

## Governance (binding on every phase)

| # | Rule |
|---|---|
| G1 | No phase closes on "code complete" — only on a recorded scorecard plus manual sign-off |
| G2 | No capability may be built without its activation path **in the same phase** |
| G3 | No phase starts before its prerequisite gate is recorded — or a written waiver names the risk |
| G4 | One concern, one contract owner — amend the spec before two phases claim it |
| G5 | Every technique / model / endpoint is data + a qualification record; if a change needs a caller edit, the seam is wrong |
| G6 | Every requirement traces to a research finding or a recorded decision. "It looked better in one render" is not evidence |
| G7 | Failures are reported as findings, never repaired silently — consistent with this repo's no-fallback rules |
| G8 | **UI is a gate condition, not a follow-up** — a phase that ships a cramped or opaque surface (violating C7) has not met its gate, even with a correct backend |

---

## Standing constraints inherited from the repo

- **No fallbacks.** Missing configuration, unqualified cells and structurally-impossible combinations
  fail fast with explicit diagnostics.
- **New data is the baseline.** No backfill, no dual read/write, no compatibility adapters.
- **All tests pass on every implementation change.**
- **RunPod changes must be recorded** in the pod registry and be reproducible from scratch and
  restart-proof.
- **Tools live in `tools/`**, git-tracked and pinned, writing to git-ignored output.
