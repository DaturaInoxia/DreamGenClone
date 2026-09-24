# 066 — Identity edit succeeds but the SceneImages row stays `Pending` (studio spins forever)

**Reported:** 2026-09-21 ~22:50 local (2026-09-22T02:50Z)
**Reported by:** user — studio item "edit identity · Pending · 2026-09-21 10:50 PM"

## Report

- Image row: `95a3cadd-3825-44ef-88f5-846bfaccf45d`
- Session: `8bc36efb-b235-485b-8675-b98ad7754e59`, Interaction: `85da7c1b-cb10-413e-b40d-b445ea1e67b3`, Beat `b4`
- Stage: `Identity`, Operation: `Edit`, Source: `75dfc4bc-9045-4862-9932-af4e77b72d3f`
- `SceneImages.Status = 'Pending'`, `StartedUtc = NULL`, `Sha256 = NULL`, `FileRelativePath = NULL`
- The render **did** succeed and the PNG is on disk:
  `DreamGenClone.Web/data/scene-images/8bc36efb-b235-485b-8675-b98ad7754e59/95a3cadd-3825-44ef-88f5-846bfaccf45d.png`
  (1,196,784 bytes, 2026-09-21 10:51:18 PM)
- Queue job `media-edit-image-editing:95a3cadd-…` is `Complete`, `AttemptCount=1/1`, no error.
- App log (2026-09-21 22:51:18):
  - `[INF] Scene image stored at …\95a3cadd-3825-44ef-88f5-846bfaccf45d.png`
  - `[WRN] Scene image completion was refused and the row is unchanged: ImageId=95a3cadd-…, Operation="Edit", Status="Complete", FilePath=8bc36efb-…/95a3cadd-….png`
  - `[INF] Media edit image completed: Subject="SceneImage", … Stage=Identity`

**Blast radius in data:** 3 rows, all Identity-stage edits in the same session, all with a
rendered PNG on disk and a `Complete` queue job:

| ImageId | CreatedUtc | File on disk |
|---|---|---|
| `95a3cadd-3825-44ef-88f5-846bfaccf45d` | 2026-09-22T02:50:27Z | 1,196,784 B |
| `6bae2aa0-9086-4680-b1b8-736c32bdff97` | 2026-09-22T02:34:10Z | 1,397,430 B |
| `e335f53e-2b4b-4caf-b079-30c9e44639d5` | 2026-09-22T01:31:17Z | 1,292,205 B |

(`SceneImages` overall: 4 Pending rows, 3 of them Edit rows that were never claimed.)

## Analysis

Same defect class as debug record **053** (crop never completes) — but on the **Edit** path, and it
survived the 053 fix because 053 only repaired the *operation* variant.

- `SceneImageRepository.TryCompleteImageAsync` (`DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs:581`)
  is claim-guarded: `WHERE Id = $id AND Status = 'Generating'`.
- `TryCompleteOperationImageAsync` (line ~550) accepts `Status IN ('Pending','Generating')` — added by
  debug 053 because the shared operation path never claims a row.
- `SceneImageMediaEditSubjectWriter.CompleteAsync` picks the variant by operation kind:
  `output.Operation == MediaEditOperationKind.Edit ? TryCompleteImageAsync : TryCompleteOperationImageAsync`.
  Its own comment states the assumption: *"A render or edit is claimed first and keeps the stricter transition."*
- **That assumption is false on the post-B-124 unified run path.** The claim used to be performed by the
  scene-specific handlers (`SceneImageEditingJobHandler:116` / `SceneImageRenderingJobHandler:97`, both
  `image.Status = SceneImageStatus.Generating; … InsertImageAsync(…)`). The unified
  `MediaEditImageEditingJobHandler` — now the only producer for scene edits — never claims:
  `PrepareAsync` → resolve editor → `ExecuteAsync` → `writer.CompleteAsync`, with no status transition in between.
- Enqueue side (`SceneImageService` `DispatchEditAsync` call sites) inserts the row as `Pending` and never
  sets `StartedUtc`, so the row is still `Pending` when completion runs → the guarded UPDATE matches 0 rows →
  `CompleteAsync` logs the warning and returns (silently losing the result, exactly as recorded in 053).
- Asset-image rows are unaffected: `UpsertImageAsync` is unguarded (same asymmetry noted in 053).

Evidence the queue/file side is fine: job `Complete`, PNG present, `MediaEditImageEditingJobHandler` logged
`Media edit image completed` with model `qwen_image_edit_2511_fp8mixed.safetensors`, 50.9 s, 1 reference.

## Plan

**Fix A (recommended) — restore the claim in the one run path**
1. `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs` + `DreamGenClone.Application/RolePlay/ISceneImageRepository.cs`:
   add one guarded claim transition `TryClaimImageAsync(imageId, startedUtc)`
   (`UPDATE SceneImages SET Status='Generating', StartedUtc=…, UpdatedUtc=… WHERE Id=$id AND Status='Pending'`).
2. `DreamGenClone.Web/Application/RolePlay/Editing/IMediaEditSubjectWriter.cs`:
   add `Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken ct)` to the writer seam.
3. `SceneImageMediaEditSubjectWriter`: implement the claim for `Operation.Kind == Edit` (no-op for operations,
   which are deliberately never claimed).
4. `SceneAssetMediaEditSubjectWriter`: implement the claim for its own store (documented unguarded upsert).
5. `MediaEditImageEditingJobHandler`: claim once, after `PrepareAsync` returns a plan and before the editor call;
   skip the run when the claim is refused (row already cancelled/completed).
