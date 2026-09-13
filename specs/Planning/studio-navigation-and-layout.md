# Studio Navigation & Layout (canonical)

**State:** Active — governs every plan that targets a studio surface.
**Created:** 2026-09-11.
**Supersedes:** the "grouped tree in the Asset Manager list" drawing in
`identity-and-reference-model.md` §4.1 — the list is now an **owner index**; per-owner detail
(pack versions, view sets, pipeline) lives inside the owner's **studio**, not in the list.

The user's model, stated once and binding on all items:

- The **Asset Studio** list shows **owners** only (Dean, Becky, Trailer, Test, …). It does **not**
  show identity-pack versions, view sets, or per-image rows.
- Each owner has a **studio** page with a fixed route pattern. A studio owns the owner's full
  lifecycle: create, edit, and the approval workflow.
- Owners are **types**: Character, Location, and later Wardrobe, Prop, Style. The route pattern is
  the same for every type; only the studio's sections differ.
- Location "views" (deck / side / front / bedroom) are **examples for one location**, never a fixed
  enum. Views are **data** (`ViewDescriptorJson` / a user label), so a Beach has beachfront / water /
  umbrella — a Trailer has deck / side / bedroom.

---

## 1. Routes (canonical)

| Surface | Route | Owner id | Notes |
|---|---|---|---|
| Asset Studio (index) | `/asset-studio` | — | owner index + "Create Asset" + Cleanup bucket |
| Character Studio | `/characters/{characterId}` | scenario character id (merged by name) | umbrella for a character |
| Location Studio | `/locations/{locationId}` | Location asset id today; location-profile id when profiles land | umbrella for a location |
| Wardrobe / Prop / Style Studio | `/wardrobe/{id}` · `/prop/{id}` · `/style/{id}` | same pattern, added as types arrive | — |
| Full create/edit workspace | `/asset-studio/{assetId}` | asset id | the shared engine, reused by every studio |
| Pack approval (legacy) | `/characters/identity` | query `characterId` | retained for existing links; absorbed into Character Studio |

`/asset-studio/{assetId}` is the shared **full-featured image create/edit** workspace. The studios
embed or link into it; they do not re-implement generate/upload/edit/review.

---

## 2. Asset Studio (finalized index)

```
Asset Manager                                  [+ Create Asset]
──────────────────────────────────────────────────────────────
  Becky        Character      → /characters/f58f959a-…
  Dean         Character      → /characters/faee1ec0-…
  Test         Location       → /locations/607e52a5-…
  TRAILER      Location       → /locations/f7c7f206-…
  ── Cleanup (unassigned) ────────────────────────────────────
    CharacterBody   [cards]
    CharacterFace   [cards]
```

- One entry per owner: **name** (link to the studio) + **kind** badge.
- **Create Asset** requires name + type. The name is the owner/group; the type selects the studio.
  A new owner appears in the list immediately.
- **Cleanup** is a non-navigable bucket holding unassigned legacy assets (grouped by type). Assets
  linked to a character or location never appear here — they belong to their studio.
- Filters (search / type / approval / character) apply within the list. Owner entries pass through;
  Cleanup rows filter normally.

---

## 3. Character Studio layout (`/characters/{characterId}`)

The umbrella for **everything about one character**. Four creation parts plus the approval surface,
each with many create/edit attempts using the shared workspace (DW Pose + identity + references).

```
Character Studio — Dean                              [Back to Asset Manager]
─────────────────────────────────────────────────────────────────────────────
 Header   [canonical front]  Dean · Campground Intimacy · pack v8 (Approved)

 Tabs / sections
 ┌──────────┬──────────────────────────────────────────────────────────┐
 │ Faces    │  B-121 7-step pipeline (see §3.1) — produces the face   │
 │ Body     │  B-122 target kind: BodyCard + full-body clothed/unclothed │
 │ LoRA img │  B-123 dataset cells (per-cell create/edit)              │
 │ LoRAs    │  B-123 trained artifacts per base model                  │
 │ Packs    │  identity-pack versions: approve / supersede / canonical │
 └──────────┴──────────────────────────────────────────────────────────┘
```

