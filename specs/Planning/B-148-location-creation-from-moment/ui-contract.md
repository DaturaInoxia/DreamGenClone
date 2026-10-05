# B-148 UI Contract — Location Stage 0 (create location from the moment)

**Extends:** `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/production-ui-contract.md`
(shared shell, state keys, keyboard/focus). Where this document is silent, that contract governs.

**Route:** `/roleplay/studio/{sessionId}/{interactionId}/production/moment/{momentEnrichmentId}`
(Production POV tab). **Component:** `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`.

**Design authority:** `plan.md` §6 (UI plan) and D1-D19. The Location stage is **optional** and
**page-local** (D15) — it is never a persisted `SceneImageProductionStage`, and no image is ever at
the Location stage.

## 1. Stage stepper

The workbench stepper gains a leading tab, always visible with an explicit state:

| Step | States |
|---|---|
| ⓪ Location | `Ready` · `Complete (bound: <image name>)` · `Not set — text only` |
| ① Compose | existing states (B-106) |
| ② Identity | existing states (B-106) |
| ③ Finish | existing states (B-106) |

Rules:

- Location is **never `Blocked`** — it is optional (D6). `Ready` means the moment enrichment is
  complete and a backdrop may be chosen; `Not set — text only` is the skip state and is not an error.
- Clicking the tab selects the stage as active; it never executes anything.
- The stage list is page-local (`Location → Composition → Identity → Finish`); the persisted
  `ProductionStage` on image records is untouched and the first render remains `Composition`.

## 2. Panel layout — three zones

```
┌───────────────────────────────────────────────────────────────────────────┐
│ Header: POV · bound backdrop (image + name, or "Not set — text only")     │
│         · moment location link ("shared across this moment's POVs")       │
├───────────────────────────────────────────────────────────────────────────┤
│ 1 · Suggestion (shown only when a match resolves)                          │
│   [ container breadcrumb: Trailer Park → Husband and Wife Trailer — … ]   │
│   [ large spot image card ]  match reason  ·  Sightline (context only)    │
│   [ Use as backdrop ]  [ Dismiss ]                                         │
├───────────────────────────────────────────────────────────────────────────┤
│ 2 · Search  [____ filter by container / image name]                        │
│   [card] [card] [card] [card] …   (approved+named selectable;              │
│                                    others disabled with reason + link)     │
├───────────────────────────────────────────────────────────────────────────┤
│ 3 · [ Create location from this moment ]   [ Skip — text-only location ]   │
└───────────────────────────────────────────────────────────────────────────┘
```

- Zones 2 and 3 are always present. Zone 1 collapses on `Dismiss` and on bind.
- Every suggestion and every search card is a **candidate**; nothing is auto-bound. The only
  action that writes anything is an explicit operator choice (D10, FR-B148-13).

## 3. Zone 1 — Suggestion

- Shown only when the resolution rule (D14) matches a scenario location; carries:
  - the container breadcrumb rendered from the hierarchy;
  - the suggested spot image as a **large card** (never a thumbnail strip);
  - the match reason in plain text ("matched scenario location by name; spot 'the yard
    clothesline'");
  - the POV's `Sightline` text as **context only** — a hint, never a pre-selection.
- `Use as backdrop` binds the suggested image (writes the group backdrop + moment link). `Dismiss`
  collapses the zone and leaves the stage in `Not set — text only`.
- "No match → ad-hoc" is a **notice, not an error**: the zone states "No scenario location matched
  — this place is new" and points at Zone 3.

## 4. Zone 2 — Search-as-cards

- Filters by container name and image name across all location assets; world containers and their
  images appear too (a park-level image is a legitimate backdrop).
- Each card shows the image, the operator-entered name, the approval badge + production version,
  and the container breadcrumb.
- Approved + named cards are **selectable**. Every other card renders **disabled with its reason**
  ("needs a name" / "not approved") and a **Finish in Location Studio** link — a card the render
  would refuse is never offered as clickable (D11, FR-B148-14).
- Selecting a card binds it immediately (same write path as Zone 1).

## 5. Zone 3 — Create / skip

- **Create location from this moment** calls `CreateAssetAsync` (name defaulted from the location
  string, editable), stores the place-only seed (D4), then navigates to
  `/locations/{asset.Id}?return=<this POV's url>`.
- **Skip — text-only location** is always available; it clears any bound backdrop for this POV and
  leaves the composition's location as the picker's existing "Text only" option (D6).

## 6. The create-from-moment handoff and return

- `LocationStudio` gains a **Back to production** link when `?return=` is present (the
  `ReviewDeck.BackUrl` / `BackLabel` pattern).
- The place-only seed is visible and editable in Location Studio; the operator uses the existing
  upload / generate / edit / name / approve tooling there — unchanged.
- Returning re-opens Stage 0 with the new container's images; the newest approved image becomes the
  suggestion card.

## 7. Per-POV behaviour

- The panel header shows: the current POV, its bound backdrop (image + operator name) or "Not set —
  text only", and the moment-level container link ("shared across this moment's POVs").
- Switching POV loads that POV's group (existing behaviour) and its **own** backdrop choice; the
  moment-level container link does not change.
- Binding writes the per-POV backdrop field on the group **and** the moment-level link keyed on
  `MomentId` (D17) — the link is written on the operator's bind action, never automatically.

## 8. Composition auto-seed

- On composer open with a bound backdrop, the `Location` slot shows the bound image (label = the
  operator's name) with form-text "Seeded from the Location stage — editable here."
- The seed fills exactly the blueprint's declared `Location` slot (D16). A blueprint that declares
  no location slot is **refused by name**, never dropped.
- A model change clears the bindings (existing behaviour); the location binding **re-seeds only
  after re-validation** against the new model's slot plan. A model that declares no location slot
  shows a notice ("this model cannot carry the location reference"), never a silent drop.

## 9. States

| Region | Empty | Loading | Failure |
|---|---|---|---|
| Suggestion zone | hidden | n/a | hidden (ad-hoc path shows a notice, not an error) |
| Search | "No locations yet — create one from this moment." | skeleton cards | error + `Retry` |
| Search (no hits) | "No matches — create from this moment." | n/a | n/a |
| Cards (unnamed/unapproved) | disabled + reason + "Finish in Location Studio" link | n/a | n/a |
| Create | n/a | spinner | error naming the exact reason, `Retry` |
| Header backdrop | "Not set — text only" | n/a | bound reference unresolvable → explicit message |

## 10. Accessibility and focus

- The Location tab joins the tablist with `aria-selected`, `tabindex`, and the existing keyboard
  handling; it is labelled "Location" for screen readers.
- Cards are buttons whose accessible names include the image's operator name and container
  breadcrumb; disabled cards keep their reason readable.
- The suggestion's `Use as backdrop` is a real button, never an auto-action; `Dismiss` returns focus
  to the suggestion card.
- Bind and skip announce their result via a single stable status region (B-032 shared shell).

## 11. State keys

- `LocationStageSearchText`
- `LocationStageSuggestedImageId` (the current suggestion's image, while the zone is open)
- `LocationStageOpenContainerId` (the container opened by create-from-moment, for return)

Refresh preserves each key whose referenced record still exists; a deleted suggestion or container
clears to "no record" with an explicit note, never an auto-selection (B-032 rule). The bound
backdrop itself is persisted on the production group (not session state), so it survives refresh
and re-navigation by construction.
