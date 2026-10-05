# B-148 — Location creation from the moment (in-flow Stage 0)

**State:** `planned` — **design pass + design review complete, `tasks.md` + `ui-contract.md` produced (2026-10-04); the explicit go-ahead is the only gate left before implementation**
**Created:** 2026-10-04
**Owner surface:** Production Studio — moment POV production
**Priority:** high (next main feature enhancement) · **Scope:** large

---

## Why this exists

The RP engine invents locations on the fly. A location therefore **cannot be a pre-registered
entity** — it must be creatable **from the moment**, as the first step of producing that moment.

Operator, verbatim:

> *"not all locations will be mapped … the source is a rp roleplay engine, it is meant to be
> dynamic not rigid, it could go anywhere, so i need to be the steps to create the location images
> as the first step in many cases for a producion moment … i need the ability to take the moment
> data and create the location which will then be the POV Backdrop as part of the production flow
> **NOT before**"*

and

> *"locations and it individual images need to be dynacmic easy to create, the rp session can make
> up any location at random."*

## The governing constraint (read this before designing anything)

**This plan owns the containers, the placement, the approval gate, the naming and the selection.
It does NOT own how an image is produced.**

| the plan specifies | the plan must NOT specify |
|---|---|
| the container — a location holding user-named images, dynamic, no fixed view set | how any image is produced |
| the placement — stage 0 *of* the moment, seeded from the moment's frozen state | which model, sampler, technique or channel |
| the approval + naming gate — what makes an image bindable | a fixed view list, or a per-container view catalog |
| the selector — which named image is the POV backdrop for this moment/beat | anything that assumes one creation technique exists |

Creation technique is explicitly **a tool on the image creation / image edit surface**. Text-driven
generation, editing from a source, 360-capture projection, photographic upload and reconstruction
are all *candidate sources*; the operator chooses and judges whether the result is good enough.
Adding or improving a technique must never require changing this plan.

## Candidate sources — all admissible, none required

Full analysis in [`OPTIONS-consistent-location.md`](OPTIONS-consistent-location.md).

| | source | armed today? |
|---|---|---|
| **A** | photographic capture, fixed views | ✅ zero tech |
| **B** | 360 capture → deterministic reprojection | ⚠️ viewer maths validated; B-139 T1/T2 not built |
| **C** | multi-view reconstruction (photogrammetry / splatting) | ❌ host has the nodes, **not the models**; external tools viable |
| **D** | pure text generation | ✅ works today |
| **E** | generation conditioned on depth / references | ✅ tested — **layout only, identity not preserved** |
| **F** | all views in one generation (turnaround sheet) | ⬜ untested |
| **G** | 3D → render → depth-ControlNet photoreal pass | ⚠️ sound, untested end-to-end |
| **H** | reuse an approved image as the next reference | ⚠️ measured to drift |
| **I** | **video camera-move → frame extraction** | ✅ **local Wan 2.2 route armed**; cloud needs a key |

**Note for the design pass:** option **I** (added 2026-10-04, operator) is the strongest
*consistency* route among the generative options — one continuous generation means the frames agree
by construction — and it brings the camera-angle capability the operator asked for. **The camera
control already exists locally**: `WanCameraEmbedding` (`camera_pose` presets plus explicit
intrinsics `fx`/`fy`/`cx`/`cy`) → `WanCameraImageToVideo`. It is blocked by the same ingest gap as
A/B/C/F/G. The host exposes MiniMax **H3**, not "M3"; confirm which was meant.

## Why the operator is stuck today (verified 2026-10-04)

The machinery exists end-to-end in the service layer, and **three of its four steps have no UI**:

| Step | Service method | Reachable? |
|---|---|---|
| Create a location batch from the moment's text | `CreateBatchAsync` | ❌ no UI — **nothing in the app calls it** |
| Generate candidate images of the place | `GenerateCandidatesAsync` | ❌ no UI |
| Accept / reject a candidate | `SetCandidateDecisionAsync` | ⚠️ Review Deck / Compare Deck — **accept only** |
| Promote accepted → location reference images | `PromoteAcceptedLocationAsync` | ❌ no UI |

`ReferenceBootstrapPanel.razor` performs all four steps and is **mounted nowhere** — the only other
mention of it in the codebase is a test asserting `ModelManager` must *not* contain it.

## The blocking gap that violates the governing constraint

`IReferenceBootstrapService.GenerateCandidatesAsync(batchId)` is the **only** way a candidate can
enter a batch. There is no upload method, no ingest method, no derive method.

So the pipeline currently says: *you may only create location images by text generation.* An
operator who photographs the shed, captures a 360, or projects views from one **has no way into the
pipeline at all.** This is a service-boundary restriction, not merely a UI omission, and it must be
fixed for this item to meet its own constraint. See `spec.md` FR-B148-04.

