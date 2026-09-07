# B-111 Cancelled Composition Remains Pending

## Report

On 2026-09-06, Production Studio displayed `Composition pending...` for session `4f2eec18-b190-4beb-ad35-8d520ae5c800`, interaction `dad139a5-8e98-469b-ab74-8e55404f1732`, and Moment enrichment `e2a8838a-bd13-4f7a-bf2c-df6cd728ee78`, although no image job was running.

## Analysis

The user explicitly created composition image `c95c8082-96a0-4e38-96bd-40ee083d5622` at `2026-09-06T17:57:59Z`. Its durable `scene-image-rendering` job `8917e9c0686b4afd9791c41febf0a780` was subsequently cancelled at `2026-09-06T18:07:05Z` before it started.

`RunTray.CancelAsync` transitions only the durable job. `SceneImageRecord` has no cancellation transition and stays `Pending`. The workbench derives the canvas spinner and the Composition `Running` stage state directly from a pending/generating image record, so it reports work that no longer exists. This is not an automatic composition enqueue.

## Plan

1. Add a status-guarded Scene Image service/repository transition that changes the exact pending image linked to a successfully cancelled durable job to `Failed`, records a clear cancellation reason, and sets its completion/update timestamps.
2. Have the shared `RunTray` invoke that transition after a successful cancellation of an image render/edit job. Prompt-only jobs remain queue-only because they do not create an image record.
3. Add focused tests for the image transition and the tray's cancellation propagation, then build the affected Web project.

## Resolution

Implemented a status-guarded `Pending`/`Generating` to `Cancelled` transition for scene image attempts. The Production Studio Jobs tray invokes it after a durable image job is cancelled, and render/edit handlers treat `Cancelled` as terminal when work starts. Completion and failure persistence now use guarded SQL transitions, so a late provider response cannot overwrite a concurrent cancellation. The Studio canvas now presents a distinct cancellation state instead of a pending/running spinner.

The existing stale composition attempt is repaired with a guarded live-development-database update that matches its exact image and production group identifiers.

Verification: `dotnet build DreamGenClone.Web\DreamGenClone.csproj --no-restore --nologo -c Release` completed successfully with zero errors. Automated tests were not run because test execution is currently disabled.

## Validated

- [x] Database trace identified the exact image and durable job and confirmed the queue job is `Cancelled` while the image is still `Pending`.
- [x] The canvas state condition was traced to the stale image record.
- [ ] Pending implementation and build/test validation.