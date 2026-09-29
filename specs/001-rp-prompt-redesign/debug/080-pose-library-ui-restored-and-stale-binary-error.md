# 080 — Library-management UI restored to the pose page, and the composer's runtime error

## Report

Operator, 2026-09-28, verbatim: **"why did you add back New library name, Export projected poses, Download a pack (zip
URL) to the UI , remove all of it., the text this pose button throws and runtime error fix it"**

Three separate things were wrong at once.

## Analysis

### 1. The UI came back through a merge, not through a new decision

`git log` on `PoseLibraryPage.razor` shows HEAD is a **merge of two writers' versions** of the same file
(`562ec01`), whose own message says *"Two writers had each been saving their own version of this file… Each save removed
the other's work, so the page is merged: neither feature set is dropped."* The restored set is named in that message:
*create-library, import-packs, export-angle-keypoints, download-pack*.

So the three sections the operator asked to remove were **re-instated by that merge**, which deliberately kept both
feature sets rather than respecting the earlier removal. Nothing in this work added them.

### 2. The runtime error was a STALE BINARY, not live code

The app log:

```
2026-09-28 23:16:30 [ERR] Unhandled exception in circuit '...'.
System.InvalidOperationException: Object of type 'DreamGenClone.Components.Shared.ImageStepComposer'
does not have a property matching the name 'SeedBindings'.
```

Measured, which settles it:

| fact | value |
|---|---|
| running process | PID 20188, started **22:41:29** |
| `bin/Debug/net9.0/DreamGenClone.dll` | built **22:41:21** |
| `PoseLibraryPage.razor` last modified | **23:06:57** — 25 min AFTER the build |
| log errors | **23:16–23:17**, i.e. from that older binary |

And in the CURRENT source there is **no `SeedBindings` attribute anywhere** — only a private page property
`TestSeedBindings`. The page now passes `Bindings="_testBindings"`, and `ImageStepComposer` does declare
`[Parameter] Bindings` (line 338). So the defect was already corrected at 23:06; the running app simply never picked it
up.

**Why it looked like a button bug**: the composer only renders when `_testPreset is not null`, so the page loads fine and
the exception fires the first time *Test this pose* is clicked.

### 3. The merge had reverted the head-pose routing (found while reading)

`OpenInEditorAsync` still carried the OLD guard:

```csharp
if (observed.Face.Count > 0) { throw new InvalidOperationException("this is a head pose: ..."); }
```

which refuses **any** pose carrying a face channel — including full-body poses that also have a face. The
`PoseFaceProxy.IsHeadPose` routing (joint count, `_loaded = null; _loadedFrom = preset`) had been dropped by the same
merge, so a face pose could not be opened in the editor at all. This is a regression of the work in records 078/079.

## Plan

1. Remove the three sections and everything that only served them.
2. Restore the head-pose routing in `OpenInEditorAsync`.
3. Rebuild; the runtime error needs no code change.

## Resolution

| File | Change |
|---|---|
| `PoseLibraryPage.razor` | removed the create-library row, the export-projected-poses row and the download-pack row; removed `_newLibraryName`, `_newLibraryDescription`, `_exportFolder`, `_downloadUrl`, `_downloadName`, `_downloadDescription`, their three methods, and the now-unused `@inject IPosePackDownloader PackDownloader`; restored `PoseFaceProxy.IsHeadPose` routing in `OpenInEditorAsync` |

**Deliberately NOT removed** (not named by the operator) — each is flagged to them rather than guessed at:

- **"Import pose packs"** — FOLLOW-UP, same day: the operator then listed its helper text ("Re-running the import adds
  nothing and leaves edited presets alone…") for removal as well, so the WHOLE row is now gone, together with
  `ImportPackAsync` and the `ILogger<PoseLibraryPage>` injection of which it was the only user. The empty-library notice
  no longer points at a button that does not exist: it now states that the bundled pack imports automatically on
  search, and that a persistent empty result means that import is failing.
- The manual `ImportAsync()` is therefore no longer reachable from this page. The bundled pack still imports
  automatically (`EnsureBundledPackAsync` runs on every search), and the composer's own `PoseLibraryPicker` still
  offers an import.
- **`PoseLibraryPicker.razor`** still carries *New library name*, *Download a pack (zip URL)* and an import button. It is
  a different surface — the pose picker inside `ImageStepComposer`, used by `CompositionComposer` and every composer
  host — so removing from it changes the composer everywhere and was not assumed.

The service APIs (`LibraryService.CreateLibraryAsync`, `ExportProjectedPosesAsync`, `IPosePackDownloader`) were left
intact: only the UI was asked to go.

**Verification**: build 0 errors. `Pose|ImageStepComposer|LoraCell` suites: **375 passed / 1 failed**, and the one
failure is **not this file** — `ImageStepComposerUsageContractTests.EveryHostPassesStringParametersAsExpressions` flags
`PromptAssetCreator.razor: SelectedModelId="_"; Prompt="_"` (a concurrent writer's in-flight file, untouched here). The
two `PoseTestCharacterConditioningContractTests` failures from records 078/079 are now **passing**, because the merge
brought in `ListPackOwnersAsync` and `IdentityService.ReadAssetBytesAsync`.

**Not verified at runtime**: the running app predates every one of these edits, so a rebuild + restart is required
before any of it is observable. The restart is the operator's to perform (it must come up
`ASPNETCORE_ENVIRONMENT=Development` from `DreamGenClone.Web`, or it reads the wrong database).

## Validated

- [ ] pending — awaiting the operator restarting the app and confirming the three sections are gone, no composer error,
       and a face pose opens in the editor.
