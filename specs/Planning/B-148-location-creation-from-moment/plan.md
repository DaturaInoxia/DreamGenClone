# B-148 — Plan / Design (settled — design pass complete, awaiting design review)

> **This file is the design-pass output.** It records what was verified, the decisions that are
> now settled, and the resolutions of the open questions. It is deliberately *not* a task list —
> `tasks.md` + `ui-contract.md` are produced only after the design review.
>
> Last verified: **2026-10-04** (session walkthrough of production moment `a607b7bc`, scenario
> `135a9237` "Campground Intimacy").

---

## 0. Settled design summary

A location is a **container** in the asset manager that the production moment's Stage 0 either
reuses (as a *suggestion*, never a lock) or creates. The container model gains three new
primitives, all verified against the live scenario data:

- **Hierarchy** — `SceneAsset` gains an optional parent. The world/Setting ("Trailer Park") is the
  root container; each `Scenario.Location` ("Husband and Wife Trailer — Shared Private Space") is a
  child container; a **spot** ("the yard clothesline") is a *named image inside* the location
  container, not a third container level.
- **Scenario-location mapping** — `SceneAsset` gains `ScenarioLocationId` + `ScenarioId`; one
  *current* container per scenario location via the existing version lineage. `ScenarioLocationId`
  null means the location is **ad-hoc** (invented by the RP engine). **The name is only the lookup
  key; the ID is the relation** — so a test asset that merely shares a name can never be mistaken
  for the scenario location.
- **World location** — `Setting` gains a structured `WorldLocation { AssetContainerId,
  RenderingDescription }`, kept separate from the narrative `WorldDescription`.

The moment's composite location string resolves as:

```
Setting/World              →  WorldLocation.AssetContainerId (root, carries RenderingDescription)
   └─ Scenario.Location     →  container by ScenarioLocationId
        └─ "- <spot>"        →  a named image inside that container (the per-POV backdrop)
```

The Stage 0 flow is **optional** (skip → text-only location), prefills a *place* description
(action stripped), and the chosen backdrop **auto-seeds the Composition's location reference**
(editable later). The B-108 location path is **retired** — no migration (the `TRAILER` asset is
test-only and ignored).

---

## 1. Verified state of the world

### 1.1 The machinery exists; the surface does not

`ReferenceBootstrapService` implements the whole candidate → curate → promote lifecycle, is
registered (`Program.cs:501`), and is injected by `ReviewDeck.razor` and `CompareDeck.razor`.

| Capability | Method | Reachable from UI? |
|---|---|---|
| Create a batch | `CreateBatchAsync` | ❌ **nothing calls it** |
| Generate candidates | `GenerateCandidatesAsync` | ❌ no UI |
| List candidates | `ListCandidatesAsync` | ⚠️ via the decks |
| Accept / reject | `SetCandidateDecisionAsync` | ⚠️ **accept only** |
| Promote face / body / wardrobe | `PromoteAccepted*Async` | ❌ no UI |
| Promote location | `PromoteAcceptedLocationAsync` | ❌ no UI |

`ReferenceBootstrapPanel.razor` performs all four location steps in one component and is **mounted
nowhere**. The only other mention in the codebase is a test asserting `ModelManager` must *not*
contain it.

### 1.2 The blocking gap — candidates can only be generated

```csharp
Task<IReadOnlyList<SceneAsset>> GenerateCandidatesAsync(string batchId, ...)
```

This is the **sole** candidate source in `IReferenceBootstrapService`. There is no upload, ingest or
derive method. Consequently the pipeline structurally permits **one** creation technique, which
directly contradicts the governing constraint in `README.md`.

*This is the single most important thing the design pass must resolve* (see OQ-4).

### 1.3 Promotion drops the operator's name

`PromoteAcceptedLocationAsync(batchId, producedImageId)`:

- has **no name parameter**;
- names the created asset `$"Reference bootstrap location {batch.Id}"` (`ReferenceBootstrapService.cs:296`)
  — batch-derived, not operator-entered;
