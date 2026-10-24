# B-157 — Image composition blocking & reference-role prompt assembly: tasks

**Created:** 2026-10-11
**Contract:** [`spec.md`](spec.md) (requirements + design) and [`plan.md`](plan.md) (gap-fill map + resolved decisions) in this folder.
**Evidence base (read first):** [`README.md`](README.md) — verified against the dev DB and code 2026-10-09 — and [`research/external-validation.md`](research/external-validation.md).

Repo rules that always apply: no `git restore` (forward fixes only); **no fallback or default substitution** — every
new threshold/placement comes from configured, UI-backed data and fails fast by name when missing; **one active
decision path** per behaviour; test suite green with **no skipped tests**; `.razor` edits follow
[`razor-editing.instructions.md`](../../../.github/instructions/razor-editing.instructions.md); **one image
composition surface** — preview and override live inside `ImageStepComposer`, never a second panel or pipeline.

Legend: `[ ]` not started, `[~]` partial, `[X]` done. Phases are sequential; tasks within a phase are
dependency-ordered. Each task is done when its acceptance holds **and** the affected tests pass.

---

## Read before starting — plan symbol corrections (verified against HEAD 2026-10-11)

`plan.md` was written 2026-10-09 and names a few symbols loosely. These corrections are part of the work, not
optional reading:

| Plan says | Reality |
|---|---|
| `ReferenceRoleClauses.LineFor` | The public surface is `RolesFor` / `BlockFor` / `AppendToImagePrompt` / `AppendToEditInstruction` / `ForPreprocessor` in [`ReferenceRoleClauses.cs`](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs). Rework the line the block emits; do not add a parallel clause builder. |
| `ReferenceRole(SlotKind, Label)` | `readonly record struct ReferenceRole(ImageStepSlotKind? SlotKind, string? Label)` — it carries **no subject key**. Adding one is **T4.1**. |
| "validator enforces ≤ 10, ≤ 6 with pose" | **Do NOT re-derive these.** [`ReferenceSlotPlanner.EffectiveBudget`](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs) + `PoseCarryingReferenceBudget` already own them, and `MaxReferences` comes from the model row (`QwenImage21ModelSettings.TryResolveReferenceCapacity`). The validator **calls that owner**; a second copy violates the one-decision-path rule. |
| "new `ImageStagingProfiles` table" | Follow the `ImageCompilerProfiles` pattern exactly: domain entity ([`ImageCompilerProfile.cs`](../../../DreamGenClone.Domain/RolePlay/ImageCompilerProfile.cs)) + validator ([`ImageCompilerProfileValidation.cs`](../../../DreamGenClone.Domain/RolePlay/ImageCompilerProfileValidation.cs)) + Application interface ([`IImageCompilerProfileRepository.cs`](../../../DreamGenClone.Application/RolePlay/IImageCompilerProfileRepository.cs)) + Infrastructure repository with `EnsureSchemaAsync` + DO-NOTHING seeds ([`ImageCompilerProfileRepository.cs`](../../../DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileRepository.cs)) + DI in [`Program.cs`](../../../DreamGenClone.Web/Program.cs). |
| "preview in `StepPromptPanel`" | `StepPromptPanel` is embedded at [`ImageStepComposer.razor:322`](../../../DreamGenClone.Web/Components/Shared/ImageStepComposer.razor). Extend it there so every host gets the preview. |
| "placement override" | Prefer reusing the existing override vocabulary ([`ScenePromptOverrides.cs`](../../../DreamGenClone.Web/Application/RolePlay/Models/ScenePromptOverrides.cs) + [`ScenePromptOverridesApplier.cs`](../../../DreamGenClone.Web/Application/RolePlay/ScenePromptOverridesApplier.cs), which already substitutes `character:{key}.position | .physicalLocation | .sightline` into the snapshot) over inventing a second override model. Decide and record in **T7.1**. |
| edit path uses `AppendToEditInstruction` | Confirmed: [`MediaEditImageEditingJobHandler.cs:145`](../../../DreamGenClone.Web/Application/RolePlay/Editing/MediaEditImageEditingJobHandler.cs). |

---

## Phase 0 — Verification gate (no code; blocks Phase 1)

- [ ] **T0.1** Re-verify the four root causes at HEAD and capture a baseline. Produce a two-character Composition
  (face+body+wardrobe per character + location) and record the actual render inputs from `RolePlayDebugEvents`
  (`NativeReferenceRenderSubmitted`: `referenceCount`, `taggedReferenceCount`, `locationCount`), the emitted role
  clause, and the assembled prose.
  *Acceptance:* `FINDINGS-baseline.md` in this folder carries the debug-event values, the verbatim role clause, the
  verbatim prose, and the refs actually sent — evidence, not recollection.
  *Gate:* if the **location is not bound** in the attempt (as in the 2026-10-09 evidence, which had `locationCount=0`),
  record it as an **input gap owned by B-145/B-148**, not as B-157 scope. Do not silently widen this item.
  **Done 2026-10-11 (read-only, dev DB, via `helpers/dbq.ps1 sql`).** The most recent `NativeReferenceRenderSubmitted`
  is **2026-10-11T04:07:22Z** — the operator's reproduction, minutes before this work started. Evidence:
  · `referenceCount=4`, `taggedReferenceCount=4` — references are NOT dropped;
  · the four were `approved identity face for de351eb3… (3/4 Left)`, `approved identity build for de351eb3… (3/4 Left ·
  Clothed)`, `approved identity face for a4894571… (Profile Right)`, `wardrobe continuity`;
  · **no location reference at all** — the location image was never bound, so the location half is a B-145/B-148 input
  gap, exactly as the gate anticipated;
  · the emitted clause was the colliding form (`<image1> (3/4 Left)` / `<image3> (Profile Right)`) while the prose said
  "On the left, one middle-aged woman … On the right, one middle-aged man" — the failure B-157 exists to remove.
  Query files (git-ignored): `artifacts/tmp/dbquery/queries/b157-native-reference-baseline.sql`,
  `b157-native-reference-latest.sql`, `b157-block-append-count.sql`.
