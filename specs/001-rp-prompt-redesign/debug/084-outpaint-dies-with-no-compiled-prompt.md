# 084 — "Extend this image" always fails: "An edit run requires the compiled prompt its subject writer prepared."

**Session:** 2026-10-02 · **State:** implemented, build green, RolePlay suite 3410 passed / 4 pre-existing failures
**Scope:** the two media-edit subject writers' routing of `MediaEditOperationKind.Outpaint`.
**Related:** CASE-24 (outpaint), 083 (the confined-edit composite, found in the same pass), 066 (the claim seam).

---

## Report

Operator, using the Asset Studio's extend control:

> "Extend this image"
> "An edit run requires the compiled prompt its subject writer prepared."

Every outpaint, on any subject kind, died the same way.

---

## Analysis

The compiled-edit path is selected by an allowlist in both writers, and `Outpaint` was missing from it:

```csharp
if (context.Operation.Kind is not MediaEditOperationKind.Edit and not MediaEditOperationKind.MaskedRegion)
    return await PrepareOperationAsync(...);   // a plan with no Prompt, no references, no editor
```

Everything else already agreed that an outpaint is an edit:

- `MediaEditCompilationService.EnqueueRunAsync` treats `MaskedRegion or Outpaint` as one kind, and the compiled
  attempt carries the outpaint geometry (`OutpaintJson`) beside the prompt revision.
- `MediaEditImageEditingJobHandler` routes `Outpaint` into the edit path (it is not in the deterministic-operation
  branch) and `ExecuteAsync` appends the geometry sentence to the compiled prompt.
- The queued row proves it: job `media-edit-image-editing:0dfe3d07…` carried `operationKind: 6` with
  `outpaint {direction: Right, percent: 50, growMaskBy: 8, featherPixels: 32}`, and its row had full compiled
  provenance (`editSessionId`, `compilationAttemptId`, `promptRevisionId`, `promptSha256`). The compiler had done its
  work; the writer threw it away and handed the handler a plan from the operation path, which carries no prompt.

The same allowlist gated the scene writer's `ClaimAsync`, so even once prepare was fixed the row would never have been
claimed — and the completion transition only matches `Generating`, which is the "render silently thrown away, row stuck
pending forever" shape debug record 066 already fixed for edits.

---

## Resolution

- `SceneAssetMediaEditSubjectWriter.PrepareAsync` and `SceneImageMediaEditSubjectWriter.PrepareAsync`: `Outpaint` joins
  the compiled-edit path, alongside `Edit` and `MaskedRegion`.
- `SceneImageMediaEditSubjectWriter.ClaimAsync`: `Outpaint` claims like the other two edit kinds.
- Both `PrepareOperationAsync` implementations now **refuse** a confined kind by name instead of silently building a
  prompt-less plan — so a future change to the allowlists fails loudly at prepare rather than at the handler.

No graph, mask, prompt-assembly or storage change: the outpaint already had everything it needed.

---

## Evidence

- **Reproduced first**: an asset-subject outpaint job through the real writer failed with exactly the operator's
  message before the fix (this was the failing test that pinned the defect).
- `MediaEditImageEditingJobHandlerTests.OutpaintRun_ThroughTheRealWriter_ResolvesTheCompiledPrompt` — the outpaint now
  completes through the real writer, and the instruction the editor received is the compiled revision **plus** the
  geometry sentence ("Change the shirt to red." + "Extend the image to the right…"), which is proof it came from the
  compiler output; a padded mask is passed.
- `SceneImageMediaEditSubjectWriterClaimTests.ConfinedRun_ClaimsTheQueuedRow` (`MaskedRegion`, `Outpaint`) — the row
  reaches `Generating`, so the guarded completion can accept the result.
- `MediaEditImageEditingJobHandlerTests.OutpaintRun_KeepsTheNewStripAndFadesIntoTheOriginal` (083) — the padded-canvas
  composite of the outpaint result, read pixel by pixel.
- `dotnet test … --filter "FullyQualifiedName~RolePlay"` → **3410 passed, 4 failed**, the same 4 pre-existing failures
  from other in-flight worktree work (3 × `SdxlSceneImagePromptBuilderTests`, 1 × `SceneLoraSelectionWireTests`).

## Files

- `DreamGenClone.Web/Application/RolePlay/Editing/SceneAssetMediaEditSubjectWriter.cs`
- `DreamGenClone.Web/Application/RolePlay/Editing/SceneImageMediaEditSubjectWriter.cs`
- `DreamGenClone.Tests/RolePlay/MediaEditImageEditingJobHandlerTests.cs`
- `DreamGenClone.Tests/RolePlay/SceneImageMediaEditSubjectWriterClaimTests.cs`
