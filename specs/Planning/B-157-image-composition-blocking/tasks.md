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
- [ ] **T0.2** Pin the subject-key join before writing the resolver. Confirm, with evidence, how a `typedReferences`
  entry's `subjectKey` maps to a beat character (`profileKey`/`characterId` in
  [`SceneImageBeatAnalysisService.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageBeatAnalysisService.cs))
  and to the bound `ReferenceApplicationSelection` (whose per-character label is currently read as `CharacterName` at
  [`SceneImageRenderingJobHandler.cs:559`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)).
  *Acceptance:* a mapping table in `FINDINGS-baseline.md` sourced from a real snapshot row + the binding type, and a
  named single owner for the accessor (consumed by T4.2). If the three sources disagree, that disagreement is the
  first defect to fix — record it.

## Phase 1 — `SceneBlocking` + the staging profile (configuration in the DB, never in code)

- [ ] **T1.1** `ImageStagingProfile` domain entity + `ImageStagingProfileValidation` (pure, fail-fast, names the exact
  field). Fields: the involvement→placement rule table, the default camera/shot, the surface it applies to
  (Composition / Asset Manager), display name, version, `UpdatedUtc`. Domain project, zero dependencies.
  *Acceptance:* validation tests refuse each missing/invalid field by name.
- [ ] **T1.2** `IImageStagingProfileRepository` (Application) + `ImageStagingProfileRepository` (Infrastructure):
  `EnsureSchemaAsync` creating `ImageStagingProfiles`, DO-NOTHING seeds, list/find/upsert. Mirror
  `ImageCompilerProfileRepository` including the "a bad seed row is a bug in this file" validation on write.
  *Acceptance:* round-trip + seed-idempotence tests; a second `EnsureSchemaAsync` run is a no-op.
- [ ] **T1.3** `IImageStagingProfileResolver` + resolver: resolves the profile for a render from configured data and
  **throws naming the profile** when none applies. No code default, no "if missing then X".
  *Acceptance:* no-fallback tests (≥ 3: missing, empty, wrong-surface).
- [ ] **T1.4** Register the repository + resolver in [`Program.cs`](../../../DreamGenClone.Web/Program.cs) as
  required (singleton) dependencies, matching how `IImageCompilerProfileRepository` /
  `IImageCompilerProfileResolver` are registered.
  *Acceptance:* a DI-resolution test asserting the dependency is required, not optional.
- [ ] **T1.5** `SceneBlocking` record + `SceneBlockingResolver` (pure, deterministic, no LLM). Inputs: per-character
  `physicalLocation`/`position`/`sightline`, `Moment.CompositionRationale`, `participantSummary[].involvement`, and
  the staging profile. Output: `subjects[{subjectKey, framePosition{horizontal,depth}, roleText}]`, `camera`, `layering`,
  `rationale`, `profileVersion`. Keyword-extract only known tokens (`tight/wide/medium/close`,
  `left/right/from X's side`, `low/high`); no match ⇒ the profile's default camera.
  *Acceptance:* unit tests over real brief fixtures for one and two subjects; a missing input that the profile cannot
  resolve throws a diagnostic naming the field — it never invents a placement.
- [ ] **T1.6** Staging-profile editor UI: a new tab on the `/profiles` page
  ([`ThemeProfiles.razor`](../../../DreamGenClone.Web/Components/Pages/ThemeProfiles.razor)), next to the existing
  Theme / Intensity / … / Templates tabs, editing the `ImageStagingProfiles` rows.
  *Acceptance:* razor self-validation checklist from `razor-editing.instructions.md`; the tab reads and writes the same
  table the resolver reads; no hardcoded placement value in the component.
- [ ] **T1.7** DbQuery coverage for the new table (named idempotent command or a `.sql` seed), registered in the
  dispatcher only if a named command is genuinely useful.
  *Acceptance:* re-running the command twice leaves the rows unchanged.

## Phase 2 — Emit `blocking` into the Still brief (snapshot, versioned)

- [ ] **T2.1** [`DeterministicMultimodalMediaCompiler.BuildStill`](../../../DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs)
  emits a `blocking` member (resolver output) beside `typedReferences`, so the brief is the single snapshot of *what*
  the scene is **and** *how it is framed*.
  *Acceptance:* `MultimodalMediaCompilerTests` assert the member's presence and shape; the existing
  `RequireNoCoverage(StillImage)` behaviour is unchanged.
- [ ] **T2.2** Persistence/reload: `SemanticInputSnapshotJson` round-trips the blocking through
  [`CompiledMediaBriefRepository.cs`](../../../DreamGenClone.Infrastructure/RolePlay/CompiledMediaBriefRepository.cs)
  with no schema change.
  *Acceptance:* repository test asserts byte-stable round-trip.
- [ ] **T2.3** Reproducibility: editing the staging profile **after** a brief is compiled does not change that brief's
  blocking.
  *Acceptance:* a test that compiles, edits the profile row, and asserts the stored snapshot is unmoved.

## Phase 3 — `FinalImagePromptPlan`: one assembly + one validation owner

- [ ] **T3.1** `FinalImagePromptPlan` — inputs: prose draft, `typedReferences`, bindings in send order, `SceneBlocking`,
  leading-image count (0 generate / 1 edit). Output: final prompt text + a **reference map**
  (`<imageN>` → subject → slot → position) + the validation result.
  *Acceptance:* pure unit tests for the map, including the two-subject two-slot case.
- [ ] **T3.2** Validation (fail-fast, no new constants): tag count == image count; each subject referenced exactly once
  per slot; no two subjects share a position; reference count within
  `ReferenceSlotPlanner.EffectiveBudget(maxReferences, filledSlotKinds)`; the N = 1 no-tag rule preserved.
  *Acceptance:* tests for each rejection, plus a guard test that the budget/threshold values are **read from**
  `ReferenceSlotPlanner`/the model row and are not literals in the new code.
- [ ] **T3.3** Port [`SceneImagePromptGenerationJobHandler.cs:344`](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs)
  to the plan.
- [ ] **T3.4** Port [`SceneImageRenderingJobHandler.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)
  — the composed-render path (~441/464), `AddIdentityPackReferencesAsync` (~577/632), and the pack path (~824/860).
- [ ] **T3.5** Port [`SceneAssetGenerationJobHandler.cs`](../../../DreamGenClone.Web/Application/RolePlay/SceneAssetGenerationJobHandler.cs)
  — the asset paths (~598/607, ~729/735) and the slot path (~1109/1134).
- [ ] **T3.6** Port the **edit** path: [`MediaEditImageEditingJobHandler.cs:145`](../../../DreamGenClone.Web/Application/RolePlay/Editing/MediaEditImageEditingJobHandler.cs)
  and [`MediaEditReferenceResolver.cs:250`](../../../DreamGenClone.Web/Application/RolePlay/Editing/MediaEditReferenceResolver.cs),
  keeping `LeadingImageCount.Edit` semantics. Edit-side blocking stays **deferred** (plan decision 6): this port is the
  label fix + the single assembly owner, not a blocking implementation for edits.
- [ ] **T3.7** Guard test: no `AppendToImagePrompt` / `AppendToEditInstruction` call site remains outside the plan —
  the same reflective-guard style as
  [`ImageNegativePurgeGuardTests.cs`](../../../DreamGenClone.Tests/RolePlay/ImageNegativePurgeGuardTests.cs).
  *Acceptance:* the guard fails if a future handler re-introduces an ad-hoc append.

## Phase 4 — Role clause re-keyed on subject + position

- [ ] **T4.1** `ReferenceRole` gains `SubjectKey` (appended, optional, so existing positional construction still
  compiles).
  *Acceptance:* all existing call sites compile unchanged.
- [ ] **T4.2** `ReferenceBindingShape` gains the single binding→subjectKey accessor (owner fixed by T0.2);
  `RolesFor` populates it.
  *Acceptance:* the accessor has exactly one definition and the guard test from T3.7 still holds.
- [ ] **T4.3** `BlockFor` emits the subject + position form when blocking is supplied (new overload taking
  `SceneBlocking`) and the current view-label form when it is not. `<imageN>` numbering, send order
  (`ReferenceBindingShape.InSendOrder`) and the N = 1 rule are **unchanged**.
  *Acceptance:* the edit-offset tests in
  [`ReferenceRoleClauseTests.cs`](../../../DreamGenClone.Tests/RolePlay/ReferenceRoleClauseTests.cs) still pass
  unmodified.
- [ ] **T4.4** Regression test for the reported collision: two characters whose packs share a view label ("3/4 Left")
  must produce two distinct, position-qualified lines.
  *Acceptance:* the test fails against the old view-label form (assert the failure mode, not just the new output).
- [ ] **T4.5** Keep `ForPreprocessor` consistent with the new form.
  *Acceptance:* the pre-processor clause and the appended block name the same positions for the same fixture.

## Phase 5 — Prose and role clause share one placement vocabulary

- [ ] **T5.1** Seed the pre-processor input with `blocking` in
  [`NaturalLanguageSceneImagePromptBuilder.BuildCanonicalUserPrompt`](../../../DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs)
  and the Pony path ([`PonySceneImagePromptBuilder.cs:623`](../../../DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs)).
- [ ] **T5.2** Test that the generated prose names positions that agree with the role clause for a two-character
  fixture.
  *Acceptance:* one vocabulary — a position word in the clause appears in the prose and no position is contradicted.

## Phase 6 — Asset Manager: minimal binding→prompt assembler

- [ ] **T6.1** Run `FinalImagePromptPlan` with an **empty prose** input and the Asset-Manager staging profile, so slots
  render directly to the role block with no operator typing.
  *Acceptance:* a bindings-only render produces a role-clause prompt; a missing Asset-Manager staging profile fails
  fast by name (no "centered by default" fallback in code).
- [ ] **T6.2** Tests for the assembler path (present + absent profile).

## Phase 7 — Operator preview in the shared composer

- [ ] **T7.1** Decide and **record** the placement-override mechanism (T-plan correction row above): extend
  `ScenePromptOverrides` with the blocking element keys applied by `ScenePromptOverridesApplier`, or a dedicated
  `SceneBlocking` override. Exactly one mechanism ends up in the code.
  *Acceptance:* the decision plus its rationale is written into this file and `plan.md`; a guard test asserts the
  non-chosen mechanism does not exist.
- [ ] **T7.2** Extend `StepPromptPanel` (via [`ImageStepComposer.razor:322`](../../../DreamGenClone.Web/Components/Shared/ImageStepComposer.razor))
  with a read-only **Final prompt** view showing the assembled prompt, the reference map, and the blocking, plus the
  chosen placement override that rewrites only the `SceneBlocking` and re-runs the assembly.
  *Acceptance:* razor self-validation; the panel remains host-driven (data in, no services); no second surface.
- [ ] **T7.3** Preview contract test (final prompt + reference map + override re-run produces a changed plan whose
  `<imageN>` map is unchanged).

## Phase 8 — Validation, docs, close-out

- [ ] **T8.1** Acceptance run against the real case: a two-character Composition with face+body+wardrobe (+location
  where binding exists), then verify the seven acceptance criteria in
  [`spec.md` §5](spec.md). Report pass/fail **honestly** with the final prompt text and the render.
- [ ] **T8.2** Full suite green (`dotnet test`), no skipped tests; fix forward for any failure.
- [ ] **T8.3** Docs: update `plan.md` status, this file's checkboxes, [`README.md`](README.md) state, and the backlog
  B-157 row (→ `implemented`, or `debugging` with the unmet criterion named).
- [ ] **T8.4** Cross-item notes recorded: B-147 S4 (edit LoRAs) remains deferred and is **not** absorbed here; B-126
  remains the multi-character program this slices; if the location binding was the gap, the finding is filed against
  B-145/B-148 with the evidence from T0.1.

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
