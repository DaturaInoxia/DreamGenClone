# B-133 — plan

**Created:** 2026-09-27

## 1. Why an edit pass and not a better prompt

The cell render is a text-to-image pass *conditioned on pixel references*. Measured 2026-09-27 across the 19 live
attempts (whole-frame mean / subject band mean / shadow p10, 0–255):

| lighting in the prompt | n | frame | subject | shadow p10 |
|---|---|---|---|---|
| flat daylight outdoors | 9 | 163 | 144 | 40 |
| golden hour | 6 | 110 | 140 | 36 |
| dim indoor | 4 | 91 | 101 | 28 |
| night, one practical | 2 | 57 | 76 | 9 |
| hard rim | 2 | 41 | 69 | 3 |

The range is real; the **floor on the subject (≈65)** is the reference's own exposure, which the prompt cannot
overrule. An edit instruction over a finished image can, because by then the identity is already in the pixels — there
is no reference left to fight.

## 2. Architecture (reuses what exists)

| Concern | Reused | New |
|---|---|---|
| Edit session (subject-agnostic) | `MediaEditSession` (`MediaEditSubjectKind.SceneImage \| AssetImage`, `SourceImageId`) | — |
| Operation | `MediaEditOperationKind.Edit` + `MediaEditOperationExecutorResolver` | — |
| Dispatch | `MediaEditImageEditingJobHandler`, `MediaEditJobPayloads`, `MediaEditProvenance` | a payload field for the picked preset key + instruction source |
| Write-back | `SceneAssetMediaEditSubjectWriter` / `SceneImageMediaEditSubjectWriter` → `AddDerivedImageAsync(sourceImageId, operation, …, candidateBatchId)` | — |
| Free-text intent | `MediaEditCompilationService` + vision compiler + revisions | — (presets bypass it by design, D1) |
| UI | `ImageEditWorkspace.razor`, `ImageStepBlueprintFactory.ForEdit` / `ForEditElements`, `ImageStepComposer.razor` | preset picker control, Lighting/Expression tabs, "apply an edit pass" action |
| Wording store | `ImageWorkflowPromptTemplates` + `IImageWorkflowTemplateService` | `image.preset.*` rows |

**A LoRA cell attempt is an `AssetImage`** (a `SceneAssetImage` in the dataset container), so "any image" needs no new
subject kind.

## 3. Phase 1 — preset data (IMPLEMENTED 2026-09-27)

- `DreamGenClone.Domain/RolePlay/ImagePresetKeys.cs` — `ImagePresetAxis`, `ImagePresetMode`, the 24 keys, `All`,
  `ShortName` (strips either namespace), `PresetKeyFor` (LoRA vocabulary → preset, refusing any non-lighting/
  non-expression axis by name), `AssemblyKey`, `PreserveKey`, `AxisOf`.
- `DreamGenClone.Infrastructure/RolePlay/ImageWorkflowRepository.cs` — 24 seeded rows (2 preserve clauses, 4
  assemblies, 6 lighting details, 12 expression details), with the rationale in the seed comment.
- `DreamGenClone.Web/Application/RolePlay/Editing/ImagePresetInstructionComposer.cs` — deterministic assembly with the
  same "no unfilled slot ever reaches a model" guarantee as `LoraCellPromptComposer`, plus the preserve-clause rules.
- Tests: `ImagePresetSeedTests` (10) and `ImagePresetInstructionComposerTests` (10).

**Operational note:** new rows are created by the seed on app startup (`INSERT OR IGNORE`), so the app needs a restart
before the presets are visible; a hand-inserted row is not the fix and not needed.

## 4. Phase 2 — the generic action

- A shared "apply an edit pass" action rendered by the image surfaces (attempt deck, composition list, asset review)
  for any `SceneAssetImage` / produced image.
- It starts a `MediaEditSession` with `SubjectKind` chosen by the host store, `SourceImageId` = the clicked image.
- The panel shows: the source, the preset list grouped by axis, the **assembled instruction** (read-only preview with
  an "edit the wording" affordance that switches to free text + compiler), and the host's default preset.
- The run carries the preset key in the payload for provenance; the result is a derivative in the same candidate
  batch, so it appears under its source.
- Fail-fast: no editor model resolved, unknown preset key, missing assembly row, or a preset on a source image that is
  not a rendered image → refuse naming the reason (no fallback to the compiler).

## 5. Phase 3 — the tabs

- `ImageStepComposer` gains a **slot-independent instruction control** (the B-130 plan already anticipates a
  slot-independent step operation for region editing; this is the same shape): a picker that writes the step's
  instruction, not a reference slot.
- **Edit** tab: Lighting and Expression pickers → `Change` assemblies.
- **Compose** tab: the same pickers → `Condition` assemblies, with the note that a compose-side lighting change is
  fighting any dominating reference and the edit pass is the reliable route.
- The host declares which presets it offers and which one is the default; the composer never guesses.

## 6. Phase 4 — LoRA wiring

- `CharacterLoraCellService.RenderCellAsync` (or its accept step) records the preset key used on the attempt.
- The accepted member carries that key; `LoraCellPromptComposer.ComposeCaption` is fed the recorded preset's phrase
  instead of the plan's snapshot for that axis.
- Curation gates run against the accepted (edited) image.
- Test: a member accepted from a relit attempt has a caption naming the relit condition.

## 7. Blast radius

Additive. No new operation kind; the shared edit pipeline (garment removal, crop, enhance, mirror, identity/body
flows) is untouched; new payload field is optional; the only behaviour change to an existing flow is the LoRA caption
source in Phase 4, which is covered by its own test.

## 8. Known risks

| Risk | Mitigation |
|---|---|
| The edit pass drifts identity | The preserve clause is mandatory in `Change` mode and the first real pass is measured against the source (identity + light level), not eyeballed. |
| Expression edits on full-body frames | Phase 3 offers the expression tab only where the face can carry it. |
| Caption/image disagreement | Phase 4 D4, with a test. |
| A preset silently degrading into a label | Seed tests assert mechanics (light properties; facial action units) and a length floor. |
| Two wordings drifting (cell vocabulary vs preset) | Keys align by suffix, and `EveryMatrixAxisValue_HasAPreset` fails if either side grows without the other. |