- **Faces** — the B-121 pipeline. Its output (a view-tagged view set) promotes into the identity
  pack. The pack is the **end result**, not the starting point.
- **Body** — the same pipeline machinery as a new **target kind** (FR21-035): `BodyCard` editor +
  full-body references in clothed and unclothed states.
- **LoRA images** — B-123 coverage cells; each cell is a user-driven exercise (source → pose → model
  → attempts → keep/discard), never a batch.
- **LoRAs** — B-123 artifacts: training profiles per base model, artifacts, and inference wiring.
- **Packs** — the current `/characters/identity` content (pack versions, approve, supersede,
  canonical slots) lives here as one section.

### 3.1 Faces section (the B-121 pipeline, embedded)

The B-121 `ui-contract.md` step-rail becomes this section's body — no change to the step mechanics:

```
[ Step rail ]  [ Step panel ]                        [ Artifacts strip ]
 1 Front  ✓     <step-specific content>               [thumb][thumb][thumb]
 2 Validate ✓
 3 De-clothe ●
 4 Crop     ○
 5 Enhance  ○
 6 Angles   ○
 7 Promote  ○
```

Steps: Front → Validate → Garment removal → Crop → Enhance → Angles → Promote. Prompt templates and
behavior settings are persisted rows (character override → global → fail fast). Promotion writes the
accepted view set into a draft `FaceOnly` pack and records the produced pack on the build.

---

## 4. Location Studio layout (`/locations/{locationId}`)

```
Location Studio — TRAILER                           [Back to Asset Manager]
─────────────────────────────────────────────────────────────────────────────
 Header   TRAILER · Location · N images

 [ + New view ]        view = a user-defined label (data, not an enum)
   deck      [imgs…]   + generate / upload / edit / review   ← shared workspace
   side      [imgs…]
   front     [imgs…]
   bedroom   [imgs…]

 Approve → location profile (approval workflow)
```

- Views/parts are **user-defined data** (`ViewDescriptorJson` label or an equivalent generic label),
  never a hardcoded enum. A Beach location has beachfront / water / umbrella.
- Each view holds many image attempts, each produced through the shared create/edit workspace with
  the full DW Pose + identity + reference features.
- Approval promotes a chosen image into the location profile (the approval workflow).

---

## 5. How the existing plans map onto this

| Plan | Current surface | Becomes |
|---|---|---|
| B-121 `ui-contract.md` | `/asset-studio/identity/{buildId}` step rail | the **Faces** section inside `/characters/{characterId}` |
| B-121 FR21-030/032 | promote links to `/characters/identity` | the **Packs** section inside the same studio |
| B-124 plan §B | grouped tree `Character → pack → view set` in the list | owner index in the list; pack/view-set detail moves into the studio |
| B-122 Phase 0 | "a new target kind" | the **Body** section of the Character Studio |
| B-123 Ph1–7 | cell workspace | the **LoRA images** section |
| B-123 Ph8 | LoRA use | the **LoRAs** section |
| B-108 `ReferenceBootstrapPanel` | Build Reference card in Asset Studio | the entry point that creates an owner and opens its studio |

---

## 6. Binding rules

1. The list is an **owner index**; it never renders pack versions, view sets, or per-image rows.
2. Studios are **type-scoped pages** with a fixed route pattern; every type plugs in without changing
   the index or the shared workspace.
3. Faces/body are **created** through a pipeline; the identity pack is the **result**.
4. Location/wardrobe/prop/style views are **data**, not enums.
5. All create/edit flows reuse the **shared workspace** (`/asset-studio/{assetId}`); no studio
   re-implements generate/upload/edit/review.
