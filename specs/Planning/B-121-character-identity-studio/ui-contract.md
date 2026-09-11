# B-121 UI Contract — Character Identity Studio

**Extends:** `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/production-ui-contract.md`
(shared shell, state keys, keyboard/focus). Where silent, that contract governs.
**Related:** B-108 `ui-contract.md` (Bootstrap tab), B-111 `studio-reference-strategy-plan.md`
("/reference-bootstrap → Retire into Asset Manager").

**Route:** `/asset-studio/identity/{buildId}` — a sub-route of Asset Manager, so the studio lives with
the asset library (FR21-032) without cramming a seven-step pipeline into the existing panel.

## 0. Entry point

The existing **Build Reference** card (`ReferenceBootstrapPanel`) gains a single action:
`Build full identity pack…`. It creates a `CharacterIdentityBuild` for the selected character and
navigates to the studio route. The existing describe → generate → curate → promote controls remain
unchanged beside it (FR21-034).

## 1. Layout

```
[ Step rail (vertical) ]        [ Step panel ]                    [ Artifacts strip ]
 1 Front        ✓              <step-specific content>            [thumb][thumb][thumb]
 2 Validate     ✓
 3 De-clothe    ● current
 4 Crop         ○
 5 Enhance      ○
 6 Angles       ○
 7 Promote      ○
```

- The rail shows status per step: `NotStarted` / `Running` / `Complete` / `Failed` / `Skipped`
  (Skipped only ever appears after an explicit user skip, and is visually distinct from Complete).
- Selecting a step in the rail switches the panel. Switching steps never resets the artifacts strip.
- A step that is blocked shows an inline reason on its rail row, not only inside the panel.

## 2. Step 1 — Front

```
Source: ( ) Generate from description   ( ) Upload an image
  [Generate]  : description [multiline]  + [Character name]
  [Upload]    : [file picker] [drop target]
[Produced front preview]
```

- Both routes converge: after the front exists, the rail advances and the remaining steps are identical.
- The resolved prompt is shown read-only beneath, with an `Edit prompt` affordance opening §8.

## 3. Step 2 — Validate

```
[ Large preview with horizontal eye-level guide lines ]
Measured:  irisDy%  -0.62    interocular 197 px    [tool: measure_iris.py]
Gate:      PASS (|irisDy%| ≤ 1.50)
[ ] Manual override — reason: [____]   [Record override]
```

- Three outcomes: `Pass`, `Fail` (blocks advancement, reason stated), `NoFaceMesh`
  (blocks until a manual override is recorded — this is the expected case for profiles).
- The override control is always visible; it is never the only path and never auto-applied.
- Raw tool output is available behind a disclosure.

## 4. Step 3 — De-clothe

```
Input preview → Output preview (side by side)
Prompt: <resolved text, read-only>  [Edit prompt]
[Run]  [Skip this step]
```

The output is always a **new** artifact; the input is never overwritten (FR21-003).

## 5. Step 4 — Crop

```
[Preview with draggable crop frame + guides]
Headroom [8] %   Aspect [1.00]   [Apply]  [Reset]  [Skip]
```

Applied in-process (ImageSharp); no model call, so it is immediate and free to retry.

## 6. Step 5 — Enhance

```
[Before/after preview]   Upscaler: <configured name>   4× → [1024] px
[Run]
⚠ Enhancement re-synthesises facial detail. Do not apply it to the front before you have judged
  likeness — sharpening can change how much the face reads as the person.
```

The warning is **mandatory and always shown** on this step (FR21-021).

## 7. Step 6 — Angles

Four view cards, produced in order 3/4L → 3/4R → ProfileL → ProfileR:

```
┌ Three-quarter left ─────────────┐
│ [image]                         │
│ yaw -0.387  ✓ image-left        │
│ rendered directly               │
│ [Re-run] [Edit prompt]          │
└─────────────────────────────────┘
┌ Three-quarter right ────────────┐
│ [image]                         │
│ yaw +0.378  ✓ image-right       │
│ mirrored from Three-quarter left│
│ [Re-run] [Edit prompt]          │
└─────────────────────────────────┘
```