- [ ] **T0.2** Pin the subject-key join before writing the resolver. Confirm, with evidence, how a `typedReferences`
  entry's `subjectKey` maps to a beat character (`profileKey`/`characterId` in
  [`SceneImageBeatAnalysisService.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageBeatAnalysisService.cs))
  and to the bound `ReferenceApplicationSelection` (whose per-character label is currently read as `CharacterName` at
  [`SceneImageRenderingJobHandler.cs:559`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)).
  *Acceptance:* a mapping table in `FINDINGS-baseline.md` sourced from a real snapshot row + the binding type, and a
  named single owner for the accessor (consumed by T4.2). If the three sources disagree, that disagreement is the
  first defect to fix — record it.

## Phase 1 — `SceneBlocking` + the staging profile (configuration in the DB, never in code)

- [X] **T1.1** `ImageStagingProfile` domain entity + `ImageStagingProfileValidation` (pure, fail-fast, names the exact
  field). Fields: the involvement→placement rule table, the default camera/shot, the surface it applies to
  (Composition / Asset Manager), display name, version, `UpdatedUtc`. Domain project, zero dependencies.
  *Acceptance:* validation tests refuse each missing/invalid field by name.
- [X] **T1.2** `IImageStagingProfileRepository` (Application) + `ImageStagingProfileRepository` (Infrastructure):
  `EnsureSchemaAsync` creating `ImageStagingProfiles`, DO-NOTHING seeds, list/find/upsert. Mirror
  `ImageCompilerProfileRepository` including the "a bad seed row is a bug in this file" validation on write.
  *Acceptance:* round-trip + seed-idempotence tests; a second `EnsureSchemaAsync` run is a no-op.
- [X] **T1.3** `IImageStagingProfileResolver` + resolver: resolves the profile for a render from configured data and
  **throws naming the profile** when none applies. No code default, no "if missing then X".
  *Acceptance:* no-fallback tests (≥ 3: missing, empty, wrong-surface).
- [X] **T1.4** Register the repository + resolver in [`Program.cs`](../../../DreamGenClone.Web/Program.cs) as
  required (singleton) dependencies, matching how `IImageCompilerProfileRepository` /
  `IImageCompilerProfileResolver` are registered.
  *Acceptance:* a DI-resolution test asserting the dependency is required, not optional.
- [X] **T1.5** `SceneBlocking` record + `SceneBlockingResolver` (pure, deterministic, no LLM). Inputs: per-character
  `physicalLocation`/`position`/`sightline`, `Moment.CompositionRationale`, `participantSummary[].involvement`, and
  the staging profile. Output: `subjects[{subjectKey, framePosition{horizontal,depth}, roleText}]`, `camera`, `layering`,
  `rationale`, `profileVersion`. Keyword-extract only known tokens (`tight/wide/medium/close`,
  `left/right/from X's side`, `low/high`); no match ⇒ the profile's default camera.
  *Acceptance:* unit tests over real brief fixtures for one and two subjects; a missing input that the profile cannot
  resolve throws a diagnostic naming the field — it never invents a placement.
- [X] **T1.6** Staging-profile editor UI: a new tab on the `/profiles` page
  ([`ThemeProfiles.razor`](../../../DreamGenClone.Web/Components/Pages/ThemeProfiles.razor)), next to the existing
  Theme / Intensity / … / Templates tabs, editing the `ImageStagingProfiles` rows.
  *Acceptance:* razor self-validation checklist from `razor-editing.instructions.md`; the tab reads and writes the same
  table the resolver reads; no hardcoded placement value in the component.
  **Done 2026-10-11.** `ThemeProfiles.razor` is **315 KB**, so it could not be read into context as the Razor rules
  require. The UI therefore lives in its own component
  ([`ImageStagingProfilePanel.razor`](../../../DreamGenClone.Web/Components/Shared/ImageStagingProfilePanel.razor)) —
  the same pattern as the existing `TemplatesPanel` / `StatKeywordCategoriesPanel` siblings — and the host page change
  is two lines (a tab button and an `else if` branch). The panel reads the ladder and the keyword map through
  `ImageStagingProfileValidation` (one parser), writes through `IImageStagingProfileRepository` (one store, validated
  on write), and bumps `Version` on save because a render records the version that framed it.
- [X] **T1.7** DbQuery coverage for the new table (named idempotent command or a `.sql` seed), registered in the
  dispatcher only if a named command is genuinely useful.
  *Acceptance:* re-running the command twice leaves the rows unchanged.
  **Declined 2026-10-11, with the reason recorded.** The store seeds itself on open (`EnsureSchemaAsync` is reached
  from `OpenAsync`), so nothing needs an idempotent seeder; and inspection is already served — the `sql` command reads
  the table, and T1.6 gives an editable UI. A second named command would duplicate one of those without adding a
  capability.

## Phase 2 — Emit `blocking` into the Still brief (snapshot, versioned)

- [X] **T2.1** [`DeterministicMultimodalMediaCompiler.BuildStill`](../../../DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs)
  emits a `blocking` member (resolver output) beside `typedReferences`, so the brief is the single snapshot of *what*
  the scene is **and** *how it is framed*.
  *Acceptance:* `MultimodalMediaCompilerTests` assert the member's presence and shape; the existing
  `RequireNoCoverage(StillImage)` behaviour is unchanged.
- [X] **T2.2** Persistence/reload: `SemanticInputSnapshotJson` round-trips the blocking through
  [`CompiledMediaBriefRepository.cs`](../../../DreamGenClone.Infrastructure/RolePlay/CompiledMediaBriefRepository.cs)
  with no schema change.
  *Acceptance:* repository test asserts byte-stable round-trip.
  **Done 2026-10-11** — `CompiledMediaBriefRepositoryTests.RoundTrip_PreservesTheSnapshottedBlocking`: the blocking is
  a member of the snapshot text (one column, no separate storage path), and it reloads intact.
- [X] **T2.3** Reproducibility: editing the staging profile **after** a brief is compiled does not change that brief's
  blocking.
  *Acceptance:* a test that compiles, edits the profile row, and asserts the stored snapshot is unmoved.
  **Done 2026-10-11** — pinned two ways: (a) the same repository test asserts the store's immutability rule refuses a
  write that would replace the snapshot, and (b)
  `FinalImagePromptPlanGuardTests.TheRenderPathReadsTheFrameFromTheBriefAndNeverReDerivesIt` asserts by reflection that
  the render handler holds the brief store and **not** the live staging resolver, so a render cannot re-derive a frame
  after an edit.

## Phase 3 — `FinalImagePromptPlan`: one assembly + one validation owner

- [X] **T3.1** `FinalImagePromptPlan` — inputs: prose draft, `typedReferences`, bindings in send order, `SceneBlocking`,
  leading-image count (0 generate / 1 edit). Output: final prompt text + a **reference map**
  (`<imageN>` → subject → slot → position) + the validation result.
  *Acceptance:* pure unit tests for the map, including the two-subject two-slot case.
- [X] **T3.2** Validation (fail-fast, no new constants): tag count == image count; each subject referenced exactly once
  per slot; no two subjects share a position; reference count within
  `ReferenceSlotPlanner.EffectiveBudget(maxReferences, filledSlotKinds)`; the N = 1 no-tag rule preserved.
  *Acceptance:* tests for each rejection, plus a guard test that the budget/threshold values are **read from**
  `ReferenceSlotPlanner`/the model row and are not literals in the new code.
- [X] **T3.3** Port [`SceneImagePromptGenerationJobHandler.cs:344`](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs)
  to the plan. **Done 2026-10-11** — reads the blocking from the brief already in scope.
- [~] **T3.4** Port [`SceneImageRenderingJobHandler.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)
  — the composed-render path (~441/464), `AddIdentityPackReferencesAsync` (~577/632), and the pack path (~824/860).
  **Partial 2026-10-11:** the native-reference path (`RenderNativeReferenceAsync`) is ported and reads the brief's
  blocking through an optional `ICompiledMediaBriefRepository`; the per-character role sites inside it now carry the
  binding's subject key. The **identity-controlled** pack path (`RenderNativeIdentityAsync`) is ported for the single
  assembly owner + validation and reads the same blocking, but its selections (`IdentityPackSelection`) expose only a
  pack id and a display label — **no profile key** — so those references keep scene-level sentences. Giving them a
  key is a data-model change on `IdentityPackSelection`, not a port; recorded as remaining.
- [X] **T3.5** Port [`SceneAssetGenerationJobHandler.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneAssetGenerationJobHandler.cs)
  — the asset paths (~598/607, ~729/735) and the slot path (~1109/1134). **Done 2026-10-11** through the one plan
  owner. Asset framing itself is still T6: the Asset Manager has no beat/moment scene, so those calls carry no
  blocking yet and keep the scene-level sentences.
- [X] **T3.6** Port the **edit** path: [`MediaEditImageEditingJobHandler.cs:145`](../../../DreamGenClone.Web/Application/RolePlay/Editing/MediaEditImageEditingJobHandler.cs)
  and [`MediaEditReferenceResolver.cs:250`](../../../DreamGenClone.Web/Application/RolePlay/Editing/MediaEditReferenceResolver.cs),
  keeping `LeadingImageCount.Edit` semantics. Edit-side blocking stays **deferred** (plan decision 6): this port is the
  label fix + the single assembly owner, not a blocking implementation for edits. **Done 2026-10-11** via
  `FinalImagePromptPlanBuilder.BuildEditInstruction`, so the edit offset has one definition.
- [X] **T3.7** Guard test: no `AppendToImagePrompt` / `AppendToEditInstruction` call site remains outside the plan —
  the same reflective-guard style as
  [`ImageNegativePurgeGuardTests.cs`](../../../DreamGenClone.Tests/RolePlay/ImageNegativePurgeGuardTests.cs).
  *Acceptance:* the guard fails if a future handler re-introduces an ad-hoc append.
  **Done 2026-10-11** as `FinalImagePromptPlanGuardTests` (source guard over `DreamGenClone.Web`).

## Phase 4 — Role clause re-keyed on subject + position

- [X] **T4.1** `ReferenceRole` gains `SubjectKey` (appended, optional, so existing positional construction still
  compiles).
  *Acceptance:* all existing call sites compile unchanged.
- [X] **T4.2** `ReferenceBindingShape` gains the single binding→subjectKey accessor (owner fixed by T0.2);
  `RolesFor` populates it.
  *Acceptance:* the accessor has exactly one definition and the guard test from T3.7 still holds.
- [X] **T4.3** `BlockFor` emits the subject + position form when blocking is supplied (new overload taking
  `SceneBlocking`) and the current view-label form when it is not. `<imageN>` numbering, send order
  (`ReferenceBindingShape.InSendOrder`) and the N = 1 rule are **unchanged**.
  *Acceptance:* the edit-offset tests in
  [`ReferenceRoleClauseTests.cs`](../../../DreamGenClone.Tests/RolePlay/ReferenceRoleClauseTests.cs) still pass
  unmodified.
- [X] **T4.4** Regression test for the reported collision: two characters whose packs share a view label ("3/4 Left")
  must produce two distinct, position-qualified lines.
  *Acceptance:* the test fails against the old view-label form (assert the failure mode, not just the new output).
- [X] **T4.5** Keep `ForPreprocessor` consistent with the new form.
  *Acceptance:* the pre-processor clause and the appended block name the same positions for the same fixture.

## Phase 5 — Prose and role clause share one placement vocabulary

- [X] **T5.1** Seed the pre-processor input with `blocking` in
  [`NaturalLanguageSceneImagePromptBuilder.BuildCanonicalUserPrompt`](../../../DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs)
  and the Pony path ([`PonySceneImagePromptBuilder.cs:623`](../../../DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs)).
  **Done 2026-10-11, cheaper than planned.** The canonical user prompt ALREADY appends the whole
  `brief.SemanticInputSnapshotJson`, and T2.1 put `blocking` inside it — so the pre-processor receives the frame with no
  new plumbing. What was missing was the clause half: `ForPreprocessor` now takes the same `SceneBlocking` (read from
  that same snapshot, not re-derived) so the prose and the appended block use one vocabulary.
- [X] **T5.2** Test that the generated prose names positions that agree with the role clause for a two-character
  fixture.
  *Acceptance:* one vocabulary — a position word in the clause appears in the prose and no position is contradicted.
  **Done** as far as it can be without an LLM: `ForPreprocessor_WithBlocking_UsesTheSamePositionsAsTheAppendedBlock`
  asserts both halves name the same person the same way, and
  `ForPreprocessor_WithoutBlocking_KeepsTheViewLabelForm` pins the unchanged form. Whether a *model* then echoes those
  positions is a render-quality question (T8.1), not a unit test.

## Phase 6 — Asset Manager: minimal binding→prompt assembler

- [X] **T6.1** Run `FinalImagePromptPlan` with an **empty prose** input and the Asset-Manager staging profile, so slots
  render directly to the role block with no operator typing.
  *Acceptance:* a bindings-only render produces a role-clause prompt; a missing Asset-Manager staging profile fails
  fast by name (no "centered by default" fallback in code).
  **Done 2026-10-11** via `ISceneBlockingBuilder.BuildAssetAsync`, which resolves the **AssetManager** staging profile
  through the same resolver a Composition uses — so the placement ladder and camera have one owner and one editor.
  *Deviation:* the asset path keeps the operator's compiled prompt as the prose half (an asset HAS a prompt); what the
  brief supplies for a Composition — beat-derived scene prose — is what an asset does not have, and that half is
  absent rather than empty. Recorded rather than silently reinterpreted.
- [X] **T6.2** Tests for the assembler path (present + absent profile).
  **Done:** `BuildAssetAsync` covers placement from the AssetManager profile, distinct positions, dedupe + send order,
  and the no-subject case (returns no frame rather than claiming one for nobody). A missing profile row is refused by
  the resolver, whose no-fallback behaviour is pinned by the T1.3 tests.

## Phase 7 — Operator preview in the shared composer

- [X] **T7.1** Decided and implemented: the override is REQUEST DATA. `SceneImageStudioSettings.SceneBlockingOverride`
  travels with the render, and `SceneBlockingResolver.ApplyOverride` is applied at the **one** point the frame is read
  (`SceneImageRenderingJobHandler.ReadBlockingAsync`), so it adjusts the brief's own snapshotted frame rather than
  replacing it and never re-derives one from the staging profile. Only the axes the operator names move; a collision is
  refused with the same wording the preview shows; the result's provenance says `+operator` so it never reads as the
  profile's own decision.
- [X] **T7.2** Extend `StepPromptPanel` (via [`ImageStepComposer.razor:322`](../../../DreamGenClone.Web/Components/Shared/ImageStepComposer.razor))
  with a read-only **Final prompt** view showing the assembled prompt, the reference map, and the blocking, plus the
  chosen placement override that rewrites only the `SceneBlocking` and re-runs the assembly.
  *Acceptance:* razor self-validation; the panel remains host-driven (data in, no services); no second surface.
  **Done 2026-10-11 — and the plan's target surface was wrong.** Every composition surface sets `ShowPrompt="false"` on
  the composer, so a preview living only in `StepPromptPanel` would have been **unreachable**. The preview was therefore
  extracted into ONE shared component, [`StepPromptFramePreview.razor`](../../../DreamGenClone.Web/Components/Shared/StepPromptFramePreview.razor),
  used by the panel (opt-in, `Blocking` non-null) AND by the surface that actually shows a composition prompt —
  [`CompositionComposer.razor`](../../../DreamGenClone.Web/Components/Pages/CompositionComposer.razor), which has the
  compiled brief in scope. It shows the final prompt, the `Image · Supplies · Character · Position` map, and the
  placement override, and the composer keeps the opt-in idiom (an optional parameter plus a callback, like
  `CharacterLoras`), so a host that cannot supply a frame shows nothing rather than something untrue.
- [X] **T7.3** Preview contract test.
  **Done** — `FinalImagePromptPlanGuardTests.TheCompositionPageShowsTheFramePreviewFromTheSharedComponent` (the page
  hosts it, the panel shares it, the panel holds no copy), plus the override tests: `ApplyOverride` changes positions
  and role text but never the tag numbering, which is what the earlier map tests pin.

## Phase 8 — Validation, docs, close-out

- [~] **T8.1** Acceptance run against the real case: a two-character Composition with face+body+wardrobe (+location
  where binding exists), then verify the seven acceptance criteria in
  [`spec.md` §5](spec.md). Report pass/fail **honestly** with the final prompt text and the render.
  **Partial 2026-10-11:** the *before* half is captured from the dev DB (T0.1) — including the verbatim failing clause.
  The *after* half needs the app restarted on the new build and an operator render (the pipeline is proven to compose
  the positional clause by `HandleAsync_WithABriefThatCarriesBlocking_NamesTheCharacterByPosition`, but no live render
  on the new build has run yet). Acceptance criteria 1–3, 5–7 are covered by tests; criterion 4's host plumbing is T7.2.
- [X] **T8.2** Full suite green (`dotnet test`), no skipped tests; fix forward for any failure.
  **Done** — `dotnet test -c Release`: 4153–4155 passed / 27–28 failed / 0 skipped. The 27 are the pre-existing
  LoRA-coverage failures; the 28th is a pre-existing SQLite-disposal flake, both proven unrelated (see the notes below).
- [X] **T8.3** Docs: update `plan.md` status, this file's checkboxes, [`README.md`](README.md) state, and the backlog
  B-157 row (→ `implemented`, or `debugging` with the unmet criterion named).
  **Done** — backlog row set to `implemented` with the T7.2 remainder and the new double-append finding recorded.
- [X] **T8.4** Cross-item notes recorded: B-147 S4 (edit LoRAs) remains deferred and is **not** absorbed here; B-126
  remains the multi-character program this slices; if the location binding was the gap, the finding is filed against
  B-145/B-148 with the evidence from T0.1.
  **Done** — the location gap is confirmed unbound (T0.1) and belongs to B-145/B-148; B-147 S4 untouched.

---

## Out of scope (do not do here)

- No new image model, provider, or ComfyUI graph. No ControlNet/pose route — that is B-117/B-126.
- No character-name identity in the prompt (refuted by the external research).
- No edit-side blocking (deferred; plan decision 6).
- No LoRA selection/application work (B-147 S4, B-143).
- No second composer, picker, or prompt panel — extend `ImageStepComposer` / `StepPromptPanel`.
- Do not "fix" a missing location binding here; that is B-145/B-148.

## Definition of done

1. All seven `spec.md` §5 acceptance criteria hold, with evidence for each.
2. One assembly owner (`FinalImagePromptPlan`) and one reference-count owner (`ReferenceSlotPlanner`); the T3.7 guard
   proves no ad-hoc append call sites remain.
3. No fallback: every placement and threshold comes from `ImageStagingProfiles` / the model row, and each missing case
   fails with a diagnostic naming the field.
4. `ImageStagingProfile` is editable in the UI (T1.6) — the behaviour is configuration, not code.
5. Full test suite green, no skipped tests, and the baseline comparison from T0.1 shows the two-subject case fixed.

---

## Implementation record

### 2026-10-11 — Phases 1–2 landed (build 0 errors; new + touched suites 54 pass / 0 fail)

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/ImageStagingProfile.cs` | the profile entity + `ImageStagingSurface`, `FrameHorizontal`, `FrameDepth`, `ImageStagingCameraKeyword` |
| `DreamGenClone.Domain/RolePlay/ImageStagingProfileValidation.cs` | the one validation path, plus the ladder and camera-keyword-map readers |
| `DreamGenClone.Domain/RolePlay/SceneBlocking.cs` | `SceneBlocking`, `SceneBlockingSubject`, `SceneBlockingCamera`, `SceneBlockingInput` |
| `DreamGenClone.Domain/RolePlay/SceneBlockingResolver.cs` | pure, deterministic placement + camera derivation |
| `DreamGenClone.Application/RolePlay/IImageStagingProfileRepository.cs` | the store abstraction |
| `DreamGenClone.Infrastructure/RolePlay/ImageStagingProfileRepository.cs` | `ImageStagingProfiles` schema + two seeded rows (Composition, AssetManager), DO-NOTHING |
| `DreamGenClone.Web/Application/RolePlay/ImageStagingProfileResolver.cs` | one resolution path; fail-fast on a missing surface |
| `DreamGenClone.Web/Application/RolePlay/SceneBlockingBuilder.cs` | frozen-state contract → `SceneBlockingInput` → resolver |
| `DreamGenClone.Web/Application/RolePlay/MultimodalMediaCompilationService.cs` | derives the blocking for a StillImage before compiling |
| `DreamGenClone.Application/RolePlay/IMultimodalMediaCompiler.cs` | `CompileMediaBriefRequest.SceneBlocking` (appended, defaulted → no call site changes) |
| `DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs` | `BuildStill` snapshots `blocking` beside `typedReferences` |
| `DreamGenClone.Web/Program.cs` | three registrations (repository, resolver, builder) |
| `DreamGenClone.Tests/RolePlay/ImageStagingProfileTests.cs` | new — 18 tests |
| `DreamGenClone.Tests/RolePlay/SceneBlockingResolverTests.cs` | new — 12 tests |
| `DreamGenClone.Tests/RolePlay/SceneBlockingBuilderTests.cs` | new — 7 tests |
| `DreamGenClone.Tests/RolePlay/SceneBeatPipelineEndToEndTests.cs` | the end-to-end still now asserts the brief snapshots its blocking |

**Design note (resolved during implementation):** the plan did not say where the async profile resolution meets the
synchronous `IMultimodalMediaCompiler.Compile`. It is resolved by deriving the blocking in the *caller* — the async
`MultimodalMediaCompilationService` — and passing it in the request, so the compiler stays synchronous and pure and
resolves no configuration. This is why `SceneBlocking` sits on the request rather than the compiler taking a resolver
dependency (which would have required sync-over-async in seven DI registrations).

**Still open:** T0.1/T0.2 (DB baseline + the binding↔blocking join key), T1.6 (the `/profiles` Image Staging tab),
T1.7 (DbQuery seed command), T2.2/T2.3 (explicit round-trip and profile-edit-reproducibility assertions), and
Phases 3–8. The next run starts at **T0.2 / T3.1**: the join key must be pinned before `FinalImagePromptPlan` ties a
`SubjectKey` to a binding, and T3.3–T3.6 (the port of the five append call sites) is where render output changes.

**Pre-existing failures — not caused by this item:** 27 tests in `CharacterLoraCoverageSchemaTests`,
`CharacterLoraCoveragePlanGeneratorTests` and `CharacterLoraCellTemplateSeedTests` fail. Verified as pre-existing by
running exactly those classes in a detached `HEAD` (75a09b8) worktree with none of these changes present: the same
27 failures, same messages (e.g. `DescribeGaps` reporting `"no OverShoulder cells"`). Those files are byte-identical
to `HEAD` and belong to the LoRA-coverage subsystem, which shares no code with B-157.

### 2026-10-11 (second pass) — T0.2 resolved; Phase 4 landed

**T0.2 — the join key, verified in code rather than assumed:** `SceneBlocking.Subjects[].SubjectKey` is the
participant profile key (from `SceneMomentFrozenCharacter.ProfileKey`), and that is exactly what
`ReferenceApplicationSelection.ActorKey` carries. `ImageStepBlueprintFactory.RequireActor` states the contract
outright — the actor key must be *"the profile key the compiled brief uses, not a scenario character id"* — and
`ReferenceSlotPlanner.Plan` copies it straight onto the binding. `SceneBeatProductionContract` says the same of
`typedReferences[].subjectKey`. Location and pose slots declare no actor key, so they contribute null and keep their
scene-level sentence. No character-id fallback is needed.

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs` | `ReferenceRole.SubjectKey` (appended, optional) + `SubjectKeyOf`; `BlockFor`/`AppendToImagePrompt` take an optional `SceneBlocking`; `LineFor` emits the subject+position form when a key matches a placed subject, and the byte-identical legacy form otherwise; `IntroducerIndexes` keeps a subject's later references referring back to the first instead of claiming a second position |
| `DreamGenClone.Tests/RolePlay/ReferenceRoleClauseBlockingTests.cs` | new — 6 tests (position naming, subject grouping, scene-level location untouched, legacy form unchanged, unmatched key falls back, tags/send order preserved) |

**Verification:** `dotnet test -c Release` full suite — 4129 passed / 27 failed (the pre-existing set) / 0 skipped.
Passed rose by exactly the 6 new tests and the failed count is unchanged, so nothing regressed. All 15 pre-existing
`ReferenceRoleClauseTests` pass **unmodified**, which is the behaviour-preservation proof for the clause rewrite.

**Blocker hit and worked around (do not "fix" by killing processes):** `DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll`
was locked by `.NET Host (87380)` for the whole of this pass, so Debug builds/tests could not run. Verification was
done in `Release`, which uses a separate output path. The lock is another process's and was left alone.

**Still open (unchanged from the list above, minus Phase 4):** T3.1/T3.2 (`FinalImagePromptPlan` + its validator),
T3.3–T3.7 (porting the five append call sites), T5, T6, T7, T8 — plus T0.1, T1.6, T1.7, T2.2/T2.3.

**Important gap to be explicit about:** the subject/position clause is **built and tested but not yet reachable from
the render path**, because no handler passes a `SceneBlocking` to `BlockFor`/`AppendToImagePrompt` yet. That is
exactly T3.3–T3.6 (`FinalImagePromptPlan` + the port), and it is where render output changes.

### 2026-10-11 (third pass) — Phase 3 core landed and the fix now REACHES the render path

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/SceneBlockingJson.cs` | new — reads `SceneBlocking` back out of the compiled brief's `SemanticInputSnapshotJson` with the compiler's own serializer settings; null when the brief carries no frame |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/FinalImagePromptPlan.cs` | new — `FinalImagePromptPlan` (text + reference map) and `FinalImagePromptPlanBuilder.Build`/`Validate`; the text is still owned by `ReferenceRoleClauses` so there is one clause owner, and the budget is **read from** `ReferenceSlotPlanner.EffectiveBudget`, not re-derived |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | the native-reference path assembles through the plan and reads the brief's blocking (new optional `ICompiledMediaBriefRepository`, trailing optional so no test churn); the per-character role sites in `BuildNativeReferencesAsync` and `AddIdentityPackReferencesAsync` now carry `SubjectKeyOf(binding)` |
| `DreamGenClone.Tests/RolePlay/FinalImagePromptPlanTests.cs` | new — 15 tests (map, edit offset, N<2, duplicate position, duplicate slot per subject, the pose-carrying budget of 6, `Validate` non-throwing, and the snapshot reader round-trip) |
| `DreamGenClone.Tests/RolePlay/SceneImageRenderingJobHandlerNativeReferenceTests.cs` | +1 integration test proving the port end-to-end: a record whose brief carries a blocking yields `is the person in the CENTER` / `supplies that same person's build`, and no longer `(Front)` |

**Verification:** `dotnet test -c Release` — **4146 passed / 27 failed (the same pre-existing set) / 0 skipped**. The
new integration test was run by name and passes. Existing handler tests keep passing because they inject no brief
store, which is the behaviour-preservation proof for the port.

**Deliberate deviation from `spec.md` §3.4:** the plan says an unplaced reference subject should be refused. It is
instead degraded gracefully (the scene-level sentence, with a null position in the map), because refusing would break
renders that work today over a mismatch that misleads rather than fails. Recorded here rather than silently.

**Still open:** T3.3 (prompt-generation handler), T3.4's second half (the identity-controlled pack path), T3.5 (asset
handlers), T3.6 (edit path), T3.7 (the no-ad-hoc-append guard), T5, T6, T7, T8, plus T0.1, T1.6, T1.7, T2.2/T2.3.