6. Data repair for the 3 stuck rows (separate, user-approved step): SHA-256 + size from the file on disk,
   `Status='Complete'`, `FileRelativePath`, `Sha256`, `ImageSize`, `ModelIdentifier`/`ProviderName`/
   `ContentPolicy` from the log, `CompletedUtc` from file mtime.

**Fix B (minimal)** — one line: make `CompleteAsync` use `TryCompleteOperationImageAsync` for Edit as well.
Rejected as the primary fix: it abandons the claim transition the design wants for edits, leaves `StartedUtc`
null, and re-introduces the duplicate-variant divergence 053 was about.

**Blast radius (Fix A):** the shared media-edit seam (both subject kinds), the scene-image repository
contract, and their tests (`MediaEditImageEditingJobHandlerTests`, `SceneImageRepositoryTests`,
`SceneAssetImageEditRunCutoverTests`, `SceneImageEditRunCutoverTests` / `MediaEditCompilationServiceTests`).
No prompt/RP-engine behaviour changes; no gate/config values involved.

## Resolution

**Applied 2026-09-21 (Fix A, approved by user).**

1. `DreamGenClone.Application/RolePlay/ISceneImageRepository.cs` — new guarded transition
   `TryClaimImageAsync(imageId, startedUtc)`.
2. `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs` — implementation:
   `UPDATE SceneImages SET Status='Generating', StartedUtc=$startedUtc, UpdatedUtc=$startedUtc
   WHERE Id=$id AND Status='Pending'`; returns false for a terminal/already-claimed row.
3. `DreamGenClone.Web/Application/RolePlay/Editing/IMediaEditSubjectWriter.cs` — `ClaimAsync` added to
   the writer seam, documented as "required where the store is claim-guarded, deliberately satisfied where
   it is not".
4. `SceneImageMediaEditSubjectWriter.ClaimAsync` — claims an `Edit` run; an operation is never claimed
   (its completion accepts `Pending`); a row already in `Generating` is reported as claimed so a died
   delivery is finished rather than abandoned; a terminal row refuses the run.
5. `SceneAssetMediaEditSubjectWriter.ClaimAsync` — reports the claim as satisfied: the scene asset store
   has no claim transition (Pending/Complete/Failed, unguarded upsert), so nothing is invented.
6. `MediaEditImageEditingJobHandler` — claims once, after `PrepareAsync` and BEFORE any work is paid for;
   a refused claim skips the run with a log line instead of spending an editor call.

**Tests** (new/updated):
- `SceneImageRepositoryTests.TryClaimImage_ClaimsAQueuedRowOnce_AndUnlocksItsCompletion`,
  `TryClaimImage_RefusesATerminalRow`.
- `SceneImageMediaEditSubjectWriterClaimTests` (new, 6 tests): claim unlocks completion, already-claimed
  row is reported claimed, terminal rows refuse (Complete/Cancelled/Failed), unknown image fails fast,
  operation run is never claimed and still completes through the operation transition.
- `MediaEditImageEditingJobHandlerTests.Run_ClaimsTheRowBeforeAnyWorkIsPaidFor`,
  `Run_SkipsWhenTheRowCanNoLongerBeClaimed` (+ a recording subject writer).
- Targeted runs: 116 + 461 passed; the only failures are the 3 documented pre-existing
  `SdxlSceneImagePromptBuilderTests` failures (unrelated workstream).

**Data repair (same day, same approval):** the 3 stuck rows are now `Complete`, with the values that the
run really produced — `Sha256`/`ImageSize` read from the stored PNG, `ModelIdentifier`
`qwen_image_edit_2511_fp8mixed.safetensors`, `ProviderName` `Local ComfyUI (WOOD-GAME-MAIN 5080)` and
`ContentPolicy` `AdultAllowed` taken from the only **enabled** registered model carrying that identifier
(its provider record holds `ContentPolicy=2` = AdultAllowed), `StartedUtc = CreatedUtc` (the job ran
immediately) and `CompletedUtc` = the moment the bytes were stored. `Rows affected: 3`; the
stuck-pending audit is now 0 for edit rows.

**Still observed (NOT part of this fix):** one unrelated `Pending` row remains —
`d2d69d9b-af23-4967-8d1a-7800ebe3fdde`, Composition/Generate, created 2026-09-08, no file on disk and no
job activity. Left untouched on purpose.

## Validated

- [x] Code fix + targeted tests (2026-09-21)
- [x] **Live validation after the 23:06 restart (same session 8bc36efb, same interaction 85da7c1b):**
  8 scene-image edits ran through the unified job, **0** `Scene image completion was refused`, **0** claim
  skips. Seven of them are in `SceneImages`, all `Complete`, all with `StartedUtc` written by the new claim —
  e.g. `50724d19-6028-4770-adcd-d50494ea1c3c` (Identity) claimed `03:45:01.63Z`, completed `03:45:38.74Z`;
  `eea7cfd7…`, `99a3cf6f…`, `ee16eb2c…` likewise Identity stage. The last refusal in the whole log is
  `22:51:18` — the pre-fix run that started this record. Asset edits are unaffected
  (`Media edit image completed: Subject=AssetImage …` at 23:51:11), confirming the asset writer's
  "claim satisfied" answer.
- **Open observation (not explained, not chased):** `c49b4875-8839-43c6-a2ca-ee5b46633839` completed at
  23:16:11 (row stored + served), yet its `SceneImages` row and its PNG are both gone now, with no
  delete/discard/purge line anywhere in the log. Asked the user whether it was discarded deliberately.
