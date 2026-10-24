# B-148 — Requirements (settled — design pass complete, for design review)

> **Status:** settled. These requirements describe *what the flow must guarantee*, not *how images
> are made*. Any requirement that names a creation technique is a defect in this document — see
> `README.md` § *The governing constraint*. All open questions from the draft are resolved; see
> `plan.md` § *Resolved design questions*.

---

## 1. Context

The RP engine invents locations at random. Producing a moment's POV images requires location
backdrops that do not exist yet and were never registered. Therefore location creation is a **step
of moment production**, not a precondition for it.

## 2. Functional requirements

### FR-B148-01 — Stage 0 placement
The operator must be able to create a location **from the currently selected moment**, inside the
production flow, without leaving the surface and without pre-registering the location. The
`Location` stage sits **before Composition** in the Production POV workbench and is **optional**:
skipping it leaves the composition with a text-only location.

### FR-B148-02 — Seed from the moment
The creation surface must be pre-filled from the moment's own frozen state. The seeded description
must describe **the place**, not the action occurring in it — a location reference is a backdrop,
and a backdrop containing the moment's action would be wrong for every other moment at that place.

The seed is built from the place fields only — `environment`, `lighting`, `timeOfDay`, `objects`,
and the characters' `physicalLocation` spatial layout — with **action, people and nudity stripped**
(`visualDescription`, `continuityState`, and the mood's action component are excluded). The
operator may edit it.

### FR-B148-03 — Dynamic container, no fixed view set, with a hierarchy
A location is a container that holds an arbitrary, operator-named set of images. **No view enum, no
per-container view catalog, and no seeded front/left/right/rear/interior list.** A *spot* (e.g.
"the yard clothesline") is a named image inside the container, not a separate container.

Containers form a **hierarchy** via an optional parent: the world/Setting container ("Trailer
Park") is the root, and each scenario location ("Husband and Wife Trailer — Shared Private Space")
is a child container. Repeat moments at the same place reuse the same container; a newly invented
place creates a new one.

### FR-B148-04 — Source-agnostic creation *(closes the blocking gap)*
A location image may be created by **any** technique available in the asset manager:
- generation (any model or tool on the image creation surface),
- upload (a photograph the operator took),
- derivation (a view produced from another asset — e.g. projection from a panorama, an edit, or a
  frame extracted from a generated camera-move video).

The flow must not require, prefer, or assume any one of these, and adding a new creation tool must
not require a change to this pipeline. Creation happens inside `/locations/{id}` through the
existing upload / generate / edit tooling; the bootstrap candidate batch is retired for locations.

### FR-B148-05 — Per-view user naming
Each location image must be named by the operator, and the name must be how the image is retrieved
(shown in the picker). Names are **required** for location images (refused with an explicit message
when absent, never defaulted) — enforced by the existing `SceneAssetImageNaming.IsNameRequiredForApproval`
gate, so the form and the store can never disagree.

### FR-B148-06 — An approved, named image is bindable
An approved, named location image must be offered by the existing `ReferencePicker` as a choice,
with no new picker mechanism. The B-108 file-bearing promotion path is **retired** — location
images are produced only through the B-145 container + approved, named, versioned image path.

### FR-B148-07 — Per-POV backdrop selection, auto-seeded into Composition
The operator must be able to choose, per POV of the moment, which named location image is that
POV's backdrop. The moment's `Sightline` data may *suggest* a view, but must never silently select
one — the operator always confirms. The chosen backdrop **auto-seeds the Composition's location
reference**, remaining editable in the composer. Omniscient and each character POV keep their own
binding: the stage resolves THAT POV's production group, one POV's bind never overwrites another's,
and the stage says so on screen rather than claiming the binding is shared.

### FR-B148-07a — The stage lists the matched location's images *(amended 2026-10-14)*
The image list in Stage 0 is the images of the location asset the moment's place resolved to, and
nothing else, with the search box filtering those images. A moment whose place matches no location
asset has no image list and is told why. Reaching the rest of the image library is one deliberate
action — **Search all image assets** — which is the shared in-app image browser in its asset-group,
search and bindable-only modes, and which binds through the same single write the inline list uses.

### FR-B148-08 — The moment→location link is created, not assumed
The link between a moment and its location container is established **after** the location exists,
and persists so that subsequent POVs of the same moment inherit it. The per-POV backdrop lives on
the production group — the working copy the composer's prefill reads for the CURRENT enrichment —
and the DURABLE record is an explicit link **keyed on (moment id, POV)**: the moment id is stable
across enrichment revisions (a link keyed on the enrichment id would be lost on every re-enrichment),
and the POV is half the key because Omniscient and each character keep their own binding (a link
keyed on the moment alone would collapse every POV into one). That link carries the bound image's
id, checksum, production version and operator label, because it is what a fresh enrichment's group
backdrop is restored from, and it is written **only by the operator's bind action** — never
automatically. *Amended 2026-10-14: originally one link per moment, shared across the moment's POVs.*

### FR-B148-09 — Fail loudly
If a required value is missing (location name, an image name at approval, a bound reference that
cannot be carried), the flow fails with an explicit message. No silent substitution, no defaulted
name, no unconditioned render.

### FR-B148-10 — Location hierarchy
`SceneAsset` carries an optional parent, forming an arbitrary-depth hierarchy: world/Setting
container (root) → scenario-location containers (children). The world container is itself an
ordinary `Location`-type container (the root, linked by `Setting.WorldLocation`); spots are named
images inside the location container, never a third container level.

### FR-B148-11 — Scenario-location mapping
`SceneAsset` carries `ScenarioLocationId` + `ScenarioId`, linking a container to a scenario
location **by ID**. One *current* container per scenario location, resolved via the existing
version lineage. `ScenarioLocationId` null means the location is **ad-hoc** (invented by the RP
engine) and is linked only via the moment→container link. **Name is the lookup key; ID is the
relation.**

### FR-B148-12 — World location on the Setting
`Setting` carries a structured `WorldLocation { AssetContainerId, RenderingDescription }`, linking
the world/Setting to its root container and carrying a rendering-oriented description, kept
separate from the narrative `WorldDescription`.

### FR-B148-13 — Reuse is a default suggestion, never a lock
When a moment's location resolves to an existing container, that container (and its best-matching
named image) is offered as the **default**, but the operator may bind any approved image — even an
unrelated one — to any POV.

### FR-B148-14 — Search-as-cards with disabled non-selectable images
The location list shows the matched location's own images as cards. Approved, named, checksummed
images are selectable; every other image renders **disabled** with its reason and a "finish in
Location Studio" link. The eligibility decision is the domain's single rule
(`SceneImageLocationBindGate.BlockerFor`), so the list and the bind cannot disagree. *(Amended
2026-10-14: the list used to span every location container.)*

### FR-B148-15 — Location resolution rule
The moment's composite `location` string resolves by **prefix-match** against
`Scenario.Location.Name`: found → its container by `ScenarioLocationId`; the `"- <spot>"` suffix
token-suggests a named image; no prefix match → **ad-hoc**. Every match is a suggestion the
operator confirms.

## 3. Non-functional requirements

- **NFR-B148-01** — No new creation technique may be required to satisfy these requirements.
- **NFR-B148-02** — The flow must work for a location invented seconds earlier, with no
  pre-existing registration.
- **NFR-B148-03** — All RP behaviour controls introduced must be configurable in UI-backed
  persisted data, not code-only defaults (repo rule).
- **NFR-B148-04** — Every implementation change leaves the test suite green (repo rule).

## 4. Explicitly out of scope

- Choosing, tuning or qualifying any creation technique.
- Any per-container view catalog or fixed view enum.
- Re-designing B-108's absorbed *character* machinery (B-111 `P2-tasks.md`: "This phase EXECUTES
  B-108. Do not redesign."). The B-108 **location** path is retired by this item (FR-B148-06).
- Generate-all-POVs batch behaviour — a follow-on item.

## 5. Design questions resolved

See `plan.md` § *Resolved design questions* (OQ-1 … OQ-14). The two competing location models are
reconciled: `ReferenceBootstrapLocationProfile`/`Reference` is retired, and the B-145
`SceneAsset(Location)` container is canonical.