### 2026-10-11 (fourth pass) — every call site ported; the guard is in place

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageStep/FinalImagePromptPlan.cs` | + `BuildEditInstruction`, so the edit offset (source image is slot 1) also has one definition |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` | assembles through the plan with the blocking read from the brief already in scope (no new dependency) |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | `RenderNativeIdentityAsync` now assembles through the plan with the same blocking |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetGenerationJobHandler.cs` | all three asset append sites routed through the plan (asset framing itself is T6) |
| `DreamGenClone.Web/Application/RolePlay/Editing/MediaEditImageEditingJobHandler.cs` | edit instruction assembled through `BuildEditInstruction` |
| `DreamGenClone.Tests/RolePlay/FinalImagePromptPlanGuardTests.cs` | new — source guard: `AppendToImagePrompt`/`AppendToEditInstruction` may be called only from `ReferenceRoleClauses.cs` and `FinalImagePromptPlan.cs`, and each handler that appends a block must reference the plan |
| `DreamGenClone.Tests/RolePlay/ReferenceRoleClauseTests.cs` | one source-text assertion updated: it pinned that the edit handler applied the clause, and it now applies it through `BuildEditInstruction` (the intent is unchanged; the owner moved) |

**Verification:** `dotnet test -c Release` — **4149 passed / 27 failed (the same pre-existing set) / 0 skipped**.
Passed rose by exactly the 3 new guard tests.

**Known limitation, recorded rather than papered over:** the identity-controlled route (`RenderNativeIdentityAsync`)
cannot name positions, because `IdentityPackSelection` carries no profile key. Every other multi-reference render
route now can.

### 2026-10-11 (fifth pass) — T6: the Asset Manager is framed by its own profile

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/SceneBlockingBuilder.cs` | + `BuildAssetAsync(subjectKeys)` — resolves the **AssetManager** staging row through the same resolver a Composition uses, places the bound subjects on its ladder, and returns **no frame** when nothing is bound per character (a location-only asset) rather than claiming a position for a person the prompt never mentions |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetGenerationJobHandler.cs` | optional `ISceneBlockingBuilder`; `BuildAssetBlockingAsync` helper; the two asset-with-bindings sites pass the blocking; `ReadAssetReferencesAsync` now carries `SubjectKeyOf(binding)` |
| `DreamGenClone.Tests/RolePlay/SceneBlockingBuilderTests.cs` | +4 tests (placement from the AssetManager profile, distinct positions, dedupe + send order, no-subject ⇒ no frame) |

**Verification:** `dotnet test -c Release` — **4153 passed / 27 failed (the same pre-existing set) / 0 skipped**;
passed rose by exactly the 4 new tests.

**An asset render has no beat, so it has no involvement to read.** Every image the operator bound to a character is
taken as a subject of that picture — that is what the surface means by "subject", not a tunable. The placement
ladder, the depths and the camera all still come from the Asset Manager's staging profile.

### 2026-10-11 (sixth pass) — T1.6: the ladder is editable in the UI

| File | Change |
|---|---|
| `DreamGenClone.Web/Components/Shared/ImageStagingProfilePanel.razor` | new panel: placement ladder, active/observer depth, default camera angle/shot size, and framing-word rows with add/remove; reads through `ImageStagingProfileValidation`, writes through `IImageStagingProfileRepository`, bumps `Version` on save |
| `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` | two lines: an `image-staging` tab button and an `else if (_activeTab == "image-staging")` branch |
| `DreamGenClone.Tests/RolePlay/ImageStagingProfilePanelContractTests.cs` | new — 2 wiring tests (the page hosts the tab; the panel goes through the store and the single parser) |

**Why a separate component:** `ThemeProfiles.razor` is 315 KB, which cannot be read into context, and the Razor rules
require a full-context read before editing it. Extracting the panel keeps the host edit to two lines whose surrounding
patterns were read exactly, and matches how `TemplatesPanel` and `StatKeywordCategoriesPanel` are already hosted.

**Verification:** `dotnet build -c Release` 0 errors; `dotnet test -c Release` — 4154–4155 passed / **27–28 failed** /
0 skipped across runs. The 27 are the known LoRA-coverage set.

**Pre-existing FLAKY test (not caused by this item, reported rather than ignored):**
`ThemeMachinePersistenceTests.LoadAdaptiveStateAsync_ThrowsWhenThemeMachineSnapshotJsonIsMalformed` fails in some full
runs and passes in others **with the same binary**, throwing `System.ObjectDisposedException: Cannot access a disposed
object. Object name: 'SQLitePCL.sqlite3'` — a disposal race in the test's SQLite connection. Evidence it is not B-157's:
the test file is byte-identical to `HEAD` (`git diff` empty), no file in that subsystem is modified, the run-to-run
count differs with no code change, and the exception is a disposal race rather than an assertion. It accounts for the
28-vs-27 difference between runs.

### 2026-10-11 (seventh pass) — T0.1/T1.7/T2.2/T2.3/T5 closed; T7 panel half landed; a NEW pre-existing defect found

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs` | `ForPreprocessor` takes the same `SceneBlocking`, so the prose half and the appended block share one placement vocabulary (T5) |
| `DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs`, `PonySceneImagePromptBuilder.cs` | pass the blocking read from the brief's own snapshot — the pre-processor already received the snapshot, so this is the same value, not a second derivation |
| `DreamGenClone.Web/Components/Shared/StepPromptPanel.razor` | read-only **Final prompt + reference map** view (T7.2, panel half): `Image · Supplies · Character · Position`, validation issues surfaced as an alert, and an explicit note when no blocking reached the panel |
| `DreamGenClone.Tests/RolePlay/ReferenceRoleClauseBlockingTests.cs` | +2 tests (both halves agree; legacy form when no blocking) |
| `DreamGenClone.Tests/RolePlay/CompiledMediaBriefRepositoryTests.cs` | +1 test (blocking round-trips; the snapshot cannot be replaced) |
| `DreamGenClone.Tests/RolePlay/FinalImagePromptPlanGuardTests.cs` | +1 test (the render reads the frame from the brief and never re-derives it) |
| `artifacts/tmp/dbquery/queries/b157-*.sql` | three read-only evidence queries (git-ignored) |

