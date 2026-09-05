# B-106 UI Contract — Staged Production Studio

**Extends:** `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/production-ui-contract.md`
(P2-051 shared shell, state keys, keyboard/focus rules). Where this document is silent, that contract
governs. Where it adds detail, it is authoritative for the staged workflow.

**Route:** `/roleplay/studio/{sessionId}/{interactionId}`
**Component:** `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`

## 1. Layout regions

The staged workflow occupies the P2-051 three-region workbench plus the full-width bottom strip:

```
┌───────────────────────────────────────────────────────────────────────────┐
│ Header: Moment label · POV · group status · primary command               │
├───────────────┬───────────────────────────────────┬───────────────────────┤
│ Context rail  │ Production canvas                 │ Inspector             │
│ (280px)       │ (minmax(480px, 1fr))              │ (340px)               │
│               │                                   │                       │
│ Moment        │  ① Compose ② Identity ③ Finish    │ Cast & identity       │
│ POV           │  ─────────────────────────────    │ readiness             │
│ Cast readiness│  [ single | compare A|B ]         │ Attempt facts         │
│               │  stage controls for active stage  │ Lineage path          │
├───────────────┴───────────────────────────────────┴───────────────────────┤
│ Attempt tree strip — grouped by stage, branch-aware, fixed thumb size     │
└───────────────────────────────────────────────────────────────────────────┘
```

Desktop `>= 1200px`: `280px minmax(480px, 1fr) 340px`. Tablet stacks the inspector under the canvas.
Mobile is one column ordered context → canvas → inspector → attempt tree. No horizontal page overflow
at 390px. Canvas keeps a stable `16 / 10` ratio, `min-height: 240px`, `object-fit: contain`.

## 2. Stage stepper

Three steps, always visible, each carrying an explicit state:

| Step | States |
|---|---|
| ① Compose | `Not started` · `Ready` · `Running` · `Complete (n attempts)` · `Failed` |
| ② Identity | `Blocked (reason)` · `Ready` · `Running` · `Complete (n attempts)` · `Skipped — reason` · `Failed` |
| ③ Finish | `Blocked (reason)` · `Ready` · `Running` · `Complete (n attempts)` · `Failed` |

Rules:

- A step is clickable only when it has at least one attempt or is `Ready`. Clicking selects that
  stage as active; it never executes anything.
- A `Blocked` step shows its exact reason inline, never a generic disabled control. Examples:
  "Select a completed Composition attempt", "Becky has no approved identity pack".
- Only the active stage's controls render in the canvas (P2-051 rule: the UI does not present all
  commands at once).

## 3. Cast and identity readiness panel

Lives in the inspector, always visible once a production group exists. One row per known visible
character in the Moment:

| Column | Content |
|---|---|
| Character | Display name from the Moment enrichment |
| Pack | Approved pack version, or `None approved` |
| Canonical face | Thumbnail of the approved owned canonical face, or an explicit gap marker |
| State | `Ready` · `Missing pack` · `Draft only` · `Superseded only` |

Behavior:

- A row in any non-`Ready` state renders a link to `/characters/identity` scoped to that character.
- The panel header summarises `n of m characters ready`.
- When `IdentityPolicy` is `SkippedByUser`, the panel is replaced by a skip banner showing the
  reason, who recorded it, when, and a `Clear skip` action.
- The panel never infers or auto-selects a pack.

## 4. Stage 2 — Identity controls

Rendered in the canvas when step ② is active.

- **Parent selector:** shows the selected Composition attempt thumbnail and id. Defaults to the
  currently selected attempt when it is a completed Composition; otherwise prompts explicitly.
- **Ordered reference list:** read-only, one row per character in ordinal order showing ordinal,
  character, pack version, asset checksum prefix. This is what will be submitted.
- **Primary command `Apply Identity`:** enabled only when every character is `Ready` and a completed
  Composition parent is selected. Disabled state always states the blocking reason in text.
- **`Record identity skip`:** opens a modal requiring a non-empty reason. Submit is disabled while
  the reason is blank. Cancel returns focus to the opener.
- On success a new Identity child attempt is created, selected, and focus moves to it.

## 5. Stage 3 — Finish controls

Rendered in the canvas when step ③ is active.

