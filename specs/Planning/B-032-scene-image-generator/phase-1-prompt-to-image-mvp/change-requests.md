# Scene Image Generator — Change Requests

**Feature**: 001-scene-image-generator (B-032)  
**Purpose**: Persistent, chronological log of change requests against the Scene Image Generator spec and implementation. Each batch is appended over time so the evolution of the feature is traceable. This file is the source of truth for requested-but-not-yet-implemented changes.

## Status Legend

| Status | Meaning |
|--------|---------|
| `requested` | Logged, not yet designed or implemented |
| `designed` | Design/approach agreed, not implemented |
| `implemented` | Code change landed |
| `verified` | Validated in the running app / POC |
| `rejected` | Deliberately not doing, with reason |

---

## Summary

| ID | Change | Status |
|----|--------|--------|
| CR-001 | Story Moment narrative text contrast is unreadable (white bg / light grey text) | `implemented` |
| CR-002 | Make Style default to Cartoon and Allow Explicit default to selected | `implemented` |
| CR-003 | Store the prompt + all generation parameters with each image to enable "continue from this image" | `implemented` |
| CR-004 | Scene Context Intensity shows "unknown" but should be resolved | `implemented` |
| CR-005 | Include character physical description so images of the same character look like the same person (likeness) | `implemented` |
| CR-006 | Preprocessor refinement: beats + participants + full-turn + POV + transparency (Options 1–4) | `implemented` |

---

## CR-001 — Story Moment narrative text contrast

**Date logged**: 2026-08-20  
**Status**: `implemented`  
**Area**: Studio UI (Story Moment panel)

### Problem

The **Story Moment** panel in `SceneImageStudio.razor` renders the narrative interaction text on a **white background** (Bootstrap `.bg-light`) with the app's default **light grey** body text. The result is near-zero contrast — the narrative text is barely readable.

### Current behavior

```razor
<div class="border rounded p-2 bg-light" style="max-height: 320px; overflow: auto; white-space: pre-wrap;">
    @_interaction?.Content
</div>
```

- White card background + light-grey text = illegible.
- Same pattern is used for the **Rendered Images** thumbnails? (verify) and other panels.

### Desired behavior

The narrative text must be readable. Prefer a **dark panel** (e.g. `bg-dark text-light`, or a scoped CSS class) consistent with the rest of the app's dark theme. Keep the scroll + `pre-wrap` behavior.

### Implementation (2026-08-20)

- Replaced the inline `.bg-light` div with a scoped `.scene-image-story-moment` panel (dark `#161616` background, light `#e6e6e6` text, `1px #2b2b2b` border) defined in the new `SceneImageStudio.razor.css`.

### Affected files

- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (Story Moment panel markup)
- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor.css` (new scoped styles)

---

## CR-002 — Default style = Cartoon, Allow Explicit = selected

**Date logged**: 2026-08-20  
**Status**: `implemented`  
**Area**: Studio defaults (US2 settings seeding)

### Problem

The studio opens with `Style = realistic` and `AllowExplicitImage` unchecked. The user wants the defaults to be **Cartoon** and **explicit allowed selected**.

### Current behavior

- `SceneImageStudioSettings.Style` default = `"realistic"` (`DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs`).
- `SceneImageStudioSettings.AllowExplicitImage` default = `false`.
- Studio seeds from `WorkspaceSettingsState` on open (`SeedSettingsFromWorkspace`), whose `AllowExplicitImage` also defaults `false` and `ImageStyleSuffix` is null.

### Desired behavior

- On studio open (and when no explicit override exists), `Style` = `cartoon` and `AllowExplicitImage` = `true` (checked).
- NOTE: the content-policy clamp must still win — if the resolved provider policy is `SfwFiltered`, the toggle is disabled and the prompt is clamped regardless of the default (existing behavior must not regress).

### Implementation (2026-08-20)

- `SceneImageStudioSettings`: `Style` default `"realistic"` → `"cartoon"`; `AllowExplicitImage` default `false` → `true`.
- `WorkspaceSettingsState.AllowExplicitImage` default `false` → `true`.
- SFW clamp unaffected: the toggle remains disabled when the resolved policy is `SfwFiltered`.

### Affected files

- `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` (defaults)
- `DreamGenClone.Web/Domain/RolePlay/WorkspaceSettingsState.cs` (defaults)

---

## CR-003 — Persist prompt + parameters with each image (enable "continue from image")

**Date logged**: 2026-08-20  
**Status**: `implemented`  
**Area**: Persistence (SceneImages) + Studio (US3 continue/iterate)

### Problem

When a user wants to **continue** from an existing image — reuse its prompt and the exact settings that produced it to generate the next image — the app does not currently retain everything needed on the image record.

### Current behavior

`SceneImages` (`SceneImageRecord`) persists:
- `PromptSnapshot` (the prompt text actually sent)
- `ImageSize`, `Style`
- `ContentPolicy`, `ModelIdentifier`, `ProviderName`

But it does **not** persist the full settings used:
- `AspectRatio` (nullable, not stored on the image row)
- `AllowExplicitImage` (not stored on the image row)
- The full `SettingsJson` is only on the linked `SceneImagePromptRecord` (`PromptRecordId` → `SceneImagePrompts.SettingsJson`), which is a *prompt* row, not an *image* row, and is not guaranteed to survive/be per-image.

There is currently no studio UI action on a rendered image to "load these settings + prompt and generate from here".

### Desired behavior

- Store a **full settings snapshot** with each image: add `SettingsJson` (or explicit `AspectRatio` + `AllowExplicitImage` + `Style` + `ImageSize`) to `SceneImages` so each image is self-describing and reconstructible without joining the prompt row.
- Add a **"Continue" / "Use this"** action on each image in the studio results strip that:
  1. Loads that image's `PromptSnapshot` into the editable prompt textarea,
  2. Loads that image's stored settings into the settings panel,
  3. (optionally) sets `RegenerateOfId` so the new render is linked as a child of the source image.
- Persist the settings from the render request at enqueue time (currently `SceneRenderRequest` carries `ImageSize` only — extend to carry the full settings or resolve from the prompt record at enqueue and snapshot it).

### Implementation (2026-08-20)

- Added `SceneImageRecord.SettingsJson` (full `SceneImageStudioSettings` JSON) + guarded `SceneImages.SettingsJson TEXT NOT NULL DEFAULT '{}'` migration.
- `SceneRenderRequest` now carries `SettingsJson`; `SceneImageService.EnqueueRenderAsync` snapshots it onto the record and extracts `Style`/`ImageSize` for display.
- Studio: **Continue (↻)** button on each completed image restores the image's `PromptSnapshot` + `SettingsJson` into the prompt/settings panels and sets `_continueFromImageId`, which the next render uses as `RegenerateOfId` (child link).
- Tests: `EnqueueRenderAsync_SnapshotsSettingsAndStyle` (service) + SettingsJson assertions in the repository round-trip test.

### Affected files

- `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` (add settings snapshot field)
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (guarded migration for new column)
- `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs` (schema + read/write mapping)
- `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs` (`EnqueueRenderAsync` snapshot)
- `DreamGenClone.Web/Application/RolePlay/Models/SceneRenderRequest.cs` (SettingsJson)
- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (Continue action)
- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor.css` (Continue button style)

---

## CR-004 — Scene Context Intensity shows "unknown"

**Date logged**: 2026-08-20  
**Status**: `implemented`  
**Area**: Studio context summary (US1)

### Problem

The **Scene Context** panel in the studio shows `Intensity: unknown`, but the resolved intensity is known and should be displayed.

### Current behavior

```razor
<li class="list-group-item d-flex justify-content-between">
    <span class="text-muted">Intensity</span><span>@(_session.LastResolvedIntensityLabel ?? "unknown")</span>
</li>
```

`_session.LastResolvedIntensityLabel` is null for the loaded session in the studio context, so it falls back to `"unknown"`.

### Investigation needed (before implementing)

