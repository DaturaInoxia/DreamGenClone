# B-108 UI Contract — Asset Manager Bootstrap Tab

**Extends:** `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/production-ui-contract.md`
(Asset Manager shared shell, state keys, keyboard/focus rules). Where silent, that contract governs.

**Route:** `/asset-studio` (new `Bootstrap` tab alongside existing `Browse`, `Versions`, `Lineage`)

## 1. Tab and target picker

A new tab, `Bootstrap`, added to the existing Asset Manager tab row (P2-051 rule: tab selection
never clears entity selection in other tabs).

Inside the tab, a target picker precedes everything else:

```
Target: ( ) Character Face   ( ) Character Full Body   ( ) Character Wardrobe   ( ) Location
         [character dropdown, shown for the three character targets]
         [location profile dropdown + "New location profile" action, shown for Location]
Description: [multiline text]
Candidate count: [1-8, default 4]
[Generate Candidates]
```

- Switching target clears the description and count but preserves the current batch list below
  (batches are historical, independent of the picker's current selection).
- `Generate Candidates` is disabled until a target and a non-blank description are set; the
  disabled state states which is missing.

## 2. Candidate gallery

Below the picker, a gallery grouped by batch, most recent first:

```
Batch <id> · <target label> · <created time> · N candidates
[thumb][thumb][thumb][thumb][thumb][thumb]
```

Per-thumbnail overlay: decision badge (`Undecided` / `Accepted` / `Rejected`), and — for expansion
candidates only — a small "from <source>" tag.

Per-thumbnail actions: `Accept`, `Reject`, `Note`, `Expand`, `Compare`, and, only when
`Accepted`, `Promote`.

Rejected candidates render dimmed but remain fully visible — never removed, never hidden by
default (FR8-002/US8.2).

## 3. Compare mode

Same interaction model as the B-106 Production Studio compare mode (`Single`/`Compare` toggle,
exactly two candidates, third replaces oldest, equal-size split panes). Differences shown: batch,
decision, source (if an expansion), compiled prompt, model.

## 4. Expand action

Opens a small panel:

```
Expand "<candidate thumbnail>"
View: [dropdown: Three-quarter left / Three-quarter right / Profile left / Profile right /
       Body — standing / Body — seated / Wardrobe — front / Wardrobe — three-quarter]
[Run Expansion]
```

The dropdown's options are scoped to the target type (face angles for Face targets, pose options
for FullBody, angle/lighting options for Wardrobe). Running expansion adds a new candidate to the
same batch with `CandidateSourceAssetId` set to the expanded candidate, and selects it once
complete. A one-line note states expansion is a same-image edit and does not merge in any other
photo (D5/FR8-007) — set expectations plainly rather than silently under-delivering.

## 5. Promote action

Available only on `Accepted` candidates. Its label and effect depend on target:

| Target | Button label | Effect |
|---|---|---|
| Character Face | `Promote to Canonical Face` | Uploads into the character's identity pack as a `Face` asset (FR8-008) |
| Character FullBody | `Promote to Full-Body Reference` | Uploads into the identity pack as a `FullBody` asset |
| Character Wardrobe | `Promote to Wardrobe Look` | Creates a `CharacterWardrobeAssetBinding` on a Draft `CharacterWardrobeLookVersion` (FR8-009) |
| Location | `Promote to Location Reference` | Creates a `ReferenceBootstrapLocationReference` on the selected profile (FR8-010) |

Attempting to promote a non-`Accepted` candidate is impossible from the UI (the button is absent,
not disabled-with-no-reason, since the decision state already fully explains it) — for API/service
callers the same rule fails explicitly (FR8-011).

After a successful promotion, a confirmation states exactly what changed ("Added as Face asset to
Dean's draft identity pack, version 3") and links to the destination page
(`/characters/identity`, the wardrobe view, or the location profile).

## 6. Location profile management (minimal)

A lightweight inline create/select control, not a separate page:

```
Location profile: [dropdown of existing ReferenceBootstrapLocationProfile] [+ New]
  New: Name [___] Description [___] [Create]
```

No coordinate frame, dimensions, or landmark fields appear anywhere in this control — that is
deliberately Phase 3 scope (D4).

## 7. Cross-target filtering

The existing Asset Manager filters (`AssetTypeFilter`, `ApprovalFilter`, `CharacterFilter`,
`SearchText`) extend with two more, scoped to the Bootstrap tab:

- `CandidateBatchFilter`
- `CandidateDecisionFilter` (`Undecided`/`Accepted`/`Rejected`/All)

Filters apply across all three target kinds simultaneously per US8.7/acceptance scenario 8.

## 8. Empty, loading, and failure states

| Region | Empty | Loading | Failure |
|---|---|---|---|
| Gallery | "No candidates yet. Generate some above." | skeleton thumbnails | generation error with `Retry` |
| Expand panel | n/a | spinner naming the requested view | edit error with `Retry` |
| Promote | n/a | spinner | promotion error naming the exact reason (e.g. "identity pack has no draft version and none could be created") |
| Location dropdown | "No location profiles yet." | n/a | n/a |

## 9. Accessibility and focus

Inherits the P2-051 rules (roving tabindex on the gallery, Enter to open, Space to toggle compare
selection, icon-only actions carry a `title` and accessible label). The compare panes are labelled
`Candidate A` / `Candidate B` for screen readers, matching the B-106 compare-mode convention.

## 10. State keys added by this contract

- `BootstrapTargetKind` — `CharacterFace` | `CharacterFullBody` | `CharacterWardrobe` | `Location`
- `BootstrapTargetCharacterId` / `BootstrapTargetLocationProfileId`
- `SelectedBatchId`
- `CandidateBatchFilter`, `CandidateDecisionFilter`
- `CompareCandidateIds` (ordered pair, at most two)
- `CanvasMode` — `Single` | `Compare` (Bootstrap-tab scoped, independent of Production Studio's)

Refresh preserves every key whose referenced record still exists; switching target clears only the
description/count inputs, never the batch history or filters.
