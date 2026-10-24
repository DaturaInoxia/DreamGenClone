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
│         · this POV's own binding — Omniscient and each character separate │
├───────────────────────────────────────────────────────────────────────────┤
│ 1 · Suggestion (shown only when a match resolves)                          │
│   [ container breadcrumb: Trailer Park → Husband and Wife Trailer — … ]   │
│   [ large spot image card ]  match reason  ·  Sightline (context only)    │
│   [ Use as backdrop ]  [ Dismiss ]                                         │
├───────────────────────────────────────────────────────────────────────────┤
│ 2 · Search  [____ filter the matched location's images]                   │
│   [card] [card] [card] [card] …   (approved+named selectable;              │
│                                    others disabled with reason + link)     │
├───────────────────────────────────────────────────────────────────────────┤
│ 3 · [ Search all image assets ]  [ Create location from this moment ]     │
│     [ Skip — text-only location ]                                         │
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

## 4. Zone 2 — Search-as-cards *(amended 2026-10-14)*

- Lists the images of the **matched location asset** — the one the moment's place resolved to — and
  nothing else. The search box filters those images by their operator-entered names; it no longer
  searches other containers, because a grid of every place's images is the library, not a candidate
  list for this moment.
- When the place matches no location asset there is no list at all (the panel says so), and the
  library is reached through **Search all image assets** (Zone 3).
- Each card shows the image, the operator-entered name, the approval badge + production version,
  and the container breadcrumb.
- Approved + named + checksummed cards are **selectable** — the eligibility rule is the domain's
  (`SceneImageLocationBindGate`), so the list can never offer an image the bind would refuse. Every
  other card renders **disabled with its reason** ("needs a name" / "not approved" / "no checksum")
  and a **Finish in Location Studio** link (D11, FR-B148-14).
- Selecting a card binds it immediately (same write path as Zone 1).

## 5. Zone 3 — Search-all / create / skip

- **Search all image assets** opens the shared `InAppImagePicker` (in its asset-group, search and
  bindable-only modes) as a modal form: choose asset groups, search, view an image full size, and
  **Bind as this POV's location backdrop**. The bind goes through the SAME single write the inline
  list uses — the modal is a way to reach the image, not a second bind path.
  *(Amended 2026-10-19: the picker marks the chosen card — highlight and pressed select button — and
  its cards carry only the picker's own commands (select + view larger), not the review deck's
  candidate-batch link. A failed pick or bind states its reason in the modal footer / the Location
  stage instead of only in the log, and a successful bind reports in the stage — otherwise rebinding
  the image already bound is silent and the Bind button looks inert.)*
- **Create location from this moment** calls `CreateAssetAsync` (name defaulted from the location
  string, editable), stores the place-only seed (D4), then navigates to
  `/locations/{asset.Id}?return=<this POV's url>`.
- **Skip — text-only location** is always available; it clears any bound backdrop for this POV —
  **and the image on this POV's link**, so a later enrichment does not restore what was just
  cleared — and leaves the composition's location as the picker's existing "Text only" option (D6).

## 6. The create-from-moment handoff and return

- `LocationStudio` gains a **Back to production** link when `?return=` is present (the
  `ReviewDeck.BackUrl` / `BackLabel` pattern).
- The place-only seed is visible and editable in Location Studio; the operator uses the existing
  upload / generate / edit / name / approve tooling there — unchanged.
- Returning re-opens Stage 0 with the new container's images; the newest approved image becomes the
  suggestion card.

## 7. Per-POV behaviour

- The panel header shows: the current POV, its bound backdrop (image + operator name) or "Not set —
  text only", and states that the binding is **this POV's own** — Omniscient and each character keep
  theirs separately. *(Amended 2026-10-14: the header used to say "shared across this moment's POVs",
  which described the old moment-level link and contradicted the per-POV backdrop beside it.)*
- Entering the stage resolves **this POV's** production group through the same
  `ISceneImageProductionService.GetOrCreateGroupAsync` call the composer button makes, then re-seeds a
  fresh group's backdrop from this POV's recorded binding (`RestoreBackdropAsync`). A bind that cannot
  be recorded fails with an explicit message; the stage never returns quietly as if it had bound.
- Switching POV loads that POV's group and its **own** backdrop choice.
- Binding writes the per-POV backdrop field on the group **and** that POV's link, keyed on
  (`MomentId`, `POV`) (D17) — the link is written on the operator's bind action, never automatically.
- The list under the search box is the **matched location asset's own images**. A place that matches
  no location asset gets no list, and is told so; the rest of the library is behind
  **Search all image assets**.

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