- stamps the moment's text onto the profile: `Description = batch.FrozenTextBlock!.Trim()` (line 309)
  — a **text stamp, not a link**: the frozen text is copied onto the bootstrap profile, but nothing
  relates the produced asset to the moment structurally;
- appends an ordered `ReferenceBootstrapLocationReference`.

The naming primitive it should be using **already ships** (B-145): `SceneAssetImages.DisplayName`,
`SetImageDisplayNameAsync`, and `SceneAssetImageNaming.IsNameRequiredForApproval`.

*Correction to an earlier note:* it was suggested B-145 might have superseded the naming task. It
did **not** — verified 2026-10-04.

### 1.4 Two competing location models — must be reconciled

| Model | Where | Shape |
|---|---|---|
| `ReferenceBootstrapLocationProfile` / `ReferenceBootstrapLocationReference` | B-108 / B-111 P2 | bootstrap-owned profile + ordered reference rows |
| `SceneAsset` of type `Location` + `SceneAssetImages.DisplayName` | B-145, **shipped** | a location *container* holding named images |

`PromoteAcceptedLocationAsync` **writes both** — it creates a `SceneAsset(Location)` *and* appends a
`ReferenceBootstrapLocationReference` to the profile. **Resolved (D3):** the bootstrap profile is
retired and the B-145 container is canonical — no migration (the only existing row, `TRAILER`, is
test-only).

B-108 deliberately named its records `ReferenceBootstrap*` so they would never be confused with
B-032 Phase 3's eventual `LocationProfile`. That caution predates B-145.

### 1.5 Technical context that constrains candidate sources

Measured 2026-10-04 on the operator's real shed (see
`specs/image-generator-tests/room-pov-proof/FINDINGS.md`):

- **Generation reproduces layout, not identity.** A conditional image model re-draws every pixel
  from noise and is only *biased* toward a source; nothing is copied. Across camera moves it
  produces *a* shed, not *the* shed. No setting, model or channel changes this.
- **Depth carries geometry, never appearance.** Supplying a target view's depth pins layout while
  all appearance in the newly-revealed area is invented.
- **Deterministic reprojection preserves pixels exactly** — projecting views from a capture moves
  the *same pixels*, so identity cannot drift.

**Design consequence:** candidate sources must be plural *by design*, because no single technique
covers all cases. Real places favour capture/projection; invented places favour generation. This
is the technical justification for FR-B148-04.

## 2. The settled Stage 0 flow

The Location stage sits **before Composition** in the Production POV workbench and is **optional**:
skipping it leaves the composition with a text-only location (the picker's existing "Text only"
option). Every step below is a *suggestion* the operator confirms; none is a lock.

| Step | What happens | State |
|---|---|---|
| **0.0** | Select the moment + POV (already done); the optional `Location` stage is offered before Composition | ✅ exists; stage added |
| **0.1** | **Default suggestion** — prefix-match the moment's `location` against `Scenario.Location.Name`; if found, resolve its container by `ScenarioLocationId`; token-suggest the spot's named image. No match → **ad-hoc**. Operator always confirms. | ❌ new |
| **0.2** | **Search** — all location assets as cards; approved+named images are selectable, unnamed/unapproved are shown disabled with a "finish in Location Studio" link. | ❌ new |
| **0.3** | **Create from this moment** — opens `/locations/{id}` pre-filled with a *place* description (environment / lighting / timeOfDay / objects / physical layout; action stripped) and a default name. | ⚠️ surface exists, not seeded |
| **0.4** | **Produce + name + approve** each view inside the container — full asset-manager tooling (upload / generate / edit / name / approve) is reused unchanged. Approval is per-image; name is required first. | ✅ existing (B-145) |
| **0.5** | **Bind** — the chosen named image auto-seeds the Composition's location reference (editable later); it also becomes that POV's backdrop. | ❌ new |
| **1** | Compose → Identity → Finish (existing stages). | ✅ existing |
| **2** | Repeat for each POV; the container is moment-scoped, the backdrop image is per-POV. | ❌ new |

## 3. Settled decisions

- **D1 — No fixed view set.** Locations hold arbitrary, operator-named images (B-145 already
  settled this; restate it, do not re-litigate). A *spot* is one such named image.
