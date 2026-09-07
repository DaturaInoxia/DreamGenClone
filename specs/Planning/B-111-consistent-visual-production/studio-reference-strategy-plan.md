# B-111 — Whole Plan on Production Surfaces + Review Deck

**State:** implemented (scope completed 2026-09-06). This is the **entire** original B-111
production/reference/consistency plan, delivered through the two production surfaces —
`/roleplay/studio` and `/asset-studio` — plus the existing dedicated `/review-deck/{batchId}` review
surface. **No new top-level page is needed.**
The review deck is intentionally retained because reviewing all candidates for one production batch
(a moment batch or an asset/reference batch) and changing their status is a distinct workflow from
creating or editing an image. The session-wide image library remains Gallery.

> **Intent (user):** *the whole production plan — references, create/edit workbench, strategies,
> curation, runs, scoring, and lineage — is implemented in the real Studio and Asset Manager. A
> dedicated Review Deck remains for quickly reviewing all images in a moment or asset batch and
> setting their status; Gallery remains the session-wide library.*

> **Scope-completion decision (2026-09-06):** B-111 is complete when every production surface,
> durable provenance boundary, reference strategy contract, qualification gate, and activation seam
> is implemented. A graph strategy becomes selectable only after its endpoint request/workflow is
> installed and Model Manager contains a passing qualification proof; until then it remains visibly
> unavailable and fails explicitly if requested. LoRA inference, ControlNet, IDM-VTON/wardrobe
> try-on, persisted evaluator scorecards, and versioned frozen-text history are future endpoint or
> evaluation integrations, not unfinished B-111 surface work. They must not be simulated in UI or
> silently downgraded to `TextOnly`.

---

## A. Surface ownership

| Surface | Owns (its single job) | Absorbs from the retired shells |
|---|---|---|
| **Asset Manager** (`/asset-studio`) | **Build & manage REFERENCES** — Identity(face), Character Body, Wardrobe, Location. Create (describe→generate→curate→promote), version/approve, browse, and **edit/iterate** a reference. | Reference Bootstrap and Reference/Image Library; links generated asset/reference batches to Review Deck for candidate review before promotion. |
| **Studio** (`/roleplay/studio/{session}/{interaction}`) | **Produce MOMENT images** — Composition (create) → Identity/apply-references → Finish (edit), with iterate/compare/lineage, runs, scorecard. | Dashboard counts (inline), Runs (embedded RunTray); links moment batches to Review Deck. |
| **Review Deck** (`/review-deck/{batchId}`) | **Review one production batch** — cycle through every candidate for a moment or asset/reference batch, compare candidates, inspect metadata/prompt/references/lineage, and set Accept/Shortlist/Reject/Revoked status. | Existing Review Deck behavior; receives links from Studio and Asset Manager. |

Creation and editing stay in the two production surfaces. Review remains a dedicated page because
keyboard-friendly candidate inspection and status triage for either moment images or asset/reference
images should not be buried inside either creator. Gallery remains responsible for browsing all images
in a session. Shared components in §C keep the data contracts and visual behavior consistent.

---

## B. The capability ceiling (keystone — threads through everything)

- `Provider.ImageProtocol` → `ProviderExecutionPolicy.Resolve` → `ProviderExecutionClass`:
  `OpenAiImages`→**HostedApi**, `ComfyUi`→**DedicatedPod**, `ComfyUiServerless`→**SelfBuiltServerless**.
- **Research F7:** HostedApi = **NO ControlNet, IP-Adapter, PuLID, LoRA** (text prompt only).
  ComfyUi/Serverless = full graph, all techniques.
- Model Manager is the authoritative capability declaration surface. `RegisteredModel` stores the
   model's supported visual strategies and configuration (IP-Adapter/PuLID, LoRA, ControlNet, and
   wardrobe/try-on), while provider fields determine where the model runs. Qualification evidence is
   stored in the same Model Manager capability configuration; each `(strategy × model × endpoint)`
   needs a passing proof. **0 qualified today**.
