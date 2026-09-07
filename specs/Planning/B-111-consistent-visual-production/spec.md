# B-111 — Consistent Visual Production Program: Specification

**State:** designed
**Created:** 2026-09-05
**Supersedes:** B-032 Phases 2–4, B-097, B-103, B-105, B-106, B-107, B-108, B-109, B-110, and the
B-102 remainder (see `superseded-map.md`)
**Evidence base:** `research/external-research-findings.md` (normative)
**Boundary:** B-100 (Beat/Moment production) and B-101 (Story Presentation) are **out of scope**;
B-111 declares a read contract to them and nothing more.

---

## 1. Why this program exists

Not because the previous plans were badly executed. Because of a repeatable structural defect:

| Defect | Evidence | Consequence |
|---|---|---|
| Plans specified **mechanisms**, not **outcomes** | B-032 Phase 2 shipped identity packs, repositories, compilers and a client; the Studio still exposes only Composition | "Complete" work produced no user-visible outcome → B-106 created to close the gap |
| Capabilities were **built but never activated** | 5 face views stored, `IdentityControlledRequestCompiler` always uses the one canonical face; `SceneImageIdentityMechanism.PuLid` coded and unreachable; LoRA complete through training dispatch, never activates in a render | Each dead capability became a new backlog item (B-107, B-109) |
| **Exit gates declared, then bypassed** | Phase 1B "blocks Phase 2"; Phase 2 partially implemented anyway. B-106's README says Phase 2 packs are "all implemented"; B-106's own `phase-2-closure-map.md` says Phase 2 is not accepted | Contradiction debt guarantees rework |
| **Two packages owning one concern with opposite rules** | B-106: "one immutable child attempt per Finish". B-110: in-panel iterate loop. B-106 forbids identity-conditioned T2I as the Identity stage; B-109 expands exactly that | Both "ready for implementation", mutually exclusive |
| The **floor was never poured** | The only real reference-creation path hard-requires a `CharacterLoraDataset`. No `LocationProfile` or wardrobe aggregate exists in code at all | Every consistency plan built on a foundation that isn't there |
| The **serverless constraint had no representation** | `RunPodServerlessImageClient`: one job, fixed 5s poll, timeout only. No warm-up, keep-alive, batching, cancel, health read or priority. `batch_size` hard-coded to 1 | A first-order constraint was absent from the design |

B-111 fixes the structure, not just the symptoms.

---

## 2. Outcomes (what "done" means)

These are the only statements that define success. Everything else is a means.

| # | Outcome | Measured by |
|---|---|---|
| **O1** | A wholly fictional character, a specific outfit, and a specific location can each be given an approved canonical reference **entirely inside the app**, with no source photograph and no LoRA training | P2 gate |
| **O2** | The same character's face is recognisably the same person across many images, including off-frontal angles | Identity metric on the golden set |
| **O3** | The same outfit stays the same outfit across every shot within a scene | Subject-fidelity metric on the wardrobe region |
| **O4** | The same location stays the same place across moments and across POVs of one moment | Subject-fidelity metric + geometry agreement |
| **O5** | Two characters in one frame each keep their own identity, including under contact and occlusion | Per-subject identity metric with region routing |
| **O6** | A staged batch of work costs **one** cold start per endpoint, and a one-off request stays interactive while a batch is running | P1 gate |
| **O7** | Adding or replacing a model, endpoint, or identity technique is a **data** change plus a qualification record — never a code change to callers | Contract conformance review |
| **O8** | No phase can close on "code complete". Every phase closes on measured evidence against a frozen golden set | Program governance |

---

## 3. Non-goals

- **Not** B-100 Beat/Moment semantics, and not B-101 presentation/VN playback.
- **Not** a change to roleplay continuation behaviour, prompts, or narrative engine.
- **Not** legacy compatibility. Per the standing roadmap policy: new sessions and new data are the
  baseline. No backfill, no dual read/write, no compatibility adapters. Old data fails with explicit
  create-new guidance.
- **Not** automated repair before automated validation is trustworthy (P6 ordering).
- **Not** video, audio, speech, music or lip-sync. Those remain separate epics consuming B-100.

---

## 4. Capability contracts (the stable seams)

Six seams. Everything else is an implementation behind one of them. **New techniques, models and
endpoints enter through these seams as data + a qualification record — never by editing callers.**

### C1 — Reference Library

The floor. A canonical, versioned, approved visual definition of a recurring entity.