- Each card shows the **measured yaw sign**, the convention verdict, and — when applicable — the
  `mirrored from …` attribution. Mirror-derived views are labelled, never passed off as renders.
- A view that fails the convention and cannot be remedied shows `Blocked` with the reason and the
  remedy options (re-run, enable mirroring for this view).
- Profile cards show `visual confirmation required` instead of an eye-gate verdict, because the eye
  tool returns no face mesh on a full profile. A profile is confirmed by an explicit user tick.
- Re-running one view re-runs only that view.

## 8. Prompt editor (shared by every step)

```
Prompt: identity.angle.three-quarter
Scope:  ( ) Global default     (•) This character (Dean)
[ multiline text area ]
Supplied by: character override
Last used: 2026-09-11 14:02  (build 7f3…  step Angles)
[Save]  [Reset to default]  [Cancel]
```

- **Supplied by** always states which scope produced the text currently in effect — the user must never
  have to guess whether they are looking at a global or a character prompt.
- `Reset to default` restores the immutable `SeedBody` and requires a confirmation, since it discards
  the user's edit.
- If the global row for a key is missing, the editor shows `Missing required template: <key>` and the
  step's Run control is disabled with that exact reason (FR21-007); no prompt is invented.
- Placeholders (`{CharacterName}`, `{Description}`, `{SubjectPronounPossessive}`) are listed beneath
  the text area and validated on save; an unknown or unbalanced placeholder is rejected with the
  offending token named.
- Editing a template never re-runs anything automatically; the UI states `Affects the next run`.

## 9. Step 7 — Promote

```
View set:  Front ✓  3/4L ✓  3/4R ✓  ProfL ✓  ProfR ✓
Gates:     validate ✓   yaw ✓   quality ✓
Target:    Dean — draft identity pack v8
[Promote view set to identity pack]
```

- The Promote action is disabled while any view is missing or failed, and the disabled state names the
  offending view(s) (FR21-028).
- After promotion: `Added 5 views to Dean's draft identity pack, version 8.` with a link to
  `/characters/identity` and the assigned `SceneImageReferenceFaceView` per view listed.
- The created pack id is recorded on the build and shown afterwards.

## 10. States

| Region | Empty | Loading | Failure |
|---|---|---|---|
| Rail | n/a | step spinner | step marked `Failed`, reason inline |
| Front | "Choose a source to begin." | generation spinner | error + `Retry` |
| Validate | "Run validation to continue." | "Measuring…" | tool-missing failure names the interpreter path |
| De-clothe / Enhance | "Not run yet." | spinner naming the model | model error + `Retry` |
| Crop | input preview | n/a (in-process) | invalid parameter named |
| Angles | "Front required first." | per-view spinner | per-view failure, isolated to that view |
| Promote | "All views required." | spinner | promotion error naming the exact reason |
| Artifacts | "No artifacts yet." | skeleton | n/a |

## 11. Accessibility and focus

- Roving tabindex on the step rail and the artifacts strip; Enter opens, Space selects.
- The step panel is labelled by the current step's rail row for screen readers.
- Yaw/convention verdicts carry text equivalents (`✓ image-left`), never colour alone.
- The mandatory enhance warning is a `role="note"`, not a tooltip.
- Icon-only actions carry both `title` and an accessible label.

## 12. State keys

- `IdentityStudioBuildId`
- `IdentityStudioStep` — `Front` | `Validate` | `GarmentRemoval` | `Crop` | `Enhance` | `Angles` | `Promote`
- `IdentityStudioSelectedArtifactId`
- `PromptEditorKey` / `PromptEditorScope` (`Global` | `Character`)
- `AnglesSelectedView`
- `CompareArtifactIds` (ordered pair, at most two)

Refresh preserves every key whose referenced record still exists. Navigating away and back to a build
restores the step and the selected artifact.
