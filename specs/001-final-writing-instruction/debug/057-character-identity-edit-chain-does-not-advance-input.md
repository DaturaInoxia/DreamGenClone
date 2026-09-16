# 057 — Character Identity edit workspace does not chain the next input

## Report

- **Reported:** 2026-09-14, during B-121 Character Identity Studio implementation.
- **Symptom:** After the canonical front was selected and a garment-removal edit completed, the Character Studio did not clearly advance the edit workspace to the produced artifact for the next Crop/Enhance operation. The workflow could continue to operate against the original front image.

## Analysis

- B-121 `spec.md` FR21-003 requires every step to produce a new artifact without overwriting its input.
- B-121 `ui-contract.md` sections 4–6 require De-clothe, Crop, and Enhance to form a visible sequential chain.
- `CharacterIdentityGarmentService.ResolveGarmentSourceAsync` previously resolved only the Front step output.
- `CharacterStudio.razor` refreshed the edited-image list after `ImageEditWorkspace` completed, but did not replace the workspace subject with the newly produced image.
- The shared workspace already reports the produced `ImageEditResultView.ImageId`; the missing behavior was host-side chaining.

## Plan

- Resolve the current edit input from the immediately preceding completed pipeline step when the build is at De-clothe, Crop, or Enhance.
- When the shared edit workspace produces an artifact, update the Character Studio source and subject to that artifact so the next operation uses it.
- Preserve the existing non-destructive artifact model and shared edit pipeline.

## Resolution

- Updated `DreamGenClone.Web/Application/RolePlay/CharacterIdentityGarmentService.cs` to resolve the current pipeline input rather than always returning the original Front output.
- Updated `DreamGenClone.Web/Components/Pages/CharacterStudio.razor` so `OnGarmentResultAsync` replaces the workspace subject with the newly produced image.

## Validated

- [x] `get_errors` reports no errors for the touched `.cs` and `.razor` files.
- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` succeeds after stopping the running web process.
- [x] Focused Character Identity test command builds `DreamGenClone.Tests` successfully and runs the filtered suite; existing project warnings remain, with no new compile error from this change.
- [ ] Runtime confirmation pending: run De-clothe, then confirm Crop and Enhance operate on the newly produced artifact.
