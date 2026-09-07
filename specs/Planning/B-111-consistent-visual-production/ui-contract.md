# B-111 — UI & Interaction Contract (C7)

**State:** normative for every phase that ships UI.
**Rationale:** two prior features in this repo failed on exactly this axis — the Production Studio
Composition stage became an opaque black box (B-103), and the continuation-settings popup grew to
nine interacting dials before B-089 had to collapse it. This contract exists so B-111's surfaces do
not repeat that. **A phase that ships a cramped or opaque UI has not met its gate, even if the
backend is correct.**

> **One-line rule:** *many small forms that flow together and are reused — never one long screen that
> crams every workflow into itself.*

---

## 1. Principles (binding)

### U1 — One screen, one decision
Each view asks the user for **one coherent decision or task**, then hands off. If a screen is doing
two unrelated jobs, it is two screens. The test: can you name the screen's job in five words? If not,
split it.

### U2 — Compose from reusable components, never duplicate a workflow
A workflow that appears in more than one place (describe → compile → run → before/after → iterate) is
**one component instance**, not three re-implementations. This is the direct lesson of B-110: the
edit loop existed three times, each slightly worse. New surfaces reuse the shared component or the
component contract is wrong.

### U3 — Progressive disclosure by default
Show the few things needed to act. Everything else — advanced parameters, provenance, raw payloads,
score breakdowns — lives behind an explicit expander, **collapsed by default**. The adaptive-panel
convention already in this repo (collapsible blocks, collapsed by default) is the standard. Power is
available; it is not in the way.

### U4 — Respect data volume
These surfaces consume large data: many candidates, many attempts, long lineages, full prompts of
600 KB+, run queues of dozens of jobs. Therefore:
- **Never render an unbounded list.** Paginate or virtualize. A gallery of 200 renders loads in
  pages, not all at once.
- **Never inline a large blob.** Full prompts / submitted payloads / debug JSON open in a panel or
  modal on demand, with a size indicator — never dumped into the main flow.
- **Thumbnails first, full-res on demand.** Grids use thumbnails; the full image loads when opened.
- **Summarize, then drill down.** A run shows counts and status; the per-job detail is one click in.

### U5 — Make state honest and visible
Cold / warming / warm, staged / draining / complete, qualified / unqualified / structurally-
impossible, approved / draft / stale — every state the backend distinguishes is **shown**, with the
same words the backend uses. No spinner that hides a cold start; no silent "unavailable".

### U6 — Show the truth of what will run
Where a surface triggers generation or editing, the user can see (on demand, per U3) the exact
resolved inputs: prompt, negative, seed, model, provider, class, strategy, size, steps. The
black-box failure (B-103) is forbidden by contract. A **"view what will be submitted"** affordance is
mandatory on any generate/edit action.

### U7 — Flow, don't trap
Small forms connect in an explicit sequence with visible progress and **Back/Next that preserve
state**. A user can leave a multi-step flow and return without losing work. No step silently discards
what a previous step produced.

### U8 — One job in flight ≠ frozen UI
Long operations (a Run draining, a candidate batch generating) never block the whole page. The user
can start a one-off, inspect other data, or queue more work while a batch runs (this mirrors the
backend priority-lane guarantee, FR-C4-05).

### U9 — Destructive and costly actions are explicit
Aborting a Run, superseding an approved reference, deleting a branch, or triggering a paid warm
window asks for confirmation and states the consequence and (where knowable) the cost.

---

## 2. Shared component library

These are built **once** and reused. Each is a self-contained, testable component with a typed input
contract. No surface re-implements one.

| Component | Job (U1) | Reused by | Notes |
|---|---|---|---|
| **`EditIterateWorkbench`** | describe → compile → editable prompt → run → before/after → iterate | Roleplay image editor, Studio Identity stage, Studio Finish stage, Asset Manager edit | The B-110 unification. Compiles against raw bytes + intent; each surface supplies a thin storage adapter (spec decision, B-110 open-decision b). Multi-turn score shown per step (FR-C6-05). |
| **`CandidateGrid`** | show N candidates, curate accept/reject, compare side-by-side | Reference Bootstrap, Studio candidate review, validation review (P6) | Virtualized (U4). Thumbnails; full-res on open. Decision persisted per candidate. |
| **`RunTray`** | show a run's staged/active/finished jobs with counts + per-job drill-down | Global Runs page, and a compact embedded form in Studio / Bootstrap / Asset Manager | Summary-first (U4). Live status (U5). Abort with confirm (U9). |
| **`ResolvedRenderBadge`** | show model · provider · class · size · steps/CFG · strategy for a pending or done render | Studio all stages, Bootstrap, Asset Manager | Compact by default; expands to the full submitted-payload view (U6). |
| **`ReferencePicker`** | select an approved reference (face view set / wardrobe / location) | Studio Identity, Studio Composition, Bootstrap promotion | Shows approval state + version (U5). Angle-aware view set surfaced (FR-C3-05). |
| **`StrategySelector`** | choose an identity strategy + strength for this request | Studio Identity, qualification UI (P3) | Greys out **structurally-impossible** cells with the reason; blocks **unqualified** with a link to qualify (FR-C3-04). Never silently substitutes (U5). |
| **`ScorecardPanel`** | show the identity/adherence/diversity **triple** for a render or a phase | Phase gates, Studio, validation | Never shows identity alone (FR-C6-01). Expander for per-metric detail (U3). |
| **`FrozenTextBlockEditor`** | edit the mandatory invariant text block of a reference | Bootstrap, Reference Library | Enforces non-empty before approval (FR-C1-02). |
| **`LineageTree`** | branch-aware attempt history grouped by stage | Studio, Asset Manager | Virtualized (U4). Branch-from-any-completed-attempt. |