- Where is `LastResolvedIntensityLabel` populated? It may only be set transiently during engine continuation and not persisted on the session blob.
- The durable source is likely the **V2 adaptive state** (`RolePlayV2AdaptiveStates`) — the studio should resolve intensity from the same source the prompt pre-processor uses (`session.LastResolvedIntensityLabel` is also used in `SceneImagePromptPreprocessor.BuildUserPrompt`), or from `AdaptiveScenarioState` / the active intensity profile.
- The studio loads only `RolePlaySession` today; it may need `IRolePlayStateRepository.LoadAdaptiveStateAsync` or the profile resolver to obtain the label.

### Desired behavior

- Show the resolved intensity label (e.g. `SensualMature`) instead of `unknown`.
- Keep the same label source consistent between the **Scene Context** panel and the **pre-processor prompt** so the user sees what the model was told.

### Implementation (2026-08-20)

- Studio now loads intensity profiles via `StoryAnalysisFacade.ListIntensityProfilesAsync` and resolves the label with `RolePlayStyleResolver.ResolveEffectiveStyle` (same resolution the workspace uses) — `_resolvedIntensityLabel` drives the Scene Context row.
- `SceneImagePromptGenerationJobHandler` resolves the same label (new `ResolveIntensityLabelAsync` + `StoryAnalysisFacade` injection) and sets `session.LastResolvedIntensityLabel` before building messages, so the prompt shows the same resolved label the user sees (no more `"unknown"` in the prompt either).

### Affected files

- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (load + display)
- `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` (resolve label before building prompt)

---

## CR-005 — Character physical description for likeness

**Date logged**: 2026-08-20  
**Status**: `implemented`  
**Area**: Pre-processor prompt (US1/US2 prompt building)

### Use case (user requirement)

The user wants images of the same character to look like the **same person** across generations — same hair, eyes, body type, etc. This is a **character-likeness** requirement: the image prompt must carry the character's stable visual identity so the image model doesn't invent a different person each time.

### Expert design (why not naive injection)

Simply concatenating every physical attribute field would **hurt** likeness and image quality:

- **Visual identity anchors** are what make a character recognizable: age, height, weight, ethnicity, hair (colour + style), eyes, skin (tone + texture), body type, and distinguishing marks/piercings/tattoos. These must be fixed and identical across every image.
- **Measurements (bust/waist/hip) and all intimate/sexual fields** are intentionally excluded from the image prompt: they don't affect a face/portrait, add noise for image models, and can trip content-policy clamps. The existing `PhysicalAttributesFormatter.FormatBlock` is designed for narrative continuation prompts (includes everything); image prompts need a **visual-only** subset.
- The pre-processor is instructed to **reproduce the descriptors verbatim** ("CHARACTER LIKENESS" rule) so the identity anchors survive into the final image-model prompt unchanged.

### Implementation (2026-08-20)

- `PhysicalAttributesFormatter.FormatVisualBlock(attrs)` — new visual-only identity formatter (excludes measurements + intimate fields; reuses the existing labelled-line style).
- `SceneImagePromptPreprocessor`:
  - `BuildMessages` accepts optional `IReadOnlyList<Character>? characters = null`.
  - New `CHARACTER APPEARANCE (FIXED IDENTITY …)` block in the user prompt listing: the interaction's actor, the persona (when different), and any other scenario character whose name appears in the story moment. Each entry uses the visual block; falls back to the character's free-text `Description` only when no structured attributes exist (so prose-described characters still get likeness cues). No appearance data → block omitted entirely.
  - System prompt gains a **CHARACTER LIKENESS** rule: reproduce the exact descriptors for every depicted character.
- `SceneImagePromptGenerationJobHandler` loads the scenario via `IScenarioService.GetScenarioAsync` (best-effort) and passes `scenario.Characters` into `BuildMessages`.

### Tests

6 new `SceneImagePromptPreprocessorTests`: actor visual identity present; measurements/intimate fields excluded; no characters → no block; no appearance data → no block; description fallback; persona appearance included when different from actor.

### Affected files

- `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs` (`FormatVisualBlock`)
- `DreamGenClone.Web/Application/RolePlay/ISceneImagePromptPreprocessor.cs` (characters param)
- `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs` (appearance block + likeness rule)
- `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` (load scenario, pass characters)
- `DreamGenClone.Tests/RolePlay/SceneImagePromptPreprocessorTests.cs` (6 new tests)