- **Availability = 3-level gate:** structurally-possible (endpoint class) → model-declared → qualified.
  **TextOnly always available. Never silent-substitute.**

### Element × strategy × endpoint (create + edit)
| Element | TextOnly (ALL incl. HostedApi) | Reference/graph (ComfyUi/Serverless ONLY) | Research |
|---|---|---|---|
| Identity (face) | prompt tokens | IP-Adapter / PuLID — create **and** edit | F1, F1.1 |
| Character body | description | LoRA — create | F1 |
| Location | frozen text block | ControlNet + plate — create | F3 |
| Wardrobe | text (mandatory even w/ image) | reference / IDM-VTON try-on | F4 |

In-context edit models (Kontext/Qwen) are **edit-only**. Multi-identity uses **regional routing** (F2).

---

## C. Retirement / keep decisions (corrected by user)

| Shell page | Decision | Why |
|---|---|---|
| `/reference-bootstrap` | **Retire** → into Asset Manager | reference building belongs in Asset Manager |
| `/image-library` | **Retire (redundant)** | an image library **already exists** at `/roleplay/gallery/{sessionId}` (`SceneImageGallery.razor`) — use it, don't duplicate |
| `/review-deck` | **KEEP and strengthen** | dedicated batch review is useful: view all images for one moment or asset/reference batch, compare, inspect lineage/metadata, and set status without reopening the creator |
| `/production-dashboard` | **KEEP** | genuinely useful new addition; the one shell worth keeping |