| Requirement | Rule |
|---|---|
| **FR-C1-01** | Reference kinds are at minimum: `CharacterFace`, `CharacterBody`, `Wardrobe`, `Location`. Each is independently versioned and independently approvable. |
| **FR-C1-02** | Every reference carries a **mandatory frozen text block** alongside its image(s). A reference with images but no text block is invalid and cannot be approved. *(F4, F8.6 — garment/person text prompts materially improve fidelity; reference conditioning augments text, it does not replace it.)* |
| **FR-C1-03** | A `Location` reference is **plate + frozen text block**, and from P5 additionally **derived control map(s)**. A single picture is not a location. *(F3)* |
| **FR-C1-04** | A `CharacterFace` reference is a **view set** (front + off-frontal views), each view tagged with its angle, not a single canonical image. *(F1, F6)* |
| **FR-C1-05** | References are created by the Reference Bootstrap flow (C2) or by upload. Reference creation must **not** require a LoRA dataset, a training run, or any B-100 lineage. |
| **FR-C1-06** | Approval is explicit and immutable-by-version. Superseding creates a new version; it never mutates an approved one. |
| **FR-C1-07** | Wardrobe covers only "same outfit across shots". Putting a *specific garment* on a character (try-on) is a separate future capability and must not be conflated. *(F4)* |

### C2 — Reference Bootstrap

Candidate generation → curation → promotion. The workflow that makes C1 possible for fictional
entities.

| Requirement | Rule |
|---|---|
| **FR-C2-01** | From a text description alone, generate **N candidates** for a target (character face/body, wardrobe, location) and present them for side-by-side comparison. |
| **FR-C2-02** | Each candidate carries an explicit decision: `Undecided` / `Accepted` / `Rejected`, with optional notes. Decisions are persisted. |
| **FR-C2-03** | An accepted candidate can be **expanded** into additional views (angles for a face; alternate framings for a location) derived from that exact accepted image. |
| **FR-C2-04** | Exactly one candidate set is **promoted** to become the canonical reference version in C1. Promotion is explicit and recorded. |
| **FR-C2-05** | Candidate batches are staged into a Run (C4). Bootstrap is the Run engine's first real consumer. |
| **FR-C2-06** | No step may require a `CharacterLoraDataset`. Enforced by test. |

### C3 — Identity Strategy

A pluggable technique portfolio, shaped exactly like the existing `ImageProtocol` dispatcher.

| Requirement | Rule |
|---|---|
| **FR-C3-01** | Strategies are at minimum: `TextOnly`, `ReferenceEdit`, `IpAdapter`, `PuLid`, `IpAdapterFaceId`, `InstantId`, `Lora`. The set is open for extension without changing callers. *(F1)* |
| **FR-C3-02** | A strategy is usable only when a **qualification cell** exists for the exact `(strategy, model, endpoint, strength)` tuple, recorded as data with its scorecard. |
| **FR-C3-03** | Selecting an unqualified cell **fails fast with explicit diagnostics**. No strategy may fall back to another. No convenience path may substitute one for another. |
| **FR-C3-04** | The contract distinguishes **unqualified** (could work, not yet proven) from **structurally impossible** (the provider class cannot host the mechanism at all). *(F7.0)* |
| **FR-C3-05** | Reference **view selection is angle-aware**: the view nearest the target head angle is chosen. Applies automatically to every strategy that consumes a reference view. *(F1, F6)* |
| **FR-C3-06** | Multi-character identity uses **per-character spatial routing**, with regions **derived from the composition**, not hand-entered. *(F2 — UniPortrait ID routing, OMG layout-then-blend)* |
| **FR-C3-07** | Identity strength is a qualified parameter of the cell, never a free-floating tuning knob, because raising it trades away prompt adherence. *(F6)* |

### C4 — Production Run

The unit of work. **A set, not an image.** *(F8.1)*