- **D2 — Creation technique is out of scope.** See `README.md` § governing constraint. Creation
  happens in `/locations/{id}` through the asset manager's existing upload / generate / edit tools.
- **D3 — Retire B-108's location path, do not reuse it.** B-111 `P2-tasks.md` "EXECUTES B-108"
  applies to the *character* bootstrap machinery only. The location path (`PromoteAcceptedLocationAsync`,
  `ReferenceBootstrapLocationProfile` / `ReferenceBootstrapLocationReference`) is retired as dead
  code. **No migration** — the existing `TRAILER` asset is test-only and ignored (it matches in
  name only, not by relation).
- **D4 — Seeding is a *place* description.** Built from `environment`, `lighting`, `timeOfDay`,
  `objects`, and the characters' `physicalLocation` spatial layout; **action/people/nudity stripped**
  (never `visualDescription`, `continuityState`, or the mood's action component). Operator-editable.
- **D5 — No silent substitution.** Missing name / missing location / uncarriable reference → fail
  with an explicit message (repo rule).
- **D6 — The Location stage is optional.** Skip → the composition proceeds with a text-only
  location, preserving existing behaviour for moments with no location.
- **D7 — Hierarchy via an optional parent field.** `SceneAsset` gains `ParentAssetId` (arbitrary
  depth). In practice: world container (root) → scenario-location container (child); spots are
  **named images inside** the location container, never a third container level.
- **D8 — Scenario-location mapping by ID.** `SceneAsset` gains `ScenarioLocationId` + `ScenarioId`;
  one *current* container per scenario location via the existing `ProductionVersion`/supersede
  lineage. `ScenarioLocationId` null = **ad-hoc** location. **Name is the lookup key; ID is the
  relation.**
- **D9 — World location lives on `Setting`.** `Setting` gains a structured
  `WorldLocation { AssetContainerId, RenderingDescription }`, separate from the narrative
  `WorldDescription`. The rendering description is "what it looks like", not the mood.
- **D10 — Reuse is a default suggestion, never a lock.** The operator may bind any approved image
  (even unrelated ones) to any POV.
- **D11 — Search shows cards; non-selectable images are disabled.** Approved+named images are
  selectable; unnamed/unapproved images render disabled with a "finish in Location Studio" link.
- **D12 — Backdrop auto-seeds Composition.** The Location-stage choice becomes the composition's
  `location` reference binding, still editable in the composer.
- **D13 — Persistence split by scope.** The per-POV backdrop lives on `SceneImageProductionGroup`;
  the moment→container default lives in an explicit moment-level link.
- **D14 — Resolution rule.** Prefix-match the moment's `location` string against
  `Scenario.Location.Name`; the spot suffix token-suggests a named image; no prefix match → ad-hoc.
  `Sightline` is a hint only, never an auto-selection. Operator always confirms.
- **D15 — The workbench stage is page-local; the persisted stage enum is untouched** *(design
  review, 2026-10-04)*. `SceneImageProductionStage` is BOTH the UI stepper stage AND the persisted
  `ProductionStage` on `SceneImageRecord`, switched on in `SceneImageMediaEditSubjectWriter`,
  `SceneImageEditingJobHandler`, `SceneImageProductionService` and `SceneImageService`. Adding a
  `Location` member would leak a UI-only concept into the persisted domain and every switch site.
  The Location stage is therefore a **page-local workbench stage** in `SceneImageStudio`
  (`Location → Composition → Identity → Finish`); no image is ever *at* the Location stage; the
  first render remains `ProductionStage = Composition`.
- **D16 — The auto-seed is a slot prefill, and the group field is the durable record** *(design
  review)*. `CompiledMediaBrief` carries no reference applications — the composer's bindings are
  rebuilt per visit and travel only with the render request. The per-POV backdrop field on the
  group is therefore the **source of truth**; a prefill sibling of `IdentityPackSlotPrefill`
  (e.g. `LocationBackdropSlotPrefill`) re-seeds the composer's declared `Location` slot from it on
  every composer open, **refusing a blueprint that declares no location slot by name** — the
  `ReferenceStrategyCatalogue.ElementKeyForSlot` contract exists for exactly this ("a host that
  seeds a binding … names it identically"). A model change clears bindings (existing behaviour);
  the location binding **re-seeds only after re-validation** against the new model's slot plan —
  if the new model declares no location slot, a notice says so. Never a silent drop.
- **D17 — The moment→container link is keyed on `MomentId`** *(design review)*. Enrichment
  revisions change `MomentEnrichmentId`; `MomentId` is stable across them. Keying the link on the
  enrichment id would lose it on every re-enrichment. One **current** link per moment (upsert),
  carrying the container asset id, the resolved `ScenarioLocationId` (null = ad-hoc), and the
  link's origin — **operator-confirmed, never auto-written**.
- **D18 — Schema changes follow the existing migration patterns; nothing destructive** *(design
  review)*. `SceneAssets` gains its three columns through the repository's existing pragma-guarded
  `ALTER TABLE` list. `SceneImageProductionGroups` has **no such mechanism today** (only
  `CREATE TABLE IF NOT EXISTS`) — the backdrop column requires adding the same pragma-guarded
  ALTER pattern to that repository, or the column never reaches an existing DB. `Setting.WorldLocation`
  is payload-JSON only (`Scenarios.PayloadJson`; no migration; old payloads deserialize null =
  not configured). The retired bootstrap location tables are **left in place** — dead tables, no
  data loss, no destructive migration.
- **D19 — The asset tree's location roots re-source to containers** *(design review)*.
  `SceneAssetTreeService` builds today's location roots from `ReferenceBootstrapLocationProfile`
  and links `/locations/{profileId}` — an id `AssetStudioView` cannot resolve (its lookup is a
  `SceneAssets` query). Retiring the profiles **requires** the tree's location roots to come from
  `SceneAsset(Type=Location)` containers, rendered with the new hierarchy (world parent → location
  children). This is a hard dependency of D3, not optional cleanup — and it fixes a latent break.

## 4. Resolved design questions

| # | Question | Resolution |
|---|---|---|
| **OQ-1** | Placement | The Production POV workbench in `SceneImageStudio.razor` (the `ProductionPov` tab) — stage 0 of the moment, *not* the Image Playground or the standalone Asset Studio. |
| **OQ-2** | Seeding mapping | Place fields only (`environment`, `lighting`, `timeOfDay`, `objects`, `physicalLocation`); action stripped. Container located by prefix-match on `Scenario.Location.Name` (D14). |
| **OQ-3** | Reconcile the two models | Retire `ReferenceBootstrapLocationProfile`/`Reference` and `PromoteAcceptedLocationAsync`; converge on the B-145 `SceneAsset(Location)` container. No migration — `TRAILER` is test-only (D3, D8). |
| **OQ-4** | Source-agnostic ingest | No new bootstrap ingest is needed: creation happens in `/locations/{id}` through the existing `AddUploadedImageAsync` / `AddGeneratedImageAsync` / edit path (the B-108 candidate batch is retired for locations). |
| **OQ-5** | Naming at promotion | No promotion parameter: naming happens in the container via `SetImageDisplayNameAsync`, gated by `SceneAssetImageNaming.IsNameRequiredForApproval` (B-145). |
| **OQ-6** | Per-POV selection | Per-POV backdrop; `Sightline` is a hint only (never auto-selects). One view → that view is the only option and the operator still confirms (D12). |
| **OQ-7** | Moment→location persistence | Per-POV backdrop on `SceneImageProductionGroup`; the moment→container default in an explicit moment-level link (D13). |
| **OQ-8** | Repeat visits | Reuse as the default suggestion (D10); the operator may add views or pick another container. |
| **OQ-9** | Generate-all-POVs | Out of scope — a follow-on item. |
| **OQ-10** | Generalisation | Location-only for this item. Character/wardrobe use the existing identity studio (B-121 / B-122). |
| **OQ-11** | Hierarchy shape | First-class `ParentAssetId` on `SceneAsset`, arbitrary depth; world → location → (spot = named image) (D7). |
| **OQ-12** | Scenario↔container cardinality | `SceneAsset` gains `ScenarioLocationId` + `ScenarioId`; one current container per scenario location via version lineage (D8). |
| **OQ-13** | World linkage + description | `Setting.WorldLocation { AssetContainerId, RenderingDescription }`, separate from `WorldDescription` (D9). |
| **OQ-14** | Spot representation | A named image inside the location container — not a third container level (D7). |

## 5. Blast radius (settled — design-review verified 2026-10-04)

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs` | + `ParentAssetId`, + `ScenarioLocationId`, + `ScenarioId` on `SceneAsset` |
| `DreamGenClone.Web/Domain/Scenarios/Setting.cs` | + `WorldLocation { AssetContainerId, RenderingDescription }` — **payload-JSON only** (`Scenarios.PayloadJson`); no DB migration; old payloads deserialize null = not configured |
| `DreamGenClone.Domain/RolePlay/SceneImageProductionGroup.cs` | + per-POV backdrop field(s) — image id + sha256 + production version + label, mirroring the fields `ReferenceApplicationSelection` carries (a bare id is not enough for render revalidation) |
| new moment→container link record + repository | keyed on `MomentId` (D17); one current link; operator-confirmed |
| `DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs` | + 3 columns via the **existing pragma-guarded `ALTER TABLE` list** (~lines 1535-1555) + hierarchy / scenario-link queries |
| `DreamGenClone.Infrastructure/RolePlay/SceneImageProductionGroupRepository.cs` | + backdrop column — **and the pragma-guarded ALTER TABLE migration mechanism itself** (today this repo only has `CREATE TABLE IF NOT EXISTS`; a new column would never reach an existing DB) |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/` — new `LocationBackdropSlotPrefill` | the auto-seed (D16): a sibling of `IdentityPackSlotPrefill`; fills the blueprint's declared `Location` slot; refuses undeclared slots by name |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetTreeService.cs` | location roots re-sourced from `SceneAsset(Location)` containers + hierarchy (D19) — **hard dependency of D3**; today they come from `ReferenceBootstrapLocationProfile` and link an id `/locations/{id}` cannot resolve |
| `DreamGenClone.Web/Application/RolePlay/IReferenceBootstrapService.cs` + `ReferenceBootstrapService.cs` | **retire** `PromoteAcceptedLocationAsync`, the `Location` arm of `MapReferenceKind`, and location batch support (D3) |
| `DreamGenClone.Infrastructure/RolePlay/ReferenceBootstrapRepository.cs` (+ its interface) | remove the location profile/reference methods; **DB tables left in place** (D18 — dead tables, no destructive migration) |
| `DreamGenClone.Web/Components/Assets/ReferenceBootstrapPanel.razor` | drop the location branch (the panel itself is mounted nowhere and remains B-121's concern) |
| `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` | optional Stage 0 tab + panel (§6); per-POV backdrop; search-as-cards; **page-local workbench stage list** (D15) |
| `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor` | `WorldLocation` editor in the Setting/World section (~lines 130-171) |
| `DreamGenClone.Web/Components/Pages/LocationStudio.razor` | `?return=` back-link (the `ReviewDeck.BackUrl/BackLabel` pattern); the created container opens through the existing `AssetStudioView` tooling unchanged |
| `DreamGenClone.Tests/RolePlay/ReferenceBootstrapRepositoryTests.cs` | remove location-promotion coverage (~line 523) |
| `DreamGenClone.Tests/RolePlay/SceneAssetTreeServiceTests.cs` | re-source location roots; drop the location-profile repository fakes |
| `DreamGenClone.Tests/RolePlay/SceneImageStudioUiContractTests.cs` | the stage-array assertion (~line 289) + new Stage-0 contract assertions |
| `DreamGenClone.Tests/RolePlay/**SceneImage**` + new prefill tests | backdrops from named views; resolution rule; hierarchy + scenario link; prefill refusals |

**Change control:** this touches `DreamGenClone.Web/Application/RolePlay/**` — RP engine change
control applies (plan → explicit go-ahead → execute). It does **not** touch the narrative engine
files (`RolePlayEngineService.cs`, `RolePlayContinuationService.cs`, prompt slots), and it does
**not** touch any prompt compiler (the backdrop travels as a reference application, not prompt
text — `StepPromptElementPlan`'s location elements are unchanged).

**Process rules that apply to the implementation:**
- `.github/instructions/razor-editing.instructions.md` — four `.razor` files change; full-context
  reads, micro-steps, diagnostics after each edit.
- `.github/instructions/db-snapshot-workflow.instructions.md` — additive columns only; refresh the
  snapshot DB after implementation.
- Every implementation change leaves the test suite green (NFR-B148-04).

## 6. Detailed UI plan — the Stage 0 surface

### 6.1 Placement and the stage tab

- The Production POV workbench stepper gains a **leading tab: `0 Location`** before
  `1 Composition`. It joins the existing `tablist` (`role="tab"`, `aria-selected`, `tabindex`,
  and the `HandleProductionStageKeyDown` keyboard handling) — no new stepper mechanism.
- Stage-state badges reuse the existing `StageState` vocabulary. The Location stage is
  **Available** under exactly the same gate as Composition (the moment enrichment is complete);
  it is never "Locked" — it is optional (D6).
- The stage list is **page-local** (D15): `Location → Composition → Identity → Finish`. The
  persisted `ProductionStage` on image records is untouched; the first render is still
  `Composition`.

### 6.2 Panel layout — three zones

**Zone 1 — Suggestion (top).** When the moment's location resolves (D14), one suggestion card:

- the container breadcrumb (`Trailer Park → Husband and Wife Trailer — Shared Private Space`),
  rendered from the new hierarchy;
- the suggested spot image as a **large card** (not a thumbnail strip — operator direction);
- the match reason in plain text ("matched scenario location by name; spot 'the yard clothesline'"),
  and the POV's `Sightline` text as **context only** — a hint, never a pre-selection;
- two actions: **Use as backdrop** (primary) and **Dismiss** (collapses the suggestion; nothing
  is ever auto-bound).

**Zone 2 — Search (middle).** A search input (filters by container name and image name) over all
location assets, with results as a **card grid**:

- each card shows the image, its operator-entered name, its approval badge + production version,
  and its container breadcrumb; world containers and their images appear too (a park-level image
  is a legitimate backdrop);
- approved + named cards are **selectable**; every other card renders **disabled with its reason**
  ("needs a name" / "not approved") and a **Finish in Location Studio** link (D11) — a card the
  render would refuse is never offered as clickable.

**Zone 3 — Create / skip (bottom).** **Create location from this moment** (primary) and
**Skip — text-only location** (secondary, always available — D6).

### 6.3 The create-from-moment handoff

- Stage 0 calls `CreateAssetAsync` directly (name defaulted from the location string, editable),
  then navigates to `/locations/{asset.Id}?return=<this POV's url>` — the same create-then-navigate
  shape `AssetCreate.razor` already uses for locations, and the same `?return=` convention
  `AssetEdit` / `ReviewDeck` use.
- The place-only seed (D4) is stored on the container, so it is visible and editable in Location
  Studio; the operator then uses the existing upload / generate / edit / name / approve tooling
  there — unchanged.
- `LocationStudio` gains a **Back to production** link when `?return=` is present (the
  `ReviewDeck.BackUrl` / `BackLabel` pattern).
- Returning re-opens Stage 0 with the new container's images; the newest approved image becomes
  the suggestion card.

### 6.4 Per-POV behaviour

- The panel header shows: the current POV, its bound backdrop (image + operator name) or
  "Not set — text only", and the moment-level container link ("shared across this moment's POVs").
- Switching POV loads that POV's group (existing behaviour) and its **own** backdrop choice; the
  moment-level container link does not change.
- Binding writes the per-POV backdrop field on the group **and** the moment-level link (D17) —
  the link is written on the operator's bind action, never automatically.

### 6.5 Composition auto-seed UX

- Opening Composition Composer with a bound backdrop: the location slot shows the bound image
  (label = the operator's name) with form-text "Seeded from the Location stage — editable here."
- The seed fills exactly the blueprint's declared `Location` slot
  (`ForPackIdentityComposition` declares it, `AllowsMultiple: true`) via the prefill (D16); a
  blueprint that declares no location slot is **refused by name**, never dropped.
- A model change clears the bindings (existing behaviour); the location binding **re-seeds only
  after re-validation** against the new model's slot plan — if the new model declares no location
  slot, a notice says the model cannot carry the location reference. Never a silent drop.

### 6.6 Empty, busy and error states

| State | Presentation |
|---|---|
| No location assets exist at all | "No locations yet — create one from this moment." |
| A container has no approved images | Its cards render disabled + "Finish in Location Studio" links |
| Search returns no hits | "No matches — create from this moment." |
| No scenario location matches the moment's string | The suggestion zone says "No scenario location matched — this place is new" and offers create (the ad-hoc path is a **notice, not an error**) |
| Busy / failure | The existing `_busy` / alert patterns; every failure is an explicit message (D5) |

### 6.7 Accessibility

The stage tab joins the tablist with `aria-selected` / `tabindex` / keyboard handling; cards are
buttons whose aria-labels name the image and its container; disabled cards keep their reason text
readable; the suggestion card's "Use as backdrop" is a real button, never an auto-action.

## 7. Review gate

1. **Design pass** — ✅ complete (2026-10-04).
2. **Design review** — ✅ **performed 2026-10-04** (this revision). Findings, all resolved in this
   file:
   - **Governing constraint: held.** No creation technique is named as required; creation is
     through the existing `/locations` tooling (FR-B148-04).
   - **Two-model question: settled.** The B-108 location path is retired; the B-145 container is
     canonical (D3). The review surfaced a **hard dependency** the pass missed — the asset tree's
     location roots are built from the retired records (D19) — now in the blast radius.
   - **No silent default: held.** Name is required before approval; the resolution produces a
     *suggestion* the operator confirms; "no match → ad-hoc" is a legitimate branch (the place is
     genuinely new), not a value fallback, and the heuristic has no tunable thresholds — if one is
     ever added it must become UI-backed config (NFR-B148-03).
   - **Mechanism gaps closed:** the stage-enum question (D15), the auto-seed precedent and the
     ephemeral-bindings fact (D16), the link keying (D17), the migration mechanisms (D18), and the
     detailed UI plan (§6).
3. **`tasks.md` + `ui-contract.md`** — ✅ produced (2026-10-04). The only gate left is the
   explicit go-ahead (RP engine change control — §5).

## 8. References

### This package
- [`FINDINGS-location-images.md`](FINDINGS-location-images.md) — **read §0 first**: the
  structure-correlation metric does not measure identity, and one wrong answer passed it
- [`FINDINGS-depth-and-models.md`](FINDINGS-depth-and-models.md) — depth's real uses, the
  model/checkpoint pairing rules, and where depth belongs in this application
- [`OPTIONS-consistent-location.md`](OPTIONS-consistent-location.md) — the candidate sources
  (capture / 360 projection / reconstruction / generation / hybrid), ranked, and the blocking gap
- [`RESEARCH-minimax-m3-t2v.md`](RESEARCH-minimax-m3-t2v.md) — external research on the video
  camera-move option (Option I): model naming, local-vs-API, camera control, VRAM, sources
- `../B-151-fun-camera-16gb-proof/README.md` — the Fun-Camera 16GB proof plan (now its own item,
  B-151): the VRAM gate (Phase A) then the camera-move consistency test (Phase B)

### Elsewhere
- `specs/Planning/B-108-reference-bootstrap-studio/` — the absorbed standalone sibling
- `specs/Planning/B-111-consistent-visual-production/tasks/P2-tasks.md` — "P2 — Reference Bootstrap"
- `specs/Planning/B-111-consistent-visual-production/production-studio-unified-model.md`
- `specs/Planning/B-145-location-references/plan.md` — named location images (shipped)
- `specs/Planning/B-139-360-location-reference/plan.md` — a candidate source
- `specs/image-generator-tests/room-pov-proof/FINDINGS.md` — the measured limits of generation