Existing surfaces that already do a plan job (reuse, don't rebuild):
- **`/roleplay/gallery/{sessionId}`** = the session-wide image library / browse across all moments.
- **`/roleplay/studio/{s}/{i}`** = moment production (Composition/Identity/Finish).
- **`/roleplay/image-editor/...`** = the working edit/iterate loop (source of `EditIterateWorkbench`).
- **`/asset-studio`** = reference manager.
- **`/review-deck/{batchId}`** = dedicated review of all candidates for one moment or asset/reference batch and status triage.

---

## D. Shared components and contracts (built once, reused across production and review surfaces)
1. **`ReferenceStrategyResolver`** (service, keystone) — `(element,strategy,model,endpoint)` →
   `Possible | Unqualified(reason,link) | Impossible(reason)`. Only source of strategy availability.
2. **`EditIterateWorkbench`** — extract from working `SceneImageEditor`; Studio Composition/Identity/
   Finish + Asset Manager edit.
3. **`ReferenceApplyPanel`** — Identity/Body/Wardrobe/Location rows; each Text-only OR Reference-image;
   text-source picker; per-row `StrategySelector`.
4. **`StrategySelector`** — reads (1); greys Impossible w/ reason, blocks Unqualified w/ qualify link.
5. **`ReferencePicker`** — approved reference + version (angle-aware view set).
6. **`CandidateGrid`** — curate accept/shortlist/reject + compare.
7. **`ResolvedRenderBadge`** — "view what will be submitted" (U6).
8. **`RunTray`** — runs + cost + honest cold/warm; embedded.
9. **`ScorecardPanel`** — identity/adherence/diversity triple.
10. **`LineageTree`** — branch-aware attempt history.
11. **`FrozenTextBlockEditor`** — mandatory invariant reference text block.

---

## E. Where each capability lives
**Asset Manager (`/asset-studio`) — references:** create reference (4 kinds, text-or-image, gated) via
`FrozenTextBlockEditor`+`EditIterateWorkbench`+`CandidateGrid` → promote; reference library/version/
approve; edit a reference (`EditIterateWorkbench`); embedded `RunTray`; link each generated asset/reference
batch to Review Deck for candidate review and status decisions.
**Studio (`/roleplay/studio`) — moment images:** Composition (create) `ReferenceApplyPanel`+
`StrategySelector`+`ResolvedRenderBadge`+`EditIterateWorkbench`+`RunTray`; Identity (`ReferencePicker`+
`StrategySelector`); Finish (edit, change-class) + **identity at edit**; `LineageTree`+compare+
`ScorecardPanel`; needs-review/failed/cold-warm inline; link each moment's completed candidates to
Review Deck.
**Review Deck (`/review-deck/{batchId}`) — batch review:** show every candidate image for the selected
moment or asset/reference batch, cycle/compare, expose score and lineage context, and persist status
changes through the canonical candidate-decision service. Global overview stays in
**`/production-dashboard`**.
**Gallery (`/roleplay/gallery/{sessionId}`) — session library:** browse all images across the session;
it is not the moment review-decision surface.

---

## F. Audit — exists vs must-build
**Exists:** identity **face** (IP-Adapter/PuLID, `ComfyUiServerless`); **text** for all 4 via frozen
state; edit loop (`ISceneImageEditCompilationService`, now reachable from staged attempts); capability
model + per-model flags; `/production-dashboard`; `/roleplay/gallery`.
**Activation-ready when endpoints/evaluators arrive:** location **reference-image**
(ControlNet+plate); body **reference** (LoRA); wardrobe **reference/try-on**; persisted triple-score
evaluation; and versioned frozen reference-text history. Their seams and availability gates are
implemented; they remain unavailable until an executable provider workflow and passing qualification
evidence are recorded.

---

## G. Build order (capability-first; no fake UI)
0. **Model Manager capability contract + tests** (keystone): extend `RegisteredModel` and the real
   Model Manager editor so capabilities are explicitly declared and qualification evidence is stored
   per model/endpoint/strategy. No capability data is owned by the UI or a parallel registry.
1. **`ReferenceStrategyResolver` + tests**: read only the Model Manager capability contract, combine it
   with endpoint structural limits, and return `Possible | Unqualified | Impossible`.
2. **Pipeline seams**: create+edit requests carry per-element `(reference?, strategy, strength?)`;
   dispatch builds IP-Adapter/LoRA/ControlNet **only** on ComfyUi/Serverless; HostedApi rejects
   non-Text strategies at the boundary (explicit, no silent swap). Face reuses existing identity
   client; add seams for location(ControlNet)/body(LoRA)/wardrobe(try-on).
3. **`StrategySelector` + `ResolvedRenderBadge`** (read the resolver).
4. **`ReferenceApplyPanel` + `ReferencePicker`**.
5. **`EditIterateWorkbench`** (extract from `SceneImageEditor`) + `CandidateGrid`/`RunTray`/
   `ScorecardPanel`/`LineageTree`; keep Review Deck as the shared candidate review/status surface for
   both moment and asset/reference batches and connect it to the shared lineage/score/status contracts.
6. **Wire into Asset Manager** (create/library/edit references).
7. **Wire into Studio** (Composition/Identity/Finish + embedded editor).
8. **Retire redundant shells**: delete `/reference-bootstrap` and `/image-library` + their nav links.
   **Keep `/review-deck` and `/production-dashboard`.** Library = `/roleplay/gallery`.

Each step: build only the affected project, tight filtered test, suite green. No element's UI ships
before its pipeline seam + resolver gate exist. Under the 2026-09-06 scope-completion decision,
endpoint-specific graph dispatch and evaluator execution are activation work after their real
endpoints/evidence exist, not prerequisites for this production-surface plan to close.

## H. Gates
- Resolver is the **only** source of strategy availability (grep proof).
- HostedApi ⇒ TextOnly everywhere (test-enforced). Unqualified ⇒ blocked w/ qualify link, never run.
- Every generate/edit action has "view what will be submitted" (U6).
- Every completed moment or asset/reference create/edit run has a direct path to Review Deck; status
   changes are persisted and visible from the originating Studio or Asset Manager surface.
- No duplicated workflow — shared components reused, not forked (U2 grep proof).
- Redundant shells deleted; `/review-deck` and `/production-dashboard` kept; library reuses
   `/roleplay/gallery` for all images in the session.