| Requirement | Rule |
|---|---|
| **FR-C4-01** | A Run is a staged set of requests with a lifecycle: `Staged → Warming → Draining → Complete/Aborted`. |
| **FR-C4-02** | Runs group work by **(endpoint, provider class)**, because cold-start cost is per endpoint. *(F7.7.9)* |
| **FR-C4-03** | Execution policy is **per provider class**, expressed as data: warm-window + drain for self-built ComfyUI serverless; concurrency + backoff + cost accounting for hosted APIs; liveness for dedicated pods. *(F7.0)* |
| **FR-C4-04** | Warm-up strategy per endpoint is configurable data (sentinel job, or temporarily raise active workers), never hard-coded. *(F7.7.2)* |
| **FR-C4-05** | A one-off request submitted while a Run is draining must remain **interactive**: Run jobs carry the provider's low-priority flag, the one-off does not. *(F7.4)* |
| **FR-C4-06** | Per-request `executionTimeout` and `ttl` are derived from the work; `ttl` must cover expected **queue** time, not just execution. *(F7.4)* |
| **FR-C4-07** | Abort is real: cancel in-flight jobs and purge the queue, respecting documented rate limits. |
| **FR-C4-08** | Endpoint state (`cold` / `warming` / `warm`) is **read from the provider's health surface**, never guessed. *(F7.4)* |
| **FR-C4-09** | Candidate count within one job (`batch_size`) is a first-class request parameter where the provider class supports it — N candidates for one cold start and one model load. *(F7.7.8)* |
| **FR-C4-10** | The Run is also the **consistency** unit: batch-scoped techniques that require images generated together attach here, not to individual requests. *(F1.2, F8.2)* |
| **FR-C4-11** | Providers with documented idle scale-down must have that hazard **surfaced in the UI**, not rediscovered as a mystery outage. *(F7.6 — 3 days → max workers 2; 7 days → 0, and it stays there)* |
| **FR-C4-12** | **Refusal is a first-class execution outcome**, distinct from a generic failure and from success. Detect the deterministic modes inline — explicit refusal/policy error and empty output. **Silent sanitisation** (an altered image returned as success) is caught at the P4 scoring gate (the scorer runs there), not inline in the render hot path. Each detected refusal is recorded against the `(model, endpoint)` with its mode. *(F8.8)* |
| **FR-C4-13** | On refusal, the render is a **clear recorded FAILURE surfaced to the user** with the model, endpoint, and refusal mode. The **user then manually selects a different model and retries.** The app performs **NO automatic model substitution, no ordered-list advance, no strategy change** — it never picks a model on the user's behalf (user decision 2026-09-05). This is stronger than the no-fallback rule: not even a configured list auto-advances. |
| **FR-C4-14** | Refusal evidence is **recorded against the `(model, endpoint)`** as an audit/learned signal that MAY inform the UI (e.g. a hint that this model has refused this content before). It does **not** auto-exclude or auto-select any model — selection stays with the user. *(F8.8)* |

### C5 — Geometry Control

Spatial conditioning. A **separate channel** from identity.

| Requirement | Rule |
|---|---|
| **FR-C5-01** | Control maps (depth / pose / segmentation / edges) are derived from a **canonical source** and are versioned artefacts, not per-shot re-inventions. *(F3)* |
| **FR-C5-02** | Multiple POVs of one moment derive their control maps from **one shared source**. *(F3)* |
| **FR-C5-03** | Geometry and identity must never be traded against each other. Blocking problems are never "fixed" with identity strength; identity problems are never "fixed" with prompt wording. *(F3, F8.3)* |
| **FR-C5-04** | Multiple simultaneous conditions are supported, because the mechanism supports them. *(F3)* |

### C6 — Consistency Scorer

The yardstick. Without it, no phase can close.

| Requirement | Rule |
|---|---|
| **FR-C6-01** | Scoring reports a **triple**, always together: identity fidelity, prompt adherence, and diversity/layout. Reporting identity alone is forbidden — it drives conditioning strength up until every render is a re-pose of the reference. *(F6)* |
| **FR-C6-02** | Identity is measured as face-embedding cosine similarity on a **detected and aligned face crop**, not on the whole image. *(F6, ArcFace-class)* |
| **FR-C6-03** | Non-face subject fidelity uses **DINO** and CLIP-I. DINO is authoritative where they disagree, because CLIP-I forgives "a different person of the same type" — the exact B-103 failure. *(F6)* |
| **FR-C6-04** | A **subject-presence check** verifies the expected number of subjects are actually rendered and detectable. This alone would have caught the B-103 dropped-character failure that eyeballing missed. *(F6)* |
| **FR-C6-05** | **Multi-turn stability** is scored separately from single-turn quality: an edit chain is scored at every step, not only at the end. *(F5)* |
| **FR-C6-06** | Thresholds are **calibrated on this project's golden set** per `(model, strategy, strength)` and stored as data. No threshold is copied from a paper and none is hard-coded. *(F6)* Where the scorer and the human verdict disagree, the **human verdict stands** and the threshold is recalibrated. *(P8)* |
| **FR-C6-07** | The scorer is a git-tracked, pinned tool at `tools/consistency-scoring/` writing to git-ignored output, per this repo's `tools/` policy. |
| **FR-C6-08** | The scorer provides the **silent-sanitisation check** that FR-C4-12 depends on: an image returned as success but altered to avoid the requested content must be detected, because it is otherwise invisible and silently corrupts every scorecard it enters. *(F8.8)* |