**Adding a component** requires: a named job (U1), a typed input contract, its own tests, and a row
here. If two surfaces need the same job, the second reuses the first (U2) — it does not fork it.

---

## 3. Screen inventory (small, single-purpose)

Not one Studio that does everything. A set of focused screens that flow.

| Screen | Single job (U1) | Composes | Phase |
|---|---|---|---|
| **Runs** | monitor and control all staged/active/finished runs | `RunTray` | P1 |
| **Bootstrap: Describe** | capture the target description + candidate count | `FrozenTextBlockEditor` (draft) | P2 |
| **Bootstrap: Curate** | accept/reject candidates | `CandidateGrid` + embedded `RunTray` | P2 |
| **Bootstrap: Expand** | generate more views from an accepted candidate | `CandidateGrid` | P2 |
| **Bootstrap: Promote** | promote one set to a canonical reference | `ReferencePicker` preview + `FrozenTextBlockEditor` | P2 |
| **Reference Library** | browse/version/approve references | `ReferencePicker` list, `FrozenTextBlockEditor` | P2 |
| **Strategy Qualification** | qualify a `(strategy,model,endpoint,strength)` cell and record its scorecard | `StrategySelector` + `ScorecardPanel` + embedded `RunTray` | P3 |
| **Studio: Composition** | frame + generate the base image, transparently | `ResolvedRenderBadge` + `EditIterateWorkbench` + embedded `RunTray` | P4 |
| **Studio: Identity** | apply identity to an approved composition | `ReferencePicker` + `StrategySelector` + `EditIterateWorkbench` | P4 |
| **Studio: Finish** | cosmetic/geometry edits with change-class | `EditIterateWorkbench` (change-class control) | P4 |
| **Studio: Lineage** | inspect/branch the attempt tree | `LineageTree` + `ScorecardPanel` | P4 |
| **Asset Manager: Edit** | edit a library asset | `EditIterateWorkbench` | P4 |
| **Location: Geometry** | derive/inspect control maps for a location | `ReferencePicker` + map preview | P5 |
| **Validation Review** | accept/reject candidates against scores | `CandidateGrid` + `ScorecardPanel` | P6 |

Each is reachable on its own, does one thing, and hands off to the next via `RunTray` or a picker.
The Studio "stages" are **separate screens that share state via the production group**, not tabs in
one mega-form.

---

## 4. Per-phase UI gate (added to each phase's exit gate)

A phase does not close unless, in addition to its backend gate:

- [ ] Every new surface passes the **five-word job test** (U1) and is listed in §3.
- [ ] Every reused workflow uses a **shared component** from §2 — grep proof of no duplicate loop (U2).
- [ ] Advanced/large data is behind **progressive disclosure** (U3) and **bounded rendering** (U4) —
      no unbounded list, no inlined large blob.
- [ ] Every backend state the phase introduces is **visibly surfaced with the same vocabulary** (U5).
- [ ] Every generate/edit action has a **"view what will be submitted"** affordance (U6).
- [ ] Multi-step flows **preserve state across Back/Next and navigation away** (U7).
- [ ] A live E2E check (the repo's `tools/e2e/` Playwright harness) exercises the new flow LLM-free.
- [ ] Manual usability pass by **you** — the authoritative verdict extends to usability, not just
      visual quality (P8).

---

## 5. What this contract forbids

- A single screen with tabs for Composition + Identity + Finish + Assets + Runs. (That is the
  cram-everything anti-pattern.)
- Re-implementing the edit/iterate loop, the candidate grid, or the run tray a second time.
- Rendering a whole gallery, a whole lineage, or a whole run queue with no pagination/virtualization.
- Dumping a 600 KB prompt or a raw payload into the main view.
- A generate/edit button whose exact inputs the user cannot inspect.
- A backend state (`cold`, `unqualified`, `structurally-impossible`, `identity-stale`) that the UI
  hides behind a generic spinner or a flat "unavailable".