- **Parent selector:** the selected eligible attempt (an Identity attempt, or a Composition attempt
  when a skip is recorded). Ineligible selections state why.
- **Edit instruction:** multiline input with word/character count.
- **Change class — required radio, no preselected value:**
  - `Cosmetic` — colour, lighting, detail, blemish, texture.
  - `Geometry` — pose, camera angle, framing, subject placement.
  Helper text: *"Geometry changes can wash out an applied face. The result will be marked
  identity-stale and will need another identity pass before approval."*
- **Adult-content toggle:** rendered only when the resolved editor model's content policy allows it.
  When disallowed it is absent and a one-line note states the configured editor is SFW-only.
- **Primary command `Run Edit`:** disabled until parent, instruction, and change class are all set.

## 6. Attempt tree strip

Replaces the current flat reverse-chronological strip.

- Grouped into three labelled lanes: `Compose`, `Identity`, `Finish`.
- Within a lane, children render under their parent with a visible connector, so a branch is legible
  at a glance. Siblings sit adjacent in creation order.
- Fixed thumbnail dimensions; status badges never resize a thumbnail.
- Each thumbnail carries: execution status, disposition (`active` / `shortlisted` / `rejected`),
  an approval marker when it is the current approved frame, and an `identity-stale` marker.
- Per-thumbnail actions: `Select`, `Shortlist`, `Reject`, `Branch from here`, `Compare`.
- Rejected attempts stay visible and dimmed. They are never removed and their lineage stays intact.
- Roving `tabindex`; Arrow keys move, Enter selects, Space toggles compare membership.

## 7. Compare mode

- Toggle in the canvas header: `Single` / `Compare`.
- Compare accepts exactly two completed attempts. Selecting a third replaces the older selection.
- Layout is a split canvas, A on the left and B on the right, sharing one stable aspect box so the
  two images are the same rendered size.
- Under each pane: stage, attempt id, model, seed, change class where applicable, and identity-stale
  marker.
- A `Differences` list between the panes names only the fields that differ (stage, model, settings,
  parent, references), computed from the persisted snapshots — no image analysis.
- Either pane can be approved directly from compare mode.
- Exiting compare restores the previously selected single attempt.

## 8. Approval surface

- `Approve` is enabled only for a completed attempt that passes the identity gate:
  `IdentityPolicy` is `SkippedByUser`, **or** the attempt is an Identity attempt, **or** it is a
  `Cosmetic` descendant of one, and it is not identity-stale.
- A blocked approve states the exact reason, for example "Identity required — this attempt has no
  identity pass" or "Identity-stale after a geometry edit".
- Approving shows the resulting decision version and checksum. A later approval creates a new
  version and marks the previous one superseded in the UI without mutating it.

## 9. Empty, loading, and failure states

Every region defines all three explicitly:

| Region | Empty | Loading | Failure |
|---|---|---|---|
| Canvas | "Create or load the production group to begin." | spinner with the running stage name | provider error text plus `Retry` |
| Readiness panel | "No known visible characters in this Moment." | skeleton rows | explicit resolution error naming the character |
| Attempt tree | "No attempts yet." | skeleton thumbnails | load error with `Refresh` |
| Compare | "Select two completed attempts." | n/a | n/a |

No region renders a bare disabled control without a stated reason.

## 10. Accessibility and focus

Inherits P2-051 rules. Additional requirements:

- The stage stepper is a `tablist` with `aria-selected` and Left/Right arrow navigation.
- The skip modal traps focus, is dismissible with `Escape`, and returns focus to its opener.
- Compare panes are labelled `Attempt A` and `Attempt B` for screen readers.
- Blocking errors receive focus through the shared status region; polling updates never steal focus.
- All state is conveyed by text or an accessible label, never colour alone.

## 11. State keys added by this contract

Added to the P2-051 Production Studio state key set:

- `ActiveStage` — `Composition` | `Identity` | `Finish`
- `SelectedParentAttemptId` — the parent chosen for the active stage
- `CompareAttemptIds` — ordered pair, at most two
- `CanvasMode` — `Single` | `Compare`
- `FinishChangeClass` — unset until the user chooses
- `SkipDialogOpen`

Refresh and polling preserve every key whose referenced record still exists, and clear only invalid
descendants. Changing the Moment or POV clears all six.