### C7 — UI & Interaction

How every surface is built. Detailed in `ui-contract.md`; summarized here as binding requirements.
The two prior failures this contract prevents are the opaque Studio (B-103) and the nine-dial
continuation popup (B-089).

| Requirement | Rule |
|---|---|
| **FR-C7-01** | **One screen, one decision.** Each view has a single nameable job; unrelated jobs are separate screens. No mega-form that crams multiple workflows into tabs of one page. *(U1, U5)* |
| **FR-C7-02** | **Reuse, never duplicate a workflow.** A workflow appearing in more than one place is one shared component instance with a typed input contract — the direct fix for B-110's triplicated edit loop. *(U2)* |
| **FR-C7-03** | **Progressive disclosure.** Only what is needed to act is shown; advanced params, provenance, raw payloads and score breakdowns are behind expanders collapsed by default. *(U3)* |
| **FR-C7-04** | **Respect data volume.** No unbounded list — paginate or virtualize. No large blob inlined — full prompts / payloads / debug JSON open on demand with a size indicator. Thumbnails first, full-res on open. *(U4)* |
| **FR-C7-05** | **Honest, vocabulary-matched state.** Every backend state (`cold`/`warming`/`warm`, `staged`/`draining`, `qualified`/`unqualified`/`structurally-impossible`, `approved`/`draft`/`identity-stale`) is shown using the backend's own words — never hidden behind a generic spinner or a flat "unavailable". *(U5)* |
| **FR-C7-06** | **No black-box actions.** Every generate/edit action exposes, on demand, the exact resolved inputs (prompt, negative, seed, model, provider, class, strategy, size, steps) — a mandatory "view what will be submitted" affordance. *(U6)* |
| **FR-C7-07** | **Flows preserve state.** Multi-step flows connect small forms with Back/Next that preserve work across steps and across navigation away and back. *(U7)* |
| **FR-C7-08** | **No frozen UI under load.** A running Run or generating batch never blocks the page; the user can act elsewhere and start priority one-offs while work drains. *(U8, mirrors FR-C4-05)* |
| **FR-C7-09** | **Costly/destructive actions are explicit** — abort, supersede, delete-branch, trigger paid warm window all confirm and state the consequence/cost. *(U9)* |

---

## 5. Cross-cutting principles

### P1 — Consistency is a property of a set
Every mechanism that works holds something constant across a group. The unit of work is a Run.
*(F8.1)*

### P2 — The cost architecture and the quality architecture are the same architecture
Batching for cold-start economics and batching for visual consistency are one mechanism. Build once.
*(F1.2, F8.2)*

### P3 — Provider class is a capability ceiling
Hosted inference APIs have no graph: no IP-Adapter, PuLID, InstantID, ControlNet, LoRA or
`batch_size`. Text-only is their identity ceiling. Whole rows of the qualification matrix are
structurally impossible, not merely unqualified. *(F7.0)*

### P4 — Compose classes across stages; never substitute one for another
A hosted API cannot apply identity, **but the image it produces can be edited afterwards on a
ComfyUI endpoint to apply identity.** Each stage may target a different provider class; the per-stage
target is persisted and visible. This is composition, not fallback, so it satisfies the no-fallback
rule — and it minimises cold-start exposure to only the stages that genuinely need a graph.
*(F7.0.3a — user decision 2026-09-05)*

### P5 — Content capability is a model property, not a pipeline mode
There is **one** production flow and **one** golden set, and **every golden-set case is explicit**.
A model either supports the content or it does not. No parallel "safe path", no SFW variant of a
case, no separate acceptance set. *(F8.8 — user decision 2026-09-05)*