---

## CR-006 — Preprocessor refinement: beats + participants + full-turn + POV + transparency

**Date logged**: 2026-08-20  
**Status**: `designed`  
**Area**: Preprocessor pipeline (US1/US2/US3), studio UI, debug/transparency

### Root cause (verified)

- Character selection uses naive name-substring matching against a single interaction and **always** includes the persona → wrong subjects (e.g. Ken's profile injected while Becky + Dean are in bed).
- `Characters present` uses `CharacterRoles.Values` (roles, not names).
- No beat/scene awareness: one interaction/turn spans many beats; the preprocessor has no idea which beat to depict.
- The app already has an authoritative presence model (`RolePlayScenePresenceHelper.IsActorInScene`, `CharacterEncounterStates`, `CharacterLocations`, `CharacterSnapshots`) and first-class turns (`RolePlayV2Turns.OutputInteractionIds`) — both unused by the preprocessor.

### Design (see `design/preprocessor-refinement-design.md`)

- **Option 1**: presence-grounded participant resolution (persona only when actually present) — P1, low risk.
- **Option 2**: pre-analysis stage — deterministic beat segmentation (2B baseline) + optional LLM beat labeling/suggestion (2A).
- **Option 3**: studio beat + POV selectors; POV modeled as a **framing line** (identity + beat constant, framing varies) so "same beat from multiple POVs" is not too complex.
- **Option 4**: `SceneImageAnalysisCompleted` debug event (beats, participants, exclusions, reasons) + in-studio "Why this prompt?" panel + prompt preview.
- **Full Turn**: resolve the turn from the selected interaction via `RolePlayV2Turns` and feed all sibling interactions (Becky / Dean / Ken / Narrative) into the analyzer.

### Recommended order

P1 (presence) → P2 (full turn) → P3 (transparency) → P4 (beat segmentation) → P5 (POV) → P6 (LLM labeling).

### Confirmed decisions (2026-08-20)

1. Beat selector **always shown and user-selectable**.
2. **POV set derived from beat participants** (`Omniscient` + present participants); not fixed.
3. Turn boundary confirmed: **one submission = one turn = one scene** (may contain 1..many beats).
4. **One POV at a time**, user-selectable per render.

### Ensemble beat amendment (approved 2026-08-21)

The implemented character-centric beat shape is superseded by the generic shared-timeline contract in `design/ensemble-beat-render-brief-plan.md`. Narrative is the authoritative turn synthesis; character interactions provide concurrent action, reaction, perception, and sightline evidence. Beats support one or many active characters and zero or many observers. The selected beat and POV are assembled into a complete deterministic render brief before the image-prompt preprocessor runs.

### Files likely touched

`SceneImagePromptPreprocessor.cs`, new `SceneImageBeatAnalyzer.cs` + `SceneImageTurnResolver.cs`, `SceneImagePromptGenerationJobHandler.cs`, `SceneImageRecord.cs` (BeatId, Pov), `SceneImageStudio.razor`, analysis DTOs, tests.

---

## Log

| Date | ID | Action |
|------|----|--------|
| 2026-08-20 | CR-001..CR-004 | Logged first batch of change requests |
| 2026-08-20 | CR-001..CR-004 | Implemented all four change requests; build 0 errors, 1151/1151 tests pass |
| 2026-08-20 | CR-005 | Implemented character-likeness appearance injection; build 0 errors, 1157/1157 tests pass |
| 2026-08-20 | CR-006 | Designed preprocessor refinement (Options 1–4); design doc at `design/preprocessor-refinement-design.md` |
| 2026-08-20 | CR-006 | Design decisions confirmed (beat selector always shown; POV derived from beat participants; turn = scene; one POV at a time) |
| 2026-08-21 | CR-006 | Approved generic ensemble-beat and deterministic render-brief amendment; implementation plan persisted in `design/ensemble-beat-render-brief-plan.md` |
| 2026-08-20 | CR-006 | **RESUME HANDOFF CREATED** at `RESUME-HANDOFF.md` — self-contained plan + current state + env notes for picking this up from a fresh machine/chat |