> **Resolved (design pass, 2026-10-04):** the bootstrap candidate batch is **retired for
> locations** (D3). Creation happens in `/locations/{id}` through the asset manager's existing
> upload / generate / edit tooling — which is what makes every source admissible without a new
> ingest path. See `plan.md` § *Settled decisions*.

## What is reused, unchanged

| Concern | Reused as-is |
|---|---|
| Location container + named images | `SceneAsset(Type=Location)` + `SceneAssetImages.DisplayName` (B-145, shipped) |
| Creation tooling (upload / generate / edit) | `/locations/{id}` → `AssetStudioView` — unchanged |
| Name-required-for-approval gate | `SceneAssetImageNaming.IsNameRequiredForApproval` (B-145, shipped) |
| Reference selection | `ReferencePicker` / `ReferenceApplyPanel` (B-111, B-145) |
| Slot prefill precedent | `IdentityPackSlotPrefill` — the auto-seed is its sibling (D16) |
| Create-then-navigate + `?return=` | `AssetCreate.razor` / `AssetEdit` / `ReviewDeck` conventions |

**Retired by this item (D3):** `PromoteAcceptedLocationAsync`, the location arm of
`MapReferenceKind`, `ReferenceBootstrapBatch.LocationProfileId`, and the
`ReferenceBootstrapLocationProfile` / `ReferenceBootstrapLocationReference` code paths. The DB
tables stay (dead, harmless — D18); no migration, and the existing `TRAILER` asset is test-only
and ignored.

## What is genuinely new

1. **Stage 0 placement** — an optional `Location` stage *inside* moment production, before
   Composition (page-local — D15).
2. **Moment seeding** — the create-from-moment prefill, place-only (D4).
3. **Location hierarchy** — an optional parent on the container; world → location, spot = a named
   image (D7).
4. **Scenario-location mapping** — `ScenarioLocationId` + `ScenarioId` on the container; ID is the
   relation, name is only the lookup key (D8).
5. **World location on the Setting** — `WorldLocation { AssetContainerId, RenderingDescription }`
   (D9).
6. **Per-POV backdrop selection** over the named views, auto-seeding the Composition's location
   reference (D12, D16).
7. **Moment→container link** — keyed on `MomentId`, operator-confirmed (D17).

## Deliverables of this item

| Artifact | Status |
|---|---|
| `README.md` (this file) | ✅ written — the persisted plan |
| `spec.md` — requirements | ✅ written — draft, for review |
| `plan.md` — design brief, verified findings, open questions | ✅ written — **the design pass input** |
| [`FINDINGS-location-images.md`](FINDINGS-location-images.md) | ✅ written — every experiment tried, its verdict, and the instrument failure that invalidates raw numbers |
| [`FINDINGS-depth-and-models.md`](FINDINGS-depth-and-models.md) | ✅ written — what depth is, **which models can consume it** (pairing rules), and where it belongs in this app |
| [`OPTIONS-consistent-location.md`](OPTIONS-consistent-location.md) | ✅ written — the implementation options for consistent location images, ranked, with the blocking gap |
| [`RESEARCH-minimax-m3-t2v.md`](RESEARCH-minimax-m3-t2v.md) | ✅ written — external research: "MiniMax M3" is a language model; H3 runs only partly locally; **Wan 2.2 Fun-Camera** is the true camera-control route (with sources) |
| **Fun-Camera 16GB proof** | ⬜ **moved to its own item, B-151** — see `../B-151-fun-camera-16gb-proof/README.md` (VRAM gate → camera-move consistency, for a separate session) |
| **Design pass** | ✅ **complete** (2026-10-04 — `plan.md` § *Settled decisions* D1-D19) |
| **Design review** | ✅ **complete** (2026-10-04 — `plan.md` § *Review gate*: constraint held, two-model settled, no silent default, mechanism gaps closed, UI plan written) |
| [`tasks.md`](tasks.md) | ✅ written — phased task list (verify → domain → persistence → retirement → services → UI → validation) |
| [`ui-contract.md`](ui-contract.md) | ✅ written — the Stage-0 surface, states, and state keys |

**No implementation begins until `tasks.md` + `ui-contract.md` exist and the explicit go-ahead is
given** (RP engine change control — the design review is complete).

## Relationship to neighbouring items

- **B-108** (reference bootstrap studio) — the *standalone, up-front* sibling. Its **character**
  machinery is reused; its **location path is retired by this item** (D3). Absorbed into
  **B-111 P2**, whose `P2-tasks.md` says "This phase EXECUTES B-108. Do not redesign."
- **B-111 P2/P3/P4** — P2 supplies the bootstrap machinery; P3 supplies view selection; P4 hosts
  the composition stage.
- **B-145** (location references) — **implemented**; supplies named accepted images and the
  approval gate this item promotes into.
- **B-139** (360° location reference) — a *candidate source* for this plan, not a dependency.
- **B-126** (multi-character composition) — the completion of the composition stage.
- **B-121** (character identity studio) — the equivalent bootstrap flow for characters; the
  structural precedent.