**Explicitness/rating is content-driven** (user decision 2026-09-05): the `rating_*` (Pony) /
explicitness (SDXL) of a render reflects **what the image actually depicts**, chosen by the model
from the scene — it is **NOT** tied to narrative phase and **NOT** a per-render user toggle. All
phases can contain explicit acts; an explicit act in BuildUp is still explicit. Consequently:
the `AllowExplicitImage` toggle is **removed**; `ResolveRatingTag(NarrativePhase)` /
`ResolveRatingTag(ImageContentPolicy)` phase/policy rating is **removed**; the LLM prompt-builder
instructs the model to pick the rating from the depicted content (explicit → rating_explicit,
suggestive/nudity → rating_questionable, non-sexual → rating_safe).

A **refusal is a measurement, not an error to route around**: it is recorded as a failure against
that `(model, endpoint)` cell and **surfaced clearly to the user**, who then **manually picks a
different model and retries**. The app performs **no automatic model substitution and no ordered-list
advance** — it never selects a model on the user's behalf (user decision 2026-09-05). This is stricter
than the no-fallback rule and keeps model choice fully with the user.

> **Decided, replacing the earlier open question:** the deterministic SFW clamp
> (`SfwFiltered` → model-family `SfwClampSuffix` in `SceneImageRenderingJobHandler`, plus the
> `ResolveRatingTag(phase, policy)` safe-rating path and the `BuildSystemPrompt` SFW branch in the
> Pony/SDXL builders) is the second flow this principle forbids and is **removed**. But removal is
> sequenced deliberately: **P0 documents and freezes its exact footprint; P1 removes it coupled with
> the refusal-outcome model (FR-C4-12→FR-C4-14) that replaces it.** Removing a content-safety
> behaviour before its replacement exists would violate G2 in reverse. Note that the underlying
> `ImageContentPolicy` enum is retained — it also carries genuine *provider capability* (adult-allowed
> vs filtered), which the refusal model consumes; only the clamp *behaviour* is removed, not the
> capability signal.

### P8 — The human verdict is authoritative
The automated scorer is **advisory and regression-detecting**, not the judge. Its value is catching
drift cheaply, making invisible failures visible (the dropped-character case), and preventing the
identity-strength trap. Where scorer and human disagree, **the human verdict stands and the
threshold is recalibrated as data** — the metric is corrected to match observed judgement, never the
reverse. *(F8.9 — user decision 2026-09-05)* The verdict covers **usability**, not only visual
quality: a correct backend behind a cramped or opaque UI is not done.

### P9 — Many small forms that flow, never one screen that crams
Every surface is a small, single-purpose form composed from reusable components and connected by an
explicit flow. Data volume is designed for (pagination, virtualization, progressive disclosure,
load-on-demand), not assumed away. This is contract C7 (`ui-contract.md`), and it is a **gate
condition**, not a finishing touch — the opaque Studio (B-103) and the nine-dial popup (B-089) are the
failures it exists to prevent. *(user decision 2026-09-05)*

### P6 — Edit vs regenerate is a rule, not a preference
If the composition is approved, **edit**. If the composition is wrong, **regenerate**. Never repair
composition with an edit; never repair identity with a re-roll. A geometry-changing edit necessarily
invalidates a previously applied identity — this is a physical consequence of the mechanism, which is
why a `Cosmetic` / `Geometry` change class is evidence-backed rather than bureaucratic. *(F5)*

### P7 — Text never stops mattering
Reference conditioning augments the frozen text block; it never replaces it. *(F8.6)*

---

## 6. Governance — the rules that stop fix-plan chains

These exist specifically to prevent the six defects in §1 from recurring. They are binding on every
phase of this program.

| # | Rule |
|---|---|
| **G1** | **No phase closes on "code complete".** A phase closes only on a recorded scorecard against the frozen golden set, **plus your manual sign-off, which is the authoritative verdict** (P8). Build-green and tests-green are necessary, never sufficient. |
| **G2** | **No capability may be built without its activation path in the same phase.** If a phase adds reference views, that phase must also select them. If it adds a strategy enum value, that phase must qualify and expose it, or must not add it. This is the direct fix for the dead-capability defect. |
| **G3** | **No downstream phase starts before its prerequisite gate is recorded as passed** — or a scoped waiver is written down with the risk named. Silent bypass is what created the Phase 1B/Phase 2 contradiction. |
| **G4** | **One concern, one owner.** Any new concern must be assigned to exactly one contract in §4 before implementation. If two phases appear to own it, the program spec is amended first. |
| **G5** | **Every technique/model/endpoint is data plus a qualification record.** If a change requires editing a caller, the seam is wrong — fix the seam, not the caller. |
| **G6** | **Every requirement traces to a finding** in `research/external-research-findings.md`, or to a recorded user decision. "It looked better in one render" is not evidence and cannot justify a change. |
| **G7** | **Failures are reported as findings, never repaired silently.** Missing configuration, unqualified cells and structurally impossible combinations all fail fast with explicit diagnostics, per this repo's standing no-fallback rules. || **G8** | **UI is a gate condition, not a follow-up.** A phase that ships a surface violating C7 (`ui-contract.md`) has not met its gate, even with a correct backend. Every phase's UI gate checklist (ui-contract §4) must pass, including your manual usability verdict (P8/P9). |
---