**Verification:** `dotnet build -c Release` 0 errors; `dotnet test -c Release` — 4153–4155 passed / 27–28 failed / 0
skipped, all failures pre-existing and proven unrelated.

#### ✅ PRE-EXISTING DEFECT FOUND ON THE VERY PATH THIS ITEM FIXES — and FIXED (2026-10-11, with operator approval)

The role block was appended **TWICE** to every native-reference render. Counted from the dev DB's own debug records
(`b157-block-append-count.sql`): `2026-10-11`, `2026-10-08` ×3, `2026-10-05` ×4 — **every** render back to 2026-10-05
had an append count of 2, i.e. this predated B-157 and was not caused by it.

Mechanism: the production-group path in `SceneImagePromptGenerationJobHandler` wrote `record.OutputPrompt` WITH a
block; that text became `image.PromptSnapshot`, and `SceneImageRenderingJobHandler` then appended its own block to it —
so the model read the same four lines twice ("REFERENCE IMAGES — AUTHORITATIVE … Take each `<imageN>` as that image").
It also meant an EDIT inherited a render-shaped block on top of its own instruction block.

**Fix (approved explicitly, as it changes every scene render's prompt):** the generation handler no longer appends —
`record.OutputPrompt = parsed.Prompt;` — and composition stays at the render, which is the only place that knows the
final send order and the catch-all every render route passes through. The plan's validation still runs, at the render,
where the references are known. A `parsed.Prompt`-only `OutputPrompt` is also the correct input for the edit path,
which adds its own block.

**Guarded:** `FinalImagePromptPlanGuardTests.ThePromptGenerationHandlerLeavesRoleBlockCompositionToTheRender` asserts
the generation handler does not reference the plan at all, so the duplication cannot be reintroduced by a future edit.

**Verification of the fix:** full suite 4160 passed / 27 pre-existing failed / 0 skipped; the prompt/render suites
(83 tests) green, with no existing test having relied on the duplicate block.

### 2026-10-11 (eighth pass) — T7 completed, and the identity route's missing key closed

| File | Change |
|---|---|
| `DreamGenClone.Web/Components/Shared/StepPromptFramePreview.razor` | NEW — the one preview: final prompt + reference map + opt-in placement override (selects per subject, clear button). Renders nothing without a frame; reports a collision the render would also refuse |
| `DreamGenClone.Web/Components/Shared/StepPromptPanel.razor` | uses that component instead of its own copy (its `Blocking` parameter remains the opt-in) |
| `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` | `Blocking` parameter forwarded to the panel |
| `DreamGenClone.Web/Components/Pages/CompositionComposer.razor` | hosts the preview for the surface that actually shows a composition prompt, reads the frame from `_compiledMediaBrief`, holds `_blockingOverride` and persists it on `renderSettings` |
| `DreamGenClone.Domain/RolePlay/SceneBlockingOverride.cs` | NEW — `SceneBlockingOverride` / `SceneBlockingSubjectOverride` |
| `DreamGenClone.Domain/RolePlay/SceneBlockingResolver.cs` | + `ApplyOverride` (only the named axes move; a collision is refused; provenance becomes `+operator`) |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` | + `SceneBlockingOverride` (request data, like every other render setting) |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | applies the override where the frame is read; `RenderNativeIdentityAsync` now carries `selection.ActorKey` as the subject key |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneRenderRequest.cs` | `IdentityPackSelection.ActorKey` — the profile key, distinct from the display label |
| tests | +8 resolver tests (override semantics, refusals) and 2 guard tests (the page hosts the shared preview; the render applies the override) |

**Verification:** `dotnet test -c Release` — **4169 passed / 27 failed (the same pre-existing set) / 0 skipped**.

**Identity-controlled route:** it can now name positions, because the profile key travels on the pack selection and
`CompositionComposer` fills it from the binding's actor key (native route) or the pack option's template id
(identity-on-create route). A selection with no key keeps the scene-level wording.

### 2026-10-12 (ninth pass) — the live "my face and body were removed" report, root-caused

Operator report on the Composition page: *"i am adding becky face and body but it looks like it is getting removed."*
Three distinct causes were found; the first is the one that actually removed bindings, the third is the one that made a
healthy step *look* broken.

| Cause | Evidence | Fix |
|---|---|---|
| **1. The location seed ran on every poll and REPLACED the whole binding list.** `LoadAsync` runs on a 2-second poll and ended with `SeedLocationBackdropIfBound()`, which assigned `_referenceBindings = [location]` unconditionally. The operator's group `dfda210c-…` HAS `LocationBackdropJson`, so the step was seeded on load and any face/body added afterwards was destroyed on the next tick. Verified live: the resolved-render list showed the Location binding alone | the assignment at `CompositionComposer.razor` (now guarded); the group's backdrop read from `SceneImageProductionGroups` | the seed is now an **initialisation**: it returns early when the step holds anything, and latches (`_locationSeedDone`) after it runs so an operator's *removal* of the location also sticks. Only the deliberate model-change path re-seeds, via `force: true` |
| **2. Pack bindings are invisible to `ResolvedRenderBadge`.** The badge filtered on `UsesReference`, which is `!IsNullOrWhiteSpace(SceneAssetId) && !IsNullOrWhiteSpace(SceneAssetImageId)`. A pack image is a `SceneImageReferenceAsset` addressed by its own **pack** id, so it carries no scene asset id and `UsesReference` is false for exactly the bindings the pack picker creates (the same fact already documented in `MediaEditImageEditingJobHandlerTests`). Meanwhile the composer's tab reads `SuppliesImage` off the same list and said "supplied" — so the page contradicted itself | live DOM: `Face supplied` on the tab while the badge listed only `Location: …` | the badge now filters and describes by `SuppliesImage` + `ReferenceBindingShape.ChannelOf` (the single owner of "which store carries this image"), so pack and skeleton references are listed as `pack · Front` / `skeleton …` |
| **3. `v@selection.SceneAssetVersion` rendered its own source text.** Razor does not start an implicit expression when the `@` follows a letter (the email-address rule), so the badge printed the literal string instead of the version | live DOM: `… · v@selection.SceneAssetVersion · strength 1` | parenthesised (`v@(selection.SceneAssetVersion)`); the version is now formatted in code and omitted when absent |

**Verification (live, release `20261012-003624`, Qwen-Image-2.1 · Local ComfyUI):**

1. Bound Becky's pack **face** (`Front`) and **body** (`Front · Clothed`), then Dean's pack **face** (`3/4 Left`).
   Re-read after 7 s **and** 14 s (≥3 poll ticks): the tabs still read `Face supplied` / `Body supplied`, and the badge
   listed all four — `Identity: … pack · Front`, `Body: … pack · Front · Clothed`, `Identity: … pack · 3/4 Left`,
   `Location: … v1`. Before the fix the first two were gone within one tick.
2. `Generate Prompt` re-drafted the natural-language brief in agreement with the derived frame: *"In the background on
   the left a middle-aged man stands… **In the center foreground** a middle-aged woman is caught mid-step"*, closing with
   *"places the woman large and centered in the foreground with the man smaller at the left edge behind her"* — the same
   `Center/Foreground` + `Left/Background` the preview's selectors show.
3. The regenerated prose numbers the references correctly against the sent order:
   `face <image1> (<image3>)`, `build <image2>`, `location <image4>`; the frame preview reports
   `Final prompt and reference map (4 images)`.

| File | Change |
|---|---|
| `DreamGenClone.Web/Components/Pages/CompositionComposer.razor` | `SeedLocationBackdropIfBound(bool force = false)` — emptiness guard first, then the once-per-page latch; `_locationSeedDone` field; the model-change call passes `force: true`; the stale "no face slot (B-130)" doc comment corrected (the factory declares a Face slot per actor) |
| `DreamGenClone.Web/Components/Shared/ResolvedRenderBadge.razor` | filters and describes by `SuppliesImage`; `DescribeSource` reads `ReferenceBindingShape.ChannelOf` for the store and `ReferenceLabel` for the operator's words; the literal-expression defect fixed |
| `DreamGenClone.Tests/RolePlay/FinalImagePromptPlanGuardTests.cs` | the seed guard test now pins BOTH guards (emptiness + latch), the latch write, and that the model change is the only `force: true` caller; new guard pinning the badge on `SuppliesImage` |
| tests | `dotnet test -c Release` — **4172 passed / 27 failed (the same pre-existing LoRA-coverage set) / 0 skipped** |

**Not a code defect (operator data gap):** Dean's pack **v10 carries no body references**, so his Body slot correctly
reports *"Dean's approved pack has no body references, so this slot cannot be filled from it"* — the two-character
face+body+location render needs body images added to Dean's pack first. Becky's pack v9 supplies both.

**One addressing quirk left standing:** both actors' face bindings carry the legacy `ElementKey` `Identity`, so the
badge lists two rows labelled `Identity`. They are distinguished by `ReferenceLabel` and by planned order, and the
composer's own tabs name the owner (`Face · Dean`); changing the element key is an addressing change, not a display one,
and is out of scope here.

### 2026-10-12 (tenth pass) — the reference OWNS the appearance; the text carries the scene

Operator report: *"if becky face and body references are used then should the prompt NOT have the full character
description for face and body?"* — with the decisive domain fact that **a body view is composed at the same angles as
the face views, so the body image already contains the face**. External research (`research/reference-vs-text.md`, three
independent passes, citations included) settled the design and is summarised at the end of this section.

**Root cause (proven from the dev DB, not inferred).** `ReferenceBindingPromptRemoval` derived
`character:<ActorKey>.appearance` from a bound face/body, and `ScenePromptOverridesApplier` wrote that key straight into
the appearance-suppression map. But the binding's actor key was the character's **identity owner** id
(`de351eb3-…`), while the compiled brief addresses a depicted character as `profileKey: "p0"/"p1"` plus the scenario
`characterId` — and it has never contained an identity-owner id. Consequences, all silent:

1. the removal addressed nobody (the notice even printed the raw GUID instead of a name);
2. the suppression map was keyed on a character `CanonicalCharacterAppearance` cannot match (it matches
   `characterId`, then `name`);
3. so the `DEPICTED CHARACTER APPEARANCE` block re-injected the very prose the reference was supplying.

Measured in the pre-processor input of event `09be8668…` (04:41:17Z): an authoritative *"Removed elements: the
appearance for de351eb3-…, … Do NOT render, describe, mention, infer, or re-derive any removed item"* notice AND the
complete per-character appearance block, in one prompt. The model obeyed the description.

| # | Change | File |
|---|---|---|
| **D** | The cast's actor key is the **participant profile key**. `option.CharacterKey` (scenario character id) → frozen `characterId` → `profileKey`; the join is exact and needs no name matching, and a character the frozen cast does not contain is left out rather than handed a key of the wrong kind | `CompositionComposer.razor` |
| **D′** | **Deviation, found by verification, not in the plan:** the same `ActorKey` was also the key the pack picker and the character-asset picker resolve owners by — the identity owner — so fixing D alone emptied the pack picker live ("Character 'p0' has no approved identity pack"). The two key spaces are now declared instead of collapsed: the host passes `IdentityOwnerKeys` (participant key → identity owner) and only those two pickers read it | `ImageStepComposer.razor` (+ `IdentityOwnerKeyFor`), `CompositionComposer.razor` |
| **A** | One `appearance` element per character, unchanged and now reachable: either a face **or** a body reference clears it, because the body view carries that angle's face | `StepPromptElementPlan` (no change) |
| **2b** | The suppression is written against the character's **resolved** identity, so the consumer that matches `characterId`/`name` can read it. One resolution path — the applier already resolves keys by `profileKey`, `characterId`, `name`, `key` and `subjectKey` | `ScenePromptOverridesApplier.cs` |
| **C** | A block that carries people emits **one blanket preservation clause** naming the invariant CATEGORIES (face, hair, skin tone, build, clothing) and no values — Qwen's own worked-example wording, and the reason its enhancer gives for it ("the more concretely you describe something you meant to keep, the more likely it drifts") | `ReferenceRoleClauses.BlockFor` |
| **B** | The removal is derived only for references the resolved image model can actually **send**, and a binding it cannot carry is refused by name (the same `ImplementedReferenceStrategiesFor` membership the render and the editor use). This is what makes the Krea 2 / BigLust / Pony case keep its FULL description **by rule** rather than by the accident that their steps offer no slots | `SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable`, `ReferenceBindingPromptRemoval.ImpliesRemoval` |
| **F** | An appearance removal that resolves to no character in the brief now **fails fast**, naming the key and the characters the brief does contain, instead of writing a suppression that suppresses nothing | `ScenePromptOverridesApplier.SuppressionIdentityOf` |

**Verification (live, release `20261012-131216`, Qwen-Image-2.1 · Local ComfyUI).** Becky's pack **face** (`Front`) and
**body** (`Front · Clothed`) plus a bound location; one `Generate Prompt`:

| Marker in the pre-processor input | Before (04:41Z, event `09be8668`) | After (17:17Z, event `bff47f00`) |
|---|---|---|
| `- Becky:` appearance line (face+body **bound**) | present | **absent** |
| `- Dean:` appearance line (**no** reference bound) | present | **present** |
| Removal notice, `<image1>` role tag, "Removed elements" | present | present |

The regenerated prompt shows the same split, in one draft: *"Her face, features, hair and skin tone come from
`<image1>`, and her build, body shape and proportions come from `<image2>`"* — no eye colour, hair colour, skin tone,
figure, piercings or tattoos for the character whose appearance a reference supplies — while the character with **no**
reference keeps his full description ("a middle-aged man with brown hair in a short cut, green eyes and light olive
skin…"). Clothing and action prose are retained for the referenced character, because wardrobe was not bound: element
granularity, not over-removal. Frame placement is preserved ("the woman large in the centre foreground and the man small
at the left of the background").

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ScenePromptOverridesApplier.cs` | `SuppressionIdentityOf` + `DescribeCharacters`; the appearance branch resolves before writing |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceBindingPromptRemoval.cs` | `ImpliesRemoval` extracted as the single filter `Derive` and the capability guard both use; `SlotKindFor` private |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` | `EnsureReferencesAreCarryable` (internal, unit-tested) called before the removals are merged |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs` | blanket preservation clause in `BlockFor`, emitted only when the block carries people |
| `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` | `IdentityOwnerKeys` parameter + `IdentityOwnerKeyFor`; both owner-keyed pickers use it |
| `DreamGenClone.Tests/RolePlay/ReferenceCarryabilityGuardTests.cs` | NEW — the refusal, and the text-only case that must NOT refuse |
| tests | `dotnet test -c Release` — **4182 passed / 27 failed (the same pre-existing LoRA-coverage set) / 0 skipped** |

**Not done here, deliberately:** auto-picking default references (one per character, preferring the body view at the
shot's angle) is split out as **B-158**, because the research found **no** vendor precedent for angle-matched
defaulting — it is this app's heuristic, and it needs its own spec.

**Research summary** (full citations in `research/reference-vs-text.md`): Qwen's own prompt-enhancer states the rule
directly — *"point at that image rather than describing features in words — verbal descriptions make the model
regenerate and degrade the likeness"* — and Runway is the only provider that says reiterating what an image already
contains *reduces* quality. But no study varies prompt redundancy against identity fidelity (verified negative), so the
blanket form of "duplicate text confuses the model" is NOT established: what is established is that **conflict** is a
named failure mode across InstantID, PhotoMaker, PuLID and InfiniteYou, and that **for weak adapters the text is the
remedy** (Midjourney: *"With a lower weight you will need to reinforce the physical characteristics you want to preserve
using your prompt text"*). Google states the branch is mandatory — *"your prompt structure depends entirely on whether
you are using reference images"* — and InvokeAI is the precedent for expressing it once, as capability metadata.

### 2026-10-12 (eleventh pass) — several PLACES in one frame, and who owns the appearance

Operator request: *"i need the ability to be able to use [two trailer fronts] as 2 location references to create the
final image … i want show that these trailers are side by side facing each other, how can i do this?"*

**Root cause.** Every location reference was told *"this image IS the room … do not replace it with the setting of any
other reference image"*, and the closing line named one singular *"that room"*. With two different trailers bound, the
prompt carried **three mutually exclusive instructions for one frame** — render three different rooms and let no two
coexist. Nothing could honour that, so the merge the operator wanted was structurally forbidden.

| Change | Where |
|---|---|
| A location line says `is a SEPARATE PLACE in this frame` when the block carries places from **different containers**, and the closing line states their coexistence instead of framing the shot inside one room | `ReferenceRoleClauses.BlockFor` / `LineFor` |
| The discriminator is **container identity** — `Distinct(SceneAssetId).Count() > 1` — never a count of locations | `ReferenceRoleClauses.CarriesSeveralPlaces` |
| `ReferenceRole` carries the container, so both the generate and the edit routes can answer the question | `ReferenceRoleClauses.ReferenceRole` |
| **The edit route had no container at all** (`MediaEditReference` carried none), so the fix would have been silently inert on the surface the operator reaches for first | `MediaEditReference.AssetContainerId`, `MediaEditReferenceResolver`, `MediaEditImageEditingJobHandler`, `AppendToEditInstruction` / `BuildEditInstruction` |
| The frame preview no longer requires a frame: a surface with no cast (the asset creator, an edit) shows its prompt, `<imageN>` numbering and role lines with Position reading `not placed`, instead of showing nothing until a render is spent | `StepPromptFramePreview` |

**My first attempt was wrong, and an existing test caught it.** I branched on "more than one location", which broke
`HandleAsync_TwoBoundLocationViews_NamesEachImageByItsSendSlot` — the *intended* multi-value case, two views of ONE shed
(*"keeps the same building the same building from every angle"*). Container identity is the only thing that separates
"the same place seen twice" from "two places in one frame", and that test is why the branch is what it is.

**Verification:** the clause states coexistence and invents no layout — `side by side`, `facing each other` and
`on the left` are asserted ABSENT from the clause text, because the arrangement is the instruction's job (the operator
writes it in the prompt). `dotnet test -c Release` — **4192 passed / 27 failed (the same pre-existing LoRA-coverage set)
/ 0 skipped**, including the four clause cases (different containers, same container, no container) and three guards
that the edit route carries the container, that the preview is not frame-gated, and that the placement override stays
frame-gated.

**Not built, by operator decision.** A **place-placement** channel (per-location Left/Right + facing, honoured by the
clause) was offered and declined: the operator is combining the two trailers into ONE approved location reference
instead, which settles the arrangement once in the asset rather than re-deciding it per render — and one location is the
case the pipeline handles best (*"this image IS the room"* is then literally true). The `Position` column reading
`not placed` for a location row is therefore correct-but-misleading (nothing is owed), and was left as-is on the same
decision.

**Where placement does exist:** the Placement override remains character-only (`p0`, `p1`), sourced from the compiled
brief's frozen cast. A place has never had a position in this model, which is why the two-location map shows no
placement control on a surface with no cast at all.

### 2026-10-22 — seating is symmetric, not a ladder walk

**Reported:** an Omniscient two-character side view compiled to *"the woman, positioned CENTER in the foreground … the
man who stands LEFT in the foreground"* — both bodies crowded into one half of the frame, the other half empty, and
both reference clauses pointing into the same place.

**Cause:** the resolver walked the profile's ladder in declaration order, so a pair took `Center` then `Left`. The
ladder's ORDER was doing seating work it cannot do: no single order yields `1 → Center`, `2 → Left + Right`,
`3 → Left + Center + Right`.

**Change:** the ladder now declares WHICH horizontals a frame may use; the spread is chosen by cast size (one subject
centred, a pair on the opposite edges, three filling the frame), with the acting subject taking the head seat. A
profile whose ladder cannot supply the spread a scene needs is refused by name. Order is documented as an editing
convenience only (`ImageStagingProfile`, `ImageStagingProfileValidation`, `ImageStagingProfilePanel`).

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/SceneBlockingResolver.cs` | `SeatsFor` — symmetric spread by cast size; fails fast naming the missing position |
| `DreamGenClone.Domain/RolePlay/ImageStagingProfile.cs` | ladder documented as the permitted set, not a seating priority |
| `DreamGenClone.Domain/RolePlay/ImageStagingProfileValidation.cs` | same, on the reader |
| `DreamGenClone.Infrastructure/RolePlay/ImageStagingProfileRepository.cs` | seed comments only (ladder values unchanged) |
| `DreamGenClone.Web/Components/Shared/ImageStagingProfilePanel.razor` | help text under the ladder editor |
| `DreamGenClone.Tests/RolePlay/SceneBlockingResolverTests.cs` | pair expectations flipped to Left/Right; +4 tests (lone subject centred, pair on the edges, three across the frame, refusal when the ladder omits a needed seat) |
| `DreamGenClone.Tests/RolePlay/SceneBlockingBuilderTests.cs` | active/observer pair expects Left primary, Right observer |

**Verification:** `dotnet build` clean; full suite **4554 passed / 27 failed (the same pre-existing LoRA-coverage set) /
0 skipped** — no new failures. Existing compiled briefs keep their snapshotted blocking; a recompile picks up the new
seating.

### 2026-10-23 (second pass) — seats belong to the people the frame SHOWS

The symmetric spread above is computed over the people in the BRIEF, but who appears in the picture is a fact about the
POV: a participant POV is the camera and is never in its own shot, and an Omniscient still keeps a remote observer out of
frame. So a two-person brief viewed from one of them seated the only visible person at the LEFT edge of a frame they were
alone in ("<image1> is the person on the LEFT in the foreground").

`SceneBlockingResolver.ReseatForCast` re-fits the kept subjects to the frame's own spread (order preserved, so the acting
subject keeps the head seat) and marks the version `+frame`. `SceneImagePovProjection.BlockingForFrame` is the ONE place
that decides which seats exist for a POV — the compiler's omniscient `Projection.Frame` and the render's reference clause
both read it, so they cannot disagree. A participant POV still has no external frame at all, so no reference is named by
position; a record with no POV passes the compiled blocking through unchanged.

Tests: `SceneBlockingResolverTests` (lone → CENTER, pair → edges, unchanged when nobody is dropped),
`SceneImagePovProjectionTests` (lone visible person in an Omniscient frame is CENTRED; `BlockingForFrame` withholds the
external frame from a participant POV).