## 7. Acceptance scenarios

| # | Scenario | Passes when |
|---|---|---|
| **A1** | Create a brand-new fictional character from a text description only | A candidate batch is generated, curated, expanded to a full view set, and promoted to an approved reference — with no LoRA dataset and no uploaded photo |
| **A2** | Create a location and an outfit the same way | Both reach approved canonical references, each with a mandatory frozen text block |
| **A3** | Stage 8 images against a cold self-built endpoint and run | Exactly one cold start is paid; all 8 complete; the UI showed honest cold/warming/warm state throughout |
| **A4** | Submit a one-off while that Run is draining | The one-off returns without waiting for the Run to finish |
| **A5** | Abort a draining Run | In-flight jobs cancel, queued jobs purge, no orphaned work remains |
| **A6** | Request an identity strategy on an endpoint whose class cannot host it | Fails fast, names the reason as *structurally impossible* (not merely unqualified), and offers the cross-class staged alternative — never silently downgrades |
| **A7** | Render the same character across 12 golden prompts | Identity metric passes its calibrated threshold **and** prompt-adherence and diversity metrics stay within bounds |
| **A8** | Render the same character at off-frontal angles | Angle-aware view selection measurably improves identity over always using the frontal view |
| **A9** | Render two characters in contact/occlusion | Each subject independently passes its own identity threshold; no identity bleed |
| **A10** | Render one moment from three POVs | Location and identity hold; geometry agrees across the three |
| **A11** | Run a 5-step edit chain | Multi-turn stability is scored at every step; degradation is visible rather than hidden |
| **A12** | Apply a geometry-class edit to an identity-applied image | The image is marked identity-stale and blocked from approval while identity is required |
| **A13** | Add a new serverless endpoint | Data change only: a provider row plus a qualification record. Zero code changes to callers |
| **A14** | Leave an endpoint idle past the provider's scale-down window | The app surfaces the degraded max-worker state instead of queueing forever |
| **A15** | A model refuses (explicit refusal or empty output) on an explicit case | Recorded as a **failure** against that `(model, endpoint)` with the refusal mode, and **surfaced clearly** so the user can pick a different model and retry; the app does **not** auto-substitute |
| **A16** | A model **silently sanitises** — returns a clothed/cropped/altered image as success | Caught at the **P4 scoring gate** by the scorer's sanitisation check and excluded from the scorecard; not counted as a passing render (not detected inline in P1) |
| **A17** | A refusal occurs | It is **never** a silent success — always a clear, recorded, user-visible failure; model choice remains with the user |
| **A18** | Open the Runs screen with dozens of jobs across several runs | Summary-first, virtualized/paginated, live state per the backend vocabulary; no unbounded render, no frozen page (U4/U5/U8) |
| **A19** | Invoke image editing from the roleplay editor, the Studio Identity stage, and the Asset Manager | All three use the **same** `EditIterateWorkbench` component — grep proof of no duplicated loop (U2) |
| **A20** | Trigger any generate/edit action | A "view what will be submitted" affordance shows the exact prompt/negative/seed/model/provider/strategy before or after submission (U6) |
| **A21** | Leave a multi-step Bootstrap flow midway and return | Curation decisions and the draft are preserved; no work lost (U7) |

---

## 8. Read contract to B-100 / B-101

B-111 consumes and produces; it does not reinterpret story semantics.

- **Consumes from B-100 (when it exists):** the frozen visual state of a Moment — identity
  references, location, wardrobe, props, composition intent. Until B-100 exists, B-111 consumes the
  current render brief. The seam is the same either way.
- **Produces for B-101:** approved, immutable, versioned image assets with full lineage. B-101 may
  place only an exact approved version.
- **B-111 never** derives beats, dialogue, action arcs, or story facts from roleplay prose.
